using UnityEngine;

// Viento de los arboles con el shader Terrenos/ArbolViento (coigues y cipreses de Sur). Pone los valores
// globales que lee Assets/Shaders/ArbolViento.hlsl; sin este componente en la escena los arboles no se mueven.
// Corre tambien en el Editor (en la vista Scene se ve moverse con "Always Refresh" activado).
[ExecuteAlways]
public class VientoArboles : MonoBehaviour
{
    [Tooltip("Hacia donde sopla, en grados (0 = +Z, 90 = +X).")]
    [Range(0f, 360f)] public float direccion = 60f;
    [Tooltip("Cuanto se dobla la punta de un arbol de 40 m, en metros. Los mas bajos se doblan menos.")]
    [Range(0f, 3f)] public float flexion = 0.8f;
    [Tooltip("Rapidez del vaiven.")]
    [Range(0f, 4f)] public float velocidad = 1.2f;
    [Tooltip("Cuanto aletean las hojas, en metros.")]
    [Range(0f, 0.5f)] public float aleteo = 0.12f;
    [Tooltip("Tamano de las olas de viento que cruzan el bosque: mas alto = olas mas cortas, arboles vecinos mas distintos.")]
    [Range(0.005f, 0.2f)] public float escalaOlas = 0.04f;

    static readonly int IdDireccion = Shader.PropertyToID("_VientoDireccion");
    static readonly int IdParametros = Shader.PropertyToID("_VientoParametros");

    void OnEnable() => Aplicar();
    void OnValidate() => Aplicar();
    void Update() => Aplicar();

    void OnDisable()
    {
        Shader.SetGlobalVector(IdParametros, Vector4.zero);
    }

    void Aplicar()
    {
        float rad = direccion * Mathf.Deg2Rad;
        Shader.SetGlobalVector(IdDireccion, new Vector4(Mathf.Sin(rad), Mathf.Cos(rad), 0f, 0f));
        Shader.SetGlobalVector(IdParametros, new Vector4(flexion, velocidad, aleteo, escalaOlas));
    }
}
