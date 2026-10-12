using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Pasarela de madera a lo largo de una curva, como el modificador "Pasarela en Curva" de Blender
// (Tools/Blender/pasarela_curva.py). Copia el modulo de Blender (PasarelaModulo.json, exportado con
// Tools/Blender/exportar_modulo_pasarela.py) una vez tras otra a lo largo de la curva:
//   tablones -> rigidos, siguen la direccion y la pendiente de la curva;
//   vigas    -> se doblan con la curva;
//   pilotes  -> verticales, estirados hasta el fondo (tierra o lecho marino).
// Cada modulo lleva un BoxCollider que cubre la superficie de los tablones.
//
// La curva pasa por los puntos (Catmull-Rom centripeta) y solo se usa en planta: la altura la pone
// el terreno. La cubierta va a `alturaSobreSuelo` sobre la tierra (o `alturaSobreAgua` sobre el mar),
// con la pendiente limitada y suavizada para que no copie cada bache.
//
// Lo generado NO se guarda en la escena (la malla de un tramo largo pesa varios MB): se rehace al
// abrir la escena, al entrar en Play y en el build, siempre igual porque la variacion sale de la semilla.
// Los puntos se dibujan en la Scene view (ver Assets/Editor/PasarelaCurvaEditor.cs).
[ExecuteAlways]
public class PasarelaCurva : MonoBehaviour
{
    [Tooltip("PasarelaModulo.json, exportado de Blender con Tools/Blender/exportar_modulo_pasarela.py")]
    public TextAsset modulo;
    public Material material;

    [Tooltip("Puntos por donde pasa la curva, locales a este objeto. Se dibujan en la Scene view.")]
    public List<Vector3> puntos = new List<Vector3>();

    [Header("Tamaño")]
    [Tooltip("Metros por unidad del módulo de Blender. Con 0.3 la pasarela mide ~1.9 m de ancho.")]
    public float escala = 0.3f;
    [Tooltip("Espacio entre un módulo y el siguiente, en metros (en Blender eran 0.15 unidades).")]
    public float separacion = 0.045f;
    [Tooltip("Estira apenas los módulos para que entre un número entero en la curva.")]
    public bool ajustarAlLargo = true;

    [Header("Altura")]
    [Tooltip("Altura de la cara de arriba de los tablones sobre la tierra. Con más de 0.3 (el stepOffset del " +
             "CharacterController del jugador) ya no se puede subir caminando desde el costado: se entra por las rampas.")]
    public float alturaSobreSuelo = 1.2f;
    [Tooltip("Largo de la rampa en los extremos que terminan en tierra: ahí la cubierta baja hasta el suelo para " +
             "poder subir caminando. 0 = sin rampa.")]
    public float rampa = 12f;
    public float nivelAgua = 0f;
    [Tooltip("Altura de la cubierta sobre el nivel del mar. El oleaje del shader tiene crestas de varios metros mar adentro.")]
    public float alturaSobreAgua = 1.5f;
    [Tooltip("Pendiente máxima de la cubierta (alto / largo).")]
    [Range(0.02f, 0.6f)] public float pendienteMaxima = 0.15f;
    [Tooltip("Largo de la ventana de suavizado de la altura, en metros.")]
    public float suavizado = 6f;
    [Tooltip("Cuánto se entierran los pilotes bajo el suelo.")]
    public float entierroPilotes = 0.8f;

    [Header("Variación")]
    public int semilla = 0;
    [Tooltip("Giro e inclinación al azar de cada tablón y pilote (0 = todos rectos).")]
    [Range(0f, 2f)] public float variacion = 1f;

    [Header("Colisión")]
    public float grosorColision = 0.4f;
    [Tooltip("Cuánto se alarga cada BoxCollider para que no queden rendijas entre módulos en las curvas.")]
    public float solapeColision = 0.15f;
    [Tooltip("Pone también un BoxCollider en cada pilote (los postes que asoman sobre la cubierta).")]
    public bool colisionPilotes = false;

    [Header("Rendimiento")]
    public int modulosPorBloque = 12;
    [Tooltip("Alto en pantalla (0-1) bajo el cual un bloque pasa a la versión sin bisel.")]
    [Range(0.01f, 0.6f)] public float lodCerca = 0.12f;
    [Tooltip("Alto en pantalla (0-1) bajo el cual un bloque deja de dibujarse.")]
    [Range(0.0005f, 0.05f)] public float lodCulling = 0.003f;
    public ShadowCastingMode sombras = ShadowCastingMode.On;

    public const string NombreGenerado = "_Pasarela (generada)";
    const float Paso = 0.25f;                    // metros entre muestras del perfil

    // Datos del ultimo Generar(), para el Inspector.
    [NonSerialized] public float largo;
    [NonSerialized] public int modulos;
    [NonSerialized] public int triangulos;
    [NonSerialized] public string error;

    void OnEnable() { Generar(); }

    // Al cerrar la escena los hijos se destruyen solos, pero las mallas no. Si solo se quito el
    // componente, el objeto generado queda huerfano: se borra en el proximo tick del Editor.
    void OnDestroy()
    {
        var hijo = transform.Find(NombreGenerado);
        if (hijo == null) return;
        foreach (var mf in hijo.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null) Destruir(mf.sharedMesh);
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            var go = hijo.gameObject;
            UnityEditor.EditorApplication.delayCall += () => { if (go != null) DestroyImmediate(go); };
        }
#endif
    }

#if UNITY_EDITOR
    // OnValidate no puede destruir objetos: se regenera en el proximo tick del Editor.
    void OnValidate()
    {
        if (Application.isPlaying) return;
        UnityEditor.EditorApplication.delayCall -= GenerarDiferido;
        UnityEditor.EditorApplication.delayCall += GenerarDiferido;
    }

    void GenerarDiferido()
    {
        if (this != null && isActiveAndEnabled) Generar();
    }
#endif

    // ---------------------------------------------------------------- curva

    // Puntos de la curva en mundo, cada `paso` metros de recorrido en planta (y = la de los puntos).
    public List<Vector3> MuestrearCurva(float paso)
    {
        var w = new List<Vector3>(puntos.Count);
        foreach (var p in puntos) w.Add(transform.TransformPoint(p));
        return Muestrear(w, paso);
    }

    public static List<Vector3> Muestrear(IList<Vector3> pts, float paso)
    {
        var salida = new List<Vector3>();
        if (pts.Count < 2) return salida;

        // Polilinea fina por Catmull-Rom; los extremos se prolongan en linea recta.
        var fina = new List<Vector3> { pts[0] };
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector3 p0 = i > 0 ? pts[i - 1] : 2 * pts[0] - pts[1];
            Vector3 p1 = pts[i], p2 = pts[i + 1];
            Vector3 p3 = i + 2 < pts.Count ? pts[i + 2] : 2 * pts[i + 1] - pts[i];
            int n = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(p1, p2) / 0.1f), 4, 2000);
            for (int k = 1; k <= n; k++) fina.Add(CatmullRom(p0, p1, p2, p3, k / (float)n));
        }

        // Re-muestreo a paso constante por largo de arco (en planta).
        salida.Add(fina[0]);
        float resto = paso;
        for (int i = 1; i < fina.Count; i++)
        {
            Vector3 a = fina[i - 1], b = fina[i];
            float d = Plano(b - a).magnitude;
            float t0 = 0f;
            while (d - t0 >= resto)
            {
                t0 += resto;
                salida.Add(Vector3.Lerp(a, b, t0 / d));
                resto = paso;
            }
            resto -= d - t0;
        }
        if (Plano(fina[fina.Count - 1] - salida[salida.Count - 1]).magnitude > paso * 0.1f)
            salida.Add(fina[fina.Count - 1]);
        return salida;
    }

    // Centripeta (alpha 0.5): no hace rulos ni se pasa de largo con puntos muy desparejos.
    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t0 = 0f;
        float t1 = t0 + Mathf.Max(Mathf.Sqrt(Vector3.Distance(p0, p1)), 1e-3f);
        float t2 = t1 + Mathf.Max(Mathf.Sqrt(Vector3.Distance(p1, p2)), 1e-3f);
        float t3 = t2 + Mathf.Max(Mathf.Sqrt(Vector3.Distance(p2, p3)), 1e-3f);
        float u = Mathf.Lerp(t1, t2, t);
        Vector3 a1 = (t1 - u) / (t1 - t0) * p0 + (u - t0) / (t1 - t0) * p1;
        Vector3 a2 = (t2 - u) / (t2 - t1) * p1 + (u - t1) / (t2 - t1) * p2;
        Vector3 a3 = (t3 - u) / (t3 - t2) * p2 + (u - t2) / (t3 - t2) * p3;
        Vector3 b1 = (t2 - u) / (t2 - t0) * a1 + (u - t0) / (t2 - t0) * a2;
        Vector3 b2 = (t3 - u) / (t3 - t1) * a2 + (u - t1) / (t3 - t1) * a3;
        return (t2 - u) / (t2 - t1) * b1 + (u - t1) / (t2 - t1) * b2;
    }

    static Vector3 Plano(Vector3 v) { v.y = 0f; return v; }

    // Altura del suelo (tierra o lecho marino) en mundo.
    public float Suelo(Vector3 p)
    {
        foreach (var t in Terrain.activeTerrains)
        {
            Vector3 o = t.transform.position, s = t.terrainData.size;
            if (p.x >= o.x && p.z >= o.z && p.x <= o.x + s.x && p.z <= o.z + s.z)
                return o.y + t.SampleHeight(p);
        }
        foreach (var h in Physics.RaycastAll(new Vector3(p.x, 5000f, p.z), Vector3.down, 10000f))
            if (!h.collider.transform.IsChildOf(transform)) return h.point.y;
        return nivelAgua - 5f;
    }

    // ---------------------------------------------------------------- perfil

    // La curva muestreada cada Paso metros, con la altura de la cubierta y su orientacion.
    class Perfil
    {
        public Vector3[] p;          // centro de la cara de arriba de los tablones
        public Vector3[] tan;        // direccion de avance con pendiente
        public Vector3[] tanH;       // direccion de avance en planta
        public float largo;

        public void Marco(float s, out Vector3 pos, out Quaternion rot, out Quaternion rotH)
        {
            float f = Mathf.Clamp(s / Paso, 0f, p.Length - 1.001f);
            int i = (int)f; f -= i;
            pos = Vector3.Lerp(p[i], p[i + 1], f);
            Vector3 th = Vector3.Lerp(tanH[i], tanH[i + 1], f).normalized;
            Vector3 t = Vector3.Lerp(tan[i], tan[i + 1], f).normalized;
            Vector3 derecha = Vector3.Cross(Vector3.up, th);
            rot = Quaternion.LookRotation(t, Vector3.Cross(t, derecha));
            rotH = Quaternion.LookRotation(th, Vector3.up);
        }
    }

    Perfil ConstruirPerfil()
    {
        var c = MuestrearCurva(Paso);
        int n = c.Count;
        if (n < 3) return null;

        // Altura minima: sobre la tierra o sobre el mar, lo que quede mas alto. En un extremo que cae en
        // tierra la altura sobre el suelo baja a 0 a lo largo de la rampa.
        var suelo = new float[n];
        for (int i = 0; i < n; i++) suelo[i] = Suelo(c[i]);
        bool rampaIni = rampa > 0f && suelo[0] > nivelAgua, rampaFin = rampa > 0f && suelo[n - 1] > nivelAgua;
        var minimo = new float[n];
        for (int i = 0; i < n; i++)
        {
            float f = 1f;
            if (rampaIni) f = Mathf.Min(f, i * Paso / rampa);
            if (rampaFin) f = Mathf.Min(f, (n - 1 - i) * Paso / rampa);
            f = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(f));
            minimo[i] = Mathf.Max(suelo[i] + alturaSobreSuelo * f, nivelAgua + alturaSobreAgua);
            if (f < 1f) minimo[i] = Mathf.Min(minimo[i], suelo[i] + alturaSobreSuelo * f + 0.01f);
        }

        // Envolvente con pendiente limitada (nunca baja del minimo), suavizado, y otra vez envolvente:
        // el suavizado redondea los quiebres pero puede hundir la cubierta en las lomas.
        var h = Envolvente((float[])minimo.Clone(), pendienteMaxima * Paso);
        int r = Mathf.Max(0, Mathf.RoundToInt(suavizado * 0.5f / Paso));
        h = Promedio(Promedio(h, r), r);
        for (int i = 0; i < n; i++) h[i] = Mathf.Max(h[i], minimo[i]);
        h = Envolvente(h, pendienteMaxima * Paso);

        var pf = new Perfil { p = new Vector3[n], tan = new Vector3[n], tanH = new Vector3[n], largo = (n - 1) * Paso };
        for (int i = 0; i < n; i++) pf.p[i] = new Vector3(c[i].x, h[i], c[i].z);
        for (int i = 0; i < n; i++)
        {
            Vector3 d = pf.p[Mathf.Min(i + 1, n - 1)] - pf.p[Mathf.Max(i - 1, 0)];
            pf.tan[i] = d.normalized;
            pf.tanH[i] = Plano(d).normalized;
        }
        return pf;
    }

    static float[] Envolvente(float[] h, float maxPaso)
    {
        for (int i = 1; i < h.Length; i++) h[i] = Mathf.Max(h[i], h[i - 1] - maxPaso);
        for (int i = h.Length - 2; i >= 0; i--) h[i] = Mathf.Max(h[i], h[i + 1] - maxPaso);
        return h;
    }

    static float[] Promedio(float[] h, int r)
    {
        if (r == 0) return h;
        var o = new float[h.Length];
        for (int i = 0; i < h.Length; i++)
        {
            float suma = 0f; int cuenta = 0;
            for (int k = Mathf.Max(0, i - r); k <= Mathf.Min(h.Length - 1, i + r); k++) { suma += h[k]; cuenta++; }
            o[i] = suma / cuenta;
        }
        return o;
    }

    // ---------------------------------------------------------------- modulo

    [Serializable] class PiezaJson { public float[] v, n, uv; public int[] t; }
    [Serializable] class ModuloJson { public PiezaJson[] piezas, piezasLod; }

    enum Tipo { Tablon, Viga, Pilote }

    class Pieza { public Vector3[] v, n; public Vector2[] uv; public int[] t; public Bounds caja; }

    class Modulo
    {
        public Pieza[] cerca, lejos;
        public Tipo[] tipo;
        public float zMin, zMax, xCentro, cubierta, mitadAncho;
    }

    static readonly Dictionary<int, Modulo> cache = new Dictionary<int, Modulo>();

    static Modulo LeerModulo(TextAsset json)
    {
        int clave = json.GetInstanceID() ^ json.text.Length;
        if (cache.TryGetValue(clave, out var m)) return m;

        var datos = JsonUtility.FromJson<ModuloJson>(json.text);
        if (datos?.piezas == null || datos.piezas.Length == 0) return null;
        m = new Modulo { cerca = Convertir(datos.piezas) };
        m.lejos = datos.piezasLod != null && datos.piezasLod.Length == datos.piezas.Length ? Convertir(datos.piezasLod) : m.cerca;

        // Clasificacion por la caja, igual que en Blender: alto -> pilote, largo en Z -> viga, el resto tablon.
        m.tipo = new Tipo[m.cerca.Length];
        m.zMin = float.MaxValue; m.zMax = float.MinValue; m.cubierta = float.MinValue;
        float xMin = float.MaxValue, xMax = float.MinValue;
        for (int i = 0; i < m.cerca.Length; i++)
        {
            Bounds b = m.cerca[i].caja; Vector3 s = b.size;
            m.tipo[i] = s.y > Mathf.Max(s.x, s.z) ? Tipo.Pilote : s.z > 2f * s.x ? Tipo.Viga : Tipo.Tablon;
            m.zMin = Mathf.Min(m.zMin, b.min.z); m.zMax = Mathf.Max(m.zMax, b.max.z);
            if (m.tipo[i] == Tipo.Tablon)
            {
                m.cubierta = Mathf.Max(m.cubierta, b.max.y);
                xMin = Mathf.Min(xMin, b.min.x); xMax = Mathf.Max(xMax, b.max.x);
            }
        }
        if (m.cubierta == float.MinValue) return null;
        m.xCentro = (xMin + xMax) * 0.5f;
        m.mitadAncho = (xMax - xMin) * 0.5f;
        cache[clave] = m;
        return m;
    }

    // Blender exporta un vertice por esquina de triangulo; aca se sueldan los iguales (posicion, normal y UV).
    static Pieza[] Convertir(PiezaJson[] js)
    {
        var r = new Pieza[js.Length];
        for (int k = 0; k < js.Length; k++)
        {
            var j = js[k];
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>();
            var mapa = new Dictionary<(Vector3, Vector3, Vector2), int>();
            var remap = new int[j.v.Length / 3];
            for (int i = 0; i < remap.Length; i++)
            {
                var clave = (new Vector3(j.v[3 * i], j.v[3 * i + 1], j.v[3 * i + 2]),
                             new Vector3(j.n[3 * i], j.n[3 * i + 1], j.n[3 * i + 2]),
                             new Vector2(j.uv[2 * i], j.uv[2 * i + 1]));
                if (!mapa.TryGetValue(clave, out int idx))
                {
                    idx = v.Count; mapa[clave] = idx;
                    v.Add(clave.Item1); n.Add(clave.Item2.normalized); uv.Add(clave.Item3);
                }
                remap[i] = idx;
            }
            var t = new int[j.t.Length];
            for (int i = 0; i < t.Length; i++) t[i] = remap[j.t[i]];
            var caja = new Bounds(v[0], Vector3.zero);
            foreach (var p in v) caja.Encapsulate(p);
            r[k] = new Pieza { v = v.ToArray(), n = n.ToArray(), uv = uv.ToArray(), t = t, caja = caja };
        }
        return r;
    }

    // ---------------------------------------------------------------- generar

    class Malla
    {
        public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> t = new List<int>();
        public void Limpiar() { v.Clear(); n.Clear(); uv.Clear(); t.Clear(); }
    }

    // Donde va una pieza de un modulo; se calcula una vez y sirve para la version de cerca y la de lejos.
    struct Colocacion
    {
        public Vector3 origen;       // tablon y pilote: centro de la pieza en mundo (pilote: solo xz)
        public Quaternion rot;
        public float arriba, abajo;  // pilote: y de la punta y del pie
        public float s0, escZ;       // viga: recorrido del modulo
        public Vector2 uv;           // desfase de la textura
    }

    public void Generar()
    {
        Limpiar();
        error = null; largo = 0f; modulos = 0; triangulos = 0;
        if (modulo == null) { error = "Falta el módulo (PasarelaModulo.json)."; return; }
        if (puntos.Count < 2) { error = "Dibuja al menos dos puntos."; return; }
        var m = LeerModulo(modulo);
        if (m == null) { error = "El módulo no tiene tablones; ¿es el JSON correcto?"; return; }
        var pf = ConstruirPerfil();
        if (pf == null) { error = "La curva es demasiado corta."; return; }

        float largoNominal = (m.zMax - m.zMin) * escala + separacion;
        int cuantos = ajustarAlLargo ? Mathf.Max(1, Mathf.RoundToInt(pf.largo / largoNominal))
                                     : Mathf.FloorToInt(pf.largo / largoNominal);
        if (cuantos < 1) { error = "La curva es más corta que un módulo."; return; }
        float paso = ajustarAlLargo ? pf.largo / cuantos : largoNominal;
        float escZ = escala * paso / largoNominal;
        float margen = separacion / escala * 0.5f;          // media separacion, en unidades del modulo

        var raiz = new GameObject(NombreGenerado);
        raiz.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        raiz.layer = gameObject.layer;
        raiz.transform.SetParent(transform, false);
        var colisiones = new GameObject("Colisiones").transform;
        colisiones.gameObject.layer = gameObject.layer;
        colisiones.SetParent(raiz.transform, false);

        Matrix4x4 aLocal = transform.worldToLocalMatrix;
        var cerca = new Malla(); var lejos = new Malla();
        var col = new Colocacion[m.cerca.Length];
        int bloque = 0;

        for (int k = 0; k < cuantos; k++)
        {
            float s0 = k * paso;
            float S(float z) => s0 + (z - m.zMin + margen) * escZ;

            for (int i = 0; i < m.cerca.Length; i++)
            {
                Bounds b = m.cerca[i].caja; Vector3 c = b.center;
                var cl = new Colocacion { s0 = s0, escZ = escZ,
                    uv = new Vector2(Azar(k, i, 1), Azar(k, i, 2)) };
                pf.Marco(S(c.z), out Vector3 pos, out Quaternion rot, out Quaternion rotH);
                Vector3 lado = new Vector3((c.x - m.xCentro) * escala, (c.y - m.cubierta) * escala, 0f);

                if (m.tipo[i] == Tipo.Tablon)
                {
                    var giro = Quaternion.Euler((Azar(k, i, 3) - 0.5f) * 1.0f * variacion,
                                                (Azar(k, i, 4) - 0.5f) * 2.4f * variacion,
                                                (Azar(k, i, 5) - 0.5f) * 2.0f * variacion);
                    if (Azar(k, i, 6) < 0.5f) giro *= Quaternion.Euler(0f, 180f, 0f);   // tablon dado vuelta
                    cl.origen = pos + rot * lado;
                    cl.rot = rot * giro;
                }
                else if (m.tipo[i] == Tipo.Pilote)
                {
                    cl.rot = rotH * Quaternion.Euler((Azar(k, i, 3) - 0.5f) * 1.5f * variacion,
                                                     (Azar(k, i, 4) - 0.5f) * 8f * variacion,
                                                     (Azar(k, i, 5) - 0.5f) * 1.5f * variacion);
                    cl.origen = pos + rotH * new Vector3(lado.x, 0f, 0f);
                    cl.arriba = pos.y + (b.max.y - m.cubierta) * escala;
                    cl.abajo = Mathf.Min(Suelo(cl.origen) - entierroPilotes, cl.arriba - 0.5f);
                    if (colisionPilotes)
                    {
                        var go = new GameObject("Pilote_" + k + "_" + i) { layer = gameObject.layer };
                        go.transform.SetParent(colisiones, false);
                        go.transform.SetPositionAndRotation(new Vector3(cl.origen.x, (cl.arriba + cl.abajo) * 0.5f, cl.origen.z), cl.rot);
                        go.AddComponent<BoxCollider>().size = new Vector3(b.size.x * escala, cl.arriba - cl.abajo, b.size.z * escala);
                    }
                }
                col[i] = cl;
            }
            for (int i = 0; i < m.cerca.Length; i++)
            {
                Emitir(cerca, m.cerca[i], m.cerca[i].caja, m.tipo[i], col[i], m, pf, aLocal);
                Emitir(lejos, m.lejos[i], m.cerca[i].caja, m.tipo[i], col[i], m, pf, aLocal);
            }

            // Colision del modulo: una caja de cuerda entre el comienzo y el final, con la cara de arriba
            // a ras de los tablones.
            pf.Marco(s0, out Vector3 ini, out _, out _);
            pf.Marco(s0 + paso, out Vector3 fin, out _, out _);
            Vector3 adelante = (fin - ini).normalized;
            Vector3 derecha = Vector3.Cross(Vector3.up, Plano(adelante)).normalized;
            Vector3 arriba = Vector3.Cross(adelante, derecha);
            var caja = new GameObject("Modulo_" + k) { layer = gameObject.layer };
            caja.transform.SetParent(colisiones, false);
            caja.transform.SetPositionAndRotation((ini + fin) * 0.5f - arriba * (grosorColision * 0.5f),
                                                  Quaternion.LookRotation(adelante, arriba));
            caja.AddComponent<BoxCollider>().size = new Vector3(m.mitadAncho * 2f * escala, grosorColision,
                                                                Vector3.Distance(ini, fin) + solapeColision);

            if ((k + 1) % Mathf.Max(1, modulosPorBloque) == 0 || k == cuantos - 1)
            {
                CrearBloque(raiz.transform, bloque++, cerca, lejos);
                cerca.Limpiar(); lejos.Limpiar();
            }
        }
        largo = pf.largo; modulos = cuantos;
    }

    void Emitir(Malla dst, Pieza pz, Bounds caja, Tipo tipo, Colocacion cl, Modulo m, Perfil pf, Matrix4x4 aLocal)
    {
        int baseIdx = dst.v.Count;
        Vector3 c = caja.center;
        for (int i = 0; i < pz.v.Length; i++)
        {
            Vector3 v = pz.v[i], w, n;
            if (tipo == Tipo.Tablon)
            {
                Vector3 d = v - c;
                w = cl.origen + cl.rot * new Vector3(d.x * escala, d.y * escala, d.z * cl.escZ);
                n = cl.rot * pz.n[i];
            }
            else if (tipo == Tipo.Pilote)
            {
                float f = Mathf.InverseLerp(caja.min.y, caja.max.y, v.y);
                Vector3 d = cl.rot * new Vector3((v.x - c.x) * escala, 0f, (v.z - c.z) * escala);
                w = new Vector3(cl.origen.x + d.x, Mathf.Lerp(cl.abajo, cl.arriba, f), cl.origen.z + d.z);
                n = cl.rot * pz.n[i];
            }
            else
            {
                float s = cl.s0 + (v.z - m.zMin + separacion / escala * 0.5f) * cl.escZ;
                pf.Marco(s, out Vector3 pos, out Quaternion rot, out _);
                w = pos + rot * new Vector3((v.x - m.xCentro) * escala, (v.y - m.cubierta) * escala, 0f);
                n = rot * pz.n[i];
            }
            dst.v.Add(aLocal.MultiplyPoint3x4(w));
            dst.n.Add(aLocal.MultiplyVector(n).normalized);
            dst.uv.Add(pz.uv[i] + cl.uv);
        }
        foreach (int t in pz.t) dst.t.Add(baseIdx + t);
    }

    void CrearBloque(Transform raiz, int indice, Malla cerca, Malla lejos)
    {
        var go = new GameObject("Bloque_" + indice) { layer = gameObject.layer };
        go.transform.SetParent(raiz, false);
        var r0 = CrearRenderer(go.transform, "LOD0", cerca);
        var r1 = CrearRenderer(go.transform, "LOD1", lejos);
        var lod = go.AddComponent<LODGroup>();
        lod.SetLODs(new[] { new LOD(lodCerca, new Renderer[] { r0 }), new LOD(lodCulling, new Renderer[] { r1 }) });
        lod.RecalculateBounds();
    }

    Renderer CrearRenderer(Transform padre, string nombre, Malla m)
    {
        var mesh = new Mesh { name = "Pasarela_" + padre.name + "_" + nombre, hideFlags = HideFlags.DontSave };
        if (m.v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(m.v); mesh.SetNormals(m.n); mesh.SetUVs(0, m.uv); mesh.SetTriangles(m.t, 0);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        triangulos += nombre == "LOD0" ? m.t.Count / 3 : 0;

        var go = new GameObject(nombre) { layer = gameObject.layer };
        go.transform.SetParent(padre, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = sombras;
        return r;
    }

    // Borra lo generado (tambien lo que quedo de antes de recompilar, que ya no esta en ninguna lista).
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

    static void Destruir(UnityEngine.Object o)
    {
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }

    float Azar(int a, int b, int c)
    {
        unchecked
        {
            uint h = (uint)semilla * 0x9E3779B1u ^ (uint)a * 0x85EBCA77u ^ (uint)b * 0xC2B2AE3Du ^ (uint)c * 0x27D4EB2Fu;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
