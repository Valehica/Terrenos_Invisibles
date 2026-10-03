using UnityEngine;

/// <summary>
/// Altura del mar en un punto, calculada igual que el vertex shader de
/// Terrenos/OceanWaterRealista: cuatro ondas de Gerstner sobre un dominio
/// deformado con ruido, aplanadas en lo bajo. Las rompientes de la costa no se
/// replican (la ruta de la canoa va por agua profunda).
///
/// Sirve para que lo que flota (la canoa) y la cámara que la sigue suban y
/// bajen con la marejada en vez de quedar tapados por las crestas.
/// </summary>
public class OleajeMuestreo
{
    private static readonly int WaveA = Shader.PropertyToID("_WaveA");
    private static readonly int WaveB = Shader.PropertyToID("_WaveB");
    private static readonly int WaveC = Shader.PropertyToID("_WaveC");
    private static readonly int WaveD = Shader.PropertyToID("_WaveD");
    private static readonly int WaveSpeedId = Shader.PropertyToID("_WaveSpeed");
    private static readonly int WarpScaleId = Shader.PropertyToID("_WarpScale");
    private static readonly int WarpStrengthId = Shader.PropertyToID("_WarpStrength");
    private static readonly int ShoreFadeId = Shader.PropertyToID("_WaveShoreFade");

    private readonly Material material;
    private readonly Transform sea;
    private readonly Terrain terrain;

    public OleajeMuestreo(Renderer seaRenderer)
    {
        if (seaRenderer == null) return;
        sea = seaRenderer.transform;
        material = seaRenderer.sharedMaterial;
        if (material != null && !material.HasProperty(WaveA)) material = null;
        terrain = Terrain.activeTerrain;
    }

    public float SeaLevel => sea != null ? sea.position.y : 0f;

    // Parámetros del material leídos una vez por frame: leerlos en cada consulta
    // (la espuma hace cientos) costaba ~2 ms.
    private int cachedFrame = -1;
    private float t, warpScale, warpStrength, shoreFade;
    private Vector4 a, b, c, d;

    private void RefreshCache()
    {
        if (cachedFrame == Time.frameCount) return;
        cachedFrame = Time.frameCount;
        t = Time.timeSinceLevelLoad * material.GetFloat(WaveSpeedId);
        a = material.GetVector(WaveA);
        b = material.GetVector(WaveB);
        c = material.GetVector(WaveC);
        d = material.GetVector(WaveD);
        warpScale = material.GetFloat(WarpScaleId);
        warpStrength = material.GetFloat(WarpStrengthId);
        shoreFade = Mathf.Max(material.GetFloat(ShoreFadeId), 0.01f);
    }

    /// <summary>
    /// Altura de la superficie del agua sobre el punto XZ dado. Con menos
    /// iteraciones es más barato y algo menos exacto donde la ola se inclina
    /// (para la espuma alcanza con una).
    /// </summary>
    public float HeightAt(Vector3 world, int iterations = 3)
    {
        float level = SeaLevel;
        if (material == null) return level;
        RefreshCache();

        float fade = 1f;
        if (terrain != null)
        {
            float depth = level - (terrain.SampleHeight(world) + terrain.transform.position.y);
            fade = Mathf.Clamp01((depth - 0.1f) / shoreFade);
            fade *= fade;
        }
        if (fade <= 0f) return level;

        // Gerstner también desplaza en horizontal: se busca el punto de la malla
        // en reposo que termina encima de "world" (tres iteraciones alcanzan).
        Vector2 target = new Vector2(world.x, world.z);
        Vector2 grid = target;
        Vector3 offset = Vector3.zero;
        for (int i = 0; i < iterations; i++)
        {
            offset = Displacement(grid, t, a, b, c, d, warpScale, warpStrength) * fade;
            grid = target - new Vector2(offset.x, offset.z);
        }
        return level + offset.y;
    }

    private static Vector3 Displacement(Vector2 grid, float t, Vector4 a, Vector4 b, Vector4 c, Vector4 d, float warpScale, float warpStrength)
    {
        Vector2 warpA = new Vector2(ValueNoise(grid * warpScale), ValueNoise(grid * warpScale + new Vector2(37.1f, 37.1f))) - new Vector2(0.5f, 0.5f);
        Vector2 warpB = new Vector2(ValueNoise(grid * warpScale * 4.3f), ValueNoise(grid * warpScale * 4.3f + new Vector2(11.7f, 11.7f))) - new Vector2(0.5f, 0.5f);
        Vector2 p = grid + warpA * warpStrength + warpB * (warpStrength * 0.42f);

        return Gerstner(a, p, t) + Gerstner(b, p, t) + Gerstner(c, p, t) + Gerstner(d, p, t);
    }

    private static Vector3 Gerstner(Vector4 wave, Vector2 p, float t)
    {
        float k = 2f * Mathf.PI / Mathf.Max(wave.w, 0.01f);
        float speed = Mathf.Sqrt(9.8f / k);
        Vector2 dir = new Vector2(wave.x, wave.y).normalized;
        float f = k * (Vector2.Dot(dir, p) - speed * t);
        float amp = wave.z / k;
        float co = Mathf.Cos(f);
        return new Vector3(dir.x * amp * co, amp * Mathf.Sin(f), dir.y * amp * co);
    }

    private static float Hash(Vector2 p)
    {
        float s = Mathf.Sin(p.x * 127.1f + p.y * 311.7f) * 43758.5453f;
        return s - Mathf.Floor(s);
    }

    private static float ValueNoise(Vector2 p)
    {
        Vector2 i = new Vector2(Mathf.Floor(p.x), Mathf.Floor(p.y));
        Vector2 f = p - i;
        Vector2 u = new Vector2(f.x * f.x * (3f - 2f * f.x), f.y * f.y * (3f - 2f * f.y));
        float a = Hash(i), b = Hash(i + new Vector2(1f, 0f));
        float c = Hash(i + new Vector2(0f, 1f)), d = Hash(i + Vector2.one);
        return Mathf.Lerp(Mathf.Lerp(a, b, u.x), Mathf.Lerp(c, d, u.x), u.y);
    }
}
