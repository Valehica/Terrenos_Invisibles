using UnityEngine;

public class CloudLayerFollow : MonoBehaviour
{
    [SerializeField] private Transform target;

    private void LateUpdate()
    {
        if (target == null && Camera.main != null)
            target = Camera.main.transform;

        if (target == null) return;

        transform.position = target.position;
    }
}
