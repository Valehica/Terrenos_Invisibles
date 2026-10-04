# Material de hojas de Queñoa con un tono distinto por hoja.
#
# datos_hojas(): en cada mancha de follaje (mallas con las hojas ya realizadas,
#   cada hoja = isla de 30 vertices copiada de MODELO) escribe el atributo de
#   punto "Hoja_Datos": R azar por hoja, G exposicion (afuera/arriba de la
#   mancha = 1, adentro = 0), B base->punta, A nervio->borde.
# crear_material(): grupo "Hojas_Quenoa" + material M_Quenoa_Hojas en esas mallas
#   y en MODELO (para instancias de particulas, que usan el azar del objeto).
# hornear_unity(): bakea el color final a un color de vertice "Color_Unity".
#
# Uso: Run Script con el .blend abierto (corre los dos primeros pasos).
import bpy, bmesh, os, sys, random
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from material_quenoa import G, lin, _ordenar

MANCHAS = ["Icosphere", "Icosphere.001", "Icosphere.002", "Icosphere.003",
           "Icosphere.004", "Icosphere.005", "Icosphere.006"]
OTROS = ["Icosphere_TEST", "Plane", "Plane.001", "Plane.002", "Plane.003"]
MODELO = "Plane"

def _local_modelo():
    """Base->punta y nervio->borde de cada vertice del modelo de hoja."""
    vs = [v.co for v in bpy.data.objects[MODELO].data.vertices]
    ys = [v.y for v in vs]; y0, y1 = min(ys), max(ys)
    xm = sum(v.x for v in vs) / len(vs)
    ancho = max(abs(v.x - xm) for v in vs)
    return [((v.y - y0) / (y1 - y0), abs(v.x - xm) / ancho) for v in vs]

def _islas(bm):
    visto = set(); islas = []
    for v0 in bm.verts:
        if v0.index in visto: continue
        pila = [v0]; visto.add(v0.index); isla = []
        while pila:
            v = pila.pop(); isla.append(v)
            for e in v.link_edges:
                w = e.other_vert(v)
                if w.index not in visto: visto.add(w.index); pila.append(w)
        islas.append(sorted(isla, key=lambda v: v.index))
    return islas

def datos_hojas(semilla=7):
    local = _local_modelo(); n = len(local)
    rnd = random.Random(semilla); informe = {}
    for nombre in MANCHAS:
        ob = bpy.data.objects[nombre]; me = ob.data
        bm = bmesh.new(); bm.from_mesh(me)
        islas = _islas(bm)
        centros = [sum((v.co for v in isla), Vector()) / len(isla) for isla in islas]
        C = sum(centros, Vector()) / len(centros)
        rmax = max((c - C).length for c in centros) or 1.0
        zs = [c.z for c in centros]; z0, z1 = min(zs), max(zs)
        datos = [(0.0, 0.0, 0.0, 0.0)] * len(bm.verts); raras = 0
        for isla, c in zip(islas, centros):
            afuera = (c - C).length / rmax
            arriba = (c.z - z0) / ((z1 - z0) or 1.0)
            expo = 0.05 + 0.95 * min(1.0, 0.6 * afuera + 0.4 * arriba)
            r = rnd.random()
            if len(isla) == n:          # misma topologia que el modelo: orden igual
                for k, v in enumerate(isla):
                    datos[v.index] = (r, expo, local[k][0], local[k][1])
            else:                       # hoja rara: sin gradiente interno
                raras += 1
                for v in isla: datos[v.index] = (r, expo, 0.5, 0.5)
        bm.free()
        at = me.color_attributes.get("Hoja_Datos")
        if at: me.color_attributes.remove(at)
        at = me.color_attributes.new("Hoja_Datos", 'FLOAT_COLOR', 'POINT')
        at.data.foreach_set("color", [x for d in datos for x in d])
        informe[nombre] = {"hojas": len(islas), "raras": raras}
    return informe

ENTRADAS = [
    ("Verde oscuro", lin("2c4420")),
    ("Verde medio", lin("56762c")),
    ("Verde claro", lin("9db24a")),
    ("Verde gris", lin("7f9070")),
    ("Hoja seca", lin("a8854a")),
    ("Enves", lin("a9b59a")),
]
VALORES = [  # nombre, valor, min, max
    ("Variacion", 0.6, 0, 1),
    ("Exposicion", 0.7, 0, 1),
    ("Hojas grises", 0.2, 0, 1),
    ("Hojas secas", 0.04, 0, 1),
    ("Puntas claras", 0.35, 0, 1),
    ("Nervio claro", 0.25, 0, 1),
]

def crear_grupo():
    ng = bpy.data.node_groups.get("Hojas_Quenoa")
    if ng: bpy.data.node_groups.remove(ng)
    ng = bpy.data.node_groups.new("Hojas_Quenoa", "ShaderNodeTree")
    for nombre, col in ENTRADAS:
        ng.interface.new_socket(nombre, in_out='INPUT', socket_type="NodeSocketColor").default_value = col
    for nombre, val, lo, hi in VALORES:
        s = ng.interface.new_socket(nombre, in_out='INPUT', socket_type="NodeSocketFloat")
        s.default_value = val; s.min_value = lo; s.max_value = hi
    ng.interface.new_socket("Base Color", in_out='OUTPUT', socket_type="NodeSocketColor")
    ng.interface.new_socket("Frente", in_out='OUTPUT', socket_type="NodeSocketColor")

    g = G(ng)
    gi = g.n("NodeGroupInput"); go = g.n("NodeGroupOutput")
    I = lambda nombre: gi.outputs[nombre]
    at = g.n("ShaderNodeAttribute", attribute_type='GEOMETRY', attribute_name="Hoja_Datos")
    sep = g.n("ShaderNodeSeparateColor"); g.link(g.out(at, "Color"), sep.inputs[0])
    info = g.n("ShaderNodeObjectInfo")
    # sin atributo (instancias de particulas) G = 0: azar del objeto y exposicion media
    hay = g.math('GREATER_THAN', sep.outputs[1], 0.01)
    azar = g.mezclaf(hay, g.out(info, "Random"), sep.outputs[0])
    expo = g.mezclaf(hay, 0.5, sep.outputs[1])
    punta = g.mezclaf(hay, 0.5, sep.outputs[2])
    lado = g.mezclaf(hay, 0.5, g.out(at, "Alpha"))
    # un poco de variacion por mancha, para que no se vean todas iguales
    azar = g.math('FRACT', g.math('ADD', azar, g.math('MULTIPLY', g.out(info, "Random"), 0.37)))
    az2 = g.math('FRACT', g.math('MULTIPLY', azar, 13.71))
    az3 = g.math('FRACT', g.math('MULTIPLY', azar, 71.37))

    # tono: hojas expuestas mas claras/amarillas, interiores oscuras; mas el azar
    t = g.math('ADD', g.math('ADD', 0.2, g.math('MULTIPLY', expo, g.math('MULTIPLY', I("Exposicion"), 0.6))),
               g.math('MULTIPLY', g.math('SUBTRACT', azar, 0.5), I("Variacion")))
    t = g.math('MINIMUM', g.math('MAXIMUM', t, 0.0), 1.0)
    c = g.mezcla(g.rango(t, 0.0, 0.5), I("Verde oscuro"), I("Verde medio"))
    c = g.mezcla(g.rango(t, 0.5, 1.0), c, I("Verde claro"))
    gris = g.math('LESS_THAN', az2, I("Hojas grises"))
    c = g.mezcla(g.math('MULTIPLY', gris, 0.5), c, I("Verde gris"))
    # dentro de la hoja: punta mas clara, nervio claro, borde algo mas oscuro
    c = g.mezcla(g.math('MULTIPLY', g.rango(punta, 0.4, 1.0), I("Puntas claras")), c, I("Verde claro"))
    c = g.mezcla(g.math('MULTIPLY', g.rango(lado, 0.15, 0.0), I("Nervio claro")), c, I("Verde claro"))
    c = g.mezcla(g.math('MULTIPLY', g.rango(lado, 0.7, 1.0), 0.25), c, I("Verde oscuro"))
    seca = g.math('LESS_THAN', az3, I("Hojas secas"))
    c = g.mezcla(seca, c, I("Hoja seca"))
    g.link(c, go.inputs["Frente"])
    # enves plateado (pelos del enves)
    geo = g.n("ShaderNodeNewGeometry")
    enves = g.mezcla(0.4, c, I("Enves"))
    g.link(g.mezcla(g.out(geo, "Backfacing"), c, enves), go.inputs["Base Color"])
    _ordenar(ng)
    return ng

def crear_material():
    ng = crear_grupo()
    m = bpy.data.materials.get("M_Quenoa_Hojas") or bpy.data.materials.new("M_Quenoa_Hojas")
    m.use_nodes = True; nt = m.node_tree; nt.nodes.clear(); g = G(nt)
    grp = g.n("ShaderNodeGroup", location=(-400, 0)); grp.node_tree = ng; grp.width = 220
    bsdf = g.n("ShaderNodeBsdfPrincipled", location=(-100, 100))
    g.link(grp.outputs["Base Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.6
    bsdf.inputs["Specular IOR Level"].default_value = 0.35
    # luz que atraviesa la hoja a contraluz
    tr = g.n("ShaderNodeBsdfTranslucent", location=(-100, -300))
    g.link(grp.outputs["Frente"], tr.inputs["Color"])
    mix = g.n("ShaderNodeMixShader", location=(250, 0)); mix.inputs[0].default_value = 0.2
    g.link(bsdf.outputs[0], mix.inputs[1]); g.link(tr.outputs[0], mix.inputs[2])
    out = g.n("ShaderNodeOutputMaterial", location=(450, 0))
    g.link(mix.outputs[0], out.inputs["Surface"])
    viejo = bpy.data.materials.get("Material")
    if viejo: viejo.use_fake_user = True
    for nombre in MANCHAS + OTROS:
        ob = bpy.data.objects.get(nombre)
        if not ob: continue
        if not ob.material_slots: ob.data.materials.append(m)
        for s in ob.material_slots: s.material = m
    return m

def hornear_unity():
    """Color final (frente) a un color de vertice 'Color_Unity' en cada mancha."""
    sc = bpy.context.scene; m = bpy.data.materials["M_Quenoa_Hojas"]; nt = m.node_tree
    out = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
    grp = next(n for n in nt.nodes if n.type == 'GROUP')
    mix = next(n for n in nt.nodes if n.type == 'MIX_SHADER')
    em = nt.nodes.new("ShaderNodeEmission"); nt.links.new(grp.outputs["Frente"], em.inputs["Color"])
    nt.links.new(em.outputs[0], out.inputs["Surface"])
    motor = sc.render.engine; sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'
    muestras = sc.cycles.samples; sc.cycles.samples = 1
    b = sc.render.bake; target = b.target; sel = b.use_selected_to_active
    b.target = 'VERTEX_COLORS'; b.use_selected_to_active = False
    activo = bpy.context.view_layer.objects.active
    seleccion = list(bpy.context.selected_objects)
    try:
        for nombre in MANCHAS:
            ob = bpy.data.objects[nombre]; me = ob.data
            at = me.color_attributes.get("Color_Unity") or me.color_attributes.new("Color_Unity", 'BYTE_COLOR', 'CORNER')
            me.color_attributes.active_color = at
            for o in bpy.context.selected_objects: o.select_set(False)
            ob.select_set(True); bpy.context.view_layer.objects.active = ob
            bpy.ops.object.bake(type='EMIT')
            # FBX con "Prioritize Active Color" lo exporta primero: es el que lee Unity
            me.color_attributes.render_color_index = me.color_attributes.active_color_index
    finally:
        nt.links.new(mix.outputs[0], out.inputs["Surface"]); nt.nodes.remove(em)
        sc.render.engine = motor; sc.cycles.samples = muestras
        b.target = target; b.use_selected_to_active = sel
        for o in bpy.context.selected_objects: o.select_set(False)
        for o in seleccion: o.select_set(True)
        bpy.context.view_layer.objects.active = activo

if __name__ == "__main__":
    print(datos_hojas())
    crear_material()
