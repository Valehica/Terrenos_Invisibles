using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Niveles de detalle de los arboles de Sur (los packs no traen ninguno). Lo usa ArbolesRocasSur.
//
//   LOD0  el modelo del pack, hasta ~DistanciaLod0 metros.
//   LOD1  hojas: queda una fraccion de las tarjetas/ramitas, agrandadas para que la copa no quede rala.
//         corteza: la version decimada en Blender (Tools/ArbolesSur/<prefab>_corteza_lod1.json, ver
//         Tools/Blender/decimar_corteza.py); si todavia no existe, la corteza original.
//         Se apaga a DistanciaCorte (para un arbol de escala tipica): de ahi en adelante, el billboard.
//         OJO: el Terrain NO corta por treeDistance a los arboles con LODGroup (medido: con el LOD1 sin corte
//         dibujaba los 102 000 arboles, 230 M triangulos). El corte lo hace el LODGroup.
//   lejos foto del arbol de frente y de costado (billboard), en un atlas comun; la dibuja BosqueLejano.
//
// La corteza para Blender se exporta sola a Tools/ArbolesSur/<prefab>_corteza.obj, en las coordenadas de
// Unity tal cual (Blender la importa sin conversion de ejes y la devuelve igual).
public static class ArbolesLodSur
{
    public const string Carpeta = "Assets/Models/Sur/Vegetacion";
    public const string CarpetaBlender = "Tools/ArbolesSur";
    const string RutaAtlas = Carpeta + "/Billboards_Sur.png";
    const string RutaMatBillboard = Carpeta + "/Billboards_Sur.mat";
    const string RutaDatos = Carpeta + "/Billboards_Sur.asset";
    const int Celda = 512;                 // pixeles por vista en el atlas
    const float DistanciaLod0 = 25f;
    public const float DistanciaCorte = 60f;   // medido: con 90 m, 3.76 M tris y 2100 lotes desde el inicio del jugador
    public const float FovJuego = 60f;     // fov de la camara del juego: los LOD se calculan con el

    public class Especie
    {
        public string nombre;
        public Mesh mesh;
        public Material[] mats;
        public int subCorteza;
        public float fraccionHojas;
        public float escalaTipica = 1f;
        public GameObject prefab;          // lo llena PrefabArbol
    }

    // ---------------------------------------------------------------- prefab con LODs

    public static GameObject PrefabArbol(Especie e)
    {
        ExportarCorteza(e);
        var lod1 = GenerarLod1(e);

        var go = new GameObject(e.nombre);
        var r0 = Hijo(go, "LOD0", e.mesh, e.mats);
        var r1 = Hijo(go, "LOD1", lod1, e.mats);
        r1.shadowCastingMode = ShadowCastingMode.Off;   // empieza casi donde terminan las sombras (40 m)

        // Collider del tronco: vertices de la corteza en el primer metro y medio.
        var v = e.mesh.vertices; var t = e.mesh.GetTriangles(e.subCorteza);
        float yMin = float.MaxValue; foreach (int i in t) yMin = Mathf.Min(yMin, v[i].y);
        Vector3 suma = Vector3.zero; int n = 0; float rMax = 0f;
        foreach (int i in t) if (v[i].y < yMin + 1.5f) { suma += v[i]; n++; }
        Vector3 c = n > 0 ? suma / n : Vector3.zero;
        foreach (int i in t) if (v[i].y < yMin + 1.5f) rMax = Mathf.Max(rMax, new Vector2(v[i].x - c.x, v[i].z - c.z).magnitude);
        var cap = go.AddComponent<CapsuleCollider>();
        cap.radius = Mathf.Clamp(rMax * 0.7f, 0.15f, 1.2f);
        cap.height = 4f;
        cap.center = new Vector3(c.x, yMin + 2f, c.z);

        // LOD0 -> LOD1 a DistanciaLod0 y LOD1 -> nada a DistanciaCorte, para un arbol de escala tipica.
        // Alto relativo = lodBias * tamano / (2 d tan(fov/2)). BosqueLejano usa la misma cuenta, arbol por
        // arbol, para que el billboard aparezca justo donde se apaga el LOD1.
        float tam = Mathf.Max(e.mesh.bounds.size.x, e.mesh.bounds.size.y, e.mesh.bounds.size.z) * e.escalaTipica;
        float k = QualitySettings.lodBias * tam / (2f * Mathf.Tan(FovJuego * 0.5f * Mathf.Deg2Rad));
        float corte0 = Mathf.Clamp(k / DistanciaLod0, 0.02f, 0.95f);
        float corte1 = Mathf.Clamp(k / DistanciaCorte, 0.001f, corte0 * 0.9f);
        var lod = go.AddComponent<LODGroup>();
        lod.fadeMode = LODFadeMode.CrossFade;
        lod.animateCrossFading = true;
        lod.SetLODs(new[] { new LOD(corte0, new Renderer[] { r0 }), new LOD(corte1, new Renderer[] { r1 }) });
        lod.RecalculateBounds();

        e.prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Carpeta}/{e.nombre}.prefab");
        Object.DestroyImmediate(go);
        return e.prefab;
    }

    static Renderer Hijo(GameObject padre, string nombre, Mesh mesh, Material[] mats)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterials = mats;
        r.shadowCastingMode = ShadowCastingMode.On;
        return r;
    }

    // ---------------------------------------------------------------- LOD1

    static Mesh GenerarLod1(Especie e)
    {
        var m = e.mesh;
        var v = m.vertices; var nrm = m.normals; var uv = m.uv;
        var V = new List<Vector3>(); var N = new List<Vector3>(); var UV = new List<Vector2>();
        var subs = new List<int>[m.subMeshCount];

        for (int s = 0; s < m.subMeshCount; s++)
        {
            subs[s] = new List<int>();
            var t = m.GetTriangles(s);
            if (s == e.subCorteza)
            {
                if (!CortezaDecimada(e.nombre, V, N, UV, subs[s]))
                    Copiar(t, v, nrm, uv, V, N, UV, subs[s]);
                continue;
            }

            // Hojas: componentes conexas (soldando por posicion), se queda una fraccion al azar, agrandada.
            var comp = Componentes(v, t);
            float k = Mathf.Min(1.8f, 0.9f / Mathf.Sqrt(e.fraccionHojas));
            var mapa = new Dictionary<int, int>();
            foreach (var tris in comp)
            {
                if (Azar(tris[0], s) >= e.fraccionHojas) continue;
                Vector3 centro = Vector3.zero; int cuenta = 0;
                foreach (int ti in tris) for (int j = 0; j < 3; j++) { centro += v[t[ti * 3 + j]]; cuenta++; }
                centro /= cuenta;
                mapa.Clear();
                foreach (int ti in tris)
                    for (int j = 0; j < 3; j++)
                    {
                        int vi = t[ti * 3 + j];
                        if (!mapa.TryGetValue(vi, out int nuevo))
                        {
                            nuevo = V.Count; mapa[vi] = nuevo;
                            V.Add(centro + (v[vi] - centro) * k); N.Add(nrm[vi]); UV.Add(uv[vi]);
                        }
                        subs[s].Add(nuevo);
                    }
            }
        }

        var lod = new Mesh { name = e.nombre + "_LOD1", indexFormat = V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        lod.SetVertices(V); lod.SetNormals(N); lod.SetUVs(0, UV);
        lod.subMeshCount = subs.Length;
        for (int s = 0; s < subs.Length; s++) lod.SetTriangles(subs[s], s);
        lod.RecalculateBounds();
        lod.RecalculateTangents();

        string ruta = $"{Carpeta}/{e.nombre}_LOD1.asset";
        var viejo = AssetDatabase.LoadAssetAtPath<Mesh>(ruta);
        if (viejo != null) { EditorUtility.CopySerialized(lod, viejo); Object.DestroyImmediate(lod); return viejo; }
        AssetDatabase.CreateAsset(lod, ruta);
        return lod;
    }

    static void Copiar(int[] t, Vector3[] v, Vector3[] n, Vector2[] uv, List<Vector3> V, List<Vector3> N, List<Vector2> UV, List<int> dst)
    {
        var mapa = new Dictionary<int, int>();
        foreach (int vi in t)
        {
            if (!mapa.TryGetValue(vi, out int nuevo)) { nuevo = V.Count; mapa[vi] = nuevo; V.Add(v[vi]); N.Add(n[vi]); UV.Add(uv[vi]); }
            dst.Add(nuevo);
        }
    }

    // Triangulos agrupados por pieza conexa. Los vertices partidos por costuras se unen por posicion.
    static List<List<int>> Componentes(Vector3[] v, int[] t)
    {
        var padre = new int[v.Length];
        for (int i = 0; i < v.Length; i++) padre[i] = i;
        int Raiz(int x) { while (padre[x] != x) { padre[x] = padre[padre[x]]; x = padre[x]; } return x; }
        void Unir(int a, int b) { a = Raiz(a); b = Raiz(b); if (a != b) padre[a] = b; }
        var soldar = new Dictionary<Vector3, int>();
        foreach (int vi in t) { if (soldar.TryGetValue(v[vi], out int w)) Unir(vi, w); else soldar[v[vi]] = vi; }
        for (int i = 0; i < t.Length; i += 3) { Unir(t[i], t[i + 1]); Unir(t[i + 1], t[i + 2]); }
        var grupos = new Dictionary<int, List<int>>();
        for (int i = 0; i < t.Length / 3; i++)
        {
            int r = Raiz(t[i * 3]);
            if (!grupos.TryGetValue(r, out var l)) grupos[r] = l = new List<int>();
            l.Add(i);
        }
        return new List<List<int>>(grupos.Values);
    }

    static float Azar(int a, int b)
    {
        unchecked
        {
            uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    // ---------------------------------------------------------------- corteza ida y vuelta a Blender

    static void ExportarCorteza(Especie e)
    {
        Directory.CreateDirectory(CarpetaBlender);
        string ruta = $"{CarpetaBlender}/{e.nombre}_corteza.obj";
        var v = e.mesh.vertices; var uv = e.mesh.uv; var t = e.mesh.GetTriangles(e.subCorteza);
        var mapa = new Dictionary<int, int>();
        var sb = new System.Text.StringBuilder();
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        sb.Append("# Corteza de ").Append(e.nombre).Append(" en coordenadas de Unity (ArbolesLodSur.cs)\n");
        var orden = new List<int>();
        foreach (int vi in t) if (!mapa.ContainsKey(vi)) { mapa[vi] = orden.Count + 1; orden.Add(vi); }
        foreach (int vi in orden) sb.Append(string.Format(ci, "v {0} {1} {2}\n", v[vi].x, v[vi].y, v[vi].z));
        foreach (int vi in orden) sb.Append(string.Format(ci, "vt {0} {1}\n", uv[vi].x, uv[vi].y));
        for (int i = 0; i < t.Length; i += 3)
        {
            int a = mapa[t[i]], b = mapa[t[i + 1]], c = mapa[t[i + 2]];
            sb.Append($"f {a}/{a} {b}/{b} {c}/{c}\n");
        }
        File.WriteAllText(ruta, sb.ToString());
    }

    [System.Serializable] class CortezaJson { public float[] v; public float[] uv; public int[] t; }

    // Lee la corteza decimada. Blender la devuelve con un vertice por esquina: se suelda por posicion para las
    // normales (suaves), y por posicion+UV para los vertices.
    static bool CortezaDecimada(string nombre, List<Vector3> V, List<Vector3> N, List<Vector2> UV, List<int> dst)
    {
        string ruta = $"{CarpetaBlender}/{nombre}_corteza_lod1.json";
        if (!File.Exists(ruta)) return false;
        var j = JsonUtility.FromJson<CortezaJson>(File.ReadAllText(ruta));
        if (j?.v == null || j.t == null) return false;

        int nv = j.v.Length / 3;
        var pos = new Vector3[nv];
        for (int i = 0; i < nv; i++) pos[i] = new Vector3(j.v[3 * i], j.v[3 * i + 1], j.v[3 * i + 2]);
        var normalPorPos = new Dictionary<Vector3, Vector3>();
        for (int i = 0; i < j.t.Length; i += 3)
        {
            Vector3 a = pos[j.t[i]], b = pos[j.t[i + 1]], c = pos[j.t[i + 2]];
            Vector3 fn = Vector3.Cross(b - a, c - a);          // con el orden de Unity apunta hacia afuera
            foreach (var p in new[] { a, b, c })
                normalPorPos[p] = (normalPorPos.TryGetValue(p, out var acc) ? acc : Vector3.zero) + fn;
        }
        var soldar = new Dictionary<(Vector3, Vector2), int>();
        foreach (int i in j.t)
        {
            var clave = (pos[i], new Vector2(j.uv[2 * i], j.uv[2 * i + 1]));
            if (!soldar.TryGetValue(clave, out int nuevo))
            {
                nuevo = V.Count; soldar[clave] = nuevo;
                V.Add(clave.Item1); N.Add(normalPorPos[clave.Item1].normalized); UV.Add(clave.Item2);
            }
            dst.Add(nuevo);
        }
        return true;
    }

    // ---------------------------------------------------------------- billboards

    // Fotografia cada especie de frente y de costado, sin luz, y arma el atlas, el material y los datos que
    // usa BosqueLejano. Durante la foto se apagan los efectos del renderer (SSAO, rayos, pase de estilo) y la
    // niebla, y se restauran al terminar.
    public static BosqueBillboards HornearBillboards(List<Especie> especies)
    {
        int columnas = 4, filas = Mathf.CeilToInt(especies.Count * 2 / (float)columnas);
        var atlas = new Texture2D(columnas * Celda, filas * Celda, TextureFormat.RGBA32, false, false);
        atlas.SetPixels32(new Color32[atlas.width * atlas.height]);
        var datos = AssetDatabase.LoadAssetAtPath<BosqueBillboards>(RutaDatos);
        if (datos == null) { datos = ScriptableObject.CreateInstance<BosqueBillboards>(); AssetDatabase.CreateAsset(datos, RutaDatos); }
        var entradas = new List<BosqueBillboards.Entrada>();

        const int capa = 31;
        var rt = new RenderTexture(Celda, Celda, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
        var camGo = new GameObject("_CamaraBillboard") { hideFlags = HideFlags.HideAndDontSave };
        var cam = camGo.AddComponent<Camera>();
        cam.enabled = false;
        cam.orthographic = true;
        cam.cullingMask = 1 << capa;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0);
        cam.allowHDR = false; cam.allowMSAA = false;
        cam.targetTexture = rt;
        var extra = cam.GetUniversalAdditionalCameraData();
        extra.renderPostProcessing = false;
        extra.renderShadows = false;
        extra.antialiasing = AntialiasingMode.None;

        var features = new List<(ScriptableRendererFeature f, bool activo)>();
        foreach (var f in RendererFeatures()) { features.Add((f, f.isActive)); f.SetActive(false); }
        bool niebla = RenderSettings.fog;
        RenderSettings.fog = false;
        var lectura = new Texture2D(Celda, Celda, TextureFormat.RGBA32, false, false);

        try
        {
            for (int k = 0; k < especies.Count; k++)
            {
                var e = especies[k];
                var go = new GameObject("_ArbolBillboard") { hideFlags = HideFlags.HideAndDontSave, layer = capa };
                go.AddComponent<MeshFilter>().sharedMesh = e.mesh;
                var unlit = new Material[e.mats.Length];
                for (int i = 0; i < unlit.Length; i++) unlit[i] = SinLuz(e.mats[i]);
                go.AddComponent<MeshRenderer>().sharedMaterials = unlit;

                Bounds b = e.mesh.bounds;
                float ladoF = Mathf.Max(b.size.x, b.size.y) * 1.04f, ladoC = Mathf.Max(b.size.z, b.size.y) * 1.04f;
                var entrada = new BosqueBillboards.Entrada { prefab = e.prefab, centro = b.center, ladoFrente = ladoF, ladoCostado = ladoC };
                for (int vista = 0; vista < 2; vista++)
                {
                    float lado = vista == 0 ? ladoF : ladoC;
                    Vector3 dir = vista == 0 ? Vector3.forward : Vector3.right;
                    float lejos = b.extents.magnitude + 5f;
                    cam.transform.SetPositionAndRotation(b.center - dir * lejos, Quaternion.LookRotation(dir, Vector3.up));
                    cam.orthographicSize = lado * 0.5f;
                    cam.nearClipPlane = 0.01f; cam.farClipPlane = lejos * 2f;
                    Render(cam, rt);
                    RenderTexture.active = rt;
                    lectura.ReadPixels(new Rect(0, 0, Celda, Celda), 0, 0);
                    RenderTexture.active = null;

                    int celda = k * 2 + vista, cx = celda % columnas, cy = filas - 1 - celda / columnas;
                    atlas.SetPixels32(cx * Celda, cy * Celda, Celda, Celda, lectura.GetPixels32());
                    // Medio pixel hacia adentro para que el filtrado no traiga la celda vecina.
                    var uvRect = new Rect((cx * Celda + 0.5f) / atlas.width, (cy * Celda + 0.5f) / atlas.height,
                                          (Celda - 1f) / atlas.width, (Celda - 1f) / atlas.height);
                    if (vista == 0) entrada.uvFrente = uvRect; else entrada.uvCostado = uvRect;
                }
                entradas.Add(entrada);
                Object.DestroyImmediate(go);
                foreach (var m in unlit) Object.DestroyImmediate(m);
            }
        }
        finally
        {
            foreach (var (f, activo) in features) f.SetActive(activo);
            RenderSettings.fog = niebla;
            cam.targetTexture = null;
            Object.DestroyImmediate(camGo);
            rt.Release(); Object.DestroyImmediate(rt);
            Object.DestroyImmediate(lectura);
        }

        Dilatar(atlas, 12);
        File.WriteAllBytes(RutaAtlas, atlas.EncodeToPNG());
        Object.DestroyImmediate(atlas);
        AssetDatabase.ImportAsset(RutaAtlas);
        var ti = (TextureImporter)AssetImporter.GetAtPath(RutaAtlas);
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = true;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = true;
        ti.mipMapsPreserveCoverage = true;          // sin esto el alfa se lava en los mips y el bosque lejano se pela
        ti.alphaTestReferenceValue = 0.5f;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.maxTextureSize = 2048;
        ti.SaveAndReimport();

        var mat = AssetDatabase.LoadAssetAtPath<Material>(RutaMatBillboard);
        if (mat == null) { mat = new Material(Shader.Find("Terrenos/ArbolBillboard")); AssetDatabase.CreateAsset(mat, RutaMatBillboard); }
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(RutaAtlas));
        EditorUtility.SetDirty(mat);

        datos.material = mat;
        datos.entradas = entradas.ToArray();
        EditorUtility.SetDirty(datos);
        AssetDatabase.SaveAssets();
        return datos;
    }

    static void Render(Camera cam, RenderTexture rt)
    {
        var pedido = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, pedido)) RenderPipeline.SubmitRenderRequest(cam, pedido);
        else cam.Render();
    }

    // Copia URP/Unlit del material: misma textura, tinte y recorte, sin luz.
    static Material SinLuz(Material lit)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetTexture("_BaseMap", lit.GetTexture("_BaseMap"));
        m.SetColor("_BaseColor", lit.GetColor("_BaseColor"));
        m.SetFloat("_AlphaClip", lit.GetFloat("_AlphaClip"));
        m.SetFloat("_Cutoff", lit.GetFloat("_Cutoff"));
        m.SetFloat("_Cull", (float)CullMode.Off);
        BaseShaderGUI.SetMaterialKeywords(m);
        return m;
    }

    static IEnumerable<ScriptableRendererFeature> RendererFeatures()
    {
        var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp == null) yield break;
        var campo = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (!(campo?.GetValue(urp) is ScriptableRendererData[] lista)) yield break;
        foreach (var rd in lista)
            if (rd != null)
                foreach (var f in rd.rendererFeatures)
                    if (f != null) yield return f;
    }

    // Rellena el color de los pixeles transparentes con el de sus vecinos opacos: si no, el filtrado y los mips
    // mezclan negro en el borde de las hojas y el bosque lejano sale con aura oscura.
    static void Dilatar(Texture2D tex, int pasadas)
    {
        int w = tex.width, h = tex.height;
        var px = tex.GetPixels32();
        var lleno = new bool[px.Length];
        for (int i = 0; i < px.Length; i++) lleno[i] = px[i].a > 8;
        var nuevos = new List<(int, Color32)>();
        for (int p = 0; p < pasadas; p++)
        {
            nuevos.Clear();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (lleno[i]) continue;
                    int r = 0, g = 0, b = 0, n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                            int j = yy * w + xx;
                            if (!lleno[j]) continue;
                            r += px[j].r; g += px[j].g; b += px[j].b; n++;
                        }
                    if (n > 0) nuevos.Add((i, new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 0)));
                }
            foreach (var (i, c) in nuevos) { px[i] = c; lleno[i] = true; }
        }
        tex.SetPixels32(px);
    }
}
