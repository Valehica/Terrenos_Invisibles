using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Rendering;

// Arbustos de Yughues Free Bushes (P_Bush01/03/04/05; el 02 es rojo y queda afuera) como arboles del Terrain de
// Centro, para tupir el bosque de arrayanes y olivillos. Los prefabs no tienen collider, asi que el Terrain no les
// pone colision.
//
// Menu Terrenos > Centro > Arbustos en la zona verde:
//   1. Pasa los materiales M_Bush* (Standard, rosados en URP) a URP/Lit con recorte, dos caras e instancing.
//   2. Agrega los prefabs como prototipos del Terrain.
//   3. Borra los arbustos que puso una corrida anterior y reparte de nuevo: solo en la zona verde (capas de pasto y
//      musgo, sin arena ni roca, sobre la orilla y en pendientes suaves), mas tupido cerca de los arboles y en
//      manchas. La semilla es fija: la misma corrida da el mismo resultado.
//   4. Pone ArbolesVisibles en el Terrain si no estaba (instancing + culling; los arbustos usan su ultimo LOD de
//      lejos y se cortan donde lo haria su LODGroup).
public static class ArbustosCentro
{
    const string Carpeta = "Assets/YughuesFreeBushes2018";
    static readonly int[] Arbustos = { 1, 3, 4, 5 };

    // Reparto
    const float Paso = 3f;              // grilla con jitter, en metros
    const float ProbBase = 0.10f;       // zona verde lejos del bosque
    const float ProbBosque = 0.60f;     // se suma cerca de los arboles
    const float CercaBosque = 10f;      // hasta aca cuenta como dentro del bosque
    const float LejosBosque = 28f;      // desde aca ya no suma
    const float LibreTronco = 1.5f;     // no pegados a un tronco
    const float PendienteMax = 35f;
    const float AlturaMin = 2f;         // sobre la orilla

    [MenuItem("Terrenos/Centro/Arbustos en la zona verde")]
    static void Repartir()
    {
        var terreno = Terrain.activeTerrain;
        if (terreno == null) { Debug.LogError("No hay Terrain activo."); return; }

        var prefabs = new List<GameObject>();
        foreach (int i in Arbustos)
        {
            PasarAUrp(AssetDatabase.LoadAssetAtPath<Material>($"{Carpeta}/Materials/M_Bush0{i}.mat"));
            prefabs.Add(AssetDatabase.LoadAssetAtPath<GameObject>($"{Carpeta}/Prefabs/P_Bush0{i}.prefab"));
        }
        AssetDatabase.SaveAssets();

        var td = terreno.terrainData;
        Undo.RegisterCompleteObjectUndo(td, "Arbustos en la zona verde");

        // Prototipos
        var protos = new List<TreePrototype>(td.treePrototypes);
        var indice = new int[prefabs.Count];
        for (int k = 0; k < prefabs.Count; k++)
        {
            indice[k] = protos.FindIndex(p => p.prefab == prefabs[k]);
            if (indice[k] < 0) { protos.Add(new TreePrototype { prefab = prefabs[k] }); indice[k] = protos.Count - 1; }
        }
        td.treePrototypes = protos.ToArray();
        var esArbusto = new HashSet<int>(indice);

        // Se conservan los arboles; los arbustos de antes se rehacen.
        var arboles = new List<TreeInstance>();
        foreach (var t in td.treeInstances) if (!esArbusto.Contains(t.prototypeIndex)) arboles.Add(t);
        var cercania = new Cercania(arboles, td.size, LejosBosque);

        int aw = td.alphamapWidth, ah = td.alphamapHeight;
        float[,,] a = td.GetAlphamaps(0, 0, aw, ah);
        Vector3 tam = td.size;
        float y0 = terreno.transform.position.y;
        var azar = new System.Random(20261013);
        var nuevos = new List<TreeInstance>(arboles);
        int puestos = 0;

        for (float z = Paso * 0.5f; z < tam.z; z += Paso)
            for (float x = Paso * 0.5f; x < tam.x; x += Paso)
            {
                float px = x + ((float)azar.NextDouble() - 0.5f) * Paso;
                float pz = z + ((float)azar.NextDouble() - 0.5f) * Paso;
                float tirada = (float)azar.NextDouble();
                float nx = Mathf.Clamp01(px / tam.x), nz = Mathf.Clamp01(pz / tam.z);

                int ax = Mathf.Min((int)(nx * aw), aw - 1), az = Mathf.Min((int)(nz * ah), ah - 1);
                float pasto = a[az, ax, 0] + a[az, ax, 1] + a[az, ax, 2] + a[az, ax, 3];
                float suelo = a[az, ax, 4] + a[az, ax, 5] + a[az, ax, 6] + a[az, ax, 7];
                if (pasto < 0.5f || suelo > 0.35f) continue;
                if (td.GetInterpolatedHeight(nx, nz) + y0 < AlturaMin) continue;
                float pendiente = td.GetSteepness(nx, nz);
                if (pendiente > PendienteMax) continue;

                float d = cercania.Distancia(px, pz);
                if (d < LibreTronco) continue;
                float bosque = Mathf.InverseLerp(LejosBosque, CercaBosque, d);
                float manchas = Mathf.PerlinNoise(px * 0.035f + 41.3f, pz * 0.035f + 17.9f);
                float prob = (ProbBase + ProbBosque * bosque) * Mathf.Lerp(0.35f, 1.4f, manchas)
                             * Mathf.InverseLerp(PendienteMax, PendienteMax * 0.6f, pendiente);
                if (tirada > prob) continue;

                float escala = Mathf.Lerp(1.2f, 2.0f, (float)azar.NextDouble()) * Mathf.Lerp(1f, 1.3f, bosque);
                nuevos.Add(new TreeInstance
                {
                    prototypeIndex = indice[azar.Next(indice.Length)],
                    position = new Vector3(nx, 0f, nz),
                    widthScale = escala * Mathf.Lerp(0.9f, 1.15f, (float)azar.NextDouble()),
                    heightScale = escala,
                    rotation = (float)azar.NextDouble() * Mathf.PI * 2f,
                    color = Color.white,
                    lightmapColor = Color.white,
                });
                puestos++;
            }

        td.SetTreeInstances(nuevos.ToArray(), true);
        terreno.Flush();
        EditorUtility.SetDirty(td);

        if (terreno.GetComponent<ArbolesVisibles>() == null)
        {
            Undo.AddComponent<ArbolesVisibles>(terreno.gameObject);
            Debug.Log("ArbolesVisibles agregado al Terrain.");
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(terreno.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"Arbustos: {puestos} puestos ({arboles.Count} arboles conservados).");
    }

    static void PasarAUrp(Material m)
    {
        if (m == null) return;
        var color = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
        var normal = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
        var ao = m.HasProperty("_OcclusionMap") ? m.GetTexture("_OcclusionMap") : null;
        float corte = m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.5f;
        if (m.shader.name != "Universal Render Pipeline/Lit")
        {
            Undo.RecordObject(m, "Arbustos a URP");
            m.shader = Shader.Find("Universal Render Pipeline/Lit");
        }
        if (color != null) m.SetTexture("_BaseMap", color);
        if (normal != null) m.SetTexture("_BumpMap", normal);
        if (ao != null) m.SetTexture("_OcclusionMap", ao);
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_WorkflowMode", 1f);    // metalico; el especular del pack era casi negro
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Smoothness", 0.1f);
        m.SetFloat("_AlphaClip", 1f);
        m.SetFloat("_Cutoff", corte);
        m.SetFloat("_Cull", (float)CullMode.Off);
        m.enableInstancing = true;
        BaseShaderGUI.SetMaterialKeywords(m, LitGUI.SetMaterialKeywords);
        EditorUtility.SetDirty(m);

        // sin esto el alfa se lava en los mips y de lejos el arbusto se pela
        if (color != null && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(color)) is TextureImporter ti && !ti.mipMapsPreserveCoverage)
        {
            ti.mipMapsPreserveCoverage = true;
            ti.alphaTestReferenceValue = corte;
            ti.SaveAndReimport();
        }
    }

    // Distancia al arbol mas cercano (en metros, plano XZ), con una grilla de cubetas.
    class Cercania
    {
        readonly Dictionary<long, List<Vector2>> cubetas = new Dictionary<long, List<Vector2>>();
        readonly float lado, tope;

        public Cercania(List<TreeInstance> arboles, Vector3 tam, float tope)
        {
            this.tope = tope;
            lado = tope;
            foreach (var t in arboles)
            {
                var p = new Vector2(t.position.x * tam.x, t.position.z * tam.z);
                long k = Clave(Mathf.FloorToInt(p.x / lado), Mathf.FloorToInt(p.y / lado));
                if (!cubetas.TryGetValue(k, out var l)) cubetas[k] = l = new List<Vector2>();
                l.Add(p);
            }
        }

        static long Clave(int x, int z) => ((long)x << 32) ^ (uint)z;

        public float Distancia(float x, float z)
        {
            int cx = Mathf.FloorToInt(x / lado), cz = Mathf.FloorToInt(z / lado);
            float mejor = tope * tope;
            var q = new Vector2(x, z);
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if (cubetas.TryGetValue(Clave(cx + dx, cz + dz), out var l))
                        foreach (var p in l) mejor = Mathf.Min(mejor, (p - q).sqrMagnitude);
            return Mathf.Sqrt(mejor);
        }
    }
}
