using UnityEditor;
using UnityEngine;

// Arma M_Vicuna (URP Lit) con las texturas que salen de Tools/Blender/vicuna_componer.py
// y lo asigna a los SkinnedMeshRenderer del prefab de la vicuña.
//   T_Vicuna_Albedo  2048, sRGB, costuras mezcladas en 3D + detalle de pelo
//   T_Vicuna_Normal  2048, tangent space (MikkTSpace, como importa el FBX)
//   T_Vicuna_AO      1024, oclusion de la pose de reposo
//   T_Vicuna_MetalSmooth 1024, A = smoothness (pelo 0.12, ojos 0.9, nariz 0.5)
public static class AplicarTexturaVicuna
{
    const string Carpeta = "Assets/Models/Norte/Vicuña";
    const string RutaMaterial = Carpeta + "/M_Vicuna.mat";
    const string RutaPrefab = "Assets/Prefab/Vicuña.prefab";

    [MenuItem("Terrenos/Norte/Aplicar textura de la vicuña")]
    static void Aplicar()
    {
        var albedo = Importar("T_Vicuna_Albedo", TextureImporterType.Default, true, 2048);
        var normal = Importar("T_Vicuna_Normal", TextureImporterType.NormalMap, false, 2048);
        var ao = Importar("T_Vicuna_AO", TextureImporterType.Default, false, 1024);
        var brillo = Importar("T_Vicuna_MetalSmooth", TextureImporterType.Default, false, 1024);
        if (albedo == null || normal == null || ao == null || brillo == null) return;

        var mat = AssetDatabase.LoadAssetAtPath<Material>(RutaMaterial);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, RutaMaterial);
        }
        mat.SetTexture("_BaseMap", albedo);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetTexture("_BumpMap", normal);
        mat.SetFloat("_BumpScale", 1f);
        mat.EnableKeyword("_NORMALMAP");
        mat.SetTexture("_OcclusionMap", ao);
        mat.SetFloat("_OcclusionStrength", 0.8f);
        mat.EnableKeyword("_OCCLUSIONMAP");
        mat.SetTexture("_MetallicGlossMap", brillo);
        mat.EnableKeyword("_METALLICSPECGLOSSMAP");
        mat.SetFloat("_SmoothnessTextureChannel", 0f);   // alpha del mapa metalico
        mat.SetFloat("_Smoothness", 1f);                  // con mapa, multiplica al alpha
        mat.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(mat);

        var raiz = PrefabUtility.LoadPrefabContents(RutaPrefab);
        int n = 0;
        foreach (var r in raiz.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats.Length > 0 ? mats : new[] { mat };
            n++;
        }
        PrefabUtility.SaveAsPrefabAsset(raiz, RutaPrefab);
        PrefabUtility.UnloadPrefabContents(raiz);
        AssetDatabase.SaveAssets();
        Debug.Log($"Vicuña: M_Vicuna asignado a {n} renderer(s) del prefab.");
    }

    static Texture2D Importar(string nombre, TextureImporterType tipo, bool srgb, int max)
    {
        string ruta = $"{Carpeta}/{nombre}.png";
        var imp = AssetImporter.GetAtPath(ruta) as TextureImporter;
        if (imp == null) { Debug.LogError("Falta " + ruta); return null; }
        imp.textureType = tipo;
        imp.sRGBTexture = srgb;
        imp.maxTextureSize = max;
        imp.mipmapEnabled = true;
        imp.anisoLevel = 4;
        imp.textureCompression = TextureImporterCompression.CompressedHQ;
        imp.alphaSource = TextureImporterAlphaSource.FromInput;
        imp.alphaIsTransparency = false;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(ruta);
    }
}
