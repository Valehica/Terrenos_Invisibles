using System;
using UnityEngine;

// Paredes invisibles que no dejan al jugador salir de la zona jugable.
//
// En Sur son dos recintos: la isla de Puerto Eden y la isla del Parque O'Higgins (a la que se llega en canoa).
// En Centro es uno solo, con pocas paredes rectas que van justo por la guia (paredesPorGuia), y en Norte es el rectangulo del pueblo de Isluga (ahi no hay mar: la pared va justo por
// la guia). Las paredes son BoxColliders hijos de este objeto y las arma el menu
// Terrenos > (escena) > Generar limite jugable (Editor/LimiteJugableGenerador.cs) a partir de las guias: el
// contorno trazado a mano sobre el mapa. La pared va por
// la costa real del Terrain (un poco dentro del agua, ver nivelCosta); solo donde la guia cruza tierra (istmos)
// manda la guia. La pasarela queda siempre adentro.
//
// - Estan en la capa LimiteJugador. La camara, el escaner y los gaviotines la sacan de sus mascaras con
//   MascaraSin, para que la pared no empuje la camara ni tape lo que se escanea sobre el agua.
// - Mientras corre el minijuego de la canoa se apagan, y ademas nunca chocan con la canoa (IgnoreCollision),
//   asi que no la empujan si queda amarrada cerca de una pared.
// - Si se regenera el terreno o se mueve la pasarela, volver a correr el menu.
[DisallowMultipleComponent]
public class LimiteJugable : MonoBehaviour
{
    public const string NombreCapa = "LimiteJugador";

    [Serializable]
    public class Guia
    {
        public string nombre;
        [Tooltip("Contorno trazado a mano, en (x, z) del mundo.")]
        public Vector2[] puntos;
    }

    [Serializable]
    public class Contorno
    {
        public string nombre;
        public Vector3[] puntos;
    }

    public Guia[] guias;

    [Header("Generacion")]
    [Tooltip("La pared va justo por la guia, una pared por lado, sin mirar la costa. Para contornos casi rectos.")]
    public bool paredesPorGuia;
    [Tooltip("La pared va por donde el fondo queda a esta altura (m, el mar esta en 0). Negativo = un poco dentro del agua.")]
    public float nivelCosta = -0.5f;
    [Tooltip("Cuanto puede quedar la costa por fuera de la guia (m). Mas lejos que esto, la pared sigue a la guia.")]
    public float toleranciaGuia = 15f;
    [Tooltip("Franja alrededor de la pasarela (m) que queda adentro aunque este sobre el agua o pase la guia.")]
    public float margenPasarela = 1.5f;
    [Tooltip("Alto de la pared por encima del punto mas alto del suelo bajo ella (m).")]
    public float altoSobreSuelo = 15f;
    public float grosor = 1f;

    [Header("Escena")]
    [Tooltip("Dibuja el limite en la vista de escena.")]
    public bool mostrarEnEscena = true;

    [Header("Resultado (lo llena el generador)")]
    public Contorno[] contornos;

    /// <summary>La mascara sin la capa de las paredes (si la capa no existe, la deja igual).</summary>
    public static int MascaraSin(int mascara)
    {
        int capa = LayerMask.NameToLayer(NombreCapa);
        return capa < 0 ? mascara : mascara & ~(1 << capa);
    }

    Collider[] paredes;
    bool apagadas;

    void Awake()
    {
        paredes = GetComponentsInChildren<Collider>(true);
        foreach (var canoa in FindObjectsByType<CanoaController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            foreach (var c in canoa.GetComponentsInChildren<Collider>(true))
                foreach (var p in paredes)
                    Physics.IgnoreCollision(c, p, true);
    }

    void Update()
    {
        bool apagar = CanoaMinigameManager.IsRunning;
        if (apagar == apagadas) return;
        apagadas = apagar;
        foreach (var p in paredes)
            if (p != null) p.enabled = !apagar;
    }

    void OnDisable()
    {
        if (!apagadas) return;
        apagadas = false;
        foreach (var p in paredes)
            if (p != null) p.enabled = true;
    }

    void OnDrawGizmos()
    {
        if (!mostrarEnEscena || contornos == null) return;
        Gizmos.color = new Color(1f, 0.15f, 0.1f, 1f);
        foreach (var c in contornos)
        {
            if (c?.puntos == null) continue;
            for (int i = 0; i < c.puntos.Length; i++)
            {
                Vector3 a = c.puntos[i], b = c.puntos[(i + 1) % c.puntos.Length];
                Gizmos.DrawLine(a, b);
                Gizmos.DrawLine(a + Vector3.up * 3f, b + Vector3.up * 3f);
            }
        }
    }
}
