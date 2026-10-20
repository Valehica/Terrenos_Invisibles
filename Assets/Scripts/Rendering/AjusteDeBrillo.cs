using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Ajustes de brillo del pase de pantalla (<see cref="RenderShaderURP"/>) que no tocan
/// el color: agrupar la luz de los medios tonos en niveles y variarla con manchas
/// fijas al mundo.
///
/// Se agrega al perfil del Global Volume de cada zona (Add Override).
/// Con los valores por defecto no hace nada.
/// </summary>
[Serializable]
[VolumeComponentMenu("Terrenos/Ajuste de brillo")]
[SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
public class AjusteDeBrillo : VolumeComponent, IPostProcessComponent
{
    [Header("Escalonado de luz")]
    [Tooltip("Cuanto se agrupa el brillo de los medios tonos en niveles. 0 = apagado. " +
             "Las sombras profundas y las luces quemadas quedan siempre continuas.")]
    public ClampedFloatParameter intensidadEscalonado = new ClampedFloatParameter(0f, 0f, 1f);
    [Tooltip("En cuantos niveles se reparte el brillo. Pocos = mas ilustrado.")]
    public ClampedFloatParameter nivelesDeLuz = new ClampedFloatParameter(6f, 2f, 16f);
    [Tooltip("Que tan blando es el paso de un nivel al siguiente. Bajo = escalon duro.")]
    public ClampedFloatParameter suavidadEntreNiveles = new ClampedFloatParameter(0.22f, 0.01f, 0.5f);

    [Header("Variacion de brillo")]
    [Tooltip("Cuanto sube y baja el brillo con manchas lentas ancladas al mundo. 0 = apagado.")]
    public ClampedFloatParameter intensidadVariacion = new ClampedFloatParameter(0f, 0f, 0.3f);
    [Tooltip("Manchas por metro de mundo. Mas alto = manchas mas chicas.")]
    public ClampedFloatParameter frecuenciaVariacion = new ClampedFloatParameter(0.06f, 0.005f, 0.5f);

    public bool IsActive() => intensidadEscalonado.value > 0f || intensidadVariacion.value > 0f;

    public bool IsTileCompatible() => false;
}
