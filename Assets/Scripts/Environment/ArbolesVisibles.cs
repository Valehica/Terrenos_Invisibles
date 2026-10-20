using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Dibuja los arboles del Terrain solo cuando se ven, en lotes con GPU instancing.
//
// El Terrain dibuja cada arbol con LODGroup por separado (Sur: 13k arboles = 22k draw calls) y los LOD de los
// prefabs se apagan recien a varios km. Este componente, en Play:
//   1. Le quita el dibujo de arboles al Terrain bajando treeLODBiasMultiplier (no toca el TerrainData, asi
//      que los colliders de los arboles y el escaneo siguen igual, y en el Editor se pueden seguir pintando).
//   2. Reparte los arboles en una grilla de celdas y cada frame descarta las celdas fuera de la camara,
//      mas lejos de distanciaMaxima o tapadas por un cerro (rayos contra el relieve del Terrain).
//   3. Hasta distanciaDetalle dibuja el arbol de verdad; mas lejos, su version de dos planos (prefab con el
//      mismo nombre + "_Lejos" entre los prototipos del Terrain). Todo lo visible de un mismo tipo sale en
//      una sola llamada a Graphics.RenderMeshInstanced.
// Los materiales necesitan "Enable GPU Instancing" (los de coigue/cipres ya lo tienen).
// Prefabs con varios LOD y sin "_Lejos" (los arbustos de Centro): cada uno se dibuja con el LOD que elegiria su
// LODGroup a esa distancia (no el LOD0 hasta distanciaDetalle: con 10k arbustos eso era peor que no tener este
// componente), y se dejan de dibujar donde los cortaria (por eso no llegan hasta distanciaMaxima).
// Si se repintan arboles, no hay que hacer nada: la grilla se arma al entrar en Play.
[RequireComponent(typeof(Terrain))]
[DisallowMultipleComponent]
public class ArbolesVisibles : MonoBehaviour
{
    [Tooltip("Hasta esta distancia (m) se dibuja el arbol de verdad; mas lejos, su version de dos planos (_Lejos).")]
    public float distanciaDetalle = 120f;
    [Tooltip("Mas alla de esta distancia (m) no se dibuja ningun arbol.")]
    public float distanciaMaxima = 2500f;
    [Tooltip("Hasta esta distancia (m) los arboles de verdad hacen sombra. Mas alla de la distancia de sombras de URP no sirve.")]
    public float distanciaSombras = 50f;
    [Tooltip("No dibujar los arboles que quedan detras de un cerro.")]
    public bool ocultarTrasCerros = true;
    [Tooltip("Celdas a las que se les rehace la prueba de cerros en cada frame (las demas usan la ultima).")]
    public int pruebasPorFrame = 120;
    [Tooltip("Lado de cada celda de la grilla, en metros. Se usa al entrar en Play.")]
    public float tamanoCelda = 64f;
    [Tooltip("Multiplica el lodBias para elegir el LOD y el corte de los prefabs con varios LOD (arbustos). " +
             "Menos de 1 = pasan antes a un LOD mas simple y se apagan mas cerca.")]
    [Range(0.1f, 1f)] public float sesgoLod = 1f;

    [Header("Estado (solo lectura)")]
    [SerializeField] int arbolesDeVerdad;
    [SerializeField] int arbolesLejanos;
    [SerializeField] int celdasVisibles;
    [SerializeField] int celdasTapadas;
    [SerializeField] int llamadasDeDibujo;

    const int MaxPorLlamada = 1023;
    const float AlturaOcultar = 0.0001f;

    // Un submesh de un renderer del prefab, con todas las matrices visibles de este frame
    class Lote
    {
        public int pieza;
        public Mesh mesh;
        public int submesh;
        public Material material;
        public ShadowCastingMode sombra;
        public Matrix4x4[] matrices = new Matrix4x4[64];
        public int cuenta;
        public Bounds limites;

        public void Agregar(Matrix4x4 m)
        {
            if (cuenta == matrices.Length) System.Array.Resize(ref matrices, cuenta * 2);
            matrices[cuenta++] = m;
        }

        public void SumarLimites(Bounds b)
        {
            if (cuenta == 0) limites = b; else limites.Encapsulate(b);
        }
    }

    class Pieza
    {
        public Mesh mesh;
        public Material[] materiales;
        public ShadowCastingMode sombra;
        public Matrix4x4 local;   // del renderer respecto de la raiz del prefab
    }

    // Un prototipo del Terrain
    class Tipo
    {
        public string nombre;
        public Pieza[] piezas;
        public Lote[] lotesConSombra;   // por pieza+submesh: los de verdad cerca
        public Lote[] lotesSinSombra;   // por pieza+submesh: los de verdad sin sombra (o los _Lejos)
        public int lejos = -1;          // Tipo que lo reemplaza a distancia
        public bool esLejano;           // es un _Lejos: siempre se dibuja como lejano
        public Bounds limitesRaiz;      // de todas las piezas, en el espacio de la raiz
        public float corte;             // alto relativo en pantalla del ultimo LOD (0 = sin corte propio)
        public float tamano;            // LODGroup.size
        public float escalaMax = 1f;    // la mayor escala de sus arboles en el Terrain
        public float corte2;            // distancia de corte al cuadrado, de este frame
        public int[] niveles;           // varios LOD sin _Lejos: el Tipo de cada LOD (niveles[0] = este)
        public float[] alturas;         // alto relativo en pantalla donde termina cada LOD
        public float[] niveles2;        // distancia al cuadrado donde termina cada LOD, de este frame
    }

    // Los arboles de un Tipo dentro de una celda
    class Grupo
    {
        public int tipo;
        public Vector3[] posiciones;
        public Matrix4x4[] raices;
    }

    class Celda
    {
        public Bounds limites;
        public List<Grupo> grupos = new List<Grupo>();
        public bool tapada;
    }

    Terrain terreno;
    Tipo[] tipos;
    Celda[] celdas;
    float multiplicadorOriginal, distanciaOriginal;
    readonly Plane[] planos = new Plane[6];
    readonly List<Lote> lotesUsados = new List<Lote>();

    // relieve para la prueba de cerros: minimo de cada bloque del heightmap, en metros del mundo
    float[] relieve;
    int relieveRes;
    float relievePaso;
    Vector3 origen;
    int siguientePrueba;
    Vector3 ultimoOjo;
    bool primeraPrueba = true;

    void OnEnable()
    {
        terreno = GetComponent<Terrain>();
        if (terreno.terrainData == null) { enabled = false; return; }

        ArmarTipos();
        ArmarCeldas();
        ArmarRelieve();

        multiplicadorOriginal = terreno.treeLODBiasMultiplier;
        distanciaOriginal = terreno.treeDistance;
        terreno.treeLODBiasMultiplier = AlturaOcultar;
        terreno.treeDistance = 0f;
        primeraPrueba = true;
    }

    void OnDisable()
    {
        if (terreno == null) return;
        terreno.treeLODBiasMultiplier = multiplicadorOriginal;
        terreno.treeDistance = distanciaOriginal;
    }

    void ArmarTipos()
    {
        TreePrototype[] protos = terreno.terrainData.treePrototypes;
        var lista = new List<Tipo>();
        for (int i = 0; i < protos.Length; i++)
            lista.Add(ArmarTipo(protos[i].prefab, null));

        for (int i = 0; i < protos.Length; i++)
        {
            if (lista[i] == null) continue;
            lista[i].esLejano = lista[i].nombre.EndsWith("_Lejos");
            for (int j = 0; j < protos.Length; j++)
                if (lista[j] != null && lista[j].nombre == lista[i].nombre + "_Lejos") lista[i].lejos = j;

            // Varios LOD y sin _Lejos: un Tipo por LOD, elegido por distancia como lo haria el LODGroup, que
            // tambien pone el corte. El ultimo LOD hace de version lejana (para los limites de las celdas).
            var lodGroup = protos[i].prefab.GetComponent<LODGroup>();
            if (lodGroup == null || lodGroup.lodCount < 2) continue;
            LOD[] lods = lodGroup.GetLODs();
            lista[i].corte = lods[lods.Length - 1].screenRelativeTransitionHeight;
            lista[i].tamano = lodGroup.size;
            if (lista[i].lejos >= 0 || lista[i].esLejano) continue;
            var niveles = new int[lods.Length];
            var alturas = new float[lods.Length];
            niveles[0] = i;
            for (int k = 0; k < lods.Length; k++)
            {
                alturas[k] = lods[k].screenRelativeTransitionHeight;
                if (k == 0) continue;
                var nivel = ArmarTipo(protos[i].prefab, lods[k].renderers);
                nivel.nombre += $" (LOD{k})";
                niveles[k] = lista.Count;
                lista.Add(nivel);
            }
            lista[i].niveles = niveles;
            lista[i].alturas = alturas;
            lista[i].niveles2 = new float[lods.Length];
            lista[i].lejos = niveles[lods.Length - 1];
        }
        tipos = lista.ToArray();
    }

    Tipo ArmarTipo(GameObject prefab, Renderer[] renderers)
    {
        if (prefab == null) return null;
        Transform raiz = prefab.transform;

        var lodGroup = prefab.GetComponent<LODGroup>();
        if (renderers == null)
            renderers = lodGroup != null && lodGroup.lodCount > 0
                ? lodGroup.GetLODs()[0].renderers
                : prefab.GetComponentsInChildren<MeshRenderer>(true);

        var piezas = new List<Pieza>();
        var conSombra = new List<Lote>();
        var sinSombra = new List<Lote>();
        var limites = new Bounds();
        bool hayLimites = false;

        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            var filtro = r.GetComponent<MeshFilter>();
            if (filtro == null || filtro.sharedMesh == null) continue;
            var pieza = new Pieza
            {
                mesh = filtro.sharedMesh,
                materiales = r.sharedMaterials,
                sombra = r.shadowCastingMode,
                local = raiz.worldToLocalMatrix * r.transform.localToWorldMatrix,
            };
            piezas.Add(pieza);

            Bounds b = Transformar(pieza.local, pieza.mesh.bounds);
            if (hayLimites) limites.Encapsulate(b); else { limites = b; hayLimites = true; }

            int submeshes = Mathf.Min(pieza.mesh.subMeshCount, pieza.materiales.Length);
            for (int s = 0; s < submeshes; s++)
            {
                Material mat = pieza.materiales[s];
                if (mat == null) continue;
                if (!mat.enableInstancing)
                {
                    Debug.LogWarning($"ArbolesVisibles: el material {mat.name} de {prefab.name} no tiene GPU instancing; esa parte no se dibuja.", mat);
                    continue;
                }
                int indice = piezas.Count - 1;
                conSombra.Add(new Lote { pieza = indice, mesh = pieza.mesh, submesh = s, material = mat, sombra = pieza.sombra });
                sinSombra.Add(new Lote { pieza = indice, mesh = pieza.mesh, submesh = s, material = mat, sombra = ShadowCastingMode.Off });
            }
        }

        return new Tipo
        {
            nombre = prefab.name,
            piezas = piezas.ToArray(),
            lotesConSombra = conSombra.ToArray(),
            lotesSinSombra = sinSombra.ToArray(),
            limitesRaiz = limites,
        };
    }

    void ArmarCeldas()
    {
        TerrainData datos = terreno.terrainData;
        Vector3 tamano = datos.size;
        origen = terreno.transform.position;
        int nx = Mathf.Max(1, Mathf.CeilToInt(tamano.x / tamanoCelda));
        int nz = Mathf.Max(1, Mathf.CeilToInt(tamano.z / tamanoCelda));

        var porCelda = new Dictionary<int, Dictionary<int, (List<Vector3> pos, List<Matrix4x4> raices)>>();
        var limites = new Dictionary<int, Bounds>();

        foreach (TreeInstance arbol in datos.treeInstances)
        {
            Tipo tipo = arbol.prototypeIndex < tipos.Length ? tipos[arbol.prototypeIndex] : null;
            if (tipo == null || tipo.piezas.Length == 0) continue;
            tipo.escalaMax = Mathf.Max(tipo.escalaMax, arbol.widthScale, arbol.heightScale);

            Vector3 pos = Vector3.Scale(arbol.position, tamano) + origen;
            var rot = Quaternion.AngleAxis(arbol.rotation * Mathf.Rad2Deg, Vector3.up);
            var escala = new Vector3(arbol.widthScale, arbol.heightScale, arbol.widthScale);
            Matrix4x4 raiz = Matrix4x4.TRS(pos, rot, escala);

            int cx = Mathf.Clamp((int)((pos.x - origen.x) / tamanoCelda), 0, nx - 1);
            int cz = Mathf.Clamp((int)((pos.z - origen.z) / tamanoCelda), 0, nz - 1);
            int clave = cz * nx + cx;

            if (!porCelda.TryGetValue(clave, out var grupos)) porCelda[clave] = grupos = new Dictionary<int, (List<Vector3>, List<Matrix4x4>)>();
            if (!grupos.TryGetValue(arbol.prototypeIndex, out var g)) grupos[arbol.prototypeIndex] = g = (new List<Vector3>(), new List<Matrix4x4>());
            g.pos.Add(pos);
            g.raices.Add(raiz);

            // los limites cubren la version de verdad y la lejana
            Bounds b = Transformar(raiz, tipo.limitesRaiz);
            if (tipo.lejos >= 0) b.Encapsulate(Transformar(raiz, tipos[tipo.lejos].limitesRaiz));
            if (limites.TryGetValue(clave, out Bounds previo)) { previo.Encapsulate(b); limites[clave] = previo; }
            else limites[clave] = b;
        }

        var lista = new List<Celda>();
        foreach (var par in porCelda)
        {
            var celda = new Celda { limites = limites[par.Key] };
            foreach (var g in par.Value)
                celda.grupos.Add(new Grupo { tipo = g.Key, posiciones = g.Value.pos.ToArray(), raices = g.Value.raices.ToArray() });
            lista.Add(celda);
        }
        celdas = lista.ToArray();
    }

    void ArmarRelieve()
    {
        TerrainData datos = terreno.terrainData;
        int res = datos.heightmapResolution;
        float[,] alturas = datos.GetHeights(0, 0, res, res);
        const int bloque = 8;
        relieveRes = Mathf.Max(1, (res - 1) / bloque);
        relievePaso = datos.size.x / relieveRes;
        relieve = new float[relieveRes * relieveRes];
        for (int z = 0; z < relieveRes; z++)
            for (int x = 0; x < relieveRes; x++)
            {
                // el minimo del bloque: un cerro nunca tapa mas de lo que tapa de verdad
                float min = float.MaxValue;
                for (int bz = 0; bz <= bloque; bz++)
                    for (int bx = 0; bx <= bloque; bx++)
                        min = Mathf.Min(min, alturas[Mathf.Min(z * bloque + bz, res - 1), Mathf.Min(x * bloque + bx, res - 1)]);
                relieve[z * relieveRes + x] = min * datos.size.y + origen.y;
            }
    }

    void Update()
    {
        Camera cam = Camera.main;
        if (cam == null || celdas == null) return;

        Vector3 ojo = cam.transform.position;
        GeometryUtility.CalculateFrustumPlanes(cam, planos);
        ProbarCerros(ojo);

        foreach (Lote l in lotesUsados) l.cuenta = 0;
        lotesUsados.Clear();
        arbolesDeVerdad = arbolesLejanos = celdasVisibles = celdasTapadas = 0;

        float detalle2 = distanciaDetalle * distanciaDetalle;
        float sombra2 = distanciaSombras * distanciaSombras;
        float maxima2 = distanciaMaxima * distanciaMaxima;

        // Donde el LODGroup cortaria a cada tipo: alto en pantalla = tamano / (2 d tan(fov/2)), por el lodBias.
        float tanMedio = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float bias = QualitySettings.lodBias * sesgoLod;
        foreach (Tipo t in tipos)
        {
            if (t == null || t.corte <= 0f) continue;
            float d = t.tamano * t.escalaMax * bias / (2f * tanMedio * t.corte);
            t.corte2 = d * d;
            if (t.niveles == null) continue;
            for (int k = 0; k < t.alturas.Length; k++)
            {
                float dk = t.tamano * t.escalaMax * bias / (2f * tanMedio * t.alturas[k]);
                t.niveles2[k] = dk * dk;
            }
        }

        foreach (Celda celda in celdas)
        {
            float dMin2 = celda.limites.SqrDistance(ojo);
            if (dMin2 > maxima2) continue;
            if (!GeometryUtility.TestPlanesAABB(planos, celda.limites)) continue;
            if (ocultarTrasCerros && celda.tapada) { celdasTapadas++; continue; }
            celdasVisibles++;

            float dMax = Vector3.Distance(ojo, celda.limites.center) + celda.limites.extents.magnitude;
            bool todaLejos = dMin2 > detalle2;
            bool todaCerca = dMax * dMax < detalle2;

            foreach (Grupo g in celda.grupos)
            {
                Tipo tipo = tipos[g.tipo];
                if (tipo.corte2 > 0f && dMin2 > tipo.corte2) continue;
                if (tipo.niveles != null)
                {
                    AgregarPorLod(tipo, g, celda.limites, ojo, sombra2);
                    continue;
                }
                if (tipo.esLejano || (todaLejos && tipo.lejos >= 0))
                {
                    Tipo lejano = tipo.esLejano ? tipo : tipos[tipo.lejos];
                    AgregarTodas(lejano.lotesSinSombra, lejano.piezas, g.raices, celda.limites);
                    arbolesLejanos += g.raices.Length;
                    continue;
                }
                if (todaLejos && tipo.lejos < 0)
                {
                    // sin version lejana: el de verdad, sin sombra
                    AgregarTodas(tipo.lotesSinSombra, tipo.piezas, g.raices, celda.limites);
                    arbolesDeVerdad += g.raices.Length;
                    continue;
                }

                for (int i = 0; i < g.raices.Length; i++)
                {
                    float d2 = (g.posiciones[i] - ojo).sqrMagnitude;
                    if (tipo.corte2 > 0f && d2 > tipo.corte2) continue;
                    if (todaCerca || d2 < detalle2 || tipo.lejos < 0)
                    {
                        Agregar(d2 < sombra2 ? tipo.lotesConSombra : tipo.lotesSinSombra, tipo.piezas, g.raices[i], celda.limites);
                        arbolesDeVerdad++;
                    }
                    else
                    {
                        Tipo lejano = tipos[tipo.lejos];
                        Agregar(lejano.lotesSinSombra, lejano.piezas, g.raices[i], celda.limites);
                        arbolesLejanos++;
                    }
                }
            }
        }

        Dibujar();
    }

    // Cada arbol con el LOD que le toca por distancia (el ultimo de niveles2 es el corte).
    void AgregarPorLod(Tipo tipo, Grupo g, Bounds limites, Vector3 ojo, float sombra2)
    {
        int ultimo = tipo.niveles.Length - 1;
        for (int i = 0; i < g.raices.Length; i++)
        {
            float d2 = (g.posiciones[i] - ojo).sqrMagnitude;
            if (d2 > tipo.niveles2[ultimo]) continue;
            int k = 0;
            while (k < ultimo && d2 > tipo.niveles2[k]) k++;
            Tipo nivel = tipos[tipo.niveles[k]];
            Agregar(d2 < sombra2 ? nivel.lotesConSombra : nivel.lotesSinSombra, nivel.piezas, g.raices[i], limites);
            if (k == 0) arbolesDeVerdad++; else arbolesLejanos++;
        }
    }

    void Agregar(Lote[] lotes, Pieza[] piezas, Matrix4x4 raiz, Bounds limites)
    {
        foreach (Lote l in lotes)
        {
            Usar(l, limites);
            l.Agregar(raiz * piezas[l.pieza].local);
        }
    }

    void AgregarTodas(Lote[] lotes, Pieza[] piezas, Matrix4x4[] raices, Bounds limites)
    {
        foreach (Lote l in lotes)
        {
            Usar(l, limites);
            Matrix4x4 local = piezas[l.pieza].local;
            foreach (Matrix4x4 raiz in raices) l.Agregar(raiz * local);
        }
    }

    void Usar(Lote l, Bounds limites)
    {
        if (l.cuenta == 0) lotesUsados.Add(l);
        l.SumarLimites(limites);
    }

    void Dibujar()
    {
        llamadasDeDibujo = 0;
        int capa = gameObject.layer;
        foreach (Lote l in lotesUsados)
        {
            var rp = new RenderParams(l.material)
            {
                layer = capa,
                shadowCastingMode = l.sombra,
                receiveShadows = true,
                lightProbeUsage = LightProbeUsage.Off,
                worldBounds = l.limites,
            };
            for (int inicio = 0; inicio < l.cuenta; inicio += MaxPorLlamada)
            {
                Graphics.RenderMeshInstanced(rp, l.mesh, l.submesh, l.matrices, Mathf.Min(MaxPorLlamada, l.cuenta - inicio), inicio);
                llamadasDeDibujo++;
            }
        }
    }

    // Prueba de cerros: no depende de hacia donde mira la camara, solo de donde esta, asi que se reparte entre
    // frames. Si la camara salta (minijuego, teletransporte) se rehacen todas de una vez.
    void ProbarCerros(Vector3 ojo)
    {
        if (!ocultarTrasCerros || relieve == null) return;

        bool todas = primeraPrueba || (ojo - ultimoOjo).sqrMagnitude > 20f * 20f;
        ultimoOjo = ojo;
        primeraPrueba = false;

        int n = todas ? celdas.Length : Mathf.Min(pruebasPorFrame, celdas.Length);
        for (int k = 0; k < n; k++)
        {
            if (siguientePrueba >= celdas.Length) siguientePrueba = 0;
            Celda c = celdas[siguientePrueba++];
            c.tapada = Tapada(c, ojo);
        }
    }

    bool Tapada(Celda c, Vector3 ojo)
    {
        Bounds b = c.limites;
        Vector3 centro = b.center;
        Vector2 aCelda = new Vector2(centro.x - ojo.x, centro.z - ojo.z);
        float dist = aCelda.magnitude;
        if (dist < tamanoCelda || dist > distanciaMaxima) return false;

        // tres puntos en la copa mas alta: el centro y los dos bordes de la celda vistos desde la camara
        Vector2 lado = new Vector2(-aCelda.y, aCelda.x) / dist * Mathf.Max(b.extents.x, b.extents.z);
        float arriba = b.max.y;
        return RayoTapado(ojo, new Vector3(centro.x, arriba, centro.z))
            && RayoTapado(ojo, new Vector3(centro.x + lado.x, arriba, centro.z + lado.y))
            && RayoTapado(ojo, new Vector3(centro.x - lado.x, arriba, centro.z - lado.y));
    }

    bool RayoTapado(Vector3 desde, Vector3 hasta)
    {
        float dx = hasta.x - desde.x, dz = hasta.z - desde.z;
        float largo = Mathf.Sqrt(dx * dx + dz * dz);
        float fin = largo - tamanoCelda * 0.75f;   // el suelo de la propia celda no cuenta
        // pasos que crecen con la distancia: saltarse un cerro angosto solo hace dibujar de mas
        for (float t = relievePaso * 2f; t < fin; t += relievePaso + t * 0.03f)
        {
            float f = t / largo;
            float y = desde.y + (hasta.y - desde.y) * f;
            if (Relieve(desde.x + dx * f, desde.z + dz * f) > y + 1f) return true;
        }
        return false;
    }

    float Relieve(float x, float z)
    {
        int ix = Mathf.Clamp((int)((x - origen.x) / relievePaso), 0, relieveRes - 1);
        int iz = Mathf.Clamp((int)((z - origen.z) / relievePaso), 0, relieveRes - 1);
        return relieve[iz * relieveRes + ix];
    }

    static Bounds Transformar(Matrix4x4 m, Bounds b)
    {
        Vector3 c = m.MultiplyPoint3x4(b.center);
        Vector3 e = b.extents;
        Vector3 ex = new Vector3(
            Mathf.Abs(m.m00) * e.x + Mathf.Abs(m.m01) * e.y + Mathf.Abs(m.m02) * e.z,
            Mathf.Abs(m.m10) * e.x + Mathf.Abs(m.m11) * e.y + Mathf.Abs(m.m12) * e.z,
            Mathf.Abs(m.m20) * e.x + Mathf.Abs(m.m21) * e.y + Mathf.Abs(m.m22) * e.z);
        return new Bounds(c, ex * 2f);
    }
}
