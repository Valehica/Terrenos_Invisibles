using UnityEngine;

/// <summary>
/// Altura del mar en un punto, calculada igual que el vertex shader de
/// Terrenos/Mar: tres ondas de Gerstner (_OlaA, _OlaB, _OlaC) sobre la posición
/// del mundo, con la velocidad escalada por _VelocidadOlas. Si se cambia la
/// cuenta en Mar.shader hay que cambiarla acá.
///
/// Sirve para que lo que flota (la canoa) y la cámara que la sigue suban y
/// bajen con la marejada en vez de quedar tapados por las crestas.
/// </summary>
public class OleajeMuestreo
{
    private static readonly int OlaA = Shader.PropertyToID("_OlaA");
    private static readonly int OlaB = Shader.PropertyToID("_OlaB");
    private static readonly int OlaC = Shader.PropertyToID("_OlaC");
    private static readonly int VelocidadOlas = Shader.PropertyToID("_VelocidadOlas");

    private readonly Material material;
    private readonly Transform sea;

    public OleajeMuestreo(Renderer seaRenderer)
    {
        if (seaRenderer == null) return;
        sea = seaRenderer.transform;
        material = seaRenderer.sharedMaterial;
        if (material != null && !material.HasProperty(OlaA)) material = null;
    }

    public float SeaLevel => sea != null ? sea.position.y : 0f;

    // Parámetros del material leídos una vez por frame: leerlos en cada consulta
    // (la espuma hace cientos) costaba ~2 ms.
    private int cachedFrame = -1;
    private float t, velocidad;
    private Vector4 a, b, c;

    private void RefreshCache()
    {
        if (cachedFrame == Time.frameCount) return;
        cachedFrame = Time.frameCount;
        // _Time.y del shader es el tiempo desde que cargó el nivel.
        t = Time.timeSinceLevelLoad;
        velocidad = material.GetFloat(VelocidadOlas);
        a = material.GetVector(OlaA);
        b = material.GetVector(OlaB);
        c = material.GetVector(OlaC);
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

        // Gerstner también desplaza en horizontal: se busca el punto de la malla
        // en reposo que termina encima de "world" (tres iteraciones alcanzan).
        Vector2 target = new Vector2(world.x, world.z);
        Vector2 grid = target;
        Vector3 offset = Vector3.zero;
        for (int i = 0; i < iterations; i++)
        {
            offset = Gerstner(a, grid) + Gerstner(b, grid) + Gerstner(c, grid);
            grid = target - new Vector2(offset.x, offset.z);
        }
        return level + offset.y;
    }

    private Vector3 Gerstner(Vector4 wave, Vector2 p)
    {
        float k = 2f * Mathf.PI / Mathf.Max(wave.w, 0.01f);
        float speed = Mathf.Sqrt(9.8f / k) * velocidad;
        Vector2 dir = new Vector2(wave.x, wave.y).normalized;
        float f = k * (Vector2.Dot(dir, p) - speed * t);
        float amp = wave.z / k;
        float co = Mathf.Cos(f);
        return new Vector3(dir.x * amp * co, amp * Mathf.Sin(f), dir.y * amp * co);
    }
}
