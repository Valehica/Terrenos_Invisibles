using System.Collections.Generic;
using UnityEngine;

/// <summary>Cada interfaz que puede estar abierta encima de la exploración.</summary>
public enum CapaUI
{
    Menu,
    Carga,
    Intro,
    /// <summary>El panel de un NPC con los botones Salir / Aceptar.</summary>
    Dialogo,
    /// <summary>Las líneas del flujo de la zona, que avanzan con Enter o clic: el cursor sigue tomado.</summary>
    Conversacion,
    Minijuego,
    /// <summary>El panel de resultado de un minijuego, con botones para el mouse.</summary>
    ResultadoMinijuego,
    Telefono,
    /// <summary>Las postales desplegadas (R).</summary>
    Postales,
    /// <summary>
    /// Una postal elegida, a la izquierda de la pantalla, para compararla con el lugar: se camina
    /// y se mira con el mouse, pero F, E y Esc siguen siendo de las postales.
    /// </summary>
    PostalEnMano,
    /// <summary>El jugador soltó el cursor con Esc.</summary>
    CursorSuelto,
}

/// <summary>
/// Lleva la cuenta de qué interfaces están abiertas y a partir de eso decide el cursor y qué
/// input queda bloqueado. Nadie más toca <see cref="Cursor"/>: se abre o se cierra una capa
/// y el resto pregunta <see cref="BloqueaMovimiento"/>, <see cref="BloqueaAcciones"/> o
/// <see cref="CursorLibre"/>.
///
/// Cada capa tiene un dueño: si se destruye sin cerrarla (por ejemplo al cambiar de escena)
/// la capa se descarta sola.
/// </summary>
public static class UIManager
{
    private static readonly Dictionary<CapaUI, Object> abiertas = new Dictionary<CapaUI, Object>();
    private static readonly List<CapaUI> huerfanas = new List<CapaUI>();
    private static int frameCierre = -1;

    // Con el domain reload apagado los estáticos sobreviven entre plays.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reiniciar()
    {
        abiertas.Clear();
        frameCierre = -1;
    }

    /// <summary>Abre la capa a nombre de <paramref name="dueño"/> (obligatorio).</summary>
    public static void Abrir(CapaUI capa, Object dueño)
    {
        abiertas[capa] = dueño;
        // Al volver de un diálogo o un minijuego el cursor tiene que quedar tomado, aunque
        // antes el jugador lo hubiera soltado con Esc.
        if (FrenaAcciones(capa)) abiertas.Remove(CapaUI.CursorSuelto);
        AplicarCursor();
    }

    /// <summary>Cierra la capa. Con <paramref name="dueño"/>, solo si la abrió él.</summary>
    public static void Cerrar(CapaUI capa, Object dueño = null)
    {
        if ((dueño == null || (abiertas.TryGetValue(capa, out Object actual) && actual == dueño))
            && abiertas.Remove(capa) && FrenaAcciones(capa))
            frameCierre = Time.frameCount;
        AplicarCursor();
    }

    public static bool EstaAbierta(CapaUI capa)
    {
        Limpiar();
        return abiertas.ContainsKey(capa);
    }

    /// <summary>Hay que mostrar el cursor para usar botones; la cámara no gira con el mouse.</summary>
    public static bool CursorLibre => Alguna(Regla.Cursor);

    /// <summary>El jugador no camina.</summary>
    public static bool BloqueaMovimiento => Alguna(Regla.Movimiento);

    /// <summary>
    /// No se habla con NPC (F), no se saca el teléfono (E) ni se suelta el cursor (Esc). Sigue
    /// bloqueado el frame en que se cierra la capa: la tecla que la cerró no llega a la exploración.
    /// </summary>
    public static bool BloqueaAcciones => frameCierre == Time.frameCount || Alguna(Regla.Acciones);

    private enum Regla { Cursor, Movimiento, Acciones }

    private static bool Cumple(CapaUI c, Regla r) => r switch
    {
        Regla.Cursor => LiberaCursor(c),
        Regla.Movimiento => FrenaAlJugador(c),
        _ => FrenaAcciones(c),
    };

    private static bool LiberaCursor(CapaUI c) =>
        c is CapaUI.Menu or CapaUI.Dialogo or CapaUI.ResultadoMinijuego or CapaUI.Postales or CapaUI.CursorSuelto;

    // El teléfono no está acá: el PlayerController lo frena por su cuenta y lo deja caer.
    // En los minijuegos que se juegan a pie (la luga) el jugador sigue caminando.
    private static bool FrenaAlJugador(CapaUI c) =>
        c is CapaUI.Menu or CapaUI.Carga or CapaUI.Intro or CapaUI.Dialogo or CapaUI.Conversacion
            or CapaUI.ResultadoMinijuego or CapaUI.Postales;

    private static bool FrenaAcciones(CapaUI c) =>
        c is not (CapaUI.Telefono or CapaUI.CursorSuelto);

    private static bool Alguna(Regla regla)
    {
        Limpiar();
        foreach (CapaUI c in abiertas.Keys)
            if (Cumple(c, regla)) return true;
        return false;
    }

    private static void Limpiar()
    {
        huerfanas.Clear();
        foreach (var par in abiertas)
            if (par.Value == null) huerfanas.Add(par.Key);
        if (huerfanas.Count == 0) return;

        foreach (CapaUI c in huerfanas) abiertas.Remove(c);
        AplicarCursor();
    }

    private static void AplicarCursor()
    {
        bool libre = false;
        foreach (var par in abiertas)
            if (par.Value != null && LiberaCursor(par.Key)) { libre = true; break; }

        Cursor.lockState = libre ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = libre;
    }
}
