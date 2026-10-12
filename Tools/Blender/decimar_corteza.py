# Decima la corteza de los arboles de Sur para su LOD1 (Assets/Editor/ArbolesLodSur.cs).
# Lee Tools/ArbolesSur/<prefab>_corteza.obj (que exporta Unity en sus propias coordenadas) y escribe
# <prefab>_corteza_lod1.json con posiciones, UV y triangulos en esas mismas coordenadas y con el mismo orden de
# vertices (Unity lo usa para la normal). Los objetos temporales se borran al terminar, pero conviene correrlo
# sin abrir ningun .blend:
#   blender -b --factory-startup --python-expr "exec(open(r'...\decimar_corteza.py').read(), {'CARPETA': r'...'})"

import bpy, os, json, glob

CARPETA = globals().get("CARPETA", r"C:\Users\valeh\Terrenos_Invisibles\Tools\ArbolesSur")
# Fraccion de triangulos que queda. El platano trae 10 500 en la corteza; los arcoiris ~1 500.
RATIOS = {"Platano": 0.15, "Haya": 0.30, "Arcoiris": 0.45}


def leer_obj(ruta):
    v, vt, caras = [], [], []
    with open(ruta) as f:
        for linea in f:
            p = linea.split()
            if not p: continue
            if p[0] == "v": v.append(tuple(map(float, p[1:4])))
            elif p[0] == "vt": vt.append(tuple(map(float, p[1:3])))
            elif p[0] == "f":
                caras.append([tuple(int(x) - 1 for x in q.split("/")[:2]) for q in p[1:]])
    return v, vt, caras


escena = bpy.context.scene
resumen = {}
try:
    for ruta in sorted(glob.glob(os.path.join(CARPETA, "*_corteza.obj"))):
        nombre = os.path.basename(ruta)[:-len("_corteza.obj")]
        ratio = next((r for k, r in RATIOS.items() if k in nombre), 0.3)
        v, vt, caras = leer_obj(ruta)

        me = bpy.data.meshes.new("_tmp_" + nombre)
        me.from_pydata(v, [], [[c[0] for c in cara] for cara in caras])
        uv = me.uv_layers.new(name="UV")
        li = 0
        for cara in caras:
            for c in cara:
                uv.data[li].uv = vt[c[1]]
                li += 1
        obj = bpy.data.objects.new("_tmp_" + nombre, me)
        escena.collection.objects.link(obj)
        mod = obj.modifiers.new("Decimar", 'DECIMATE')
        mod.decimate_type = 'COLLAPSE'
        mod.ratio = ratio
        mod.use_collapse_triangulate = True

        dg = bpy.context.evaluated_depsgraph_get()
        dg.update()
        ev = obj.evaluated_get(dg)
        m2 = ev.to_mesh()
        m2.calc_loop_triangles()
        uvd = m2.uv_layers[0].data
        P, U, T = [], [], []
        for t in m2.loop_triangles:
            for l in t.loops:
                co = m2.vertices[m2.loops[l].vertex_index].co
                P += [round(co.x, 5), round(co.y, 5), round(co.z, 5)]
                U += [round(uvd[l].uv.x, 5), round(uvd[l].uv.y, 5)]
                T.append(len(T))
        ev.to_mesh_clear()
        with open(os.path.join(CARPETA, nombre + "_corteza_lod1.json"), "w") as f:
            json.dump({"v": P, "uv": U, "t": T}, f, separators=(",", ":"))
        resumen[nombre] = {"antes": len(caras), "despues": len(T) // 3, "ratio": ratio}

        bpy.data.objects.remove(obj)
        bpy.data.meshes.remove(me)
finally:
    for o in [o for o in bpy.data.objects if o.name.startswith("_tmp_")]:
        bpy.data.objects.remove(o)

result = resumen
