using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Rayos de luz (god rays) estilo Breath of the Wild para URP.
/// Toma el cielo brillante alrededor del sol, lo difumina en dirección radial
/// desde el sol y lo suma a la imagen. Donde algo tapa el cielo (el cerro, el
/// pasto, el personaje) quedan sombras y así aparecen los haces.
/// Se agrega al Renderer de URP (PC_Renderer) como Renderer Feature.
/// </summary>
public class RayosDeLuzFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Ajustes
    {
        public Shader shader;
        public RenderPassEvent evento = RenderPassEvent.BeforeRenderingPostProcessing;

        [Tooltip("Resolución del efecto: 2 = mitad, 3 = un tercio. Más alto = más barato.")]
        [Range(1, 4)] public int reduccion = 2;
        [Range(8, 96)] public int muestras = 48;
        [Range(0f, 3f)] public float intensidad = 0.8f;
        [Tooltip("Qué tan largos son los haces.")]
        [Range(0.1f, 1.5f)] public float densidad = 0.85f;
        [Tooltip("Cuánto se apagan a lo largo del haz.")]
        [Range(0.8f, 1f)] public float decaimiento = 0.965f;
        [Range(0.01f, 0.2f)] public float peso = 0.05f;
        [Tooltip("Brillo mínimo del cielo para emitir rayos.")]
        [Range(0f, 1f)] public float umbral = 0.55f;
        [Tooltip("Radio alrededor del sol (en pantalla) que emite rayos.")]
        [Range(0.1f, 1.5f)] public float radioSol = 0.6f;
        public Color tinte = new Color(1f, 0.93f, 0.8f);
    }

    public Ajustes ajustes = new Ajustes();

    private Material material;
    private RayosPass pass;

    public override void Create()
    {
        pass = new RayosPass { renderPassEvent = ajustes.evento };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (ajustes.shader == null) return;

        var type = renderingData.cameraData.cameraType;
        if (type != CameraType.Game && type != CameraType.SceneView) return;

        if (material == null) material = CoreUtils.CreateEngineMaterial(ajustes.shader);

        pass.Setup(material, ajustes);
        pass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Color);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(material);
    }

    private class RayosPass : ScriptableRenderPass
    {
        private static readonly int SunScreenPos = Shader.PropertyToID("_SunScreenPos");
        private static readonly int RayParams = Shader.PropertyToID("_RayParams");
        private static readonly int RayTint = Shader.PropertyToID("_RayTint");
        private static readonly int Threshold = Shader.PropertyToID("_Threshold");
        private static readonly int Intensity = Shader.PropertyToID("_Intensity");
        private static readonly int SunRadius = Shader.PropertyToID("_SunRadius");

        private Material material;
        private Ajustes ajustes;

        private class PassData
        {
            public Material material;
            public TextureHandle source;
            public int pass;
        }

        public void Setup(Material mat, Ajustes a)
        {
            material = mat;
            ajustes = a;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var lightData = frameData.Get<UniversalLightData>();

            if (resources.isActiveTargetBackBuffer) return;
            if (lightData.mainLightIndex < 0) return;

            // Dirección hacia el sol: la inversa del "forward" de la luz principal.
            var sun = lightData.visibleLights[lightData.mainLightIndex];
            Vector3 toSun = -(Vector3)sun.localToWorldMatrix.GetColumn(2);
            Camera cam = cameraData.camera;

            float facing = Vector3.Dot(cam.transform.forward, toSun);
            if (facing <= 0.05f) return;                     // el sol está detrás: no hay rayos

            Vector3 vp = cam.WorldToViewportPoint(cam.transform.position + toSun * 1000f);
            // se apagan cuando el sol se aleja mucho del borde de la pantalla
            float edge = Mathf.Clamp01(1.5f - Mathf.Max(Mathf.Abs(vp.x - 0.5f), Mathf.Abs(vp.y - 0.5f)) * 2f);
            float strength = Mathf.Clamp01(facing * 2f) * edge;
            if (strength <= 0.001f) return;

            material.SetVector(SunScreenPos, new Vector4(vp.x, vp.y, strength, 0f));
            material.SetVector(RayParams, new Vector4(ajustes.muestras, ajustes.densidad, ajustes.decaimiento, ajustes.peso));
            material.SetColor(RayTint, ajustes.tinte);
            material.SetFloat(Threshold, ajustes.umbral);
            material.SetFloat(Intensity, ajustes.intensidad);
            material.SetFloat(SunRadius, ajustes.radioSol);

            var desc = cameraData.cameraTargetDescriptor;
            desc.width = Mathf.Max(1, desc.width / ajustes.reduccion);
            desc.height = Mathf.Max(1, desc.height / ajustes.reduccion);
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;

            TextureHandle color = resources.activeColorTexture;
            TextureHandle mask = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_RayosMascara", true);
            TextureHandle rays = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_RayosRadial", true);

            AddPass(renderGraph, "Rayos de luz: máscara", color, mask, 0, AccessFlags.Write, resources);
            AddPass(renderGraph, "Rayos de luz: radial", mask, rays, 1, AccessFlags.Write, null);
            AddPass(renderGraph, "Rayos de luz: suma", rays, color, 2, AccessFlags.ReadWrite, null);
        }

        private void AddPass(RenderGraph renderGraph, string name, TextureHandle source, TextureHandle target,
                             int shaderPass, AccessFlags targetAccess, UniversalResourceData readDepthFrom)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(name, out var data))
            {
                data.material = material;
                data.source = source;
                data.pass = shaderPass;

                builder.UseTexture(source);
                if (readDepthFrom != null && readDepthFrom.cameraDepthTexture.IsValid())
                    builder.UseTexture(readDepthFrom.cameraDepthTexture);
                builder.UseAllGlobalTextures(true);
                builder.SetRenderAttachment(target, 0, targetAccess);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                    Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1f, 1f, 0f, 0f), d.material, d.pass));
            }
        }
    }
}
