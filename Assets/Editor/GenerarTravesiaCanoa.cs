using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Arma el minijuego de la canoa en la escena Sur: ruta, rocas por tramo,
/// canoa, faro de llegada y manager, y lo conecta al NPC y a la Main Camera.
///
/// Se apoya en los dos marcadores puestos a mano en la escena:
/// "Puerto Eden" (salida) y "Parque ohiggins" (llegada). Entre ellos la ruta
/// bordea por el oeste la costa de la península; los puntos intermedios se
/// eligieron midiendo la profundidad del terreno (todos con agua a -4 m o más).
///
/// Se puede volver a correr: borra lo que generó antes y lo rehace igual
/// (las rocas salen de una semilla fija).
/// </summary>
public static class GenerarTravesiaCanoa
{
    private const string RootName = "MinijuegoCanoa";
    private const string AssetFolder = "Assets/Minigame/Canoa";
    private const string RocksFolder = "Assets/FlipGameDev/Terrain&GrassPack/Art/Meshes/Rocks/";
    private const float WaterY = 0f;
    private const int Seed = 20260921;

    // Puntos intermedios de la ruta (XZ). El primero es la salida, 20 m mar
    // adentro del marcador de Puerto Edén, para que la canoa no nazca en la orilla.
    private static readonly Vector2[] Via =
    {
        new Vector2(-122f, -282f),
        new Vector2(-160f, -150f),
        new Vector2(-215f, 0f),
        new Vector2(-228f, 150f),
        new Vector2(-212f, 262f),
        new Vector2(-245f, 380f),
    };

    // Rocas bajas/anchas y agujas (los "picos" para los pasos estrechos).
    private static readonly int[] BlockRocks = { 1, 3, 4, 8, 9, 12 };
    private static readonly int[] SpireRocks = { 2, 5, 10, 11 };

    private const float CorridorHalfWidth = 26f;
    // Límite duro de la canoa: en agua abierta, y en los canales (medido desde el
    // centro del paso) un poco detrás de la cara interna de las murallas, para que
    // se choque con las rocas antes que con el muro invisible y no se pueda salir
    // por entre dos rocas.
    private const float OpenHalfWidth = 22f;
    private const float WallBoundaryExtra = 3.2f;
    private const float Gap3 = 5.2f, Gap5 = 5f;
    private const float C3a = 312f, C3b = 440f, C5a = 668f, C5b = 760f;

    private static System.Random rng;
    private static Transform obstacles;
    private static readonly Dictionary<int, GameObject> rockCache = new Dictionary<int, GameObject>();
    private static readonly List<Vector3> pts = new List<Vector3>();
    private static readonly List<float> cum = new List<float>();
    private static int placed, skippedOnLand;

    [MenuItem("Terrenos/Sur/Generar travesia en canoa")]
    public static void Generar()
    {
        var start = GameObject.Find("Puerto Eden");
        var end = GameObject.Find("Parque ohiggins");
        if (start == null || end == null)
        {
            EditorUtility.DisplayDialog("Travesía en canoa",
                "No encontré los marcadores \"Puerto Eden\" y \"Parque ohiggins\" en la escena abierta.", "OK");
            return;
        }

        var old = GameObject.Find(RootName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        EnsureFolder(AssetFolder);
        rng = new System.Random(Seed);
        rockCache.Clear();
        placed = skippedOnLand = 0;

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Generar travesía en canoa");

        // ---- Ruta
        var route = new GameObject("Ruta").transform;
        route.SetParent(root.transform, false);
        pts.Clear();
        cum.Clear();
        for (int i = 0; i < Via.Length; i++) AddPoint(route, new Vector3(Via[i].x, WaterY, Via[i].y), i);
        Vector3 endPos = end.transform.position;
        AddPoint(route, new Vector3(endPos.x, WaterY, endPos.z), Via.Length);

        // ---- Obstáculos
        obstacles = new GameObject("Obstaculos").transform;
        obstacles.SetParent(root.transform, false);
        var tramos = BuildObstacles();

        // ---- Ruta fina: cada 4 m, por el centro real de los canales, con el ancho
        // que se permite a cada lado. Es el límite duro del minijuego.
        var halfWidths = BuildFineRoute(route, tramos);
        Transform startPoint = route.GetChild(0);

        // ---- Canoa, faro y manager
        var canoe = BuildCanoe(root.transform, startPoint.position);
        var beacon = BuildBeacon(root.transform, endPos, out Renderer beaconRenderer, out Light beaconLight);
        var landing = BuildLanding(root.transform, endPos);

        var manager = root.AddComponent<CanoaMinigameManager>();
        var clima = root.AddComponent<CanoaVientoCorriente>();
        var cso = new SerializedObject(clima);
        cso.FindProperty("foamMaterial").objectReferenceValue = SaveFoamMaterial();
        cso.ApplyModifiedPropertiesWithoutUndo();

        var mainCam = Camera.main;
        CanoaCamera canoeCam = null;
        var camBehaviours = new List<Behaviour>();
        if (mainCam != null)
        {
            canoeCam = mainCam.GetComponent<CanoaCamera>();
            if (canoeCam == null) canoeCam = Undo.AddComponent<CanoaCamera>(mainCam.gameObject);
            canoeCam.enabled = false;
            var so = new SerializedObject(canoeCam);
            so.FindProperty("target").objectReferenceValue = canoe;
            so.ApplyModifiedPropertiesWithoutUndo();

            foreach (var b in mainCam.GetComponents<MonoBehaviour>())
                if (b != null && !(b is CanoaCamera) && b.GetType().Assembly == typeof(CanoaCamera).Assembly)
                    camBehaviours.Add(b);
        }

        GameObject player = null;
        foreach (var r in root.scene.GetRootGameObjects())
            if (r.name == "Player") player = r;

        var mso = new SerializedObject(manager);
        mso.FindProperty("canoe").objectReferenceValue = canoe;
        mso.FindProperty("clima").objectReferenceValue = clima;
        mso.FindProperty("startPoint").objectReferenceValue = startPoint;
        mso.FindProperty("route").objectReferenceValue = route;
        mso.FindProperty("destination").objectReferenceValue = end.transform;
        mso.FindProperty("corridorHalfWidth").floatValue = OpenHalfWidth;
        var hwProp = mso.FindProperty("routeHalfWidths");
        hwProp.arraySize = halfWidths.Count;
        for (int i = 0; i < halfWidths.Count; i++) hwProp.GetArrayElementAtIndex(i).floatValue = halfWidths[i];
        mso.FindProperty("beaconRenderer").objectReferenceValue = beaconRenderer;
        mso.FindProperty("beaconLight").objectReferenceValue = beaconLight;
        mso.FindProperty("explorationPlayer").objectReferenceValue = player;
        mso.FindProperty("playerArrivalPoint").objectReferenceValue = landing;
        mso.FindProperty("mainCamera").objectReferenceValue = mainCam;
        mso.FindProperty("canoeCamera").objectReferenceValue = canoeCam;
        var cb = mso.FindProperty("explorationCameraBehaviours");
        cb.arraySize = camBehaviours.Count;
        for (int i = 0; i < camBehaviours.Count; i++) cb.GetArrayElementAtIndex(i).objectReferenceValue = camBehaviours[i];
        var tp = mso.FindProperty("tramos");
        tp.arraySize = tramos.Count;
        for (int i = 0; i < tramos.Count; i++)
        {
            var e = tp.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("nombre").stringValue = tramos[i].nombre;
            e.FindPropertyRelative("desde").floatValue = tramos[i].desde;
        }
        mso.ApplyModifiedPropertiesWithoutUndo();

        // ---- El NPC de Puerto Edén lanza la travesía.
        var npc = GameObject.Find("NPC");
        var interactable = npc != null ? npc.GetComponent<Interactable>() : null;
        if (interactable != null)
        {
            var iso = new SerializedObject(interactable);
            iso.FindProperty("startsMinigame").boolValue = true;
            iso.FindProperty("minigame").objectReferenceValue = manager;
            string text = iso.FindProperty("dialogueText").stringValue;
            if (string.IsNullOrEmpty(text) || text.Contains("chaku"))
                iso.FindProperty("dialogueText").stringValue =
                    "Maite, mi familia siempre navegó estos canales. Hoy tengo que llevar la canoa hasta la isla del parque, " +
                    "pero el paso entre las rocas es difícil. ¿Me ayudas a remar la travesía?";
            iso.ApplyModifiedProperties();
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;

        float routeLen = cum[cum.Count - 1];
        Debug.Log($"[Canoa] Travesía generada: ruta de {routeLen:F0} m, {placed} rocas ({skippedOnLand} descartadas por caer en tierra). " +
                  $"NPC conectado: {(interactable != null ? "sí" : "no")}. Guarda la escena.");
    }

    private static void AddPoint(Transform route, Vector3 p, int index)
    {
        if (pts.Count > 0) cum.Add(cum[cum.Count - 1] + Vector3.Distance(pts[pts.Count - 1], p));
        else cum.Add(0f);
        pts.Add(p);
    }

    // =================================================================== Obstáculos

    private static List<CanoaMinigameManager.Tramo> BuildObstacles()
    {
        var tramos = new List<CanoaMinigameManager.Tramo>();
        void Tramo(string n, float s) => tramos.Add(new CanoaMinigameManager.Tramo { nombre = n, desde = s });

        // Tramo 1 — Salida: canal amplio para aprender los controles, con un par de
        // rocas a los lados para anticipar.
        Tramo("Salida desde Puerto Edén", 0f);
        Rock(At(60f, -18f), 6f, false, "T1");
        Rock(At(85f, 14f), 4.5f, false, "T1");
        Rock(At(100f, -9f), 3.5f, true, "T1");

        // Tramo 2 — Rocas aisladas, cada 14–18 m y casi siempre sobre la línea de la
        // ruta: hay que corregir el rumbo todo el tiempo.
        Tramo("Primer sector de rocas", 110f);
        float s2 = 122f;
        int k2 = 0;
        while (s2 < 280f)
        {
            float x = (k2 % 2 == 0 ? 1f : -1f) * RandomRange(0f, 6f);
            Rock(At(s2, x), RandomRange(4.5f, 7f), k2 % 3 == 2, "T2");
            if (k2 % 2 == 1)
                Rock(At(s2 + RandomRange(-2f, 2f), x + (x > 0 ? 1 : -1) * RandomRange(4f, 5.5f)), RandomRange(2.5f, 4f), false, "T2");
            s2 += RandomRange(14f, 18f);
            k2++;
        }

        // Tramo 3 — Canal estrecho: dos murallas con un paso de ~10 m que hace una S
        // marcada, y agujas sueltas dentro del canal que dejan apenas espacio por un lado.
        Tramo("Canal estrecho", 290f);
        const float c3a = C3a, c3b = C3b, gap3 = Gap3;
        Funnel(292f, c3a, CorridorHalfWidth, gap3, Center3(c3a), "T3");
        Wall(c3a, c3b, Center3, gap3, 3.6f, "T3");
        float[] spire3 = { 340f, 368f, 396f, 422f };
        for (int i = 0; i < spire3.Length; i++)
            Rock(At(spire3[i], Center3(spire3[i]) + (i % 2 == 0 ? 1.6f : -1.6f)), 2.4f, true, "T3");

        // Tramo 4 — Navegación compleja: siete filas de grupos de rocas, cada 26 m,
        // con pasos de ~9 m que saltan de un lado al otro; algunas filas tienen uno solo.
        Tramo("Zona de navegación compleja", 460f);
        float[] rowS = { 470f, 496f, 522f, 548f, 574f, 600f, 626f };
        float[][] rowGaps =
        {
            new[] { -14f, 12f },
            new[] { 2f },
            new[] { -18f, 16f },
            new[] { -4f },
            new[] { 14f, -20f },
            new[] { -10f },
            new[] { 18f, 0f },
        };
        for (int r = 0; r < rowS.Length; r++)
        {
            Row(rowS[r], rowGaps[r], 4.6f, 5f, "T4");
            // Entre filas, una roca justo delante de uno de los pasos: obliga a entrar en diagonal.
            if (r < rowS.Length - 1)
            {
                float mid = (rowS[r] + rowS[r + 1]) * 0.5f;
                float g = rowGaps[r + 1][0];
                Rock(At(mid + RandomRange(-2f, 2f), g + (r % 2 == 0 ? 3.5f : -3.5f)), RandomRange(3f, 4.5f), r % 2 == 0, "T4");
            }
        }

        // Tramo 5 — Llegada: el paso más difícil. Canal de ~10 m que serpentea, con agujas
        // en zigzag cada 14 m que dejan ~2 m por un lado y ~5 m por el otro.
        Tramo("Llegada", 650f);
        const float c5a = C5a, c5b = C5b, gap5 = Gap5;
        Funnel(650f, c5a, CorridorHalfWidth, gap5, Center5(c5a), "T5");
        Wall(c5a, c5b, Center5, gap5, 3.6f, "T5");
        int k = 0;
        for (float s = c5a + 10f; s < c5b - 6f; s += 14f, k++)
            Rock(At(s, Center5(s) + (k % 2 == 0 ? 1f : -1f) * 1.6f), 2.8f, true, "T5");

        return tramos;
    }

    /// <summary>
    /// Reemplaza los hijos de <paramref name="route"/> por puntos cada 4 m que siguen
    /// el centro de los canales, y devuelve el medio ancho permitido en cada uno.
    /// Corrige los "desde" de los tramos a metros sobre esta ruta.
    /// </summary>
    private static List<float> BuildFineRoute(Transform route, List<CanoaMinigameManager.Tramo> tramos)
    {
        var widths = new List<float>();
        float total = cum[cum.Count - 1];
        float fineLen = 0f;
        Vector3 prev = Vector3.zero;
        var sAtFine = new List<float>();
        var fineCum = new List<float>();

        int index = 0;
        for (float s = 0f; ; s += 4f)
        {
            bool last = s >= total;
            if (last) s = total;

            Vector3 p = At(s, CenterAt(s));
            if (index > 0) fineLen += Vector3.Distance(prev, p);
            prev = p;

            var t = new GameObject($"P{index:000}").transform;
            t.SetParent(route, false);
            t.position = p;
            widths.Add(HalfWidthAt(s));
            sAtFine.Add(s);
            fineCum.Add(fineLen);
            index++;
            if (last) break;
        }

        for (int i = 0; i < tramos.Count; i++)
        {
            var tr = tramos[i];
            int j = 0;
            while (j < sAtFine.Count - 1 && sAtFine[j] < tr.desde) j++;
            tr.desde = fineCum[j];
            tramos[i] = tr;
        }
        return widths;
    }

    private static float CenterAt(float s)
    {
        if (s >= C3a && s <= C3b) return Center3(s);
        if (s >= C5a && s <= C5b) return Center5(s);
        return 0f;
    }

    private static float HalfWidthAt(float s)
    {
        float w3 = Gap3 + WallBoundaryExtra, w5 = Gap5 + WallBoundaryExtra;
        if (s < 292f) return OpenHalfWidth;
        if (s < C3a) return Mathf.Lerp(OpenHalfWidth, w3, Mathf.InverseLerp(292f, C3a, s));
        if (s <= C3b) return w3;
        if (s < C3b + 15f) return Mathf.Lerp(w3, OpenHalfWidth, Mathf.InverseLerp(C3b, C3b + 15f, s));
        if (s < 650f) return OpenHalfWidth;
        if (s < C5a) return Mathf.Lerp(OpenHalfWidth, w5, Mathf.InverseLerp(650f, C5a, s));
        if (s <= C5b) return w5;
        if (s < C5b + 15f) return Mathf.Lerp(w5, OpenHalfWidth, Mathf.InverseLerp(C5b, C5b + 15f, s));
        return OpenHalfWidth;
    }

    private static float Center3(float s) => 8f * Mathf.Sin((s - 312f) / 128f * Mathf.PI * 2f);
    private static float Center5(float s) => 6f * Mathf.Sin((s - 668f) / 92f * Mathf.PI * 2f);

    /// <summary>Dos murallas paralelas a la ruta, a ±(gap + medio ancho de roca) del centro.</summary>
    private static void Wall(float sa, float sb, System.Func<float, float> center, float halfGap, float spacing, string tag)
    {
        for (float s = sa; s <= sb; s += spacing)
        {
            float c = center(s);
            for (int side = -1; side <= 1; side += 2)
            {
                bool spire = rng.NextDouble() < 0.4;
                float size = spire ? RandomRange(3.2f, 4.2f) : RandomRange(4.5f, 6f);
                float lateral = c + side * (halfGap + size * 0.5f + RandomRange(0f, 0.8f));
                Rock(At(s + RandomRange(-0.8f, 0.8f), lateral), size, spire, tag);

                // Segunda fila detrás para que la muralla se lea maciza y no queden huecos.
                if (rng.NextDouble() < 0.5)
                    Rock(At(s + RandomRange(-1.5f, 1.5f), lateral + side * RandomRange(4f, 6f)), RandomRange(4.5f, 7f), false, tag);
            }
        }
    }

    /// <summary>
    /// Embudo a ambos lados: cierra el corredor completo desde el borde hasta la
    /// boca del canal, para que no se pueda rodear por mar abierto.
    /// </summary>
    private static void Funnel(float sa, float sb, float outer, float halfGap, float mouthCenter, string tag)
    {
        for (int side = -1; side <= 1; side += 2)
        {
            Vector2 a = new Vector2(sa, side * (outer + 4f));
            Vector2 b = new Vector2(sb, mouthCenter + side * (halfGap + 2.5f));
            float len = Vector2.Distance(a, b);
            for (float d = 0f; d <= len; d += 4.5f)
            {
                Vector2 p = Vector2.Lerp(a, b, d / len);
                Rock(At(p.x + RandomRange(-0.7f, 0.7f), p.y), RandomRange(4.5f, 6.5f), rng.NextDouble() < 0.3, tag);
            }
        }
    }

    /// <summary>Fila de grupos de rocas a lo ancho del corredor, dejando pasos libres de 2·clearHalf.</summary>
    private static void Row(float s, float[] gaps, float clearHalf, float spacing, string tag)
    {
        for (float x = -CorridorHalfWidth - 4f; x <= CorridorHalfWidth + 4f; x += spacing)
        {
            float size = RandomRange(4f, 6.5f);
            bool blocked = false;
            foreach (float g in gaps)
                if (Mathf.Abs(x - g) < clearHalf + size * 0.5f) blocked = true;
            if (blocked) continue;

            float along = s + RandomRange(-2.5f, 2.5f);
            Rock(At(along, x + RandomRange(-0.8f, 0.8f)), size, rng.NextDouble() < 0.35, tag);
        }

        // Marca visual de los pasos: agujas altas a los costados de cada uno.
        foreach (float g in gaps)
        {
            Rock(At(s, g - clearHalf - 1.8f), 3.2f, true, tag);
            Rock(At(s, g + clearHalf + 1.8f), 3.2f, true, tag);
        }
    }

    /// <summary>Punto a <paramref name="s"/> metros por la ruta, desplazado <paramref name="lateral"/> hacia la derecha (la costa).</summary>
    private static Vector3 At(float s, float lateral)
    {
        s = Mathf.Clamp(s, 0f, cum[cum.Count - 1]);
        int i = 0;
        while (i < cum.Count - 2 && cum[i + 1] < s) i++;
        float segLen = cum[i + 1] - cum[i];
        Vector3 a = pts[i], b = pts[i + 1];
        Vector3 p = Vector3.Lerp(a, b, (s - cum[i]) / segLen);
        Vector3 t = (b - a).normalized;
        Vector3 right = new Vector3(t.z, 0f, -t.x);
        return p + right * lateral;
    }

    private static void Rock(Vector3 pos, float footprint, bool spire, string tag)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain != null)
        {
            float ground = terrain.SampleHeight(pos) + terrain.transform.position.y;
            if (ground > 1.2f) { skippedOnLand++; return; }
        }

        int[] pool = spire ? SpireRocks : BlockRocks;
        int id = pool[rng.Next(pool.Length)];
        var prefab = LoadRock(id);
        if (prefab == null) return;

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, obstacles);
        go.name = $"{tag}_Roca{id}_{placed:000}";
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

        var mf = go.GetComponentInChildren<MeshFilter>();
        Vector3 size = mf.sharedMesh.bounds.size;
        float widest = Mathf.Max(size.x, size.z);
        float s = footprint / widest;
        // Las agujas se estiran para que sobresalgan: 7–13 m sobre el agua.
        float heightScale = spire
            ? RandomRange(8f, 14f) / (size.y * s)
            : RandomRange(1f, 1.6f);
        go.transform.localScale = new Vector3(s, s * heightScale, s);

        // El FBX trae un hijo "Rock_N_Collider" con una malla de colisión que además
        // se dibuja encima de la roca: se quita, igual que cualquier otro collider, y
        // la colisión la da un MeshCollider exacto (no convexo) en la malla principal.
        // La canoa es la única pieza dinámica, así que la roca puede ser cóncava.
        foreach (var tr in go.GetComponentsInChildren<Transform>(true))
            if (tr != go.transform && tr.name.EndsWith("_Collider"))
                Object.DestroyImmediate(tr.gameObject);
        foreach (var col in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
        var mc = mf.gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = mf.sharedMesh;
        mc.convex = false;

        // Hunde la base 5 m: la marejada del shader baja hasta ~4 m en los valles
        // y no se debe ver la roca flotando.
        var rend = go.GetComponentInChildren<Renderer>();
        float bottom = rend.bounds.min.y;
        go.transform.position += Vector3.up * (WaterY - 5f - bottom);

        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        placed++;
    }

    private static GameObject LoadRock(int id)
    {
        if (rockCache.TryGetValue(id, out var go)) return go;
        go = AssetDatabase.LoadAssetAtPath<GameObject>($"{RocksFolder}Rock_{id}.fbx");
        rockCache[id] = go;
        return go;
    }

    private static float RandomRange(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    // =================================================================== Canoa

    private static CanoaController BuildCanoe(Transform parent, Vector3 position)
    {
        var canoe = new GameObject("Canoa");
        canoe.transform.SetParent(parent, false);
        canoe.transform.position = new Vector3(position.x, WaterY, position.z);

        var body = canoe.AddComponent<Rigidbody>();
        body.mass = 120f;
        body.useGravity = false;

        var box = canoe.AddComponent<BoxCollider>();
        box.size = new Vector3(0.95f, 0.8f, 4.4f);
        box.center = Vector3.zero;
        string pmPath = $"{AssetFolder}/CanoaCasco.physicMaterial";
        var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(pmPath);
        if (pm == null)
        {
            pm = new PhysicsMaterial("CanoaCasco") { bounciness = 0.1f, dynamicFriction = 0.2f, staticFriction = 0.2f, frictionCombine = PhysicsMaterialCombine.Minimum };
            AssetDatabase.CreateAsset(pm, pmPath);
        }
        box.sharedMaterial = pm;

        var controller = canoe.AddComponent<CanoaController>();

        var visual = new GameObject("Modelo").transform;
        visual.SetParent(canoe.transform, false);

        var outerMat = SaveMaterial("CanoaCorteza", new Color(0.32f, 0.2f, 0.12f), 0.25f);
        var innerMat = SaveMaterial("CanoaInterior", new Color(0.55f, 0.4f, 0.26f), 0.2f);
        var emberMat = SaveMaterial("CanoaFuego", new Color(1f, 0.45f, 0.1f), 0.1f, new Color(1f, 0.4f, 0.08f) * 1.5f); // más alto el pase de estilo lo vira a verde

        var hull = new GameObject("Casco");
        hull.transform.SetParent(visual, false);
        hull.AddComponent<MeshFilter>().sharedMesh = SaveMesh(BuildHullMesh(4.6f, 0.86f, 0.32f), "CanoaCasco");
        hull.AddComponent<MeshRenderer>().sharedMaterials = new[] { outerMat, innerMat };

        // Asiento: ahí se pone el PlayerModel del jugador mientras dura la travesía
        // (los pies sobre el fondo del casco, mirando a proa).
        var seat = new GameObject("Asiento").transform;
        seat.SetParent(visual, false);
        seat.localPosition = new Vector3(0f, -0.2f, -0.9f);


        // El fuego que las familias kawésqar llevaban encendido al centro de la canoa,
        // sobre una base de arcilla.
        Primitive(PrimitiveType.Cylinder, "BaseFuego", visual, new Vector3(0f, -0.05f, 0.45f), new Vector3(0.45f, 0.03f, 0.45f), innerMat);
        var ember = Primitive(PrimitiveType.Sphere, "Brasas", visual, new Vector3(0f, 0.0f, 0.45f), new Vector3(0.28f, 0.14f, 0.28f), emberMat);
        var fire = new GameObject("LuzFuego").AddComponent<Light>();
        fire.transform.SetParent(ember.transform, false);
        fire.transform.localPosition = new Vector3(0f, 2f, 0f);
        fire.type = LightType.Point;
        fire.color = new Color(1f, 0.55f, 0.25f);
        fire.range = 5f;
        fire.intensity = 1.5f;
        fire.shadows = LightShadows.None;

        var so = new SerializedObject(controller);
        so.FindProperty("visual").objectReferenceValue = visual;
        so.ApplyModifiedPropertiesWithoutUndo();

        return controller;
    }

    private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    /// <summary>
    /// Casco de canoa de corteza: secciones en U a lo largo del eje Z, más
    /// angostas y con la borda levantada hacia proa y popa. Submalla 0 = exterior,
    /// submalla 1 = interior (misma forma, caras invertidas y 3 cm hacia adentro).
    /// </summary>
    private static Mesh BuildHullMesh(float length, float beam, float depth)
    {
        const int sections = 28, around = 14;
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var outer = new List<int>();
        var inner = new List<int>();

        for (int pass = 0; pass < 2; pass++)
        {
            float shrink = pass == 0 ? 0f : 0.03f;
            int baseIndex = verts.Count;
            for (int i = 0; i <= sections; i++)
            {
                float u = Mathf.Lerp(-1f, 1f, i / (float)sections);
                float z = u * length * 0.5f;
                float au = Mathf.Abs(u);
                float hw = Mathf.Max(0.004f, beam * 0.5f * Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(au, 2.2f)), 0.6f) - shrink);
                float keel = -depth * Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(au, 3f)), 0.5f) + shrink;
                float sheer = 0.1f + 0.22f * Mathf.Pow(au, 4f);

                for (int j = 0; j <= around; j++)
                {
                    float th = Mathf.PI * j / around;
                    float x = hw * Mathf.Cos(th);
                    float y = sheer - (sheer - keel) * Mathf.Pow(Mathf.Abs(Mathf.Sin(th)), 0.7f); // sin(π) sale apenas negativo: sin Abs da NaN
                    verts.Add(new Vector3(x, y, z));
                    uvs.Add(new Vector2(j / (float)around, i / (float)sections));
                }
            }

            var tris = pass == 0 ? outer : inner;
            int row = around + 1;
            for (int i = 0; i < sections; i++)
                for (int j = 0; j < around; j++)
                {
                    int a = baseIndex + i * row + j, b = a + 1, c = a + row, d = c + 1;
                    if (pass == 0) { tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(b); tris.Add(d); tris.Add(c); }
                    else { tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(b); tris.Add(c); tris.Add(d); }
                }
        }

        var mesh = new Mesh { name = "CanoaCasco" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(outer, 0);
        mesh.SetTriangles(inner, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        // Si el exterior quedó con las normales hacia adentro, se invierten ambas submallas.
        var n = mesh.normals;
        int probe = (28 / 2) * 15 + 14 / 2; // sección central, punto de la quilla
        if (n[probe].y > 0f)
        {
            outer.Reverse();
            inner.Reverse();
            mesh.SetTriangles(outer, 0);
            mesh.SetTriangles(inner, 1);
            mesh.RecalculateNormals();
        }
        return mesh;
    }

    // =================================================================== Faro

    private static GameObject BuildBeacon(Transform parent, Vector3 at, out Renderer glowRenderer, out Light light)
    {
        var beacon = new GameObject("FaroLlegada");
        beacon.transform.SetParent(parent, false);

        var terrain = Terrain.activeTerrain;
        float ground = terrain != null ? terrain.SampleHeight(at) + terrain.transform.position.y : WaterY;
        beacon.transform.position = new Vector3(at.x, Mathf.Max(ground, WaterY), at.z);

        var poleMat = SaveMaterial("FaroPoste", new Color(0.35f, 0.27f, 0.2f), 0.2f);
        var glowMat = SaveMaterial("FaroLuz", new Color(1f, 0.62f, 0.2f), 0.1f, new Color(1f, 0.62f, 0.2f) * 3f);

        Primitive(PrimitiveType.Cylinder, "Poste", beacon.transform, new Vector3(0f, 4f, 0f), new Vector3(0.35f, 4f, 0.35f), poleMat);
        var glow = Primitive(PrimitiveType.Sphere, "Luz", beacon.transform, new Vector3(0f, 8.6f, 0f), Vector3.one * 1.3f, glowMat);
        glowRenderer = glow.GetComponent<Renderer>();

        light = new GameObject("Luz puntual").AddComponent<Light>();
        light.transform.SetParent(beacon.transform, false);
        light.transform.localPosition = new Vector3(0f, 8.6f, 0f);
        light.type = LightType.Point;
        light.range = 30f;
        light.intensity = 4f;
        light.color = new Color(1f, 0.62f, 0.2f);
        light.shadows = LightShadows.None;
        return beacon;
    }

    // =================================================================== Llegada

    /// <summary>
    /// Dónde queda el jugador al terminar la travesía: el punto de tierra firme más
    /// cercano al faro (sobre la marea, con poca pendiente y terreno parejo
    /// alrededor), mirando hacia el interior de la isla. La altura es la del centro
    /// del CharacterController del Player (sus pies quedan 1 m más abajo).
    /// </summary>
    private static Transform BuildLanding(Transform parent, Vector3 beacon)
    {
        var terrain = Terrain.activeTerrain;
        Vector3 best = beacon;
        float bestD = float.MaxValue;
        if (terrain != null)
        {
            var td = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            for (float dx = -40f; dx <= 40f; dx += 1f)
                for (float dz = -40f; dz <= 40f; dz += 1f)
                {
                    var p = new Vector3(beacon.x + dx, 0f, beacon.z + dz);
                    float h = terrain.SampleHeight(p) + origin.y;
                    if (h < 1.8f || h > 12f) continue;
                    float steep = td.GetSteepness((p.x - origin.x) / td.size.x, (p.z - origin.z) / td.size.z);
                    if (steep > 18f) continue;

                    bool flat = true;
                    for (int a = 0; a < 8 && flat; a++)
                    {
                        Vector3 q = p + Quaternion.Euler(0f, a * 45f, 0f) * Vector3.forward * 3f;
                        float hq = terrain.SampleHeight(q) + origin.y;
                        if (hq < 1.5f || Mathf.Abs(hq - h) > 1.2f) flat = false;
                    }
                    if (!flat) continue;

                    float d = dx * dx + dz * dz;
                    if (d < bestD) { bestD = d; best = new Vector3(p.x, h, p.z); }
                }
        }
        if (bestD == float.MaxValue)
            Debug.LogWarning("[Canoa] No encontré tierra firme cerca del faro: el jugador aparecerá junto a él.");

        var t = new GameObject("LlegadaJugador").transform;
        t.SetParent(parent, false);
        t.position = best + Vector3.up * 1.05f;
        Vector3 inland = best - beacon;
        inland.y = 0f;
        if (inland.sqrMagnitude > 0.01f) t.rotation = Quaternion.LookRotation(inland);
        return t;
    }

    // =================================================================== Assets

    private static Material SaveMaterial(string name, Color color, float smoothness, Color? emission = null)
    {
        string path = $"{AssetFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission.Value);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>
    /// Material de la espuma que marca las corrientes: partícula URP sin luz,
    /// transparente, con una mancha blanda generada (no hay textura de espuma en el proyecto).
    /// </summary>
    private static Material SaveFoamMaterial()
    {
        string texPath = $"{AssetFolder}/EspumaMancha.asset";
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        if (tex == null)
        {
            const int n = 64;
            tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "EspumaMancha", wrapMode = TextureWrapMode.Clamp };
            var rng = new System.Random(5);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    // Borde irregular: la espuma no es un círculo perfecto.
                    float grain = 0.75f + 0.25f * (float)rng.NextDouble();
                    float a = Mathf.Clamp01(1f - Mathf.SmoothStep(0.35f, 1f, r)) * grain;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            AssetDatabase.CreateAsset(tex, texPath);
        }

        string path = $"{AssetFolder}/EspumaCorriente.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", new Color(0.95f, 0.97f, 1f, 0.8f));
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Mesh SaveMesh(Mesh mesh, string name)
    {
        string path = $"{AssetFolder}/{name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            existing.Clear();
            EditorUtility.CopySerialized(mesh, existing);
            EditorUtility.SetDirty(existing);
            return existing;
        }
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parentPath = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parentPath);
        AssetDatabase.CreateFolder(parentPath, System.IO.Path.GetFileName(path));
    }
}
