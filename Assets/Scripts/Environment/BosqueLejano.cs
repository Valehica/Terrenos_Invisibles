using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Bosque lejano: cada arbol del Terrain que tenga billboard (BosqueBillboards) se dibuja de lejos como dos
// cuadros cruzados con su foto. Los cuadros de una zona de `tamBloque` metros van todos en una sola malla, asi
// que 60 000 arboles cuestan unas pocas decenas de draw calls y nada de CPU por arbol.
//
// De cerca dibuja el Terrain con el LODGroup del prefab, que apaga el arbol a cierta distancia segun su tamano
// (el Terrain no corta por treeDistance a los arboles con LODGroup). Para cada arbol se calcula esa misma
// distancia, igual que Unity (lodBias * tamano * escala / (2 tan(fov/2) * corte), desde el punto de referencia
// del LODGroup), y va en el vertice; el shader Terrenos/ArbolBillboard esconde el billboard mas cerca que eso.
// Asi en cada arbol hay una sola representacion a la vez.
//
// Ademas dibuja los arboles que son SOLO billboard (`soloLejos`): los del paisaje fuera de la zona que recorre
// el jugador (ZonaJugador). No estan en el Terrain, asi que no tienen modelo, collider ni costo de LOD; su
// distancia de corte es 0 y se dibujan siempre. Formato del archivo: int cantidad, y por arbol
// int entrada (indice en billboards.entradas), float x, y, z (pie), giro (rad), escala ancho, escala alto,
// uint color RGBA. Lo escribe ArbolesRocasSur.
//
// Como la pasarela, lo generado no se guarda en la escena: se arma en OnEnable desde el TerrainData. Despues de
// cambiar los arboles del Terrain hay que llamar Generar() (el menu de ArbolesRocasSur ya lo hace).
[ExecuteAlways]
public class BosqueLejano : MonoBehaviour
{
    public Terrain terreno;
    public BosqueBillboards billboards;
    [Tooltip("Arboles que existen solo como billboard (lejos de la zona del jugador). Lo escribe ArbolesRocasSur.")]
    public TextAsset soloLejos;
    [Tooltip("Lado de cada zona que se junta en una malla, en metros.")]
    public float tamBloque = 200f;
    [Tooltip("Campo de vision de la camara del juego: los LOD dependen de el.")]
    public float fovCamara = 60f;

    public const string NombreGenerado = "_Bosque lejano (generado)";

    [System.NonSerialized] public int arboles;
    [System.NonSerialized] public int bloques;

    void OnEnable() { Generar(); }

    void OnDestroy()
    {
        var hijo = transform.Find(NombreGenerado);
        if (hijo == null) return;
        foreach (var mf in hijo.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null) Destruir(mf.sharedMesh);
    }

    class Bloque
    {
        public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
        public readonly List<Vector4> refCorte = new List<Vector4>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<Color32> c = new List<Color32>();
        public readonly List<int> t = new List<int>();
    }

    public void Generar()
    {
        Limpiar();
        arboles = 0; bloques = 0;
        if (terreno == null) terreno = GetComponent<Terrain>();
        if (terreno == null || billboards == null || billboards.material == null) return;
        var td = terreno.terrainData;
        var protos = td.treePrototypes;
        var porProto = new BosqueBillboards.Entrada[protos.Length];
        var corteBase = new float[protos.Length];       // distancia de corte del LODGroup a escala 1
        var referencia = new Vector3[protos.Length];
        float tan = 2f * Mathf.Tan(fovCamara * 0.5f * Mathf.Deg2Rad);
        for (int i = 0; i < protos.Length; i++)
        {
            foreach (var e in billboards.entradas)
                if (e.prefab == protos[i].prefab) porProto[i] = e;
            var lod = protos[i].prefab != null ? protos[i].prefab.GetComponent<LODGroup>() : null;
            if (lod == null || lod.lodCount == 0) continue;
            var lods = lod.GetLODs();
            corteBase[i] = QualitySettings.lodBias * lod.size / (tan * lods[lods.Length - 1].screenRelativeTransitionHeight);
            referencia[i] = lod.localReferencePoint;
        }

        Vector3 o = terreno.transform.position, s = td.size;
        var bloquesPorClave = new Dictionary<long, Bloque>();
        foreach (var ti in td.treeInstances)
        {
            if (ti.prototypeIndex >= porProto.Length) continue;
            var e = porProto[ti.prototypeIndex];
            if (e == null) continue;
            Vector3 pie = o + Vector3.Scale(ti.position, s);
            long clave = ((long)Mathf.FloorToInt(pie.x / tamBloque) << 32) ^ (uint)Mathf.FloorToInt(pie.z / tamBloque);
            if (!bloquesPorClave.TryGetValue(clave, out var b)) bloquesPorClave[clave] = b = new Bloque();

            // Igual que el Terrain: giro en Y (radianes), widthScale en X/Z y heightScale en Y.
            var rot = Quaternion.AngleAxis(ti.rotation * Mathf.Rad2Deg, Vector3.up);
            var esc = new Vector3(ti.widthScale, ti.heightScale, ti.widthScale);
            Color32 col = ti.color;
            // El LODGroup escala su tamano por la mayor componente de la escala.
            var rc = (Vector4)(pie + rot * Vector3.Scale(referencia[ti.prototypeIndex], esc));
            rc.w = corteBase[ti.prototypeIndex] * Mathf.Max(ti.widthScale, ti.heightScale);
            Cuadro(b, pie, rot, esc, e.centro, Vector3.right, e.ladoFrente, e.uvFrente, col, rc);
            Cuadro(b, pie, rot, esc, e.centro, Vector3.back, e.ladoCostado, e.uvCostado, col, rc);
            arboles++;
        }

        if (soloLejos != null)
        {
            var datos = soloLejos.bytes;
            using (var lector = new System.IO.BinaryReader(new System.IO.MemoryStream(datos)))
            {
                int n = lector.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    int idx = lector.ReadInt32();
                    var pie = new Vector3(lector.ReadSingle(), lector.ReadSingle(), lector.ReadSingle());
                    float giro = lector.ReadSingle(), ancho = lector.ReadSingle(), alto = lector.ReadSingle();
                    uint c = lector.ReadUInt32();
                    if (idx < 0 || idx >= billboards.entradas.Length) continue;
                    var e = billboards.entradas[idx];
                    long clave = ((long)Mathf.FloorToInt(pie.x / tamBloque) << 32) ^ (uint)Mathf.FloorToInt(pie.z / tamBloque);
                    if (!bloquesPorClave.TryGetValue(clave, out var b)) bloquesPorClave[clave] = b = new Bloque();
                    var rot = Quaternion.AngleAxis(giro * Mathf.Rad2Deg, Vector3.up);
                    var esc = new Vector3(ancho, alto, ancho);
                    var col = new Color32((byte)c, (byte)(c >> 8), (byte)(c >> 16), (byte)(c >> 24));
                    var rc = new Vector4(pie.x, pie.y, pie.z, 0f);
                    Cuadro(b, pie, rot, esc, e.centro, Vector3.right, e.ladoFrente, e.uvFrente, col, rc);
                    Cuadro(b, pie, rot, esc, e.centro, Vector3.back, e.ladoCostado, e.uvCostado, col, rc);
                    arboles++;
                }
            }
        }

        var raiz = new GameObject(NombreGenerado) { layer = gameObject.layer };
        raiz.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        raiz.transform.SetParent(transform, false);
        foreach (var kv in bloquesPorClave)
        {
            var b = kv.Value;
            var mesh = new Mesh { name = "BosqueLejano_" + bloques, hideFlags = HideFlags.DontSave };
            if (b.v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(b.v); mesh.SetNormals(b.n); mesh.SetUVs(0, b.uv); mesh.SetUVs(1, b.refCorte);
            mesh.SetColors(b.c); mesh.SetTriangles(b.t, 0);
            mesh.RecalculateBounds();

            var go = new GameObject("Bloque_" + bloques) { layer = gameObject.layer };
            go.transform.SetParent(raiz.transform, false);
            // La malla esta en mundo: el bloque queda en el origen aunque el Terrain este corrido.
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = new Vector3(1f / transform.lossyScale.x, 1f / transform.lossyScale.y, 1f / transform.lossyScale.z);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = billboards.material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            bloques++;
        }
    }

    // Un cuadro vertical que pasa por el centro del arbol; `eje` es su horizontal antes de girar.
    // La normal va hacia arriba: con la del cuadro, el lado que no mira al sol saldria negro.
    static void Cuadro(Bloque b, Vector3 pie, Quaternion rot, Vector3 esc, Vector3 centro, Vector3 eje, float lado, Rect uv, Color32 col, Vector4 refCorte)
    {
        int i0 = b.v.Count;
        float m = lado * 0.5f;
        for (int k = 0; k < 4; k++)
        {
            float sx = (k == 1 || k == 2) ? 1f : -1f, sy = k >= 2 ? 1f : -1f;
            Vector3 local = centro + eje * (sx * m) + Vector3.up * (sy * m);
            b.v.Add(pie + rot * Vector3.Scale(local, esc));
            b.n.Add(Vector3.up);
            b.uv.Add(new Vector2(sx < 0 ? uv.xMin : uv.xMax, sy < 0 ? uv.yMin : uv.yMax));
            b.refCorte.Add(refCorte);
            b.c.Add(col);
        }
        b.t.Add(i0); b.t.Add(i0 + 2); b.t.Add(i0 + 1);
        b.t.Add(i0); b.t.Add(i0 + 3); b.t.Add(i0 + 2);
    }

    public void Limpiar()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var hijo = transform.GetChild(i);
            if (hijo.name != NombreGenerado) continue;
            foreach (var mf in hijo.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) Destruir(mf.sharedMesh);
            Destruir(hijo.gameObject);
        }
    }

    static void Destruir(Object o)
    {
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }
}
