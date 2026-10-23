using UnityEngine;

/// <summary>Una línea de una conversación (ver <see cref="DialogueManager.StartConversation"/>).</summary>
[System.Serializable]
public class LineaDialogo
{
    [Tooltip("Quién habla, tal como se muestra arriba del texto.")]
    public string quien;

    [TextArea(2, 5)]
    public string texto;

    [Tooltip("Monólogo interior: va en cursiva y no lleva la cámara hacia nadie.")]
    public bool pensando;

    public LineaDialogo() { }

    public LineaDialogo(string quien, string texto, bool pensando = false)
    {
        this.quien = quien;
        this.texto = texto;
        this.pensando = pensando;
    }
}
