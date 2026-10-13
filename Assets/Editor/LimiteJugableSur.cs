using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Arma las paredes invisibles de LimiteJugable en la escena Sur (menu Terrenos > Sur > Generar limite jugable).
//
// Por cada guia (contorno trazado a mano sobre una captura cenital del mapa, calzada contra el Terrain):
//   1. Grilla de 1 m. Una celda es "adentro" si el suelo esta por encima de nivelCosta y ademas cae dentro de
//      la guia o a menos de toleranciaGuia de ella. Las celdas a menos de margenPasarela de la pasarela
//      tambien cuentan, aunque esten sobre el agua o pasen la guia.
//   2. Relleno desde la celda mas alta dentro de la guia: queda solo esa isla, sin lo que toque la franja.
//   3. Borde exterior de esa mancha, simplificado (Douglas-Peucker).
//   4. Un BoxCollider por tramo, del lado de afuera de la linea, alto hasta altoSobreSuelo sobre el suelo.
// Asi la pared va por la costa real (el trazo a mano tiene +-10 m de error) y solo donde la guia cruza tierra
// manda la guia. Volver a correr si se regenera el terreno o se mueve la pasarela.
public static class LimiteJugableSur
{
    const float Celda = 1f;
    const float ToleranciaSimplificar = 0.75f;

    [MenuItem("Terrenos/Sur/Generar limite jugable")]
    static void Generar()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null || !AssetDatabase.GetAssetPath(terrain.terrainData).Contains("Sur"))
        {
            Debug.LogError("Abrir la escena Sur antes de generar el limite jugable.");
            return;
        }

        int capa = AsegurarCapa(LimiteJugable.NombreCapa);
        if (capa < 0)
        {
            Debug.LogError("No queda ninguna capa libre para " + LimiteJugable.NombreCapa + ".");
            return;
        }

        var limite = Object.FindFirstObjectByType<LimiteJugable>(FindObjectsInactive.Include);
        if (limite == null)
        {
            var go = new GameObject("LimiteJugable");
            Undo.RegisterCreatedObjectUndo(go, "Generar limite jugable");
            limite = go.AddComponent<LimiteJugable>();
            limite.guias = GuiasSur();
        }
        Undo.RegisterFullObjectHierarchyUndo(limite.gameObject, "Generar limite jugable");

        for (int i = limite.transform.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(limite.transform.GetChild(i).gameObject);

        var pasarelas = new List<Collider>();
        foreach (var p in Object.FindObjectsByType<PasarelaCurva>(FindObjectsSortMode.None))
            foreach (var c in p.GetComponentsInChildren<Collider>())
                if (!c.isTrigger) pasarelas.Add(c);

        var contornos = new List<LimiteJugable.Contorno>();
        int paredes = 0;
        foreach (var guia in limite.guias)
        {
            if (guia.puntos == null || guia.puntos.Length < 3) continue;
            var borde = Contorno(guia.puntos, limite, terrain, pasarelas);
            if (borde == null)
            {
                Debug.LogError("Guia " + guia.nombre + ": no hay tierra adentro.");
                continue;
            }

            var grupo = new GameObject(guia.nombre);
            grupo.layer = capa;
            grupo.transform.SetParent(limite.transform, false);
            paredes += CrearParedes(borde, grupo.transform, capa, limite, terrain);

            var puntos = new Vector3[borde.Count];
            for (int i = 0; i < borde.Count; i++)
                puntos[i] = new Vector3(borde[i].x, Mathf.Max(Suelo(terrain, borde[i]), 0f), borde[i].y);
            contornos.Add(new LimiteJugable.Contorno { nombre = guia.nombre, puntos = puntos });
        }

        limite.gameObject.layer = capa;
        limite.contornos = contornos.ToArray();
        EditorUtility.SetDirty(limite);
        EditorSceneManager.MarkSceneDirty(limite.gameObject.scene);
        Debug.Log($"Limite jugable: {contornos.Count} recintos, {paredes} paredes en la capa {LimiteJugable.NombreCapa}.");
    }

    // ---------------------------------------------------------------- contorno

    static List<Vector2> Contorno(Vector2[] guia, LimiteJugable lim, Terrain terrain, List<Collider> pasarelas)
    {
        Vector2 min = guia[0], max = guia[0];
        foreach (var p in guia) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        // Holgura de sobra: la pasarela puede pasar la guia bastante mas que la tolerancia.
        float margen = lim.toleranciaGuia + 60f;
        min -= Vector2.one * margen;
        max += Vector2.one * margen;
        int nx = Mathf.CeilToInt((max.x - min.x) / Celda), nz = Mathf.CeilToInt((max.y - min.y) / Celda);

        var adentro = new bool[nx, nz];
        var enGuia = new bool[nx, nz];
        float tol2 = lim.toleranciaGuia * lim.toleranciaGuia;
        for (int j = 0; j < nz; j++)
        for (int i = 0; i < nx; i++)
        {
            var p = new Vector2(min.x + (i + 0.5f) * Celda, min.y + (j + 0.5f) * Celda);
            enGuia[i, j] = DentroDe(guia, p);
            if (Suelo(terrain, p) <= lim.nivelCosta) continue;
            adentro[i, j] = enGuia[i, j] || Distancia2(guia, p) <= tol2;
        }

        foreach (var c in pasarelas)
        {
            Bounds b = c.bounds;
            b.Expand(lim.margenPasarela * 2f);
            int i0 = Mathf.Max(0, (int)((b.min.x - min.x) / Celda)), i1 = Mathf.Min(nx - 1, (int)((b.max.x - min.x) / Celda));
            int j0 = Mathf.Max(0, (int)((b.min.z - min.y) / Celda)), j1 = Mathf.Min(nz - 1, (int)((b.max.z - min.y) / Celda));
            for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                var p = new Vector3(min.x + (i + 0.5f) * Celda, c.bounds.center.y, min.y + (j + 0.5f) * Celda);
                Vector3 q = c.ClosestPoint(p);
                if (new Vector2(q.x - p.x, q.z - p.z).sqrMagnitude <= lim.margenPasarela * lim.margenPasarela)
                    adentro[i, j] = true;
            }
        }

        // Semilla: la celda mas alta dentro de la guia.
        int si = -1, sj = -1;
        float alto = float.MinValue;
        for (int j = 0; j < nz; j++)
        for (int i = 0; i < nx; i++)
        {
            if (!adentro[i, j] || !enGuia[i, j]) continue;
            float h = Suelo(terrain, new Vector2(min.x + (i + 0.5f) * Celda, min.y + (j + 0.5f) * Celda));
            if (h > alto) { alto = h; si = i; sj = j; }
        }
        if (si < 0) return null;

        var isla = new bool[nx, nz];
        var pila = new Stack<Vector2Int>();
        pila.Push(new Vector2Int(si, sj));
        isla[si, sj] = true;
        while (pila.Count > 0)
        {
            var c = pila.Pop();
            foreach (var d in Vecinos)
            {
                int i = c.x + d.x, j = c.y + d.y;
                if (i < 0 || j < 0 || i >= nx || j >= nz || isla[i, j] || !adentro[i, j]) continue;
                isla[i, j] = true;
                pila.Push(new Vector2Int(i, j));
            }
        }

        var esquinas = BordeExterior(isla, nx, nz);
        var borde = new List<Vector2>(esquinas.Count);
        foreach (var e in esquinas) borde.Add(new Vector2(min.x + e.x * Celda, min.y + e.y * Celda));
        return Simplificar(borde, ToleranciaSimplificar);
    }

    static readonly Vector2Int[] Vecinos = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

    // Borde exterior de la mancha como esquinas de celda, en sentido antihorario (adentro a la izquierda).
    // Cada celda aporta los lados que dan afuera, orientados antihorario alrededor de ella; se encadenan desde
    // el lado de abajo de la primera celda (fila mas baja), que siempre es del borde exterior.
    static List<Vector2Int> BordeExterior(bool[,] m, int nx, int nz)
    {
        var sale = new Dictionary<long, List<Vector2Int>>();
        bool In(int i, int j) => i >= 0 && j >= 0 && i < nx && j < nz && m[i, j];
        void Arista(int ax, int ay, int bx, int by)
        {
            long k = Clave(ax, ay);
            if (!sale.TryGetValue(k, out var l)) sale[k] = l = new List<Vector2Int>(1);
            l.Add(new Vector2Int(bx, by));
        }

        bool hay = false;
        var inicio = Vector2Int.zero;
        for (int j = 0; j < nz; j++)
        for (int i = 0; i < nx; i++)
        {
            if (!m[i, j]) continue;
            if (!hay) { hay = true; inicio = new Vector2Int(i, j); }
            if (!In(i, j - 1)) Arista(i, j, i + 1, j);
            if (!In(i + 1, j)) Arista(i + 1, j, i + 1, j + 1);
            if (!In(i, j + 1)) Arista(i + 1, j + 1, i, j + 1);
            if (!In(i - 1, j)) Arista(i, j + 1, i, j);
        }

        var res = new List<Vector2Int>();
        var v = inicio;
        var dir = new Vector2Int(1, 0);
        do
        {
            res.Add(v);
            var opciones = sale[Clave(v.x, v.y)];
            int elegida = 0;
            if (opciones.Count > 1)
            {
                // Esquina compartida por dos celdas en diagonal: doblar a la izquierda, rodeando la celda actual.
                var izq = new Vector2Int(-dir.y, dir.x);
                for (int k = 0; k < opciones.Count; k++)
                    if (opciones[k] - v == izq) { elegida = k; break; }
            }
            var sig = opciones[elegida];
            opciones.RemoveAt(elegida);
            dir = sig - v;
            v = sig;
        } while (v != inicio);
        return res;
    }

    static long Clave(int x, int y) => ((long)x << 32) | (uint)y;

    // Douglas-Peucker sobre un poligono cerrado: se parte en el punto 0 y el mas lejano a el.
    static List<Vector2> Simplificar(List<Vector2> p, float tol)
    {
        int lejos = 0;
        float dMax = -1f;
        for (int i = 1; i < p.Count; i++)
        {
            float d = (p[i] - p[0]).sqrMagnitude;
            if (d > dMax) { dMax = d; lejos = i; }
        }
        var marca = new bool[p.Count];
        marca[0] = marca[lejos] = true;
        DP(p, 0, lejos, tol, marca);
        DP(p, lejos, p.Count, tol, marca);
        var res = new List<Vector2>();
        for (int i = 0; i < p.Count; i++) if (marca[i]) res.Add(p[i]);
        return res;
    }

    static void DP(List<Vector2> p, int a, int b, float tol, bool[] marca)
    {
        if (b - a < 2) return;
        Vector2 pa = p[a], pb = p[b % p.Count];
        int peor = -1;
        float dPeor = tol;
        for (int i = a + 1; i < b; i++)
        {
            float d = Mathf.Sqrt(DistSegmento2(p[i], pa, pb));
            if (d > dPeor) { dPeor = d; peor = i; }
        }
        if (peor < 0) return;
        marca[peor] = true;
        DP(p, a, peor, tol, marca);
        DP(p, peor, b, tol, marca);
    }

    // ---------------------------------------------------------------- paredes

    static int CrearParedes(List<Vector2> borde, Transform padre, int capa, LimiteJugable lim, Terrain terrain)
    {
        // Antihorario = adentro a la izquierda, afuera a la derecha.
        float area = 0f;
        for (int i = 0; i < borde.Count; i++)
        {
            Vector2 a = borde[i], b = borde[(i + 1) % borde.Count];
            area += a.x * b.y - b.x * a.y;
        }
        if (area < 0f) borde.Reverse();

        for (int i = 0; i < borde.Count; i++)
        {
            Vector2 a = borde[i], b = borde[(i + 1) % borde.Count];
            float largo = Vector2.Distance(a, b);
            Vector2 d = (b - a) / largo;
            Vector2 afuera = new Vector2(d.y, -d.x);

            float hMin = float.MaxValue, hMax = float.MinValue;
            int pasos = Mathf.Max(1, Mathf.CeilToInt(largo / 2f));
            for (int k = 0; k <= pasos; k++)
            {
                float h = Suelo(terrain, Vector2.Lerp(a, b, k / (float)pasos));
                hMin = Mathf.Min(hMin, h);
                hMax = Mathf.Max(hMax, h);
            }
            float abajo = Mathf.Min(hMin, 0f) - 4f;
            float arriba = Mathf.Max(hMax, 0f) + lim.altoSobreSuelo;

            Vector2 c = (a + b) * 0.5f + afuera * (lim.grosor * 0.5f);
            var go = new GameObject("Pared_" + i.ToString("000"));
            go.layer = capa;
            go.transform.SetParent(padre, false);
            go.transform.SetPositionAndRotation(new Vector3(c.x, (abajo + arriba) * 0.5f, c.y),
                                                Quaternion.LookRotation(new Vector3(d.x, 0f, d.y)));
            var box = go.AddComponent<BoxCollider>();
            // Mas largo que el tramo para que las esquinas no dejen rendijas.
            box.size = new Vector3(lim.grosor, arriba - abajo, largo + lim.grosor);
        }
        return borde.Count;
    }

    // ---------------------------------------------------------------- utilidades

    static float Suelo(Terrain t, Vector2 p) => t.SampleHeight(new Vector3(p.x, 0f, p.y)) + t.transform.position.y;

    static bool DentroDe(Vector2[] poli, Vector2 p)
    {
        bool dentro = false;
        for (int i = 0, j = poli.Length - 1; i < poli.Length; j = i++)
        {
            Vector2 a = poli[i], b = poli[j];
            if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                dentro = !dentro;
        }
        return dentro;
    }

    static float Distancia2(Vector2[] poli, Vector2 p)
    {
        float d = float.MaxValue;
        for (int i = 0, j = poli.Length - 1; i < poli.Length; j = i++)
            d = Mathf.Min(d, DistSegmento2(p, poli[j], poli[i]));
        return d;
    }

    static float DistSegmento2(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return (a + ab * t - p).sqrMagnitude;
    }

    static int AsegurarCapa(string nombre)
    {
        int capa = LayerMask.NameToLayer(nombre);
        if (capa >= 0) return capa;
        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var capas = tm.FindProperty("layers");
        for (int i = 8; i < 32; i++)
        {
            var p = capas.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(p.stringValue)) continue;
            p.stringValue = nombre;
            tm.ApplyModifiedProperties();
            return i;
        }
        return -1;
    }

    static Vector2 V(float x, float z) => new Vector2(x, z);

    // Trazo del usuario (2026-10-12) sobre una captura cenital de Sur, calzada contra el Terrain
    // (2.18 m por pixel, 96% de coincidencia tierra/agua). Contorno exterior del trazo, en (x, z) del mundo.
    static LimiteJugable.Guia[] GuiasSur() => new[]
    {
        new LimiteJugable.Guia
        {
            nombre = "Isla O'Higgins",
            puntos = new[]
            {
                V(-221.0f, 1021.9f), V(-264.6f, 1008.8f), V(-295.1f, 989.2f), V(-343.1f, 941.2f), V(-362.7f, 915.1f), V(-380.1f, 882.4f),
                V(-382.3f, 864.9f), V(-371.4f, 858.4f), V(-373.6f, 779.9f), V(-364.9f, 740.7f), V(-345.3f, 705.8f), V(-319.1f, 688.3f),
                V(-292.9f, 660.0f), V(-284.2f, 575.0f), V(-260.2f, 492.1f), V(-245.0f, 470.3f), V(-229.7f, 457.3f), V(-216.6f, 450.7f),
                V(-197.0f, 450.7f), V(-164.3f, 468.2f), V(-146.9f, 485.6f), V(-125.1f, 516.1f), V(-83.7f, 555.4f), V(-59.7f, 596.8f),
                V(-53.1f, 618.6f), V(-51.0f, 677.4f), V(-35.7f, 771.2f), V(-35.7f, 799.5f), V(-22.6f, 847.5f), V(-22.6f, 878.0f),
                V(-29.2f, 936.9f), V(-44.4f, 967.4f), V(-61.9f, 989.2f), V(-88.0f, 1004.4f), V(-136.0f, 1017.5f),
            },
        },
        new LimiteJugable.Guia
        {
            nombre = "Isla Puerto Eden",
            puntos = new[]
            {
                V(-149.1f, 289.4f), V(-183.9f, 272.0f), V(-207.9f, 248.0f), V(-229.7f, 195.7f), V(-231.9f, 119.4f), V(-225.4f, 110.6f),
                V(-225.4f, 67.0f), V(-216.6f, -4.9f), V(-203.6f, -59.4f), V(-183.9f, -111.7f), V(-144.7f, -164.0f), V(-144.7f, -170.6f),
                V(-136.0f, -188.0f), V(-122.9f, -253.4f), V(-112.0f, -281.8f), V(-103.3f, -294.8f), V(-94.6f, -323.2f), V(-64.0f, -351.5f),
                V(-44.4f, -362.4f), V(-27.0f, -364.6f), V(-16.1f, -355.9f), V(-16.1f, -323.2f), V(-9.5f, -294.8f), V(-5.2f, -292.7f),
                V(-0.8f, -253.4f), V(-0.8f, -201.1f), V(-5.2f, -185.8f), V(-9.5f, -183.7f), V(-16.1f, -168.4f), V(-22.6f, -140.1f),
                V(-9.5f, -111.7f), V(-0.8f, -103.0f), V(12.3f, -74.7f), V(42.8f, -37.6f), V(58.0f, -26.7f), V(66.8f, -24.5f),
                V(88.6f, -0.5f), V(97.3f, 14.7f), V(95.1f, 47.4f), V(71.1f, 104.1f), V(64.6f, 143.3f), V(38.4f, 171.7f),
                V(-20.4f, 210.9f), V(-35.7f, 215.3f), V(-57.5f, 228.4f), V(-79.3f, 245.8f), V(-81.5f, 252.3f), V(-96.7f, 265.4f),
                V(-109.8f, 282.9f), V(-125.1f, 289.4f),
            },
        },
    };
}
