using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Rendering;

// Texturas de los coigues y cipreses de Sur (CipreGrande/CiprePeque/CoigueGrande/CoiguePeque.fbx, exportados
// de Concept/Sur/Pasarela/Arbol/coigue_collection 6.blend).
//
// En Blender la corteza NO usa UV: va con coordenadas de objeto -> Mapping (escala 1, 1, -0.4) -> Image
// Texture en proyeccion Box con blend 0. El FBX no lleva eso, asi que en Unity el tronco del coigue llegaba
// sin UV y el del cipres con unas UV que Blender ni mira. Este postprocesador rehace esa misma proyeccion
// como UV al importar: cada triangulo toma el eje dominante de su normal (igual que Box con blend 0) y los
// vertices se separan solo donde cambia el eje. Se aplica solo a las mallas con material M_Corteza_*, asi
// que re-exportar el FBX no requiere nada.
//
// Las hojas: en Blender la imagen empaquetada entra solo como Value de un nodo HSV sobre un verde fijo (y el
// alfa recorta). Hojas_Cipres.png / Hojas_Coigue.png son esas imagenes con RGB = luminancia y el alfa intacto;
// el verde va en _BaseColor.
//
// Menu Terrenos > Sur > Materiales coigue y cipres: crea los materiales en Vegetacion/Materials y los asigna
// a los cuatro FBX (remap), sin tocar el .blend. Usan Terrenos/ArbolViento (URP/Lit con viento); los datos de
// cuanto se mueve cada vertice los hornea este postprocesador en el color de vertice (DatosViento).
// Menu Terrenos > Sur > Arboles coigue y cipres al Terrain: arma Arbol_*.prefab (LODGroup, collider del
// tronco y Scannable) y cambia los prototipos del Terrain que eran los FBX por esos prefabs (los arboles ya
// pintados se conservan). Tambien hornea las versiones lejanas Arbol_*_Lejos (ArbolesLejanosSur) y las agrega
// como prototipos para pintarlas. Re-correrlo despues de cambiar los modelos o los materiales.
public class ArbolesCoigueCipresSur : AssetPostprocessor
{
    const string Carpeta = "Assets/Models/Sur/Vegetacion";
    const string CarpetaMat = Carpeta + "/Materials";
    static readonly string[] Modelos = { "CipreGrande", "CiprePeque", "CoigueGrande", "CoiguePeque" };

    // Mapping de los materiales de corteza en Blender.
    static readonly Vector3 EscalaMapping = new Vector3(1f, 1f, -0.4f);

    public override uint GetVersion() => 4;

    bool EsArbol()
    {
        if (!assetPath.StartsWith(Carpeta + "/") || !assetPath.EndsWith(".fbx")) return false;
        return System.Array.IndexOf(Modelos, System.IO.Path.GetFileNameWithoutExtension(assetPath)) >= 0;
    }

    // Con Read/Write apagado Unity ignora en silencio lo que se escriba en la malla (queda con 0 vertices).
    // Son ~2k vertices por arbol, asi que dejarlas legibles no pesa.
    void OnPreprocessModel()
    {
        if (EsArbol()) ((ModelImporter)assetImporter).isReadable = true;
    }

    void OnPostprocessModel(GameObject raiz)
    {
        if (!EsArbol()) return;

        foreach (var r in raiz.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mat = r.sharedMaterial;
            var mf = r.GetComponent<MeshFilter>();
            if (mat == null || mf == null || mf.sharedMesh == null) continue;
            if (!EsCorteza(mat)) continue;
            UvBox(mf.sharedMesh, EscalaMapping);
        }
        DatosViento(raiz, EsCorteza);
    }

    // ---------------------------------------------------------------- datos de viento (color de vertice)

    // Lo que lee Assets/Shaders/ArbolViento.hlsl: r = altura sobre la base del tronco / 40 m, g = hoja (1) o
    // corteza (0), b = fase al azar por tarjeta de hojas, a = distancia al eje del tronco / 15 m. Se mide con
    // las transformaciones del FBX aplicadas (las hojas son hijos con su propia posicion, giro y escala, y la
    // raiz del CipreGrande tiene escala 1.6), asi las alturas son metros reales.
    const float AlturaViento = 40f, RadioViento = 15f;

    static bool EsCorteza(Material m) => m != null && m.name.StartsWith("M_Corteza");

    // Tambien la usa ArbolesArrayanOlivillo, con su propio criterio de que material es corteza.
    internal static void DatosViento(GameObject raiz, System.Func<Material, bool> esCorteza)
    {
        var rs = raiz.GetComponentsInChildren<MeshRenderer>(true);

        // Base y eje del tronco.
        float yMin = float.MaxValue;
        var corteza = new List<Vector3>();
        foreach (var r in rs)
        {
            if (!esCorteza(r.sharedMaterial)) continue;
            var m = r.transform.localToWorldMatrix;
            foreach (var v in r.GetComponent<MeshFilter>().sharedMesh.vertices)
            {
                var p = m.MultiplyPoint3x4(v);
                corteza.Add(p);
                yMin = Mathf.Min(yMin, p.y);
            }
        }
        if (corteza.Count == 0) return;
        Vector2 eje = Vector2.zero; int n = 0;
        foreach (var p in corteza) if (p.y < yMin + 2f) { eje += new Vector2(p.x, p.z); n++; }
        eje /= Mathf.Max(n, 1);

        foreach (var r in rs)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var mesh = mf.sharedMesh;
            bool hojas = !esCorteza(r.sharedMaterial);
            var m = r.transform.localToWorldMatrix;
            var vs = mesh.vertices;
            var fase = hojas ? FasePorTarjeta(mesh) : null;
            var col = new Color[vs.Length];
            for (int i = 0; i < vs.Length; i++)
            {
                var p = m.MultiplyPoint3x4(vs[i]);
                float altura = Mathf.Clamp01((p.y - yMin) / AlturaViento);
                float afuera = Mathf.Clamp01(Vector2.Distance(new Vector2(p.x, p.z), eje) / RadioViento);
                col[i] = new Color(altura, hojas ? 1f : 0f, hojas ? fase[i] : 0f, afuera);
            }
            mesh.colors = col;
        }
    }

    // Una fase al azar (fija) por isla conexa de triangulos: cada tarjeta de hojas aletea a su ritmo.
    static float[] FasePorTarjeta(Mesh mesh)
    {
        int nv = mesh.vertexCount;
        var padre = new int[nv];
        for (int i = 0; i < nv; i++) padre[i] = i;
        int Raiz(int i) { while (padre[i] != i) { padre[i] = padre[padre[i]]; i = padre[i]; } return i; }
        var t = mesh.triangles;
        for (int i = 0; i < t.Length; i += 3)
        {
            int a = Raiz(t[i]);
            padre[Raiz(t[i + 1])] = a;
            padre[Raiz(t[i + 2])] = a;
        }
        var fase = new float[nv];
        for (int i = 0; i < nv; i++)
        {
            float h = Mathf.Sin(Raiz(i) * 12.9898f + 78.233f) * 43758.5453f;
            fase[i] = h - Mathf.Floor(h);
        }
        return fase;
    }

    // Unity importa el FBX de Blender con x_unity = -x_blender, y_unity = z_blender, z_unity = -y_blender
    // (medido: los bounds coinciden). Las coordenadas de objeto de Blender son el espacio de la malla.
    static Vector3 ABlender(Vector3 u) => new Vector3(-u.x, -u.z, u.y);

    // Lo mismo que hace Cycles en svm_node_tex_image_box para un eje.
    static Vector2 UvEje(Vector3 co, Vector3 n, int eje)
    {
        switch (eje)
        {
            case 0: return new Vector2(n.x < 0f ? 1f - co.y : co.y, co.z);
            case 1: return new Vector2(n.y > 0f ? 1f - co.x : co.x, co.z);
            default: return new Vector2(n.z > 0f ? 1f - co.y : co.y, co.x);
        }
    }

    internal static void UvBox(Mesh mesh, Vector3 escalaMapping)
    {
        var pos = new List<Vector3>(); mesh.GetVertices(pos);
        var nor = new List<Vector3>(); mesh.GetNormals(nor);
        if (nor.Count != pos.Count) { mesh.RecalculateNormals(); mesh.GetNormals(nor); }

        var nPos = new List<Vector3>(pos.Count * 2);
        var nNor = new List<Vector3>(pos.Count * 2);
        var nUv = new List<Vector2>(pos.Count * 2);
        var mapa = new Dictionary<long, int>();       // (vertice original, cara de la caja) -> vertice nuevo
        var subTris = new List<int[]>();

        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var tris = mesh.GetTriangles(s);
            var nuevos = new int[tris.Length];
            for (int t = 0; t < tris.Length; t += 3)
            {
                // Normal de la cara en espacio Blender: promedio de las normales de vertice (no depende del winding).
                Vector3 n = ABlender(nor[tris[t]] + nor[tris[t + 1]] + nor[tris[t + 2]]);
                Vector3 a = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
                int eje = a.x >= a.y && a.x >= a.z ? 0 : (a.y >= a.z ? 1 : 2);
                int cara = eje * 2 + (n[eje] < 0f ? 0 : 1);

                for (int k = 0; k < 3; k++)
                {
                    int v = tris[t + k];
                    long clave = (long)v * 6 + cara;
                    if (!mapa.TryGetValue(clave, out int nv))
                    {
                        nv = nPos.Count;
                        mapa[clave] = nv;
                        nPos.Add(pos[v]);
                        nNor.Add(nor[v]);
                        Vector3 co = Vector3.Scale(ABlender(pos[v]), escalaMapping);
                        nUv.Add(UvEje(co, n, eje));
                    }
                    nuevos[t + k] = nv;
                }
            }
            subTris.Add(nuevos);
        }

        mesh.Clear();
        mesh.indexFormat = nPos.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(nPos);
        mesh.SetNormals(nNor);
        mesh.SetUVs(0, nUv);
        mesh.subMeshCount = subTris.Count;
        for (int s = 0; s < subTris.Count; s++) mesh.SetTriangles(subTris[s], s);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
    }

    // ---------------------------------------------------------------- prefabs para el Terrain

    // El Terrain, con un FBX comun como prototipo, dibuja SOLO la malla de la raiz (las hojas son hijos y no
    // salian) y de lejos lo cambia por un billboard propio que en URP sale azul. Con un LODGroup en el prefab
    // dibuja todos los renderers y no hace billboard; el corte de lejos lo hace el LODGroup.
    internal const float CorteLod = 0.012f;   // alto relativo en pantalla (con lodBias 2): un arbol de 25 m se apaga a ~3.6 km

    // Datos del escaneo con el telefono (Scannable). Los textos son un primer borrador: revisarlos.
    class Especie { public string id, nombre, cientifico, descripcion; }
    static readonly Especie Coigue = new Especie
    {
        id = "coigue", nombre = "Coigüe de Magallanes", cientifico = "Nothofagus betuloides",
        descripcion = "Árbol siempreverde que domina los bosques húmedos de los canales patagónicos. Sus hojas " +
            "pequeñas, gruesas y de borde aserrado resisten el viento y la lluvia casi constante de la zona, y " +
            "puede superar los 25 metros de altura. Los kawésqar usaban su corteza para fabricar sus canoas " +
            "tradicionales.",
    };
    static readonly Especie Cipres = new Especie
    {
        id = "cipres", nombre = "Ciprés de las Guaitecas", cientifico = "Pilgerodendron uviferum",
        descripcion = "Es la conífera que crece más al sur del mundo. Vive en suelos anegados y turberas, donde " +
            "crece muy lento. Su madera es tan resistente a la humedad que fue explotada intensamente para " +
            "postes y tejuelas, y hoy es una especie protegida.",
    };

    [MenuItem("Terrenos/Sur/Arboles coigue y cipres al Terrain")]
    static void PrefabsAlTerrain()
    {
        var reemplazo = new Dictionary<GameObject, GameObject>();
        var cercanos = new List<(string, GameObject)>();
        foreach (var nombre in Modelos)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{Carpeta}/{nombre}.fbx");
            if (fbx == null) continue;
            var prefab = Prefab(nombre, fbx);
            reemplazo[fbx] = prefab;
            reemplazo[prefab] = prefab;
            cercanos.Add((nombre, prefab));
        }
        var lejanos = ArbolesLejanosSur.Crear(cercanos);

        foreach (var t in Terrain.activeTerrains)
        {
            var td = t.terrainData;
            var protos = new List<TreePrototype>(td.treePrototypes);
            bool tieneArboles = false;
            foreach (var p in protos)
                if (p.prefab != null && reemplazo.TryGetValue(p.prefab, out var nuevo)) { p.prefab = nuevo; tieneArboles = true; }
            if (!tieneArboles) continue;
            // Los lejanos se agregan como prototipos nuevos, para pintarlos con la herramienta de arboles.
            foreach (var l in lejanos)
                if (!protos.Exists(p => p.prefab == l)) protos.Add(new TreePrototype { prefab = l });
            Undo.RegisterCompleteObjectUndo(td, "Arboles coigue y cipres");
            td.treePrototypes = protos.ToArray();
            td.RefreshPrototypes();
            t.Flush();
            EditorUtility.SetDirty(td);
            Debug.Log($"{t.name}: arboles de coigue y cipres actualizados ({protos.Count} prototipos).");
        }
        AssetDatabase.SaveAssets();
    }

    // Arbol_<nombre>.prefab: raiz vacia con LODGroup, collider del tronco y Scannable, y el FBX como hijo,
    // corrido para que la base del tronco quede en el pivote (el del CoiguePeque esta a 12 m en el FBX).
    // El Terrain toma el CapsuleCollider de la raiz como collider del arbol; el Scannable lo lee AnimalScanner
    // del prefab cuando el rayo pega en un arbol del Terrain.
    static GameObject Prefab(string nombre, GameObject fbx)
    {
        string ruta = $"{Carpeta}/Arbol_{nombre}.prefab";
        var raiz = new GameObject("Arbol_" + nombre);
        var hijo = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        hijo.transform.SetParent(raiz.transform, false);

        var rs = hijo.GetComponentsInChildren<MeshRenderer>(true);
        var tronco = System.Array.Find(rs, r => r.sharedMaterial != null && r.sharedMaterial.name.StartsWith("M_Corteza"));
        if (tronco != null)
        {
            // Pivote = centro de la base del tronco (la caja entera se corre con las ramas inclinadas).
            var cap = ColliderTronco(raiz, tronco);
            hijo.transform.localPosition -= new Vector3(cap.center.x, 0f, cap.center.z);
            cap.center = new Vector3(0f, cap.center.y, 0f);
        }

        var esp = nombre.StartsWith("Cipre") ? Cipres : Coigue;
        var so = new SerializedObject(raiz.AddComponent<Scannable>());
        so.FindProperty("speciesId").stringValue = esp.id;
        so.FindProperty("displayName").stringValue = esp.nombre;
        so.FindProperty("scientificName").stringValue = esp.cientifico;
        so.FindProperty("description").stringValue = esp.descripcion;
        so.ApplyModifiedPropertiesWithoutUndo();

        var lod = raiz.AddComponent<LODGroup>();
        lod.SetLODs(new[] { new LOD(CorteLod, rs) });
        lod.RecalculateBounds();

        var prefab = PrefabUtility.SaveAsPrefabAsset(raiz, ruta);
        Object.DestroyImmediate(raiz);
        return prefab;
    }

    // Capsula vertical sobre el tronco, 8 m de alto para que el rayo del telefono le pegue aunque se mire un poco hacia arriba.
    const float AltoCollider = 8f;

    internal static CapsuleCollider ColliderTronco(GameObject raiz, MeshRenderer tronco)
    {
        var mesh = tronco.GetComponent<MeshFilter>().sharedMesh;
        var m = raiz.transform.worldToLocalMatrix * tronco.transform.localToWorldMatrix;
        var vs = mesh.vertices;
        float yMin = float.MaxValue;
        for (int i = 0; i < vs.Length; i++) { vs[i] = m.MultiplyPoint3x4(vs[i]); yMin = Mathf.Min(yMin, vs[i].y); }

        // Los troncos son low poly (el del CipreGrande no tiene ningun vertice entre 0.5 y 2.5 m): se mide en
        // los primeros 4 m, y la mediana deja afuera el ensanche de la base.
        Vector3 suma = Vector3.zero; int n = 0;
        foreach (var v in vs) if (v.y < yMin + 4f) { suma += v; n++; }
        Vector3 c = n > 0 ? suma / n : Vector3.zero;
        var radios = new List<float>();
        foreach (var v in vs) if (v.y < yMin + 4f) radios.Add(new Vector2(v.x - c.x, v.z - c.z).magnitude);
        radios.Sort();
        float r = radios.Count > 0 ? radios[radios.Count / 2] : 0.4f;

        var cap = raiz.AddComponent<CapsuleCollider>();
        cap.direction = 1;
        cap.radius = Mathf.Clamp(r, 0.15f, 1.5f);
        cap.height = AltoCollider;
        cap.center = new Vector3(c.x, yMin + AltoCollider * 0.5f, c.z);
        return cap;
    }

    // ---------------------------------------------------------------- materiales

    [MenuItem("Terrenos/Sur/Materiales coigue y cipres")]
    static void Materiales()
    {
        ImportarTextura($"{Carpeta}/bark_brown_02_diff_512.jpg", false, false);
        ImportarTextura($"{Carpeta}/bark_brown_02_nor_gl_512.exr", true, false);
        ImportarTextura($"{Carpeta}/japanese_sycamore_diff_512.jpg", false, false);
        ImportarTextura($"{Carpeta}/japanese_sycamore_nor_gl_512.exr", true, false);
        ImportarTextura($"{Carpeta}/Hojas_Cipres.png", false, true);
        ImportarTextura($"{Carpeta}/Hojas_Coigue.png", false, true);

        // Colores de Blender (lineales). El del coigue ya con la saturacion x0.8 del nodo HSV aplicada.
        var mats = new Dictionary<string, Material>
        {
            ["M_Corteza_Cipre"] = Corteza("M_Corteza_Cipre", "bark_brown_02_diff_512.jpg", "bark_brown_02_nor_gl_512.exr"),
            ["M_Corteza_Coigue"] = Corteza("M_Corteza_Coigue", "japanese_sycamore_diff_512.jpg", "japanese_sycamore_nor_gl_512.exr"),
            ["M_Hojas_Cipres"] = Hojas("M_Hojas_Cipres", "Hojas_Cipres.png", new Color(0.0167f, 0.5762f, 0f)),
            ["M_HojasCoigue"] = Hojas("M_HojasCoigue", "Hojas_Coigue.png", new Color(0.461f, 0.890f, 0.391f)),
        };
        AssetDatabase.SaveAssets();

        foreach (var nombre in Modelos)
        {
            string ruta = $"{Carpeta}/{nombre}.fbx";
            if (!(AssetImporter.GetAtPath(ruta) is ModelImporter mi)) continue;
            foreach (var m in mats)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.Key), m.Value);
            mi.SaveAndReimport();
        }
        Debug.Log("Materiales de coigue y cipres asignados.");
    }

    static Material Corteza(string nombre, string color, string normal)
    {
        var m = CargarOCrear(nombre, out bool nuevo);
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Carpeta}/{color}"));
        m.SetColor("_BaseColor", Color.white);
        m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Carpeta}/{normal}"));
        m.SetFloat("_BumpScale", 1f);
        m.SetFloat("_Smoothness", 0.1f);        // la rugosidad de Blender anda en 0.9
        m.SetFloat("_AlphaClip", 0f);
        m.SetFloat("_Cull", (float)CullMode.Back);
        return Guardar(m, nombre, nuevo);
    }

    static Material Hojas(string nombre, string textura, Color verdeLineal)
    {
        var m = CargarOCrear(nombre, out bool nuevo);
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Carpeta}/{textura}"));
        m.SetColor("_BaseColor", verdeLineal.gamma);
        m.SetTexture("_BumpMap", null);
        m.SetFloat("_Smoothness", 0f);          // Roughness 1 en Blender
        m.SetFloat("_AlphaClip", 1f);
        m.SetFloat("_Cutoff", 0.5f);
        m.SetFloat("_Cull", (float)CullMode.Off);   // Blender las dibuja por las dos caras
        return Guardar(m, nombre, nuevo);
    }

    static Material CargarOCrear(string nombre, out bool nuevo)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>($"{CarpetaMat}/{nombre}.mat");
        nuevo = m == null;
        // URP/Lit con viento (Assets/Shaders/ArbolViento.shader): mismas propiedades e inspector.
        var shader = Shader.Find("Terrenos/ArbolViento");
        if (nuevo) m = new Material(shader);
        else m.shader = shader;
        return m;
    }

    static Material Guardar(Material m, string nombre, bool nuevo)
    {
        m.SetFloat("_WorkflowMode", 1f);   // metalico
        m.SetFloat("_Metallic", 0f);
        m.enableInstancing = true;
        BaseShaderGUI.SetMaterialKeywords(m, LitGUI.SetMaterialKeywords);
        if (nuevo)
        {
            if (!AssetDatabase.IsValidFolder(CarpetaMat)) AssetDatabase.CreateFolder(Carpeta, "Materials");
            AssetDatabase.CreateAsset(m, $"{CarpetaMat}/{nombre}.mat");
        }
        else EditorUtility.SetDirty(m);
        return m;
    }

    static void ImportarTextura(string ruta, bool normal, bool hojas)
    {
        if (!(AssetImporter.GetAtPath(ruta) is TextureImporter ti)) { Debug.LogWarning("Falta " + ruta); return; }
        ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        ti.sRGBTexture = !normal;
        ti.maxTextureSize = 512;
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.alphaSource = hojas ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        ti.alphaIsTransparency = hojas;
        ti.mipMapsPreserveCoverage = hojas;     // sin esto el alfa se lava en los mips y la copa se pela de lejos
        ti.alphaTestReferenceValue = 0.5f;
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.SaveAndReimport();
    }
}
