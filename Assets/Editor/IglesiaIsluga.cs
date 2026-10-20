using System.IO;
using UnityEditor;
using UnityEngine;

// Iglesia de Isluga (Norte). Blender: Concept/Norte/Catedral/Catedral2.blend, scripts en
// Tools/Blender/catedral (unity_catedral.py hornea y exporta). El FBX trae cuatro partes
// (Iglesia, Torre, Barandas, Casucha), cada una con un solo material "M_Isluga_<Parte>" y su
// propio atlas horneado:
//   Texturas/Isluga_<Parte>_BaseColor.png           color (sRGB)
//   Texturas/Isluga_<Parte>_MetallicSmoothness.png  R = metal, A = suavidad (1 - rugosidad de Blender)
//   Texturas/Isluga_<Parte>_Normal.png, _AO.png
// Aquí se crean esos materiales como URP/Lit, se remapean en el importador y se crea el prefab.
// Re-ejecutar es seguro: actualiza materiales e importador en el lugar y no pisa el prefab.
public static class IglesiaIsluga
{
    const string Carpeta = "Assets/Models/Norte/Catedral/Isluga";
    const string CarpetaTex = Carpeta + "/Texturas";
    const string CarpetaMat = Carpeta + "/Materiales";
    const string CarpetaPrefab = "Assets/Prefabs/Iglesia";
    const string Fbx = Carpeta + "/Isluga.fbx";
    static readonly string[] Partes = { "Iglesia", "Torre", "Barandas", "Casucha" };

    [MenuItem("Terrenos/Norte/Iglesia de Isluga: materiales y prefab")]
    public static void Configurar()
    {
        AssetDatabase.Refresh();
        if (!File.Exists(Fbx)) { Debug.LogWarning($"[Isluga] Falta {Fbx}"); return; }
        Carpetas();
        var mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
        foreach (var parte in Partes)
        {
            string nombre = "Isluga_" + parte;
            ImportarTextura($"{CarpetaTex}/{nombre}_BaseColor.png", Tipo.Color);
            ImportarTextura($"{CarpetaTex}/{nombre}_MetallicSmoothness.png", Tipo.MetalSuav);
            ImportarTextura($"{CarpetaTex}/{nombre}_Normal.png", Tipo.Normal);
            ImportarTextura($"{CarpetaTex}/{nombre}_AO.png", Tipo.Datos);
            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_" + nombre), Material(nombre));
        }
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        mi.importNormals = ModelImporterNormals.Import;
        mi.importTangents = ModelImporterTangents.CalculateMikk;   // mismo espacio tangente que el horneado de Blender
        mi.importCameras = false;
        mi.importLights = false;
        mi.importAnimation = false;
        mi.animationType = ModelImporterAnimationType.None;
        mi.SaveAndReimport();
        Prefab();
        AssetDatabase.SaveAssets();
        Debug.Log($"[Isluga] Listo: materiales en {CarpetaMat}, prefab en {CarpetaPrefab}/Isluga.prefab");
    }

    static void Carpetas()
    {
        if (!AssetDatabase.IsValidFolder(CarpetaMat)) AssetDatabase.CreateFolder(Carpeta, "Materiales");
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(CarpetaPrefab)) AssetDatabase.CreateFolder("Assets/Prefabs", "Iglesia");
    }

    enum Tipo { Color, Normal, MetalSuav, Datos }

    static void ImportarTextura(string ruta, Tipo tipo)
    {
        if (!(AssetImporter.GetAtPath(ruta) is TextureImporter ti)) { Debug.LogWarning($"[Isluga] Falta {ruta}"); return; }
        ti.textureType = tipo == Tipo.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        ti.sRGBTexture = tipo == Tipo.Color;
        ti.alphaSource = tipo == Tipo.MetalSuav ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        // metal/suavidad y AO varían suave: a 1024 bastan
        ti.maxTextureSize = tipo == Tipo.Color || tipo == Tipo.Normal ? 2048 : 1024;
        ti.mipmapEnabled = true;
        ti.anisoLevel = tipo == Tipo.Color || tipo == Tipo.Normal ? 4 : 1;   // techos y muros vistos en ángulo
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.SaveAndReimport();
    }

    static Material Material(string nombre)
    {
        string ruta = $"{CarpetaMat}/M_{nombre}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        bool nuevo = mat == null;
        if (nuevo) mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_" + nombre };

        mat.SetTexture("_BaseMap", Tex($"{nombre}_BaseColor"));
        mat.SetColor("_BaseColor", Color.white);
        mat.SetTexture("_BumpMap", Tex($"{nombre}_Normal"));
        mat.SetFloat("_BumpScale", 1f);
        mat.EnableKeyword("_NORMALMAP");
        mat.SetTexture("_MetallicGlossMap", Tex($"{nombre}_MetallicSmoothness"));
        mat.SetFloat("_Metallic", 1f);
        mat.SetFloat("_Smoothness", 1f);                 // con mapa, es el multiplicador del alfa
        mat.SetFloat("_SmoothnessTextureChannel", 0f);   // alfa del mapa metálico
        mat.EnableKeyword("_METALLICSPECGLOSSMAP");
        mat.SetTexture("_OcclusionMap", Tex($"{nombre}_AO"));
        mat.SetFloat("_OcclusionStrength", 1f);
        mat.EnableKeyword("_OCCLUSIONMAP");
        mat.enableInstancing = true;

        if (nuevo) AssetDatabase.CreateAsset(mat, ruta);
        else EditorUtility.SetDirty(mat);
        return mat;
    }

    static Texture2D Tex(string nombre) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{CarpetaTex}/{nombre}.png");

    static void Prefab()
    {
        string ruta = $"{CarpetaPrefab}/Isluga.prefab";
        if (File.Exists(ruta)) return;
        var modelo = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(modelo);
        inst.name = "Isluga";
        foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
            if (!mf.GetComponent<Collider>()) mf.gameObject.AddComponent<MeshCollider>();
        PrefabUtility.SaveAsPrefabAsset(inst, ruta);
        Object.DestroyImmediate(inst);
    }
}
