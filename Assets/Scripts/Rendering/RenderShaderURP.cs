using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Pase de pantalla para URP que ajusta brillo y color del paisaje: escalonado y
/// variacion de brillo, correccion de color por matiz y perspectiva aerea por
/// profundidad.
/// Corre antes del post-proceso, así que el tonemapping y el color grading
/// siguen aplicándose encima.
/// Se agrega al Renderer de URP (PC_Renderer) como Renderer Feature. Los valores
/// no viven aca sino en el perfil del Global Volume de cada zona, con los overrides
/// <see cref="AjusteDeBrillo"/>, <see cref="CorreccionDeColorPorMatiz"/> y
/// <see cref="PerspectivaAerea"/>.
/// </summary>
public class RenderShaderURP : ScriptableRendererFeature
{
    [System.Serializable]
    public class Ajustes
    {
        public Shader shader;
        [Tooltip("Hidden/Terrenos/MascaraPersonajes. Sin el, los personajes reciben el pase completo.")]
        public Shader shaderMascara;
        [Tooltip("Capa de render de los personajes y animales. A ellos no se les aplica el " +
                 "escalonado de luz ni la correccion por matiz (calibrados para el paisaje); " +
                 "si la variacion de brillo y la perspectiva aerea. Nada = sin mascara.")]
        public RenderingLayerMask capaPersonajes = 0;
        public RenderPassEvent evento = RenderPassEvent.BeforeRenderingPostProcessing;
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
        private static readonly int Escalonado = Shader.PropertyToID("_Escalonado");
        private static readonly int Variacion = Shader.PropertyToID("_Variacion");
        private static readonly int BandaVegetacion = Shader.PropertyToID("_BandaVegetacion");
        private static readonly int VentanaVegetacion = Shader.PropertyToID("_VentanaVegetacion");
        private static readonly int BandaSuelo = Shader.PropertyToID("_BandaSuelo");
        private static readonly int VentanaSuelo = Shader.PropertyToID("_VentanaSuelo");
        private static readonly int BandaCielo = Shader.PropertyToID("_BandaCielo");
        private static readonly int VentanaCielo = Shader.PropertyToID("_VentanaCielo");
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

            // Los valores salen del perfil del Global Volume de la zona. Sin override,
            // cada componente queda en su valor por defecto, que no hace nada.
            var stack = VolumeManager.instance.stack;
            if (stack == null) return;
            var brillo = stack.GetComponent<AjusteDeBrillo>();
            var matiz = stack.GetComponent<CorreccionDeColorPorMatiz>();
            var aire = stack.GetComponent<PerspectivaAerea>();

            // Si nada esta activo el pase no aporta nada y solo costaria dos blits
            // de pantalla completa.
            if (!brillo.IsActive() && !matiz.IsActive() && !aire.IsActive()) return;

            Vector2 distancia = aire.distancia.value;
            var paramsAire = new Vector4(distancia.x, distancia.y, aire.tinte.value, aire.desaturacion.value);
            material.SetColor(AireColor, aire.color.value);
            material.SetVector(AireParams, paramsAire);
            material.SetVector(Escalonado, new Vector4(brillo.nivelesDeLuz.value, brillo.suavidadEntreNiveles.value,
                                                       brillo.intensidadEscalonado.value, 0f));
            material.SetVector(Variacion, new Vector4(brillo.frecuenciaVariacion.value, brillo.intensidadVariacion.value, 0f, 0f));
            material.SetVector(BandaVegetacion, new Vector4(matiz.saturacionVegetacion.value, matiz.matizVegetacion.value,
                                                            matiz.correccionMatizVegetacion.value, matiz.brilloVegetacion.value));
            material.SetVector(VentanaVegetacion, CorreccionDeColorPorMatiz.Ventana(matiz.rangoVegetacion, matiz.suavizadoVegetacion));
            material.SetVector(BandaSuelo, new Vector4(matiz.saturacionSuelo.value, matiz.matizSuelo.value,
                                                       matiz.correccionMatizSuelo.value, matiz.brilloSuelo.value));
            material.SetVector(VentanaSuelo, CorreccionDeColorPorMatiz.Ventana(matiz.rangoSuelo, matiz.suavizadoSuelo));
            material.SetVector(BandaCielo, new Vector4(matiz.saturacionCielo.value, matiz.matizCielo.value,
                                                       matiz.correccionMatizCielo.value, matiz.brilloCielo.value));
            material.SetVector(VentanaCielo, CorreccionDeColorPorMatiz.Ventana(matiz.rangoCielo, matiz.suavizadoCielo));

            // El agua es transparente y no escribe en la textura de profundidad, asi
            // que este pase la toma por cielo y la saltea. Se publican los parametros
            // como globales para que el shader del agua aplique la misma perspectiva
            // aerea por su cuenta y las dos superficies se desvanezcan igual.
            Shader.SetGlobalColor(AireColorGlobal, aire.color.value);
            Shader.SetGlobalVector(AireParamsGlobal, paramsAire);

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
