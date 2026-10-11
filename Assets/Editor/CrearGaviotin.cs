using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// Arma el gaviotín de Centro a partir del FBX que sale de Blender:
//   1. Renombra los clips (rig.001|Idle -> Idle) y deja en loop Idle y Planear.
//   2. Crea GaviotinController con un estado por clip, sin transiciones:
//      GaviotinBehavior los cambia con CrossFade.
//   3. Crea el prefab Gaviotin (modelo a escala real, colisionador para el
//      escáner, capa de render "Personajes" para el pase de estilo).
//   4. Pone una bandada de gaviotines en la costa de Centro, al suroeste del spawn.
// Se puede re-correr después de re-exportar el FBX: actualiza todo sin
// duplicar nada en la escena.
public static class CrearGaviotin
{
    const string RutaFbx = "Assets/Models/Centro/Gaviotin/Gabiortin.fbx";
    const string RutaControlador = "Assets/Animations/GaviotinController.controller";
    const string RutaPrefab = "Assets/Prefab/Gaviotin.prefab";
    const string Escena = "Assets/Scenes/Centro.unity";

    static readonly string[] Clips = { "Idle", "Rascar_Panza", "Gorjear", "Planear", "Despegue", "Aterrizaje" };
    static readonly string[] Ciclicos = { "Idle", "Planear" };

    // El modelo mide 3,54 m de pico a cola; un gaviotín real mide unos 42 cm.
    const float LargoModelo = 3.54f;
    const float LargoReal = 0.42f;
    const uint CapaRenderPersonajes = 1u << 8;

    // Costa de Centro al suroeste del spawn del jugador (-12.5, -46).
    static readonly Vector3 CentroZona = new Vector3(-10f, 0f, -62f);
    const int Cantidad = 10;

    [MenuItem("Terrenos/Centro/Crear gaviotines")]
    static void Crear()
    {
        if (!ConfigurarClips()) return;
        var controlador = CrearControlador();
        if (controlador == null) return;
        var prefab = CrearPrefab(controlador);
        Colocar(prefab);
    }

    static bool ConfigurarClips()
    {
        var imp = AssetImporter.GetAtPath(RutaFbx) as ModelImporter;
        if (imp == null) { Debug.LogError("No encuentro " + RutaFbx); return false; }

        var tomas = imp.defaultClipAnimations;
        var clips = new System.Collections.Generic.List<ModelImporterClipAnimation>();
        foreach (var nombre in Clips)
        {
            var toma = tomas.FirstOrDefault(t => t.takeName == nombre || t.takeName.EndsWith("|" + nombre));
            if (toma == null) { Debug.LogError($"El FBX no trae la acción {nombre}."); return false; }
            toma.name = nombre;
            toma.loopTime = Ciclicos.Contains(nombre);
            clips.Add(toma);
        }
        imp.clipAnimations = clips.ToArray();
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        imp.SaveAndReimport();
        return true;
    }

    static AnimatorController CrearControlador()
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(RutaFbx).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview")).ToDictionary(c => c.name);

        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(RutaControlador);
        if (ctrl == null) ctrl = AnimatorController.CreateAnimatorControllerAtPath(RutaControlador);

        var sm = ctrl.layers[0].stateMachine;
        foreach (var s in sm.states.ToArray()) sm.RemoveState(s.state);

        for (int i = 0; i < Clips.Length; i++)
        {
            if (!clips.TryGetValue(Clips[i], out var clip)) { Debug.LogError("Falta el clip " + Clips[i]); return null; }
            var estado = sm.AddState(Clips[i], new Vector3(300f, 60f * i, 0f));
            estado.motion = clip;
            if (Clips[i] == "Idle") sm.defaultState = estado;
        }
        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        return ctrl;
    }

    static GameObject CrearPrefab(AnimatorController controlador)
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(RutaFbx);
        var raiz = new GameObject("Gaviotin");

        var modelo = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        modelo.name = "Modelo";
        modelo.transform.SetParent(raiz.transform, false);
        modelo.transform.localScale = Vector3.one * (LargoReal / LargoModelo);

        var animator = modelo.GetComponent<Animator>();
        if (animator == null) animator = modelo.AddComponent<Animator>();
        animator.runtimeAnimatorController = controlador;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        modelo.AddComponent<GaviotinShapeKeys>();

        foreach (var r in modelo.GetComponentsInChildren<Renderer>())
            r.renderingLayerMask |= CapaRenderPersonajes;

        // El escáner tira un rayo que acepta triggers; el jugador lo atraviesa.
        var col = raiz.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 0.3f;
        col.center = new Vector3(0f, 0.15f, 0f);

        var escaneable = new SerializedObject(raiz.AddComponent<Scannable>());
        escaneable.FindProperty("speciesId").stringValue = "gaviotin";
        escaneable.FindProperty("displayName").stringValue = "Gaviotín";
        escaneable.FindProperty("scientificName").stringValue = "Sterna hirundinacea";
        escaneable.FindProperty("description").stringValue =
            "Ave ágil y activa que suele desplazarse cerca de zonas costeras y cuerpos de agua. " +
            "Se mantiene en constante movimiento, alternando entre volar, posarse y buscar alimento. " +
            "Ante la presencia de personas, puede alejarse volando rápidamente.";
        escaneable.ApplyModifiedPropertiesWithoutUndo();

        var comportamiento = new SerializedObject(raiz.AddComponent<GaviotinBehavior>());
        comportamiento.FindProperty("capasSuelo").intValue = ~LayerMask.GetMask(
            "TransparentFX", "Ignore Raycast", "Water", "UI", "Suimono_Water", "Suimono_Depth", "Suimono_Screen", "Cielo");
        comportamiento.ApplyModifiedPropertiesWithoutUndo();

        var prefab = PrefabUtility.SaveAsPrefabAsset(raiz, RutaPrefab);
        Object.DestroyImmediate(raiz);
        return prefab;
    }

    static void Colocar(GameObject prefab)
    {
        var escena = EditorSceneManager.GetActiveScene();
        if (escena.path != Escena)
        {
            Debug.Log($"Prefab listo en {RutaPrefab}. Abrí {Escena} y volvé a correr el menú para poner la bandada.");
            return;
        }

        var viejo = GameObject.Find("Gaviotines");
        if (viejo != null) Undo.DestroyObjectImmediate(viejo);

        var grupo = new GameObject("Gaviotines");
        Undo.RegisterCreatedObjectUndo(grupo, "Crear gaviotines");
        var zona = new GameObject("CentroZona").transform;
        zona.SetParent(grupo.transform, false);
        var terreno = Terrain.activeTerrain;
        Vector3 c = CentroZona;
        if (terreno != null) c.y = terreno.SampleHeight(c) + terreno.transform.position.y;
        zona.position = c;

        for (int i = 0; i < Cantidad; i++)
        {
            var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab, escena);
            g.name = "Gaviotin_" + (i + 1);
            g.transform.SetParent(grupo.transform, true);
            // Espiral de ángulo áureo: quedan repartidos sin amontonarse. Al dar Play
            // cada uno se va al posadero libre más cercano.
            float radio = Mathf.Lerp(6f, 30f, Mathf.Sqrt((i + 0.5f) / Cantidad));
            Vector3 p = c + Quaternion.Euler(0f, i * 137.5f, 0f) * new Vector3(0f, 0f, radio);
            if (terreno != null) p.y = terreno.SampleHeight(p) + terreno.transform.position.y;
            g.transform.position = p;

            var so = new SerializedObject(g.GetComponent<GaviotinBehavior>());
            so.FindProperty("centroZona").objectReferenceValue = zona;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorSceneManager.MarkSceneDirty(escena);
        Selection.activeGameObject = grupo;
        Debug.Log($"{Cantidad} gaviotines en Centro, alrededor de {c}. Guardá la escena para conservarlos.");
    }
}
