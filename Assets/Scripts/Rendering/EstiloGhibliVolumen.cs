using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Valores por escena para el pase de estilo (<see cref="RenderShaderURP"/>).
///
/// El Renderer Feature vive en PC_Renderer, que es global: sus valores valen para
/// todas las escenas. Pero cada zona tiene su paleta — la playa de Centro necesita
/// arena dorada y el altiplano de Norte se vuelve naranja con esos mismos valores.
/// Este componente permite que el perfil de volumen de cada escena pise lo que
/// necesite; lo que no se marque queda con el valor del Feature.
///
/// Se agrega al perfil del Global Volume de la escena (Add Override).
/// </summary>
[Serializable]
[VolumeComponentMenu("Terrenos/Estilo Ghibli")]
[SupportedOnRenderPipeline(typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset))]
public class EstiloGhibliVolumen : VolumeComponent, IPostProcessComponent
{
    [Header("Cuantizacion de luz")]
    public ClampedFloatParameter pasosDeLuz = new ClampedFloatParameter(6f, 2f, 16f);
    public ClampedFloatParameter suavidadBorde = new ClampedFloatParameter(0.22f, 0.01f, 0.5f);
    public ClampedFloatParameter fuerzaPintura = new ClampedFloatParameter(0.45f, 0f, 1f);

    [Header("Pigmento")]
    public ClampedFloatParameter escalaPigmento = new ClampedFloatParameter(0.06f, 0.005f, 0.5f);
    public ClampedFloatParameter fuerzaPigmento = new ClampedFloatParameter(0.07f, 0f, 0.3f);

    [Header("Vegetacion")]
    public ClampedFloatParameter saturacionVegetacion = new ClampedFloatParameter(1.4f, 0.2f, 3f);
    public ClampedFloatParameter brilloVegetacion = new ClampedFloatParameter(1f, 0.5f, 2f);
    public ClampedFloatParameter matizVegetacion = new ClampedFloatParameter(60f, 25f, 170f);
    public ClampedFloatParameter correccionMatiz = new ClampedFloatParameter(0.8f, 0f, 1f);
    public Vector4Parameter rangoMatiz = new Vector4Parameter(new Vector4(48f, 70f, 150f, 185f));

    [Header("Arena")]
    public ClampedFloatParameter saturacionArena = new ClampedFloatParameter(1.8f, 0.2f, 3f);
    public ClampedFloatParameter brilloArena = new ClampedFloatParameter(1f, 0.5f, 2f);
    public ClampedFloatParameter matizArena = new ClampedFloatParameter(38f, 15f, 60f);
    public ClampedFloatParameter correccionMatizArena = new ClampedFloatParameter(0.5f, 0f, 1f);
    public Vector4Parameter rangoMatizArena = new Vector4Parameter(new Vector4(12f, 26f, 44f, 52f));

    [Header("Cielo y agua")]
    public ClampedFloatParameter saturacionCielo = new ClampedFloatParameter(1f, 0.2f, 3f);
    public ClampedFloatParameter brilloCielo = new ClampedFloatParameter(1f, 0.5f, 2f);
    public ClampedFloatParameter matizCielo = new ClampedFloatParameter(205f, 160f, 260f);
    public ClampedFloatParameter correccionMatizCielo = new ClampedFloatParameter(0.4f, 0f, 1f);
    public Vector4Parameter rangoMatizCielo = new Vector4Parameter(new Vector4(165f, 190f, 235f, 260f));

    [Header("Perspectiva aerea")]
    public ColorParameter colorDelAire = new ColorParameter(new Color(0.72f, 0.80f, 0.86f), false, false, true);
    public FloatParameter distanciaInicio = new FloatParameter(150f);
    public FloatParameter distanciaPlena = new FloatParameter(2000f);
    public ClampedFloatParameter tinteAire = new ClampedFloatParameter(0.42f, 0f, 1f);
    public ClampedFloatParameter desaturacionAire = new ClampedFloatParameter(0.22f, 0f, 1f);

    /// <summary>
    /// El pase decide si corre mirando sus propios valores, no este metodo, porque
    /// aca solo estan los overrides. Siempre true para no bloquearlo.
    /// </summary>
    public bool IsActive() => true;

    public bool IsTileCompatible() => false;

    /// <summary>Devuelve el override si esta marcado, si no el valor del Feature.</summary>
    public static float Elegir(VolumeParameter<float> p, float porDefecto)
        => (p != null && p.overrideState) ? p.value : porDefecto;

    public static Vector4 Elegir(VolumeParameter<Vector4> p, Vector4 porDefecto)
        => (p != null && p.overrideState) ? p.value : porDefecto;

    public static Color Elegir(VolumeParameter<Color> p, Color porDefecto)
        => (p != null && p.overrideState) ? p.value : porDefecto;
}
