using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Herramienta para dibujar la curva de una PasarelaCurva en la Scene view.
//   Modo dibujo: click agrega un punto al final, Shift+click lo inserta en el tramo mas cercano,
//   Ctrl+click sobre un punto lo borra, Esc termina.
//   Siempre: arrastrar un punto lo mueve pegado al suelo; Supr borra el punto seleccionado.
// La pasarela se rehace al soltar el mouse (rehacerla en cada cuadro del arrastre traba el Editor
// con tramos largos); mientras se arrastra se ve la curva en amarillo.
//
// Menu Terrenos > Pasarela > Nueva pasarela en curva: crea el objeto con el modulo de Blender y el
// material de madera, y entra directo al modo dibujo.
[CustomEditor(typeof(PasarelaCurva))]
public class PasarelaCurvaEditor : Editor
{
    const string Carpeta = "Assets/Models/Sur/Pasarela";
    const string RutaModulo = Carpeta + "/PasarelaModulo.json";
    const string RutaMaterial = Carpeta + "/M_Madera_Pasarela.mat";

    static bool dibujando;
    static bool costaIzquierda = true;
    static float distanciaCosta = 2.5f;
    static float pasoCosta = 8f;
    int seleccionado = -1;
    bool pendiente;

    PasarelaCurva Pc => (PasarelaCurva)target;

    void OnDisable() { dibujando = false; }

    // ---------------------------------------------------------------- Inspector

    public override void OnInspectorGUI()
    {
        var pc = Pc;
        var color = GUI.backgroundColor;
        if (dibujando) GUI.backgroundColor = new Color(0.5f, 1f, 0.5f);
        if (GUILayout.Button(dibujando ? "Dibujando… (Esc para terminar)" : "Dibujar curva", GUILayout.Height(30)))
        {
            dibujando = !dibujando;
            SceneView.RepaintAll();
        }
        GUI.backgroundColor = color;
        EditorGUILayout.HelpBox(
            "Modo dibujo: click agrega un punto al final · Shift+click lo inserta en el tramo más cercano · " +
            "Ctrl+click sobre un punto lo borra.\nSiempre: arrastrar un punto lo mueve (pegado al suelo) · Supr borra el seleccionado.",
            MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Regenerar")) pc.Generar();
            if (GUILayout.Button("Invertir sentido"))
            {
                Undo.RecordObject(pc, "Invertir pasarela");
                pc.puntos.Reverse();
                pc.Generar();
            }
            if (GUILayout.Button("Borrar puntos") &&
                EditorUtility.DisplayDialog("Borrar puntos", "¿Borrar los " + pc.puntos.Count + " puntos de la curva?", "Borrar", "Cancelar"))
            {
                Undo.RecordObject(pc, "Borrar puntos");
                pc.puntos.Clear();
                pc.Generar();
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Pegar a la costa", EditorStyles.boldLabel);
        costaIzquierda = EditorGUILayout.Popup("Costa", costaIzquierda ? 0 : 1,
            new[] { "A la izquierda del recorrido", "A la derecha del recorrido" }) == 0;
        distanciaCosta = EditorGUILayout.FloatField(new GUIContent("Distancia al agua (m)",
            "Cuánto queda la pasarela tierra adentro desde la línea del agua (y = nivel del agua)."), distanciaCosta);
        pasoCosta = Mathf.Max(2f, EditorGUILayout.FloatField(new GUIContent("Un punto cada (m)"), pasoCosta));
        if (GUILayout.Button("Pegar la curva a la costa"))
        {
            Undo.RecordObject(pc, "Pegar pasarela a la costa");
            PegarACosta(pc, costaIzquierda, distanciaCosta, pasoCosta);
            pc.Generar();
        }

        if (!string.IsNullOrEmpty(pc.error))
            EditorGUILayout.HelpBox(pc.error, MessageType.Warning);
        else
            EditorGUILayout.HelpBox($"{pc.largo:0.0} m · {pc.modulos} módulos · {pc.triangulos:N0} triángulos de cerca", MessageType.Info);

        EditorGUILayout.Space();
        DrawDefaultInspector();
    }

    // ---------------------------------------------------------------- Scene view

    void OnSceneGUI()
    {
        var pc = Pc;
        var e = Event.current;
        var tr = pc.transform;

        if (e.type == EventType.Repaint && pc.puntos.Count >= 2)
        {
            var c = pc.MuestrearCurva(0.5f);
            var linea = new Vector3[c.Count];
            for (int i = 0; i < c.Count; i++)
                linea[i] = new Vector3(c[i].x, Mathf.Max(pc.Suelo(c[i]), pc.nivelAgua) + 0.3f, c[i].z);
            Handles.color = new Color(1f, 0.85f, 0.1f, 1f);
            Handles.DrawAAPolyLine(4f, linea);
        }

        bool borrar = dibujando && e.control;
        for (int i = 0; i < pc.puntos.Count; i++)
        {
            Vector3 w = tr.TransformPoint(pc.puntos[i]);
            float tam = HandleUtility.GetHandleSize(w) * 0.12f;
            if (borrar)
            {
                Handles.color = new Color(1f, 0.3f, 0.3f);
                if (Handles.Button(w, Quaternion.identity, tam, tam, Handles.SphereHandleCap))
                {
                    Undo.RecordObject(pc, "Borrar punto de pasarela");
                    pc.puntos.RemoveAt(i);
                    seleccionado = -1;
                    pc.Generar();
                    GUIUtility.ExitGUI();
                }
                continue;
            }
            Handles.color = i == seleccionado ? new Color(0.3f, 0.9f, 1f) : Color.white;
            EditorGUI.BeginChangeCheck();
            Vector3 nuevo = Handles.FreeMoveHandle(w, tam, Vector3.zero, Handles.SphereHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                if (!PuntoEnSuelo(pc, e.mousePosition, out nuevo))
                    nuevo.y = Mathf.Max(pc.Suelo(nuevo), pc.nivelAgua);
                Undo.RecordObject(pc, "Mover punto de pasarela");
                pc.puntos[i] = tr.InverseTransformPoint(nuevo);
                seleccionado = i;
                pendiente = true;
            }
            if (GUIUtility.hotControl != 0 && i == seleccionado) continue;
            Handles.Label(w + Vector3.up * tam * 2.5f, i.ToString(), EditorStyles.boldLabel);
        }

        // Se regenera al soltar.
        if (pendiente && GUIUtility.hotControl == 0)
        {
            pendiente = false;
            pc.Generar();
            Repaint();
        }

        if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
            && seleccionado >= 0 && seleccionado < pc.puntos.Count)
        {
            Undo.RecordObject(pc, "Borrar punto de pasarela");
            pc.puntos.RemoveAt(seleccionado);
            seleccionado = -1;
            pc.Generar();
            e.Use();
        }

        if (dibujando) Dibujar(pc, e);
    }

    void Dibujar(PasarelaCurva pc, Event e)
    {
        int id = GUIUtility.GetControlID(FocusType.Passive);
        if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);   // que el click no deseleccione
        if (e.type == EventType.MouseMove) SceneView.RepaintAll();
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            dibujando = false;
            e.Use();
            Repaint();
            return;
        }
        if (e.control || !PuntoEnSuelo(pc, e.mousePosition, out Vector3 p)) return;

        var tr = pc.transform;
        if (e.type == EventType.Repaint)
        {
            Handles.color = new Color(1f, 0.85f, 0.1f, 0.8f);
            float tam = HandleUtility.GetHandleSize(p) * 0.1f;
            Handles.SphereHandleCap(0, p, Quaternion.identity, tam, EventType.Repaint);
            if (pc.puntos.Count > 0)
            {
                if (e.shift && pc.puntos.Count >= 2)
                {
                    int t = TramoMasCercano(pc, p);
                    Handles.DrawDottedLine(tr.TransformPoint(pc.puntos[t]), p, 4f);
                    Handles.DrawDottedLine(p, tr.TransformPoint(pc.puntos[t + 1]), 4f);
                }
                else
                    Handles.DrawDottedLine(tr.TransformPoint(pc.puntos[pc.puntos.Count - 1]), p, 4f);
            }
        }

        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            Undo.RecordObject(pc, "Agregar punto de pasarela");
            Vector3 local = tr.InverseTransformPoint(p);
            if (e.shift && pc.puntos.Count >= 2)
            {
                seleccionado = TramoMasCercano(pc, p) + 1;
                pc.puntos.Insert(seleccionado, local);
            }
            else
            {
                pc.puntos.Add(local);
                seleccionado = pc.puntos.Count - 1;
            }
            pc.Generar();
            e.Use();
        }
    }

    // Donde cae el mouse: el primer collider que no sea de la pasarela, o el agua si esta antes.
    // Sobre el mar se usa el punto de la superficie (lo que se ve), no el del fondo.
    static bool PuntoEnSuelo(PasarelaCurva pc, Vector2 mouse, out Vector3 p)
    {
        Ray r = HandleUtility.GUIPointToWorldRay(mouse);
        float mejor = float.MaxValue;
        p = default;
        foreach (var h in Physics.RaycastAll(r, 20000f))
        {
            if (h.collider.isTrigger || h.collider.transform.IsChildOf(pc.transform) || h.distance >= mejor) continue;
            mejor = h.distance; p = h.point;
        }
        var agua = new Plane(Vector3.up, new Vector3(0f, pc.nivelAgua, 0f));
        if (agua.Raycast(r, out float d) && d < mejor) { mejor = d; p = r.GetPoint(d); }
        return mejor < float.MaxValue;
    }

    // Rehace los puntos siguiendo la orilla: cada `paso` metros de la curva actual busca el agua hacia el
    // costado elegido y pone el punto a `distancia` tierra adentro. El desplazamiento se suaviza para que la
    // pasarela no copie cada entrante de la orilla. Los tramos que ya van sobre el mar (el muelle de la punta)
    // se corren lo mismo que el ultimo tramo en tierra, sin buscar orilla.
    public static void PegarACosta(PasarelaCurva pc, bool izquierda, float distancia, float paso)
    {
        var c = pc.MuestrearCurva(paso);
        int n = c.Count;
        if (n < 2) return;
        var lado = new Vector3[n];
        var desp = new float[n];
        var valido = new bool[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 t = c[Mathf.Min(i + 1, n - 1)] - c[Mathf.Max(i - 1, 0)]; t.y = 0f;
            Vector3 derecha = Vector3.Cross(Vector3.up, t.normalized);
            lado[i] = izquierda ? -derecha : derecha;
            if (pc.Suelo(c[i]) <= pc.nivelAgua) continue;
            for (float d = 0.25f; d <= 80f; d += 0.25f)
                if (pc.Suelo(c[i] + lado[i] * d) <= pc.nivelAgua) { desp[i] = d - distancia; valido[i] = true; break; }
        }

        // Los que no encontraron orilla toman el desplazamiento del valido mas cercano.
        var lleno = (float[])desp.Clone();
        for (int i = 0; i < n; i++)
        {
            if (valido[i]) continue;
            for (int k = 1; k < n; k++)
            {
                if (i - k >= 0 && valido[i - k]) { lleno[i] = desp[i - k]; break; }
                if (i + k < n && valido[i + k]) { lleno[i] = desp[i + k]; break; }
            }
        }
        var suave = new float[n];
        for (int i = 0; i < n; i++)
        {
            float a = lleno[Mathf.Max(i - 1, 0)], b = lleno[i], z = lleno[Mathf.Min(i + 1, n - 1)];
            suave[i] = (a + 2f * b + z) * 0.25f;
        }

        var nuevos = new List<Vector3>(n);
        for (int i = 0; i < n; i++)
        {
            Vector3 p = c[i] + lado[i] * suave[i];
            // El suavizado puede dejar un punto en el agua donde la orilla hace punta: se retrocede a tierra.
            if (valido[i])
                for (int k = 0; k < 80 && pc.Suelo(p) <= pc.nivelAgua + 0.1f; k++) p -= lado[i] * 0.5f;
            p.y = Mathf.Max(pc.Suelo(p), pc.nivelAgua);
            nuevos.Add(pc.transform.InverseTransformPoint(p));
        }
        pc.puntos = nuevos;
        EditorUtility.SetDirty(pc);
    }

    static int TramoMasCercano(PasarelaCurva pc, Vector3 p)
    {
        int mejor = 0; float dMin = float.MaxValue;
        Vector2 q = new Vector2(p.x, p.z);
        for (int i = 0; i < pc.puntos.Count - 1; i++)
        {
            Vector3 a3 = pc.transform.TransformPoint(pc.puntos[i]), b3 = pc.transform.TransformPoint(pc.puntos[i + 1]);
            Vector2 a = new Vector2(a3.x, a3.z), b = new Vector2(b3.x, b3.z);
            float t = Mathf.Clamp01(Vector2.Dot(q - a, b - a) / Mathf.Max((b - a).sqrMagnitude, 1e-6f));
            float d = (a + (b - a) * t - q).sqrMagnitude;
            if (d < dMin) { dMin = d; mejor = i; }
        }
        return mejor;
    }

    // ---------------------------------------------------------------- creacion

    [MenuItem("Terrenos/Pasarela/Nueva pasarela en curva")]
    static void Nueva()
    {
        var go = new GameObject("Pasarela");
        var sv = SceneView.lastActiveSceneView;
        if (sv != null) go.transform.position = new Vector3(sv.pivot.x, 0f, sv.pivot.z);
        var pc = go.AddComponent<PasarelaCurva>();
        pc.modulo = AssetDatabase.LoadAssetAtPath<TextAsset>(RutaModulo);
        pc.material = MaterialMadera();
        Undo.RegisterCreatedObjectUndo(go, "Nueva pasarela");
        Selection.activeGameObject = go;
        dibujando = true;
        SceneView.RepaintAll();
    }

    [MenuItem("Terrenos/Pasarela/Rehacer material de madera")]
    static void RehacerMaterial()
    {
        AssetDatabase.DeleteAsset(RutaMaterial);
        var m = MaterialMadera();
        foreach (var pc in Object.FindObjectsByType<PasarelaCurva>(FindObjectsSortMode.None))
        {
            pc.material = m;
            pc.Generar();
        }
    }

    // URP/Lit con las texturas que hizo madera_pasarela.py. La rugosidad de Blender pasa al alfa de un
    // mapa metallic/smoothness (URP no lee rugosidad); la altura no se usa.
    public static Material MaterialMadera()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(RutaMaterial);
        if (mat != null) return mat;

        string tex = Carpeta + "/Texturas/Madera_Pasarela_";
        Importar(tex + "BaseColor.png", true, false);
        Importar(tex + "Normal.png", false, true);
        Importar(tex + "AO.png", false, false);

        string rutaSuav = tex + "Smoothness.png";
        if (!File.Exists(rutaSuav) && File.Exists(tex + "Roughness.png"))
        {
            var rug = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            rug.LoadImage(File.ReadAllBytes(tex + "Roughness.png"));
            var px = rug.GetPixels32();
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, (byte)(255 - px[i].r));
            var suav = new Texture2D(rug.width, rug.height, TextureFormat.RGBA32, false, true);
            suav.SetPixels32(px);
            File.WriteAllBytes(rutaSuav, suav.EncodeToPNG());
            Object.DestroyImmediate(rug); Object.DestroyImmediate(suav);
            AssetDatabase.ImportAsset(rutaSuav);
        }
        Importar(rutaSuav, false, false);

        mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_Madera_Pasarela" };
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tex + "BaseColor.png"));
        mat.SetColor("_BaseColor", Color.white);
        mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tex + "Normal.png"));
        mat.SetFloat("_BumpScale", 1f);
        mat.EnableKeyword("_NORMALMAP");
        mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(rutaSuav));
        mat.SetFloat("_Smoothness", 1f);
        mat.SetFloat("_SmoothnessTextureChannel", 0f);
        mat.EnableKeyword("_METALLICSPECGLOSSMAP");
        mat.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tex + "AO.png"));
        mat.SetFloat("_OcclusionStrength", 1f);
        mat.EnableKeyword("_OCCLUSIONMAP");
        mat.enableInstancing = true;
        AssetDatabase.CreateAsset(mat, RutaMaterial);
        AssetDatabase.SaveAssets();
        return mat;
    }

    static void Importar(string ruta, bool srgb, bool normal)
    {
        if (!(AssetImporter.GetAtPath(ruta) is TextureImporter ti)) return;
        ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        ti.sRGBTexture = srgb;
        ti.maxTextureSize = 2048;
        ti.alphaSource = ruta.EndsWith("Smoothness.png") ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        ti.SaveAndReimport();
    }
}
