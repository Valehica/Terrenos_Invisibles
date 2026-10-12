using System.IO;
using UnityEditor;
using UnityEngine;

// Genera las dos texturas del agua Terrenos/Mar. Todo es periodico (ruido de
// valor y Voronoi con la grilla envuelta), asi que las texturas repiten sin costura.
//
//   Mar_Ondas_N.png  normal map de ondas chicas (40 senoides periodicas), importado como Normal map.
//   Mar_Ruido.png    lineal, sin sRGB:
//                      R = bordes de Voronoi (F2 - F1): las cuerdas de las causticas
//                      G = fbm suave: corta el borde de la espuma
//                      B = puntos dispersos: destellos del sol
public static class GenerarTexturasMar
{
    const string Carpeta = "Assets/Textures/Mar";
    const int Lado = 512;

    [MenuItem("Terrenos/Agua/Generar texturas del mar")]
    public static void Generar()
    {
        Directory.CreateDirectory(Carpeta);
        string rutaN = Carpeta + "/Mar_Ondas_N.png";
        string rutaR = Carpeta + "/Mar_Ruido.png";
        File.WriteAllBytes(rutaN, Ondas().EncodeToPNG());
        File.WriteAllBytes(rutaR, Ruido().EncodeToPNG());
        AssetDatabase.Refresh();

        var imp = (TextureImporter)AssetImporter.GetAtPath(rutaN);
        imp.textureType = TextureImporterType.NormalMap;
        imp.wrapMode = TextureWrapMode.Repeat;
        imp.anisoLevel = 4;
        imp.SaveAndReimport();

        imp = (TextureImporter)AssetImporter.GetAtPath(rutaR);
        imp.textureType = TextureImporterType.Default;
        imp.sRGBTexture = false;
        imp.alphaSource = TextureImporterAlphaSource.None;
        imp.wrapMode = TextureWrapMode.Repeat;
        imp.textureCompression = TextureImporterCompression.CompressedHQ;
        imp.SaveAndReimport();
        Debug.Log("Texturas del mar generadas en " + Carpeta);
    }

    static Texture2D Ondas()
    {
        // Altura = suma de senoides con vectores de onda enteros (repiten exacto en
        // la textura y no tienen grilla), con direcciones agrupadas alrededor de un
        // eje para que las ondas tengan un sentido, y amplitud ~ 1/|k|^1.6 como un
        // espectro de mar. La pendiente sale analitica.
        const int nOndas = 40;
        var kx = new float[nOndas]; var ky = new float[nOndas];
        var amp = new float[nOndas]; var fase = new float[nOndas];
        var azar = new System.Random(7);
        for (int i = 0; i < nOndas; i++)
        {
            float mod = Mathf.Lerp(3f, 26f, Mathf.Pow((float)azar.NextDouble(), 1.4f));
            float ang = (float)(azar.NextDouble() * 2 - 1) * 1.1f + (azar.NextDouble() < 0.25 ? Mathf.PI * 0.5f : 0f);
            kx[i] = Mathf.Round(mod * Mathf.Cos(ang));
            ky[i] = Mathf.Round(mod * Mathf.Sin(ang));
            if (kx[i] == 0 && ky[i] == 0) kx[i] = 3;
            amp[i] = 1f / Mathf.Pow(Mathf.Sqrt(kx[i] * kx[i] + ky[i] * ky[i]), 1.6f);
            fase[i] = (float)(azar.NextDouble() * Mathf.PI * 2);
        }

        var gx = new float[Lado * Lado]; var gy = new float[Lado * Lado];
        float max = 0;
        for (int y = 0; y < Lado; y++)
            for (int x = 0; x < Lado; x++)
            {
                float u = (float)x / Lado, v = (float)y / Lado, dx = 0, dy = 0;
                for (int i = 0; i < nOndas; i++)
                {
                    float c = Mathf.Cos(2 * Mathf.PI * (kx[i] * u + ky[i] * v) + fase[i]) * amp[i];
                    dx += c * kx[i]; dy += c * ky[i];
                }
                gx[y * Lado + x] = dx; gy[y * Lado + x] = dy;
                max = Mathf.Max(max, Mathf.Sqrt(dx * dx + dy * dy));
            }

        // La pendiente mas fuerte queda en ~50 grados; el shader la escala con _OndasFuerza.
        float escala = 1.2f / max;
        var tex = new Texture2D(Lado, Lado, TextureFormat.RGB24, false, true);
        var px = new Color[Lado * Lado];
        for (int j = 0; j < px.Length; j++)
        {
            var n = new Vector3(-gx[j] * escala, -gy[j] * escala, 1f).normalized;
            px[j] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f);
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    static Texture2D Ruido()
    {
        var tex = new Texture2D(Lado, Lado, TextureFormat.RGB24, false, true);
        var px = new Color[Lado * Lado];
        for (int y = 0; y < Lado; y++)
            for (int x = 0; x < Lado; x++)
            {
                float u = (float)x / Lado, v = (float)y / Lado;

                // Causticas: bordes de Voronoi, finos y brillantes donde F2 ~ F1.
                Voronoi(u * 8, v * 8, 8, 5, out float f1, out float f2);
                float borde = Mathf.Clamp01(1f - (f2 - f1) * 3.2f);
                float causticas = borde * borde * borde;

                float fbm = 0, amp = 0.5f;
                for (int o = 0; o < 4; o++) { int per = 6 << o; fbm += amp * Valor(u * per, v * per, per, per, 101 + o * 13); amp *= 0.5f; }
                fbm = Mathf.Clamp01((fbm / 0.9375f - 0.5f) * 1.6f + 0.5f);

                // Destellos: un punto chico en algunas celdas de una grilla de 64.
                Voronoi(u * 64, v * 64, 64, 9, out float d1, out _);
                float semilla = Hash(Mathf.Floor(u * 64), Mathf.Floor(v * 64), 23);
                float destello = semilla > 0.55f ? Mathf.Clamp01(1f - d1 / 0.22f) : 0f;

                px[y * Lado + x] = new Color(causticas, fbm, destello);
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    static float Hash(float x, float y, int s)
    {
        float h = Mathf.Sin(x * 127.1f + y * 311.7f + s * 74.7f) * 43758.5453f;
        return h - Mathf.Floor(h);
    }

    // Ruido de valor periodico: la grilla envuelve cada perX / perY celdas.
    static float Valor(float x, float y, int perX, int perY, int s)
    {
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        int x0 = ((ix % perX) + perX) % perX, x1 = (x0 + 1) % perX;
        int y0 = ((iy % perY) + perY) % perY, y1 = (y0 + 1) % perY;
        float a = Hash(x0, y0, s), b = Hash(x1, y0, s), c = Hash(x0, y1, s), d = Hash(x1, y1, s);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    // Voronoi periodico: distancia al punto mas cercano (f1) y al segundo (f2).
    static void Voronoi(float x, float y, int per, int s, out float f1, out float f2)
    {
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        f1 = 9; f2 = 9;
        for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                int cx = ix + i, cy = iy + j;
                int wx = ((cx % per) + per) % per, wy = ((cy % per) + per) % per;
                float px = cx + Hash(wx, wy, s), py = cy + Hash(wx, wy, s + 7);
                float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                if (d < f1) { f2 = f1; f1 = d; }
                else if (d < f2) f2 = d;
            }
    }
}
