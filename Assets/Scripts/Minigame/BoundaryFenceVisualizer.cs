using UnityEngine;

// Dibuja un "cerco" rojo semitransparente alrededor del BoxCollider de este GameObject,
// para que el jugador vea claramente el límite de la zona donde pueden andar las vicuñas
// (cruzarlo dispara el reinicio de la ronda en ChakuMinigameManager.CheckEscapeAndWin).
[RequireComponent(typeof(BoxCollider))]
public class BoundaryFenceVisualizer : MonoBehaviour
{
    [Header("Muro visual")]
    [SerializeField] private float wallHeight = 4f;
    [SerializeField] private float wallThickness = 0.4f;
    [SerializeField] private Color wallColor = new Color(1f, 0f, 0f, 0.35f);

    [Header("Suelo")]
    [SerializeField] private Terrain groundTerrain;

    private void Awake()
    {
        if (groundTerrain == null) groundTerrain = Terrain.activeTerrain;
        BuildFence();
    }

    private void BuildFence()
    {
        BoxCollider col = GetComponent<BoxCollider>();
        Bounds b = col.bounds;

        float halfX = b.extents.x;
        float halfZ = b.extents.z;
        Vector3 center = new Vector3(b.center.x, 0f, b.center.z);

        Material mat = CreateWallMaterial();

        CreateWall("Fence_Norte", center + new Vector3(0f, 0f, halfZ), new Vector3(b.size.x + wallThickness, wallHeight, wallThickness), mat);
        CreateWall("Fence_Sur", center + new Vector3(0f, 0f, -halfZ), new Vector3(b.size.x + wallThickness, wallHeight, wallThickness), mat);
        CreateWall("Fence_Este", center + new Vector3(halfX, 0f, 0f), new Vector3(wallThickness, wallHeight, b.size.z + wallThickness), mat);
        CreateWall("Fence_Oeste", center + new Vector3(-halfX, 0f, 0f), new Vector3(wallThickness, wallHeight, b.size.z + wallThickness), mat);
    }

    private void CreateWall(string wallName, Vector3 xzPos, Vector3 size, Material mat)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = wallName;
        wall.transform.SetParent(transform);

        Collider wallCollider = wall.GetComponent<Collider>();
        if (wallCollider != null) Destroy(wallCollider);

        float groundY = groundTerrain != null
            ? groundTerrain.SampleHeight(xzPos) + groundTerrain.transform.position.y
            : xzPos.y;

        wall.transform.position = new Vector3(xzPos.x, groundY + size.y / 2f, xzPos.z);
        wall.transform.localScale = size;

        Renderer wallRenderer = wall.GetComponent<Renderer>();
        wallRenderer.sharedMaterial = mat;
        wallRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        wallRenderer.receiveShadows = false;
    }

    private Material CreateWallMaterial()
    {
        // Mismo shader que usa la soga de ChainController: siempre alfa-blend por defecto,
        // sin tener que pelear con las keywords de superficie/blend de los shaders URP.
        Material mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = wallColor;
        return mat;
    }
}
