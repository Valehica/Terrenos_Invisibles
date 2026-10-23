using System;
using UnityEngine;

/// <summary>
/// Base común de los minijuegos. Permite que un <see cref="Interactable"/> apunte
/// al minijuego que debe lanzar, sin que el DialogueManager conozca cada zona.
/// </summary>
public abstract class MinigameBase : MonoBehaviour
{
    /// <summary>
    /// Se dispara al volver a la exploración: true si se completó, false si se abandonó.
    /// Lo escucha el flujo de la zona para seguir con el paso siguiente.
    /// </summary>
    public event Action<bool> Terminado;

    public abstract void StartMinigame();

    protected void AvisarTerminado(bool completado) => Terminado?.Invoke(completado);
}
