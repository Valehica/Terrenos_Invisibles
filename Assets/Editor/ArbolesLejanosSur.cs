using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Version lejana de los coigues y cipreses de Sur (y de los arrayanes y olivillos de Centro, ver
// ArbolesArrayanOlivillo), para el bosque que el jugador ve pero no alcanza:
// Arbol_<nombre>_Lejos.prefab = dos cuadros cruzados (frente y costado) con la foto del arbol de verdad,
// sin collider ni Scannable. Se pintan en el Terrain como cualquier arbol (los agrega el menu
// Terrenos > Sur > Arboles coigue y cipres al Terrain, que llama a Crear).
//
// Las fotos se sacan con camara ortografica y materiales Unlit (sin luz: la pone el shader
// Terrenos/ArbolBillboard en el juego). Durante la foto se apagan los efectos del renderer (SSAO, rayos,
// pase de estilo) y la niebla, y se restauran al terminar. Todas van a un atlas comun, asi los lejanos
// comparten un solo material (uno por carpeta: Sur y Centro tienen cada una su atlas).
//
// Llevan LODGroup aunque tengan un solo nivel: sin el, el Terrain cambia el arbol por su propio billboard
// pasados treeBillboardDistance, y en URP sale azul.
public static class ArbolesLejanosSur
{
    const string CarpetaSur = "Assets/Models/Sur/Vegetacion";
    const int Celda = 512;                 // pixeles por vista
    const int Columnas = 4;
    const float CorteLod = 0.002f;         // con lodBias 2, un arbol de 25 m se apaga a ~20 km: siempre visible

    public static List<GameObject> Crear(List<(string nombre, GameObject prefab)> arboles) => Crear(arboles, CarpetaSur, 1f);

    // Atlas, material, mallas y prefabs quedan en la carpeta (el material en <carpeta>/Materials).
    // tonoCorteza multiplica el color de los materiales sin recorte (la corteza) en la foto: el shader de los
    // lejanos ilumina con la normal hacia arriba, y un tronco bajo la copa recibe mucha menos luz que eso.
    public static List<GameObject> Crear(List<(string nombre, GameObject prefab)> arboles, string carpeta, float tonoCorteza)
    {
        string rutaAtlas = carpeta + "/Arboles_Lejos_Atlas.png";
        int filas = Mathf.CeilToInt(arboles.Count * 2 / (float)Columnas);
        var atlas = new Texture2D(Columnas * Celda, filas * Celda, TextureFormat.RGBA32, false, false);
        atlas.SetPixels32(new Color32[atlas.width * atlas.height]);
        var mat = Material(carpeta + "/Materials/M_Arboles_Lejos.mat");
        var mallas = new List<(string nombre, Mesh mesh)>();

        const int capa = 31;
        var rt = new RenderTexture(Celda, Celda, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
        var camGo = new GameObject("_CamaraArbolLejano") { hideFlags = HideFlags.HideAndDontSave };
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
            for (int k = 0; k < arboles.Count; k++)
            {
                var go = (GameObject)Object.Instantiate(arboles[k].prefab);
                go.hideFlags = HideFlags.HideAndDontSave;
                var temporales = new List<Material>();
                Bounds b = default; bool primero = true;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    r.gameObject.layer = capa;
                    var unlit = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < unlit.Length; i++) { unlit[i] = SinLuz(r.sharedMaterials[i], tonoCorteza); temporales.Add(unlit[i]); }
                    r.sharedMaterials = unlit;
                    if (primero) { b = r.bounds; primero = false; } else b.Encapsulate(r.bounds);
                }

                var rects = new Rect[2];
                var lados = new float[2];
                for (int vista = 0; vista < 2; vista++)
                {
                    float ancho = vista == 0 ? b.size.x : b.size.z;
                    float lado = Mathf.Max(ancho, b.size.y) * 1.03f;
                    Vector3 dir = vista == 0 ? Vector3.forward : Vector3.right;
                    float lejos = b.extents.magnitude + 5f;
                    cam.transform.SetPositionAndRotation(b.center - dir * lejos, Quaternion.LookRotation(dir, Vector3.up));
                    cam.orthographicSize = lado * 0.5f;
                    cam.nearClipPlane = 0.01f; cam.farClipPlane = lejos * 2f;
                    Render(cam, rt);
                    RenderTexture.active = rt;
                    lectura.ReadPixels(new Rect(0, 0, Celda, Celda), 0, 0);
                    RenderTexture.active = null;

                    int celda = k * 2 + vista, cx = celda % Columnas, cy = filas - 1 - celda / Columnas;
                    atlas.SetPixels32(cx * Celda, cy * Celda, Celda, Celda, lectura.GetPixels32());
                    // Medio pixel hacia adentro para que el filtrado no traiga la celda vecina.
                    rects[vista] = new Rect((cx * Celda + 0.5f) / atlas.width, (cy * Celda + 0.5f) / atlas.height,
                                            (Celda - 1f) / atlas.width, (Celda - 1f) / atlas.height);
                    lados[vista] = lado;
                }
                mallas.Add((arboles[k].nombre, Malla(carpeta, arboles[k].nombre, b.center, lados, rects)));

                Object.DestroyImmediate(go);
                foreach (var m in temporales) Object.DestroyImmediate(m);
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
        File.WriteAllBytes(rutaAtlas, atlas.EncodeToPNG());
        Object.DestroyImmediate(atlas);
        AssetDatabase.ImportAsset(rutaAtlas);
        var ti = (TextureImporter)AssetImporter.GetAtPath(rutaAtlas);
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = true;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = true;
        ti.mipMapsPreserveCoverage = true;          // sin esto el alfa se lava en los mips y el bosque lejano se pela
        ti.alphaTestReferenceValue = 0.5f;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.maxTextureSize = 2048;
        ti.SaveAndReimport();
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(rutaAtlas));
        EditorUtility.SetDirty(mat);

        var prefabs = new List<GameObject>();
        foreach (var (nombre, mesh) in mallas) prefabs.Add(Prefab(carpeta, nombre, mesh, mat));
        AssetDatabase.SaveAssets();
        return prefabs;
    }

    // Dos cuadros cruzados en el centro de la caja del arbol. Normal hacia arriba en todos los vertices
    // (ver el shader). Vista 0 = camara mirando +Z (derecha = +X); vista 1 = mirando +X (derecha = -Z).
    static Mesh Malla(string carpeta, string nombre, Vector3 c, float[] lados, Rect[] rects)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        for (int vista = 0; vista < 2; vista++)
        {
            float h = lados[vista] * 0.5f;
            Vector3 derecha = vista == 0 ? Vector3.right : Vector3.back;
            int b0 = v.Count;
            v.Add(c - derecha * h + Vector3.down * h);
            v.Add(c + derecha * h + Vector3.down * h);
            v.Add(c + derecha * h + Vector3.up * h);
            v.Add(c - derecha * h + Vector3.up * h);
            Rect r = rects[vista];
            uv.Add(new Vector2(r.xMin, r.yMin)); uv.Add(new Vector2(r.xMax, r.yMin));
            uv.Add(new Vector2(r.xMax, r.yMax)); uv.Add(new Vector2(r.xMin, r.yMax));
            tri.AddRange(new[] { b0, b0 + 2, b0 + 1, b0, b0 + 3, b0 + 2 });
        }

        string ruta = $"{carpeta}/Arbol_{nombre}_Lejos_Malla.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(ruta);
        bool nuevo = mesh == null;
        if (nuevo) mesh = new Mesh();
        mesh.Clear();
        mesh.name = $"Arbol_{nombre}_Lejos";
        mesh.SetVertices(v);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(tri, 0);
        var normales = new Vector3[v.Count];
        for (int i = 0; i < normales.Length; i++) normales[i] = Vector3.up;
        mesh.normals = normales;
        mesh.RecalculateBounds();
        if (nuevo) AssetDatabase.CreateAsset(mesh, ruta); else EditorUtility.SetDirty(mesh);
        return mesh;
    }

    static GameObject Prefab(string carpeta, string nombre, Mesh mesh, Material mat)
    {
        var raiz = new GameObject($"Arbol_{nombre}_Lejos");
        var planos = new GameObject("Planos");
        planos.transform.SetParent(raiz.transform, false);
        planos.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = planos.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;   // lejos de la distancia de sombras igual
        r.receiveShadows = false;
        var lod = raiz.AddComponent<LODGroup>();
        lod.SetLODs(new[] { new LOD(CorteLod, new Renderer[] { r }) });
        lod.RecalculateBounds();
        var prefab = PrefabUtility.SaveAsPrefabAsset(raiz, $"{carpeta}/Arbol_{nombre}_Lejos.prefab");
        Object.DestroyImmediate(raiz);
        return prefab;
    }

    static Material Material(string ruta)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        if (m == null) { m = new Material(Shader.Find("Terrenos/ArbolBillboard")); AssetDatabase.CreateAsset(m, ruta); }
        m.enableInstancing = true;
        return m;
    }

    static void Render(Camera cam, RenderTexture rt)
    {
        var pedido = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, pedido)) RenderPipeline.SubmitRenderRequest(cam, pedido);
        else cam.Render();
    }

    // Copia URP/Unlit del material: misma textura, tinte y recorte, sin luz.
    static Material SinLuz(Material lit, float tonoCorteza)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetTexture("_BaseMap", lit.GetTexture("_BaseMap"));
        Color c = lit.GetColor("_BaseColor");
        if (lit.GetFloat("_AlphaClip") < 0.5f) c = new Color(c.r * tonoCorteza, c.g * tonoCorteza, c.b * tonoCorteza, c.a);
        m.SetColor("_BaseColor", c);
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
