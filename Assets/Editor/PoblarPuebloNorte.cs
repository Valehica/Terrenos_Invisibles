using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Puebla Isluga (escena Norte) con las casas de Assets/Prefabs/OtrasCasas, imitando la foto
// satelital: casas en hilera a lo largo de las calles, con la puerta hacia la calle y patio
// al centro de la manzana. La grilla es la del terreno pintado (calles Norte_Tierra de 8 m,
// manzanas Mud_Pebbles de ~21x20 m) más una corrida de manzanas nuevas alrededor, cuyas
// calles y manzanas se pintan en el terreno igual que las existentes.
// Las casas van en la raíz "CasasPueblo" (re-ejecutar la borra y la rehace con la misma
// semilla). No toca la raíz "Casas" (las que reemplazaron a las casas viejas): las nuevas
// la esquivan, igual que a la iglesia, los árboles, las vicuñas, el jugador y el NPC.
public static class PoblarPuebloNorte
{
    const string Raiz = "CasasPueblo";
    const string CarpetaPrefab = "Assets/Prefabs/OtrasCasas";
    const float Escala = 1.5f;          // igual que las casas que reemplazaron a las viejas
    const int Semilla = 1985;
    const float MargenCalle = 0.6f;     // retiro de la fachada respecto del borde de la calle
    const float Separacion = 0.3f;      // mínimo entre casas vecinas (en Isluga casi se tocan)
    const float PendienteMax = 1.4f;    // diferencia de altura máxima bajo una casa (m)
    const int CapaManzana = 2;          // Mud_Pebbles
    const int CapaCalle = 7;            // Norte_Tierra
    const int CapaSuelo = 0;            // Mud: solo se repinta donde domina esta capa
    const float AnchoCalle = 8f;

    // columnas y filas de manzanas (x0, x1) / (z0, z1) en metros de mundo
    static readonly float[][] Cols = {
        new[]{-131.5f,-110f}, new[]{-102.5f,-81f}, new[]{-73.5f,-52f}, new[]{-44f,-22f}, new[]{-6f,16f} };
    static readonly float[][] Filas = {
        new[]{30f,50f}, new[]{1.5f,21.5f}, new[]{-26f,-6f}, new[]{-54f,-34f}, new[]{-82f,-63f}, new[]{-110f,-90f} };

    // manzanas a poblar: (columna, fila). Las de la iglesia (2,2) y (2,3) quedan fuera.
    static readonly int[][] Manzanas = {
        new[]{0,0}, new[]{1,0}, new[]{2,0}, new[]{3,0}, new[]{4,0},
        new[]{0,1}, new[]{3,1}, new[]{4,1},
        new[]{4,2}, new[]{4,3},
        new[]{0,4}, new[]{1,4}, new[]{3,4}, new[]{4,4},
        new[]{0,5}, new[]{1,5}, new[]{2,5}, new[]{3,5}, new[]{4,5},
        // estas ya tienen dos casas grandes; se rellena lo que quede libre
        new[]{1,1}, new[]{2,1}, new[]{0,2}, new[]{1,2}, new[]{0,3}, new[]{1,3}, new[]{2,4} };

    // manzanas que no existían pintadas en el terreno (se pintan calles y manzana)
    static bool EsNueva(int c, int f) => f == 0 || f == 5 || c == 4;

    class Tipo
    {
        public string Casa; public Vector3 Frente;  // dirección local de la puerta
        public float Fachada, Fondo;                 // a lo largo de la calle / hacia adentro (m, ya escalado)
        public float Peso;
    }

    [MenuItem("Terrenos/Norte/Poblar pueblo (casas en hilera)")]
    public static void Poblar()
    {
        var tipos = Tipos();
        if (tipos.Count == 0) { Debug.LogWarning("[Pueblo] No hay prefabs en " + CarpetaPrefab); return; }
        var ter = Terrain.activeTerrain;
        var escena = EditorSceneManager.GetActiveScene();

        var viejo = GameObject.Find(Raiz);
        if (viejo) Undo.DestroyObjectImmediate(viejo);
        var raiz = new GameObject(Raiz);
        Undo.RegisterCreatedObjectUndo(raiz, "Poblar pueblo");

        PintarTerreno(ter);

        var obst = Obstaculos(ter);
        var rnd = new System.Random(Semilla);
        int n = 0;
        foreach (var m in Manzanas)
        {
            float bx0 = Cols[m[0]][0], bx1 = Cols[m[0]][1], bz0 = Filas[m[1]][0], bz1 = Filas[m[1]][1];
            float prof = bz1 - bz0;
            // hilera norte (mira a +z) y sur (mira a -z); el fondo de una limita al de la otra
            float usadoN = Hilera(raiz, tipos, rnd, ter, obst, bx0, bx1, bz1, Vector3.forward, prof * 0.55f, ref n);
            Hilera(raiz, tipos, rnd, ter, obst, bx0, bx1, bz0, Vector3.back, prof - usadoN - 2.5f, ref n);
        }
        PintarGrilla();
        DespejarCalles();
        EditorSceneManager.MarkSceneDirty(escena);
        Debug.Log($"[Pueblo] {n} casas nuevas en '{Raiz}'.");
    }

    const float RetiroCalle = 1.2f;     // suelo de manzana que queda alrededor de cada casa
    const float Fundido = 1.0f;         // borde suave entre ese suelo y la calle

    // Todo lo que queda entre las manzanas de la grilla es calle: se pinta parejo (algunas
    // calles del centro estaban pintadas a mano muy suave y se veían como tierra). Después
    // DespejarCalles devuelve a suelo de manzana lo que quede bajo casas y la iglesia.
    [MenuItem("Terrenos/Norte/Repintar calles del pueblo")]
    public static void RepintarCalles()
    {
        PintarGrilla();
        DespejarCalles();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    static void PintarGrilla()
    {
        var ter = Terrain.activeTerrain;
        var td = ter.terrainData; var tp = ter.transform.position;
        int aw = td.alphamapWidth, ah = td.alphamapHeight, L = td.alphamapLayers;
        float cel = td.size.x / aw;
        Undo.RegisterCompleteObjectUndo(td, "Repintar calles");
        var mapa = td.GetAlphamaps(0, 0, aw, ah);
        float gx0 = Cols[0][0] - AnchoCalle, gx1 = Cols[Cols.Length - 1][1] + AnchoCalle;
        float gz0 = Filas[Filas.Length - 1][0] - AnchoCalle, gz1 = Filas[0][1] + AnchoCalle;
        int n = 0;
        for (int z = 0; z < ah; z++) for (int x = 0; x < aw; x++)
        {
            float wx = tp.x + (x + 0.5f) * cel, wz = tp.z + (z + 0.5f) * cel;
            if (wx < gx0 || wx > gx1 || wz < gz0 || wz > gz1) continue;
            bool enManzana = false;
            foreach (var c in Cols) foreach (var f in Filas)
                if (wx >= c[0] && wx <= c[1] && wz >= f[0] && wz <= f[1]) enManzana = true;
            if (enManzana) continue;
            for (int l = 0; l < L; l++) mapa[z, x, l] = l == CapaCalle ? 1f : 0f;
            n++;
        }
        td.SetAlphamaps(0, 0, mapa);
        EditorUtility.SetDirty(td);
        Debug.Log($"[Pueblo] {n} celdas pintadas como calle en la grilla.");
    }

    // Las calles pintadas no deben pasar por debajo de las casas ni por el recinto de la
    // iglesia: ahí la capa de calle se cambia por la de manzana (con un borde suave).
    [MenuItem("Terrenos/Norte/Despejar calles bajo casas e iglesia")]
    public static void DespejarCalles()
    {
        var ter = Terrain.activeTerrain;
        var td = ter.terrainData; var tp = ter.transform.position;
        int aw = td.alphamapWidth, ah = td.alphamapHeight, L = td.alphamapLayers;
        float cel = td.size.x / aw;
        Undo.RegisterCompleteObjectUndo(td, "Despejar calles");
        var mapa = td.GetAlphamaps(0, 0, aw, ah);
        var cubrir = new float[ah, aw];

        // lotes: (de mundo a local, centro y semitamaño en local, escala, retiro en metros)
        var lotes = new List<(Matrix4x4 inv, Vector3 c, Vector3 half, float esc, float retiro, Bounds aabb)>();
        foreach (var nombre in new[] { "Casas", Raiz })
        {
            var r = GameObject.Find(nombre);
            if (!r) continue;
            foreach (Transform t in r.transform)
            {
                var box = t.GetComponent<BoxCollider>();
                if (!box) continue;
                lotes.Add((t.worldToLocalMatrix, box.center, box.size * 0.5f, t.lossyScale.x, RetiroCalle, box.bounds));
            }
        }
        var igl = GameObject.Find("Isluga");
        if (igl)
            foreach (var rd in igl.GetComponentsInChildren<Renderer>())
            {
                // el recinto (barandas) entero, y cada edificio con su retiro
                var b = rd.bounds;
                float retiro = rd.name.Contains("Barandas") ? 0.5f : RetiroCalle;
                lotes.Add((Matrix4x4.identity, b.center, b.extents, 1f, retiro, b));
            }

        foreach (var lo in lotes)
        {
            float ext = lo.retiro + Fundido;
            int ix0 = Mathf.Max(0, (int)((lo.aabb.min.x - ext - tp.x) / cel)), ix1 = Mathf.Min(aw - 1, (int)((lo.aabb.max.x + ext - tp.x) / cel));
            int iz0 = Mathf.Max(0, (int)((lo.aabb.min.z - ext - tp.z) / cel)), iz1 = Mathf.Min(ah - 1, (int)((lo.aabb.max.z + ext - tp.z) / cel));
            for (int z = iz0; z <= iz1; z++) for (int x = ix0; x <= ix1; x++)
            {
                var p = lo.inv.MultiplyPoint3x4(new Vector3(tp.x + (x + 0.5f) * cel, lo.c.y, tp.z + (z + 0.5f) * cel));
                float dx = Mathf.Max(Mathf.Abs(p.x - lo.c.x) - lo.half.x, 0f);
                float dz = Mathf.Max(Mathf.Abs(p.z - lo.c.z) - lo.half.z, 0f);
                float d = Mathf.Sqrt(dx * dx + dz * dz) * lo.esc - lo.retiro;
                float k = d <= 0f ? 1f : Mathf.Clamp01(1f - d / Fundido);
                if (k > cubrir[z, x]) cubrir[z, x] = k;
            }
        }
        int n = 0;
        for (int z = 0; z < ah; z++) for (int x = 0; x < aw; x++)
        {
            float k = cubrir[z, x];
            if (k <= 0f || mapa[z, x, CapaCalle] <= 0f) continue;
            float w = mapa[z, x, CapaCalle] * k;
            mapa[z, x, CapaCalle] -= w;
            mapa[z, x, CapaManzana] += w;
            n++;
        }
        td.SetAlphamaps(0, 0, mapa);
        EditorUtility.SetDirty(td);
        Debug.Log($"[Pueblo] {n} celdas de calle cambiadas a suelo de manzana bajo casas e iglesia.");
    }

    // Recorre un lado de la manzana colocando casas. borde = z de la calle; frente = hacia la calle.
    // Devuelve el fondo máximo usado.
    static float Hilera(GameObject raiz, List<Tipo> tipos, System.Random rnd, Terrain ter, List<Bounds> obst,
                        float x0, float x1, float zBorde, Vector3 frente, float fondoMax, ref int n)
    {
        float usado = 0f;
        float x = x0 + MargenCalle + (float)rnd.NextDouble() * 0.8f;
        int fallos = 0;
        string ultima = null;
        while (x < x1 - MargenCalle - 4f && fallos < 40)
        {
            float resto = x1 - MargenCalle - x;
            var cand = tipos.FindAll(t => t.Fachada <= resto && t.Fondo <= fondoMax);
            if (cand.Count == 0) break;
            // no repetir el mismo diseño al lado
            if (cand.Count > 1) cand.RemoveAll(t => t.Casa == ultima);
            var tipo = Elegir(cand, rnd);
            float retiro = MargenCalle + (float)rnd.NextDouble() * 0.7f;
            float cx = x + tipo.Fachada * 0.5f;
            float cz = zBorde - frente.z * (retiro + tipo.Fondo * 0.5f);
            var centro = new Vector3(cx, 0f, cz);
            var huella = new Bounds(centro, new Vector3(tipo.Fachada + Separacion * 2f, 50f, tipo.Fondo + Separacion * 2f));
            if (Choca(huella, obst) || Pendiente(ter, centro, tipo.Fachada, tipo.Fondo) > PendienteMax)
            {
                x += 1f; fallos++; continue;
            }
            Colocar(raiz, tipo, rnd, ter, centro, frente);
            obst.Add(new Bounds(centro, new Vector3(tipo.Fachada, 50f, tipo.Fondo)));
            usado = Mathf.Max(usado, retiro + tipo.Fondo);
            ultima = tipo.Casa;
            x += tipo.Fachada + Separacion + (float)rnd.NextDouble() * 0.6f;
            n++;
        }
        return usado;
    }

    static void Colocar(GameObject raiz, Tipo tipo, System.Random rnd, Terrain ter, Vector3 centro, Vector3 frente)
    {
        string v = "V" + (1 + rnd.Next(3));
        var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{CarpetaPrefab}/Norte_{tipo.Casa}_{v}.prefab");
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf, raiz.transform);
        Undo.RegisterCreatedObjectUndo(inst, "Poblar pueblo");
        // SignedAngle y no FromToRotation: con vectores opuestos ésta puede voltear la casa en X
        float yaw = Vector3.SignedAngle(tipo.Frente, frente, Vector3.up) + ((float)rnd.NextDouble() - 0.5f) * 3f;
        inst.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        inst.transform.localScale = Vector3.one * Escala;
        inst.transform.position = new Vector3(centro.x, Apoyo(ter, centro, tipo.Fachada, tipo.Fondo) - 0.05f, centro.z);
    }

    static Tipo Elegir(List<Tipo> cand, System.Random rnd)
    {
        float total = 0f; foreach (var t in cand) total += t.Peso;
        float r = (float)rnd.NextDouble() * total;
        foreach (var t in cand) { r -= t.Peso; if (r <= 0f) return t; }
        return cand[cand.Count - 1];
    }

    // Tamaños desde el BoxCollider de cada prefab; la puerta de cada diseño mira hacia:
    // Casa1 -z (frontis), Casa2 -x, Casa3 -x (lado largo), Casa4 -z, Casa5 +x (lado largo).
    static List<Tipo> Tipos()
    {
        var frentes = new Dictionary<string, Vector3> {
            { "Casa1", Vector3.back }, { "Casa2", Vector3.left }, { "Casa3", Vector3.left },
            { "Casa4", Vector3.back }, { "Casa5", Vector3.right } };
        var pesos = new Dictionary<string, float> {
            { "Casa1", 1.2f }, { "Casa2", 1.4f }, { "Casa3", 1.4f }, { "Casa4", 0.6f }, { "Casa5", 0.8f } };
        var lista = new List<Tipo>();
        foreach (var kv in frentes)
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{CarpetaPrefab}/Norte_{kv.Key}_V1.prefab");
            if (!pf) continue;
            var box = pf.GetComponent<BoxCollider>();
            Vector3 s = box.size * Escala;
            bool frenteEnX = Mathf.Abs(kv.Value.x) > 0.5f;
            lista.Add(new Tipo {
                Casa = kv.Key, Frente = kv.Value, Peso = pesos[kv.Key],
                Fachada = frenteEnX ? s.z : s.x, Fondo = frenteEnX ? s.x : s.z });
        }
        return lista;
    }

    static List<Bounds> Obstaculos(Terrain ter)
    {
        var obst = new List<Bounds>();
        void Agregar(GameObject g, float extra)
        {
            if (!g) return;
            var rs = g.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) { obst.Add(new Bounds(g.transform.position, Vector3.one * extra * 2f)); return; }
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            b.Expand(new Vector3(extra * 2f, 0f, extra * 2f));
            obst.Add(b);
        }
        Agregar(GameObject.Find("Isluga"), 2f);
        var casas = GameObject.Find("Casas");
        if (casas) foreach (Transform c in casas.transform) Agregar(c.gameObject, 0.5f);
        foreach (var n in new[] { "Player", "NPC", "Vicuña", "Vicuña (1)", "Vicuña (2)", "Vicuña (3)" })
        {
            var g = GameObject.Find(n);
            if (g) obst.Add(new Bounds(g.transform.position, new Vector3(8f, 50f, 8f)));
        }
        var q = GameObject.Find("Queñoas");
        if (q) foreach (Transform t in q.transform) obst.Add(new Bounds(t.position, new Vector3(8f, 50f, 8f)));
        var td = ter.terrainData; var tp = ter.transform.position;
        foreach (var ti in td.treeInstances)
        {
            var p = Vector3.Scale(ti.position, td.size) + tp;
            obst.Add(new Bounds(p, new Vector3(6f, 50f, 6f)));
        }
        return obst;
    }

    static bool Choca(Bounds b, List<Bounds> obst)
    {
        foreach (var o in obst)
            if (b.min.x < o.max.x && b.max.x > o.min.x && b.min.z < o.max.z && b.max.z > o.min.z) return true;
        return false;
    }

    static float Pendiente(Terrain ter, Vector3 c, float fx, float fz)
    {
        float mn = float.MaxValue, mx = float.MinValue;
        for (int i = -1; i <= 1; i++) for (int j = -1; j <= 1; j++)
        {
            float h = ter.SampleHeight(c + new Vector3(i * fx * 0.5f, 0f, j * fz * 0.5f));
            mn = Mathf.Min(mn, h); mx = Mathf.Max(mx, h);
        }
        return mx - mn;
    }

    // la esquina más baja: la casa nunca queda flotando (se entierra un poco en pendiente)
    static float Apoyo(Terrain ter, Vector3 c, float fx, float fz)
    {
        float mn = float.MaxValue;
        for (int i = -1; i <= 1; i++) for (int j = -1; j <= 1; j++)
            mn = Mathf.Min(mn, ter.SampleHeight(c + new Vector3(i * fx * 0.5f, 0f, j * fz * 0.5f)));
        return mn + ter.transform.position.y;
    }

    // Pinta las manzanas nuevas (Mud_Pebbles) y sus calles (Norte_Tierra), solo sobre suelo natural.
    static void PintarTerreno(Terrain ter)
    {
        var td = ter.terrainData; var tp = ter.transform.position;
        int aw = td.alphamapWidth, ah = td.alphamapHeight, L = td.alphamapLayers;
        if (L <= Mathf.Max(CapaCalle, CapaManzana)) return;
        Undo.RegisterCompleteObjectUndo(td, "Poblar pueblo: pintar calles");
        var mapa = td.GetAlphamaps(0, 0, aw, ah);
        float cel = td.size.x / aw;
        int pintadas = 0;
        foreach (var m in Manzanas)
        {
            if (!EsNueva(m[0], m[1])) continue;
            float bx0 = Cols[m[0]][0], bx1 = Cols[m[0]][1], bz0 = Filas[m[1]][0], bz1 = Filas[m[1]][1];
            int ix0 = Mathf.Max(0, (int)((bx0 - AnchoCalle - tp.x) / cel)), ix1 = Mathf.Min(aw - 1, (int)((bx1 + AnchoCalle - tp.x) / cel));
            int iz0 = Mathf.Max(0, (int)((bz0 - AnchoCalle - tp.z) / cel)), iz1 = Mathf.Min(ah - 1, (int)((bz1 + AnchoCalle - tp.z) / cel));
            for (int z = iz0; z <= iz1; z++) for (int x = ix0; x <= ix1; x++)
            {
                int dom = 0; for (int l = 1; l < L; l++) if (mapa[z, x, l] > mapa[z, x, dom]) dom = l;
                if (dom != CapaSuelo) continue;
                float wx = tp.x + (x + 0.5f) * cel, wz = tp.z + (z + 0.5f) * cel;
                bool dentro = wx >= bx0 && wx <= bx1 && wz >= bz0 && wz <= bz1;
                int capa = dentro ? CapaManzana : CapaCalle;
                for (int l = 0; l < L; l++) mapa[z, x, l] = l == capa ? 1f : 0f;
                pintadas++;
            }
        }
        td.SetAlphamaps(0, 0, mapa);
        EditorUtility.SetDirty(td);
        Debug.Log($"[Pueblo] {pintadas} celdas del terreno repintadas (calles y manzanas nuevas).");
    }
}
