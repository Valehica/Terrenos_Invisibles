using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Escena Norte (Isluga, Tarapaca): queñoa, pueblo, pintado del terreno y vegetacion.
//
// El terreno de Norte se esculpio a mano y NO se toca. Lo que hace esto:
//
//  1. Actualizar Queñoa: arma materiales, LODs del follaje y el prefab desde el FBX
//     que exporta el usuario desde Blender. Re-correrlo cada vez que se reexporte.
//  2. Reordenar casas: las pone en manzanas detras de la iglesia, como el pueblo real.
//  3. Pintar terreno: splatmap por reglas + mascaras sacadas del satelite.
//  4. Poblar: queñoas en las laderas (arboles del Terrain), ichu y tola (detalles).
//
// Las mascaras (Assets/Terrain/Norte/Isluga_Mascaras.png) salen de
// Tools/TerrenoNorte: imagen satelital de Isluga (Esri World Imagery, z17)
// girada 30 grados y escalada x0.4 con la iglesia como ancla, para que las calles
// reales queden alineadas con los ejes de la escena y el pueblo real (~400 m)
// entre en el de la escena (~160 m). R = bofedal, G = pajonal amarillo,
// B = densidad de tola, A = suelo rojizo de cerro.
public static class IslugaNorte
{
    // ---------------------------------------------------------------- rutas
    const string CarpetaQuenoa = "Assets/Models/Norte/Queños/";
    const string FbxQuenoa = CarpetaQuenoa + "Queñoa.fbx";
    const string PrefabQuenoa = "Assets/Prefab/Queñoa.prefab";
    const string RutaMascaras = "Assets/Terrain/Norte/Isluga_Mascaras.png";
    const string CarpetaCapas = "Assets/Terrain/Layers/";
    const string CarpetaDetalles = "Assets/Prefabs/Detalles/";

    // ------------------------------------------------------------ el pueblo
    // Iglesia: se queda donde esta (el NPC, el jugador y la postal dependen de ella).
    // El pueblo real queda al oeste y noroeste de la iglesia; el camino pasa por el
    // sur de la iglesia y sigue hacia el este, y el bofedal rodea por el este y el sur.
    static readonly float[] CallesNS = { -135f, -106f, -77f, -48f };   // x de las calles norte-sur
    static readonly float[] CallesEO = { -86f, -58f, -30f, -2f, 26f };  // z de las calles este-oeste
    const float MedioAnchoCalle = 3.5f;

    // Manzanas (centro x, centro z): 22 x 21 m entre calles.
    static readonly Vector2[] ManzanasCasas =
    {
        new Vector2(-120.5f, -44f), new Vector2(-91.5f, -44f), new Vector2(-62.5f, -44f),
        new Vector2(-120.5f, -16f), new Vector2(-91.5f, -16f), new Vector2(-62.5f, -16f),
        new Vector2(-91.5f, 12f),   new Vector2(-62.5f, 12f),
        new Vector2(-62.5f, -72f),
    };
    static readonly Vector2[] ManzanasCorrales =
    {
        new Vector2(-120.5f, 12f), new Vector2(-91.5f, -72f), new Vector2(-33f, -72f), new Vector2(-33f, 12f),
    };

    // Camino: entra por el oeste por la calle z = -2, baja por la calle x = -48 y pasa
    // por el sur de la iglesia hacia el este.
    static readonly Vector2[] Camino =
    {
        new Vector2(-252f, -6f), new Vector2(-160f, -3f), new Vector2(-135f, -2f), new Vector2(-48f, -2f),
        new Vector2(-48f, -58f), new Vector2(-10f, -60f), new Vector2(30f, -53f), new Vector2(90f, -45f),
        new Vector2(170f, -38f), new Vector2(252f, -33f),
    };

    // Atrio de la iglesia y explanada al este (donde aparece el jugador).
    static readonly Rect Atrio = Rect.MinMaxRect(-47f, -56f, -20f, -34f);
    static readonly Rect Explanada = Rect.MinMaxRect(-20f, -64f, 6f, -28f);
    // Zona del minijuego del chaku (corral y limites): sin arboles.
    static readonly Rect ZonaChaku = Rect.MinMaxRect(88f, -128f, 172f, -45f);

    // ---------------------------------------------------- 1. Queñoa
    [MenuItem("Terrenos/Norte/Actualizar Queñoa")]
    public static void ActualizarQuenoa()
    {
        ConfigurarTextura("Quenoa_BaseColor", TextureImporterType.Default, true);
        ConfigurarTextura("Quenoa_Normal", TextureImporterType.NormalMap, false);
        foreach (var n in new[] { "Quenoa_AO", "Quenoa_Roughness", "Quenoa_Cavidad", "Quenoa_N_alta" })
            ConfigurarTextura(n, TextureImporterType.Default, false);
        var metalSuave = CrearMetalSuave();

        var corteza = CargarOCrearMaterial(CarpetaQuenoa + "M_Quenoa_Corteza.mat", Shader.Find("Universal Render Pipeline/Lit"));
        corteza.SetTexture("_BaseMap", Tex("Quenoa_BaseColor"));
        corteza.SetColor("_BaseColor", Color.white);
        corteza.SetTexture("_BumpMap", Tex("Quenoa_Normal"));
        corteza.SetFloat("_BumpScale", 1f);
        corteza.EnableKeyword("_NORMALMAP");
        corteza.SetTexture("_OcclusionMap", Tex("Quenoa_AO"));
        corteza.SetFloat("_OcclusionStrength", 1f);
        corteza.EnableKeyword("_OCCLUSIONMAP");
        corteza.SetTexture("_MetallicGlossMap", metalSuave);
        corteza.SetFloat("_Smoothness", 1f);   // con mapa, URP multiplica el alfa por esto
        corteza.EnableKeyword("_METALLICSPECGLOSSMAP");
        corteza.enableInstancing = true;
        EditorUtility.SetDirty(corteza);

        var hojas = CargarOCrearMaterial(CarpetaQuenoa + "M_Quenoa_Hojas.mat", Shader.Find("Terrenos/HojasQuenoa"));
        hojas.enableInstancing = true;
        EditorUtility.SetDirty(hojas);
        AssetDatabase.SaveAssets();

        // Que el FBX suelto tambien se vea bien si se arrastra a una escena.
        var imp = (ModelImporter)AssetImporter.GetAtPath(FbxQuenoa);
        imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_Quenoa_Corteza"), corteza);
        imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_Quenoa_Hojas"), hojas);
        imp.SaveAndReimport();

        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxQuenoa);
        Mesh tronco = null;
        var manchas = new List<MeshFilter>();
        foreach (var mf in fbx.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.name.StartsWith("Icosphere")) manchas.Add(mf);
            else if (tronco == null) tronco = mf.sharedMesh;
        }
        if (tronco == null || manchas.Count == 0)
        {
            Debug.LogError("Queñoa.fbx no trae el tronco o las manchas de hojas (Icosphere*).");
            return;
        }
        AvisarColorDeHojas(manchas[0].sharedMesh);
        tronco = TroncoConUVMadera(tronco);

        // LOD1 a LOD3: menos hojas pero mas grandes, para que la copa conserve el
        // volumen. 1/3 de las hojas a x1.7, 1/10 a x3.2 y 1/30 a x5.5 cubren la misma
        // superficie. Con ~110 queñoas en el terreno la mayoria cae en LOD3 (GPU integrada).
        var hojas0 = CombinarHojas(manchas, 1f, "Quenoa_Hojas_LOD0");
        var hojas1 = CombinarHojas(manchas, 1f / 3f, "Quenoa_Hojas_LOD1");
        var hojas2 = CombinarHojas(manchas, 1f / 10f, "Quenoa_Hojas_LOD2");
        var hojas3 = CombinarHojas(manchas, 1f / 30f, "Quenoa_Hojas_LOD3");

        // Base del tronco: centro y radio de los vertices de los primeros 30 cm.
        Vector3 baseTronco; float radioTronco;
        BaseDelTronco(tronco, out baseTronco, out radioTronco);

        var raiz = PrefabUtility.LoadPrefabContents(PrefabQuenoa);
        try
        {
            for (int i = raiz.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(raiz.transform.GetChild(i).gameObject);
            var rTronco = Hijo(raiz, "Tronco", tronco, corteza);
            var r0 = Hijo(raiz, "Hojas_LOD0", hojas0, hojas);
            var r1 = Hijo(raiz, "Hojas_LOD1", hojas1, hojas);
            var r2 = Hijo(raiz, "Hojas_LOD2", hojas2, hojas);
            var r3 = Hijo(raiz, "Hojas_LOD3", hojas3, hojas);

            var lod = raiz.GetComponent<LODGroup>();
            if (lod == null) lod = raiz.AddComponent<LODGroup>();
            lod.fadeMode = LODFadeMode.None;
            lod.SetLODs(new[]
            {
                new LOD(0.30f, new Renderer[] { rTronco, r0 }),
                new LOD(0.10f, new Renderer[] { rTronco, r1 }),
                new LOD(0.04f, new Renderer[] { rTronco, r2 }),
                new LOD(0.008f, new Renderer[] { rTronco, r3 }),
            });
            lod.RecalculateBounds();

            var cap = raiz.GetComponent<CapsuleCollider>();
            if (cap == null) cap = raiz.AddComponent<CapsuleCollider>();
            cap.direction = 1;
            cap.radius = Mathf.Max(0.35f, radioTronco);
            cap.height = 4.5f;
            cap.center = new Vector3(baseTronco.x, baseTronco.y + 2.25f, baseTronco.z);

            PrefabUtility.SaveAsPrefabAsset(raiz, PrefabQuenoa);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(raiz);
        }
        Debug.Log($"Queñoa actualizada: hojas LOD0 {hojas0.vertexCount} v, LOD1 {hojas1.vertexCount} v, LOD2 {hojas2.vertexCount} v, LOD3 {hojas3.vertexCount} v; " +
                  $"tronco {tronco.vertexCount} v; base del tronco {baseTronco}, radio {radioTronco:F2} m.");
    }

    static void ConfigurarTextura(string nombre, TextureImporterType tipo, bool srgb)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(CarpetaQuenoa + nombre + ".png");
        if (ti == null) return;
        if (ti.textureType == tipo && ti.sRGBTexture == srgb) return;
        ti.textureType = tipo;
        ti.sRGBTexture = srgb;
        ti.SaveAndReimport();
    }

    static Texture2D Tex(string nombre) => AssetDatabase.LoadAssetAtPath<Texture2D>(CarpetaQuenoa + nombre + ".png");

    // URP/Lit lee la suavidad del alfa del mapa metalico. Sale de 1 - rugosidad.
    static Texture2D CrearMetalSuave()
    {
        string origen = CarpetaQuenoa + "Quenoa_Roughness.png";
        string destino = CarpetaQuenoa + "Quenoa_MetalSuave.png";
        var rug = new Texture2D(2, 2);
        rug.LoadImage(File.ReadAllBytes(origen));
        var px = rug.GetPixels32();
        for (int i = 0; i < px.Length; i++)
            px[i] = new Color32(0, 0, 0, (byte)(255 - px[i].r));
        var ms = new Texture2D(rug.width, rug.height, TextureFormat.RGBA32, false);
        ms.SetPixels32(px);
        File.WriteAllBytes(destino, ms.EncodeToPNG());
        Object.DestroyImmediate(rug);
        Object.DestroyImmediate(ms);
        AssetDatabase.ImportAsset(destino);
        var ti = (TextureImporter)AssetImporter.GetAtPath(destino);
        ti.sRGBTexture = false;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(destino);
    }

    static Material CargarOCrearMaterial(string ruta, Shader shader)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        if (m == null)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, ruta);
        }
        else if (m.shader != shader) m.shader = shader;
        return m;
    }

    static MeshRenderer Hijo(GameObject raiz, string nombre, Mesh malla, Material mat)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(raiz.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = malla;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        return mr;
    }

    // Las hojas traen dos atributos de color: Hoja_Datos (datos para el shader de
    // Blender, alfa variable) y Color_Unity (el color horneado, alfa 1). Unity solo
    // importa el primero que venga en el FBX: hay que exportar con
    // "Prioritize Active Color" y Color_Unity como color activo.
    static void AvisarColorDeHojas(Mesh m)
    {
        using (var datos = Mesh.AcquireReadOnlyMeshData(m))
        {
            var d = datos[0];
            if (!d.HasVertexAttribute(VertexAttribute.Color))
            {
                Debug.LogError("Las hojas de Queñoa.fbx no traen color de vertice. Exportar con Color_Unity activo.");
                return;
            }
            var c = new NativeArray<Color>(d.vertexCount, Allocator.Temp);
            d.GetColors(c);
            float alfa = 0f;
            for (int i = 0; i < c.Length; i++) alfa += c[i].a;
            c.Dispose();
            if (alfa / d.vertexCount < 0.99f)
                Debug.LogError("El color de vertice de las hojas parece ser Hoja_Datos, no Color_Unity. " +
                               "Re-exportar el FBX con Geometry > Prioritize Active Color y Color_Unity activo.");
        }
    }

    // El tronco trae dos UV: "UVMap" (la vieja) y "UV_Madera", donde se hornearon las
    // texturas. El FBX las exporta en ese orden y URP/Lit solo lee la primera, asi que
    // se arma una copia con UV_Madera en el canal 0 y las tangentes recalculadas sobre ella.
    static Mesh TroncoConUVMadera(Mesh fuente)
    {
        var m = Object.Instantiate(fuente);
        var uv0 = new List<Vector2>(); var uv1 = new List<Vector2>();
        m.GetUVs(0, uv0); m.GetUVs(1, uv1);
        // UV_Madera ocupa casi todo el cuadro; la vieja, una franja angosta.
        float Ancho(List<Vector2> uv) { float a = 1f, b = 0f; foreach (var p in uv) { a = Mathf.Min(a, p.x); b = Mathf.Max(b, p.x); } return b - a; }
        if (uv1.Count == m.vertexCount && Ancho(uv1) > Ancho(uv0))
        {
            m.SetUVs(0, uv1);
            m.SetUVs(1, uv0);
        }
        m.RecalculateTangents();
        m.name = "Quenoa_Tronco";

        string ruta = CarpetaQuenoa + "Quenoa_Tronco.asset";
        var existente = AssetDatabase.LoadAssetAtPath<Mesh>(ruta);
        if (existente == null) { AssetDatabase.CreateAsset(m, ruta); return m; }
        EditorUtility.CopySerialized(m, existente);
        Object.DestroyImmediate(m);
        EditorUtility.SetDirty(existente);
        AssetDatabase.SaveAssets();
        return existente;
    }

    static void BaseDelTronco(Mesh m, out Vector3 centro, out float radio)
    {
        using (var datos = Mesh.AcquireReadOnlyMeshData(m))
        {
            var d = datos[0];
            var pos = new NativeArray<Vector3>(d.vertexCount, Allocator.Temp);
            d.GetVertices(pos);
            float minY = float.MaxValue;
            for (int i = 0; i < pos.Length; i++) minY = Mathf.Min(minY, pos[i].y);
            Vector3 suma = Vector3.zero; int n = 0;
            for (int i = 0; i < pos.Length; i++)
                if (pos[i].y < minY + 0.3f) { suma += pos[i]; n++; }
            centro = suma / Mathf.Max(n, 1);
            radio = 0f;
            for (int i = 0; i < pos.Length; i++)
                if (pos[i].y < minY + 0.3f)
                    radio = Mathf.Max(radio, new Vector2(pos[i].x - centro.x, pos[i].z - centro.z).magnitude);
            centro.y = minY;
            pos.Dispose();
        }
    }

    // Junta las manchas de follaje en una sola malla (una llamada de dibujo en vez
    // de siete). Cada hoja llega partida en caras sueltas, asi que se reagrupa
    // soldando vertices por posicion. Las normales se mezclan con la direccion
    // desde el centro de su mancha (ver HojasQuenoa.shader).
    static Mesh CombinarHojas(List<MeshFilter> manchas, float fraccion, string nombre)
    {
        const float MezclaVolumen = 0.65f;
        float escalaHoja = 1f / Mathf.Sqrt(fraccion);
        var pos = new List<Vector3>();
        var nor = new List<Vector3>();
        var col = new List<Color32>();
        var tri = new List<int>();
        int hojaGlobal = 0;

        foreach (var mf in manchas)
        {
            var m = mf.sharedMesh;
            var aRaiz = mf.transform.localToWorldMatrix;   // la raiz del FBX esta en el origen
            Vector3 centroMancha = mf.transform.position;
            using (var datos = Mesh.AcquireReadOnlyMeshData(m))
            {
                var d = datos[0];
                int vc = d.vertexCount;
                var p = new NativeArray<Vector3>(vc, Allocator.Temp);
                var nn = new NativeArray<Vector3>(vc, Allocator.Temp);
                var cc = new NativeArray<Color>(vc, Allocator.Temp);
                var ix = new NativeArray<int>(d.GetSubMesh(0).indexCount, Allocator.Temp);
                d.GetVertices(p); d.GetNormals(nn); d.GetColors(cc); d.GetIndices(ix, 0);

                // Union-find: vertices en la misma posicion y vertices del mismo triangulo.
                var padre = new int[vc];
                for (int i = 0; i < vc; i++) padre[i] = i;
                int Raiz(int x) { while (padre[x] != x) { padre[x] = padre[padre[x]]; x = padre[x]; } return x; }
                void Unir(int a, int b) { a = Raiz(a); b = Raiz(b); if (a != b) padre[b] = a; }
                var soldado = new Dictionary<Vector3Int, int>();
                for (int i = 0; i < vc; i++)
                {
                    var k = Vector3Int.RoundToInt(p[i] * 20000f);
                    if (soldado.TryGetValue(k, out int j)) Unir(i, j); else soldado[k] = i;
                }
                for (int t = 0; t < ix.Length; t += 3) { Unir(ix[t], ix[t + 1]); Unir(ix[t], ix[t + 2]); }

                var idHoja = new Dictionary<int, int>();
                var centros = new List<Vector3>();
                var cuenta = new List<int>();
                var hojaDe = new int[vc];
                for (int i = 0; i < vc; i++)
                {
                    int r = Raiz(i);
                    if (!idHoja.TryGetValue(r, out int h)) { h = centros.Count; idHoja[r] = h; centros.Add(Vector3.zero); cuenta.Add(0); }
                    hojaDe[i] = h;
                    centros[h] += p[i]; cuenta[h]++;
                }
                var conservar = new bool[centros.Count];
                for (int h = 0; h < centros.Count; h++)
                {
                    centros[h] /= cuenta[h];
                    conservar[h] = fraccion >= 1f || Azar(hojaGlobal + h, 917) < fraccion;
                }
                hojaGlobal += centros.Count;

                var nuevo = new int[vc];
                for (int i = 0; i < vc; i++)
                {
                    nuevo[i] = -1;
                    int h = hojaDe[i];
                    if (!conservar[h]) continue;
                    var local = centros[h] + (p[i] - centros[h]) * escalaHoja;
                    var w = aRaiz.MultiplyPoint3x4(local);
                    var nw = aRaiz.MultiplyVector(nn[i]).normalized;
                    var radial = (w - centroMancha).normalized;
                    if (Vector3.Dot(nw, radial) < 0f) nw = -nw;
                    nuevo[i] = pos.Count;
                    pos.Add(w);
                    nor.Add(Vector3.Lerp(nw, radial, MezclaVolumen).normalized);
                    col.Add(cc[i]);
                }
                for (int t = 0; t < ix.Length; t += 3)
                {
                    int a = nuevo[ix[t]], b = nuevo[ix[t + 1]], c = nuevo[ix[t + 2]];
                    if (a < 0 || b < 0 || c < 0) continue;
                    tri.Add(a); tri.Add(b); tri.Add(c);
                }
                p.Dispose(); nn.Dispose(); cc.Dispose(); ix.Dispose();
            }
        }

        string ruta = CarpetaQuenoa + nombre + ".asset";
        var malla = AssetDatabase.LoadAssetAtPath<Mesh>(ruta);
        bool nueva = malla == null;
        if (nueva) malla = new Mesh();
        malla.Clear();
        malla.name = nombre;
        malla.indexFormat = pos.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        malla.SetVertices(pos);
        malla.SetNormals(nor);
        malla.SetColors(col);
        malla.SetTriangles(tri, 0);
        malla.RecalculateBounds();
        malla.UploadMeshData(false);
        if (nueva) AssetDatabase.CreateAsset(malla, ruta);
        else EditorUtility.SetDirty(malla);
        AssetDatabase.SaveAssets();
        return malla;
    }

    // ------------------------------------------------ 2. Casas
    [MenuItem("Terrenos/Norte/Reordenar casas (Isluga)")]
    public static void ReordenarCasas()
    {
        var casas = GameObject.Find("Casas");
        var terreno = Terrain.activeTerrain;
        if (casas == null || terreno == null) { Debug.LogError("Abrir la escena Norte."); return; }

        var cabanas = new List<Transform>();
        var corrales = new List<Transform>();
        foreach (Transform t in casas.transform)
        {
            if (t.name.StartsWith("abandoned_cottage")) cabanas.Add(t);
            else if (t.name.StartsWith("RuralStall")) corrales.Add(t);
        }
        cabanas.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        corrales.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        // Dos cabañas por manzana, espalda con espalda, la fachada hacia la calle.
        var puestos = new List<(Vector2 c, float rot)>();
        foreach (var m in ManzanasCasas)
        {
            puestos.Add((new Vector2(m.x, m.y + 5.2f), 0f));
            puestos.Add((new Vector2(m.x, m.y - 5.2f), 180f));
        }
        var puestosCorral = new List<(Vector2 c, float rot)>();
        foreach (var m in ManzanasCorrales)
        {
            puestosCorral.Add((new Vector2(m.x - 5.5f, m.y), 0f));
            puestosCorral.Add((new Vector2(m.x + 5.5f, m.y), 0f));
        }

        var azar = new System.Random(7);
        int nc = Ubicar(cabanas, puestos, terreno, azar);
        int nr = Ubicar(corrales, puestosCorral, terreno, azar);
        EditorSceneManager.MarkSceneDirty(casas.scene);
        Debug.Log($"Casas reordenadas: {nc} cabañas y {nr} corrales.");
    }

    static int Ubicar(List<Transform> objetos, List<(Vector2 c, float rot)> puestos, Terrain terreno, System.Random azar)
    {
        int n = Mathf.Min(objetos.Count, puestos.Count);
        if (objetos.Count > puestos.Count)
            Debug.LogWarning($"Sobran {objetos.Count - puestos.Count} edificios ({objetos[0].name}...): quedan donde estaban.");
        for (int i = 0; i < n; i++)
        {
            var t = objetos[i];
            Undo.RecordObject(t, "Reordenar casas");
            // Huella en el espacio local del objeto (el pivote no esta en el centro).
            var b = Limites(t.gameObject);
            var offLocal = Quaternion.Inverse(t.rotation) * (b.center - t.position);
            var tam = Quaternion.Inverse(t.rotation) * b.size;
            tam = new Vector3(Mathf.Abs(tam.x), Mathf.Abs(tam.y), Mathf.Abs(tam.z));
            float pie = b.min.y - t.position.y;

            float rot = puestos[i].rot + (float)(azar.NextDouble() * 5.0 - 2.5);
            var q = Quaternion.Euler(0f, rot, 0f);
            var c = puestos[i].c + new Vector2((float)(azar.NextDouble() - 0.5), (float)(azar.NextDouble() - 0.5));
            var centro = new Vector3(c.x, 0f, c.y);
            var p = centro - q * new Vector3(offLocal.x, 0f, offLocal.z);

            // Apoyar cerca de la esquina mas baja: en desnivel, un poco enterrada del lado
            // alto antes que flotando del lado bajo.
            float minH = float.MaxValue, maxH = float.MinValue;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var esquina = centro + q * new Vector3(sx * tam.x * 0.5f, 0f, sz * tam.z * 0.5f);
                    float hh = terreno.SampleHeight(esquina) + terreno.transform.position.y;
                    minH = Mathf.Min(minH, hh);
                    maxH = Mathf.Max(maxH, hh);
                }
            float apoyo = minH + 0.12f * (maxH - minH) - 0.08f;
            p.y = apoyo - pie;
            t.SetPositionAndRotation(p, q);
            // Algunos modelos venian levemente inclinados: al dejarles solo el giro en Y
            // cambia la altura de su base. Se corrige midiendo de nuevo.
            p.y += apoyo - Limites(t.gameObject).min.y;
            t.position = p;
        }
        return n;
    }

    static Bounds Limites(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.zero);
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // ------------------------------------------------ 3. Pintado
    // Capas finales (indice fijo). El "NewLayer" con la textura de alturas y la nieve
    // solo se usan en la zona de montañas, que se conserva como la pinto el usuario.
    static readonly string[] Capas =
    {
        "Assets/FlipGameDev/Terrain&GrassPack/Terrain_Settings/Terrain_Layers/Mud.terrainlayer",          // 0 pampa
        "Assets/FlipGameDev/Terrain&GrassPack/Terrain_Settings/Terrain_Layers/Mud_Lieves_1.terrainlayer", // 1 hojarasca de tola
        "Assets/FlipGameDev/Terrain&GrassPack/Terrain_Settings/Terrain_Layers/Mud_Pebbles.terrainlayer",  // 2 pedregal
        "Assets/Layer-Terrain/NewLayer.terrainlayer",                                                     // 3 montaña (del usuario)
        "Assets/Layer-Terrain/NewLayer 1.terrainlayer",                                                   // 4 suelo rojizo de cerro
        "Assets/Layer-Terrain/NewLayer 3.terrainlayer",                                                   // 5 nieve (del usuario)
        "Assets/FlipGameDev/Terrain&GrassPack/Terrain_Settings/Terrain_Layers/RockSmooth_20x20.terrainlayer", // 6 roca
        CarpetaCapas + "Norte_Tierra.terrainlayer",                                                        // 7 camino y calles (tierra apisonada)
        CarpetaCapas + "Norte_Bofedal.terrainlayer",                                                       // 8 bofedal
        CarpetaCapas + "Norte_Pajonal.terrainlayer",                                                       // 9 pajonal
    };
    const int Pampa = 0, Tola = 1, Pedregal = 2, CerroRojo = 4, Roca = 6, Calle = 7, Bofedal = 8, Pajonal = 9;

    // Por encima de estas alturas se conserva el pintado original (las montañas).
    const float MontanaDesde = 14f, MontanaPlena = 22f;

    [MenuItem("Terrenos/Norte/Pintar terreno (Isluga)")]
    public static void PintarTerreno()
    {
        var terreno = Terrain.activeTerrain;
        if (terreno == null || !AssetDatabase.GetAssetPath(terreno.terrainData).Contains("Norte"))
        {
            Debug.LogError("El Terrain activo no es el de Norte.");
            return;
        }
        var td = terreno.terrainData;
        var capas = CargarCapas();
        if (capas == null) return;
        var masc = CargarMascaras();
        if (masc == null) return;

        int res = td.alphamapResolution;
        var viejo = td.GetAlphamaps(0, 0, res, res);
        var viejasCapas = td.terrainLayers;
        // Capa vieja -> capa nueva para conservar la montaña.
        var mapa = new int[viejasCapas.Length];
        for (int i = 0; i < viejasCapas.Length; i++)
        {
            mapa[i] = System.Array.IndexOf(capas, viejasCapas[i]);
            if (mapa[i] < 0)
                mapa[i] = viejasCapas[i] != null && viejasCapas[i].name.StartsWith("Mud_Lieves") ? Tola : Pampa;
        }

        var nuevo = new float[res, res, capas.Length];
        var w = new float[capas.Length];
        var tam = td.size;
        var origen = terreno.transform.position;

        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float u = (x + 0.5f) / res, v = (y + 0.5f) / res;
                float px = origen.x + u * tam.x, pz = origen.z + v * tam.z;
                float h = td.GetInterpolatedHeight(u, v);
                float pend = td.GetSteepness(u, v);
                var m = Mascara(masc, u, v);   // r bofedal, g pajonal, b tola, a rojizo

                System.Array.Clear(w, 0, w.Length);
                w[Pampa] = 1f;

                float ruido = Mathf.PerlinNoise(px * 0.045f + 11.3f, pz * 0.045f + 7.1f);
                float ruidoFino = Mathf.PerlinNoise(px * 0.21f + 3.7f, pz * 0.21f + 1.9f);

                // Pedregal: manchas en la pampa y mas en las laderas.
                Sobre(w, Pedregal, Mathf.Clamp01(0.10f + 0.45f * S(0.55f, 0.80f, ruido) + 0.35f * S(6f, 16f, pend)) * 0.6f);
                // Hojarasca de tola: sigue la densidad de matorral del satelite.
                Sobre(w, Tola, Mathf.Clamp01(m.b * 1.2f) * (0.45f + 0.55f * ruidoFino) * 0.75f);
                // Suelo rojizo de los cerros: con la altura, la pendiente y el rojo del satelite.
                float cerro = Mathf.Clamp01(0.55f * S(2f, 12f, h) + 0.5f * m.a + 0.3f * S(8f, 22f, pend));
                Sobre(w, CerroRojo, cerro * (0.55f + 0.45f * ruido));
                Sobre(w, Roca, S(24f, 36f, pend) * 0.85f);

                // Bofedal y pajonal: solo en lo bajo y llano.
                float llano = 1f - S(4f, 10f, pend);
                float bajo = 1f - S(4f, 9f, h);
                float bof = S(0.15f, 0.55f, m.r) * llano * bajo;
                float paj = S(0.10f, 0.50f, m.g) * (1f - S(8f, 16f, pend)) * (1f - S(6f, 12f, h));
                // Orla de pajonal alrededor del bofedal, como en el satelite.
                paj = Mathf.Max(paj, S(0.05f, 0.25f, m.r) * (1f - bof) * llano * bajo * 0.8f);

                // El pueblo pisa todo lo anterior.
                float calle = Calles(px, pz, ruidoFino);
                float camino = 1f - S(2.6f, 4.2f, DistanciaPolilinea(Camino, px, pz) + (ruidoFino - 0.5f) * 1.6f);
                float solar = EnSolar(px, pz);
                float atrio = Mathf.Max(DentroSuave(Atrio, px, pz, 2f), 0.35f * DentroSuave(Explanada, px, pz, 5f) * (0.5f + ruidoFino));
                float pueblo = Mathf.Max(Mathf.Max(calle, solar), atrio);

                Sobre(w, Pajonal, paj * 0.85f * (1f - pueblo));
                Sobre(w, Bofedal, bof * (1f - pueblo) * (1f - camino));
                Sobre(w, Pedregal, solar * 0.45f);
                                // Solo un toque: el ripio tiene saturacion ~0.1 y, si domina, el pase
                // de estilo lo lleva al azul. En Isluga las calles son la misma tierra.
                Sobre(w, Calle, Mathf.Max(calle * 0.42f, atrio * 0.35f));
                Sobre(w, Calle, camino * 0.55f);
                Sobre(w, Pedregal, Mathf.Max(calle, camino) * 0.25f);

                // Montaña: lo que pinto el usuario.
                float mont = S(MontanaDesde, MontanaPlena, h);
                float suma = 0f;
                for (int l = 0; l < w.Length; l++) w[l] *= 1f - mont;
                for (int l = 0; l < viejasCapas.Length; l++) w[mapa[l]] += viejo[y, x, l] * mont;
                for (int l = 0; l < w.Length; l++) suma += w[l];
                for (int l = 0; l < w.Length; l++) nuevo[y, x, l] = w[l] / Mathf.Max(suma, 1e-5f);
            }

        td.terrainLayers = capas;
        td.SetAlphamaps(0, 0, nuevo);
        EditorUtility.SetDirty(td);
        AssetDatabase.SaveAssets();

        var media = new float[capas.Length];
        for (int y = 0; y < res; y++) for (int x = 0; x < res; x++) for (int l = 0; l < capas.Length; l++) media[l] += nuevo[y, x, l];
        var sb = new System.Text.StringBuilder("Terreno de Norte pintado. Peso medio: ");
        for (int l = 0; l < capas.Length; l++) sb.Append($"{capas[l].name} {media[l] / (res * res):F3}  ");
        Debug.Log(sb.ToString());
    }

    static TerrainLayer[] CargarCapas()
    {
        // Calles y camino de tierra: la capa "Road" del pack es un empedrado de lajas.
        // Ripio pardo (matiz 30): una tierra gris (GroundCracked01, sat 0.08) queda
        // fuera de la banda de suelo del pase de estilo y la toma la del cielo: se ve hielo.
        CrearCapa(CarpetaCapas + "Norte_Tierra.terrainlayer",
                  "Assets/_TerrainAutoUpgrade/layer_GroundStones01GroundStones01_N2023054508611406.terrainlayer", 5f);
        CrearCapa(CarpetaCapas + "Norte_Bofedal.terrainlayer", CarpetaCapas + "Centro_Grass_Moss.terrainlayer", 5f);
        CrearCapa(CarpetaCapas + "Norte_Pajonal.terrainlayer", CarpetaCapas + "Centro_Grass_Dry.terrainlayer", 6f);
        var capas = new TerrainLayer[Capas.Length];
        for (int i = 0; i < Capas.Length; i++)
        {
            capas[i] = AssetDatabase.LoadAssetAtPath<TerrainLayer>(Capas[i]);
            if (capas[i] == null) { Debug.LogError("Falta la capa " + Capas[i]); return null; }
        }
        return capas;
    }

    static void CrearCapa(string ruta, string desde, float baldosa)
    {
        if (AssetDatabase.LoadAssetAtPath<TerrainLayer>(ruta) != null) return;
        var src = AssetDatabase.LoadAssetAtPath<TerrainLayer>(desde);
        var l = new TerrainLayer
        {
            diffuseTexture = src.diffuseTexture,
            normalMapTexture = src.normalMapTexture,
            maskMapTexture = src.maskMapTexture,
            normalScale = src.normalScale,
            smoothness = src.smoothness,
            metallic = src.metallic,
            tileSize = new Vector2(baldosa, baldosa),
        };
        AssetDatabase.CreateAsset(l, ruta);
    }

    static Color[] mascarasCache;
    static int mascarasLado;

    static Texture2D CargarMascaras()
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(RutaMascaras);
        if (ti == null) { Debug.LogError("Falta " + RutaMascaras + " (ver Tools/TerrenoNorte)."); return null; }
        if (!ti.isReadable || ti.sRGBTexture || ti.mipmapEnabled || ti.textureCompression != TextureImporterCompression.Uncompressed)
        {
            ti.isReadable = true;
            ti.sRGBTexture = false;
            ti.mipmapEnabled = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = false;
            ti.SaveAndReimport();
        }
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>(RutaMascaras);
        mascarasCache = t.GetPixels();
        mascarasLado = t.width;
        return t;
    }

    static Color Mascara(Texture2D t, float u, float v)
    {
        // Bilineal a mano sobre el cache (GetPixelBilinear por texel es lento).
        int n = mascarasLado;
        float fx = Mathf.Clamp(u * n - 0.5f, 0, n - 1.001f), fy = Mathf.Clamp(v * n - 0.5f, 0, n - 1.001f);
        int x0 = (int)fx, y0 = (int)fy;
        float tx = fx - x0, ty = fy - y0;
        var a = mascarasCache[y0 * n + x0]; var b = mascarasCache[y0 * n + x0 + 1];
        var c = mascarasCache[(y0 + 1) * n + x0]; var d = mascarasCache[(y0 + 1) * n + x0 + 1];
        return Color.Lerp(Color.Lerp(a, b, tx), Color.Lerp(c, d, tx), ty);
    }

    static float Calles(float x, float z, float ruido)
    {
        float d = float.MaxValue;
        float zMin = CallesEO[0], zMax = CallesEO[CallesEO.Length - 1];
        float xMin = CallesNS[0], xMax = CallesNS[CallesNS.Length - 1];
        foreach (var cx in CallesNS) d = Mathf.Min(d, DistanciaSegmento(new Vector2(cx, zMin), new Vector2(cx, zMax), x, z));
        foreach (var cz in CallesEO) d = Mathf.Min(d, DistanciaSegmento(new Vector2(xMin, cz), new Vector2(xMax, cz), x, z));
        // Las calles del borde del pueblo se pierden en la pampa.
        d += (ruido - 0.5f) * 1.4f;
        return 1f - S(MedioAnchoCalle - 0.8f, MedioAnchoCalle + 1.0f, d);
    }

    static float EnSolar(float x, float z)
    {
        float s = 0f;
        foreach (var m in ManzanasCasas) s = Mathf.Max(s, DentroSuave(new Rect(m.x - 11f, m.y - 10.5f, 22f, 21f), x, z, 1.5f));
        foreach (var m in ManzanasCorrales) s = Mathf.Max(s, 0.7f * DentroSuave(new Rect(m.x - 11f, m.y - 10.5f, 22f, 21f), x, z, 1.5f));
        return s;
    }

    static float DentroSuave(Rect r, float x, float z, float borde)
    {
        float dx = Mathf.Max(r.xMin - x, x - r.xMax, 0f);
        float dz = Mathf.Max(r.yMin - z, z - r.yMax, 0f);
        return 1f - S(0f, borde, Mathf.Sqrt(dx * dx + dz * dz));
    }

    static float DistanciaPolilinea(Vector2[] pl, float x, float z)
    {
        float d = float.MaxValue;
        for (int i = 0; i + 1 < pl.Length; i++) d = Mathf.Min(d, DistanciaSegmento(pl[i], pl[i + 1], x, z));
        return d;
    }

    static float DistanciaSegmento(Vector2 a, Vector2 b, float x, float z)
    {
        var p = new Vector2(x, z);
        var ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
        return Vector2.Distance(p, a + ab * t);
    }

    static void Sobre(float[] w, int capa, float a)
    {
        a = Mathf.Clamp01(a);
        if (a <= 0f) return;
        for (int i = 0; i < w.Length; i++) w[i] *= 1f - a;
        w[capa] += a;
    }

    static float S(float e0, float e1, float x)
    {
        float t = Mathf.Clamp01((x - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }

    static float Azar(int i, int semilla)
    {
        uint h = (uint)(i * 747796405 + semilla * 2891336453);
        h = ((h >> (int)((h >> 28) + 4)) ^ h) * 277803737u;
        h = (h >> 22) ^ h;
        return (h & 0xFFFFFF) / 16777216f;
    }

    // ------------------------------------------------ 4. Queñoas, ichu y tola
    // Queñoas (Polylepis tarapacana): no crecen en el pueblo ni en el bofedal sino
    // en las laderas pedregosas, en bosquetes ralos.
    const float QuenoaEscalaMin = 0.34f, QuenoaEscalaMax = 0.62f;   // el modelo mide 9.8 m
    const float SeparacionQuenoas = 3.5f;

    [MenuItem("Terrenos/Norte/Poblar queñoas e ichu")]
    public static void Poblar()
    {
        var terreno = Terrain.activeTerrain;
        if (terreno == null || !AssetDatabase.GetAssetPath(terreno.terrainData).Contains("Norte"))
        {
            Debug.LogError("El Terrain activo no es el de Norte.");
            return;
        }
        var td = terreno.terrainData;
        var quenoa = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabQuenoa);
        if (CargarMascaras() == null) return;

        int res = td.alphamapResolution;
        var alfa = td.GetAlphamaps(0, 0, res, res);
        int iCalle = IndiceCapa(td, "Norte_Tierra"), iBof = IndiceCapa(td, "Norte_Bofedal"), iPaj = IndiceCapa(td, "Norte_Pajonal");
        float Peso(int capa, float u, float v)
        {
            if (capa < 0) return 0f;
            int x = Mathf.Clamp((int)(u * res), 0, res - 1), y = Mathf.Clamp((int)(v * res), 0, res - 1);
            return alfa[y, x, capa];
        }

        var tam = td.size;
        var origen = terreno.transform.position;

        // --- arboles
        td.treePrototypes = new[] { new TreePrototype { prefab = quenoa } };
        var arboles = new List<TreeInstance>();
        var puestos = new List<Vector2>();
        const float Paso = 4f;
        int semilla = 0;
        for (float z = origen.z + 2f; z < origen.z + tam.z - 2f; z += Paso)
            for (float x = origen.x + 2f; x < origen.x + tam.x - 2f; x += Paso)
            {
                semilla++;
                float px = x + (Azar(semilla, 3) - 0.5f) * Paso, pz = z + (Azar(semilla, 5) - 0.5f) * Paso;
                float u = (px - origen.x) / tam.x, v = (pz - origen.z) / tam.z;
                float h = td.GetInterpolatedHeight(u, v);
                float pend = td.GetSteepness(u, v);

                float apto = S(2.5f, 5f, h) * (1f - S(26f, 32f, h)) * S(3f, 7f, pend) * (1f - S(32f, 40f, pend));
                apto *= 1f - Mathf.Clamp01(Peso(iCalle, u, v) * 2f);
                apto *= 1f - Mathf.Clamp01(Peso(iBof, u, v) * 2f);
                if (ZonaChaku.Contains(new Vector2(px, pz)) || DentroPueblo(px, pz, 10f)) apto = 0f;
                if (apto <= 0f) continue;

                // Bosquetes ralos y algun arbol suelto: ~120 en todo el terreno.
                float bosquete = S(0.56f, 0.76f, Mathf.PerlinNoise(px / 45f + 31.7f, pz / 45f + 12.9f));
                float p = apto * (0.005f + 0.16f * bosquete);
                if (Azar(semilla, 7) >= p) continue;

                var punto = new Vector2(px, pz);
                bool cerca = false;
                foreach (var q in puestos) if ((q - punto).sqrMagnitude < SeparacionQuenoas * SeparacionQuenoas) { cerca = true; break; }
                if (cerca) continue;
                puestos.Add(punto);

                float esc = Mathf.Lerp(QuenoaEscalaMin, QuenoaEscalaMax, Azar(semilla, 11));
                arboles.Add(new TreeInstance
                {
                    prototypeIndex = 0,
                    position = new Vector3(u, 0f, v),
                    heightScale = esc,
                    widthScale = esc * Mathf.Lerp(0.9f, 1.15f, Azar(semilla, 13)),
                    rotation = Azar(semilla, 17) * Mathf.PI * 2f,
                    color = Color.white,
                    lightmapColor = Color.white,
                });
            }
        td.SetTreeInstances(arboles.ToArray(), true);
        terreno.treeDistance = 600f;
        terreno.treeMaximumFullLODCount = 50;

        // --- detalles: dos ichus y la tola
        td.detailPrototypes = new[]
        {
            Pasto(AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Detalles/Det_Grass_3.png"), 0.8f, 1.3f, 0.55f, 0.95f),
            Pasto(TexturaIchu(), 0.7f, 1.2f, 0.50f, 0.85f),
            new DetailPrototype
            {
                usePrototypeMesh = true,
                prototype = ArbustoNorte(),
                renderMode = DetailRenderMode.VertexLit,
                useInstancing = true,
                minWidth = 0.45f, maxWidth = 0.9f, minHeight = 0.3f, maxHeight = 0.6f,
                noiseSpread = 0.4f,
                healthyColor = Color.white, dryColor = Color.white,
            },
            // Pasto corto del bofedal (en el real son cojines verdes de Distichia y Oxychloe).
            new DetailPrototype
            {
                usePrototypeMesh = false,
                prototypeTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Detalles/Det_Grass_2.png"),
                renderMode = DetailRenderMode.Grass,
                minWidth = 0.6f, maxWidth = 1.0f, minHeight = 0.25f, maxHeight = 0.45f,
                noiseSpread = 0.5f,
                healthyColor = new Color(0.42f, 0.62f, 0.28f, 1f),
                dryColor = new Color(0.70f, 0.66f, 0.40f, 1f),
            },
        };
        td.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
        td.SetDetailResolution(512, 32);   // celdas de ~1 m: 0/1 instancia por m2
        int dr = td.detailResolution;
        var ichuA = new int[dr, dr];
        var ichuB = new int[dr, dr];
        var tola = new int[dr, dr];
        var vega = new int[dr, dr];
        int nIchu = 0, nTola = 0;
        for (int y = 0; y < dr; y++)
            for (int x = 0; x < dr; x++)
            {
                float u = (x + 0.5f) / dr, v = (y + 0.5f) / dr;
                float px = origen.x + u * tam.x, pz = origen.z + v * tam.z;
                float h = td.GetInterpolatedHeight(u, v);
                float pend = td.GetSteepness(u, v);
                var m = Mascara(null, u, v);
                int kb = y * dr + x;
                float pb = Peso(iBof, u, v);
                if (pb > 0.2f && !DentroPueblo(px, pz, 2f))
                    vega[y, x] = Azar(kb, 31) < pb * 0.9f ? (Azar(kb, 37) < 0.5f ? 2 : 1) : 0;
                float libre = 1f - Mathf.Clamp01(Peso(iCalle, u, v) * 2.5f);
                libre *= 1f - Mathf.Clamp01(Peso(iBof, u, v) * 1.6f);
                if (DentroPueblo(px, pz, 2f)) libre = 0f;
                libre *= (1f - S(18f, 24f, h)) * (1f - S(26f, 36f, pend));
                if (libre <= 0f) continue;

                int k = y * dr + x;
                float mancha = Mathf.PerlinNoise(px * 0.06f + 5.1f, pz * 0.06f + 9.4f);
                float pIchu = (0.08f + 0.50f * Peso(iPaj, u, v) + 0.22f * m.b + 0.25f * S(0.45f, 0.75f, mancha)) * libre;
                if (Azar(k, 21) < pIchu) { if (Azar(k, 23) < 0.6f) ichuA[y, x] = 1; else ichuB[y, x] = 1; nIchu++; }
                float pTola = (0.015f + 0.12f * m.b) * libre * (1f - S(14f, 22f, pend)) * (1f - Peso(iPaj, u, v));
                if (Azar(k, 29) < pTola) { tola[y, x] = 1; nTola++; }
            }
        td.SetDetailLayer(0, 0, 0, ichuA);
        td.SetDetailLayer(0, 0, 1, ichuB);
        td.SetDetailLayer(0, 0, 2, tola);
        td.SetDetailLayer(0, 0, 3, vega);
        td.RefreshPrototypes();
        terreno.detailObjectDistance = 70f;
        EditorUtility.SetDirty(td);
        EditorUtility.SetDirty(terreno);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(terreno.gameObject.scene);
        Debug.Log($"Norte poblado: {arboles.Count} queñoas, {nIchu} matas de ichu, {nTola} tolas.");
    }

    static bool DentroPueblo(float x, float z, float margen)
    {
        if (x > CallesNS[0] - margen && x < CallesNS[CallesNS.Length - 1] + margen &&
            z > CallesEO[0] - margen && z < CallesEO[CallesEO.Length - 1] + margen) return true;
        foreach (var r in new[] { Atrio, Explanada })
            if (x > r.xMin - margen && x < r.xMax + margen && z > r.yMin - margen && z < r.yMax + margen) return true;
        foreach (var m in ManzanasCorrales)
            if (Mathf.Abs(x - m.x) < 11f + margen && Mathf.Abs(z - m.y) < 10.5f + margen) return true;
        return false;
    }

    static int IndiceCapa(TerrainData td, string nombre)
    {
        var c = td.terrainLayers;
        for (int i = 0; i < c.Length; i++) if (c[i] != null && c[i].name == nombre) return i;
        return -1;
    }

    // Ichu (Festuca orthophylla): matas de hoja fina color paja.
    static DetailPrototype Pasto(Texture2D tex, float w0, float w1, float h0, float h1)
    {
        return new DetailPrototype
        {
            usePrototypeMesh = false,
            prototypeTexture = tex,
            renderMode = DetailRenderMode.Grass,
            minWidth = w0, maxWidth = w1, minHeight = h0, maxHeight = h1,
            noiseSpread = 0.35f,
            // Mas dorado que el suelo: con el mismo valor que la pampa se pierde en ella.
            healthyColor = new Color(0.86f, 0.70f, 0.38f, 1f),
            dryColor = new Color(0.76f, 0.60f, 0.33f, 1f),
        };
    }

    // Copia propia: el original vive en la demo de Suimono, que no se usa (no sirve en URP).
    static Texture2D TexturaIchu()
    {
        const string destino = "Assets/Textures/Detalles/Det_Ichu_Paja.psd";
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(destino) == null)
            AssetDatabase.CopyAsset("Assets/SUIMONO - WATER SYSTEM 2/_DEMO/TERRAIN/textures_grass/tex_detail_grass_straw.psd", destino);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(destino);
    }

    // Tola (Parastrephia): arbusto bajo, redondo, verde oliva muy oscuro.
    static GameObject ArbustoNorte()
    {
        const string rutaPrefab = CarpetaDetalles + "Detalle_Arbusto_Norte.prefab";
        const string rutaMat = "Assets/Materials/Arbusto_Norte.mat";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(rutaPrefab);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(rutaMat);
        if (mat == null)
        {
            var orig = AssetDatabase.LoadAssetAtPath<GameObject>(CarpetaDetalles + "Detalle_Arbusto.prefab");
            mat = new Material(orig.GetComponentInChildren<Renderer>().sharedMaterial);
            mat.enableInstancing = true;
            AssetDatabase.CreateAsset(mat, rutaMat);
        }
        // Con 0.36 se leia como piedra negra: la textura del arbusto ya es oscura.
        mat.SetColor("_BaseColor", new Color(0.62f, 0.60f, 0.42f, 1f));
        EditorUtility.SetDirty(mat);
        if (prefab == null)
        {
            AssetDatabase.CopyAsset(CarpetaDetalles + "Detalle_Arbusto.prefab", rutaPrefab);
            var raiz = PrefabUtility.LoadPrefabContents(rutaPrefab);
            foreach (var r in raiz.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;
            PrefabUtility.SaveAsPrefabAsset(raiz, rutaPrefab);
            PrefabUtility.UnloadPrefabContents(raiz);
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(rutaPrefab);
        }
        return prefab;
    }

    // ------------------------------------------------ todo junto
    [MenuItem("Terrenos/Norte/Isluga completo (casas, pintado, vegetacion)")]
    public static void Todo()
    {
        ReordenarCasas();
        PintarTerreno();
        Poblar();
    }

    // ------------------------------------------------ capturas para revisar
    // Render con post y Tenkoku al dia (en modo edicion Tenkoku no corre su
    // LateUpdate y el cielo se queda de noche: ver memoria de calibracion).
    public static void Captura(string ruta, Vector3 pos, Vector3 mira, float fov = 60f, int ancho = 1280, int alto = 720)
    {
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (mb.GetType().Name != "TenkokuModule") continue;
            foreach (var n in new[] { "TimeUpdate", "UpdatePositions", "LateUpdate" })
            {
                try { mb.GetType().GetMethod(n, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)?.Invoke(mb, null); }
                catch (System.Exception) { }
            }
        }
        var main = Camera.main;
        var go = new GameObject("__captura") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.CopyFrom(main);
        var datos = cam.GetUniversalAdditionalCameraData();
        var datosMain = main.GetUniversalAdditionalCameraData();
        datos.renderPostProcessing = true;
        datos.antialiasing = datosMain.antialiasing;
        datos.volumeLayerMask = datosMain.volumeLayerMask;
        datos.renderShadows = true;
        go.transform.position = pos;
        go.transform.LookAt(mira);
        cam.fieldOfView = fov;
        var rt = new RenderTexture(ancho, alto, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(ancho, alto, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, ancho, alto), 0, 0);
        RenderTexture.active = null;
        cam.targetTexture = null;
        File.WriteAllBytes(ruta, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
    }
}
