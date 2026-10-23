using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Arma en Norte lo que necesita FlujoNorte (pasos 1 a 7 del Acto 2):
//   - NpcGuia en el NPC (Wara), para que camine por las rutas.
//   - IglesiaEscaneo: cajas trigger sobre el templo y el campanario con la ficha de la iglesia
//     (el modelo de Isluga no tiene colliders, y el teléfono escanea por raycast).
//   - FlujoNorte con sus rutas: la de la iglesia, y desde PuntoTransportar (después del viaje)
//     las de la queñoa del Terrain, las vicuñas y PuntoPostal en los géiseres.
// Lo que ya existe no se toca, así que las rutas movidas a mano se conservan al volver a correrlo.
public static class FlujoNorteEditor
{
    // Rutas por defecto, en X/Z de mundo; la altura sale del Terrain.
    static readonly Vector2[] RutaIglesia = { new Vector2(-17f, -41f) };
    static readonly Vector2[] RutaQuenoa = { new Vector2(38f, -164f), new Vector2(57f, -159f) };
    static readonly Vector2[] RutaVicunas = { new Vector2(70f, -150f), new Vector2(84f, -133f), new Vector2(93f, -124f) };
    // Después del último punto, Wara sigue hasta PuntoPostalNPC.
    static readonly Vector2[] RutaPostal = { new Vector2(108f, -132f) };

    [MenuItem("Terrenos/Norte/Preparar flujo hasta la postal")]
    public static void Preparar()
    {
        var npc = GameObject.Find("NPC");
        var player = GameObject.Find("Player");
        var canvas = GameObject.Find("Canvas");
        var chaku = Object.FindFirstObjectByType<ChakuMinigameManager>(FindObjectsInactive.Include);
        if (npc == null || npc.GetComponent<Interactable>() == null || player == null || canvas == null)
        {
            Debug.LogError("Preparar flujo: falta NPC (con Interactable), Player o Canvas en la escena abierta.");
            return;
        }

        var guia = npc.GetComponent<NpcGuia>();
        if (guia == null) guia = Undo.AddComponent<NpcGuia>(npc);

        EscaneoIglesia();

        var raiz = GameObject.Find("FlujoNorte");
        if (raiz == null)
        {
            raiz = new GameObject("FlujoNorte");
            Undo.RegisterCreatedObjectUndo(raiz, "Flujo Norte");
        }
        var flujo = raiz.GetComponent<FlujoNorte>();
        if (flujo == null) flujo = Undo.AddComponent<FlujoNorte>(raiz);

        var so = new SerializedObject(flujo);
        so.FindProperty("wara").objectReferenceValue = npc.GetComponent<Interactable>();
        so.FindProperty("guia").objectReferenceValue = guia;
        so.FindProperty("jugador").objectReferenceValue = player.transform;
        so.FindProperty("chaku").objectReferenceValue = chaku;
        so.FindProperty("canvas").objectReferenceValue = canvas.GetComponent<RectTransform>();
        so.FindProperty("rutaIglesia").objectReferenceValue = Ruta(raiz.transform, "RutaIglesia", RutaIglesia);
        so.FindProperty("rutaQuenoa").objectReferenceValue = Ruta(raiz.transform, "RutaQuenoa", RutaQuenoa);
        so.FindProperty("rutaVicunas").objectReferenceValue = Ruta(raiz.transform, "RutaVicunas", RutaVicunas);
        so.FindProperty("rutaPostal").objectReferenceValue = Ruta(raiz.transform, "RutaPostal", RutaPostal);

        var punto = GameObject.Find("PuntoTransportar");
        if (punto == null) Debug.LogWarning("Preparar flujo: no hay PuntoTransportar en la escena; el viaje no mueve a nadie.");
        so.FindProperty("puntoTransportar").objectReferenceValue = punto != null ? punto.transform : null;
        var postalNpc = GameObject.Find("PuntoPostalNPC");
        if (postalNpc == null) Debug.LogWarning("Preparar flujo: no hay PuntoPostalNPC en la escena; Wara termina en el último punto de RutaPostal.");
        so.FindProperty("puntoPostalNpc").objectReferenceValue = postalNpc != null ? postalNpc.transform : null;

        var ocultar = so.FindProperty("ocultarAlViajar");
        if (ocultar.arraySize == 0)
        {
            var pueblo = new[] { GameObject.Find("Isluga"), GameObject.Find("IglesiaEscaneo") };
            foreach (var go in pueblo)
            {
                if (go == null) continue;
                ocultar.InsertArrayElementAtIndex(ocultar.arraySize);
                ocultar.GetArrayElementAtIndex(ocultar.arraySize - 1).objectReferenceValue = go;
            }
        }
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(raiz.scene);
        Selection.activeGameObject = raiz;
        Debug.Log("Flujo de Norte listo: FlujoNorte, rutas de Wara y escaneo de la iglesia. Guarda la escena.");
    }

    static Transform Ruta(Transform padre, string nombre, Vector2[] puntos)
    {
        var existente = padre.Find(nombre);
        if (existente != null) return existente;

        var ruta = new GameObject(nombre).transform;
        Undo.RegisterCreatedObjectUndo(ruta.gameObject, "Ruta");
        ruta.SetParent(padre, false);
        Terrain t = Terrain.activeTerrain;
        for (int i = 0; i < puntos.Length; i++)
        {
            var p = new GameObject("Punto" + (i + 1)).transform;
            p.SetParent(ruta, false);
            Vector3 w = new Vector3(puntos[i].x, 0f, puntos[i].y);
            if (t != null) w.y = t.SampleHeight(w) + t.transform.position.y;
            p.position = w;
        }
        return ruta;
    }

    static void EscaneoIglesia()
    {
        if (GameObject.Find("IglesiaEscaneo") != null) return;
        var templo = GameObject.Find("Isluga/Isluga_Iglesia");
        var torre = GameObject.Find("Isluga/Isluga_Torre");
        if (templo == null)
        {
            Debug.LogWarning("Preparar flujo: no encontré Isluga/Isluga_Iglesia; la iglesia queda sin ficha.");
            return;
        }

        var go = new GameObject("IglesiaEscaneo");
        Undo.RegisterCreatedObjectUndo(go, "Escaneo iglesia");
        Caja(go, templo);
        if (torre != null) Caja(go, torre);

        var s = go.AddComponent<Scannable>();
        var so = new SerializedObject(s);
        so.FindProperty("speciesId").stringValue = "iglesia_isluga";
        so.FindProperty("displayName").stringValue = "Iglesia de Isluga";
        so.FindProperty("scientificName").stringValue = "Monumento Histórico de Chile (1975)";
        so.FindProperty("description").stringValue =
            "Levantada en tiempos coloniales a 3.780 metros de altura, es una de las iglesias más emblemáticas del altiplano chileno. " +
            "Sus muros de piedra, blanqueados con cal, están rodeados por un muro que encierra el atrio, y su campanario de dos cuerpos, " +
            "de unos 12 metros, se alza separado del templo. El pueblo pasa casi vacío durante el año: las familias aymara vuelven para " +
            "sus fiestas, y un fabriquero la cuida todo el tiempo.";
        so.ApplyModifiedProperties();
    }

    // Trigger: el raycast del teléfono lo ve, pero no frena al jugador ni a la cámara.
    static void Caja(GameObject go, GameObject modelo)
    {
        var rs = modelo.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return;
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = b.center - go.transform.position;
        box.size = b.size;
    }
}
