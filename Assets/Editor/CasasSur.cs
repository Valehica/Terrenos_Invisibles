using System.IO;
using UnityEditor;
using UnityEngine;

// Casas de Sur (Blender: Concept/Sur/Pasarela/Casas/Casas.blend, scripts en Tools/Blender/casas).
// Cada FBX trae un solo material "M_<Casa>_<V>"; aquí se crea ese material como URP/Lit con el
// atlas horneado (hornear_casas.py), se remapea en el importador del FBX y se crea el prefab.
//   Texturas/<Casa>_<V>_BaseColor.png          color (sRGB)
//   Texturas/<Casa>_<V>_MetallicSmoothness.png R = metal, A = suavidad (1 - rugosidad de Blender)
//   Texturas/<Casa>_Normal.png, <Casa>_AO.png  compartidas por las 3 variantes del diseño
// Re-ejecutar es seguro: actualiza materiales e importadores en el lugar y no pisa prefabs existentes.
public static class CasasSur
{
    const string Carpeta = "Assets/Models/Sur/Casas";
    const string CarpetaTex = Carpeta + "/Texturas";
    const string CarpetaMat = Carpeta + "/Materiales";
    const string CarpetaPrefab = "Assets/Prefabs/Casas";
    static readonly string[] Casas = { "Casa1", "Casa2", "Casa3", "Casa4" };
    static readonly string[] Variantes = { "V1", "V2", "V3" };

    [MenuItem("Terrenos/Sur/Casas: materiales y prefabs")]
    public static void Configurar()
    {
        AssetDatabase.Refresh();
        Carpetas();
        int n = 0;
        foreach (var casa in Casas)
        {
            ImportarTextura($"{CarpetaTex}/{casa}_Normal.png", Tipo.Normal);
            ImportarTextura($"{CarpetaTex}/{casa}_AO.png", Tipo.Datos);
            foreach (var v in Variantes)
            {
                string nombre = $"{casa}_{v}";
                string fbx = $"{Carpeta}/{nombre}.fbx";
                if (!File.Exists(fbx)) { Debug.LogWarning($"[Casas] Falta {fbx}"); continue; }
                ImportarTextura($"{CarpetaTex}/{nombre}_BaseColor.png", Tipo.Color);
                ImportarTextura($"{CarpetaTex}/{nombre}_MetallicSmoothness.png", Tipo.MetalSuav);
                var mat = Material(casa, nombre);
                ConfigurarFbx(fbx, nombre, mat);
                Prefab(fbx, nombre);
                n++;
            }
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Casas] {n} casas configuradas (materiales en {CarpetaMat}, prefabs en {CarpetaPrefab}).");
    }

    static void Carpetas()
    {
        if (!AssetDatabase.IsValidFolder(CarpetaMat)) AssetDatabase.CreateFolder(Carpeta, "Materiales");
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(CarpetaPrefab)) AssetDatabase.CreateFolder("Assets/Prefabs", "Casas");
    }

    enum Tipo { Color, Normal, MetalSuav, Datos }

    static void ImportarTextura(string ruta, Tipo tipo)
    {
        if (!(AssetImporter.GetAtPath(ruta) is TextureImporter ti)) { Debug.LogWarning($"[Casas] Falta {ruta}"); return; }
        ti.textureType = tipo == Tipo.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        ti.sRGBTexture = tipo == Tipo.Color;
        ti.alphaSource = tipo == Tipo.MetalSuav ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        // metal/suavidad y AO varían suave: a 1024 bastan y ahorran memoria
        ti.maxTextureSize = tipo == Tipo.Color || tipo == Tipo.Normal ? 2048 : 1024;
        ti.mipmapEnabled = true;
        ti.anisoLevel = tipo == Tipo.Color || tipo == Tipo.Normal ? 4 : 1;   // techos y muros vistos en ángulo
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.SaveAndReimport();
    }

    static Material Material(string casa, string nombre)
    {
        string ruta = $"{CarpetaMat}/M_{nombre}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        bool nuevo = mat == null;
        if (nuevo) mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_" + nombre };

        mat.SetTexture("_BaseMap", Tex($"{nombre}_BaseColor"));
        mat.SetColor("_BaseColor", Color.white);
        mat.SetTexture("_BumpMap", Tex($"{casa}_Normal"));
        mat.SetFloat("_BumpScale", 1f);
        mat.EnableKeyword("_NORMALMAP");
        mat.SetTexture("_MetallicGlossMap", Tex($"{nombre}_MetallicSmoothness"));
        mat.SetFloat("_Metallic", 1f);
        mat.SetFloat("_Smoothness", 1f);                 // con mapa, es el multiplicador del alfa
        mat.SetFloat("_SmoothnessTextureChannel", 0f);   // alfa del mapa metálico
        mat.EnableKeyword("_METALLICSPECGLOSSMAP");
        mat.SetTexture("_OcclusionMap", Tex($"{casa}_AO"));
        mat.SetFloat("_OcclusionStrength", 1f);
        mat.EnableKeyword("_OCCLUSIONMAP");
        mat.enableInstancing = true;

        if (nuevo) AssetDatabase.CreateAsset(mat, ruta);
        else EditorUtility.SetDirty(mat);
        return mat;
    }

    static Texture2D Tex(string nombre) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{CarpetaTex}/{nombre}.png");

    static void ConfigurarFbx(string ruta, string nombre, Material mat)
    {
        var mi = (ModelImporter)AssetImporter.GetAtPath(ruta);
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        mi.importNormals = ModelImporterNormals.Import;
        mi.importTangents = ModelImporterTangents.CalculateMikk;   // mismo espacio tangente que el horneado de Blender
        mi.importCameras = false;
        mi.importLights = false;
        mi.importAnimation = false;
        mi.animationType = ModelImporterAnimationType.None;
        mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_" + nombre), mat);
        mi.SaveAndReimport();
    }

    static void Prefab(string fbx, string nombre)
    {
        string ruta = $"{CarpetaPrefab}/{nombre}.prefab";
        if (File.Exists(ruta)) return;
        var modelo = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(modelo);
        inst.name = nombre;
        foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
            if (!mf.GetComponent<Collider>()) mf.gameObject.AddComponent<MeshCollider>();
        PrefabUtility.SaveAsPrefabAsset(inst, ruta);
        Object.DestroyImmediate(inst);
    }
}
