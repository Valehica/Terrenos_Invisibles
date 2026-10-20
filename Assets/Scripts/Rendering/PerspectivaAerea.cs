using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Perspectiva aerea del pase de pantalla (<see cref="RenderShaderURP"/>): con la
/// distancia, la geometria pierde saturacion y se tine del color del aire.
///
/// El agua no escribe profundidad, asi que el pase no la ve; estos mismos valores se
/// publican como globales (_AireColorGlobal, _AireParamsGlobal) y Mar.shader los aplica
/// por su cuenta.
/// Con los valores por defecto no hace nada.
/// </summary>
[Serializable]
[VolumeComponentMenu("Terrenos/Perspectiva aerea")]
[SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
public class PerspectivaAerea : VolumeComponent, IPostProcessComponent
{
    [Tooltip("Cuanto se mezcla lo lejano con el color del aire. 0 = apagado.")]
    public ClampedFloatParameter tinte = new ClampedFloatParameter(0f, 0f, 1f);
    [Tooltip("Cuanta saturacion pierde lo lejano. 0 = apagado.")]
    public ClampedFloatParameter desaturacion = new ClampedFloatParameter(0f, 0f, 1f);
    [Tooltip("Color hacia el que se va lo lejano.")]
    public ColorParameter color = new ColorParameter(new Color(0.72f, 0.80f, 0.86f), false, false, true);
    [Tooltip("Distancia en metros a la que empieza (x) y a la que llega a pleno (y). " +
             "Calibrar contra la extension real de la escena, no contra lo cercano.")]
    public FloatRangeParameter distancia = new FloatRangeParameter(new Vector2(150f, 2000f), 0f, 10000f);

    public bool IsActive() => tinte.value > 0f || desaturacion.value > 0f;

    public bool IsTileCompatible() => false;
}
