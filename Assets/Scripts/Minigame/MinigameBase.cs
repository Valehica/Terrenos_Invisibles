using UnityEngine;

/// <summary>
/// Base común de los minijuegos. Permite que un <see cref="Interactable"/> apunte
/// al minijuego que debe lanzar, sin que el DialogueManager conozca cada zona.
/// </summary>
public abstract class MinigameBase : MonoBehaviour
{
    public abstract void StartMinigame();
}
