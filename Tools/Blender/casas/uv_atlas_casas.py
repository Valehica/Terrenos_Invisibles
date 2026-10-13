# UV de atlas por diseño de casa: una sola textura para todas las piezas de la casa.
# - Islas = caras coplanares conectadas, proyectadas en metros reales (respeta la
#   escala sin aplicar de los objetos), con "arriba" de la textura = arriba del mundo.
# - Caras que nunca se ven (base apoyada en el suelo, tope de muros bajo el techo)
#   se encogen para no gastar textura.
# - Se empaquetan todas las piezas del diseño juntas. Las variantes V2/V3 comparten
#   la malla con V1, así que heredan las mismas UV.
# Sobrescribe la capa "UVMap" (eran las UV por defecto de los cubos).
import bpy, bmesh
from mathutils import Vector

DISENOS = ["Casa1", "Casa1.001", "Casa3", "Casa4"]
MARGEN = 0.003          # fracción del atlas (~6 px a 2048)
ESCALA_OCULTA = 0.05


def islas(bm, mw3):
    normales = {f: (mw3 @ f.normal).normalized() for f in bm.faces}
    vistas = set(); res = []
    for f in bm.faces:
        if f in vistas: continue
        pila = [f]; isla = []; vistas.add(f)
        while pila:
            g = pila.pop(); isla.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h not in vistas and normales[h].dot(normales[g]) > 0.9998:
                        vistas.add(h); pila.append(h)
        res.append((isla, normales[f]))
    return res


def ejes(n):
    ref = Vector((0, 0, 1)) if abs(n.z) < 0.95 else Vector((0, 1, 0))
    b = (ref - n * n.dot(ref)).normalized()     # "arriba" de la textura
    t = b.cross(n)                              # derecha (t x b = n: sin espejar)
    return t, b


def oculta(ob, f, n, zmin):
    if n.z < -0.99 and all(abs((ob.matrix_world @ v.co).z - zmin) < 0.02 for v in f.verts):
        return True
    if ob.name.split('.')[0] == 'Cuerpo' and f.material_index == 0 and 0.3 < n.z < 0.95:
        return True
    return False


def desenvolver(ob):
    me = ob.data
    mw = ob.matrix_world
    mw3 = mw.to_3x3()
    bm = bmesh.new(); bm.from_mesh(me)
    uv = bm.loops.layers.uv.get('UVMap') or bm.loops.layers.uv.new('UVMap')
    zmin = min((mw @ v.co).z for v in bm.verts)
    for isla, n in islas(bm, mw3):
        t, b = ejes(n)
        k = ESCALA_OCULTA if all(oculta(ob, f, n, zmin) for f in isla) else 1.0
        for f in isla:
            for l in f.loops:
                p = mw3 @ l.vert.co
                l[uv].uv = (p.dot(t) * k, p.dot(b) * k)
    bm.to_mesh(me); bm.free()
    if me.uv_layers.get('UVMap'):
        me.uv_layers.active = me.uv_layers['UVMap']
        me.uv_layers['UVMap'].active_render = True


def atlas(col_name):
    obs = [o for o in bpy.data.collections[col_name].objects if o.type == 'MESH']
    for o in obs:
        desenvolver(o)
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    for o in bpy.context.selected_objects: o.select_set(False)
    for o in obs: o.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(udim_source='CLOSEST_UDIM', rotate=True, rotate_method='AXIS_ALIGNED',
                            scale=True, merge_overlap=False, margin_method='FRACTION',
                            margin=MARGEN, shape_method='CONCAVE')
    bpy.ops.object.mode_set(mode='OBJECT')
    return [o.name for o in obs]


def atlas_todos():
    return {c: atlas(c) for c in DISENOS}


if __name__ == "__main__":
    print(atlas_todos())
