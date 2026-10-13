# Exporta cada colección de casa a su propio FBX para Unity.
# - Cada casa se centra en XY y apoya su base en z=0 solo durante la exportación
#   (después vuelve a su lugar en la escena): el pivote del prefab queda abajo al centro.
# - Para Unity cada casa lleva UN solo material, "M_<Casa>_<V>", porque todas sus
#   piezas comparten el mismo atlas horneado (hornear_casas.py). Los materiales
#   procedurales de Blender se restauran al terminar.
# - Se escribe en CARPETA y se copia a la carpeta de Unity (los .meta no se tocan).
import bpy, os, shutil
from mathutils import Vector

CARPETA = r"C:\Users\valeh\OneDrive\Desktop\Personal\Memoria\Concept\Sur\Pasarela\Casas\Fbx"
UNITY = r"C:\Users\valeh\Terrenos_Invisibles\Assets\Models\Sur\Casas"
ESCALA = 1.5  # multiplicador de tamaño al exportar (la escena de Blender no cambia)

# colección -> nombre de archivo (Casa1.001 es el segundo diseño: Casa2)
DISENOS = {"Casa1": "Casa1", "Casa1.001": "Casa2", "Casa3": "Casa3", "Casa4": "Casa4"}


def colecciones():
    for col, nombre in DISENOS.items():
        yield col, f"{nombre}_V1"
        for v in ("V2", "V3"):
            yield f"{col}_{v}", f"{nombre}_{v}"


def un_material(obs, nombre):
    """Deja temporalmente las mallas con un solo material. Devuelve cómo restaurar."""
    mat = bpy.data.materials.new(nombre)
    guard = {}
    for o in obs:
        me = o.data
        if me in guard: continue
        idx = [0] * len(me.polygons)
        me.polygons.foreach_get('material_index', idx)
        guard[me] = (list(me.materials), idx)
        me.materials.clear()
        me.materials.append(mat)
        me.polygons.foreach_set('material_index', [0] * len(me.polygons))
    return mat, guard


def restaurar(mat, guard):
    for me, (mats, idx) in guard.items():
        me.materials.clear()
        for m in mats: me.materials.append(m)
        me.polygons.foreach_set('material_index', idx)
        me.update()
    bpy.data.materials.remove(mat)


def exportar(col_name, archivo):
    col = bpy.data.collections[col_name]
    obs = [o for o in col.objects if o.type == 'MESH']
    pts = [o.matrix_world @ Vector(c) for o in obs for c in o.bound_box]
    off = Vector(((min(p.x for p in pts) + max(p.x for p in pts)) / 2,
                  (min(p.y for p in pts) + max(p.y for p in pts)) / 2,
                  min(p.z for p in pts)))
    locs = {o: o.location.copy() for o in obs}
    mat, guard = un_material(obs, "M_" + archivo)
    try:
        for o in obs:
            o.location -= off
        bpy.context.view_layer.update()
        for o in bpy.context.selected_objects:
            o.select_set(False)
        for o in obs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = obs[0]
        ruta = os.path.join(CARPETA, archivo + ".fbx")
        bpy.ops.export_scene.fbx(
            filepath=ruta, use_selection=True, object_types={'MESH'},
            global_scale=ESCALA, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
            axis_forward='-Z', axis_up='Y', bake_space_transform=True,
            use_mesh_modifiers=True, mesh_smooth_type='FACE',
            use_custom_props=True, add_leaf_bones=False, bake_anim=False,
            path_mode='AUTO', embed_textures=False)
    finally:
        for o, loc in locs.items():
            o.location = loc
        restaurar(mat, guard)
        bpy.context.view_layer.update()
    if UNITY and os.path.isdir(UNITY):
        shutil.copy2(ruta, os.path.join(UNITY, archivo + ".fbx"))
    return ruta, [o.name for o in obs]


def exportar_todo():
    os.makedirs(CARPETA, exist_ok=True)
    return {archivo: exportar(col, archivo) for col, archivo in colecciones()}


if __name__ == "__main__":
    print(exportar_todo())
