using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Prepara los géiseres de la escena abierta:
//   1. Genera la textura del vapor (4 bocanadas en 2x2, blanco con alfa) si no existe.
//   2. Crea el material M_VaporGeiser (URP Particles/Unlit, transparente, partículas suaves).
//   3. Agrega GeiserController a cada instancia de Geiser.fbx que no lo tenga y le asigna el material.
// Se puede volver a correr después de poner más géiseres: los que ya están no se tocan.
public static class GeiseresNorte
{
    const string RutaModelo = "Assets/Models/Norte/Geiser.fbx";
    const string RutaTextura = "Assets/Textures/Geiser/T_VaporGeiser.png";
    const string RutaMaterial = "Assets/Materials/M_VaporGeiser.mat";
    const int Lado = 256; // de cada bocanada; la textura mide el doble

    [MenuItem("Terrenos/Norte/Preparar géiseres")]
    public static void Preparar()
    {
        var mat = Material();
        int nuevos = 0, total = 0;
        foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(mf.gameObject) != RutaModelo) continue;
            total++;
            var g = mf.GetComponent<GeiserController>();
            if (g == null)
            {
                g = Undo.AddComponent<GeiserController>(mf.gameObject);
                nuevos++;
            }
            var so = new SerializedObject(g);
            var prop = so.FindProperty("material");
            if (prop.objectReferenceValue == null)
            {
                prop.objectReferenceValue = mat;
                so.ApplyModifiedProperties();
            }
        }
        EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"Géiseres: {total} en la escena, {nuevos} con controlador nuevo. Material: {RutaMaterial}");
    }

    static Material Material()
    {
        if (!File.Exists(RutaTextura)) GenerarTextura();
        var mat = AssetDatabase.LoadAssetAtPath<Material>(RutaMaterial);
        if (mat != null) return mat;

        // Sin luz a propósito: con Simple Lit, a contraluz el vapor solo recibe el ambiente
        // del cielo y sale azul. La luz la pone GeiserController oscureciendo el color.
        mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(RutaTextura));
        mat.SetColor("_BaseColor", Color.white);

        // Transparente con mezcla alfa.
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", (float)CullMode.Off);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;

        // Partículas suaves (no cortan recto contra el suelo) y que se apaguen al pasar la cámara por adentro.
        mat.SetFloat("_SoftParticlesEnabled", 1f);
        mat.SetFloat("_SoftParticlesNearFadeDistance", 0f);
        mat.SetFloat("_SoftParticlesFarFadeDistance", 0.8f);
        mat.SetVector("_SoftParticleFadeParams", new Vector4(0f, 1f / 0.8f, 0f, 0f));
        mat.EnableKeyword("_SOFTPARTICLES_ON");
        mat.SetFloat("_CameraFadingEnabled", 1f);
        mat.SetFloat("_CameraNearFadeDistance", 0.4f);
        mat.SetFloat("_CameraFarFadeDistance", 1.5f);
        mat.SetVector("_CameraFadeParams", new Vector4(0.4f, 1f / (1.5f - 0.4f), 0f, 0f));
        mat.EnableKeyword("_FADING_ON");

        Directory.CreateDirectory(Path.GetDirectoryName(RutaMaterial));
        AssetDatabase.CreateAsset(mat, RutaMaterial);
        AssetDatabase.SaveAssets();
        return mat;
    }

    static void GenerarTextura()
    {
        // Cada bocanada: un círculo de borde blando comido por ruido fbm, así no se ve redondo.
        int n = Lado * 2;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color32[n * n];
        var azar = new System.Random(5);
        for (int tile = 0; tile < 4; tile++)
        {
            int ox = (tile % 2) * Lado, oy = (tile / 2) * Lado;
            float sx = (float)azar.NextDouble() * 100f, sy = (float)azar.NextDouble() * 100f;
            for (int y = 0; y < Lado; y++)
                for (int x = 0; x < Lado; x++)
                {
                    float u = (x + 0.5f) / Lado * 2f - 1f, v = (y + 0.5f) / Lado * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float f = 0f, a = 0.5f, fr = 2.5f;
                    for (int o = 0; o < 5; o++, a *= 0.5f, fr *= 2f)
                        f += a * Mathf.PerlinNoise(sx + u * fr, sy + v * fr);
                    // Borde a ~0.85 del radio, corrido por el ruido; el centro más denso que la orilla.
                    float borde = 1f - Suave(0.25f, 0.95f, r + (0.5f - f) * 0.7f);
                    float alfa = Mathf.Clamp01(borde * (0.55f + 0.9f * (f - 0.25f)));
                    alfa *= 1f - Suave(0.8f, 1f, r); // nada en las esquinas del cuadro
                    // Un poco más oscuro abajo: da volumen aunque el material sea con luz.
                    byte b = (byte)(255 * Mathf.Lerp(0.82f, 1f, Mathf.Clamp01(0.5f + v * 0.5f + (f - 0.5f))));
                    px[(oy + y) * n + ox + x] = new Color32(b, b, b, (byte)(255 * alfa));
                }
        }
        tex.SetPixels32(px);
        Directory.CreateDirectory(Path.GetDirectoryName(RutaTextura));
        File.WriteAllBytes(RutaTextura, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.Refresh();

        var imp = (TextureImporter)AssetImporter.GetAtPath(RutaTextura);
        imp.textureType = TextureImporterType.Default;
        imp.alphaIsTransparency = true;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.maxTextureSize = 512;
        imp.SaveAndReimport();
    }

    // smoothstep de shader (Mathf.SmoothStep interpola entre a y b, no es lo mismo).
    static float Suave(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }
}
