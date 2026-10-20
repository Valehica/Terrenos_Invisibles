using UnityEngine;

// Publica la altura de este objeto (el plano del mar) como global de shader, para que RenderShaderURP sepa que
// pixeles opacos estan bajo el agua.
//
// El agua es transparente y no escribe profundidad: el pase de estilo ve el fondo marino como suelo y le aplica
// pigmento, bandas de color y perspectiva aerea encima de lo que el Mar ya hizo (el Mar aplica su propia
// perspectiva aerea). El agua sobre fondo se veia gris y el agua sin fondo azul, con un escalon donde termina el
// Terrain. Con esto el pase trata lo sumergido como agua.
//
// Va en el mismo objeto que mueve la marea (MareaController mueve el transform). _AguaGlobal = (nivel, activo):
// sin este componente en la escena el pase no cambia nada.
[ExecuteAlways]
[DisallowMultipleComponent]
public class NivelDelAgua : MonoBehaviour
{
    static readonly int IdAgua = Shader.PropertyToID("_AguaGlobal");

    void OnEnable() => Publicar();
    void LateUpdate() => Publicar();
    void OnDisable() => Shader.SetGlobalVector(IdAgua, Vector4.zero);

    void Publicar() => Shader.SetGlobalVector(IdAgua, new Vector4(transform.position.y, 1f, 0f, 0f));
}
