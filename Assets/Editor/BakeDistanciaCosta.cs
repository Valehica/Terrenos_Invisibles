using UnityEditor;
using UnityEngine;

// Bakea la distancia horizontal a la linea de costa para el shader de agua
// Terrenos/OceanWaterRealista. Las olas de rompiente avanzan por esa distancia,
// asi que siguen la forma de la costa en vez de viajar en una direccion fija.
//
// Sale del mismo heightmap bakeado que ya usa el agua (_ShoreHeightMap), no del
// Terrain: el mapa ya tiene el origen y el tamano que espera el shader.
//
// Textura RGBAHalf:
//   R = distancia con signo a la costa en metros (positiva mar adentro, negativa en tierra)
//   G, B = direccion XZ hacia la costa (unitaria), del gradiente de la distancia suavizada
//   A = 1
public static class BakeDistanciaCosta
{
    const float Infinito = 1e10f;   // mas que cualquier distancia^2 real (1024^2*2)

    [MenuItem("Terrenos/Agua/Bakear distancia a la costa")]
    static void BakearDesdeEscena()
    {
        var sea = GameObject.Find("Sea");
        if (sea == null) { Debug.LogError("No hay un GameObject 'Sea' en la escena activa."); return; }
        var mat = sea.GetComponent<MeshRenderer>().sharedMaterial;
        var marea = sea.GetComponent("MareaController");
        float nivel = 0f;
        if (marea != null)
        {
            var campo = marea.GetType().GetField("nivelMedio");
            if (campo != null) nivel = (float)campo.GetValue(marea);
        }
        Bakear(mat, nivel);
    }

    public static Texture2D Bakear(Material mat, float nivelDelMar)
    {
        var hm = mat.GetTexture("_ShoreHeightMap") as Texture2D;
        if (hm == null) { Debug.LogError("El material no tiene _ShoreHeightMap."); return null; }

        Vector4 tam = mat.GetVector("_ShoreMapSize");
        int w = hm.width, h = hm.height;
        float texelX = tam.x / w, texelZ = tam.y / h;
        if (Mathf.Abs(texelX - texelZ) > 1e-3f)
            Debug.LogWarning("El heightmap no tiene texeles cuadrados; la distancia usa el promedio.");
        float texel = 0.5f * (texelX + texelZ);

        var alturas = hm.GetPixels();
        var esTierra = new bool[w * h];
        for (int i = 0; i < w * h; i++) esTierra[i] = alturas[i].r > nivelDelMar;

        // Distancia al texel del otro lado de la costa, en los dos sentidos.
        float[] alMar = DistanciaEuclidea(esTierra, w, h, true);    // en el mar: distancia a tierra
        float[] aTierra = DistanciaEuclidea(esTierra, w, h, false); // en tierra: distancia al mar

        var dist = new float[w * h];
        for (int i = 0; i < w * h; i++)
        {
            // El -0.5 pone el cero en el borde entre texeles, no en su centro.
            dist[i] = esTierra[i] ? -(Mathf.Sqrt(aTierra[i]) - 0.5f) * texel
                                  :  (Mathf.Sqrt(alMar[i]) - 0.5f) * texel;
            dist[i] = Mathf.Clamp(dist[i], -1e4f, 1e4f);   // sin costa en la linea: "muy lejos"
        }

        // La direccion sale de una copia suavizada: el gradiente crudo de una
        // distancia euclidea tiene quiebres en el eje medial (ensenadas, puntas)
        // y las crestas se partirian ahi.
        float[] suave = Desenfocar(dist, w, h, 6, 3);

        var px = new Color[w * h];
        for (int z = 0; z < h; z++)
        for (int x = 0; x < w; x++)
        {
            int i = z * w + x;
            int x0 = Mathf.Max(x - 1, 0), x1 = Mathf.Min(x + 1, w - 1);
            int z0 = Mathf.Max(z - 1, 0), z1 = Mathf.Min(z + 1, h - 1);
            float gx = suave[z * w + x1] - suave[z * w + x0];
            float gz = suave[z1 * w + x] - suave[z0 * w + x];
            var g = new Vector2(gx, gz);
            // La costa esta bajando la distancia: la direccion es menos el gradiente.
            Vector2 haciaCosta = g.sqrMagnitude > 1e-10f ? -g.normalized : Vector2.zero;
            px[i] = new Color(dist[i], haciaCosta.x, haciaCosta.y, 1f);
        }

        var tex = new Texture2D(w, h, TextureFormat.RGBAHalf, false, true)
        {
            name = "ShoreDistance",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        tex.SetPixels(px);
        tex.Apply(false, false);

        string rutaHm = AssetDatabase.GetAssetPath(hm);
        string ruta = System.IO.Path.GetDirectoryName(rutaHm).Replace('\\', '/') + "/"
                    + hm.name.Replace("TerrainHeight", "ShoreDistance") + ".asset";
        if (ruta.EndsWith(hm.name + ".asset")) ruta = ruta.Replace(".asset", "_Distancia.asset");

        tex.name = System.IO.Path.GetFileNameWithoutExtension(ruta);   // Unity avisa si no coincide con el archivo
        var existente = AssetDatabase.LoadAssetAtPath<Texture2D>(ruta);
        if (existente != null)
        {
            EditorUtility.CopySerialized(tex, existente);
            existente.name = tex.name;
            tex = existente;
        }
        else AssetDatabase.CreateAsset(tex, ruta);
        EditorUtility.SetDirty(tex);
        AssetDatabase.SaveAssets();

        if (mat.HasProperty("_ShoreDistMap"))
        {
            Undo.RecordObject(mat, "Asignar distancia a la costa");
            mat.SetTexture("_ShoreDistMap", tex);
            EditorUtility.SetDirty(mat);
        }

        float maxMar = 0; foreach (var d in dist) if (d < 1e4f) maxMar = Mathf.Max(maxMar, d);
        Debug.Log($"Distancia a la costa bakeada en {ruta} ({w}x{h}, texel {texel:F2} m, nivel {nivelDelMar} m, max {maxMar:F0} m).");
        return tex;
    }

    // Transformada de distancia euclidea al cuadrado (Felzenszwalb & Huttenlocher),
    // en texeles. Devuelve, para cada texel del lado "origen", la distancia al
    // texel mas cercano del otro lado. Los del otro lado quedan en 0.
    static float[] DistanciaEuclidea(bool[] esTierra, int w, int h, bool origenEsMar)
    {
        var f = new float[w * h];
        for (int i = 0; i < w * h; i++)
        {
            bool origen = origenEsMar ? !esTierra[i] : esTierra[i];
            f[i] = origen ? Infinito : 0f;
        }

        int n = Mathf.Max(w, h);
        var linea = new float[n];
        var salida = new float[n];
        var v = new int[n];
        var zz = new float[n + 1];

        for (int x = 0; x < w; x++)
        {
            for (int z = 0; z < h; z++) linea[z] = f[z * w + x];
            Dt1D(linea, h, salida, v, zz);
            for (int z = 0; z < h; z++) f[z * w + x] = salida[z];
        }
        for (int z = 0; z < h; z++)
        {
            for (int x = 0; x < w; x++) linea[x] = f[z * w + x];
            Dt1D(linea, w, salida, v, zz);
            for (int x = 0; x < w; x++) f[z * w + x] = salida[x];
        }
        return f;
    }

    static void Dt1D(float[] f, int n, float[] d, int[] v, float[] zf)
    {
        // En double: con floats, INF + q^2 - (INF + p^2) pierde toda la precision.
        var z = new double[n + 1];
        int k = 0;
        v[0] = 0; z[0] = double.NegativeInfinity; z[1] = double.PositiveInfinity;
        for (int q = 1; q < n; q++)
        {
            double s = Parabola(f, q, v[k]);
            while (s <= z[k]) { k--; s = Parabola(f, q, v[k]); }
            k++;
            v[k] = q; z[k] = s; z[k + 1] = double.PositiveInfinity;
        }
        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[k + 1] < q) k++;
            double dq = q - v[k];
            d[q] = (float)(dq * dq + f[v[k]]);
        }
    }

    static double Parabola(float[] f, int q, int p)
    {
        return (((double)f[q] + (double)q * q) - ((double)f[p] + (double)p * p)) / (2.0 * q - 2.0 * p);
    }

    // Desenfoque de caja separable, repetido: tres pasadas se acercan a una gaussiana.
    static float[] Desenfocar(float[] src, int w, int h, int radio, int pasadas)
    {
        var a = (float[])src.Clone();
        var b = new float[w * h];
        for (int p = 0; p < pasadas; p++)
        {
            for (int z = 0; z < h; z++)
            {
                float suma = 0; int cuenta = 0;
                for (int x = -radio; x <= radio; x++) { int xx = Mathf.Clamp(x, 0, w - 1); suma += a[z * w + xx]; cuenta++; }
                for (int x = 0; x < w; x++)
                {
                    b[z * w + x] = suma / cuenta;
                    int sale = Mathf.Clamp(x - radio, 0, w - 1), entra = Mathf.Clamp(x + radio + 1, 0, w - 1);
                    suma += a[z * w + entra] - a[z * w + sale];
                }
            }
            for (int x = 0; x < w; x++)
            {
                float suma = 0; int cuenta = 0;
                for (int z = -radio; z <= radio; z++) { int zc = Mathf.Clamp(z, 0, h - 1); suma += b[zc * w + x]; cuenta++; }
                for (int z = 0; z < h; z++)
                {
                    a[z * w + x] = suma / cuenta;
                    int sale = Mathf.Clamp(z - radio, 0, h - 1), entra = Mathf.Clamp(z + radio + 1, 0, h - 1);
                    suma += b[entra * w + x] - b[sale * w + x];
                }
            }
        }
        return a;
    }
}
