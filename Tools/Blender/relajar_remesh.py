# Arregla caras dobladas/concavas que deja QRemeshify en las bifurcaciones.
# Relaja (Laplaciano) las zonas malas + 6 anillos alrededor y reproyecta cada
# vertice sobre la malla original por raycast a lo largo de su normal.
# Uso: objeto remesheado activo, cambiar ORIGEN si hace falta, Run Script.
import bpy, bmesh, math
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ORIGEN = "Arbol"      # malla de alta resolucion
ANILLOS = 6
ITER = 150
LAMBDA = 0.6
MAX_RAY = 0.15        # m

ob = bpy.context.active_object
bpy.ops.object.mode_set(mode='OBJECT')
bk = ob.copy(); bk.data = ob.data.copy(); bk.name = ob.name + "_respaldo_remesh"
for c in ob.users_collection: c.objects.link(bk)
bk.hide_set(True); bk.hide_render = True

bvh = BVHTree.FromObject(bpy.data.objects[ORIGEN], bpy.context.evaluated_depsgraph_get())

def cara_mala(f):
    n = f.normal; vs = f.verts; k = len(vs); concava = False; angs = []
    for i in range(k):
        e1 = vs[i-1].co - vs[i].co; e2 = vs[(i+1) % k].co - vs[i].co
        if e1.length < 1e-9 or e2.length < 1e-9: return True
        angs.append(math.degrees(e1.angle(e2)))
        if e2.cross(e1).dot(n) < 0: concava = True
    flip = sum(1 for e in f.edges for g in e.link_faces if g != f and g.normal.dot(n) < -0.3)
    return concava or flip >= 2 or min(angs) < 20 or max(angs) > 160

bm = bmesh.new(); bm.from_mesh(ob.data); bm.normal_update()
malas = [f for f in bm.faces if cara_mala(f)]
zona = set(v for f in malas for v in f.verts)
for _ in range(ANILLOS):
    zona |= set(e.other_vert(v) for v in list(zona) for e in v.link_edges)

for _ in range(ITER):
    bm.normal_update(); nuevo = {}
    for v in zona:
        c = sum((e.other_vert(v).co for e in v.link_edges), Vector()) / len(v.link_edges)
        p = v.co + (c - v.co) * LAMBDA; n = v.normal; mejor = None
        for d in (n, -n):
            hit = bvh.ray_cast(p, d, MAX_RAY)
            if hit[0] is not None and (mejor is None or hit[3] < mejor[1]): mejor = (hit[0], hit[3])
        nuevo[v] = mejor[0] if mejor else bvh.find_nearest(p)[0]
    for v, p in nuevo.items(): v.co = p

bm.normal_update()
print("caras malas:", len(malas), "->", sum(1 for f in bm.faces if cara_mala(f)))
bm.to_mesh(ob.data); ob.data.update(); bm.free()
