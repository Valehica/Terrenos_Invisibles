using UnityEditor;
using UnityEngine;

// Puebla el Terrain de la escena Sur (Puerto Eden) con los mismos detalles que
// Centro: cuatro pastos, cuatro flores, el arbusto y el helecho.
//
// En Centro se pintaron a mano. Aca va por reglas, porque el terreno de Sur se
// regenera desde datos (Tools/TerrenoSur) y lo pintado a mano se perderia:
// despues de regenerar, correr de nuevo este menu.
//
// Reglas: la cobertura sale del splatmap (musgo y matorral llevan vegetacion,
// roca y nieve la quitan), de la pendiente y de la altura sobre el mar, y se
// agrupa en manchas con ruido para que no quede una alfombra pareja.
public static class VegetacionSur
{
    const string DatosCentro = "Assets/New Terrain.asset";

    // Las celdas de detalle de Sur miden 1600/1024 = 1.56 m y las de Centro
    // ~0.87 m (1000x800 en 1024). Para la misma densidad por metro cuadrado,
    // cada celda de Sur necesita ~3.2 veces mas instancias.
    const float EscalaDensidad = 3.2f;

    // Tope por celda: el pasto de Centro llega a 5 por celda (~6.5 por m2).
    const int MaxPorCelda = 16;

    // Tinte oscuro y casi gris a proposito: el pase de estilo de Sur ya lleva la
    // vegetacion a matiz 135 y le multiplica la saturacion x1.8. Un tinte verde
    // (se probo 0.60/0.72/0.50) salia verde neon. Un gris claro tampoco sirve: un
    // multiplicador parejo no desatura. Lo que funciona es bajar el valor, que lee
    // como verde perenne de fiordo.
    static readonly Color PastoSano = new Color(0.50f, 0.52f, 0.45f, 1f);
    static readonly Color PastoSeco = new Color(0.62f, 0.58f, 0.46f, 1f);   // pasto quemado por el invierno
    static readonly Color FlorSana = new Color(0.86f, 0.86f, 0.76f, 1f);
    static readonly Color FlorSeca = new Color(0.86f, 0.80f, 0.64f, 1f);

    enum Tipo { Pasto, Flor, Arbusto, Helecho }

    struct Regla
    {
        public Tipo tipo;
        public float densidad;   // instancias por celda de Centro donde la cobertura es plena
        public float escalaMancha; // tamano de las manchas en metros
        public float corteMancha;  // 0 = en todos lados, 0.6 = solo en manchas
    }

    [MenuItem("Terrenos/Sur/Poblar vegetacion")]
    static void Poblar()
    {
        var terrain = Terrain.activeTerrain;
        var centro = AssetDatabase.LoadAssetAtPath<TerrainData>(DatosCentro);
        if (terrain == null || centro == null)
        {
            Debug.LogError("Hace falta la escena Sur abierta y " + DatosCentro + " en el proyecto.");
            return;
        }
        var td = terrain.terrainData;
        if (!AssetDatabase.GetAssetPath(td).Contains("Sur"))
        {
            Debug.LogError("El Terrain activo no es el de Sur: " + AssetDatabase.GetAssetPath(td));
            return;
        }

        Undo.RegisterCompleteObjectUndo(td, "Poblar vegetacion de Sur");

        // Prototipos copiados de Centro, en el mismo orden, con color de Sur.
        var protos = centro.detailPrototypes;
        var reglas = new Regla[protos.Length];
        for (int i = 0; i < protos.Length; i++)
        {
            var p = protos[i];
            string nombre = p.usePrototypeMesh ? (p.prototype ? p.prototype.name : "") : (p.prototypeTexture ? p.prototypeTexture.name : "");
            reglas[i] = ReglaPara(nombre, i);
            // Las mallas (helecho, arbusto) no reciben el healthyColor: su color es el del
            // material, que comparten con Centro. Sur usa una copia con tinte propio.
            if (p.usePrototypeMesh && p.prototype != null) p.prototype = VarianteSur(p.prototype);
            // Sin instancing el Terrain arma las mallas de detalle en la CPU cada vez
            // que cambia la vista: con ~150 mil helechos era lo más caro del frame.
            if (p.usePrototypeMesh) p.useInstancing = true;
            bool esFlor = reglas[i].tipo == Tipo.Flor;
            p.healthyColor = esFlor ? FlorSana : PastoSano;
            p.dryColor = esFlor ? FlorSeca : PastoSeco;
            protos[i] = p;
        }
        td.detailPrototypes = protos;
        td.RefreshPrototypes();   // sin esto el Terrain sigue dibujando las mallas viejas
        td.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
        td.wavingGrassAmount = centro.wavingGrassAmount;
        td.wavingGrassSpeed = centro.wavingGrassSpeed * 1.3f;       // Sur es mas ventoso
        td.wavingGrassStrength = centro.wavingGrassStrength * 1.2f;
        td.wavingGrassTint = new Color(0.80f, 0.80f, 0.77f, 1f);

        int dw = td.detailWidth, dh = td.detailHeight;
        int aw = td.alphamapWidth, ah = td.alphamapHeight;
        var alfa = td.GetAlphamaps(0, 0, aw, ah);
        int capas = alfa.GetLength(2);
        int iMusgo = Capa(td, "Musgo"), iMatorral = Capa(td, "Matorral"), iRoca = Capa(td, "Roca"),
            iRibera = Capa(td, "Ribera"), iNieve = Capa(td, "Nieve"), iLecho = Capa(td, "Lecho");

        float baseY = terrain.transform.position.y;
        var capasDetalle = new int[protos.Length][,];
        for (int i = 0; i < protos.Length; i++) capasDetalle[i] = new int[dh, dw];
        long[] totales = new long[protos.Length];

        // Semilla fija: correr el menu dos veces da el mismo resultado.
        var azar = new System.Random(4919);
        float tamCelda = td.size.x / dw;

        for (int z = 0; z < dh; z++)
        {
            for (int x = 0; x < dw; x++)
            {
                float nx = (x + 0.5f) / dw, nz = (z + 0.5f) / dh;
                float alturaMar = td.GetInterpolatedHeight(nx, nz) + baseY;   // el mar esta en y = 0
                if (alturaMar < 0.8f) continue;                               // resaca y playa
                float pendiente = td.GetSteepness(nx, nz);
                if (pendiente > 42f) continue;

                int ax = Mathf.Min(aw - 1, (int)(nx * aw)), az = Mathf.Min(ah - 1, (int)(nz * ah));
                float musgo = Peso(alfa, az, ax, iMusgo), matorral = Peso(alfa, az, ax, iMatorral);
                float roca = Peso(alfa, az, ax, iRoca), ribera = Peso(alfa, az, ax, iRibera);
                float nieve = Peso(alfa, az, ax, iNieve), lecho = Peso(alfa, az, ax, iLecho);

                // Cuanto admite el suelo, antes de mirar que planta es.
                // La roca resta poco: en Puerto Eden la vegetacion lo cubre casi todo, y la
                // capa de roca del splatmap sale de la pendiente, no de roca desnuda real.
                float suelo = Mathf.Clamp01(1f - roca * 0.8f - nieve * 1.8f - lecho * 2f);
                // Pendiente: plena hasta 25 grados, nada a 42.
                float pend = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(25f, 42f, pendiente));
                // Cerca del agua el viento y la sal dejan menos.
                float costa = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f, 4f, alturaMar));
                // Arriba de 55 m el pasto ralea (altura de Sur llega a ~75 m).
                float altura = 1f - 0.6f * Mathf.InverseLerp(40f, 75f, alturaMar);
                float base01 = suelo * pend * costa * altura;
                if (base01 <= 0.01f) continue;

                float wx = x * tamCelda, wz = z * tamCelda;

                for (int i = 0; i < protos.Length; i++)
                {
                    var r = reglas[i];
                    float habitat;
                    switch (r.tipo)
                    {
                        case Tipo.Pasto:   habitat = musgo * 1.0f + matorral * 0.55f + ribera * 0.5f; break;
                        case Tipo.Flor:    habitat = musgo * 0.8f + ribera * 0.6f + matorral * 0.2f; break;
                        case Tipo.Arbusto: habitat = matorral * 1.0f + musgo * 0.15f; break;
                        default:           habitat = matorral * 0.9f + musgo * 0.6f; break;   // helecho
                    }
                    // El habitat se mide sobre la parte vegetal del suelo: la roca ya resto en
                    // 'suelo', y si se la contara aca tambien penalizaria dos veces.
                    habitat /= Mathf.Max(musgo + matorral + ribera, 0.25f);
                    // El helecho y el arbusto prefieren la ladera abrigada, no el plano.
                    if (r.tipo == Tipo.Helecho || r.tipo == Tipo.Arbusto)
                        habitat *= Mathf.Lerp(0.6f, 1.15f, Mathf.InverseLerp(4f, 20f, pendiente));

                    float mancha = Mathf.PerlinNoise(wx / r.escalaMancha + i * 31.7f, wz / r.escalaMancha + i * 17.3f) * 0.7f
                                 + Mathf.PerlinNoise(wx / (r.escalaMancha * 0.33f) + i * 7.1f, wz / (r.escalaMancha * 0.33f) - i * 5.9f) * 0.3f;
                    mancha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(r.corteMancha, 1f, mancha));

                    float esperado = r.densidad * EscalaDensidad * base01 * Mathf.Clamp01(habitat) * mancha;
                    if (esperado <= 0f) continue;
                    // Parte entera fija mas una tirada por la fraccion: sin esto las
                    // densidades bajas (arbusto, flores) redondearian a cero siempre.
                    int n = (int)esperado;
                    if (azar.NextDouble() < esperado - n) n++;
                    n = Mathf.Min(n, MaxPorCelda);
                    if (n > 0) { capasDetalle[i][z, x] = n; totales[i] += n; }
                }
            }
        }

        for (int i = 0; i < protos.Length; i++) td.SetDetailLayer(0, 0, i, capasDetalle[i]);
        EditorUtility.SetDirty(td);
        AssetDatabase.SaveAssets();

        var sb = new System.Text.StringBuilder("Vegetacion de Sur poblada:\n");
        for (int i = 0; i < protos.Length; i++)
        {
            string nombre = protos[i].usePrototypeMesh ? protos[i].prototype.name : protos[i].prototypeTexture.name;
            sb.AppendLine($"  {i} {nombre} ({reglas[i].tipo}): {totales[i]} instancias");
        }
        Debug.Log(sb.ToString());
    }

    // Densidad por tipo, en instancias por celda de Centro. Los numeros salen del
    // promedio de Centro donde hay cobertura (pasto principal ~2.6 por celda). Las
    // flores van a la mitad: en Sur es invierno.
    static Regla ReglaPara(string nombre, int indice)
    {
        string n = nombre.ToLowerInvariant();
        if (n.Contains("arbusto") || n.Contains("bush"))
            return new Regla { tipo = Tipo.Arbusto, densidad = 0.10f, escalaMancha = 30f, corteMancha = 0.45f };
        if (n.Contains("helecho") || n.Contains("fern"))
            return new Regla { tipo = Tipo.Helecho, densidad = 0.45f, escalaMancha = 22f, corteMancha = 0.35f };
        if (n.Contains("flower"))
            return new Regla { tipo = Tipo.Flor, densidad = 0.28f, escalaMancha = 14f, corteMancha = 0.55f };
        switch (n)
        {
            case "det_grass_3": return new Regla { tipo = Tipo.Pasto, densidad = 2.6f, escalaMancha = 40f, corteMancha = 0.10f };
            case "det_grass_2": return new Regla { tipo = Tipo.Pasto, densidad = 1.0f, escalaMancha = 28f, corteMancha = 0.30f };
            case "det_grass_1": return new Regla { tipo = Tipo.Pasto, densidad = 0.9f, escalaMancha = 18f, corteMancha = 0.45f };
            default:            return new Regla { tipo = Tipo.Pasto, densidad = 0.6f, escalaMancha = 16f, corteMancha = 0.50f };
        }
    }

    // Tinte de las mallas de Sur: el mismo criterio que PastoSano (ver arriba), algo
    // mas oscuro porque el material Lit no pasa por el sombreado de pasto.
    static readonly Color TinteMallaSur = new Color(0.40f, 0.42f, 0.36f, 1f);

    // Copia prefab y material con sufijo _Sur (una sola vez) y devuelve la copia.
    static GameObject VarianteSur(GameObject original)
    {
        string ruta = AssetDatabase.GetAssetPath(original);
        if (original.name.EndsWith("_Sur")) return original;
        string rutaSur = ruta.Replace(".prefab", "_Sur.prefab");
        var sur = AssetDatabase.LoadAssetAtPath<GameObject>(rutaSur);
        if (sur == null)
        {
            AssetDatabase.CopyAsset(ruta, rutaSur);
            sur = AssetDatabase.LoadAssetAtPath<GameObject>(rutaSur);
        }

        var contenido = PrefabUtility.LoadPrefabContents(rutaSur);
        foreach (var mr in contenido.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = mr.sharedMaterials;
            for (int m = 0; m < mats.Length; m++)
            {
                if (mats[m] == null) continue;
                string rutaMat = AssetDatabase.GetAssetPath(mats[m]);
                if (!mats[m].name.EndsWith("_Sur"))
                {
                    string rutaMatSur = rutaMat.Replace(".mat", "_Sur.mat");
                    if (AssetDatabase.LoadAssetAtPath<Material>(rutaMatSur) == null)
                        AssetDatabase.CopyAsset(rutaMat, rutaMatSur);
                    mats[m] = AssetDatabase.LoadAssetAtPath<Material>(rutaMatSur);
                }
                if (mats[m].HasProperty("_BaseColor")) mats[m].SetColor("_BaseColor", TinteMallaSur);
                mats[m].enableInstancing = true; // lo exige el instancing de detalles del Terrain
                EditorUtility.SetDirty(mats[m]);
            }
            mr.sharedMaterials = mats;
        }
        PrefabUtility.SaveAsPrefabAsset(contenido, rutaSur);
        PrefabUtility.UnloadPrefabContents(contenido);
        return AssetDatabase.LoadAssetAtPath<GameObject>(rutaSur);
    }

    static int Capa(TerrainData td, string contiene)
    {
        var capas = td.terrainLayers;
        for (int i = 0; i < capas.Length; i++)
            if (capas[i] != null && capas[i].name.Contains(contiene)) return i;
        return -1;
    }

    static float Peso(float[,,] alfa, int z, int x, int capa) => capa < 0 ? 0f : alfa[z, x, capa];
}
