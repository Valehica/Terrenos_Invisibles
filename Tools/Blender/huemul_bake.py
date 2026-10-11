# Bakes del huemul para huemul_componer.py. Correr en Blender con el .blend del
# huemul abierto (Concept/Sur/Ñandu.blend, malla "Plane" con Mirror). No toca
# M_Huemul ni la escena: usa una escena y un material temporales y los borra.
#
# Coordenadas: el objeto esta girado 90° en Y; aqui se trabaja en "mundo"
# (x = ancho, |x| por el Mirror; y = largo, cabeza en +y; z = alto, 0 = suelo).
#
# Salida en OUT (.npy float, filas de abajo hacia arriba):
#   pos      posicion mundo normalizada al bbox (rgb 0 = fuera de isla)
#   onormal  normal mundo * 0.5 + 0.5
#   isla     id de isla UV / 64
#   zona     R = pezuña, G = cuerno (por isla, ver ZONAS)
#   height   relieve (pelo / poros / estrias) que da la normal
#   normal   normal tangente con ese relieve
#   ao       oclusion (con el Mirror encendido, distancia 0.3 m)
import bpy, bmesh, mathutils, numpy as np, os, tempfile

OUT = globals().get("OUT_HUEMUL") or os.path.join(tempfile.gettempdir(), "bakes_huemul")
R = 2048
OBJ = "Plane"
MN = mathutils.Vector((-0.01, -0.57, -0.01))   # igual que MN/EXT en huemul_componer.py
EXT = mathutils.Vector((0.32, 2.03, 2.10))

os.makedirs(OUT, exist_ok=True)
o = bpy.data.objects[OBJ]; me = o.data
W = o.matrix_world.to_3x3()

# ---- islas UV y zonas ----
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
isla_cara = [ids[raiz(f.index)] for f in bm.faces]
# ZONAS: pezuña = isla entera bajo 12 cm; cuerno = isla entera sobre 1,78 m
zmin = {}; zmax = {}
for f in bm.faces:
    i = isla_cara[f.index]
    for v in f.verts:
        z = (W @ v.co).z
        zmin[i] = min(zmin.get(i, 9), z); zmax[i] = max(zmax.get(i, -9), z)
bm.free()
zona = {i: (1.0 if zmax[i] < 0.12 else 0.0, 1.0 if zmin[i] > 1.78 else 0.0) for i in zmin}
print("pezuñas", [i for i in zona if zona[i][0]], "cuernos", [i for i in zona if zona[i][1]])
at = me.attributes.new("_isla", 'FLOAT_COLOR', 'CORNER')
az = me.attributes.new("_zona", 'FLOAT_COLOR', 'CORNER')
for p in me.polygons:
    i = isla_cara[p.index]; v = (i + 1) / 64.0; zr, zg = zona[i]
    for li in p.loop_indices:
        at.data[li].color = (v, v, v, 1); az.data[li].color = (zr, zg, 0, 1)

# ---- material de bake ----
mat = bpy.data.materials.new("_bake_huemul"); mat.use_nodes = True
nt = mat.node_tree; nt.nodes.clear(); N = nt.nodes; L = nt.links
def nodo(t, **kw):
    n = N.new(t)
    for k, v in kw.items(): setattr(n, k, v)
    return n
def mate(op, a, b=None, c=None):
    m = nodo('ShaderNodeMath', operation=op)
    for i, x in enumerate((a, b, c)):
        if x is None: continue
        if isinstance(x, (int, float)): m.inputs[i].default_value = x
        else: L.new(x, m.inputs[i])
    return m.outputs[0]
def mezcla(a, b, fac):
    m = nodo('ShaderNodeMix', data_type='FLOAT')
    for s, x in (('Factor', fac), ('A', a), ('B', b)):
        if isinstance(x, (int, float)): m.inputs[s].default_value = x
        else: L.new(x, m.inputs[s])
    return m.outputs['Result']

tc = nodo('ShaderNodeTexCoord'); geo = nodo('ShaderNodeNewGeometry')
# posicion "mundo" con |x| (simetrica: las dos mitades del Mirror dan lo mismo)
wp = nodo('ShaderNodeVectorTransform', vector_type='POINT', convert_from='OBJECT', convert_to='WORLD')
L.new(tc.outputs['Object'], wp.inputs[0])
sw = nodo('ShaderNodeSeparateXYZ'); L.new(wp.outputs[0], sw.inputs[0])
cw = nodo('ShaderNodeCombineXYZ')
L.new(mate('ABSOLUTE', sw.outputs['X']), cw.inputs['X']); L.new(sw.outputs['Y'], cw.inputs['Y']); L.new(sw.outputs['Z'], cw.inputs['Z'])
P = cw.outputs[0]
Y, Z = sw.outputs['Y'], sw.outputs['Z']

def ruido(vec, escala, detalle, rug, dist):
    vm = nodo('ShaderNodeVectorMath', operation='MULTIPLY'); vm.inputs[1].default_value = escala
    L.new(vec, vm.inputs[0])
    nz = nodo('ShaderNodeTexNoise', noise_dimensions='3D')
    nz.inputs['Scale'].default_value = 1.0; nz.inputs['Detail'].default_value = detalle
    nz.inputs['Roughness'].default_value = rug; nz.inputs['Distortion'].default_value = dist
    L.new(vm.outputs[0], nz.inputs['Vector']); return nz.outputs['Fac']
def suave(x, a, b):
    m = nodo('ShaderNodeMapRange', interpolation_type='SMOOTHSTEP'); L.new(x, m.inputs['Value'])
    m.inputs['From Min'].default_value = a; m.inputs['From Max'].default_value = b; return m.outputs['Result']

# --- pelo: corto y liso. Torso y cabeza peinados hacia atras (a lo largo de y);
# patas y cuello peinados hacia abajo (a lo largo de z) ---
pata = suave(Z, 0.92, 0.74)
cuello = mate('MULTIPLY', suave(Y, 0.70, 0.90), suave(Z, 1.62, 1.48))
abajo = mate('MAXIMUM', pata, cuello)
fino = mezcla(ruido(P, (320, 55, 320), 4, 0.55, 0.15), ruido(P, (320, 320, 55), 4, 0.55, 0.15), abajo)
mecha = mezcla(ruido(P, (80, 14, 80), 2, 0.5, 0.0), ruido(P, (80, 80, 14), 2, 0.5, 0.0), abajo)
h_pelo = mate('MULTIPLY_ADD', fino, 0.6, mate('MULTIPLY', mecha, 0.4))

# --- pezuña: queratina porosa (poros de Voronoi + grano fino) ---
vor = nodo('ShaderNodeTexVoronoi', voronoi_dimensions='3D', feature='F1')
vor.inputs['Scale'].default_value = 220; vor.inputs['Randomness'].default_value = 1.0
L.new(P, vor.inputs['Vector'])
poro = suave(vor.outputs['Distance'], 0.0, 0.35)                 # 0 en el centro del poro
grano = ruido(P, (350, 350, 350), 3, 0.6, 0.0)
vetas = ruido(P, (200, 200, 25), 2, 0.5, 0.0)                    # crecimiento vertical de la uña
h_uña = mate('MULTIPLY_ADD', poro, 0.55, mate('MULTIPLY_ADD', grano, 0.25, mate('MULTIPLY', vetas, 0.2)))

# --- cuerno: estrias a lo largo del cuerno. Las islas son tubos desplegados con
# U alrededor y V a lo largo, asi que las lineas varian en U ---
sep_uv = nodo('ShaderNodeSeparateXYZ'); L.new(tc.outputs['UV'], sep_uv.inputs[0])
U, V = sep_uv.outputs['X'], sep_uv.outputs['Y']
uvn = nodo('ShaderNodeCombineXYZ'); L.new(U, uvn.inputs['X']); L.new(V, uvn.inputs['Y'])
ond = ruido(uvn.outputs[0], (60, 6, 1), 2, 0.5, 0.0)              # serpenteo suave de las lineas
u2 = mate('MULTIPLY_ADD', mate('SUBTRACT', ond, 0.5), 0.012, U)
estria = mate('ABSOLUTE', mate('SINE', mate('MULTIPLY', u2, 3.14159 * 230)))   # ~13 lineas en la isla grande
estria = mate('POWER', estria, 0.6)
nudo = ruido(uvn.outputs[0], (900, 120, 1), 3, 0.55, 0.0)       # rugosidad perlada de la base
h_cuerno = mate('MULTIPLY_ADD', estria, 0.75, mate('MULTIPLY', nudo, 0.25))

zat = nodo('ShaderNodeAttribute', attribute_name="_zona")
zsep = nodo('ShaderNodeSeparateColor'); L.new(zat.outputs['Color'], zsep.inputs[0])
es_uña, es_cuerno = zsep.outputs['Red'], zsep.outputs['Green']
h = mezcla(mezcla(h_pelo, h_uña, es_uña), h_cuerno, es_cuerno)
# relieve por zona (metros de bump): pelo ~7°, poros ~8°, estrias ~10° de mediana
relieve = mezcla(mezcla(0.0026, 0.005, es_uña), 0.0008, es_cuerno)

def emision(sock):
    e = nodo('ShaderNodeEmission'); L.new(sock, e.inputs['Color']); return e.outputs[0]
pm = nodo('ShaderNodeVectorMath', operation='SUBTRACT'); pm.inputs[1].default_value = tuple(MN); L.new(wp.outputs[0], pm.inputs[0])
pd = nodo('ShaderNodeVectorMath', operation='DIVIDE'); pd.inputs[1].default_value = tuple(EXT); L.new(pm.outputs[0], pd.inputs[0])
nmad = nodo('ShaderNodeVectorMath', operation='MULTIPLY_ADD')
nmad.inputs[1].default_value = (0.5, 0.5, 0.5); nmad.inputs[2].default_value = (0.5, 0.5, 0.5); L.new(geo.outputs['Normal'], nmad.inputs[0])
atn = nodo('ShaderNodeAttribute', attribute_name="_isla")
bump = nodo('ShaderNodeBump'); bump.inputs['Strength'].default_value = 1.0
L.new(relieve, bump.inputs['Distance']); L.new(h, bump.inputs['Height'])
bsdf = nodo('ShaderNodeBsdfPrincipled'); L.new(bump.outputs['Normal'], bsdf.inputs['Normal'])
salidas = {"pos": emision(pd.outputs[0]), "onormal": emision(nmad.outputs[0]), "isla": emision(atn.outputs['Color']),
           "zona": emision(zat.outputs['Color']), "height": emision(h), "normal": bsdf.outputs[0]}
out = nodo('ShaderNodeOutputMaterial'); nimg = nodo('ShaderNodeTexImage')

# ---- escena temporal ----
esc = bpy.data.scenes.new("_bake_huemul")
esc.collection.objects.link(o)
esc.render.engine = 'CYCLES'; esc.cycles.device = 'CPU'
mundo = bpy.data.worlds.new("_bake_huemul"); esc.world = mundo; mundo.light_settings.distance = 0.3
vl = esc.view_layers[0]

original = [s.material for s in o.material_slots]
mir = o.modifiers.get("Mirror"); estado_mir = (mir.show_viewport, mir.show_render) if mir else None
img = bpy.data.images.new("_bake_huemul", R, R, alpha=True, float_buffer=True)
try:
    o.material_slots[0].material = mat
    # BAKES_HUEMUL = ("height", "normal") rehace solo el relieve (el AO es el lento)
    pedidos = globals().get("BAKES_HUEMUL")
    for nombre, tipo, muestras, margen in [("pos", 'EMIT', 1, 0), ("onormal", 'EMIT', 1, 0), ("isla", 'EMIT', 1, 0),
                                           ("zona", 'EMIT', 1, 0), ("height", 'EMIT', 4, 32),
                                           ("normal", 'NORMAL', 8, 32), ("ao", 'AO', 64, 32)]:
        if pedidos and nombre not in pedidos: continue
        # el Mirror solo hace falta para el AO (la otra mitad hace sombra);
        # sin el, cada texel se hornea una sola vez y pos/normal son exactas
        if mir: mir.show_viewport = mir.show_render = (nombre == "ao")
        if nombre in salidas:
            for l in list(out.inputs['Surface'].links): L.remove(l)
            L.new(salidas[nombre], out.inputs['Surface'])
        nimg.image = img; N.active = nimg; esc.cycles.samples = muestras
        esc.render.bake.margin_type = 'EXTEND'
        with bpy.context.temp_override(scene=esc, view_layer=vl, active_object=o, object=o,
                                       selected_objects=[o], selected_editable_objects=[o]):
            bpy.ops.object.bake(type=tipo, margin=margen, use_clear=True, normal_space='TANGENT')
        a = np.empty(R * R * 4, np.float32); img.pixels.foreach_get(a)
        a = a.reshape(R, R, 4)
        np.save(os.path.join(OUT, nombre + ".npy"), a[..., 0] if nombre == "isla" else a)
        print("bake", nombre)
finally:
    for s, m in zip(o.material_slots, original): s.material = m
    if mir: mir.show_viewport, mir.show_render = estado_mir
    for n in ("_isla", "_zona"):
        if n in me.attributes: me.attributes.remove(me.attributes[n])
    bpy.data.images.remove(img); bpy.data.scenes.remove(esc)
    bpy.data.materials.remove(mat); bpy.data.worlds.remove(mundo)
print("listo:", OUT)
