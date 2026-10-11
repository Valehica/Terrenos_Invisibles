# Pasarela dispareja (Blender, via exec). Parte siempre de la malla de respaldo "<malla>_base"
# (cajas rectas), subdivide cada caja a lo largo, rehace UVs/material con madera_pasarela.py
# (GEN_TEX=False) y deforma cada pieza: comba, torsion, curva lateral, inclinacion, desfase
# y grosor/ancho distintos. Re-correr con otro SEED da otra pasarela; FUERZA escala todo.
# Uso: exec(open(ruta).read(), {"OBJ": "Cube", "MADERA": ruta_madera_pasarela, "SEED": 3})
import bpy, bmesh, os, random, math
import numpy as np
from mathutils import Matrix, Vector

OBJ = globals().get("OBJ", "Cube")
SEED = globals().get("SEED", 3)
FUERZA = globals().get("FUERZA", 1.0)
MADERA = globals().get("MADERA", os.path.join(os.path.dirname(globals().get("__file__", "")), "madera_pasarela.py"))
SEG_M = 0.6                                   # largo maximo de cada tramo al subdividir

obj = bpy.data.objects[OBJ]
if obj.mode != 'OBJECT':
    bpy.context.view_layer.objects.active = obj; bpy.ops.object.mode_set(mode='OBJECT')
name = obj.data.name
base = bpy.data.meshes.get(name + "_base")
if base is None:                              # respaldo de las cajas originales
    base = obj.data.copy(); base.name = name + "_base"; base.use_fake_user = True
old = obj.data
me = base.copy(); me.use_fake_user = False
obj.data = me
if old.users == 0: bpy.data.meshes.remove(old)
me.name = name

def islas(bm):
    seen, out = set(), []
    for f0 in bm.faces:
        if f0.index in seen: continue
        comp, stack = [], [f0]
        while stack:
            g = stack.pop()
            if g.index in seen: continue
            seen.add(g.index); comp.append(g)
            stack += [h for e in g.edges for h in e.link_faces if h.index not in seen]
        out.append(comp)
    return out

def ejes(verts):
    V = np.array([v.co[:] for v in verts]); lo, hi = V.min(0), V.max(0)
    dim = hi - lo; L = int(np.argmax(dim)); b = 2 if L != 2 else 1
    a = [i for i in range(3) if i not in (L, b)][0]
    return (lo + hi) / 2, dim, L, a, b

# 1) subdividir a lo largo (cajas todavia rectas)
bm = bmesh.new(); bm.from_mesh(me)
for comp in islas(bm):
    vs = {v for g in comp for v in g.verts}
    ctr, dim, L, a, b = ejes(vs)
    eds = {e for g in comp for e in g.edges}
    largas = [e for e in eds if abs((e.verts[0].co - e.verts[1].co)[L]) > dim[L] * 0.9]
    cuts = max(1, int(math.ceil(dim[L] / SEG_M)) - 1)
    bmesh.ops.subdivide_edges(bm, edges=largas, cuts=cuts, use_grid_fill=True)
bm.to_mesh(me); bm.free()

# 2) UVs, atributos, material y bisel sobre la geometria recta
g = {"OBJ": OBJ, "GEN_TEX": False, "__name__": "madera"}
exec(open(MADERA, encoding="utf-8").read(), g)

# 3) deformar cada pieza
pr = random.Random(SEED)
U = lambda r: pr.uniform(-r, r) * FUERZA
bm = bmesh.new(); bm.from_mesh(me)
for comp in islas(bm):
    vs = list({v for g_ in comp for v in g_.verts})
    ctr, dim, L, a, b = ejes(vs)
    poste = (L == 2)
    tabla = (not poste) and dim[b] > 0.5            # tablones del piso (los mas gruesos)
    if poste:
        bow, swp, tw = U(0.03), U(0.03), math.radians(U(2.0))
        rot = Matrix.Rotation(math.radians(U(2.5)), 4, 'X') @ Matrix.Rotation(math.radians(U(2.5)), 4, 'Y') \
            @ Matrix.Rotation(math.radians(U(7.0)), 4, 'Z')
        off = Vector((U(0.04), U(0.04), U(0.12)))
        sa, sb = 1 + U(0.08), 1 + U(0.08)
    else:
        bow = U(0.045) if tabla else U(0.02)          # comba vertical
        swp = U(0.04)                                 # curva lateral
        tw = math.radians(U(3.0) if tabla else U(1.5))  # torsion de punta a punta
        ang = lambda r: math.radians(U(r))
        ejes_v = "XYZ"
        rot = Matrix.Rotation(ang(1.2), 4, ejes_v[a]) @ Matrix.Rotation(ang(1.0 if tabla else 0.5), 4, ejes_v[L]) \
            @ Matrix.Rotation(ang(1.5), 4, ejes_v[b])
        off = Vector((0, 0, 0)); off[L] = U(0.10); off[a] = U(0.035); off[b] = U(0.025) if tabla else U(0.01)
        sa, sb = 1 + U(0.06), 1 + U(0.07)
    # grosor/ancho y torsion se aplican en coordenadas de la pieza; la base de los tablones queda fija
    for v in vs:
        p = np.array(v.co[:]) - ctr
        t = p[L] / (dim[L] / 2)                       # -1..1 a lo largo
        if poste:
            p[a] *= sa; p[b] *= sb
        else:
            p[a] *= sa; p[b] = (p[b] + dim[b] / 2) * sb - dim[b] / 2
        c, s = math.cos(tw * t), math.sin(tw * t)
        pa, pb = p[a], p[b]
        p[a], p[b] = c * pa - s * pb, s * pa + c * pb
        p[b] += bow * (1 - t * t)
        p[a] += swp * (1 - t * t)
        if poste:                                     # el pie del poste no se mueve en altura
            p[L] += off[2] * (t + 1) / 2
        q = rot @ Vector(p.tolist())
        if poste: q += Vector((off[0], off[1], 0))
        else: q += off
        v.co = Vector(ctr.tolist()) + q
    # cantos irregulares: jitter minimo por vertice
    for v in vs:
        v.co += Vector((U(0.006), U(0.006), U(0.006)))
bm.normal_update(); bm.to_mesh(me); bm.free()
me.update()
result = {"ok": True, "verts": len(me.vertices), "base": base.name}
