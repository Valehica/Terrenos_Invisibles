using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Prepara Norte, Centro y Sur para RendimientoOptimizacion.cs. Se puede correr las veces que haga falta.
//
//   - Static batching: marca "Batching Static" en los MeshRenderer que no se mueven nunca (casas, iglesia,
//     rocas de la canoa...). Unity los junta en mallas combinadas al entrar en Play y en el build.
//     No se marca nada que tenga Rigidbody, Animator, particulas o un script que no este en la lista de
//     ScriptsQuietos, ni materiales con shader propio (las hojas de la queñoa se mueven con viento en el
//     vertex shader y se romperian al combinarlas), ni lo que se genera sin guardarse (la pasarela).
//   - GPU instancing: "Enable GPU Instancing" en los materiales de la escena y de los arboles y detalles del
//     Terrain. Con el SRP Batcher encendido, URP lo usa en lo que el Batcher no cubre (detalles, arboles,
//     Graphics.RenderMeshInstanced de ArbolesVisibles).
//   - Pone en cada escena una instancia del prefab RendimientoOptimizacion llamada "RendimientoOptimización".
public static class RendimientoOptimizacionEditor
{
    const string RutaPrefab = "Assets/Prefabs/RendimientoOptimizacion.prefab";
    const string NombreObjeto = "RendimientoOptimización";
    static readonly string[] Escenas = { "Assets/Scenes/Norte.unity", "Assets/Scenes/Centro.unity", "Assets/Scenes/Sur.unity" };

    // Scripts que no mueven a su objeto ni a sus hijos: no impiden marcarlos como estaticos.
    static readonly HashSet<string> ScriptsQuietos = new HashSet<string>
    {
        "Scannable", "CanoaMinigameManager", "CanoaVientoCorriente",
    };

    static readonly HashSet<string> ShadersCombinables = new HashSet<string>
    {
        "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit",
        "Universal Render Pipeline/Baked Lit", "Universal Render Pipeline/Unlit",
    };

    [MenuItem("Terrenos/Rendimiento/Preparar escenas (estaticos, instancing y objeto)")]
    static void PrepararEscenas()
    {
        var prefab = ObtenerPrefab();
        var informe = new System.Text.StringBuilder("[Rendimiento] Escenas preparadas:\n");
        var activa = SceneManager.GetActiveScene().path;

        foreach (var ruta in Escenas)
        {
            var escena = SceneManager.GetSceneByPath(ruta);
            bool estabaAbierta = escena.isLoaded;
            if (!estabaAbierta) escena = EditorSceneManager.OpenScene(ruta, OpenSceneMode.Additive);

            int estaticos = MarcarEstaticos(escena, out int vertices);
            int materiales = ActivarInstancing(escena);
            bool creado = PonerObjeto(escena, prefab);

            EditorSceneManager.MarkSceneDirty(escena);
            EditorSceneManager.SaveScene(escena);
            informe.AppendLine($"  {escena.name}: {estaticos} renderers estaticos ({vertices:N0} vertices), " +
                               $"{materiales} materiales con instancing, objeto {(creado ? "creado" : "ya estaba")}");

            if (!estabaAbierta && ruta != activa) EditorSceneManager.CloseScene(escena, true);
        }

        AssetDatabase.SaveAssets();
        Debug.Log(informe.ToString());
    }

    static GameObject ObtenerPrefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RutaPrefab);
        if (prefab != null) return prefab;

        if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
        var go = new GameObject(NombreObjeto);
        var comp = go.AddComponent<RendimientoOptimizacion>();
        comp.capasExcluidas = LayerMask.GetMask("Cielo", "Water", "UI", "LimiteJugador");
        prefab = PrefabUtility.SaveAsPrefabAsset(go, RutaPrefab);
        Object.DestroyImmediate(go);
        return prefab;
    }

    static bool PonerObjeto(Scene escena, GameObject prefab)
    {
        foreach (var raiz in escena.GetRootGameObjects())
            if (raiz.GetComponentInChildren<RendimientoOptimizacion>(true) != null) return false;

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, escena);
        go.name = NombreObjeto;
        return true;
    }

    static int MarcarEstaticos(Scene escena, out int vertices)
    {
        int marcados = 0;
        vertices = 0;
        int capasFuera = LayerMask.GetMask("Cielo", "Water");

        foreach (var raiz in escena.GetRootGameObjects())
        foreach (var r in raiz.GetComponentsInChildren<MeshRenderer>(true))
        {
            var go = r.gameObject;
            var filtro = r.GetComponent<MeshFilter>();
            if (filtro == null || filtro.sharedMesh == null) continue;
            if ((go.hideFlags & HideFlags.DontSaveInEditor) != 0) continue;
            if (((1 << go.layer) & capasFuera) != 0) continue;
            if (r.sharedMaterials.Any(m => m == null || !ShadersCombinables.Contains(m.shader.name))) continue;
            if (!EsQuieto(go.transform)) continue;

            var flags = GameObjectUtility.GetStaticEditorFlags(go);
            if ((flags & StaticEditorFlags.BatchingStatic) == 0)
                GameObjectUtility.SetStaticEditorFlags(go, flags | StaticEditorFlags.BatchingStatic);
            marcados++;
            vertices += filtro.sharedMesh.vertexCount;
        }
        return marcados;
    }

    static bool EsQuieto(Transform t)
    {
        for (; t != null; t = t.parent)
        {
            if ((t.gameObject.hideFlags & HideFlags.DontSaveInEditor) != 0) return false;
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;   // script perdido: mejor no arriesgar
                if (c is Rigidbody || c is Animator || c is Animation || c is ParticleSystem || c is Cloth) return false;
                if (c is MonoBehaviour && !ScriptsQuietos.Contains(c.GetType().Name)) return false;
            }
        }
        return true;
    }

    static int ActivarInstancing(Scene escena)
    {
        var materiales = new HashSet<Material>();
        foreach (var raiz in escena.GetRootGameObjects())
        {
            foreach (var r in raiz.GetComponentsInChildren<Renderer>(true))
                if (!(r is ParticleSystemRenderer)) materiales.UnionWith(r.sharedMaterials.Where(m => m != null));

            foreach (var t in raiz.GetComponentsInChildren<Terrain>(true))
            {
                var datos = t.terrainData;
                if (datos == null) continue;
                foreach (var a in datos.treePrototypes)
                    if (a.prefab != null) materiales.UnionWith(a.prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null));
                foreach (var d in datos.detailPrototypes)
                    if (d.usePrototypeMesh && d.prototype != null) materiales.UnionWith(d.prototype.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null));
            }
        }

        int cambiados = 0;
        foreach (var m in materiales)
        {
            if (m.enableInstancing) continue;
            var ruta = AssetDatabase.GetAssetPath(m);
            // Solo materiales sueltos del proyecto: los de los paquetes y los que vienen dentro de un FBX no se pueden editar.
            if (string.IsNullOrEmpty(ruta) || !ruta.StartsWith("Assets/") || !ruta.EndsWith(".mat")) continue;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            cambiados++;
        }
        return cambiados;
    }
}
