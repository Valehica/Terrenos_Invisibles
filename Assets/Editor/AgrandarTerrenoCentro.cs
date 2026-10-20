using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Agranda el Terrain de Centro (menu Terrenos > Centro > Agrandar terreno) para que no se vea donde se corta.
//
// El Terrain original media 1000 x 800 m y terminaba en seco: al oeste cortaba tierra (cerros de hasta 114 m)
// y al sur y al este cortaba el fondo marino a -5/-12 m, donde el agua todavia se ve turquesa. Pasado el borde
// no hay nada en el buffer de profundidad y el Mar lo pinta como agua honda, asi que se veia un escalon recto.
//
// Pasa a 2000 x 1600 m con el terreno viejo en el centro, sin remuestrear nada: con 1025 de heightmap, 1024 de
// alphamap y 2048 de detalle el paso por muestra queda igual que antes (1.953 x 1.5625 m), y lo viejo se copia
// tal cual corrido 256 muestras (512 en el detalle). Los arboles solo cambian de coordenadas normalizadas.
//
// Lo nuevo sigue lo que habia en el borde y se hunde hasta Hondo:
//   - El valor del borde se estira hacia afuera con un desenfoque que crece con la distancia, para que no queden
//     franjas perpendiculares al borde (estirar el valor tal cual dibuja rayas).
//   - La distancia a la que se hunde varia con ruido en coordenadas de mundo: con un ancho fijo la costa nueva
//     seria otro rectangulo, otro tajo pero mas disimulado.
//   - En los ultimos MargenBorde metros todo baja a Hondo, para que el borde nuevo quede siempre bajo agua opaca
//     (el Mar llega al color hondo a los 16 m, _ProfundidadPlena).
//   - Una franja de Banda muestras a cada lado de la juntura se suaviza, para que no quede un quiebre.
// El splatmap de afuera es el del borde (desenfocado igual), con arena en la orilla y roca en lo empinado.
// El detalle (pasto) no se extiende: lo nuevo esta a mas de 250 m de la zona jugable y el pasto se dibuja a 75.
//
// Respaldo del asset original: Terrenos_Invisibles_RESPALDO_borrados/Terrain_Centro_2026-10-14.
public static class AgrandarTerrenoCentro
{
    const int ResVieja = 513, ResNueva = 1025, Corrimiento = 256;
    static readonly Vector3 TamViejo = new Vector3(1000f, 300f, 800f);
    static readonly Vector3 TamNuevo = new Vector3(2000f, 300f, 1600f);

    const float Hondo = -26f;           // m, fondo en el borde nuevo
    const float HundirMin = 220f;       // m, distancia en la que el borde baja hasta Hondo (con ruido)
    const float HundirMax = 420f;
    const float HundirPorMetro = 1.2f;  // los cerros se hunden mas lejos: m extra por m de altura en el borde
    const float MargenBorde = 150f;     // m, franja final que se fuerza a Hondo
    const int Banda = 12;               // muestras a cada lado de la juntura que se suavizan

    const string CapaArena = "Centro_Sand";
    const string CapaRoca = "Centro_Rock";

    [MenuItem("Terrenos/Centro/Agrandar terreno")]
    static void Agrandar()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null || EditorSceneManager.GetActiveScene().name != "Centro")
        {
            Debug.LogError("Abrir la escena Centro antes de agrandar el terreno.");
            return;
        }
        var d = terrain.terrainData;
        if (d.heightmapResolution != ResVieja || d.size != TamViejo || d.alphamapResolution != 512 || d.detailResolution != 1024)
        {
            Debug.LogError("El terreno no tiene el tamaño original (1000 x 800, 513): ¿ya se agrando?");
            return;
        }

        float oy = terrain.transform.position.y, sy = d.size.y;
        float dx = TamNuevo.x / (ResNueva - 1), dz = TamNuevo.z / (ResNueva - 1);
        Vector3 origen = terrain.transform.position - new Vector3(Corrimiento * dx, 0f, Corrimiento * dz);

        // ---- leer todo antes de tocar resoluciones (cada setter borra sus datos) ----
        var hVieja = d.GetHeights(0, 0, ResVieja, ResVieja);
        int nc = d.alphamapLayers;
        var aVieja = d.GetAlphamaps(0, 0, 512, 512);
        int nDet = d.detailPrototypes.Length, ppp = d.detailResolutionPerPatch;
        var detalles = new int[nDet][,];
        for (int l = 0; l < nDet; l++) detalles[l] = d.GetDetailLayer(0, 0, 1024, 1024, l);
        var arboles = d.treeInstances;
        var huecos = d.GetHoles(0, 0, 512, 512);

        // ---- alturas ----
        int n = ResNueva;
        int i0 = Corrimiento, i1 = Corrimiento + ResVieja - 1;
        var metros = new float[n * n];
        var extendida = new float[n * n];
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            int ci = Mathf.Clamp(i, i0, i1) - i0, cj = Mathf.Clamp(j, i0, i1) - i0;
            extendida[j * n + i] = hVieja[cj, ci] * sy + oy;
        }
        var borde = new Difusor(extendida, n, n);

        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            int k = j * n + i;
            float fx = (Mathf.Clamp(i, i0, i1) - i) * dx, fz = (Mathf.Clamp(j, i0, i1) - j) * dz;
            float dist = Mathf.Sqrt(fx * fx + fz * fz);
            if (dist <= 0f) { metros[k] = extendida[k]; continue; }

            float wx = origen.x + i * dx, wz = origen.z + j * dz;
            float hb = borde.Muestra(i, j, 0.45f * dist / ((dx + dz) * 0.5f));
            float hundir = Mathf.Lerp(HundirMin, HundirMax, Mathf.PerlinNoise(wx / 350f + 17.3f, wz / 350f + 4.1f))
                           + Mathf.Max(0f, hb) * HundirPorMetro;
            float s = Mathf.SmoothStep(0f, 1f, dist / hundir);
            float h = Mathf.Lerp(hb, Hondo, s);

            // Costa irregular en tierra, fondo ondulado en el agua.
            float ruido = Mathf.PerlinNoise(wx / 90f + 31.7f, wz / 90f + 8.9f) - 0.5f
                        + 0.5f * (Mathf.PerlinNoise(wx / 33f + 2.2f, wz / 33f + 50.1f) - 0.5f);
            float tierra = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-2f, 4f, hb));
            h += ruido * (1f - s) * Mathf.Lerp(3f, 8f, tierra);

            float alBorde = Mathf.Min(Mathf.Min(i * dx, (n - 1 - i) * dx), Mathf.Min(j * dz, (n - 1 - j) * dz));
            h = Mathf.Lerp(Hondo, h, Mathf.SmoothStep(0f, 1f, alBorde / MargenBorde));
            metros[k] = h;
        }

        // Suavizar la juntura (a los dos lados) para que no quede un quiebre de pendiente.
        var suave = Desenfocar(metros, n, n, 3);
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            int dentro = Mathf.Min(Mathf.Min(i - i0, i1 - i), Mathf.Min(j - i0, i1 - j));
            float dDentro = dentro >= 0 ? dentro : Mathf.Sqrt(Sq(Mathf.Max(0, Mathf.Max(i0 - i, i - i1))) + Sq(Mathf.Max(0, Mathf.Max(i0 - j, j - i1))));
            float w = 1f - dDentro / Banda;
            if (w <= 0f) continue;
            int k = j * n + i;
            metros[k] = Mathf.Lerp(metros[k], suave[k], w);
        }

        var hNueva = new float[n, n];
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
            hNueva[j, i] = Mathf.Clamp01((metros[j * n + i] - oy) / sy);

        // ---- splatmap ----
        int na = 1024, a0 = Corrimiento, a1 = Corrimiento + 511;
        int iArena = IndiceCapa(d, CapaArena), iRoca = IndiceCapa(d, CapaRoca);
        if (iArena < 0 || iRoca < 0)
        {
            Debug.LogError("No encuentro las capas " + CapaArena + " / " + CapaRoca + ".");
            return;
        }
        var aNueva = new float[na, na, nc];
        var capas = new Difusor[nc];
        for (int c = 0; c < nc; c++)
        {
            var e = new float[na * na];
            for (int j = 0; j < na; j++)
            for (int i = 0; i < na; i++)
                e[j * na + i] = aVieja[Mathf.Clamp(j, a0, a1) - a0, Mathf.Clamp(i, a0, a1) - a0, c];
            capas[c] = new Difusor(e, na, na);
        }
        var pesos = new float[nc];
        for (int j = 0; j < na; j++)
        for (int i = 0; i < na; i++)
        {
            if (i >= a0 && i <= a1 && j >= a0 && j <= a1)
            {
                for (int c = 0; c < nc; c++) aNueva[j, i, c] = aVieja[j - a0, i - a0, c];
                continue;
            }
            // El texel (i, j) cubre las muestras de altura (i, j)-(i+1, j+1); se toma el centro.
            float fx = (Mathf.Clamp(i, a0, a1) - i) * dx, fz = (Mathf.Clamp(j, a0, a1) - j) * dz;
            float r = 0.45f * Mathf.Sqrt(fx * fx + fz * fz) / ((dx + dz) * 0.5f);
            float h = (metros[j * n + i] + metros[j * n + i + 1] + metros[(j + 1) * n + i] + metros[(j + 1) * n + i + 1]) * 0.25f;
            float hb = borde.Muestra(i, j, r);
            float pendiente = Mathf.Atan(new Vector2((metros[j * n + i + 1] - metros[j * n + i]) / dx,
                                                     (metros[(j + 1) * n + i] - metros[j * n + i]) / dz).magnitude) * Mathf.Rad2Deg;

            float suma = 0f;
            for (int c = 0; c < nc; c++) { pesos[c] = Mathf.Max(0f, capas[c].Muestra(i, j, r)); suma += pesos[c]; }
            if (suma < 1e-4f) { pesos[iArena] = 1f; suma = 1f; }
            for (int c = 0; c < nc; c++) pesos[c] /= suma;

            // Bajo el agua: lo que tuviera el borde si alli tambien era fondo; si el borde era tierra, arena.
            float bordeEsFondo = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, -2f, hb));
            float arena = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.5f, 3.5f, h));
            arena *= h < -3f ? 1f - bordeEsFondo : 1f;
            float roca = h > 1f ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(30f, 42f, pendiente)) : 0f;
            for (int c = 0; c < nc; c++) pesos[c] *= (1f - arena) * (1f - roca);
            pesos[iArena] += arena * (1f - roca);
            pesos[iRoca] += roca;
            for (int c = 0; c < nc; c++) aNueva[j, i, c] = pesos[c];
        }

        // ---- aplicar ----
        d.heightmapResolution = ResNueva;
        d.size = TamNuevo;
        d.SetHeights(0, 0, hNueva);

        d.alphamapResolution = na;
        d.SetAlphamaps(0, 0, aNueva);
        d.baseMapResolution = 2048;

        d.SetDetailResolution(2048, ppp);
        for (int l = 0; l < nDet; l++)
        {
            var det = new int[2048, 2048];
            for (int j = 0; j < 1024; j++)
            for (int i = 0; i < 1024; i++)
                det[j + 512, i + 512] = detalles[l][j, i];
            d.SetDetailLayer(0, 0, l, det);
        }

        var huecosNuevos = new bool[1024, 1024];
        for (int j = 0; j < 1024; j++)
        for (int i = 0; i < 1024; i++)
            huecosNuevos[j, i] = true;
        for (int j = 0; j < 512; j++)
        for (int i = 0; i < 512; i++)
            huecosNuevos[j + a0, i + a0] = huecos[j, i];
        d.SetHoles(0, 0, huecosNuevos);

        // Arboles: mismas coordenadas de mundo; la altura se corrige por lo que movio el suavizado de la juntura.
        for (int k = 0; k < arboles.Length; k++)
        {
            var p = arboles[k].position;
            var nx = (p.x * TamViejo.x + Corrimiento * dx) / TamNuevo.x;
            var nz = (p.z * TamViejo.z + Corrimiento * dz) / TamNuevo.z;
            arboles[k].position = new Vector3(nx, d.GetInterpolatedHeight(nx, nz) / sy, nz);
        }
        d.treeInstances = arboles;

        Undo.RecordObject(terrain.transform, "Agrandar terreno");
        terrain.transform.position = origen;

        EditorUtility.SetDirty(d);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);

        float bordeMax = float.MinValue;
        for (int q = 0; q < n; q++)
            bordeMax = Mathf.Max(bordeMax, Mathf.Max(metros[q], metros[(n - 1) * n + q]), Mathf.Max(metros[q * n], metros[q * n + n - 1]));
        Debug.Log($"Terreno de Centro agrandado a {TamNuevo.x} x {TamNuevo.z} m, posicion {origen}. " +
                  $"Punto mas alto del borde nuevo: {bordeMax:0.0} m.");
    }

    static float Sq(float v) => v * v;

    static int IndiceCapa(TerrainData d, string nombre)
    {
        var capas = d.terrainLayers;
        for (int i = 0; i < capas.Length; i++)
            if (capas[i] != null && capas[i].name == nombre) return i;
        return -1;
    }

    // Caja separable con el radio dado, dos pasadas (casi gaussiano), bordes recortados.
    static float[] Desenfocar(float[] f, int w, int h, int radio)
    {
        var a = (float[])f.Clone();
        var b = new float[f.Length];
        for (int pasada = 0; pasada < 2; pasada++)
        {
            Caja(a, b, w, h, radio, true);
            Caja(b, a, w, h, radio, false);
        }
        return a;
    }

    static void Caja(float[] src, float[] dst, int w, int h, int r, bool horizontal)
    {
        int largo = horizontal ? w : h, lineas = horizontal ? h : w;
        var acum = new double[largo + 1];
        for (int l = 0; l < lineas; l++)
        {
            for (int t = 0; t < largo; t++)
                acum[t + 1] = acum[t] + src[horizontal ? l * w + t : t * w + l];
            for (int t = 0; t < largo; t++)
            {
                int lo = Mathf.Max(0, t - r), hi = Mathf.Min(largo - 1, t + r);
                dst[horizontal ? l * w + t : t * w + l] = (float)((acum[hi + 1] - acum[lo]) / (hi - lo + 1));
            }
        }
    }

    // El campo desenfocado a varios radios; Muestra interpola entre los dos niveles que encierran el radio pedido.
    class Difusor
    {
        static readonly int[] Radios = { 0, 3, 8, 20, 48, 110 };
        readonly float[][] niveles;
        readonly int w;

        public Difusor(float[] campo, int w, int h)
        {
            this.w = w;
            niveles = new float[Radios.Length][];
            niveles[0] = campo;
            for (int k = 1; k < Radios.Length; k++) niveles[k] = Desenfocar(campo, w, h, Radios[k]);
        }

        public float Muestra(int i, int j, float radio)
        {
            int idx = j * w + i;
            if (radio <= 0f) return niveles[0][idx];
            for (int k = 1; k < Radios.Length; k++)
            {
                if (radio > Radios[k]) continue;
                float t = (radio - Radios[k - 1]) / (Radios[k] - Radios[k - 1]);
                return Mathf.Lerp(niveles[k - 1][idx], niveles[k][idx], t);
            }
            return niveles[Radios.Length - 1][idx];
        }
    }
}
