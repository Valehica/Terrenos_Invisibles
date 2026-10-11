# Bakes de la vicuña para vicuna_componer.py. Correr en Blender con Vicuña9.blend
# abierto (Scripting > Run). No toca M_Vicuña ni la escena: usa una escena y un
# material temporales y los borra al final. Pose de reposo (Armature apagado).
#
# Salida en OUT (.npy float, filas de abajo hacia arriba):
#   pos      posicion objeto normalizada al bbox (margen 0: rgb 0 = fuera de isla)
#   onormal  normal objeto * 0.5 + 0.5
#   isla     id de isla UV / 64
#   height   ruido 3D de pelo (el mismo que da la normal)
#   normal   normal tangente con el bump del pelo
#   ao       oclusion (distancia 0.18 m)
import bpy, bmesh, mathutils, numpy as np, os, tempfile

# ~400 MB de .npy: fuera de OneDrive y del proyecto
OUT = globals().get("OUT_VICUNA") or os.path.join(tempfile.gettempdir(), "bakes_vicuna")
R = 2048
OBJ, RIG = "Plane", "rig"
MN = mathutils.Vector((-0.1098, -0.4670, -0.0023))   # igual que EXT en vicuna_componer.py
EXT = mathutils.Vector((0.2196, 0.7202, 0.9077))

os.makedirs(OUT, exist_ok=True)
o = bpy.data.objects[OBJ]; me = o.data

# ---- id de isla UV como atributo por esquina ----
bm = bmesh.new(); bm.from_mesh(me); uv = bm.loops.layers.uv.active; bm.faces.ensure_lookup_table()
padre = list(range(len(bm.faces)))
def raiz(a):
    while padre[a] != a: padre[a] = padre[padre[a]]; a = padre[a]
    return a
def uv_de(f, v):
    for l in f.loops:
        if l.vert == v: return tuple(round(c, 5) for c in l[uv].uv)
for e in bm.edges:
    if len(e.link_faces) == 2:
        f1, f2 = e.link_faces; v1, v2 = e.verts
        if uv_de(f1, v1) == uv_de(f2, v1) and uv_de(f1, v2) == uv_de(f2, v2):
            a, b = raiz(f1.index), raiz(f2.index)
            if a != b: padre[a] = b
ids = {r: i for i, r in enumerate(sorted(set(raiz(f.index) for f in bm.faces)))}
isla_cara = [ids[raiz(f.index)] for f in bm.faces]; bm.free()
at = me.attributes.new("_isla", 'FLOAT_COLOR', 'CORNER')
for p in me.polygons:
    v = (isla_cara[p.index] + 1) / 64.0
    for li in p.loop_indices: at.data[li].color = (v, v, v, 1)

# ---- material de bake ----
mat = bpy.data.materials.new("_bake_vicuna"); mat.use_nodes = True
nt = mat.node_tree; nt.nodes.clear(); N = nt.nodes; L = nt.links
tc = N.new('ShaderNodeTexCoord'); geo = N.new('ShaderNodeNewGeometry')
vt = N.new('ShaderNodeVectorTransform'); vt.vector_type = 'NORMAL'; vt.convert_from = 'WORLD'; vt.convert_to = 'OBJECT'
L.new(geo.outputs['Normal'], vt.inputs[0])
sep = N.new('ShaderNodeSeparateXYZ'); L.new(vt.outputs[0], sep.inputs[0])
absz = N.new('ShaderNodeMath'); absz.operation = 'ABSOLUTE'; L.new(sep.outputs['Z'], absz.inputs[0])
# 0 = pelo que cuelga (vertical), 1 = superficie que mira arriba/abajo (pelo a lo largo del lomo)
f = N.new('ShaderNodeMapRange'); f.interpolation_type = 'SMOOTHSTEP'; L.new(absz.outputs[0], f.inputs['Value'])
f.inputs['From Min'].default_value = 0.5; f.inputs['From Max'].default_value = 0.85

def ruido(escala, detalle, rug, dist):
    vm = N.new('ShaderNodeVectorMath'); vm.operation = 'MULTIPLY'; vm.inputs[1].default_value = escala
    L.new(tc.outputs['Object'], vm.inputs[0])
    nz = N.new('ShaderNodeTexNoise'); nz.noise_dimensions = '3D'
    nz.inputs['Scale'].default_value = 1.0; nz.inputs['Detail'].default_value = detalle
    nz.inputs['Roughness'].default_value = rug; nz.inputs['Distortion'].default_value = dist
    L.new(vm.outputs[0], nz.inputs['Vector']); return nz.outputs['Fac']
def mezcla(a, b, fac):
    m = N.new('ShaderNodeMix'); m.data_type = 'FLOAT'
    L.new(fac, m.inputs['Factor']); L.new(a, m.inputs['A']); L.new(b, m.inputs['B']); return m.outputs['Result']
fino = mezcla(ruido((260, 260, 28), 4, 0.55, 0.15), ruido((260, 28, 260), 4, 0.55, 0.15), f.outputs['Result'])
mecha = mezcla(ruido((60, 60, 9), 2, 0.5, 0.0), ruido((60, 9, 60), 2, 0.5, 0.0), f.outputs['Result'])
h2 = N.new('ShaderNodeMath'); h2.operation = 'MULTIPLY'; h2.inputs[1].default_value = 0.35; L.new(mecha, h2.inputs[0])
h = N.new('ShaderNodeMath'); h.operation = 'MULTIPLY_ADD'; h.inputs[1].default_value = 0.65
L.new(fino, h.inputs[0]); L.new(h2.outputs[0], h.inputs[2])

def emision(sock):
    e = N.new('ShaderNodeEmission'); L.new(sock, e.inputs['Color']); return e.outputs[0]
pm = N.new('ShaderNodeVectorMath'); pm.operation = 'SUBTRACT'; pm.inputs[1].default_value = tuple(MN); L.new(tc.outputs['Object'], pm.inputs[0])
pd = N.new('ShaderNodeVectorMath'); pd.operation = 'DIVIDE'; pd.inputs[1].default_value = tuple(EXT); L.new(pm.outputs[0], pd.inputs[0])
nmad = N.new('ShaderNodeVectorMath'); nmad.operation = 'MULTIPLY_ADD'
nmad.inputs[1].default_value = (0.5, 0.5, 0.5); nmad.inputs[2].default_value = (0.5, 0.5, 0.5); L.new(vt.outputs[0], nmad.inputs[0])
atn = N.new('ShaderNodeAttribute'); atn.attribute_name = "_isla"
bump = N.new('ShaderNodeBump'); bump.inputs['Strength'].default_value = 0.5; bump.inputs['Distance'].default_value = 0.002
L.new(h.outputs[0], bump.inputs['Height'])
bsdf = N.new('ShaderNodeBsdfPrincipled'); L.new(bump.outputs['Normal'], bsdf.inputs['Normal'])
salidas = {"pos": emision(pd.outputs[0]), "onormal": emision(nmad.outputs[0]), "isla": emision(atn.outputs['Color']),
           "height": emision(h.outputs[0]), "normal": bsdf.outputs[0]}
out = N.new('ShaderNodeOutputMaterial'); nimg = N.new('ShaderNodeTexImage')

# ---- escena temporal ----
esc = bpy.data.scenes.new("_bake_vicuna")
esc.collection.objects.link(o); esc.collection.objects.link(bpy.data.objects[RIG])
esc.render.engine = 'CYCLES'; esc.cycles.device = 'CPU'
mundo = bpy.data.worlds.new("_bake_vicuna"); esc.world = mundo; mundo.light_settings.distance = 0.18
vl = esc.view_layers[0]

original = [s.material for s in o.material_slots]
arm = o.modifiers.get("Armature"); estado_arm = (arm.show_viewport, arm.show_render) if arm else None
img = bpy.data.images.new("_bake_vicuna", R, R, alpha=True, float_buffer=True)
try:
    o.material_slots[0].material = mat
    if arm: arm.show_viewport = arm.show_render = False
    # (nombre, tipo, muestras, margen)
    # BAKES_VICUNA = ("height", "normal") rehace solo el pelo (el AO es el lento)
    pedidos = globals().get("BAKES_VICUNA")
    for nombre, tipo, muestras, margen in [("pos", 'EMIT', 1, 0), ("onormal", 'EMIT', 1, 0), ("isla", 'EMIT', 1, 0),
                                           ("height", 'EMIT', 4, 32), ("normal", 'NORMAL', 8, 32), ("ao", 'AO', 96, 32)]:
        if pedidos and nombre not in pedidos: continue
        if nombre in salidas:
            for l in list(out.inputs['Surface'].links): L.remove(l)
            L.new(salidas[nombre], out.inputs['Surface'])
        nimg.image = img; N.active = nimg; esc.cycles.samples = muestras
        esc.render.bake.margin_type = 'EXTEND'
        with bpy.context.temp_override(scene=esc, view_layer=vl, active_object=o, object=o,
                                       selected_objects=[o], selected_editable_objects=[o]):
            bpy.ops.object.bake(type=tipo, margin=margen, use_clear=True, normal_space='TANGENT')
        a = np.empty(R * R * 4, np.float32); img.pixels.foreach_get(a)
        np.save(os.path.join(OUT, nombre + ".npy"), a.reshape(R, R, 4) if nombre != "isla" else a.reshape(R, R, 4)[..., 0])
        print("bake", nombre)
finally:
    for s, m in zip(o.material_slots, original): s.material = m
    if arm: arm.show_viewport, arm.show_render = estado_arm
    me.attributes.remove(me.attributes["_isla"])
    bpy.data.images.remove(img); bpy.data.scenes.remove(esc)
    bpy.data.materials.remove(mat); bpy.data.worlds.remove(mundo)
print("listo:", OUT)
