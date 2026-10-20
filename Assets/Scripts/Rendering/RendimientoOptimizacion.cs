using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Optimizaciones de rendimiento comunes a las tres escenas. Vive en el objeto "RendimientoOptimización"
// (prefab Assets/Prefabs/RendimientoOptimizacion.prefab, puesto en Norte, Centro y Sur): cambiar el prefab
// cambia las tres escenas. Todo actua solo en Play y se deshace al desactivar el componente; no toca assets.
//
//   1. Culling por tamaño en pantalla: cada MeshRenderer suelto deja de dibujarse cuando quedaria mas chico
//      que tamanoMinimoEnPantalla (como el ultimo corte de un LODGroup, pero sin armar LODs a mano). Usa un
//      CullingGroup, asi que no cuesta nada por frame salvo cuando un objeto cruza su distancia.
//   2. Batching / instancing: asegura el SRP Batcher y el dibujo instanciado del Terrain, y acorta el LOD de
//      los arboles del Terrain que no usan ArbolesVisibles (Centro, Norte). El static batching
//      y el GPU instancing de los materiales se preparan en el Editor con
//      Terrenos/Rendimiento/Preparar escenas (RendimientoOptimizacionEditor.cs).
//   3. Postproceso limitado: un Volume global de prioridad alta que solo pisa parametros de costo (el bloom
//      sin filtrado bicubico y a un cuarto de resolucion), nunca los de look de Volumen_<zona>.
//   4. Sonda de reflejos del cielo "horneada" al inicio: Tenkoku re-renderiza la sonda y recalcula la luz
//      ambiente cada 2 s aunque la hora este quieta. Se deja renderizar al arrancar y despues solo cuando
//      cambia la hora (el minijuego de la luga hace anochecer).
//   5. Carga al inicio: con la pantalla todavia en negro (SceneFadeIn) renderiza la escena en 6 direcciones
//      desde la camara, asi las texturas, mallas y shaders suben a la GPU antes de jugar y no en el primer giro.
//
// Lo que NO se hace y por que: lightmaps horneados. Las tres escenas son terreno abierto con el sol de
// Tenkoku (que la luga mueve) y las sombras medidas en Centro costaban ~0 ms; hornear no daria FPS y
// llenaria la memoria de una GPU de 1,8 GB. Sin luz horneada, las light probes para objetos dinamicos
// tampoco aportan: la luz ambiente ya sale de Tenkoku.
[DisallowMultipleComponent]
[DefaultExecutionOrder(-500)]
public class RendimientoOptimizacion : MonoBehaviour
{
    [Header("1. Culling por tamaño en pantalla")]
    public bool cullingPorTamano = true;
    [Tooltip("Fraccion del alto de la pantalla por debajo de la cual un objeto deja de dibujarse (0.01 = 1%, ~11 px a 1080p).")]
    [Range(0.002f, 0.05f)] public float tamanoMinimoEnPantalla = 0.01f;
    [Tooltip("Los objetos con radio mayor a este (m) nunca se cortan (cerros, mar, cielo).")]
    public float radioMaximo = 16f;
    [Tooltip("Capas que no se cortan nunca.")]
    public LayerMask capasExcluidas;
    [Tooltip("Cada cuantos segundos se buscan renderers nuevos (los que crean los minijuegos o la pasarela). 0 = solo al inicio.")]
    public float reescanearCada = 10f;
    [Tooltip("Esferas a las que se les actualiza la posicion por frame (para objetos que se mueven).")]
    public int actualizacionesPorFrame = 128;

    [Header("2. Batching e instancing")]
    [Tooltip("Asegura el SRP Batcher de URP (ya viene encendido en PC_RPAsset).")]
    public bool srpBatcher = true;
    [Tooltip("Dibujo instanciado del Terrain (una llamada por parche en vez de una por parche y capa).")]
    public bool terrenoInstanciado = true;
    [Tooltip("Multiplica el LOD de los arboles del Terrain: con 0.35 cambian de LOD y se dejan de dibujar a un 35% de la distancia. " +
             "En Centro los 14k arboles y arbustos eran ~40% del frame y no se nota la diferencia. 1 = no tocar. " +
             "(ArbolesVisibles con sus valores por defecto lo empeoraba: 150-230 ms contra 109.)")]
    [Range(0.1f, 1f)] public float sesgoLodArboles = 0.35f;

    [Header("3. Postproceso limitado")]
    public bool limitarPostproceso = true;
    [Tooltip("Bloom sin filtrado bicubico de alta calidad.")]
    public bool bloomSinAltaCalidad = true;
    [Tooltip("Bloom calculado a un cuarto de resolucion en vez de la mitad.")]
    public bool bloomCuartoDeResolucion = true;

    [Header("Terreno y texturas")]
    [Tooltip("Tope para Terrain.basemapDistance (m): mas lejos se dibuja la textura base en vez de mezclar todas las capas. Norte venia en 1000. 0 = no tocar.")]
    public float distanciaBasemapMaxima = 250f;
    [Tooltip("Filtrado anisotropico solo en las texturas que lo piden, no forzado en todas (la calidad PC lo tenia en ForceEnable).")]
    public bool anisotropicoPorTextura = true;

    [Header("4. Sonda de reflejos del cielo")]
    [Tooltip("Tenkoku solo re-renderiza la sonda de reflejos cuando cambia la hora.")]
    public bool congelarSondaDelCielo = true;
    [Tooltip("Segundos de Play durante los que Tenkoku actualiza la sonda normalmente antes de congelarla.")]
    public float segundosDeArranque = 3f;

    [Header("5. Carga al inicio")]
    [Tooltip("Renderiza la escena en 6 direcciones al arrancar para subir texturas, mallas y shaders a la GPU.")]
    public bool precalentarAlInicio = true;
    [Tooltip("Colecciones de variantes de shader para compilar al inicio (opcional).")]
    public ShaderVariantCollection[] coleccionesDeShaders;

    [Header("Estado (solo lectura)")]
    [SerializeField] int renderersVigilados;
    [SerializeField] int renderersCortados;
    [SerializeField] float msPrecalentado;

    // --- culling ---
    readonly List<MeshRenderer> renderers = new List<MeshRenderer>();
    readonly List<int> bandaDeCorte = new List<int>();
    readonly HashSet<MeshRenderer> registrados = new HashSet<MeshRenderer>();
    BoundingSphere[] esferas = new BoundingSphere[0];
    float[] distancias;
    CullingGroup grupo;
    Camera camaraGrupo;
    int cursorActualizacion;
    float proximoEscaneo;
    bool iniciado;

    // Radios de cada banda: 0.25, 0.5, 1, 2 ... hasta radioMaximo. Cada renderer va a la primera banda que lo contiene.
    const float RadioPrimeraBanda = 0.25f;

    // --- lo que se restaura al desactivar ---
    Volume volumen;
    VolumeProfile perfil;
    bool srpBatcherAntes;
    AnisotropicFiltering anisotropicoAntes;
    readonly Dictionary<Terrain, float> basemapAntes = new Dictionary<Terrain, float>();
    readonly Dictionary<Terrain, bool> instanciadoAntes = new Dictionary<Terrain, bool>();
    readonly Dictionary<Terrain, float> sesgoArbolesAntes = new Dictionary<Terrain, float>();

    // --- Tenkoku por reflexion (como MareaController): sigue compilando si se saca Tenkoku ---
    Object tenkoku;
    System.Reflection.FieldInfo campoFpsSonda, campoHora, campoMinuto;
    float fpsSondaOriginal = -1f;
    int horaCongelada = int.MinValue;
    const float FpsSondaCongelada = 0.0001f;

    void OnEnable()
    {
        srpBatcherAntes = GraphicsSettings.useScriptableRenderPipelineBatching;
        if (srpBatcher) GraphicsSettings.useScriptableRenderPipelineBatching = true;

        anisotropicoAntes = QualitySettings.anisotropicFiltering;
        if (anisotropicoPorTextura && QualitySettings.anisotropicFiltering == AnisotropicFiltering.ForceEnable)
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;

        AjustarTerrenos();
        if (limitarPostproceso) CrearVolumenDeCosto();

        // Si se vuelve a encender en Play, el culling se rearma en el proximo Update (Start no se repite).
        proximoEscaneo = 0f;
    }

    IEnumerator Start()
    {
        // Un frame de espera: la pasarela y los minijuegos arman sus mallas en Awake/Start.
        yield return null;

        if (precalentarAlInicio) Precalentar();
        if (cullingPorTamano) Escanear();
        proximoEscaneo = Time.unscaledTime + reescanearCada;
        iniciado = true;
    }

    void Update()
    {
        if (!iniciado) return;

        if (cullingPorTamano)
        {
            if (reescanearCada > 0f && Time.unscaledTime >= proximoEscaneo)
            {
                proximoEscaneo = Time.unscaledTime + reescanearCada;
                Escanear();
            }
            ActualizarCulling();
        }

        if (congelarSondaDelCielo) ControlarSondaDelCielo();
    }

    void OnDisable()
    {
        GraphicsSettings.useScriptableRenderPipelineBatching = srpBatcherAntes;
        QualitySettings.anisotropicFiltering = anisotropicoAntes;

        foreach (var par in basemapAntes) if (par.Key) par.Key.basemapDistance = par.Value;
        foreach (var par in instanciadoAntes) if (par.Key) par.Key.drawInstanced = par.Value;
        basemapAntes.Clear();
        instanciadoAntes.Clear();

        foreach (var par in sesgoArbolesAntes) if (par.Key) par.Key.treeLODBiasMultiplier = par.Value;
        sesgoArbolesAntes.Clear();

        if (volumen) Destroy(volumen.gameObject);
        if (perfil) Destroy(perfil);

        LiberarCulling();

        if (tenkoku && campoFpsSonda != null && fpsSondaOriginal >= 0f)
            campoFpsSonda.SetValue(tenkoku, fpsSondaOriginal);
        tenkoku = null;
        fpsSondaOriginal = -1f;
        horaCongelada = int.MinValue;
    }

    // ------------------------------------------------------------------ 1. culling

    void Escanear()
    {
        int capasFuera = capasExcluidas.value;
        bool hayNuevos = false;

        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (registrados.Contains(r)) continue;
            if (((1 << r.gameObject.layer) & capasFuera) != 0) continue;
            if (r.GetComponentInParent<LODGroup>() != null) continue;   // ya tiene su propio corte

            float radio = r.bounds.extents.magnitude;
            if (radio <= 0f || radio > radioMaximo) continue;

            int banda = 0;
            for (float rb = RadioPrimeraBanda; rb < radio; rb *= 2f) banda++;

            registrados.Add(r);
            renderers.Add(r);
            bandaDeCorte.Add(banda);
            hayNuevos = true;
        }

        // Los destruidos se quitan solo al reescanear, para no mover indices entre eventos.
        for (int i = renderers.Count - 1; i >= 0; i--)
        {
            if (renderers[i] != null) continue;
            renderers.RemoveAt(i);
            bandaDeCorte.RemoveAt(i);
            hayNuevos = true;
        }
        registrados.RemoveWhere(r => r == null);

        if (hayNuevos || grupo == null) ArmarGrupo();
    }

    void ArmarGrupo()
    {
        var cam = Camera.main;
        if (cam == null) return;

        if (grupo == null)
        {
            grupo = new CullingGroup();
            grupo.onStateChanged = AlCambiarEstado;
        }

        // Distancia a la que un objeto del radio maximo de cada banda ocupa tamanoMinimoEnPantalla del alto.
        int bandas = 0;
        for (float rb = RadioPrimeraBanda; rb < radioMaximo * 2f; rb *= 2f) bandas++;
        distancias = new float[bandas];
        float factor = 1f / (tamanoMinimoEnPantalla * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
        float radioBanda = RadioPrimeraBanda;
        for (int b = 0; b < bandas; b++, radioBanda *= 2f) distancias[b] = radioBanda * factor;

        if (esferas.Length < renderers.Count) esferas = new BoundingSphere[Mathf.NextPowerOfTwo(renderers.Count)];
        for (int i = 0; i < renderers.Count; i++)
        {
            var b = renderers[i] != null ? renderers[i].bounds : new Bounds();
            esferas[i] = new BoundingSphere(b.center, b.extents.magnitude);
        }

        camaraGrupo = cam;
        grupo.targetCamera = cam;
        grupo.SetDistanceReferencePoint(cam.transform);
        grupo.SetBoundingDistances(distancias);
        grupo.SetBoundingSpheres(esferas);
        grupo.SetBoundingSphereCount(renderers.Count);
        renderersVigilados = renderers.Count;

        // El grupo recien avisa cambios desde el proximo frame; el estado inicial se aplica a mano.
        StartCoroutine(AplicarEstadoInicial());
    }

    IEnumerator AplicarEstadoInicial()
    {
        yield return null;
        if (grupo == null) yield break;
        for (int i = 0; i < renderers.Count; i++) Aplicar(i, grupo.GetDistance(i));
    }

    void AlCambiarEstado(CullingGroupEvent e)
    {
        if (e.index < renderers.Count) Aplicar(e.index, e.currentDistance);
    }

    // banda de distancia b = entre distancias[b-1] y distancias[b]. Un renderer se ve mientras b <= su banda.
    void Aplicar(int i, int bandaDistancia)
    {
        var r = renderers[i];
        if (r == null) return;
        bool cortar = bandaDistancia > bandaDeCorte[i];
        if (r.forceRenderingOff == cortar) return;
        r.forceRenderingOff = cortar;
        renderersCortados += cortar ? 1 : -1;
    }

    void ActualizarCulling()
    {
        if (grupo == null) return;

        // Si cambia la camara (minijuegos), el grupo la sigue.
        var cam = Camera.main;
        if (cam != null && cam != camaraGrupo)
        {
            camaraGrupo = cam;
            grupo.targetCamera = cam;
            grupo.SetDistanceReferencePoint(cam.transform);
        }

        // Posiciones al dia de a poco, para lo que se mueve.
        int n = renderers.Count;
        if (n == 0) return;
        int cuantos = Mathf.Min(actualizacionesPorFrame, n);
        for (int k = 0; k < cuantos; k++)
        {
            cursorActualizacion = (cursorActualizacion + 1) % n;
            var r = renderers[cursorActualizacion];
            if (r == null) continue;
            var b = r.bounds;
            esferas[cursorActualizacion].position = b.center;
            esferas[cursorActualizacion].radius = b.extents.magnitude;
        }
    }

    void LiberarCulling()
    {
        foreach (var r in renderers) if (r != null) r.forceRenderingOff = false;
        renderers.Clear();
        bandaDeCorte.Clear();
        registrados.Clear();
        renderersCortados = 0;
        renderersVigilados = 0;
        if (grupo != null)
        {
            grupo.Dispose();
            grupo = null;
        }
    }

    // ------------------------------------------------------------------ 2 y terreno

    void AjustarTerrenos()
    {
        foreach (var t in Terrain.activeTerrains)
        {
            if (terrenoInstanciado && !t.drawInstanced)
            {
                instanciadoAntes[t] = t.drawInstanced;
                t.drawInstanced = true;
            }
            if (distanciaBasemapMaxima > 0f && t.basemapDistance > distanciaBasemapMaxima)
            {
                basemapAntes[t] = t.basemapDistance;
                t.basemapDistance = distanciaBasemapMaxima;
            }
            // Con ArbolesVisibles (Sur) el Terrain ya no dibuja arboles y ese componente maneja el multiplicador.
            if (sesgoLodArboles > 0f && sesgoLodArboles < 1f && t.GetComponent<ArbolesVisibles>() == null)
            {
                sesgoArbolesAntes[t] = t.treeLODBiasMultiplier;
                t.treeLODBiasMultiplier *= sesgoLodArboles;
            }
        }
    }

    // ------------------------------------------------------------------ 3. postproceso

    void CrearVolumenDeCosto()
    {
        perfil = ScriptableObject.CreateInstance<VolumeProfile>();
        perfil.name = "Rendimiento (runtime)";

        // Add<T>(false): todos los parametros quedan sin override, asi el look sigue saliendo de Volumen_<zona>.
        var bloom = perfil.Add<Bloom>(false);
        if (bloomSinAltaCalidad) bloom.highQualityFiltering.Override(false);
        if (bloomCuartoDeResolucion) bloom.downscale.Override(BloomDownscaleMode.Quarter);

        var go = new GameObject("Volumen_Rendimiento");
        go.transform.SetParent(transform, false);
        volumen = go.AddComponent<Volume>();
        volumen.isGlobal = true;
        volumen.priority = 1000f;
        volumen.sharedProfile = perfil;
    }

    // ------------------------------------------------------------------ 4. sonda de Tenkoku

    void ControlarSondaDelCielo()
    {
        if (tenkoku == null)
        {
            var tipo = System.Type.GetType("Tenkoku.Core.TenkokuModule, Assembly-CSharp");
            if (tipo == null) { congelarSondaDelCielo = false; return; }
            tenkoku = FindFirstObjectByType(tipo);
            if (tenkoku == null) return;
            campoFpsSonda = tipo.GetField("reflectionProbeFPS");
            campoHora = tipo.GetField("currentHour");
            campoMinuto = tipo.GetField("currentMinute");
            if (campoFpsSonda == null || campoHora == null || campoMinuto == null) { congelarSondaDelCielo = false; return; }
            fpsSondaOriginal = (float)campoFpsSonda.GetValue(tenkoku);
        }

        if (Time.timeSinceLevelLoad < segundosDeArranque) return;

        int hora = (int)campoHora.GetValue(tenkoku) * 60 + (int)campoMinuto.GetValue(tenkoku);
        if (hora == horaCongelada) return;

        if (horaCongelada == int.MinValue)
        {
            // Primera vez: la sonda ya se renderizo durante el arranque. Se congela.
            campoFpsSonda.SetValue(tenkoku, FpsSondaCongelada);
        }
        else
        {
            // La hora se movio: Tenkoku acumula el tiempo desde la ultima vez, asi que con el valor original
            // renderiza en el proximo frame. Se vuelve a congelar en el siguiente cambio de minuto o enseguida
            // si la hora se queda quieta.
            campoFpsSonda.SetValue(tenkoku, fpsSondaOriginal);
            StartCoroutine(RecongelarSonda());
        }
        horaCongelada = hora;
    }

    IEnumerator RecongelarSonda()
    {
        // Tenkoku necesita un par de frames para renderizar las 6 caras.
        for (int i = 0; i < 3; i++) yield return null;
        if (tenkoku && campoFpsSonda != null) campoFpsSonda.SetValue(tenkoku, FpsSondaCongelada);
    }

    // ------------------------------------------------------------------ 5. carga al inicio

    void Precalentar()
    {
        var reloj = System.Diagnostics.Stopwatch.StartNew();

        if (coleccionesDeShaders != null)
            foreach (var c in coleccionesDeShaders) if (c != null && !c.isWarmedUp) c.WarmUp();

        var principal = Camera.main;
        if (principal != null)
        {
            var rt = RenderTexture.GetTemporary(256, 256, 24, RenderTextureFormat.DefaultHDR);
            var go = new GameObject("Precalentado") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(principal);
            cam.enabled = false;
            cam.fieldOfView = 90f;
            cam.aspect = 1f;
            cam.targetTexture = rt;
            go.transform.position = principal.transform.position;

            var direcciones = new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right, Vector3.up, Vector3.down };
            foreach (var d in direcciones)
            {
                go.transform.rotation = Quaternion.LookRotation(d, d == Vector3.up || d == Vector3.down ? Vector3.forward : Vector3.up);
                cam.Render();
            }

            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            Destroy(go);
        }

        // Basura de la carga ahora, con la pantalla en negro, y no en medio del juego.
        System.GC.Collect();

        msPrecalentado = (float)reloj.Elapsed.TotalMilliseconds;
    }
}
