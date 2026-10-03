using System.Collections.Generic;
using UnityEngine;

public class VicunaHerdGroup : MonoBehaviour
{
    private readonly List<VicunaHerdMember> members = new List<VicunaHerdMember>();

    public IReadOnlyList<VicunaHerdMember> Members => members;
    public Vector3 Centroid { get; private set; }

    private void Awake()
    {
        GetComponentsInChildren(members);
        Centroid = ComputeCentroid();
    }

    private void Update()
    {
        Centroid = ComputeCentroid();
    }

    private Vector3 ComputeCentroid()
    {
        if (members.Count == 0) return transform.position;

        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (var member in members)
        {
            if (member == null) continue;
            sum += member.transform.position;
            count++;
        }

        return count > 0 ? sum / count : transform.position;
    }
}
