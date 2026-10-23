using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Pone las postales (R) en la escena abierta: un objeto "Postales" con PostalesUI y los sprites
// de Assets/Sprites/Postales en orden de nombre. Si ya está, solo vuelve a cargar los sprites,
// así que se puede re-correr después de agregar una postal nueva a la carpeta.
public static class PostalesEditor
{
    const string Carpeta = "Assets/Sprites/Postales";

    [MenuItem("Terrenos/Postales/Agregar a la escena")]
    public static void Agregar()
    {
        var sprites = AssetDatabase.FindAssets("t:Sprite", new[] { Carpeta })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Distinct()
            .OrderBy(p => p, System.StringComparer.OrdinalIgnoreCase)
            .Select(AssetDatabase.LoadAssetAtPath<Sprite>)
            .Where(s => s != null)
            .ToArray();

        var ui = Object.FindAnyObjectByType<PostalesUI>(FindObjectsInactive.Include);
        if (ui == null)
        {
            var go = new GameObject("Postales");
            Undo.RegisterCreatedObjectUndo(go, "Agregar postales");
            ui = go.AddComponent<PostalesUI>();
        }

        var so = new SerializedObject(ui);
        var prop = so.FindProperty("postales");
        prop.arraySize = sprites.Length;
        for (int i = 0; i < sprites.Length; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
        Selection.activeObject = ui.gameObject;
        Debug.Log($"Postales: {sprites.Length} sprites en \"{ui.gameObject.scene.name}\" desde {Carpeta}.");
    }
}
