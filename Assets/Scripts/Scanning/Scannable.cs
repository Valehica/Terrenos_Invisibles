using UnityEngine;

public class Scannable : MonoBehaviour
{
    [SerializeField] private string speciesId = "vicuna";
    [SerializeField] private string displayName = "Vicuña";
    [SerializeField] private string scientificName = "Vicugna vicugna";

    [TextArea(3, 8)]
    [SerializeField] private string description = "Descripción pendiente.";

    public string SpeciesId => speciesId;
    public string DisplayName => displayName;
    public string ScientificName => scientificName;
    public string Description => description;
}
