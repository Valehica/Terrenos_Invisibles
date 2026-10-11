using UnityEngine;

/// <summary>
/// Replica en Unity los drivers de Blender del gaviotín. Los huesos alas_ctrl y
/// panza_ctrl suben hasta 0,1 m por su Y local, y eso mueve los blend shapes
/// Alas_Plegadas, Alas_Medias y Panza_Vuelo. Unity no importa drivers, así que
/// sin esto el ala plegada se ve con el skinning crudo.
///
/// Solo hace algo si el FBX trae los blend shapes y los dos huesos de control.
/// </summary>
public class GaviotinShapeKeys : MonoBehaviour
{
    [Tooltip("Lo que sube cada hueso de control para llegar a 1 (Limit Location en Blender).")]
    [SerializeField] private float recorrido = 0.1f;
    [Tooltip("Plegado en que el skinning sin correctivo está limpio (F_LIMPIO).")]
    [SerializeField] private float fLimpio = 0.4f;
    [Tooltip("Plegado en que Alas_Medias es máxima y empieza Alas_Plegadas (F_MEDIA).")]
    [SerializeField] private float fMedia = 0.8f;

    private SkinnedMeshRenderer smr;
    private Transform alas, panza;
    private Vector3 alasReposo, panzaReposo;
    private int iPlegadas = -1, iMedias = -1, iPanza = -1;

    private void Awake()
    {
        smr = GetComponentInChildren<SkinnedMeshRenderer>();
        foreach (var t in GetComponentsInChildren<Transform>())
        {
            if (t.name == "alas_ctrl") alas = t;
            else if (t.name == "panza_ctrl") panza = t;
        }
        if (alas != null) alasReposo = alas.localPosition;
        if (panza != null) panzaReposo = panza.localPosition;

        var mesh = smr != null ? smr.sharedMesh : null;
        if (mesh != null)
        {
            iPlegadas = mesh.GetBlendShapeIndex("Alas_Plegadas");
            iMedias = mesh.GetBlendShapeIndex("Alas_Medias");
            iPanza = mesh.GetBlendShapeIndex("Panza_Vuelo");
        }

        bool hayFormas = iPlegadas >= 0 || iMedias >= 0 || iPanza >= 0;
        bool hayHuesos = alas != null || panza != null;
        if (hayFormas && !hayHuesos)
            Debug.LogWarning($"{name}: el FBX trae los shape keys pero no los huesos alas_ctrl/panza_ctrl. " +
                             "Exportar sin 'Only Deform Bones'.", this);
        enabled = hayFormas && hayHuesos;
    }

    private void LateUpdate()
    {
        if (alas != null)
        {
            float f = Control(alas, alasReposo);
            Poner(iPlegadas, (f - fMedia) / (1f - fMedia));
            Poner(iMedias, Mathf.Min((f - fLimpio) / (fMedia - fLimpio), (1f - f) / (1f - fMedia)));
        }
        if (panza != null) Poner(iPanza, Control(panza, panzaReposo));
    }

    // El Limit Location deja mover solo la Y local entre 0 y el recorrido, así que
    // alcanza con cuánto se apartó del reposo (no depende del eje que use el FBX).
    private float Control(Transform hueso, Vector3 reposo) =>
        Mathf.Clamp01((hueso.localPosition - reposo).magnitude / recorrido);

    private void Poner(int indice, float valor)
    {
        if (indice >= 0) smr.SetBlendShapeWeight(indice, Mathf.Clamp01(valor) * 100f);
    }
}
