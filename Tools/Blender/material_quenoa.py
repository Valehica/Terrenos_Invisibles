# Material de corteza de Queñoa (Polylepis tarapacana): hebras retorcidas + bakes.
#
# hornear_mascaras(): de la malla alta (ALTA) a la baja (BAJA) sobre UV_Madera:
#   normal tangente, AO y cavidad (pointiness). Guarda PNG en <blend>/Texturas.
# crear_material(): marca en la malla las islas giradas al empacar (atributo de
#   cara "veta_u", el grupo gira ahi las coordenadas), arma el grupo
#   "Corteza_Quenoa" (hebras largas con torsion, tiras sueltas, madera gris
#   expuesta, liquen, polvo del altiplano) y el material M_Quenoa_Corteza.
# hornear_unity(): BaseColor, Roughness y Normal final de ese material.
#
# Uso: con el .blend abierto, Run Script (corre los dos primeros pasos).
# Las coordenadas del grupo van en metros: X alrededor de la rama, Y a lo largo
# (UV_Madera pone la veta en V), asi que las escalas son tamanos reales.
import bpy, os

BAJA = "Arbol_Remesh"
ALTA = "Arbol"
UV = "UV_Madera"
RES = 2048
EXTRUSION = 0.10      # m, el 95% de la baja esta a <6 cm de la alta
MAX_RAYO = 0.5
AO_MUESTRAS = 32
AO_DISTANCIA = 0.6    # m

def carpeta():
    d = os.path.join(os.path.dirname(bpy.data.filepath), "Texturas")
    os.makedirs(d, exist_ok=True)
    return d

def lin(hexstr):
    c = [int(hexstr[i:i+2], 16) / 255 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c) + (1.0,)

def metros_por_uv(ob):
    import bmesh
    bm = bmesh.new(); bm.from_mesh(ob.data); uv = bm.loops.layers.uv[UV]
    a3 = auv = 0.0
    for f in bm.faces:
        a3 += f.calc_area(); p = [l[uv].uv for l in f.loops]
        auv += abs(sum(p[i].x * p[i-1].y - p[i-1].x * p[i].y for i in range(len(p)))) / 2
    bm.free()
    return (a3 / auv) ** 0.5

def marcar_veta(ob):
    """Atributo de cara 'veta_u' = 1 en islas cuya rama corre en U (isla girada al
    empacar). El grupo gira las coordenadas ahi para que la veta siga la rama."""
    import bmesh
    from mathutils import Vector, Matrix
    bm = bmesh.new(); bm.from_mesh(ob.data); uv = bm.loops.layers.uv[UV]
    k = lambda l: (round(l[uv].uv.x, 5), round(l[uv].uv.y, 5))
    visto = set(); valores = [0.0] * len(bm.faces)
    for f0 in bm.faces:
        if f0.index in visto: continue
        pila = [f0]; visto.add(f0.index); isla = []
        while pila:
            f = pila.pop(); isla.append(f)
            for l in f.loops:
                for g in l.edge.link_faces:
                    if g.index in visto: continue
                    lg = next(x for x in g.loops if x.edge == l.edge)
                    if {k(l), k(l.link_loop_next)} == {k(lg), k(lg.link_loop_next)}:
                        visto.add(g.index); pila.append(g)
        # eje largo de la isla en 3D (iteracion de potencia sobre la covarianza)
        pts = [v.co for f in isla for v in f.verts]; c = sum(pts, Vector()) / len(pts)
        cov = Matrix(((0,) * 3,) * 3)
        for p in pts:
            d = p - c
            for i in range(3):
                for j in range(3): cov[i][j] += d[i] * d[j]
        eje = Vector((0.3, 0.2, 1.0))
        for _ in range(30): eje = (cov @ eje).normalized()
        su = sv = 0.0
        for f in isla:
            for l in f.loops:
                d3 = abs((l.link_loop_next.vert.co - l.vert.co).dot(eje))
                su += d3 * abs(l.link_loop_next[uv].uv.x - l[uv].uv.x)
                sv += d3 * abs(l.link_loop_next[uv].uv.y - l[uv].uv.y)
        if su > 2 * sv:
            for f in isla: valores[f.index] = 1.0
    bm.free()
    at = ob.data.attributes.get("veta_u")
    if at: ob.data.attributes.remove(at)
    at = ob.data.attributes.new("veta_u", 'FLOAT', 'FACE')
    at.data.foreach_set("value", valores)
    return int(sum(valores))

def imagen(nombre, color=False):
    im = bpy.data.images.get(nombre)
    if im is None or tuple(im.size) != (RES, RES):
        if im: bpy.data.images.remove(im)
        im = bpy.data.images.new(nombre, RES, RES, alpha=False)
    im.colorspace_settings.name = 'sRGB' if color else 'Non-Color'
    return im

def guardar(im):
    im.filepath_raw = os.path.join(carpeta(), im.name + ".png")
    im.file_format = 'PNG'
    im.save()

# ---------------------------------------------------------------- bakes

class _Bake:
    """Deja la escena lista para hornear y la restaura al salir."""
    def __init__(self, con_alta):
        self.con_alta = con_alta
    def __enter__(self):
        sc = bpy.context.scene; self.sc = sc
        self.baja = bpy.data.objects[BAJA]; self.alta = bpy.data.objects[ALTA]
        if bpy.context.object and bpy.context.object.mode != 'OBJECT':
            bpy.ops.object.mode_set(mode='OBJECT')
        self.motor = sc.render.engine; sc.render.engine = 'CYCLES'
        sc.cycles.device = 'CPU'
        self.muestras = sc.cycles.samples
        self.uv_activa = self.baja.data.uv_layers.active.name
        self.baja.data.uv_layers.active = self.baja.data.uv_layers[UV]
        a = self.alta
        self.vis_alta = (a.hide_viewport, a.hide_render, a.hide_get())
        if self.con_alta:
            a.hide_viewport = False; a.hide_render = False; a.hide_set(False)
        for o in bpy.context.selected_objects: o.select_set(False)
        if self.con_alta: a.select_set(True)
        self.baja.select_set(True); bpy.context.view_layer.objects.active = self.baja
        b = sc.render.bake
        b.use_selected_to_active = self.con_alta
        b.cage_extrusion = EXTRUSION; b.max_ray_distance = MAX_RAYO
        b.margin = 16; b.use_clear = True; b.normal_space = 'TANGENT'
        return self
    def __exit__(self, *exc):
        sc = self.sc
        sc.render.engine = self.motor; sc.cycles.samples = self.muestras
        self.baja.data.uv_layers.active = self.baja.data.uv_layers[self.uv_activa]
        a = self.alta; a.hide_viewport, a.hide_render, h = self.vis_alta; a.hide_set(h)
        sc.render.bake.use_selected_to_active = False

def _material_destino(ob, im):
    """Material temporal con un unico nodo de imagen activo (destino del bake)."""
    m = bpy.data.materials.get("_bake_destino") or bpy.data.materials.new("_bake_destino")
    m.use_nodes = True; nt = m.node_tree
    n = nt.nodes.get("destino") or nt.nodes.new("ShaderNodeTexImage")
    n.name = "destino"; n.image = im; nt.nodes.active = n
    return m

def _con_material(ob, m):
    viejos = [s.material for s in ob.material_slots]
    if not ob.material_slots: ob.data.materials.append(None)
    for s in ob.material_slots: s.material = m
    return viejos

def _restaurar(ob, viejos):
    for s, m in zip(ob.material_slots, viejos): s.material = m

def hornear_mascara(tipo):
    """tipo: 'NORMAL', 'AO' o 'CAVIDAD'. Una llamada por mapa (el AO tarda)."""
    nombre = {"NORMAL": "Quenoa_N_alta", "AO": "Quenoa_AO", "CAVIDAD": "Quenoa_Cavidad"}[tipo]
    im = imagen(nombre)
    with _Bake(True) as k:
        viejos = _con_material(k.baja, _material_destino(k.baja, im))
        tmp_alta = None
        try:
            if tipo == "NORMAL":
                k.sc.cycles.samples = 1
                bpy.ops.object.bake(type='NORMAL')
            elif tipo == "AO":
                k.sc.cycles.samples = AO_MUESTRAS
                w = k.sc.world; d = w.light_settings.distance; w.light_settings.distance = AO_DISTANCIA
                try: bpy.ops.object.bake(type='AO')
                finally: w.light_settings.distance = d
            else:
                # La alta emite su pointiness (0.5 = plano, <0.5 hueco, >0.5 convexo)
                tmp_alta = bpy.data.materials.new("_bake_cavidad"); tmp_alta.use_nodes = True
                nt = tmp_alta.node_tree; nt.nodes.clear()
                g = nt.nodes.new("ShaderNodeNewGeometry")
                mr = nt.nodes.new("ShaderNodeMapRange")
                mr.inputs["From Min"].default_value = 0.42; mr.inputs["From Max"].default_value = 0.58
                em = nt.nodes.new("ShaderNodeEmission"); out = nt.nodes.new("ShaderNodeOutputMaterial")
                nt.links.new(g.outputs["Pointiness"], mr.inputs["Value"])
                nt.links.new(mr.outputs["Result"], em.inputs["Color"])
                nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
                viejos_alta = _con_material(k.alta, tmp_alta)
                k.sc.cycles.samples = 1
                try: bpy.ops.object.bake(type='EMIT')
                finally: _restaurar(k.alta, viejos_alta)
        finally:
            _restaurar(k.baja, viejos)
            if tmp_alta: bpy.data.materials.remove(tmp_alta)
    guardar(im)
    return im

def hornear_mascaras():
    for t in ("NORMAL", "CAVIDAD", "AO"): hornear_mascara(t)

# ---------------------------------------------------------------- grupo

class G:
    """Ayudante para armar arboles de nodos sin perder la cabeza."""
    def __init__(self, nt): self.nt = nt; self.x = 0
    def n(self, tipo, **props):
        nd = self.nt.nodes.new(tipo)
        for k, v in props.items(): setattr(nd, k, v)
        return nd
    def inp(self, nd, nombre):
        for s in nd.inputs:
            if (s.name == nombre or s.identifier == nombre) and getattr(s, "enabled", True): return s
        raise KeyError(f"{nd.bl_idname}.{nombre}")
    def out(self, nd, nombre):
        for s in nd.outputs:
            if (s.name == nombre or s.identifier == nombre) and getattr(s, "enabled", True): return s
        raise KeyError(f"{nd.bl_idname}.{nombre}")
    def link(self, a, b): self.nt.links.new(a, b)
    def set(self, nd, nombre, v):
        if isinstance(v, bpy.types.NodeSocket): self.link(v, self.inp(nd, nombre))
        else: self.inp(nd, nombre).default_value = v
    # operaciones que devuelven sockets
    def math(self, op, a, b=0.0, c=None, clamp=False):
        nd = self.n("ShaderNodeMath", operation=op, use_clamp=clamp)
        for i, v in enumerate((a, b) if c is None else (a, b, c)):
            if isinstance(v, bpy.types.NodeSocket): self.link(v, nd.inputs[i])
            else: nd.inputs[i].default_value = v
        return nd.outputs[0]
    def vmath(self, op, a, b=None, escala=None):
        nd = self.n("ShaderNodeVectorMath", operation=op)
        for i, v in enumerate((a, b)):
            if v is None: continue
            if isinstance(v, bpy.types.NodeSocket): self.link(v, nd.inputs[i])
            else: nd.inputs[i].default_value = v
        if escala is not None: self.set(nd, "Scale", escala)
        return nd.outputs[0] if op not in ("DOT_PRODUCT", "LENGTH", "DISTANCE") else nd.outputs["Value"]
    def rango(self, v, a, b, c=0.0, d=1.0, suave=True):
        nd = self.n("ShaderNodeMapRange", interpolation_type='SMOOTHSTEP' if suave else 'LINEAR')
        self.set(nd, "Value", v); self.set(nd, "From Min", a); self.set(nd, "From Max", b)
        self.set(nd, "To Min", c); self.set(nd, "To Max", d)
        return self.out(nd, "Result")
    def mezcla(self, f, a, b, modo='MIX'):
        nd = self.n("ShaderNodeMix", data_type='RGBA', blend_type=modo, clamp_factor=True)
        self.set(nd, "Factor_Float", f); self.set(nd, "A_Color", a); self.set(nd, "B_Color", b)
        return self.out(nd, "Result_Color")
    def mezclaf(self, f, a, b):
        nd = self.n("ShaderNodeMix", data_type='FLOAT', clamp_factor=True)
        self.set(nd, "Factor_Float", f); self.set(nd, "A_Float", a); self.set(nd, "B_Float", b)
        return self.out(nd, "Result_Float")
    def ruido(self, v, escala, detalle=3.0, rugosidad=0.55, w=None):
        nd = self.n("ShaderNodeTexNoise", noise_dimensions='4D' if w is not None else '3D')
        self.set(nd, "Vector", v); self.set(nd, "Scale", escala)
        self.set(nd, "Detail", detalle); self.set(nd, "Roughness", rugosidad)
        if w is not None: self.set(nd, "W", w)
        return self.out(nd, "Fac"), self.out(nd, "Color")
    def voronoi(self, v, rasgo='F1'):
        nd = self.n("ShaderNodeTexVoronoi", voronoi_dimensions='3D', feature=rasgo)
        self.set(nd, "Vector", v); self.set(nd, "Scale", 1.0)
        return nd

ENTRADAS = [
    # nombre, tipo, valor, min, max
    ("UV", "NodeSocketVector", None, None, None),
    ("AO", "NodeSocketFloat", 1.0, 0, 1),
    ("Cavidad", "NodeSocketFloat", 0.5, 0, 1),
    ("Normal", "NodeSocketVector", None, None, None),
    ("Metros por UV", "NodeSocketFloat", 11.45, 0.01, 1000),
    ("Z suelo", "NodeSocketFloat", 0.0, -100, 100),
    ("Corteza oscura", "NodeSocketColor", lin("4a2516"), None, None),
    ("Corteza rojiza", "NodeSocketColor", lin("8a3a1e"), None, None),
    ("Corteza canela", "NodeSocketColor", lin("a85c34"), None, None),
    ("Tira suelta", "NodeSocketColor", lin("9a5a38"), None, None),
    ("Surco", "NodeSocketColor", lin("1e0f09"), None, None),
    ("Madera gris", "NodeSocketColor", lin("958e85"), None, None),
    ("Surco madera", "NodeSocketColor", lin("5e5954"), None, None),
    ("Liquen gris", "NodeSocketColor", lin("a2ab8c"), None, None),
    ("Liquen naranjo", "NodeSocketColor", lin("cf7429"), None, None),
    ("Polvo", "NodeSocketColor", lin("c0aa8a"), None, None),
    ("Ancho hebras (m)", "NodeSocketFloat", 0.035, 0.005, 0.5),
    ("Largo hebras", "NodeSocketFloat", 14.0, 1.0, 50.0),
    ("Torsion", "NodeSocketFloat", 0.3, -2.0, 2.0),
    ("Ondulacion", "NodeSocketFloat", 0.5, 0, 2),
    ("Tiras sueltas", "NodeSocketFloat", 0.08, 0, 1),
    ("Madera expuesta", "NodeSocketFloat", 0.5, 0, 1),
    ("Liquen", "NodeSocketFloat", 0.2, 0, 1),
    ("Polvo cantidad", "NodeSocketFloat", 0.4, 0, 1),
    ("AO en color", "NodeSocketFloat", 0.5, 0, 1),
    ("Relieve", "NodeSocketFloat", 1.0, 0, 3),
]

def crear_grupo():
    ng = bpy.data.node_groups.get("Corteza_Quenoa")
    if ng: bpy.data.node_groups.remove(ng)
    ng = bpy.data.node_groups.new("Corteza_Quenoa", "ShaderNodeTree")
    for nombre, tipo, val, lo, hi in ENTRADAS:
        s = ng.interface.new_socket(nombre, in_out='INPUT', socket_type=tipo)
        if val is not None: s.default_value = val
        if lo is not None: s.min_value = lo; s.max_value = hi
        if nombre in ("UV", "Normal"): s.hide_value = True
    for nombre, tipo in (("Base Color", "NodeSocketColor"), ("Roughness", "NodeSocketFloat"),
                         ("Normal", "NodeSocketVector"), ("AO", "NodeSocketFloat"), ("Altura", "NodeSocketFloat")):
        ng.interface.new_socket(nombre, in_out='OUTPUT', socket_type=tipo)

    g = G(ng)
    gi = g.n("NodeGroupInput"); go = g.n("NodeGroupOutput")
    I = lambda nombre: gi.outputs[nombre]
    geo = g.n("ShaderNodeNewGeometry")
    sep_p = g.n("ShaderNodeSeparateXYZ"); g.link(geo.outputs["Position"], sep_p.inputs[0])
    sep_n = g.n("ShaderNodeSeparateXYZ"); g.link(geo.outputs["Normal"], sep_n.inputs[0])
    nz = sep_n.outputs["Z"]
    altura = g.math('SUBTRACT', sep_p.outputs["Z"], I("Z suelo"))

    # P en metros: X alrededor, Y a lo largo de la rama
    P = g.vmath('SCALE', I("UV"), escala=I("Metros por UV"))
    veta = g.n("ShaderNodeAttribute", attribute_type='GEOMETRY', attribute_name="veta_u")
    sp = g.n("ShaderNodeSeparateXYZ"); g.link(P, sp.inputs[0])
    rot = g.n("ShaderNodeCombineXYZ"); g.link(sp.outputs["Y"], rot.inputs[0])
    g.link(g.math('MULTIPLY', sp.outputs["X"], -1.0), rot.inputs[1])
    mv = g.n("ShaderNodeMix", data_type='VECTOR', clamp_factor=True)
    g.set(mv, "Factor_Float", g.out(veta, "Fac")); g.set(mv, "A_Vector", P)
    g.set(mv, "B_Vector", rot.outputs[0])
    P = g.out(mv, "Result_Vector")

    # --- torsion y ondulacion: las hebras giran alrededor de la rama y serpentean
    sp2 = g.n("ShaderNodeSeparateXYZ"); g.link(P, sp2.inputs[0])
    px, py = sp2.outputs["X"], sp2.outputs["Y"]
    cw = g.n("ShaderNodeCombineXYZ"); cw.inputs[0].default_value = 0.8
    cw.inputs[1].default_value = 0.25; cw.inputs[2].default_value = 1.0
    ond, _ = g.ruido(g.vmath('MULTIPLY', P, cw.outputs[0]), 1.0, detalle=2.0)
    ond = g.math('MULTIPLY', g.math('SUBTRACT', ond, 0.5),
                 g.math('MULTIPLY', I("Ondulacion"), g.math('MULTIPLY', I("Ancho hebras (m)"), 8.0)))
    px = g.math('ADD', g.math('ADD', px, g.math('MULTIPLY', py, I("Torsion"))), ond)
    ct = g.n("ShaderNodeCombineXYZ"); g.link(px, ct.inputs[0]); g.link(py, ct.inputs[1])
    Pt = ct.outputs[0]

    def celdas(ancho, largo, semilla, deshilachado):
        sx = g.math('DIVIDE', 1.0, ancho); sy = g.math('DIVIDE', sx, largo)
        c = g.n("ShaderNodeCombineXYZ"); g.link(sx, c.inputs[0]); g.link(sy, c.inputs[1])
        c.inputs[2].default_value = 1.0
        Q = g.vmath('ADD', g.vmath('MULTIPLY', Pt, c.outputs[0]), (0.0, 0.0, semilla))
        _, d = g.ruido(Q, 3.0, detalle=4.0, rugosidad=0.65)
        Q = g.vmath('ADD', Q, g.vmath('SCALE', g.vmath('SUBTRACT', d, (0.5, 0.5, 0.5)), escala=deshilachado))
        vf1 = g.voronoi(Q, 'F1'); ve = g.voronoi(Q, 'DISTANCE_TO_EDGE')
        sc_ = g.n("ShaderNodeSeparateColor"); g.link(g.out(vf1, "Color"), sc_.inputs[0])
        return sc_.outputs[0], sc_.outputs[1], g.out(ve, "Distance")

    # --- hebras: celdas muy alargadas a lo largo de la rama
    azar, azar2, borde = celdas(I("Ancho hebras (m)"), I("Largo hebras"), 5.1, 0.15)
    surco = g.rango(borde, 0.12, 0.0)                    # 1 en el fondo del surco
    lomo = g.rango(borde, 0.0, 0.35)                     # perfil redondeado de la hebra
    c1 = g.mezcla(g.rango(azar, 0.0, 0.6), I("Corteza oscura"), I("Corteza rojiza"))
    col = g.mezcla(g.rango(azar, 0.7, 1.0), c1, I("Corteza canela"))
    # fibra fina dentro de cada hebra
    cf = g.n("ShaderNodeCombineXYZ"); cf.inputs[0].default_value = 70.0
    cf.inputs[1].default_value = 1.2; cf.inputs[2].default_value = 1.0
    fibra, _ = g.ruido(g.vmath('MULTIPLY', Pt, cf.outputs[0]), 1.0, detalle=6.0, rugosidad=0.65)
    fibra_c = g.rango(fibra, 0.3, 0.7, -1.0, 1.0, suave=False)
    col = g.mezcla(1.0, col, g.rango(fibra_c, -1.0, 1.0, 0.8, 1.15, suave=False), 'MULTIPLY')
    col = g.mezcla(g.math('MULTIPLY', surco, 0.85), col, I("Surco"))
    alto = g.math('ADD', g.math('MULTIPLY', lomo, g.rango(azar2, 0.0, 1.0, 0.6, 1.0, suave=False)),
                  g.math('MULTIPLY', fibra_c, 0.08))
    rugo = g.math('ADD', 0.92, g.math('MULTIPLY', fibra_c, 0.03))

    # tronco bajo mas oscuro, ramas altas mas rojizas
    col = g.mezcla(g.rango(altura, 2.5, 0.0, 0.0, 0.45), col, I("Corteza oscura"))
    macro, _ = g.ruido(geo.outputs["Position"], 0.6, detalle=2.0)
    col = g.mezcla(g.rango(macro, 0.35, 0.65, 0.0, 0.3), col, I("Corteza rojiza"))

    # --- tiras sueltas: hebras anchas que se despegan, mas claras y levantadas
    az_t, az_t2, borde_t = celdas(g.math('MULTIPLY', I("Ancho hebras (m)"), 2.2),
                                  g.math('MULTIPLY', I("Largo hebras"), 0.6), 41.7, 0.25)
    hay = g.math('LESS_THAN', az_t, I("Tiras sueltas"))
    tira = g.math('MULTIPLY', hay, g.rango(borde_t, 0.01, 0.05))
    canto = g.math('MULTIPLY', tira, g.rango(borde_t, 0.12, 0.04))
    sombra = g.math('MULTIPLY', g.math('SUBTRACT', 1.0, tira),
                    g.math('MULTIPLY', hay, g.rango(borde_t, 0.05, 0.0)))
    col = g.mezcla(g.math('MULTIPLY', sombra, 0.6), col, I("Surco"))
    ct_ = g.mezcla(1.0, I("Tira suelta"), g.rango(az_t2, 0.0, 1.0, 0.75, 1.1, suave=False), 'MULTIPLY')
    ct_ = g.mezcla(1.0, ct_, g.rango(fibra_c, -1.0, 1.0, 0.85, 1.1, suave=False), 'MULTIPLY')
    ct_ = g.mezcla(g.math('MULTIPLY', canto, 0.4), ct_, I("Madera gris"))
    col = g.mezcla(tira, col, ct_)
    alto = g.math('MAXIMUM', alto, g.math('ADD', g.math('MULTIPLY', tira, 1.3),
                                         g.math('MULTIPLY', canto, 0.15)))

    # --- madera gris expuesta: manchas grandes cuyo borde sigue las hebras
    # cintas largas en el espacio de las hebras (siguen la torsion) + reparto grande
    cc = g.n("ShaderNodeCombineXYZ"); cc.inputs[0].default_value = 4.0
    cc.inputs[1].default_value = 0.5; cc.inputs[2].default_value = 1.0
    cinta, _ = g.ruido(g.vmath('MULTIPLY', Pt, cc.outputs[0]), 1.0, detalle=3.0, rugosidad=0.6)
    mz, _ = g.ruido(geo.outputs["Position"], 0.35, detalle=2.0)
    mz = g.math('ADD', g.math('MULTIPLY', mz, 0.5), g.math('MULTIPLY', cinta, 0.6))
    mz = g.math('ADD', mz, g.math('MULTIPLY', g.rango(I("Cavidad"), 0.5, 0.8), 0.15))
    mz = g.math('ADD', mz, g.math('MULTIPLY', g.math('SUBTRACT', azar, 0.5), 0.12))
    umbral = g.math('SUBTRACT', 0.79, g.math('MULTIPLY', I("Madera expuesta"), 0.4))
    madera = g.rango(mz, umbral, g.math('ADD', umbral, 0.025))
    filo = g.math('MULTIPLY', g.rango(mz, g.math('SUBTRACT', umbral, 0.04), umbral),
                  g.math('SUBTRACT', 1.0, madera))           # borde de corteza viva
    col = g.mezcla(g.math('MULTIPLY', filo, 0.7), col, I("Surco"))
    cm = g.mezcla(1.0, I("Madera gris"), g.rango(azar2, 0.0, 1.0, 0.8, 1.08, suave=False), 'MULTIPLY')
    cm = g.mezcla(1.0, cm, g.rango(fibra_c, -1.0, 1.0, 0.75, 1.1, suave=False), 'MULTIPLY')
    cm = g.mezcla(g.math('MULTIPLY', surco, 0.9), cm, I("Surco madera"))
    col = g.mezcla(madera, col, cm)
    alto = g.mezclaf(madera, alto, g.math('SUBTRACT', g.math('ADD', g.math('MULTIPLY', lomo, 0.5),
                                                              g.math('MULTIPLY', fibra_c, 0.15)), 0.3))
    rugo = g.mezclaf(madera, rugo, 0.82)

    # --- surcos por cavidad + AO
    hueco = g.rango(I("Cavidad"), 0.45, 0.2)
    col = g.mezcla(g.math('MULTIPLY', hueco, 0.6), col, I("Surco"))
    col = g.mezcla(g.math('MULTIPLY', g.math('SUBTRACT', 1.0, I("AO")), I("AO en color")),
                   col, I("Surco"))

    # --- liquen: manchas en caras altas, mas en tronco bajo/medio
    rl, _ = g.ruido(geo.outputs["Position"], 3.5, detalle=5.0, rugosidad=0.7)
    manchas = g.rango(rl, g.math('SUBTRACT', 0.68, g.math('MULTIPLY', I("Liquen"), 0.18)),
                      g.math('SUBTRACT', 0.72, g.math('MULTIPLY', I("Liquen"), 0.18)))
    orient = g.rango(nz, -0.3, 0.6, 0.35, 1.0)
    alto_ok = g.rango(altura, 6.0, 2.5, 0.3, 1.0)
    liq = g.math('MULTIPLY', g.math('MULTIPLY', manchas, orient), alto_ok)
    liq = g.math('MULTIPLY', liq, g.math('MINIMUM', g.math('MULTIPLY', I("Liquen"), 3.0), 1.0))
    ro, _ = g.ruido(geo.outputs["Position"], 9.0, detalle=2.0)
    col_liq = g.mezcla(g.rango(ro, 0.58, 0.62), I("Liquen gris"), I("Liquen naranjo"))
    col = g.mezcla(liq, col, col_liq)
    alto = g.math('ADD', alto, g.math('MULTIPLY', liq, 0.15))
    rugo = g.mezclaf(liq, rugo, 0.95)

    # --- polvo del altiplano: base del tronco y fondo de las grietas
    base = g.rango(altura, 1.2, 0.0)
    en_grieta = g.math('MULTIPLY', g.math('SUBTRACT', 1.0, I("AO")), 0.8)
    pv, _ = g.ruido(geo.outputs["Position"], 5.0, detalle=3.0)
    polvo = g.math('MULTIPLY', g.math('MAXIMUM', base, en_grieta),
                   g.rango(pv, 0.3, 0.65, 0.4, 1.0, suave=False))
    polvo = g.math('MULTIPLY', polvo, I("Polvo cantidad"), clamp=True)
    col = g.mezcla(g.math('MULTIPLY', polvo, 0.7), col, I("Polvo"))
    rugo = g.mezclaf(polvo, rugo, 0.97)

    # --- normal: mapa horneado + relieve de hebras
    bump = g.n("ShaderNodeBump")
    g.set(bump, "Strength", g.math('MULTIPLY', I("Relieve"), 0.5, clamp=False))
    g.set(bump, "Distance", 0.006)
    g.link(alto, g.inp(bump, "Height")); g.link(I("Normal"), g.inp(bump, "Normal"))

    g.link(col, go.inputs["Base Color"])
    g.link(g.math('MINIMUM', g.math('MAXIMUM', rugo, 0.0), 1.0), go.inputs["Roughness"])
    g.link(bump.outputs["Normal"], go.inputs["Normal"])
    g.link(I("AO"), go.inputs["AO"])
    g.link(alto, go.inputs["Altura"])
    _ordenar(ng)
    return ng

def _ordenar(nt):
    """Columnas por profundidad, para que el grupo se pueda leer al abrirlo."""
    prof = {n: 0 for n in nt.nodes}
    for _ in range(len(nt.nodes)):
        cambio = False
        for l in nt.links:
            if prof[l.to_node] < prof[l.from_node] + 1:
                prof[l.to_node] = prof[l.from_node] + 1; cambio = True
        if not cambio: break
    filas = {}
    for n in nt.nodes:
        k = prof[n]; filas[k] = filas.get(k, 0) + 1
        n.location = (k * 220, -filas[k] * 190)

# ---------------------------------------------------------------- material

def crear_material():
    baja = bpy.data.objects[BAJA]
    marcar_veta(baja)
    ng = crear_grupo()
    m = bpy.data.materials.get("M_Quenoa_Corteza") or bpy.data.materials.new("M_Quenoa_Corteza")
    m.use_nodes = True; nt = m.node_tree; nt.nodes.clear()
    g = G(nt)
    uvn = g.n("ShaderNodeUVMap", uv_map=UV, location=(-900, 0))
    def tex(nombre, y):
        im = bpy.data.images.get(nombre)
        n = g.n("ShaderNodeTexImage", image=im, location=(-650, y), label=nombre)
        g.link(uvn.outputs["UV"], n.inputs["Vector"])
        return n
    tn = tex("Quenoa_N_alta", 300); tao = tex("Quenoa_AO", 0); tcv = tex("Quenoa_Cavidad", -300)
    nm = g.n("ShaderNodeNormalMap", uv_map=UV, location=(-350, 300))
    g.link(tn.outputs["Color"], nm.inputs["Color"])
    grp = g.n("ShaderNodeGroup", location=(-100, 0)); grp.node_tree = ng; grp.width = 240
    g.link(uvn.outputs["UV"], grp.inputs["UV"])
    g.link(tao.outputs["Color"], grp.inputs["AO"])
    g.link(tcv.outputs["Color"], grp.inputs["Cavidad"])
    g.link(nm.outputs["Normal"], grp.inputs["Normal"])
    grp.inputs["Metros por UV"].default_value = metros_por_uv(baja)
    grp.inputs["Z suelo"].default_value = min((baja.matrix_world @ v.co).z for v in baja.data.vertices)
    bsdf = g.n("ShaderNodeBsdfPrincipled", location=(300, 0))
    for s in ("Base Color", "Roughness", "Normal"): g.link(grp.outputs[s], bsdf.inputs[s])
    bsdf.inputs["Specular IOR Level"].default_value = 0.3     # corteza seca, sin brillo
    out = g.n("ShaderNodeOutputMaterial", location=(600, 0))
    g.link(bsdf.outputs[0], out.inputs["Surface"])
    # el material anterior queda guardado (fake user) por si se quiere volver
    for s in baja.material_slots:
        if s.material and s.material != m: s.material.use_fake_user = True
    if not baja.material_slots: baja.data.materials.append(m)
    baja.material_slots[0].material = m
    return m

# ---------------------------------------------------------------- unity

def hornear_unity():
    """BaseColor (sin luz), Roughness y Normal final de M_Quenoa_Corteza."""
    m = bpy.data.materials["M_Quenoa_Corteza"]; nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    out = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
    grp = next(n for n in nt.nodes if n.type == 'GROUP')
    dest = nt.nodes.new("ShaderNodeTexImage")
    em = nt.nodes.new("ShaderNodeEmission")
    hechos = []
    with _Bake(False) as k:
        k.sc.cycles.samples = 1
        try:
            for nombre, salida, color in (("Quenoa_BaseColor", "Base Color", True),
                                          ("Quenoa_Roughness", "Roughness", False)):
                im = imagen(nombre, color); dest.image = im; nt.nodes.active = dest
                nt.links.new(grp.outputs[salida], em.inputs["Color"])
                nt.links.new(em.outputs[0], out.inputs["Surface"])
                bpy.ops.object.bake(type='EMIT'); guardar(im); hechos.append(nombre)
            nt.links.new(bsdf.outputs[0], out.inputs["Surface"])
            im = imagen("Quenoa_Normal"); dest.image = im; nt.nodes.active = dest
            bpy.ops.object.bake(type='NORMAL'); guardar(im); hechos.append("Quenoa_Normal")
        finally:
            nt.links.new(bsdf.outputs[0], out.inputs["Surface"])
            nt.nodes.remove(dest); nt.nodes.remove(em)
    return hechos

if __name__ == "__main__":
    hornear_mascaras()
    crear_material()
