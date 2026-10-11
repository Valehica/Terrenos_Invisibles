# Pasarela a lo largo de una curva (Blender, via exec). Crea el grupo de Geometry Nodes "Pasarela en Curva"
# y lo pone como modificador en la curva. Copia el modulo (objeto con la pasarela, Y = direccion de avance,
# X = ancho, Z = arriba) una vez tras otra a lo largo de la curva; se actualiza en vivo al editarla.
# Cada pieza del modulo (isla de malla) se coloca rigida en su punto de la curva:
#   tablones -> siguen tangente y pendiente; pilotes -> verticales; vigas (largas en Y) -> se doblan con la curva.
# Cada copia cambia: giro/inclinacion leve por pieza, desfase de la textura y tono (madera_var/madera_var2).
# Uso: exec(open(ruta).read(), {"CURVA": "BezierCurve", "MODULO": "PasarelaBase"})
import bpy, math
import numpy as np

CURVA = globals().get("CURVA", "BézierCurve")
MODULO = globals().get("MODULO", "PasarelaBase")
GRUPO = "Pasarela en Curva"

mod_obj = bpy.data.objects[MODULO]
V = np.empty(len(mod_obj.data.vertices) * 3); mod_obj.data.vertices.foreach_get("co", V)
V = V.reshape(-1, 3)
largo_def = float(V[:, 1].max() - V[:, 1].min()) + 0.15     # largo del modulo + separacion entre tablones

ng = bpy.data.node_groups.get(GRUPO)
if ng is None:
    ng = bpy.data.node_groups.new(GRUPO, 'GeometryNodeTree')
ng.nodes.clear(); ng.interface.clear()
ng.is_modifier = True
S = ng.interface.new_socket
S("Curva", in_out='INPUT', socket_type='NodeSocketGeometry')
s_mod = S("Modulo", in_out='INPUT', socket_type='NodeSocketObject')
s_lar = S("Largo modulo", in_out='INPUT', socket_type='NodeSocketFloat')
s_lar.default_value = largo_def; s_lar.min_value = 0.5
s_aju = S("Ajustar al largo", in_out='INPUT', socket_type='NodeSocketBool'); s_aju.default_value = True
s_var = S("Variacion", in_out='INPUT', socket_type='NodeSocketFloat'); s_var.default_value = 1.0; s_var.min_value = 0.0
s_sem = S("Semilla", in_out='INPUT', socket_type='NodeSocketInt'); s_sem.default_value = 0
S("Geometry", in_out='OUTPUT', socket_type='NodeSocketGeometry')

N_ = ng.nodes.new; Lk = ng.links.new
_x = [0]
def node(t, x=None, y=0, **props):
    n = N_(t)
    for k, v in props.items(): setattr(n, k, v)
    n.location = (x if x is not None else _x[0], y)
    return n
def sk(n, name, out=False, i=0):            # socket habilitado por nombre (i = n-esimo con ese nombre)
    return [s for s in (n.outputs if out else n.inputs) if s.name == name and s.enabled][i]
def con(a, b):
    if isinstance(a, (int, float, bool)): b.default_value = a
    elif isinstance(a, tuple): b.default_value = a
    else: Lk(a, b)
def fm(op, a, b=None, x=0, y=0):              # math float
    n = node("ShaderNodeMath", x, y, operation=op)
    con(a, n.inputs[0])
    if b is not None: con(b, n.inputs[1])
    return n.outputs[0]
def vm(op, a, b=None, x=0, y=0, out="Vector"):  # vector math
    n = node("ShaderNodeVectorMath", x, y, operation=op)
    con(a, n.inputs[0])
    if b is not None:
        if op == 'SCALE': con(b, n.inputs["Scale"])
        else: con(b, n.inputs[1])
    return sk(n, out, True)
def sep(v, x=0, y=0):
    n = node("ShaderNodeSeparateXYZ", x, y); con(v, n.inputs[0]); return n.outputs
def comb(a, b, c, x=0, y=0):
    n = node("ShaderNodeCombineXYZ", x, y)
    for i, s in enumerate((a, b, c)): con(s, n.inputs[i])
    return n.outputs[0]
def switch(t, cond, f, tr, x=0, y=0):
    n = node("GeometryNodeSwitch", x, y, input_type=t)
    con(cond, sk(n, "Switch")); con(f, sk(n, "False")); con(tr, sk(n, "True")); return n.outputs[0]
def cmp(a, b, op='GREATER_THAN', x=0, y=0):
    n = node("FunctionNodeCompare", x, y, data_type='FLOAT', operation=op)
    con(a, sk(n, "A")); con(b, sk(n, "B")); return n.outputs[0]
def band(a, b, x=0, y=0):
    n = node("FunctionNodeBooleanMath", x, y, operation='AND'); con(a, n.inputs[0]); con(b, n.inputs[1]); return n.outputs[0]
def rnd(t, mn, mx, ident, seed_off, x=0, y=0):
    n = node("FunctionNodeRandomValue", x, y, data_type=t)
    con(mn, sk(n, "Min")); con(mx, sk(n, "Max")); con(ident, sk(n, "ID"))
    con(fm('ADD', gi.outputs["Semilla"], seed_off, x - 150, y - 120), sk(n, "Seed"))
    return sk(n, "Value", True)
def acc(t, val, grp, x=0, y=0):
    n = node("GeometryNodeAccumulateField", x, y, data_type=t, domain='POINT')
    con(val, sk(n, "Value")); con(grp, sk(n, "Group ID")); return sk(n, "Total", True)

gi = node("NodeGroupInput", -2600, 0)
go = node("NodeGroupOutput", 2600, 0)

# --- cuantos modulos y cada cuanto ---
oi = node("GeometryNodeObjectInfo", -2400, 300, transform_space='ORIGINAL')
Lk(gi.outputs["Modulo"], oi.inputs["Object"])
G = oi.outputs["Geometry"]
cl = node("GeometryNodeCurveLength", -2400, 600); Lk(gi.outputs["Curva"], cl.inputs[0]); Lc = cl.outputs[0]
ratio = fm('DIVIDE', Lc, gi.outputs["Largo modulo"], -2200, 600)
n_aj = fm('MAXIMUM', fm('ROUND', ratio, None, -2000, 700), 1.0, -1800, 700)
n_fl = fm('MAXIMUM', fm('FLOOR', ratio, None, -2000, 550), 1.0, -1800, 550)
Nmod = switch('FLOAT', gi.outputs["Ajustar al largo"], n_fl, n_aj, -1600, 650)
paso = switch('FLOAT', gi.outputs["Ajustar al largo"], gi.outputs["Largo modulo"], fm('DIVIDE', Lc, Nmod, -1800, 850), -1600, 850)
bb = node("GeometryNodeBoundBox", -2200, 150); Lk(G, bb.inputs["Geometry"])
y0 = sep(bb.outputs["Min"], -2000, 150)[1]

# --- N copias, cada una sabe su indice ---
g2i = node("GeometryNodeGeometryToInstance", -2200, 350); Lk(G, g2i.inputs[0])
dup = node("GeometryNodeDuplicateElements", -2000, 350, domain='INSTANCE')
Lk(g2i.outputs[0], dup.inputs["Geometry"]); con(Nmod, dup.inputs["Amount"])
st = node("GeometryNodeStoreNamedAttribute", -1800, 350, data_type='INT', domain='INSTANCE')
Lk(dup.outputs["Geometry"], sk(st, "Geometry")); sk(st, "Name").default_value = "modulo"
Lk(dup.outputs["Duplicate Index"], sk(st, "Value"))
real = node("GeometryNodeRealizeInstances", -1600, 350); Lk(st.outputs[0], real.inputs[0])

# --- por pieza: centro, ejes y tipo ---
isl = node("GeometryNodeInputMeshIsland", -1600, -300).outputs["Island Index"]
P = node("GeometryNodeInputPosition", -1600, -100).outputs[0]
cnt = acc('FLOAT', 1.0, isl, -1400, -500)
C = vm('SCALE', acc('FLOAT_VECTOR', P, isl, -1400, -100), fm('DIVIDE', 1.0, cnt, -1200, -500), -1200, -100)
D = vm('SUBTRACT', P, C, -1000, -100)
var3 = vm('SCALE', acc('FLOAT_VECTOR', vm('MULTIPLY', D, D, -1000, -300), isl, -800, -300),
          fm('DIVIDE', 1.0, cnt, -800, -500), -600, -300)
sx, sy, sz = sep(var3, -400, -300)
es_poste = band(cmp(sz, sx, x=-200, y=-250), cmp(sz, sy, x=-200, y=-400), 0, -300)
es_viga = band(cmp(sy, sx, x=-200, y=-550), cmp(sy, sz, x=-200, y=-700), 0, -600)
modn = node("GeometryNodeInputNamedAttribute", -1600, -700, data_type='INT')
modn.inputs["Name"].default_value = "modulo"; m = modn.outputs["Attribute"]
Cx, Cy, Cz = sep(C, -1000, -800)
Px, Py, Pz = sep(P, -1000, -1000)
mp = fm('MULTIPLY', m, paso, -800, -900)
s_rig = fm('ADD', fm('SUBTRACT', Cy, y0, -800, -800), mp, -600, -800)
s_bend = fm('ADD', fm('SUBTRACT', Py, y0, -800, -1000), mp, -600, -1000)
s = switch('FLOAT', es_viga, s_rig, s_bend, -400, -900)
s = fm('MINIMUM', fm('MAXIMUM', s, 0.0, -250, -900), Lc, -100, -900)

# --- marco de la curva en ese punto ---
sc = node("GeometryNodeSampleCurve", 100, -900, mode='LENGTH', use_all_curves=True)
Lk(gi.outputs["Curva"], sc.inputs["Curves"]); con(s, sk(sc, "Length"))
Pc = sc.outputs["Position"]
F = vm('NORMALIZE', sc.outputs["Tangent"], None, 300, -1000)
Zw = (0.0, 0.0, 1.0)
R = vm('NORMALIZE', vm('CROSS_PRODUCT', F, Zw, 450, -1100), None, 600, -1100)
U = vm('CROSS_PRODUCT', R, F, 750, -1150)
Fx, Fy, _ = sep(F, 450, -1300)
Fh = vm('NORMALIZE', comb(Fx, Fy, 0.0, 600, -1300), None, 750, -1300)
Fsel = switch('VECTOR', es_poste, F, Fh, 950, -1050)
Usel = switch('VECTOR', es_poste, U, Zw, 950, -1200)

# --- variacion por pieza (ID = isla, unica en cada copia) ---
Vr = gi.outputs["Variacion"]
def rv(mn, mx, off, y):
    return fm('MULTIPLY', rnd('FLOAT', mn, mx, isl, off, 100, y), Vr, 300, y)
yaw = rv(-math.radians(1.5), math.radians(1.5), 11, 300)
roll = rv(-math.radians(1.0), math.radians(1.0), 12, 150)
dlat = rv(-0.03, 0.03, 13, 0)
dz = rv(-0.015, 0.015, 14, -150)
r1 = node("ShaderNodeVectorRotate", 500, 250, rotation_type='Z_AXIS'); Lk(D, r1.inputs["Vector"]); Lk(yaw, r1.inputs["Angle"])
r2 = node("ShaderNodeVectorRotate", 700, 250, rotation_type='Y_AXIS'); Lk(r1.outputs[0], r2.inputs["Vector"]); Lk(roll, r2.inputs["Angle"])
o_rig = vm('ADD', comb(fm('ADD', Cx, dlat, 500, 0), 0.0, fm('ADD', Cz, dz, 500, -150), 700, 0), r2.outputs[0], 900, 100)
o_beam = comb(Px, 0.0, Pz, 900, -150)
o = switch('VECTOR', es_viga, o_rig, o_beam, 1100, 0)
ox, oy, oz = sep(o, 1250, 0)
pos = vm('ADD', Pc, vm('ADD', vm('SCALE', R, ox, 1400, -300),
          vm('ADD', vm('SCALE', Fsel, oy, 1400, -450), vm('SCALE', Usel, oz, 1400, -600), 1550, -500), 1700, -400), 1850, -300)
setp = node("GeometryNodeSetPosition", 1700, 350)
Lk(real.outputs[0], setp.inputs["Geometry"]); Lk(pos, setp.inputs["Position"])

# --- textura y tono distintos por pieza ---
uvn = node("GeometryNodeInputNamedAttribute", 1700, 700, data_type='FLOAT_VECTOR'); uvn.inputs["Name"].default_value = "UV_Madera"
uvo = vm('ADD', uvn.outputs["Attribute"], rnd('FLOAT_VECTOR', (0.0, 0.0, 0.0), (1.0, 1.0, 0.0), isl, 21, 1700, 900), 1900, 750)
st_uv = node("GeometryNodeStoreNamedAttribute", 1900, 350, data_type='FLOAT2', domain='CORNER')
Lk(setp.outputs[0], sk(st_uv, "Geometry")); sk(st_uv, "Name").default_value = "UV_Madera"; Lk(uvo, sk(st_uv, "Value"))
last = st_uv.outputs[0]
for i, nm in enumerate(("madera_var", "madera_var2")):
    stv = node("GeometryNodeStoreNamedAttribute", 2100 + 200 * i, 350, data_type='FLOAT', domain='FACE')
    Lk(last, sk(stv, "Geometry")); sk(stv, "Name").default_value = nm
    Lk(rnd('FLOAT', 0.0, 1.0, isl, 31 + i, 2100 + 200 * i, 650), sk(stv, "Value"))
    last = stv.outputs[0]
Lk(last, go.inputs[0])

# --- modificador en la curva ---
cv = bpy.data.objects[CURVA]
md = cv.modifiers.get("Pasarela") or cv.modifiers.new("Pasarela", 'NODES')
md.node_group = ng
ids = {it.name: it.identifier for it in ng.interface.items_tree if getattr(it, "in_out", "") == 'INPUT'}
md[ids["Modulo"]] = mod_obj
result = {"ok": True, "largo_modulo": round(largo_def, 3)}
