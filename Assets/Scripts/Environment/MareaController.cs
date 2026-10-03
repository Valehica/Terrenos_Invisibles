using UnityEngine;

/// <summary>
/// Marea del océano: sube y baja el nivel del agua y acompaña con el oleaje.
///
/// El shader del agua lee el nivel del mar del propio transform
/// (unity_ObjectToWorld._m13) para calcular la profundidad contra el heightmap
/// bakeado del terreno, así que mover este objeto en Y ya arrastra solo la
/// espuma de orilla, el aplanado de olas en lo bajo y el desvanecido del borde.
/// Este script solo mueve el transform y modula el oleaje.
///
/// Va en el GameObject "Sea".
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshRenderer))]
public class MareaController : MonoBehaviour
{
    public enum FuenteDeTiempo
    {
        [Tooltip("Ciclo propio, independiente del reloj del mundo.")]
        Libre,
        [Tooltip("Atada al reloj de Tenkoku. OJO: si timeCompression esta en 0 el reloj " +
                 "no avanza y la marea se queda quieta.")]
        HoraDelMundo
    }

    [Header("Ciclo")]
    public FuenteDeTiempo fuente = FuenteDeTiempo.Libre;

    [Tooltip("Duración de un ciclo completo (pleamar a pleamar) en minutos reales. Solo en modo Libre.")]
    [Min(0.1f)] public float periodoMinutosReales = 4f;

    [Tooltip("Duración de un ciclo completo en horas del mundo. 12.4 es el valor real de una marea semidiurna.")]
    [Min(0.1f)] public float periodoHorasDelMundo = 12.4f;

    [Tooltip("Corre el ciclo para que la pleamar no caiga siempre a la misma hora.")]
    [Range(0f, 1f)] public float desfase = 0f;

    [Header("Altura")]
    [Tooltip("Altura del nivel medio del mar, en Y de mundo. Es un valor fijo a proposito: " +
             "si se tomara del transform se correria solo, porque el transform ya viene " +
             "movido por la marea del frame anterior.")]
    public float nivelMedio = 0f;

    [Tooltip("Metros entre el nivel medio y la pleamar. La bajamar baja lo mismo.")]
    public float amplitud = 1.5f;

    [Tooltip("Suavizado del nivel. El reloj del mundo avanza de a un minuto, y sin " +
             "esto la marea daría saltitos en vez de moverse continua.")]
    [Min(0f)] public float suavizado = 0.6f;

    [Header("Oleaje segun la marea")]
    [Tooltip("Cuánto crece el oleaje en pleamar. 1 = igual que el material, 1.5 = mitad más grande.")]
    [Range(0.5f, 2f)] public float oleajeEnPleamar = 1.5f;

    [Tooltip("Cuánto se calma el oleaje en bajamar.")]
    [Range(0.3f, 1.5f)] public float oleajeEnBajamar = 0.9f;

    [Tooltip("Alcance de la espuma de orilla en pleamar, en metros.")]
    public float espumaEnPleamar = 3.0f;

    [Tooltip("Alcance de la espuma de orilla en bajamar, en metros.")]
    public float espumaEnBajamar = 1.6f;

    [Header("Previsualizacion en el editor")]
    [Tooltip("Sin Play, este control mueve la marea a mano para poder encuadrar.")]
    [Range(0f, 1f)] public float fasePrevia = 0.25f;

    [Tooltip("Dibuja las líneas de pleamar y bajamar al seleccionar el objeto.")]
    public bool mostrarGuias = true;

    /// <summary>0 = bajamar, 1 = pleamar.</summary>
    public float Altura01 { get; private set; }

    /// <summary>Nivel del mar actual, en metros de mundo.</summary>
    public float Nivel => transform.position.y;

    /// <summary>True mientras la marea está subiendo.</summary>
    public bool Subiendo { get; private set; }

    private MeshRenderer mr;
    private MaterialPropertyBlock bloque;
    private float nivelSuavizado;
    private float velocidadSuavizado;
    private bool suavizadoIniciado;

    // Valores del material sin modular, leídos una vez.
    private Vector4[] olasBase = new Vector4[4];
    private float crestaBase;
    private bool materialLeido;

    private static readonly int[] IdsOlas =
    {
        Shader.PropertyToID("_WaveA"), Shader.PropertyToID("_WaveB"),
        Shader.PropertyToID("_WaveC"), Shader.PropertyToID("_WaveD")
    };
    private static readonly int IdCresta = Shader.PropertyToID("_CrestFoamHeight");
    private static readonly int IdEspuma = Shader.PropertyToID("_ShoreFoamDistance");

    private void OnEnable()
    {
        mr = GetComponent<MeshRenderer>();
        bloque = new MaterialPropertyBlock();
        nivelSuavizado = nivelMedio;
        suavizadoIniciado = false;
        LeerMaterial();
    }

    private void OnDisable()
    {
        // Se devuelve el renderer a los valores del material para no dejar el
        // oleaje congelado en el último estado de la marea.
        if (mr != null) mr.SetPropertyBlock(null);
        transform.position = new Vector3(transform.position.x, nivelMedio, transform.position.z);
    }

    private void LeerMaterial()
    {
        var mat = mr != null ? mr.sharedMaterial : null;
        if (mat == null) { materialLeido = false; return; }

        for (int i = 0; i < 4; i++)
            olasBase[i] = mat.HasProperty(IdsOlas[i]) ? mat.GetVector(IdsOlas[i]) : Vector4.zero;
        crestaBase = mat.HasProperty(IdCresta) ? mat.GetFloat(IdCresta) : 1.75f;
        materialLeido = true;
    }

    private void Update()
    {
        if (!materialLeido) LeerMaterial();

        float fase = CalcularFase();

        // seno desfasado para que 0 sea bajamar y 0.5 pleamar
        Altura01 = 0.5f - 0.5f * Mathf.Cos(fase * Mathf.PI * 2f);
        Subiendo = Mathf.Sin(fase * Mathf.PI * 2f) > 0f;

        float objetivo = nivelMedio + Mathf.Lerp(-amplitud, amplitud, Altura01);

        if (Application.isPlaying && suavizado > 0f && suavizadoIniciado)
            nivelSuavizado = Mathf.SmoothDamp(nivelSuavizado, objetivo, ref velocidadSuavizado, suavizado);
        else
            nivelSuavizado = objetivo;   // el primer frame arranca en su lugar, sin deslizarse
        suavizadoIniciado = true;

        var p = transform.position;
        transform.position = new Vector3(p.x, nivelSuavizado, p.z);

        AplicarOleaje();
    }

    private float CalcularFase()
    {
        if (!Application.isPlaying) return Mathf.Repeat(fasePrevia + desfase, 1f);

        if (fuente == FuenteDeTiempo.HoraDelMundo)
        {
            if (TryHoraDelMundo(out float horas))
                return Mathf.Repeat(horas / Mathf.Max(periodoHorasDelMundo, 0.1f) + desfase, 1f);
            // Sin Tenkoku en escena se cae al ciclo libre en vez de quedarse quieta.
        }

        return Mathf.Repeat(Time.time / (Mathf.Max(periodoMinutosReales, 0.1f) * 60f) + desfase, 1f);
    }

    // Tenkoku se lee por reflexión a propósito: así este script sigue compilando
    // si algún día se saca el asset del proyecto.
    private static System.Type tipoTenkoku;
    private static System.Reflection.FieldInfo campoHora, campoMinuto, campoDia;
    private static Object instanciaTenkoku;
    private static bool tenkokuBuscado;

    private bool TryHoraDelMundo(out float horas)
    {
        horas = 0f;
        if (!tenkokuBuscado)
        {
            tenkokuBuscado = true;
            tipoTenkoku = System.Type.GetType("Tenkoku.Core.TenkokuModule, Assembly-CSharp");
            if (tipoTenkoku != null)
            {
                campoHora = tipoTenkoku.GetField("currentHour");
                campoMinuto = tipoTenkoku.GetField("currentMinute");
                campoDia = tipoTenkoku.GetField("currentDay");
            }
        }
        if (tipoTenkoku == null || campoHora == null || campoMinuto == null) return false;

        if (instanciaTenkoku == null)
            instanciaTenkoku = FindFirstObjectByType(tipoTenkoku);
        if (instanciaTenkoku == null) return false;

        float h = System.Convert.ToSingle(campoHora.GetValue(instanciaTenkoku));
        float m = System.Convert.ToSingle(campoMinuto.GetValue(instanciaTenkoku));
        float d = campoDia != null ? System.Convert.ToSingle(campoDia.GetValue(instanciaTenkoku)) : 0f;
        horas = d * 24f + h + m / 60f;
        return true;
    }

    private void AplicarOleaje()
    {
        if (mr == null || !materialLeido) return;

        float escala = Mathf.Lerp(oleajeEnBajamar, oleajeEnPleamar, Altura01);

        bloque.Clear();
        for (int i = 0; i < 4; i++)
        {
            var w = olasBase[i];
            // z es la pendiente de la ola de Gerstner: es lo que fija la amplitud.
            bloque.SetVector(IdsOlas[i], new Vector4(w.x, w.y, w.z * escala, w.w));
        }
        // El umbral de espuma de cresta acompaña a la amplitud. Si quedara fijo,
        // en pleamar la espuma saltaría en toda la superficie y en bajamar nunca.
        bloque.SetFloat(IdCresta, crestaBase * escala);
        bloque.SetFloat(IdEspuma, Mathf.Lerp(espumaEnBajamar, espumaEnPleamar, Altura01));
        mr.SetPropertyBlock(bloque);
    }

    private void OnDrawGizmosSelected()
    {
        if (!mostrarGuias) return;
        float baseY = nivelMedio;
        var c = transform.position;
        var tam = new Vector3(400f, 0.02f, 400f);

        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.5f);
        Gizmos.DrawWireCube(new Vector3(c.x, baseY + amplitud, c.z), tam);   // pleamar
        Gizmos.color = new Color(0.9f, 0.7f, 0.3f, 0.5f);
        Gizmos.DrawWireCube(new Vector3(c.x, baseY - amplitud, c.z), tam);   // bajamar
    }
}
