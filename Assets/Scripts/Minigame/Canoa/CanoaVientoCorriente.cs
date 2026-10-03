using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Viento y corrientes de la travesía en canoa.
///
/// Corrientes: zonas de la ruta (en metros recorridos) donde el agua se mueve de
/// costado o a lo largo del canal. La canoa se mueve con el agua, así que para
/// cruzar una fila de rocas con corriente de costado hay que apuntar aguas arriba
/// del paso. Se ven como espuma que flota alrededor de la canoa en la dirección
/// del agua.
///
/// Viento: sopla del oeste (los vientos que dominan los canales australes), cambia
/// de dirección de a poco y trae ráfagas. Deriva la canoa y le gira la proa hacia
/// sotavento; sube por tramo, de casi nada en la salida al máximo en la llegada.
///
/// Lo usa <see cref="CanoaMinigameManager"/>: le entrega a la canoa
/// <see cref="Sample"/> y le arma el indicador en el HUD.
/// </summary>
public class CanoaVientoCorriente : MonoBehaviour
{
    [Serializable]
    public struct Corriente
    {
        public string nombre;
        [Tooltip("Metros recorridos por la ruta donde empieza y termina (entra y sale gradual).")]
        public float desde, hasta;
        [Tooltip("m/s de costado respecto de la ruta: positivo empuja a la derecha (hacia la costa), negativo a la izquierda.")]
        public float deCostado;
        [Tooltip("m/s a lo largo de la ruta: negativo es corriente en contra.")]
        public float aLoLargo;
        [Tooltip("Si es mayor que 0, la corriente de costado cambia de lado cada tantos metros (remolinos entre las rocas).")]
        public float alternaCada;
    }

    [Header("Corrientes")]
    [SerializeField] private Corriente[] corrientes =
    {
        new Corriente { nombre = "Corriente hacia la costa", desde = 150f, hasta = 285f, deCostado = 0.35f },
        new Corriente { nombre = "Corriente en contra en el canal", desde = 300f, hasta = 450f, deCostado = 0.35f, aLoLargo = -0.5f, alternaCada = 45f },
        new Corriente { nombre = "Corriente cruzada entre las rocas", desde = 462f, hasta = 645f, deCostado = -0.6f },
        new Corriente { nombre = "Remolinos en el último paso", desde = 655f, hasta = 780f, deCostado = 0.45f, aLoLargo = -0.2f, alternaCada = 30f },
    };
    [SerializeField] private float rampaMetros = 15f;

    [Header("Viento")]
    [Tooltip("Hacia dónde sopla, en grados (0 = norte/+Z, 90 = este/+X). Del oeste hacia el este: 90.")]
    [SerializeField] private float windHeading = 85f;
    [SerializeField] private float windWander = 30f;
    [Tooltip("Viento sostenido en m/s cuando la escala del tramo es 1.")]
    [SerializeField] private float baseWind = 1.6f;
    [Tooltip("Lo que suma una ráfaga en m/s cuando la escala del tramo es 1.")]
    [SerializeField] private float gustWind = 3f;
    [SerializeField] private Vector2 gustInterval = new Vector2(10f, 18f);
    [SerializeField] private Vector2 gustDuration = new Vector2(2f, 3.5f);
    [Tooltip("Escala del viento por tramo (mismo orden que los tramos del manager).")]
    [SerializeField] private float[] windByTramo = { 0.15f, 0.5f, 0.7f, 0.85f, 1f };

    [Header("Espuma de la corriente")]
    [SerializeField] private Material foamMaterial;
    [SerializeField] private float foamRadius = 16f;
    [SerializeField] private int maxFoam = 450;

    public event Action<string> Aviso;

    public Vector3 Wind => wind;
    public bool IsGusting => gustAmount > 0.5f;

    private Transform route;
    private readonly List<Vector3> points = new List<Vector3>();
    private readonly List<float> cumulative = new List<float>();
    private Func<float, int> tramoAt;

    private bool active;
    private Vector3 wind;
    private float windScale;
    private float gustAmount;
    private float gustTarget;
    private float nextGustAt;
    private float gustEndsAt;
    private int lastCorriente = -1;
    private float lastAlong;

    private AudioSource windAudio;
    private ParticleSystem foam;
    private ParticleSystem.Particle[] foamBuffer;
    private OleajeMuestreo oleaje;
    private Transform follow;

    // Indicador del HUD.
    private RectTransform windArrow;
    private Image windArrowShaft, windArrowHead;
    private TMP_Text windLabel;
    private RectTransform flowArrow;
    private Image flowArrowShaft, flowArrowHead;
    private TMP_Text flowLabel;
    private Camera viewCamera;

    public void Setup(Transform routeRoot, Func<float, int> tramoIndexAt, Transform canoe, Camera cam, Renderer seaRenderer)
    {
        route = routeRoot;
        tramoAt = tramoIndexAt;
        follow = canoe;
        viewCamera = cam;
        oleaje = new OleajeMuestreo(seaRenderer);

        points.Clear();
        cumulative.Clear();
        float len = 0f;
        if (route != null)
            foreach (Transform child in route)
            {
                Vector3 p = child.position;
                p.y = 0f;
                if (points.Count > 0) len += Vector3.Distance(points[points.Count - 1], p);
                points.Add(p);
                cumulative.Add(len);
            }

        windAudio = gameObject.AddComponent<AudioSource>();
        windAudio.clip = WindLoop();
        windAudio.loop = true;
        windAudio.playOnAwake = false;
        windAudio.spatialBlend = 0f;
        windAudio.volume = 0f;

        BuildFoam();
    }

    /// <summary>Enciende o apaga viento y corriente (solo actúan mientras se navega).</summary>
    public void SetActive(bool on)
    {
        active = on;
        if (on)
        {
            if (!windAudio.isPlaying) windAudio.Play();
            if (foam != null) foam.Play();
        }
        else
        {
            if (foam != null) foam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    public void ResetRun()
    {
        gustAmount = gustTarget = 0f;
        nextGustAt = Time.time + UnityEngine.Random.Range(gustInterval.x, gustInterval.y);
        lastCorriente = -1;
        wind = Vector3.zero;
    }

    /// <summary>Lo llama la canoa en cada paso de física.</summary>
    public void Sample(Vector3 position, out Vector3 waterFlow, out Vector3 windOut)
    {
        waterFlow = Vector3.zero;
        windOut = Vector3.zero;
        if (!active) return;

        waterFlow = FlowAt(position, out _, out _);
        windOut = wind;
    }

    private Vector3 FlowAt(Vector3 position, out float along, out int strongest)
    {
        along = Project(position, out Vector3 tangent);
        strongest = -1;
        Vector3 right = new Vector3(tangent.z, 0f, -tangent.x);
        Vector3 flow = Vector3.zero;
        float best = 0f;

        for (int i = 0; i < corrientes.Length; i++)
        {
            var c = corrientes[i];
            float w = Mathf.Min(Mathf.InverseLerp(c.desde, c.desde + rampaMetros, along),
                                1f - Mathf.InverseLerp(c.hasta - rampaMetros, c.hasta, along));
            if (w <= 0f) continue;

            float side = c.deCostado;
            if (c.alternaCada > 0f)
            {
                // Cambia de lado de forma suave, no de golpe.
                float phase = (along - c.desde) / c.alternaCada * Mathf.PI;
                side *= Mathf.Clamp(Mathf.Sin(phase) * 2.5f, -1f, 1f);
            }

            flow += (right * side + tangent * c.aLoLargo) * w;
            if (w > best) { best = w; strongest = i; }
        }
        return flow;
    }

    private void Update()
    {
        if (!active || follow == null) return;

        Vector3 flow = FlowAt(follow.position, out float along, out int zone);
        lastAlong = along;

        // Aviso al entrar a cada zona de corriente.
        if (zone != lastCorriente)
        {
            if (zone >= 0 && flow.magnitude > 0.3f) Aviso?.Invoke(corrientes[zone].nombre + ".");
            lastCorriente = zone;
        }

        UpdateWind(along);
        UpdateFoam(flow);
        UpdateHud(flow);

        float w = wind.magnitude;
        windAudio.volume = Mathf.Lerp(windAudio.volume, Mathf.Clamp01(w / 8f) * 0.55f, 1f - Mathf.Exp(-3f * Time.deltaTime));
        windAudio.pitch = 0.8f + Mathf.Clamp01(w / 8f) * 0.45f;
    }

    private void UpdateWind(float along)
    {
        int tramo = tramoAt != null ? tramoAt(along) : 0;
        float targetScale = windByTramo != null && windByTramo.Length > 0
            ? windByTramo[Mathf.Clamp(tramo, 0, windByTramo.Length - 1)]
            : 1f;
        windScale = Mathf.MoveTowards(windScale, targetScale, Time.deltaTime * 0.2f);

        if (Time.time >= nextGustAt && gustTarget <= 0f)
        {
            gustTarget = UnityEngine.Random.Range(0.7f, 1f);
            gustEndsAt = Time.time + UnityEngine.Random.Range(gustDuration.x, gustDuration.y);
            if (windScale > 0.4f) Aviso?.Invoke("¡Ráfaga de viento!");
        }
        if (gustTarget > 0f && Time.time >= gustEndsAt)
        {
            gustTarget = 0f;
            nextGustAt = Time.time + UnityEngine.Random.Range(gustInterval.x, gustInterval.y);
        }
        // Sube rápido y se va de a poco, como una ráfaga de verdad.
        gustAmount = Mathf.MoveTowards(gustAmount, gustTarget, Time.deltaTime * (gustTarget > gustAmount ? 1.6f : 0.5f));

        float heading = windHeading + (Mathf.PerlinNoise(Time.time * 0.03f, 3.1f) - 0.5f) * 2f * windWander;
        float steady = baseWind * (0.75f + 0.5f * Mathf.PerlinNoise(Time.time * 0.15f, 7.7f));
        float strength = (steady + gustWind * gustAmount) * windScale;
        wind = Quaternion.Euler(0f, heading, 0f) * Vector3.forward * strength;
    }

    // ------------------------------------------------------------ Espuma

    private void BuildFoam()
    {
        var go = new GameObject("EspumaCorriente");
        go.transform.SetParent(transform, false);
        foam = go.AddComponent<ParticleSystem>();
        foam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = foam.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 6f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(1f, 1f, 1f, 0.75f);
        main.maxParticles = maxFoam;
        main.playOnAwake = false;

        var emission = foam.emission;
        emission.rateOverTime = 0f;

        var shape = foam.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = foamRadius;
        shape.rotation = new Vector3(90f, 0f, 0f); // el círculo acostado sobre el agua

        var col = foam.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.2f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var vel = foam.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;

        var rend = foam.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        if (foamMaterial != null) rend.sharedMaterial = foamMaterial;

        foamBuffer = new ParticleSystem.Particle[maxFoam];
    }

    private void UpdateFoam(Vector3 flow)
    {
        if (foam == null) return;

        Vector3 p = follow.position;
        foam.transform.position = new Vector3(p.x, p.y, p.z);

        // La espuma solo aparece donde hay corriente, y va a la velocidad del agua.
        float speed = flow.magnitude;
        var emission = foam.emission;
        emission.rateOverTime = speed > 0.15f ? Mathf.Lerp(18f, 55f, Mathf.Clamp01(speed / 1.2f)) : 0f;

        var vel = foam.velocityOverLifetime;
        // Un poco exagerada para que se lea desde la cámara.
        vel.x = new ParticleSystem.MinMaxCurve(flow.x * 1.4f);
        vel.y = new ParticleSystem.MinMaxCurve(0f);
        vel.z = new ParticleSystem.MinMaxCurve(flow.z * 1.4f);

        // Cada mancha se apoya sobre la ola que tiene debajo.
        int n = foam.GetParticles(foamBuffer);
        for (int i = 0; i < n; i++)
        {
            Vector3 fp = foamBuffer[i].position;
            fp.y = oleaje.HeightAt(fp, 1) + 0.12f;
            foamBuffer[i].position = fp;
        }
        foam.SetParticles(foamBuffer, n);
    }

    // ------------------------------------------------------------ HUD

    /// <summary>Indicador arriba a la derecha: flecha del viento y flecha de la corriente, vistas desde la cámara.</summary>
    public void BuildHud(Transform hud)
    {
        var box = new GameObject("VientoCorriente", typeof(RectTransform), typeof(Image));
        box.transform.SetParent(hud, false);
        box.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
        box.GetComponent<Image>().raycastTarget = false;
        var rect = (RectTransform)box.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
        rect.sizeDelta = new Vector2(360f, 160f);
        rect.anchoredPosition = new Vector2(-48f, -48f);

        windArrow = Arrow(rect, new Vector2(-90f, 12f), new Color(1f, 1f, 1f, 0.95f), out windArrowShaft, out windArrowHead);
        windLabel = Label(rect, new Vector2(-90f, -58f));
        flowArrow = Arrow(rect, new Vector2(90f, 12f), new Color(0.45f, 0.9f, 1f, 0.95f), out flowArrowShaft, out flowArrowHead);
        flowLabel = Label(rect, new Vector2(90f, -58f));
    }

    private void UpdateHud(Vector3 flow)
    {
        if (windArrow == null || viewCamera == null) return;

        Vector3 camFwd = viewCamera.transform.forward;
        camFwd.y = 0f;
        if (camFwd.sqrMagnitude < 1e-4f) camFwd = Vector3.forward;

        SetArrow(windArrow, windArrowShaft, windArrowHead, wind, camFwd, 8f);
        float w = wind.magnitude;
        windLabel.text = w < 0.8f ? "Viento: calma" : IsGusting ? "Viento: RÁFAGA" : w < 3f ? "Viento: suave" : "Viento: fuerte";
        windLabel.color = IsGusting ? new Color(1f, 0.75f, 0.4f) : Color.white;

        SetArrow(flowArrow, flowArrowShaft, flowArrowHead, flow, camFwd, 1.2f);
        float f = flow.magnitude;
        flowLabel.text = f < 0.15f ? "Corriente: no hay" : f < 0.7f ? "Corriente: débil" : "Corriente: fuerte";
    }

    private static void SetArrow(RectTransform arrow, Image shaft, Image head, Vector3 dir, Vector3 camFwd, float full)
    {
        float mag = dir.magnitude;
        float k = Mathf.Clamp01(mag / full);
        arrow.gameObject.SetActive(mag > 0.05f);
        if (mag <= 0.05f) return;

        // Arriba en la pantalla es hacia donde mira la cámara.
        float angle = Vector3.SignedAngle(camFwd, dir, Vector3.up);
        arrow.localRotation = Quaternion.Euler(0f, 0f, -angle);
        float len = Mathf.Lerp(18f, 60f, k);
        shaft.rectTransform.sizeDelta = new Vector2(7f, len);
        shaft.rectTransform.anchoredPosition = Vector2.zero;
        head.rectTransform.anchoredPosition = new Vector2(0f, len * 0.5f);
        Color c = shaft.color;
        c.a = Mathf.Lerp(0.45f, 1f, k);
        shaft.color = c;
        head.color = c;
    }

    private static RectTransform Arrow(RectTransform parent, Vector2 pos, Color color, out Image shaft, out Image head)
    {
        var root = new GameObject("Flecha", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var rect = (RectTransform)root.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = new Vector2(80f, 80f);

        shaft = NewImage("Asta", rect, color);
        head = NewImage("Punta", rect, color);
        head.rectTransform.sizeDelta = new Vector2(22f, 22f);
        head.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        return rect;
    }

    private static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static TMP_Text Label(RectTransform parent, Vector2 pos)
    {
        var go = new GameObject("Etiqueta", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.fontSize = 21f;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        var r = t.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.sizeDelta = new Vector2(175f, 30f);
        r.anchoredPosition = pos;
        return t;
    }

    // ------------------------------------------------------------ Ruta

    private float Project(Vector3 pos, out Vector3 tangent)
    {
        pos.y = 0f;
        tangent = Vector3.forward;
        if (points.Count < 2) return 0f;

        float best = float.MaxValue, along = 0f;
        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector3 a = points[i], b = points[i + 1];
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(pos - a, ab) / ab.sqrMagnitude);
            float d = (pos - (a + ab * t)).sqrMagnitude;
            if (d < best)
            {
                best = d;
                tangent = ab.normalized;
                along = cumulative[i] + ab.magnitude * t;
            }
        }
        return along;
    }

    /// <summary>Ruido de viento en bucle: ruido filtrado con un vaivén lento.</summary>
    private static AudioClip WindLoop()
    {
        const int rate = 44100;
        int n = rate * 4;
        var data = new float[n];
        var rng = new System.Random(11);
        float lp1 = 0f, lp2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            float cutoff = 0.02f + 0.03f * (0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 0.5f));
            lp1 += (noise - lp1) * cutoff;
            lp2 += (lp1 - lp2) * cutoff;
            data[i] = lp2 * 6f;
        }
        // Cruce suave del final con el principio para que el bucle no haga clic.
        int fade = rate / 4;
        for (int i = 0; i < fade; i++)
        {
            float k = i / (float)fade;
            data[i] = data[i] * k + data[n - fade + i] * (1f - k);
        }
        Array.Resize(ref data, n - fade);
        var clip = AudioClip.Create("canoa_viento", data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
