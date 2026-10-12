using System.Collections.Generic;
using UnityEngine;

// Zona que recorre el jugador (dibujada por el usuario sobre una captura de Sur, 2026-10-11). Dentro van los
// arboles de verdad (con LODs y collider); afuera el terreno solo es paisaje lejano y lleva arboles que son
// solo billboard (ver ArbolesRocasSur y BosqueLejano). Los puntos son X/Z de mundo; se ve en amarillo en la
// Scene view. Si se cambia, hay que volver a correr Terrenos > Sur > Poblar arboles y rocas.
public class ZonaJugador : MonoBehaviour
{
    public List<Vector2> puntos = new List<Vector2>();

    public bool Contiene(Vector3 p)
    {
        bool dentro = false;
        for (int i = 0, j = puntos.Count - 1; i < puntos.Count; j = i++)
        {
            Vector2 a = puntos[i], b = puntos[j];
            if ((a.y > p.z) != (b.y > p.z) && p.x < (b.x - a.x) * (p.z - a.y) / (b.y - a.y) + a.x)
                dentro = !dentro;
        }
        return dentro;
    }

    void OnDrawGizmos()
    {
        if (puntos.Count < 2) return;
        Gizmos.color = new Color(1f, 0.85f, 0.1f);
        var t = Terrain.activeTerrain;
        Vector3 P(Vector2 q)
        {
            var w = new Vector3(q.x, 0f, q.y);
            w.y = t != null ? Mathf.Max(t.SampleHeight(w) + t.transform.position.y, 0f) + 5f : 5f;
            return w;
        }
        for (int i = 0; i < puntos.Count; i++) Gizmos.DrawLine(P(puntos[i]), P(puntos[(i + 1) % puntos.Count]));
    }
}
