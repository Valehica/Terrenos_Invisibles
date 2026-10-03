using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Hunde la franja del perímetro del Terrain de Sur por debajo del nivel del mar.
///
/// El Terrain es un cuadrado de 1600 m y la tierra llegaba hasta el borde mismo.
/// Desde dentro de la escena eso se ve como un tajo recto: ahí se termina la
/// geometría detallada y empieza <c>MontanasLejanas</c>, que es otra malla con
/// otro shader. Hundiendo el perímetro, la juntura queda debajo del agua, donde
/// no se ve, y la isla grande del centro no se toca.
///
/// La máscara viene calculada desde <c>Tools/TerrenoSur/gen_common.py</c> y se
/// lee de <c>Tools/TerrenoSur/mascara_bordes.bin</c>, para que el terreno que se
/// regenere con los scripts y el que se parchea acá sean el mismo. El ancho de
/// la franja está modulado con ruido a propósito: con un ancho constante la
/// costa nueva sería un rectángulo, que es otro tajo, sólo que más disimulado.
///
/// Es idempotente: la primera pasada guarda las alturas originales en
/// <c>Tools/TerrenoSur/heights_previo.bin</c> y todas las pasadas siguientes
/// trabajan sobre ésas, no sobre el resultado de la anterior.
/// </summary>
public static class BordesDeTerreno
{
    const string RutaTerrain = "Assets/Terrain/TerrainData_Sur.asset";
    const string RutaMascara = "Tools/TerrenoSur/mascara_bordes.bin";
    const string RutaPrevio  = "Tools/TerrenoSur/heights_previo.bin";

    const float BaseY  = -90f;    // Terrain.position.y
    const float RangoY = 180f;    // TerrainData.size.y

    // Nombres de las capas, no índices: el orden dentro del TerrainData no está
    // garantizado y equivocarse acá pinta la playa de nieve sin avisar.
    const string CapaLecho  = "Sur_Lecho";
    const string CapaRibera = "Sur_Ribera";

    [MenuItem("Terrenos/Sur/Hundir bordes del terreno")]
    public static void Aplicar() { Ejecutar(true); }

    [MenuItem("Terrenos/Sur/Restaurar bordes del terreno")]
    public static void Restaurar() { Ejecutar(false); }

    static string Raiz => Directory.GetParent(Application.dataPath).FullName;

    public static string Ejecutar(bool hundir)
    {
        var data = AssetDatabase.LoadAssetAtPath<TerrainData>(RutaTerrain);
        if (data == null) return Fallo("No encuentro " + RutaTerrain);

        int res = data.heightmapResolution;
        string rutaPrevio = Path.Combine(Raiz, RutaPrevio);

        // ---- alturas originales ----
        float[,] h = data.GetHeights(0, 0, res, res);
        if (File.Exists(rutaPrevio))
        {
            var previo = LeerFloats(rutaPrevio, res * res);
            if (previo == null) return Fallo("El respaldo " + RutaPrevio + " no tiene el tamaño esperado.");
            Buffer.BlockCopy(previo, 0, h, 0, previo.Length * sizeof(float));
        }
        else
        {
            var plano = new float[res * res];
            Buffer.BlockCopy(h, 0, plano, 0, plano.Length * sizeof(float));
            EscribirFloats(rutaPrevio, plano);
            Debug.Log("[BordesDeTerreno] alturas originales guardadas en " + RutaPrevio);
        }

        if (!hundir)
        {
            data.SetHeights(0, 0, h);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            return Ok("Bordes restaurados a las alturas originales. El splatmap no se revierte: " +
                      "para eso hay que volver a correr gen_terrain.py.");
        }

        // ---- máscara ----
        string rutaMascara = Path.Combine(Raiz, RutaMascara);
        if (!File.Exists(rutaMascara)) return Fallo("No encuentro " + RutaMascara);

        float[] t; float profundidad;
        using (var br = new BinaryReader(File.OpenRead(rutaMascara)))
        {
            int n = br.ReadInt32();
            profundidad = br.ReadSingle();
            if (n != res) return Fallo("La máscara es de " + n + " y el heightmap de " + res + ".");
            t = new float[res * res];
            for (int i = 0; i < t.Length; i++) t[i] = br.ReadSingle();
        }

        // ---- hundir: nunca levanta el fondo, sólo baja lo que sobresale ----
        float bordeMax = float.NegativeInfinity;
        int tocados = 0;
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float m = t[z * res + x];
                if (m >= 0.999f) continue;
                float metros = h[z, x] * RangoY + BaseY;
                float objetivo = m * metros + (1f - m) * (-profundidad);
                if (objetivo < metros)
                {
                    h[z, x] = Mathf.Clamp01((objetivo - BaseY) / RangoY);
                    tocados++;
                }
            }
        for (int i = 0; i < res; i++)
        {
            bordeMax = Mathf.Max(bordeMax, h[0, i], h[res - 1, i], h[i, 0], h[i, res - 1]);
        }
        data.SetHeights(0, 0, h);
        float bordeMetros = bordeMax * RangoY + BaseY;

        // ---- splatmap: lo que quedó bajo el agua se pinta como lecho marino ----
        int iLecho = IndiceCapa(data, CapaLecho);
        int iRibera = IndiceCapa(data, CapaRibera);
        string avisoCapas = "";
        if (iLecho < 0) avisoCapas = " OJO: no encontré la capa " + CapaLecho + ", el splatmap quedó sin tocar.";
        else
        {
            int aw = data.alphamapWidth, ah = data.alphamapHeight, nc = data.alphamapLayers;
            float[,,] a = data.GetAlphamaps(0, 0, aw, ah);
            for (int z = 0; z < ah; z++)
                for (int x = 0; x < aw; x++)
                {
                    // el alphamap tiene un texel menos por lado que el heightmap
                    int hz = Mathf.Min(z * (res - 1) / ah, res - 1);
                    int hx = Mathf.Min(x * (res - 1) / aw, res - 1);
                    if (t[hz * res + hx] >= 0.999f) continue;

                    float metros = h[hz, hx] * RangoY + BaseY;
                    float sub = Mathf.Clamp01(-metros / 1.4f);
                    if (sub <= 0f) continue;

                    float suma = 0f;
                    for (int c = 0; c < nc; c++) { a[z, x, c] *= (1f - sub); suma += a[z, x, c]; }
                    a[z, x, iLecho] = sub * 2f; suma += sub * 2f;
                    if (iRibera >= 0)
                    {
                        float orilla = Mathf.Clamp01(1f - Mathf.Abs(metros + 1f) / 2.6f) * 0.8f;
                        a[z, x, iRibera] += orilla; suma += orilla;
                    }
                    if (suma < 1e-4f) { a[z, x, iLecho] = 1f; suma = 1f; }
                    for (int c = 0; c < nc; c++) a[z, x, c] /= suma;
                }
            data.SetAlphamaps(0, 0, a);
        }

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        return Ok(string.Format(
            "Bordes hundidos. {0} vértices bajados, el punto más alto del perímetro quedó en {1:0.00} m " +
            "(tiene que ser negativo).{2}", tocados, bordeMetros, avisoCapas));
    }

    static int IndiceCapa(TerrainData data, string nombre)
    {
        var capas = data.terrainLayers;
        for (int i = 0; i < capas.Length; i++)
            if (capas[i] != null && capas[i].name == nombre) return i;
        return -1;
    }

    static float[] LeerFloats(string ruta, int n)
    {
        var bytes = File.ReadAllBytes(ruta);
        if (bytes.Length != n * sizeof(float)) return null;
        var v = new float[n];
        Buffer.BlockCopy(bytes, 0, v, 0, bytes.Length);
        return v;
    }

    static void EscribirFloats(string ruta, float[] v)
    {
        var bytes = new byte[v.Length * sizeof(float)];
        Buffer.BlockCopy(v, 0, bytes, 0, bytes.Length);
        Directory.CreateDirectory(Path.GetDirectoryName(ruta));
        File.WriteAllBytes(ruta, bytes);
    }

    static string Ok(string m) { Debug.Log("[BordesDeTerreno] " + m); return "OK: " + m; }
    static string Fallo(string m) { Debug.LogError("[BordesDeTerreno] " + m); return "ERROR: " + m; }
}
