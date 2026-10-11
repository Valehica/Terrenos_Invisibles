using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Pase de estilo general tipo Ghibli para URP: cuantización suave de la luz,
/// pigmento anclado al mundo y perspectiva aérea por profundidad.
/// Corre antes del post-proceso, así que el tonemapping y el color grading
/// siguen aplicándose encima.
/// Se agrega al Renderer de URP (PC_Renderer) como Renderer Feature.
/// </summary>
public class RenderShaderURP : ScriptableRendererFeature
{
    [System.Serializable]
    public class Ajustes
    {
        public Shader shader;
        [Tooltip("Hidden/Terrenos/MascaraPersonajes. Sin el, los personajes reciben el pase completo.")]
        public Shader shaderMascara;
        [Tooltip("Capa de render de los personajes y animales. A ellos no se les aplica la " +
                 "cuantizacion de luz ni las bandas de color (calibradas para el paisaje); " +
                 "si el pigmento y la perspectiva aerea. Nada = sin mascara.")]
        public RenderingLayerMask capaPersonajes = 0;
        public RenderPassEvent evento = RenderPassEvent.BeforeRenderingPostProcessing;

        [Header("Cuantizacion de luz")]
        [Tooltip("En cuántos valores se pinta la luz. Pocos = más ilustrado.")]
        [Range(2f, 16f)] public float pasosDeLuz = 6f;
        [Tooltip("Qué tan blando es el borde entre valores. 0 = escalón duro (toon).")]
        [Range(0.01f, 0.5f)] public float suavidadBorde = 0.22f;
        [Tooltip("0 apaga la cuantización.")]
        [Range(0f, 1f)] public float fuerzaPintura = 0.45f;

        [Header("Pigmento")]
        [Tooltip("Tamaño de las manchas, en metros de mundo.")]
        [Range(0.005f, 0.5f)] public float escalaPigmento = 0.06f;
        [Tooltip("0 apaga el pigmento.")]
        [Range(0f, 0.3f)] public float fuerzaPigmento = 0.07f;

        [Header("Vegetacion  saturacion por matiz)")]
        [Tooltip("Multiplica la saturacion del verde. 1 = sin cambio. URP ignora el tinte " +
                 "de las capas de terreno, asi que el pasto solo se corrige desde aca.")]
        [Range(0.2f, 3f)] public float saturacionVegetacion = 1.6f;
        [Tooltip("Multiplica el brillo de la vegetacion. 1 = sin cambio.")]
        [Range(0.5f, 2f)] public float brilloVegetacion = 1f;
        [Tooltip("Matiz al que se corre el verde, en grados. 60 = verde amarillento, " +
                 "130 = verde azulado de selva fria.")]
        [Range(25f, 170f)] public float matizVegetacion = 60f;
        [Tooltip("Cuanto se corre hacia ese matiz. 0 = solo satura, no corrige el tono.")]
        [Range(0f, 1f)] public float correccionMatiz = 0.45f;
        [Tooltip("Ventana de matiz afectada (grados): sube de x a y, baja de z a w.")]
        public Vector4 rangoMatiz = new Vector4(48f, 70f, 150f, 185f);

        [Header("Arena  saturacion por matiz")]
        [Tooltip("Multiplica la saturacion de la arena. 1 = sin cambio.")]
        [Range(0.2f, 3f)] public float saturacionArena = 1.8f;
        [Tooltip("Multiplica el brillo de la arena/suelo. 1 = sin cambio.")]
        [Range(0.5f, 2f)] public float brilloArena = 1f;
        [Tooltip("Matiz al que se corre la arena, en grados. 38 = arena dorada.")]
        [Range(15f, 60f)] public float matizArena = 38f;
        [Range(0f, 1f)] public float correccionMatizArena = 0.5f;
        [Tooltip("Ventana de matiz de la arena. Termina antes de donde empieza la del pasto.")]
        public Vector4 rangoMatizArena = new Vector4(12f, 26f, 44f, 52f);

        [Header("Cielo y agua  saturacion por matiz")]
        [Tooltip("Multiplica la saturacion de los azules. El cielo del altiplano y el " +
                 "fiordo de Sur son mucho mas intensos de lo que rinde el cielo dinamico.")]
        [Range(0.2f, 3f)] public float saturacionCielo = 1f;
        [Tooltip("Multiplica el brillo de los azules. 1 = sin cambio.")]
        [Range(0.5f, 2f)] public float brilloCielo = 1f;
        [Tooltip("Matiz al que se corren los azules, en grados. 205 = azul de mar.")]
        [Range(160f, 260f)] public float matizCielo = 205f;
        [Range(0f, 1f)] public float correccionMatizCielo = 0.4f;
        [Tooltip("Ventana de matiz de los azules. Empieza despues de donde termina la del pasto.")]
        public Vector4 rangoMatizCielo = new Vector4(165f, 190f, 235f, 260f);

        [Header("Perspectiva aerea")]
        public Color colorDelAire = new Color(0.72f, 0.80f, 0.86f);
        [Tooltip("Distancia a la que empieza a palidecer.")]
        public float distanciaInicio = 60f;
        [Tooltip("Distancia a la que el efecto llega a pleno.")]
        public float distanciaPlena = 600f;
        [Range(0f, 1f)] public float tinteAire = 0.55f;
        [Range(0f, 1f)] public float desaturacionAire = 0.45f;
    }

    public Ajustes ajustes = new Ajustes();

    private Material material;
    private Material materialMascara;
    private EstiloPass pass;

    public override void Create()
    {
        pass = new EstiloPass { renderPassEvent = ajustes.evento };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (ajustes.shader == null) return;

        var type = renderingData.cameraData.cameraType;
        if (type != CameraType.Game && type != CameraType.SceneView) return;

        if (material == null) material = CoreUtils.CreateEngineMaterial(ajustes.shader);
        if (materialMascara == null && ajustes.shaderMascara != null)
            materialMascara = CoreUtils.CreateEngineMaterial(ajustes.shaderMascara);

        pass.Setup(material, materialMascara, ajustes);
        pass.ConfigureInput(ScriptableRenderPassInput.Depth);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(material);
        CoreUtils.Destroy(materialMascara);
    }

    private class EstiloPass : ScriptableRenderPass
    {
        private static readonly int AireColor = Shader.PropertyToID("_AireColor");
        private static readonly int AireParams = Shader.PropertyToID("_AireParams");
        private static readonly int PinturaParams = Shader.PropertyToID("_PinturaParams");
        private static readonly int PigmentoParams = Shader.PropertyToID("_PigmentoParams");
        private static readonly int VegeParams = Shader.PropertyToID("_VegeParams");
        private static readonly int VegeRango = Shader.PropertyToID("_VegeRango");
        private static readonly int ArenaParams = Shader.PropertyToID("_ArenaParams");
        private static readonly int ArenaRango = Shader.PropertyToID("_ArenaRango");
        private static readonly int CieloParams = Shader.PropertyToID("_CieloParams");
        private static readonly int CieloRango = Shader.PropertyToID("_CieloRango");
        private static readonly int AireColorGlobal = Shader.PropertyToID("_AireColorGlobal");
        private static readonly int AireParamsGlobal = Shader.PropertyToID("_AireParamsGlobal");
        private static readonly int MascaraPersonajes = Shader.PropertyToID("_MascaraPersonajes");
        private static readonly int UsarMascara = Shader.PropertyToID("_UsarMascara");

        private static readonly List<ShaderTagId> PasesOpacos = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit"),
        };

        private Material material;
        private Material materialMascara;
        private Ajustes ajustes;

        private class PassData
        {
            public Material material;
            public TextureHandle source;
            public int pass;
            public TextureHandle mascara;
        }

        private class MascaraData
        {
            public RendererListHandle lista;
        }

        public void Setup(Material mat, Material matMascara, Ajustes a)
        {
            material = mat;
            materialMascara = matMascara;
            ajustes = a;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();

            if (resources.isActiveTargetBackBuffer) return;

            // Cada escena puede pisar estos valores desde el perfil de su Global Volume.
            // Sin override, manda lo que tenga el Feature. Esto existe porque el Feature
            // es global: los valores de la playa de Centro ponian naranja el altiplano
            // de Norte.
            var vol = VolumeManager.instance.stack != null
                    ? VolumeManager.instance.stack.GetComponent<EstiloGhibliVolumen>()
                    : null;

            float pasosDeLuz      = vol != null ? EstiloGhibliVolumen.Elegir(vol.pasosDeLuz, ajustes.pasosDeLuz) : ajustes.pasosDeLuz;
            float suavidadBorde   = vol != null ? EstiloGhibliVolumen.Elegir(vol.suavidadBorde, ajustes.suavidadBorde) : ajustes.suavidadBorde;
            float fuerzaPintura   = vol != null ? EstiloGhibliVolumen.Elegir(vol.fuerzaPintura, ajustes.fuerzaPintura) : ajustes.fuerzaPintura;
            float escalaPigmento  = vol != null ? EstiloGhibliVolumen.Elegir(vol.escalaPigmento, ajustes.escalaPigmento) : ajustes.escalaPigmento;
            float fuerzaPigmento  = vol != null ? EstiloGhibliVolumen.Elegir(vol.fuerzaPigmento, ajustes.fuerzaPigmento) : ajustes.fuerzaPigmento;
            float satVege         = vol != null ? EstiloGhibliVolumen.Elegir(vol.saturacionVegetacion, ajustes.saturacionVegetacion) : ajustes.saturacionVegetacion;
            float brilloVege      = vol != null ? EstiloGhibliVolumen.Elegir(vol.brilloVegetacion, ajustes.brilloVegetacion) : ajustes.brilloVegetacion;
            float matizVege       = vol != null ? EstiloGhibliVolumen.Elegir(vol.matizVegetacion, ajustes.matizVegetacion) : ajustes.matizVegetacion;
            float corrVege        = vol != null ? EstiloGhibliVolumen.Elegir(vol.correccionMatiz, ajustes.correccionMatiz) : ajustes.correccionMatiz;
            Vector4 rangoVege     = vol != null ? EstiloGhibliVolumen.Elegir(vol.rangoMatiz, ajustes.rangoMatiz) : ajustes.rangoMatiz;
            float satArena        = vol != null ? EstiloGhibliVolumen.Elegir(vol.saturacionArena, ajustes.saturacionArena) : ajustes.saturacionArena;
            float brilloArena     = vol != null ? EstiloGhibliVolumen.Elegir(vol.brilloArena, ajustes.brilloArena) : ajustes.brilloArena;
            float matizArena      = vol != null ? EstiloGhibliVolumen.Elegir(vol.matizArena, ajustes.matizArena) : ajustes.matizArena;
            float corrArena       = vol != null ? EstiloGhibliVolumen.Elegir(vol.correccionMatizArena, ajustes.correccionMatizArena) : ajustes.correccionMatizArena;
            Vector4 rangoArena    = vol != null ? EstiloGhibliVolumen.Elegir(vol.rangoMatizArena, ajustes.rangoMatizArena) : ajustes.rangoMatizArena;
            float satCielo        = vol != null ? EstiloGhibliVolumen.Elegir(vol.saturacionCielo, ajustes.saturacionCielo) : ajustes.saturacionCielo;
            float brilloCielo     = vol != null ? EstiloGhibliVolumen.Elegir(vol.brilloCielo, ajustes.brilloCielo) : ajustes.brilloCielo;
            float matizCielo      = vol != null ? EstiloGhibliVolumen.Elegir(vol.matizCielo, ajustes.matizCielo) : ajustes.matizCielo;
            float corrCielo       = vol != null ? EstiloGhibliVolumen.Elegir(vol.correccionMatizCielo, ajustes.correccionMatizCielo) : ajustes.correccionMatizCielo;
            Vector4 rangoCielo    = vol != null ? EstiloGhibliVolumen.Elegir(vol.rangoMatizCielo, ajustes.rangoMatizCielo) : ajustes.rangoMatizCielo;
            Color colorAire       = vol != null ? EstiloGhibliVolumen.Elegir(vol.colorDelAire, ajustes.colorDelAire) : ajustes.colorDelAire;
            float distInicio      = vol != null ? EstiloGhibliVolumen.Elegir(vol.distanciaInicio, ajustes.distanciaInicio) : ajustes.distanciaInicio;
            float distPlena       = vol != null ? EstiloGhibliVolumen.Elegir(vol.distanciaPlena, ajustes.distanciaPlena) : ajustes.distanciaPlena;
            float tinteAire       = vol != null ? EstiloGhibliVolumen.Elegir(vol.tinteAire, ajustes.tinteAire) : ajustes.tinteAire;
            float desatAire       = vol != null ? EstiloGhibliVolumen.Elegir(vol.desaturacionAire, ajustes.desaturacionAire) : ajustes.desaturacionAire;

            // Si todo quedo en cero el pase no aporta nada y solo costaria dos blits
            // de pantalla completa.
            bool algoQueHacer = fuerzaPintura > 0f || fuerzaPigmento > 0f
                             || tinteAire > 0f || desatAire > 0f
                             || satVege != 1f || corrVege > 0f || brilloVege != 1f
                             || satArena != 1f || corrArena > 0f || brilloArena != 1f
                             || satCielo != 1f || brilloCielo != 1f;
            if (!algoQueHacer) return;

            material.SetColor(AireColor, colorAire);
            material.SetVector(AireParams, new Vector4(distInicio, distPlena, tinteAire, desatAire));
            material.SetVector(PinturaParams, new Vector4(pasosDeLuz, suavidadBorde, fuerzaPintura, 0f));
            material.SetVector(PigmentoParams, new Vector4(escalaPigmento, fuerzaPigmento, 0f, 0f));
            material.SetVector(VegeParams, new Vector4(satVege, matizVege, corrVege, brilloVege));
            material.SetVector(VegeRango, rangoVege);
            material.SetVector(ArenaParams, new Vector4(satArena, matizArena, corrArena, brilloArena));
            material.SetVector(ArenaRango, rangoArena);
            material.SetVector(CieloParams, new Vector4(satCielo, matizCielo, corrCielo, brilloCielo));
            material.SetVector(CieloRango, rangoCielo);

            // El agua es transparente y no escribe en la textura de profundidad, asi
            // que este pase la toma por cielo y la saltea. Se publican los parametros
            // como globales para que el shader del agua aplique la misma perspectiva
            // aerea por su cuenta y las dos superficies se desvanezcan igual.
            Shader.SetGlobalColor(AireColorGlobal, colorAire);
            Shader.SetGlobalVector(AireParamsGlobal, new Vector4(distInicio, distPlena, tinteAire, desatAire));

            var desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;

            TextureHandle color = resources.activeColorTexture;
            // No se puede leer y escribir la misma textura en un mismo pase, así que
            // el efecto va a un intermedio y una copia lo devuelve al color de cámara.
            TextureHandle temp = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_RenderShaderURP", false);

            TextureHandle mascara = AddMascara(renderGraph, frameData, desc);
            material.SetFloat(UsarMascara, mascara.IsValid() ? 1f : 0f);

            AddPass(renderGraph, "RenderShaderURP", color, temp, 0, AccessFlags.Write, mascara);
            AddPass(renderGraph, "RenderShaderURP: copia", temp, color, 1, AccessFlags.Write, TextureHandle.nullHandle);
        }

        // Pinta en una R8 los renderers de la capa de personajes que no esten tapados.
        private TextureHandle AddMascara(RenderGraph renderGraph, ContextContainer frameData, RenderTextureDescriptor desc)
        {
            uint capa = ajustes.capaPersonajes;
            if (capa == 0 || materialMascara == null) return TextureHandle.nullHandle;

            var resources = frameData.Get<UniversalResourceData>();
            var renderingData = frameData.Get<UniversalRenderingData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var lightData = frameData.Get<UniversalLightData>();
            if (!resources.cameraDepthTexture.IsValid()) return TextureHandle.nullHandle;

            var dibujo = RenderingUtils.CreateDrawingSettings(PasesOpacos, renderingData, cameraData, lightData,
                                                              SortingCriteria.CommonOpaque);
            dibujo.overrideMaterial = materialMascara;
            dibujo.overrideMaterialPassIndex = 0;
            var filtro = new FilteringSettings(RenderQueueRange.opaque) { renderingLayerMask = capa };

            desc.graphicsFormat = GraphicsFormat.R8_UNorm;
            TextureHandle mascara = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_MascaraPersonajes", true);

            using (var builder = renderGraph.AddRasterRenderPass<MascaraData>("RenderShaderURP: mascara personajes", out var data))
            {
                data.lista = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, dibujo, filtro));
                builder.UseRendererList(data.lista);
                builder.UseTexture(resources.cameraDepthTexture);
                builder.UseAllGlobalTextures(true);
                builder.SetRenderAttachment(mascara, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((MascaraData d, RasterGraphContext ctx) =>
                {
                    ctx.cmd.ClearRenderTarget(false, true, Color.clear);
                    ctx.cmd.DrawRendererList(d.lista);
                });
            }
            return mascara;
        }

        private void AddPass(RenderGraph renderGraph, string name, TextureHandle source, TextureHandle target,
                             int shaderPass, AccessFlags targetAccess, TextureHandle mascara)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(name, out var data))
            {
                data.material = material;
                data.source = source;
                data.pass = shaderPass;

                builder.UseTexture(source);
                data.mascara = mascara;
                if (mascara.IsValid()) builder.UseTexture(mascara);
                builder.UseAllGlobalTextures(true);
                builder.SetRenderAttachment(target, 0, targetAccess);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                {
                    if (d.mascara.IsValid()) d.material.SetTexture(MascaraPersonajes, d.mascara);
                    Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1f, 1f, 0f, 0f), d.material, d.pass);
                });
            }
        }
    }
}
