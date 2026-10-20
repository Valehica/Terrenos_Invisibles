using UnityEngine.SceneManagement;

/// <summary>
/// Cambio de escena pasando por la pantalla de carga.
///
/// La escena "Carga" es liviana: al entrar en ella se descarga la zona anterior
/// y recién entonces se empieza a cargar la siguiente, así nunca hay dos zonas
/// en memoria al mismo tiempo.
/// </summary>
public static class CargadorEscenas
{
    public const string EscenaCarga = "Carga";

    public static string Destino { get; private set; }
    public static string Titulo { get; private set; }
    public static string Dato { get; private set; }

    /// <param name="destino">Nombre de la escena a cargar (tiene que estar en Build Settings).</param>
    /// <param name="titulo">Texto grande de la pantalla de carga, por ejemplo "Acto 2 · Isluga".</param>
    /// <param name="dato">Texto chico de la pantalla de carga: un dato de la zona.</param>
    public static void Ir(string destino, string titulo = null, string dato = null)
    {
        Destino = destino;
        Titulo = titulo;
        Dato = dato;
        SceneManager.LoadScene(EscenaCarga);
    }

    /// <summary>Lo usa la pantalla de carga: entrega el destino pedido y lo olvida.</summary>
    public static string TomarDestino(string porDefecto)
    {
        string destino = string.IsNullOrEmpty(Destino) ? porDefecto : Destino;
        Destino = null;
        return destino;
    }
}
