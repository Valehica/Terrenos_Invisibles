using System.Collections.Generic;
using UnityEngine;

public class OutlineHighlight : MonoBehaviour
{
    [SerializeField] private Material outlineMaterial;
    [SerializeField] private bool visibleOnStart = true;

    [Tooltip("Renderers que no deben recibir el contorno (por ejemplo, la malla del rostro cuando la caricatura sale fea sobre los ojos/nariz/boca).")]
    [SerializeField] private Renderer[] excludedRenderers;

    private Renderer[] renderers;
    private readonly Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();
    private bool outlineVisible;

    private void Awake()
    {
        var allRenderers = GetComponentsInChildren<Renderer>(true);
        var included = new List<Renderer>(allRenderers.Length);
        foreach (var r in allRenderers)
        {
            bool excluded = excludedRenderers != null && System.Array.IndexOf(excludedRenderers, r) >= 0;
            if (!excluded) included.Add(r);
        }
        renderers = included.ToArray();

        foreach (var r in renderers)
            originalMaterials[r] = r.sharedMaterials;
    }

    private void Start()
    {
        if (visibleOnStart) ShowOutline();
    }

    public void ShowOutline()
    {
        if (outlineVisible || outlineMaterial == null) return;
        outlineVisible = true;

        foreach (var r in renderers)
        {
            if (r == null) continue;
            Material[] baseMats = originalMaterials[r];
            Material[] withOutline = new Material[baseMats.Length + 1];
            baseMats.CopyTo(withOutline, 0);
            withOutline[baseMats.Length] = outlineMaterial;
            r.materials = withOutline;
        }
    }

    public void HideOutline()
    {
        if (!outlineVisible) return;
        outlineVisible = false;

        foreach (var r in renderers)
        {
            if (r == null) continue;
            r.materials = originalMaterials[r];
        }
    }
}
