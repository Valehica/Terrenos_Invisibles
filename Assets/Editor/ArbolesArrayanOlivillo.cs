using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Rendering;

// Texturas de los arrayanes y olivillos de Centro (ArrayanGrande/ArrayanPeque/OlivilloGrande/OlivilloPeque.fbx,
// exportados de Concept/Centro/Arboles/Arboles_Centro_Collection8.blend).
//
// Mismo caso que los coigues (ver ArbolesCoigueCipresSur): en Blender la corteza va con coordenadas de objeto ->
// Mapping (1, 1, -0.4) -> Image Texture en Box, asi que los troncos llegan sin UV. Este postprocesador rehace esa
// proyeccion como UV al importar (en Blender el Box tiene blend 0.5; aca queda en 0, la unica que entra en UV)
// y hornea los datos de viento en el color de vertice.
//
// Las hojas del arrayan: en Blender la imagen entra solo como Value de un HSV sobre un verde fijo (y el alfa
// recorta). Hojas_Arrayan.png es Arrayan.png con RGB = luminancia y el alfa intacto; el verde va en _BaseColor.
// Las del olivillo usan la imagen tal cual.
//
// Menu Terrenos > Centro > Materiales arrayan y olivillo: crea los materiales en Vegetacion/Materials y los
// asigna a los cuatro FBX (remap), sin tocar el .blend.
// Menu Terrenos > Centro > Arrayan y olivillo al Terrain: arma Arbol_*.prefab y los pone como prototipos.
// Menu Terrenos > Centro > Arrayan y olivillo lejanos: hornea Arbol_*_Lejos.prefab para ArbolesVisibles.
public class ArbolesArrayanOlivillo : AssetPostprocessor
{
    const string Carpeta = "Assets/Models/Centro/Gaviotin/Vegetacion";
    const string CarpetaMat = Carpeta + "/Materials";
    static readonly string[] Modelos = { "ArrayanGrande", "ArrayanPeque", "OlivilloGrande", "OlivilloPeque" };
    // Sueltos en la escena (no van al Terrain): solo UV Box, viento y materiales. ArrayanEspecial sale de
    // Concept/Centro/Arboles/ArrayanEspecial.blend con los mismos materiales que el arrayan.
    static readonly string[] Sueltos = { "Assets/Models/Sur/Vegetacion/ArrayanEspecial.fbx" };

    static IEnumerable<string> RutasFbx()
    {
        foreach (var nombre in Modelos) yield return $"{Carpeta}/{nombre}.fbx";
        foreach (var ruta in Sueltos) yield return ruta;
    }

    // Materiales de corteza en el .blend (los nombres que trae el FBX).
    static readonly string[] Cortezas = { "M_TroncoArrayan", "M_Olivillo" };
    static readonly Vector3 EscalaMapping = new Vector3(1f, 1f, -0.4f);

    public override uint GetVersion() => 1;

    static bool EsCorteza(Material m) => m != null && System.Array.IndexOf(Cortezas, m.name) >= 0;

    bool EsArbol() => System.Linq.Enumerable.Contains(RutasFbx(), assetPath);

    // Con Read/Write apagado lo que se escribe en la malla se pierde (queda con 0 vertices).
    void OnPreprocessModel()
    {
        if (EsArbol()) ((ModelImporter)assetImporter).isReadable = true;
    }

    void OnPostprocessModel(GameObject raiz)
    {
        if (!EsArbol()) return;
        foreach (var r in raiz.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mf = r.GetComponent<MeshFilter>();
            if (!EsCorteza(r.sharedMaterial) || mf == null || mf.sharedMesh == null) continue;
            ArbolesCoigueCipresSur.UvBox(mf.sharedMesh, EscalaMapping);
        }
        ArbolesCoigueCipresSur.DatosViento(raiz, EsCorteza);
    }

    // ---------------------------------------------------------------- prefabs para el Terrain

    // El Terrain no acepta estos FBX como prototipo (la raiz no tiene malla: "the prefab contains no valid mesh
    // renderer"). Arbol_<nombre>.prefab es una raiz con LODGroup y el collider del tronco, y el FBX como hijo
    // corrido para que la base del tronco quede en el pivote (en el .blend estan desparramados hasta 22 m).
    // Los prototipos que eran los FBX pasan a ser estos prefabs; los arboles ya pintados se conservan.
    [MenuItem("Terrenos/Centro/Arrayan y olivillo al Terrain")]
    static void PrefabsAlTerrain()
    {
        var reemplazo = new Dictionary<GameObject, GameObject>();
        foreach (var nombre in Modelos)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{Carpeta}/{nombre}.fbx");
            if (fbx == null) continue;
            reemplazo[fbx] = Prefab(nombre, fbx);
        }

        foreach (var t in Terrain.activeTerrains)
        {
            var td = t.terrainData;
            var protos = td.treePrototypes;
            bool cambio = false;
            foreach (var p in protos)
                if (p.prefab != null && reemplazo.TryGetValue(p.prefab, out var nuevo)) { p.prefab = nuevo; cambio = true; }
            if (!cambio) continue;
            Undo.RegisterCompleteObjectUndo(td, "Arrayan y olivillo al Terrain");
            td.treePrototypes = protos;
            td.RefreshPrototypes();
            t.Flush();
            EditorUtility.SetDirty(td);
            Debug.Log($"{t.name}: prototipos de arrayan y olivillo cambiados por sus prefabs.");
        }
        AssetDatabase.SaveAssets();
    }

    // Arbol_<nombre>_Lejos.prefab (dos planos con la foto del arbol, ver ArbolesLejanosSur) de los cuatro prefabs, y
    // los agrega como prototipos sin pintar ninguno: ArbolesVisibles los usa en lugar del arbol de verdad pasada su
    // distanciaDetalle. Re-correrlo si cambian los prefabs o sus materiales.
    const float TonoCortezaLejos = 0.55f;

    [MenuItem("Terrenos/Centro/Arrayan y olivillo lejanos")]
    static void Lejanos()
    {
        var cercanos = new List<(string, GameObject)>();
        foreach (var nombre in Modelos)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Carpeta}/Arbol_{nombre}.prefab");
            if (prefab != null) cercanos.Add((nombre, prefab));
        }
        if (cercanos.Count == 0) { Debug.LogWarning("Faltan los Arbol_*.prefab: correr antes 'Arrayan y olivillo al Terrain'."); return; }
        // Corteza al 55%: los troncos lejanos salian claros (rosados los del olivillo) al lado de los de verdad.
        var lejanos = ArbolesLejanosSur.Crear(cercanos, Carpeta, TonoCortezaLejos);

        // Brillo y tinte igualados contra los arboles de verdad en la misma vista (2026-10-20, pantallazos con
        // distanciaDetalle 60 contra 2500). El corte de alfa mas bajo: a 300 m o mas las copas se afinaban en los
        // mips y la linea del bosque quedaba mas baja que con los de verdad.
        var mat = AssetDatabase.LoadAssetAtPath<Material>(CarpetaMat + "/M_Arboles_Lejos.mat");
        mat.SetFloat("_Brillo", 0.98f);
        mat.SetColor("_Tinte", new Color(0.87f, 0.89f, 1.08f));
        mat.SetFloat("_Cutoff", 0.35f);
        EditorUtility.SetDirty(mat);

        foreach (var t in Terrain.activeTerrains)
        {
            var td = t.terrainData;
            var protos = new List<TreePrototype>(td.treePrototypes);
            if (!protos.Exists(p => cercanos.Exists(c => c.Item2 == p.prefab))) continue;
            int antes = protos.Count;
            foreach (var l in lejanos)
                if (!protos.Exists(p => p.prefab == l)) protos.Add(new TreePrototype { prefab = l });
            if (protos.Count == antes) continue;
            Undo.RegisterCompleteObjectUndo(td, "Arrayan y olivillo lejanos");
            td.treePrototypes = protos.ToArray();
            td.RefreshPrototypes();
            t.Flush();
            EditorUtility.SetDirty(td);
            Debug.Log($"{t.name}: {protos.Count - antes} prototipos lejanos agregados ({protos.Count} en total).");
        }
        AssetDatabase.SaveAssets();
    }

    static GameObject Prefab(string nombre, GameObject fbx)
    {
        var raiz = new GameObject("Arbol_" + nombre);
        var hijo = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        hijo.transform.SetParent(raiz.transform, false);

        var rs = hijo.GetComponentsInChildren<MeshRenderer>(true);
        var tronco = System.Array.Find(rs, r => EsCorteza(r.sharedMaterial));
        if (tronco != null)
        {
            var cap = ArbolesCoigueCipresSur.ColliderTronco(raiz, tronco);
            float yMin = cap.center.y - cap.height * 0.5f;
            hijo.transform.localPosition -= new Vector3(cap.center.x, yMin, cap.center.z);
            cap.center = new Vector3(0f, cap.height * 0.5f, 0f);
            // ColliderTronco mide en los primeros 4 m y el ArrayanPeque ya se abrio en ramas ahi (salia 1.3 m).
            float r = RadioBase(raiz, tronco);
            if (r > 0f) cap.radius = Mathf.Min(cap.radius, Mathf.Max(r, 0.15f));
        }

        var lod = raiz.AddComponent<LODGroup>();
        lod.SetLODs(new[] { new LOD(ArbolesCoigueCipresSur.CorteLod, rs) });
        lod.RecalculateBounds();

        var prefab = PrefabUtility.SaveAsPrefabAsset(raiz, $"{Carpeta}/Arbol_{nombre}.prefab");
        Object.DestroyImmediate(raiz);
        return prefab;
    }

    // Mediana de la distancia al eje (x=z=0, ya centrado) de los vertices del primer metro y medio. 0 si no hay.
    static float RadioBase(GameObject raiz, MeshRenderer tronco)
    {
        var m = raiz.transform.worldToLocalMatrix * tronco.transform.localToWorldMatrix;
        var radios = new List<float>();
        foreach (var v in tronco.GetComponent<MeshFilter>().sharedMesh.vertices)
        {
            var p = m.MultiplyPoint3x4(v);
            if (p.y < 1.5f) radios.Add(new Vector2(p.x, p.z).magnitude);
        }
        if (radios.Count < 6) return 0f;
        radios.Sort();
        return radios[radios.Count / 2];
    }

    // ---------------------------------------------------------------- materiales

    [MenuItem("Terrenos/Centro/Materiales arrayan y olivillo")]
    static void Materiales()
    {
        const string A = Carpeta + "/TextureArrayan/", O = Carpeta + "/TextureOlivillo/";
        ImportarTextura(A + "pine_bark_diff_500.jpg", false, false);
        ImportarTextura(A + "pine_bark_nor_gl_500.exr", true, false);
        ImportarTextura(A + "Hojas_Arrayan.png", false, true);
        ImportarTextura(O + "bark_willow_02_diff_500.jpg", false, false);
        ImportarTextura(O + "bark_willow_02_nor_gl_500.exr", true, false);
        ImportarTextura(O + "OlivilloHoja.png", false, true);

        // Verde de las hojas del arrayan (lineal). En Blender es (0.275, 0.373, 0.2), pero en Centro las copas
        // salian casi negras; se aclaro al calibrar el postpro (2026-10-12). La corteza del olivillo, gris, se
        // veia azul con el cielo de Tenkoku: lleva un tinte calido.
        var mats = new Dictionary<string, Material>
        {
            ["M_TroncoArrayan"] = Corteza("M_TroncoArrayan", A + "pine_bark_diff_500.jpg", A + "pine_bark_nor_gl_500.exr", Color.white),
            ["M_Olivillo"] = Corteza("M_Olivillo", O + "bark_willow_02_diff_500.jpg", O + "bark_willow_02_nor_gl_500.exr", new Color(1f, 0.88f, 0.76f)),
            ["M_Hojas_Arrayan"] = Hojas("M_Hojas_Arrayan", A + "Hojas_Arrayan.png", new Color(0.44f, 0.60f, 0.30f)),
            ["M_Hojas_Olivillos"] = Hojas("M_Hojas_Olivillos", O + "OlivilloHoja.png", Color.white),
        };
        AssetDatabase.SaveAssets();

        foreach (var ruta in RutasFbx())
        {
            if (!(AssetImporter.GetAtPath(ruta) is ModelImporter mi)) continue;
            foreach (var m in mats)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.Key), m.Value);
            mi.SaveAndReimport();
        }
        Debug.Log("Materiales de arrayan y olivillo asignados.");
    }

    // En Blender el normal entra a un Bump sin altura (no hace nada) y la rugosidad sale del disp (~0.6) con
    // IOR 1, o sea sin brillo: aca va el normal de verdad y una superficie mate.
    static Material Corteza(string nombre, string color, string normal, Color tinte)
    {
        var m = CargarOCrear(nombre, out bool nuevo);
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(color));
        m.SetColor("_BaseColor", tinte);
        m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
        m.SetFloat("_BumpScale", 1f);
        m.SetFloat("_Smoothness", 0.1f);
        m.SetFloat("_AlphaClip", 0f);
        m.SetFloat("_Cull", (float)CullMode.Back);
        return Guardar(m, nombre, nuevo);
    }

    static Material Hojas(string nombre, string textura, Color colorLineal)
    {
        var m = CargarOCrear(nombre, out bool nuevo);
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(textura));
        m.SetColor("_BaseColor", colorLineal.gamma);
        m.SetTexture("_BumpMap", null);
        m.SetFloat("_Smoothness", 0f);          // Roughness 1 en Blender
        m.SetFloat("_AlphaClip", 1f);
        m.SetFloat("_Cutoff", 0.5f);
        m.SetFloat("_Cull", (float)CullMode.Off);   // Blender las dibuja por las dos caras
        return Guardar(m, nombre, nuevo);
    }

    static Material CargarOCrear(string nombre, out bool nuevo)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>($"{CarpetaMat}/{nombre}.mat");
        nuevo = m == null;
        // URP/Lit con viento: sin el componente VientoArboles en la escena quedan quietos.
        var shader = Shader.Find("Terrenos/ArbolViento");
        if (nuevo) m = new Material(shader);
        else m.shader = shader;
        return m;
    }

    static Material Guardar(Material m, string nombre, bool nuevo)
    {
        m.SetFloat("_WorkflowMode", 1f);   // metalico
        m.SetFloat("_Metallic", 0f);
        m.enableInstancing = true;
        BaseShaderGUI.SetMaterialKeywords(m, LitGUI.SetMaterialKeywords);
        if (nuevo)
        {
            if (!AssetDatabase.IsValidFolder(CarpetaMat)) AssetDatabase.CreateFolder(Carpeta, "Materials");
            AssetDatabase.CreateAsset(m, $"{CarpetaMat}/{nombre}.mat");
        }
        else EditorUtility.SetDirty(m);
        return m;
    }

    static void ImportarTextura(string ruta, bool normal, bool hojas)
    {
        if (!(AssetImporter.GetAtPath(ruta) is TextureImporter ti)) { Debug.LogWarning("Falta " + ruta); return; }
        ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        ti.sRGBTexture = !normal;
        ti.maxTextureSize = 512;
        ti.wrapMode = hojas ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        ti.alphaSource = hojas ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        ti.alphaIsTransparency = hojas;
        ti.mipMapsPreserveCoverage = hojas;     // sin esto el alfa se lava en los mips y la copa se pela de lejos
        ti.alphaTestReferenceValue = 0.5f;
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.SaveAndReimport();
    }
}
