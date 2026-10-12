using System;
using UnityEngine;

// Billboards de los arboles del Terrain, horneados por Assets/Editor/ArbolesLodSur.cs. Una entrada por prefab
// de arbol: donde esta su foto de frente y de costado en el atlas y que tamano tiene el cuadro en el espacio
// del arbol (sin escala de instancia). Lo usa BosqueLejano para armar las mallas del bosque lejano.
[CreateAssetMenu(menuName = "Terrenos/Bosque Billboards")]
public class BosqueBillboards : ScriptableObject
{
    [Serializable]
    public class Entrada
    {
        public GameObject prefab;
        public Rect uvFrente, uvCostado;   // en el atlas
        public Vector3 centro;             // centro de la caja del arbol (espacio del arbol)
        public float ladoFrente, ladoCostado;  // los cuadros son cuadrados, centrados en `centro`
    }

    public Material material;
    public Entrada[] entradas = new Entrada[0];
}
