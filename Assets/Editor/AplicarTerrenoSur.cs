using System.IO;
using UnityEditor;
using UnityEngine;

// Aplica en Unity lo que generan los scripts de Tools/TerrenoSur:
//   heights.bin -> alturas de TerrainData_Sur (y su tamano y resolucion)
//   splat.bin   -> alphamaps
//   fondo.bin   -> malla de MontanasLejanas (FondoSur.asset)
// y actualiza lo que depende del tamano del terreno:
//   TerrainHeight_Sur.asset (el _ShoreHeightMap del agua) y el origen/tamano en
//   el material del mar, y la malla del mar (OceanGrid_Sur.asset).
//
// Orden completo: gen_landcover.py, gen_terrain.py, gen_fondo.py, este menu,
// Terrenos > Agua > Bakear distancia a la costa, Terrenos > Sur > Poblar vegetacion.
public static class AplicarTerrenoSur
{
    const string Carpeta = "Tools/TerrenoSur";
    const float Base = -90f, Rango = 420f;   // igual que BASE_Y / RANGE_Y de gen_terrain.py
    const string RutaHeightAgua = "Assets/Textures/TerrainHeight_Sur.asset";
    const string RutaFondo = "Assets/Meshes/FondoSur.asset";
    const string RutaMar = "Assets/Meshes/OceanGrid_Sur.asset";

    // Mitad del lado del Terrain. Tiene que coincidir con HALF de gen_common.py.
    const float Mitad = 1600f;

    // Malla del mar: 5000 m de lado (mas que el Terrain, el resto lo pinta el
    // fondo), con vertices densos en el centro y ralos hacia afuera, igual que la
    // OceanGrid de Centro: x = A*u + B*u^3. A = 600 da 2.5 m en el centro y ~19 m a
    // 1600 m, lo mismo que la malla vieja tenia en su borde.
    const float MarMitad = 2500f, MarA = 600f;
    const int MarPasos = 480;

    [MenuItem("Terrenos/Sur/Aplicar terreno generado")]
    static void Aplicar()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null || !AssetDatabase.GetAssetPath(terrain.terrainData).Contains("Sur"))
        {
            Debug.LogError("Abrir la escena Sur antes de aplicar el terreno.");
            return;
        }
        var td = terrain.terrainData;
        Undo.RegisterCompleteObjectUndo(new Object[] { td, terrain.transform }, "Aplicar terreno de Sur");

        // ---- alturas ----
        var crudo = LeerFloats(Carpeta + "/heights.bin");
        int res = Mathf.RoundToInt(Mathf.Sqrt(crudo.Length));
        if (res * res != crudo.Length) { Debug.LogError("heights.bin no es cuadrado"); return; }
        var h = new float[res, res];
        for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) h[z, x] = crudo[z * res + x];

        td.heightmapResolution = res;
        td.size = new Vector3(2 * Mitad, Rango, 2 * Mitad);   // despues de la resolucion: cambiarla resetea el tamano
        td.SetHeights(0, 0, h);
        terrain.transform.position = new Vector3(-Mitad, Base, -Mitad);

        // ---- splat ----
        var s = LeerFloats(Carpeta + "/splat.bin");
        int capas = td.terrainLayers.Length;
        int ares = Mathf.RoundToInt(Mathf.Sqrt(s.Length / (float)capas));
        td.alphamapResolution = ares;
        var a = new float[ares, ares, capas];
        int k = 0;
        for (int z = 0; z < ares; z++) for (int x = 0; x < ares; x++) for (int l = 0; l < capas; l++) a[z, x, l] = s[k++];
        td.SetAlphamaps(0, 0, a);

        // Detalles a la misma densidad por metro que antes (1.56 m por celda).
        td.SetDetailResolution(ares, 32);
        EditorUtility.SetDirty(td);

        ActualizarAgua(terrain);
        ConstruirFondo();
        ConstruirMar();
        AssetDatabase.SaveAssets();
        Debug.Log($"Terreno de Sur aplicado: {2 * Mitad} m, heightmap {res}, splat {ares}. " +
                  "Falta: Bakear distancia a la costa y Poblar vegetacion.");
    }

    static void ActualizarAgua(Terrain terrain)
    {
        var td = terrain.terrainData;
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(RutaHeightAgua);
        int n = td.alphamapResolution;   // un texel por celda de splat: 1.56 m
        if (tex.width != n) tex.Reinitialize(n, n, TextureFormat.RFloat, false);
        var px = new Color[n * n];
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
                px[z * n + x] = new Color(td.GetInterpolatedHeight((x + 0.5f) / n, (z + 0.5f) / n) + terrain.transform.position.y, 0, 0, 1);
        tex.SetPixels(px);
        tex.Apply(false, false);
        EditorUtility.SetDirty(tex);

        var sea = GameObject.Find("Sea");
        if (sea == null) return;
        var mat = sea.GetComponent<MeshRenderer>().sharedMaterial;
        Undo.RecordObject(mat, "Agua de Sur");
        mat.SetVector("_ShoreMapOrigin", new Vector4(-Mitad, -Mitad, 0, 0));
        mat.SetVector("_ShoreMapSize", new Vector4(2 * Mitad, 2 * Mitad, 0, 0));
        EditorUtility.SetDirty(mat);
    }

    static void ConstruirFondo()
    {
        string ruta = Carpeta + "/fondo.bin";
        if (!File.Exists(ruta)) { Debug.LogWarning("No hay fondo.bin: correr gen_fondo.py"); return; }
        int nr, na; float[] wx, wz, hy;
        using (var br = new BinaryReader(File.OpenRead(ruta)))
        {
            nr = br.ReadInt32(); na = br.ReadInt32();
            wx = Leer(br, nr * na); wz = Leer(br, nr * na); hy = Leer(br, nr * na);
        }
        var v = new Vector3[nr * na];
        for (int i = 0; i < v.Length; i++) v[i] = new Vector3(wx[i], hy[i], wz[i]);
        var t = new int[(nr - 1) * na * 6];
        int k = 0;
        for (int r = 0; r < nr - 1; r++)
            for (int j = 0; j < na; j++)
            {
                int j1 = (j + 1) % na;
                int a = r * na + j, b = r * na + j1, c = (r + 1) * na + j, d = (r + 1) * na + j1;
                t[k++] = a; t[k++] = b; t[k++] = c;
                t[k++] = b; t[k++] = d; t[k++] = c;
            }
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(RutaFondo);
        mesh.Clear();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = v;
        mesh.triangles = t;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
    }

    static void ConstruirMar()
    {
        int n = MarPasos + 1;
        float B = MarMitad - MarA;
        var v = new Vector3[n * n];
        var nor = new Vector3[n * n];
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float ux = -1f + 2f * x / MarPasos, uz = -1f + 2f * z / MarPasos;
                v[z * n + x] = new Vector3(MarA * ux + B * ux * ux * ux, 0, MarA * uz + B * uz * uz * uz);
                nor[z * n + x] = Vector3.up;
            }
        var t = new int[MarPasos * MarPasos * 6];
        int k = 0;
        for (int z = 0; z < MarPasos; z++)
            for (int x = 0; x < MarPasos; x++)
            {
                int i = z * n + x;
                // mismo orden que OceanGrid: 0, n, 1 / 1, n, n+1
                t[k++] = i; t[k++] = i + n; t[k++] = i + 1;
                t[k++] = i + 1; t[k++] = i + n; t[k++] = i + n + 1;
            }
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(RutaMar);
        if (mesh == null) { mesh = new Mesh { name = "OceanGrid_Sur" }; AssetDatabase.CreateAsset(mesh, RutaMar); }
        mesh.Clear();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = v;
        mesh.normals = nor;
        mesh.triangles = t;
        // Las olas suben y bajan varios metros: el bounds plano haria que se recorte.
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(2 * MarMitad + 50, 20, 2 * MarMitad + 50));
        EditorUtility.SetDirty(mesh);

        var sea = GameObject.Find("Sea");
        if (sea != null)
        {
            var mf = sea.GetComponent<MeshFilter>();
            Undo.RecordObject(mf, "Mar de Sur");
            mf.sharedMesh = mesh;
            EditorUtility.SetDirty(mf);
        }
    }

    static float[] LeerFloats(string ruta)
    {
        var bytes = File.ReadAllBytes(ruta);
        var f = new float[bytes.Length / 4];
        System.Buffer.BlockCopy(bytes, 0, f, 0, f.Length * 4);
        return f;
    }

    static float[] Leer(BinaryReader br, int n)
    {
        var f = new float[n];
        for (int i = 0; i < n; i++) f[i] = br.ReadSingle();
        return f;
    }
}
