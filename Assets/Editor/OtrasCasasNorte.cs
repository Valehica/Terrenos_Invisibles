using System.IO;
using UnityEditor;
using UnityEngine;

// Otras casas de Norte (Isluga). Blender: Concept/Norte/OtrasCasas/PackCasas.blend, scripts en
// Tools/Blender/casas_norte (unity_casas_norte.py hornea y exporta). Cada casa es su propio FBX
// "Norte_CasaN_Vm.fbx" con un solo material "M_Norte_CasaN_Vm" y su atlas horneado:
//   Texturas/<casa>_BaseColor.png           color (sRGB)
//   Texturas/<casa>_MetallicSmoothness.png  R = metal, A = suavidad (1 - rugosidad de Blender)
//   Texturas/<casa>_Normal.png, _AO.png
// Aquí se crean los materiales URP/Lit, se remapean en el importador y se crean los prefabs.
// Re-ejecutar es seguro: actualiza materiales e importadores en el lugar y no pisa prefabs existentes.
public static class OtrasCasasNorte
{
    const string Carpeta = "Assets/Models/Norte/OtrasCasas";
    const string CarpetaTex = Carpeta + "/Texturas";
    const string CarpetaMat = Carpeta + "/Materiales";
    const string CarpetaPrefab = "Assets/Prefabs/OtrasCasas";

    [MenuItem("Terrenos/Norte/Otras casas: materiales y prefabs")]
    public static void Configurar()
    {
        AssetDatabase.Refresh();
        if (!AssetDatabase.IsValidFolder(Carpeta)) { Debug.LogWarning($"[OtrasCasas] Falta {Carpeta}"); return; }
        Carpetas();
        int n = 0;
        foreach (var guid in AssetDatabase.FindAssets("Norte_Casa t:Model", new[] { Carpeta }))
        {
            string fbx = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetDirectoryName(fbx).Replace('\\', '/') != Carpeta) continue;
            string nombre = Path.GetFileNameWithoutExtension(fbx);
            ImportarTextura($"{CarpetaTex}/{nombre}_BaseColor.png", Tipo.Color);
            ImportarTextura($"{CarpetaTex}/{nombre}_MetallicSmoothness.png", Tipo.MetalSuav);
            ImportarTextura($"{CarpetaTex}/{nombre}_Normal.png", Tipo.Normal);
            ImportarTextura($"{CarpetaTex}/{nombre}_AO.png", Tipo.Datos);
            var mat = Material(nombre);
            ConfigurarFbx(fbx, nombre, mat);
            Prefab(fbx, nombre);
            n++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[OtrasCasas] {n} casas configuradas (materiales en {CarpetaMat}, prefabs en {CarpetaPrefab}).");
    }

    static void Carpetas()
    {
        if (!AssetDatabase.IsValidFolder(CarpetaMat)) AssetDatabase.CreateFolder(Carpeta, "Materiales");
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(CarpetaPrefab)) AssetDatabase.CreateFolder("Assets/Prefabs", "OtrasCasas");
    }

    enum Tipo { Color, Normal, MetalSuav, Datos }

    static void ImportarTextura(string ruta, Tipo tipo)
    {
        if (!(AssetImporter.GetAtPath(ruta) is TextureImporter ti)) { Debug.LogWarning($"[OtrasCasas] Falta {ruta}"); return; }
        ti.textureType = tipo == Tipo.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        ti.sRGBTexture = tipo == Tipo.Color;
        ti.alphaSource = tipo == Tipo.MetalSuav ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        // metal/suavidad y AO varían suave: a 1024 bastan
        ti.maxTextureSize = tipo == Tipo.Color || tipo == Tipo.Normal ? 2048 : 1024;
        ti.mipmapEnabled = true;
        ti.anisoLevel = tipo == Tipo.Color || tipo == Tipo.Normal ? 4 : 1;
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
