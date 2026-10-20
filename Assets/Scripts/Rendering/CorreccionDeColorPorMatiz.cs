using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Correccion de color selectiva por matiz del pase de pantalla (<see cref="RenderShaderURP"/>).
/// Tres bandas que no se pisan: vegetacion, suelo (arena y tierra) y cielo y agua.
/// Cada una cambia saturacion y brillo, y corre el matiz hacia un objetivo, solo
/// dentro de su rango de matices; el resto de la imagen queda intacto.
///
/// Existe porque el Terrain/Lit de URP ignora el tinte de las capas de terreno: el
/// color del pasto y el suelo solo se puede corregir en pantalla.
/// Las bandas de vegetacion y suelo actuan solo sobre geometria; la de cielo y agua,
/// solo sobre el cielo y el agua.
///
/// Los rangos estan en grados (0-360) y pueden pasarse de los extremos: un rango
/// (2, 40) con entrada 18 empieza en -16, es decir, en 344.
/// Con los valores por defecto no hace nada.
/// </summary>
[Serializable]
[VolumeComponentMenu("Terrenos/Correccion de color por matiz")]
[SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
public class CorreccionDeColorPorMatiz : VolumeComponent, IPostProcessComponent
{
    const float MatizMin = -90f, MatizMax = 450f;

    [Header("Vegetacion")]
    [Tooltip("Multiplica la saturacion. 1 = sin cambio; menos de 1 desatura.")]
    public ClampedFloatParameter saturacionVegetacion = new ClampedFloatParameter(1f, 0.2f, 3f);
    [Tooltip("Multiplica el brillo. 1 = sin cambio.")]
    public ClampedFloatParameter brilloVegetacion = new ClampedFloatParameter(1f, 0.5f, 2f);
    [Tooltip("Matiz hacia el que se corre la banda, en grados. 60 = verde amarillento, " +
             "130 = verde azulado.")]
    public ClampedFloatParameter matizVegetacion = new ClampedFloatParameter(60f, 25f, 170f);
    [Tooltip("Cuanto se corre hacia ese matiz. 0 = no cambia el tono.")]
    public ClampedFloatParameter correccionMatizVegetacion = new ClampedFloatParameter(0f, 0f, 1f);
    [Tooltip("Matices afectados de lleno, en grados.")]
    public FloatRangeParameter rangoVegetacion = new FloatRangeParameter(new Vector2(70f, 150f), MatizMin, MatizMax);
    [Tooltip("Grados de transicion antes (x) y despues (y) del rango.")]
    public Vector2Parameter suavizadoVegetacion = new Vector2Parameter(new Vector2(22f, 35f));

    [Header("Suelo (arena y tierra)")]
    [Tooltip("Multiplica la saturacion. 1 = sin cambio; menos de 1 desatura.")]
    public ClampedFloatParameter saturacionSuelo = new ClampedFloatParameter(1f, 0.2f, 3f);
    [Tooltip("Multiplica el brillo. 1 = sin cambio.")]
    public ClampedFloatParameter brilloSuelo = new ClampedFloatParameter(1f, 0.5f, 2f);
    [Tooltip("Matiz hacia el que se corre la banda, en grados. 38 = arena dorada.")]
    public ClampedFloatParameter matizSuelo = new ClampedFloatParameter(38f, 15f, 60f);
    [Tooltip("Cuanto se corre hacia ese matiz. 0 = no cambia el tono.")]
    public ClampedFloatParameter correccionMatizSuelo = new ClampedFloatParameter(0f, 0f, 1f);
    [Tooltip("Matices afectados de lleno, en grados. Conviene que termine antes de donde " +
             "empieza la vegetacion.")]
    public FloatRangeParameter rangoSuelo = new FloatRangeParameter(new Vector2(26f, 44f), MatizMin, MatizMax);
    [Tooltip("Grados de transicion antes (x) y despues (y) del rango.")]
    public Vector2Parameter suavizadoSuelo = new Vector2Parameter(new Vector2(14f, 8f));

    [Header("Cielo y agua")]
    [Tooltip("Multiplica la saturacion. 1 = sin cambio; menos de 1 desatura.")]
    public ClampedFloatParameter saturacionCielo = new ClampedFloatParameter(1f, 0.2f, 3f);
    [Tooltip("Multiplica el brillo. 1 = sin cambio.")]
    public ClampedFloatParameter brilloCielo = new ClampedFloatParameter(1f, 0.5f, 2f);
    [Tooltip("Matiz hacia el que se corre la banda, en grados. 205 = azul de mar. Al " +
             "saturar, el tonemapping arrastra el azul hacia el cian: apuntar mas alla.")]
    public ClampedFloatParameter matizCielo = new ClampedFloatParameter(205f, 160f, 260f);
    [Tooltip("Cuanto se corre hacia ese matiz. 0 = no cambia el tono.")]
    public ClampedFloatParameter correccionMatizCielo = new ClampedFloatParameter(0f, 0f, 1f);
    [Tooltip("Matices afectados de lleno, en grados. Conviene que empiece despues de " +
             "donde termina la vegetacion.")]
    public FloatRangeParameter rangoCielo = new FloatRangeParameter(new Vector2(190f, 235f), MatizMin, MatizMax);
    [Tooltip("Grados de transicion antes (x) y despues (y) del rango.")]
    public Vector2Parameter suavizadoCielo = new Vector2Parameter(new Vector2(25f, 25f));

    public bool IsActive() =>
        BandaActiva(saturacionVegetacion, brilloVegetacion, correccionMatizVegetacion) ||
        BandaActiva(saturacionSuelo, brilloSuelo, correccionMatizSuelo) ||
        BandaActiva(saturacionCielo, brilloCielo, correccionMatizCielo);

    public bool IsTileCompatible() => false;

    static bool BandaActiva(ClampedFloatParameter saturacion, ClampedFloatParameter brillo,
                            ClampedFloatParameter correccion)
        => saturacion.value != 1f || brillo.value != 1f || correccion.value > 0f;

    /// <summary>
    /// Arma la ventana que espera el shader: (empieza a subir, pleno desde, pleno hasta, termina de bajar).
    /// </summary>
    public static Vector4 Ventana(FloatRangeParameter rango, Vector2Parameter suavizado)
    {
        Vector2 r = rango.value, s = suavizado.value;
        return new Vector4(r.x - s.x, r.x, r.y, r.y + s.y);
    }
}
