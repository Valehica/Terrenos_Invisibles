using System;
using UnityEngine;

/// <summary>
/// Géiser de Puchuldiza: echa vapor todo el tiempo y cada tanto entra en erupción.
///
/// Ciclo: Reposo (hilos de vapor) → Borboteo (el agua salta en el cráter, sube el
/// vapor) → Erupción (chorro de agua y nube de vapor, con pulsos) → Calma (el vapor
/// vuelve de a poco al de reposo). Las partículas se arman por código al empezar,
/// en hijos que no se guardan en la escena; solo hace falta asignar el material.
///
/// Va en cada géiser (el FBX Geiser). El menú Terrenos/Norte/Preparar géiseres lo
/// agrega a todos los de la escena y le asigna el material del vapor.
/// </summary>
[DisallowMultipleComponent]
public class GeiserController : MonoBehaviour
{
    public enum Fase { Reposo, Borboteo, Erupcion, Calma }

    [Header("Ciclo")]
    [Tooltip("Segundos entre una erupción y la siguiente (se sortea cada vez).")]
    [SerializeField] private Vector2 reposo = new Vector2(25f, 50f);
    [Tooltip("Segundos que borbotea antes de que salte el chorro.")]
    [SerializeField] private Vector2 borboteo = new Vector2(3f, 6f);
    [Tooltip("Segundos que dura el chorro.")]
    [SerializeField] private Vector2 erupcion = new Vector2(5f, 10f);
    [Tooltip("Segundos que tarda el vapor en volver al de reposo después del chorro.")]
    [SerializeField] private float calma = 6f;
    [Tooltip("La primera erupción cae en un momento al azar, para que los géiseres vecinos no salten juntos.")]
    [SerializeField] private bool desfasarAlInicio = true;

    [Header("Boca")]
    [Tooltip("Fondo del cráter, en coordenadas locales del modelo.")]
    [SerializeField] private Vector3 boca = new Vector3(0f, 0.5f, 0f);
    [Tooltip("Radio del cráter, en metros locales del modelo.")]
    [SerializeField] private float radioBoca = 0.3f;

    [Header("Chorro")]
    [Tooltip("Altura del chorro en metros para un géiser de escala 1.")]
    [SerializeField] private float alturaChorro = 5f;
    [Tooltip("Los géiseres más grandes saltan más alto y echan más vapor.")]
    [SerializeField] private bool escalarConTamano = true;
    [SerializeField] private Color colorAgua = new Color(0.88f, 0.94f, 1f, 0.75f);

    [Header("Vapor")]
    [Tooltip("Material de partículas (URP Particles/Simple Lit, transparente, textura 2x2 de bocanadas).")]
    [SerializeField] private Material material;
    [SerializeField] private Color colorVapor = new Color(0.93f, 0.94f, 0.96f, 1f);
    [Tooltip("Bocanadas por segundo en reposo; en la erupción salen varias veces más.")]
    [SerializeField] private float vaporReposo = 3f;
    [Tooltip("Hacia dónde lleva el viento el vapor, en grados (0 = +Z, 90 = +X).")]
    [Range(0f, 360f)] [SerializeField] private float direccionViento = 70f;
    [Tooltip("Velocidad del viento en m/s.")]
    [SerializeField] private float fuerzaViento = 0.8f;

    [Header("Sonido")]
    [Range(0f, 1f)] [SerializeField] private float volumen = 0.8f;
    [Tooltip("Desde cuántos metros deja de oírse.")]
    [SerializeField] private float distanciaSonido = 45f;

    [Header("Rendimiento")]
    [Tooltip("Más lejos que esto de la cámara no emite partículas (el ciclo sigue corriendo).")]
    [SerializeField] private float distanciaMaxima = 150f;

    public Fase FaseActual => fase;

    /// <summary>Se dispara cuando salta el chorro.</summary>
    public event Action<GeiserController> EmpezoErupcion;

    private const float Gravedad = 9.81f;
    // Luz (sol + ambiente) con la que el vapor se ve de su color; con menos se oscurece.
    private const float plenoSol = 1.5f;
    private const float luzMinima = 0.1f;
    private static readonly Vector3[] arriba = { Vector3.up };
    private static readonly Color[] ambiente = new Color[1];

    private Fase fase;
    private float faseInicio;
    private float faseDuracion;
    private float semilla;
    private float escala = 1f;
    private bool cerca = true;
    private float proximoChequeo;

    private ParticleSystem vapor, chorro, gotas;
    private AudioSource audioSource;
    private static AudioClip silbido;

    // ------------------------------------------------------------ Ciclo

    /// <summary>Salta el chorro ahora (pasa por un borboteo corto). No hace nada si ya está en erupción.</summary>
    [ContextMenu("Erupcionar ahora")]
    public void Erupcionar()
    {
        if (fase == Fase.Borboteo || fase == Fase.Erupcion) return;
        CambiarFase(Fase.Borboteo, 1.5f);
    }

    private void Start()
    {
        semilla = UnityEngine.Random.value * 100f;
        if (escalarConTamano)
        {
            Vector3 s = transform.lossyScale;
            escala = (Mathf.Abs(s.x) + Mathf.Abs(s.y) + Mathf.Abs(s.z)) / 3f;
        }

        ArmarParticulas();
        ArmarSonido();

        float espera = UnityEngine.Random.Range(reposo.x, reposo.y);
        if (desfasarAlInicio) espera *= UnityEngine.Random.value;
        CambiarFase(Fase.Reposo, espera);
    }

    private void CambiarFase(Fase nueva, float duracion)
    {
        fase = nueva;
        faseInicio = Time.time;
        faseDuracion = duracion;

        if (nueva == Fase.Erupcion)
        {
            // El primer golpe del chorro sale de una.
            if (cerca) chorro.Emit(Mathf.RoundToInt(40 * escala));
            EmpezoErupcion?.Invoke(this);
        }
    }

    private void Avanzar()
    {
        switch (fase)
        {
            case Fase.Reposo: CambiarFase(Fase.Borboteo, UnityEngine.Random.Range(borboteo.x, borboteo.y)); break;
            case Fase.Borboteo: CambiarFase(Fase.Erupcion, UnityEngine.Random.Range(erupcion.x, erupcion.y)); break;
            case Fase.Erupcion: CambiarFase(Fase.Calma, calma); break;
            default: CambiarFase(Fase.Reposo, UnityEngine.Random.Range(reposo.x, reposo.y)); break;
        }
    }

    private void Update()
    {
        if (vapor == null) return;

        float t = Time.time - faseInicio;
        if (t >= faseDuracion)
        {
            Avanzar();
            t = 0f;
        }
        float k = faseDuracion > 0f ? Mathf.Clamp01(t / faseDuracion) : 1f;

        if (Time.time >= proximoChequeo)
        {
            proximoChequeo = Time.time + 0.5f;
            var cam = Camera.main;
            cerca = cam == null || (cam.transform.position - transform.position).sqrMagnitude < distanciaMaxima * distanciaMaxima;
            Iluminar();
        }

        // Ritmo de cada parte según la fase. "empuje" es lo fuerte que sube el vapor (0..1).
        float rVapor = vaporReposo, rChorro = 0f, rGotas = 0f, empuje = 0f, fuerza = 0f;
        float vol = 0.08f, tono = 0.8f;
        switch (fase)
        {
            case Fase.Borboteo:
                float golpes = Pulso(3.5f);
                rVapor = vaporReposo * Mathf.Lerp(1.5f, 4f, k);
                rGotas = Mathf.Lerp(8f, 35f, k) * golpes;
                empuje = 0.25f * k;
                vol = Mathf.Lerp(0.15f, 0.4f, k) * (0.7f + 0.3f * golpes);
                tono = 0.65f;
                break;
            case Fase.Erupcion:
                // Sube de golpe, pulsa mientras dura y al final el chorro se cae.
                float sube = Mathf.Clamp01(t / 0.6f);
                float baja = Mathf.Clamp01((faseDuracion - t) / 1.8f);
                fuerza = sube * baja * (0.75f + 0.25f * Pulso(1.3f));
                rChorro = 220f * fuerza;
                rVapor = vaporReposo * Mathf.Lerp(4f, 10f, fuerza);
                rGotas = 20f * fuerza;
                empuje = Mathf.Max(0.35f, fuerza);
                vol = 0.45f + 0.55f * fuerza;
                tono = 0.85f + 0.2f * fuerza;
                break;
            case Fase.Calma:
                rVapor = vaporReposo * Mathf.Lerp(5f, 1f, k);
                rGotas = Mathf.Lerp(6f, 0f, k);
                empuje = Mathf.Lerp(0.3f, 0f, k);
                vol = Mathf.Lerp(0.3f, 0.08f, k);
                break;
        }

        float factor = cerca ? escala : 0f;
        SetRate(vapor, rVapor * factor);
        SetRate(chorro, rChorro * factor);
        SetRate(gotas, rGotas * factor);

        var mv = vapor.main;
        mv.startSpeed = new ParticleSystem.MinMaxCurve(Mathf.Lerp(0.4f, 3f, empuje), Mathf.Lerp(0.9f, 5f, empuje));
        // Con más empuje las bocanadas se separan más: más grandes para que la columna no se corte.
        mv.startSize = new ParticleSystem.MinMaxCurve(0.6f * escala * (1f + 1.5f * empuje), 1.2f * escala * (1f + 1.5f * empuje));

        if (rChorro > 0f)
        {
            // v = √(2gh): con esa velocidad hacia arriba el agua llega a la altura pedida.
            float v = Mathf.Sqrt(2f * Gravedad * alturaChorro * escala) * Mathf.Lerp(0.45f, 1f, fuerza);
            var mc = chorro.main;
            mc.startSpeed = new ParticleSystem.MinMaxCurve(v * 0.8f, v);
        }

        if (audioSource != null)
        {
            float a = 1f - Mathf.Exp(-4f * Time.deltaTime);
            audioSource.volume = Mathf.Lerp(audioSource.volume, vol * volumen, a);
            audioSource.pitch = Mathf.Lerp(audioSource.pitch, tono, a);
        }
    }

    /// <summary>
    /// El material es sin luz (con luz, a contraluz el vapor salía azul del ambiente del cielo),
    /// así que acá se oscurece y se tiñe con el sol y el ambiente: blanco de día, naranjo al
    /// atardecer, apagado de noche.
    /// </summary>
    private void Iluminar()
    {
        Color luz = Color.black;
        var sol = RenderSettings.sun;
        if (sol != null && sol.isActiveAndEnabled)
        {
            float alto = Mathf.Clamp01(-sol.transform.forward.y * 4f); // se apaga al bajar al horizonte
            luz = sol.color * (sol.intensity * alto);
        }
        RenderSettings.ambientProbe.Evaluate(arriba, ambiente);
        // Del ambiente solo el brillo: su color es el azul del cielo y teñía el vapor.
        float gris = ambiente[0].grayscale;
        luz += new Color(gris, gris, gris);
        luz *= 1f / plenoSol;
        float max = Mathf.Max(luz.r, Mathf.Max(luz.g, luz.b));
        if (max > 1f) luz *= 1f / max;
        luz.r = Mathf.Max(luz.r, luzMinima);
        luz.g = Mathf.Max(luz.g, luzMinima);
        luz.b = Mathf.Max(luz.b, luzMinima);

        Colorear(vapor, colorVapor, luz);
        Colorear(chorro, colorAgua, luz);
        Colorear(gotas, colorAgua, luz);
    }

    private static void Colorear(ParticleSystem ps, Color baseColor, Color luz)
    {
        var main = ps.main;
        main.startColor = new Color(baseColor.r * luz.r, baseColor.g * luz.g, baseColor.b * luz.b, baseColor.a);
    }

    /// <summary>Ruido lento entre 0 y 1, distinto en cada géiser.</summary>
    private float Pulso(float frecuencia) => Mathf.PerlinNoise(Time.time * frecuencia, semilla);

    private static void SetRate(ParticleSystem ps, float rate)
    {
        var e = ps.emission;
        e.rateOverTime = rate;
    }

    // ------------------------------------------------------------ Partículas

    private void ArmarParticulas()
    {
        Vector3 posBoca = transform.TransformPoint(boca);
        float radio = radioBoca * escala;
        Vector3 viento = Quaternion.Euler(0f, direccionViento, 0f) * Vector3.forward * fuerzaViento;

        // Vapor: bocanadas que suben, crecen, se las lleva el viento y se deshacen.
        vapor = Crear("Vapor", posBoca, 160);
        {
            var main = vapor.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f * escala, 1.2f * escala);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = colorVapor;
            main.gravityModifier = -0.02f;

            var shape = vapor.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = radio;

            var vel = vapor.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(viento.x);
            vel.y = new ParticleSystem.MinMaxCurve(0f);
            vel.z = new ParticleSystem.MinMaxCurve(viento.z);

            // El vapor frena al subir: el empuje inicial se pierde y queda solo el viento.
            var limit = vapor.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 6f), new Keyframe(0.5f, 1.6f), new Keyframe(1f, 1.2f)));
            limit.dampen = 0.15f;

            var size = vapor.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 3.2f)));

            var rot = vapor.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

            var col = vapor.colorOverLifetime;
            col.enabled = true;
            col.color = Desvanecer(0.45f);

            var noise = vapor.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.strength = 0.35f;
            noise.frequency = 0.4f;
            noise.scrollSpeed = 0.2f;

            Flipbook(vapor);
        }

        // Chorro: gotas estiradas que salen rectas hacia arriba y caen con la gravedad.
        chorro = Crear("Chorro", posBoca, 600);
        {
            var main = chorro.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f * escala, 0.35f * escala);
            main.startColor = colorAgua;
            main.gravityModifier = 1f;

            var shape = chorro.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 4f;
            shape.radius = radio * 0.5f;

            var force = chorro.forceOverLifetime;
            force.enabled = true;
            force.space = ParticleSystemSimulationSpace.World;
            force.x = new ParticleSystem.MinMaxCurve(viento.x * 0.4f);
            force.y = new ParticleSystem.MinMaxCurve(0f);
            force.z = new ParticleSystem.MinMaxCurve(viento.z * 0.4f);

            var size = chorro.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1.8f)));

            var col = chorro.colorOverLifetime;
            col.enabled = true;
            col.color = Desvanecer(1f);

            var rend = chorro.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = 0.06f;
            rend.lengthScale = 1.5f;

            Flipbook(chorro);
        }

        // Gotas: el agua que salta dentro del cráter mientras borbotea.
        gotas = Crear("Borboteo", posBoca, 120);
        {
            var main = gotas.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f * escala, 0.18f * escala);
            main.startColor = colorAgua;
            main.gravityModifier = 1f;

            var shape = gotas.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 25f;
            shape.radius = radio;

            var col = gotas.colorOverLifetime;
            col.enabled = true;
            col.color = Desvanecer(1f);

            var rend = gotas.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = 0.05f;
            rend.lengthScale = 1.2f;

            Flipbook(gotas);
        }
    }

    private ParticleSystem Crear(string nombre, Vector3 posicion, int max)
    {
        var go = new GameObject(nombre);
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(transform, false);
        // El cono apunta hacia arriba aunque el géiser esté inclinado.
        go.transform.SetPositionAndRotation(posicion, Quaternion.LookRotation(Vector3.up, Vector3.forward));
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        // Solo la escala propia: la del géiser (a veces no uniforme) ya entra en "escala".
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.maxParticles = max;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        if (material != null) rend.sharedMaterial = material;

        ps.Play();
        return ps;
    }

    /// <summary>La textura trae 4 bocanadas en 2x2: cada partícula toma una al azar.</summary>
    private static void Flipbook(ParticleSystem ps)
    {
        var sheet = ps.textureSheetAnimation;
        sheet.enabled = true;
        sheet.numTilesX = 2;
        sheet.numTilesY = 2;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 0.999f);
        sheet.cycleCount = 1;
    }

    private static Gradient Desvanecer(float alfa)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(alfa, 0.12f), new GradientAlphaKey(alfa * 0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    // ------------------------------------------------------------ Sonido

    private void ArmarSonido()
    {
        if (silbido == null) silbido = Silbido();
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.hideFlags = HideFlags.DontSave;
        audioSource.clip = silbido;
        audioSource.loop = true;
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.minDistance = 3f;
        audioSource.maxDistance = distanciaSonido;
        audioSource.dopplerLevel = 0f;
        audioSource.volume = 0f;
        audioSource.pitch = 0.8f;
        // Cada géiser arranca en otro punto del bucle para que no suenen al unísono.
        audioSource.timeSamples = UnityEngine.Random.Range(0, silbido.samples);
        audioSource.Play();
    }

    /// <summary>
    /// Bucle de vapor: siseo (ruido sin graves) sobre un retumbo grave que va y viene.
    /// Se genera una vez y lo comparten todos los géiseres.
    /// </summary>
    private static AudioClip Silbido()
    {
        const int rate = 44100;
        int n = rate * 5;
        var data = new float[n];
        var rng = new System.Random(23);
        float lpAgudo = 0f, lpGrave1 = 0f, lpGrave2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float ruido = (float)(rng.NextDouble() * 2.0 - 1.0);
            lpAgudo += (ruido - lpAgudo) * 0.25f;
            float siseo = ruido - lpAgudo;
            lpGrave1 += (ruido - lpGrave1) * 0.004f;
            lpGrave2 += (lpGrave1 - lpGrave2) * 0.004f;
            float retumbo = lpGrave2 * 60f * (0.6f + 0.4f * Mathf.Sin(t * Mathf.PI * 0.8f));
            float vaiven = 0.8f + 0.2f * Mathf.Sin(t * Mathf.PI * 0.4f + 1f);
            data[i] = siseo * 0.35f * vaiven + retumbo * 0.5f;
        }
        // Cruce suave del final con el principio para que el bucle no haga clic.
        int fade = rate / 4;
        for (int i = 0; i < fade; i++)
        {
            float k = i / (float)fade;
            data[i] = data[i] * k + data[n - fade + i] * (1f - k);
        }
        Array.Resize(ref data, n - fade);
        var clip = AudioClip.Create("geiser_vapor", data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.6f, 0.85f, 1f, 0.8f);
        Vector3 p = transform.TransformPoint(boca);
        Vector3 s = transform.lossyScale;
        float e = escalarConTamano ? (Mathf.Abs(s.x) + Mathf.Abs(s.y) + Mathf.Abs(s.z)) / 3f : 1f;
        Gizmos.DrawWireSphere(p, radioBoca * e);
        Gizmos.DrawLine(p, p + Vector3.up * alturaChorro * e);
    }
}
