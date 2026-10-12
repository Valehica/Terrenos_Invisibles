using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Rendering;

// Reparte arboles y rocas de los packs importados por el Terrain de Sur, por reglas y con semilla fija
// (como VegetacionSur): re-correr el menu despues de regenerar el terreno o de mover la pasarela.
//
// Packs: "Mountain Terrain, Rocks and Tree" (tree_01 + 4 rocas), "Realistic Tree 9" (Tree9_2..5, arbol
// arcoiris) y "Free Plane Tree" (ZNS3D). Ninguno trae materiales de URP (Standard/Legacy, Tree Creator y
// HDRP: se ven rosados), asi que se crean copias _Sur en Assets/Models/Sur/Vegetacion con URP/Lit, sin
// tocar los packs.
//
// Bosque cerrado, como el real de Puerto Eden: cubre casi toda la tierra bajo la linea de arboles, con
// claros (turberas) y mas ralo en la roca y la ribera. ~70 000 arboles, asi que nada se dibuja entero de lejos:
//   hasta DistanciaArboles  arboles del Terrain con LOD0 (modelo del pack) y LOD1 (aliviado), ver ArbolesLodSur;
//   mas lejos              billboards, que dibuja BosqueLejano (en el GameObject del Terrain) en mallas por zona.
// Los arboles llevan collider de capsula en el tronco (todos, tambien los lejanos).
// Rocas -> GameObjects bajo "ArbolesYRocas_Sur", porque llevan MeshCollider e inclinacion al azar.
//
// Solo la zona que recorre el jugador (el objeto ZonaJugador de la escena) lleva arboles de verdad y rocas.
// Afuera el terreno es paisaje que se ve de lejos: los arboles de ahi son solo billboard (sin modelo ni
// collider) y van a BosqueSoloLejos_Sur.bytes, que dibuja BosqueLejano.
//
// Nada cerca de la pasarela, el jugador, el NPC ni los marcadores de la travesia.
public static class ArbolesRocasSur
{
    const string Carpeta = "Assets/Models/Sur/Vegetacion";
    const string NombreRaiz = "ArbolesYRocas_Sur";
    const string RutaSoloLejos = Carpeta + "/BosqueSoloLejos_Sur.bytes";
    const int Semilla = 4127;

    const int MaxRocas = 700;
    const float AlturaMinima = 1.5f;        // m sobre el mar: la ribera queda pelada
    const float DistanciaArboles = 90f;     // Terrain.treeDistance: de ahi en adelante, billboards
    const float SeparacionArboles = 5.5f;   // lado de la grilla con jitter: un arbol por celda como mucho

    // Albedo: el terreno de Sur anda en 0.07-0.12 lineal [ver memoria de albedos]. La roca del pack mide
    // 0.23 y la corteza de tree_01 0.33: sin tinte se ven como yeso. Tintes en gamma.
    static readonly Color TinteRoca = new Color(0.60f, 0.64f, 0.63f);     // x0.36-0.40 lineal, hacia gris verdoso
    static readonly Color TinteCorteza = new Color(0.62f, 0.55f, 0.50f);

    class Especie
    {
        public ArbolesLodSur.Especie lod;
        public string nombre;
        public int grupo;                    // 0 haya, 1 arcoiris, 2 platano: forman rodales
        public float peso, escalaMin, escalaMax, anchoExtra = 1f;
        public GameObject prefab => lod.prefab;
    }

    [MenuItem("Terrenos/Sur/Poblar arboles y rocas")]
    static void Poblar()
    {
        var terreno = Terrain.activeTerrain;
        if (terreno == null) { Debug.LogError("No hay Terrain activo."); return; }
        var td = terreno.terrainData;

        var arboles = PrepararArboles();
        var lista = new List<ArbolesLodSur.Especie>();
        foreach (var e in arboles) lista.Add(e.lod);
        var billboards = ArbolesLodSur.HornearBillboards(lista);
        var rocas = PrepararRocas();

        // Sin Undo para el TerrainData: copiarlo entero (alturas, splat, detalles) traba el Editor. Para
        // volver atras, git o re-correr con otra semilla.
        // Prototipos: se quitan los de esta carpeta (corridas anteriores) y se agregan los de ahora; los
        // arboles que no son de este script se conservan con su indice corregido.
        var viejos = td.treePrototypes;
        var protos = new List<TreePrototype>();
        var remapa = new int[viejos.Length];
        for (int i = 0; i < viejos.Length; i++)
        {
            bool nuestro = viejos[i].prefab != null && AssetDatabase.GetAssetPath(viejos[i].prefab).StartsWith(Carpeta);
            remapa[i] = nuestro ? -1 : protos.Count;
            if (!nuestro) protos.Add(viejos[i]);
        }
        int primero = protos.Count;
        foreach (var e in arboles) protos.Add(new TreePrototype { prefab = e.prefab, bendFactor = 0f });
        var instanciasPrevias = new List<TreeInstance>();
        foreach (var t in td.treeInstances)
        {
            if (t.prototypeIndex >= viejos.Length || remapa[t.prototypeIndex] < 0) continue;
            var c = t; c.prototypeIndex = remapa[t.prototypeIndex]; instanciasPrevias.Add(c);
        }
        td.treePrototypes = protos.ToArray();

        var excluir = Exclusiones();
        var rng = new System.Random(Semilla);
        Vector3 o = terreno.transform.position, s = td.size;
        var alfa = td.GetAlphamaps(0, 0, td.alphamapWidth, td.alphamapHeight);

        // ---------------------------------------------------------- arboles
        // Grilla con jitter: un candidato por celda, aceptado segun lo apto que sea el lugar.
        var zona = Object.FindFirstObjectByType<ZonaJugador>();
        if (zona == null) Debug.LogWarning("ArbolesRocasSur: no hay ZonaJugador en la escena; todos los arboles van de verdad.");
        var lejos = new MemoryStream();
        var escritor = new BinaryWriter(lejos);
        escritor.Write(0);
        int nLejos = 0;
        var ocupado = new Dictionary<long, Vector2>();
        var instancias = new List<TreeInstance>(instanciasPrevias);
        int celdasX = Mathf.FloorToInt(s.x / SeparacionArboles), celdasZ = Mathf.FloorToInt(s.z / SeparacionArboles);
        for (int gx = 0; gx < celdasX; gx++)
            for (int gz = 0; gz < celdasZ; gz++)
            {
                var p = new Vector3(o.x + (gx + 0.15f + 0.7f * (float)rng.NextDouble()) * SeparacionArboles, 0f,
                                    o.z + (gz + 0.15f + 0.7f * (float)rng.NextDouble()) * SeparacionArboles);
                float tiroApto = (float)rng.NextDouble(), tiroEspecie = (float)rng.NextDouble();
                float tiroEscala = (float)rng.NextDouble(), tiroAncho = (float)rng.NextDouble();
                float tiroGiro = (float)rng.NextDouble(), tiroColor = (float)rng.NextDouble();
                float u = (p.x - o.x) / s.x, v = (p.z - o.z) / s.z;
                float h = terreno.SampleHeight(p) + o.y;
                if (h < AlturaMinima) continue;
                float pend = td.GetSteepness(u, v);
                Capas(alfa, td, u, v, out float musgo, out float matorral, out float roca, out float ribera, out float nieve, out float lecho);

                float apto = 1f - nieve - lecho - 0.25f * roca - 0.3f * ribera;
                apto *= 1f - Mathf.InverseLerp(38f, 50f, pend);           // el bosque de fiordo sube por laderas empinadas
                apto *= 1f - Mathf.InverseLerp(220f, 300f, h);                     // linea de arboles
                apto *= Mathf.InverseLerp(AlturaMinima, AlturaMinima + 4f, h);      // borde costero mas ralo
                // Claros: turberas grandes y claritos sueltos.
                apto *= Mathf.InverseLerp(0.22f, 0.30f, Mathf.PerlinNoise(p.x / 260f + 3.1f, p.z / 260f + 7.7f));
                apto *= Mathf.InverseLerp(0.18f, 0.26f, Mathf.PerlinNoise(p.x / 55f + 11.3f, p.z / 55f + 5.9f));
                if (tiroApto >= apto) continue;
                bool deVerdad = zona == null || zona.Contiene(p);
                if (deVerdad && Excluido(excluir, p, 6f)) continue;

                // Rodales: cada grupo pesa mas donde su ruido es alto.
                float arco = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.7f, Mathf.PerlinNoise(p.x / 220f + 41.0f, p.z / 220f + 17.0f)));
                float plat = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.7f, Mathf.PerlinNoise(p.x / 220f + 93.0f, p.z / 220f + 64.0f)));
                float total = 0f;
                foreach (var e in arboles) total += Peso(e, arco, plat);
                float tiro = tiroEspecie * total;
                int k = 0;
                for (; k < arboles.Count - 1; k++) { tiro -= Peso(arboles[k], arco, plat); if (tiro <= 0f) break; }
                var esp = arboles[k];
                float esc = Mathf.Lerp(esp.escalaMin, esp.escalaMax, tiroEscala);
                if (!deVerdad)
                {
                    Color32 c = Color.Lerp(new Color(0.82f, 0.82f, 0.82f), Color.white, tiroColor);
                    escritor.Write(k);
                    escritor.Write(p.x); escritor.Write(h); escritor.Write(p.z);
                    escritor.Write(tiroGiro * Mathf.PI * 2f);
                    escritor.Write(esc * esp.anchoExtra * Mathf.Lerp(0.9f, 1.1f, tiroAncho));
                    escritor.Write(esc);
                    escritor.Write((uint)(c.r | c.g << 8 | c.b << 16 | c.a << 24));
                    nLejos++;
                    continue;
                }
                instancias.Add(new TreeInstance
                {
                    prototypeIndex = primero + k,
                    position = new Vector3(u, 0f, v),
                    widthScale = esc * esp.anchoExtra * Mathf.Lerp(0.9f, 1.1f, tiroAncho),
                    heightScale = esc,
                    rotation = tiroGiro * Mathf.PI * 2f,
                    color = Color.Lerp(new Color(0.82f, 0.82f, 0.82f), Color.white, tiroColor),
                    lightmapColor = Color.white,
                });
                ocupado[Clave(gx, gz)] = new Vector2(p.x, p.z);
            }
        td.SetTreeInstances(instancias.ToArray(), true);
        escritor.Seek(0, SeekOrigin.Begin);
        escritor.Write(nLejos);
        escritor.Flush();
        File.WriteAllBytes(RutaSoloLejos, lejos.ToArray());
        AssetDatabase.ImportAsset(RutaSoloLejos);
        terreno.treeDistance = DistanciaArboles;
        terreno.drawInstanced = true;
        terreno.Flush();

        var bosque = terreno.GetComponent<BosqueLejano>();
        if (bosque == null) bosque = Undo.AddComponent<BosqueLejano>(terreno.gameObject);
        bosque.terreno = terreno;
        bosque.billboards = billboards;
        bosque.soloLejos = AssetDatabase.LoadAssetAtPath<TextAsset>(RutaSoloLejos);
        EditorUtility.SetDirty(bosque);
        bosque.Generar();

        // ---------------------------------------------------------- rocas
        var viejo = GameObject.Find(NombreRaiz);
        if (viejo != null) Undo.DestroyObjectImmediate(viejo);
        var raiz = new GameObject(NombreRaiz);
        Undo.RegisterCreatedObjectUndo(raiz, "Poblar arboles y rocas");
        int nRocas = 0;
        var rocasPuestas = new List<Vector2>();
        for (int intento = 0; intento < MaxRocas * 60 && nRocas < MaxRocas; intento++)
        {
            var p = new Vector3(o.x + (float)rng.NextDouble() * s.x, 0f, o.z + (float)rng.NextDouble() * s.z);
            float u = (p.x - o.x) / s.x, v = (p.z - o.z) / s.z;
            float h = terreno.SampleHeight(p) + o.y;
            if (h < 0.6f) continue;
            float pend = td.GetSteepness(u, v);
            Capas(alfa, td, u, v, out float musgo, out float matorral, out float roca, out float ribera, out float nieve, out float lecho);

            float apto = 0.12f + 0.9f * roca + 0.5f * ribera + 0.2f * matorral - 0.5f * nieve;
            apto *= Mathf.Lerp(0.6f, 1.3f, Mathf.InverseLerp(5f, 35f, pend)) * (pend > 50f ? 0f : 1f);
            apto *= Mathf.InverseLerp(0.30f, 0.62f, Mathf.PerlinNoise(p.x / 90f + 77.1f, p.z / 90f + 12.9f));
            if ((float)rng.NextDouble() >= apto) continue;
            if (zona != null && !zona.Contiene(p)) continue;
            if (Excluido(excluir, p, 4f) || MuyCerca(rocasPuestas, p, 4f) || CercaDeArbol(ocupado, o, p, 2.5f)) continue;

            var pref = rocas[rng.Next(rocas.Count)];
            var go = (GameObject)PrefabUtility.InstantiatePrefab(pref, raiz.transform);
            float esc = Mathf.Lerp(0.6f, 1.8f, Mathf.Pow((float)rng.NextDouble(), 1.8f));   // muchas chicas, pocas grandes
            Vector3 normal = td.GetInterpolatedNormal(u, v);
            var rot = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, normal, 0.6f)) *
                      Quaternion.Euler((float)rng.NextDouble() * 16f - 8f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 16f - 8f);
            float alto = pref.GetComponent<MeshFilter>().sharedMesh.bounds.size.y * esc;
            go.transform.SetPositionAndRotation(new Vector3(p.x, h - alto * Mathf.Lerp(0.15f, 0.35f, (float)rng.NextDouble()), p.z), rot);
            go.transform.localScale = Vector3.one * esc;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
            rocasPuestas.Add(new Vector2(p.x, p.z));
            nRocas++;
        }

        EditorUtility.SetDirty(td);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(terreno.gameObject.scene);
        AssetDatabase.SaveAssets();

        var cuenta = new int[arboles.Count];
        foreach (var t in instancias) if (t.prototypeIndex >= primero) cuenta[t.prototypeIndex - primero]++;
        var txt = "";
        for (int i = 0; i < arboles.Count; i++) txt += arboles[i].nombre + " " + cuenta[i] + "  ";
        Debug.Log($"ArbolesRocasSur: {ocupado.Count} arboles de verdad ({txt}), {nLejos} solo billboard, {nRocas} rocas, {bosque.bloques} bloques de bosque lejano.");
    }

    static void Capas(float[,,] a, TerrainData td, float u, float v, out float musgo, out float matorral,
                      out float roca, out float ribera, out float nieve, out float lecho)
    {
        int x = Mathf.Clamp((int)(u * td.alphamapWidth), 0, td.alphamapWidth - 1);
        int y = Mathf.Clamp((int)(v * td.alphamapHeight), 0, td.alphamapHeight - 1);
        float C(int i) => i < td.alphamapLayers ? a[y, x, i] : 0f;
        // Orden de capas de Sur: Musgo, Matorral, Roca, Ribera, Nieve, Lecho.
        musgo = C(0); matorral = C(1); roca = C(2); ribera = C(3); nieve = C(4); lecho = C(5);
    }

    // ---------------------------------------------------------------- exclusiones

    struct Zona { public Vector3 c; public float r; }

    static List<Zona> Exclusiones()
    {
        var z = new List<Zona>();
        foreach (var n in new[] { "Player", "NPC" })
        {
            var g = GameObject.Find(n);
            if (g != null) z.Add(new Zona { c = g.transform.position, r = 25f });
        }
        foreach (var n in new[] { "Puerto Eden", "Parque ohiggins" })
        {
            var g = GameObject.Find(n);
            if (g != null) z.Add(new Zona { c = g.transform.position, r = 30f });
        }
        // La pasarela: muestras cada 3 m, con su medio ancho.
        foreach (var pc in Object.FindObjectsByType<PasarelaCurva>(FindObjectsSortMode.None))
            foreach (var p in pc.MuestrearCurva(3f))
                z.Add(new Zona { c = p, r = 3.2f * pc.escala / 0.3f });
        return z;
    }

    static bool Excluido(List<Zona> zonas, Vector3 p, float margen)
    {
        foreach (var z in zonas)
        {
            float dx = p.x - z.c.x, dz = p.z - z.c.z, r = z.r + margen;
            if (dx * dx + dz * dz < r * r) return true;
        }
        return false;
    }

    static float Peso(Especie e, float arco, float plat)
    {
        return e.grupo == 1 ? e.peso * (0.3f + 1.7f * arco) : e.grupo == 2 ? e.peso * (0.3f + 1.7f * plat) : e.peso;
    }

    static long Clave(int gx, int gz) => ((long)gx << 32) ^ (uint)gz;

    static bool CercaDeArbol(Dictionary<long, Vector2> ocupado, Vector3 o, Vector3 p, float d)
    {
        int gx = Mathf.FloorToInt((p.x - o.x) / SeparacionArboles), gz = Mathf.FloorToInt((p.z - o.z) / SeparacionArboles);
        var q = new Vector2(p.x, p.z);
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                if (ocupado.TryGetValue(Clave(gx + dx, gz + dz), out var a) && (a - q).sqrMagnitude < d * d) return true;
        return false;
    }

    static bool MuyCerca(List<Vector2> pts, Vector3 p, float d)
    {
        var q = new Vector2(p.x, p.z);
        foreach (var o in pts) if ((o - q).sqrMagnitude < d * d) return true;
        return false;
    }

    // ---------------------------------------------------------------- prefabs y materiales

    static List<Especie> PrepararArboles()
    {
        Directory.CreateDirectory(Carpeta);
        var l = new List<Especie>();

        // tree_01 (haya chica, 6 m)
        {
            string b = "Assets/AssetStoreResourses/Mountain Terrain rocks and tree/Materials/Trees/tree_01/";
            Importar(b + "bark01_normal.png", true);
            Importar(b + "beech_leaf_Normal_01.png", true);
            var corteza = Mat("Corteza_Haya_Sur", b + "bark01.png", b + "bark01_normal.png", TinteCorteza, false);
            var hojas = Mat("Hojas_Haya_Sur", b + "beech_leaf_01.png", b + "beech_leaf_Normal_01.png", new Color(0.80f, 0.86f, 0.78f), true, 0.5f);
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/AssetStoreResourses/Mountain Terrain rocks and tree/3d model/Trees/tree_01.fbx");
            l.Add(Nueva("Haya", 0, "Arbol_Haya_Sur", mesh, new[] { corteza, hojas }, 0.30f, 1.9f, 0.45f, 1.5f, 2.3f, 1.4f));
        }

        // Tree9 (eucalipto arcoiris, ~23 m): atlas de Tree Creator, corteza a la izquierda y hojas a la derecha.
        {
            string b = "Assets/AssetStoreResourses/Tree9/Tree9_1_Textures/";
            string normal = NormalDesdeTreeCreator(b + "normal_specular.png", Carpeta + "/Tree9_Normal_Sur.png");
            var corteza = Mat("Corteza_Arcoiris_Sur", b + "diffuse.png", normal, Color.white, false);
            var hojas = Mat("Hojas_Arcoiris_Sur", b + "diffuse.png", normal, new Color(0.80f, 0.85f, 0.75f), true, 0.3f);
            for (int i = 2; i <= 5; i++)
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/AssetStoreResourses/Tree9/Tree9_{i}.prefab");
                var mesh = src.GetComponent<MeshFilter>().sharedMesh;
                l.Add(Nueva("Arcoiris" + i, 1, "Arbol_Arcoiris_" + i + "_Sur", mesh, new[] { corteza, hojas }, 0.40f, 0.57f, 0.30f / 4f, 0.45f, 0.70f, 1f));
            }
        }

        // Plane_Tree (platano, 12 m): dos mallas (tronco y hojas) -> una malla con dos submallas, porque el
        // Terrain necesita el renderer en la raiz del prefab.
        {
            string b = "Assets/AssetStoreResourses/ZNS3D/PLANE_TREE/TEXTURES/";
            Importar(b + "TRUNK/PLANE_Plane_Tree_Bark_Normal.png", true);
            var corteza = Mat("Corteza_Platano_Sur", b + "TRUNK/PLANE_Plane_Tree_Bark_BaseColor.png", b + "TRUNK/PLANE_Plane_Tree_Bark_Normal.png", TinteCorteza, false);
            var hojas = Mat("Hojas_Platano_Sur", b + "LEAF/plane_leaf.png", null, new Color(0.85f, 0.88f, 0.80f), true, 0.5f);
            string rutaMalla = Carpeta + "/Platano_Sur.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(rutaMalla);
            if (mesh == null)
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetStoreResourses/ZNS3D/PLANE_TREE/PREFABS/Plane_Tree.prefab");
                MeshFilter tronco = null, hoja = null;
                foreach (var mf in src.GetComponentsInChildren<MeshFilter>())
                    if (mf.name.Contains("Leaf")) hoja = mf; else tronco = mf;
                mesh = new Mesh { name = "Platano_Sur", indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(new[]
                {
                    new CombineInstance { mesh = tronco.sharedMesh, transform = src.transform.worldToLocalMatrix * tronco.transform.localToWorldMatrix },
                    new CombineInstance { mesh = hoja.sharedMesh, transform = src.transform.worldToLocalMatrix * hoja.transform.localToWorldMatrix },
                }, false, true);
                AssetDatabase.CreateAsset(mesh, rutaMalla);
            }
            l.Add(Nueva("Platano", 2, "Arbol_Platano_Sur", mesh, new[] { corteza, hojas }, 0.30f, 0.85f, 0.25f, 0.70f, 1.00f, 1f));
        }
        return l;
    }

    static List<GameObject> PrepararRocas()
    {
        string b = "Assets/AssetStoreResourses/Mountain Terrain rocks and tree/Materials/Rock/";
        Importar(b + "rock_set_mat_Normal.png", true);
        var mat = Mat("Roca_Montana_Sur", b + "rock_set_mat_AlbedoTransparency.png", b + "rock_set_mat_Normal.png", TinteRoca, false);
        mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(b + "rock_set_mat_MetallicSmoothness.png"));
        mat.SetFloat("_Smoothness", 0.6f);
        mat.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(b + "rock_set_mat_AO.png"));
        Validar(mat);

        var l = new List<GameObject>();
        for (int i = 1; i <= 4; i++)
        {
            string ruta = $"{Carpeta}/Roca_{i}_Sur.prefab";
            var src = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/AssetStoreResourses/Mountain Terrain rocks and tree/Prefab/rock_set_0{i}.prefab");
            var go = new GameObject("Roca_" + i + "_Sur");
            go.AddComponent<MeshFilter>().sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = src.GetComponent<MeshCollider>().sharedMesh;
            var lod = go.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.012f, new Renderer[] { r }) });
            l.Add(PrefabUtility.SaveAsPrefabAsset(go, ruta));
            Object.DestroyImmediate(go);
        }
        return l;
    }

    static Especie Nueva(string nombre, int grupo, string prefab, Mesh mesh, Material[] mats, float fraccionHojas,
                         float escalaTipica, float peso, float escalaMin, float escalaMax, float anchoExtra)
    {
        var lod = new ArbolesLodSur.Especie
        {
            nombre = prefab, mesh = mesh, mats = mats, subCorteza = 0,
            fraccionHojas = fraccionHojas, escalaTipica = escalaTipica,
        };
        ArbolesLodSur.PrefabArbol(lod);
        return new Especie { lod = lod, nombre = nombre, grupo = grupo, peso = peso, escalaMin = escalaMin, escalaMax = escalaMax, anchoExtra = anchoExtra };
    }

    static Material Mat(string nombre, string color, string normal, Color tinte, bool hojas, float corte = 0.5f)
    {
        string ruta = $"{Carpeta}/{nombre}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        bool nuevo = m == null;
        if (nuevo) m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(color));
        m.SetColor("_BaseColor", tinte);
        m.SetTexture("_BumpMap", normal != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(normal) : null);
        m.SetFloat("_Smoothness", hojas ? 0.25f : 0.1f);
        m.SetFloat("_AlphaClip", hojas ? 1f : 0f);
        m.SetFloat("_Cutoff", corte);
        m.SetFloat("_Cull", hojas ? (float)CullMode.Off : (float)CullMode.Back);
        m.enableInstancing = true;
        Validar(m);
        if (nuevo) AssetDatabase.CreateAsset(m, ruta);
        else EditorUtility.SetDirty(m);
        return m;
    }

    // Deja las keywords y la cola como las pondria el Inspector de URP.
    static void Validar(Material m)
    {
        BaseShaderGUI.SetMaterialKeywords(m, LitGUI.SetMaterialKeywords);
    }

    static void Importar(string ruta, bool normal)
    {
        if (!(AssetImporter.GetAtPath(ruta) is TextureImporter ti)) return;
        var tipo = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (ti.textureType == tipo) return;
        ti.textureType = tipo;
        ti.SaveAndReimport();
    }

    // Tree Creator guarda la normal como DXT5nm (X en alfa, Y en verde) y el especular en rojo.
    static string NormalDesdeTreeCreator(string origen, string destino)
    {
        if (!File.Exists(destino))
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            t.LoadImage(File.ReadAllBytes(origen));
            var px = t.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                float x = px[i].a / 255f * 2f - 1f, y = px[i].g / 255f * 2f - 1f;
                float z = Mathf.Sqrt(Mathf.Max(0f, 1f - x * x - y * y));
                px[i] = new Color32(px[i].a, px[i].g, (byte)Mathf.RoundToInt((z * 0.5f + 0.5f) * 255f), 255);
            }
            var o = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false, true);
            o.SetPixels32(px);
            File.WriteAllBytes(destino, o.EncodeToPNG());
            Object.DestroyImmediate(t); Object.DestroyImmediate(o);
            AssetDatabase.ImportAsset(destino);
        }
        Importar(destino, true);
        return destino;
    }
}
