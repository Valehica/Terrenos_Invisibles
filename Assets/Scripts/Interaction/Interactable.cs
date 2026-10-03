using UnityEngine;

public class Interactable : MonoBehaviour
{
    [TextArea]
    [SerializeField] private string dialogueText = "Hola, buenos días";

    [SerializeField] private bool startsMinigame = false;

    [Tooltip("Minijuego que lanza el botón Aceptar. Si queda vacío se usa el ChakuMinigameManager de la escena.")]
    [SerializeField] private MinigameBase minigame;

    public string DialogueText => dialogueText;
    public Transform InteractionPoint => transform;
    public bool StartsMinigame => startsMinigame;
    public MinigameBase Minigame => minigame;
}
