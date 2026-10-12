# Corteza generica: grupo de nodos "Corteza" para cualquier tronco + bakes para Unity.
#
# crear_grupo(): arma el grupo con colores, densidad, forma y relieve. El relieve es una
#   sola altura (~0-1) armada en capas: lomos y surcos en V (voronoi estirado, con bordes
#   mordidos y ancho que cambia), cortes horizontales en bloques, laminas apiladas en la
#   pared del surco, escamas craqueladas en cm reales, fibra, costra aspera, grano micro y
#   parches donde se cayo la corteza de afuera. El normal se calcula de esa altura con
#   "Profundidad (m)" en metros reales, asi que el mapa horneado tiene la inclinacion
#   correcta y no depende de un "Strength" a ojo.
# crear_material(ob, uv, preset): material M_Corteza_<ob> con el grupo + Principled.
# aplicar_preset(material, nombre): Roble, Fisurada, Pino, Quenoa, Lisa_manchas (ver PRESETS).
#   Roble, Fisurada y Pino estan calibrados contra fotos (lomos en rombos; tiras en bloques;
#   placas con laminas y parches rojizos). Tambien hay botones en Propiedades > Material >
#   "Corteza" (los registra correr el script).
# hornear_unity(ob, uv, res): BaseColor, Roughness, Normal (OpenGL, Y+), Altura y AO en PNG
#   (<carpeta del blend>/Texturas/<ob>_*.png).
#
# Uso: con el tronco seleccionado, Run Script -> crea el material con PRESET_INICIAL.
# Coordenadas: "Usar UV" = 1 toma la UV en metros con la veta en V (como uv_madera.py);
#   0 toma las coordenadas de objeto con la veta en Z (sin costuras, sirve para troncos
#   rectos o mallas sin UV pensada). Las escalas son metros: "Densidad" = lomos por metro.
import bpy, os

PRESET_INICIAL = "Pino"
UV_POR_DEFECTO = "UV_Madera"
RES = 2048

def lin(hexstr):
    c = [int(hexstr[i:i+2], 16) / 255 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c) + (1.0,)

# ---------------------------------------------------------------- ayudante de nodos

class G:
    def __init__(self, nt): self.nt = nt
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
        return nd.outputs["Value"] if op in ("DOT_PRODUCT", "LENGTH", "DISTANCE") else nd.outputs[0]
    def xyz(self, x, y, z):
        nd = self.n("ShaderNodeCombineXYZ")
        for i, v in enumerate((x, y, z)):
            if isinstance(v, bpy.types.NodeSocket): self.link(v, nd.inputs[i])
            else: nd.inputs[i].default_value = v
        return nd.outputs[0]
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
    def mezclav(self, f, a, b):
        nd = self.n("ShaderNodeMix", data_type='VECTOR', clamp_factor=True)
        self.set(nd, "Factor_Float", f); self.set(nd, "A_Vector", a); self.set(nd, "B_Vector", b)
        return self.out(nd, "Result_Vector")
    def ruido(self, v, escala, detalle=3.0, rugosidad=0.55):
        nd = self.n("ShaderNodeTexNoise", noise_dimensions='3D')
        self.set(nd, "Vector", v); self.set(nd, "Scale", escala)
        self.set(nd, "Detail", detalle); self.set(nd, "Roughness", rugosidad)
        return self.out(nd, "Fac"), self.out(nd, "Color")
    def voronoi(self, v, rasgo='F1'):
        nd = self.n("ShaderNodeTexVoronoi", voronoi_dimensions='3D', feature=rasgo)
        self.set(nd, "Vector", v); self.set(nd, "Scale", 1.0)
        return nd

# ---------------------------------------------------------------- grupo

ENTRADAS = [
    # nombre, tipo, valor, min, max
    ("UV", "NodeSocketVector", None, None, None),
    ("Normal base", "NodeSocketVector", None, None, None),
    ("AO horneado", "NodeSocketFloat", 1.0, 0, 1),
    ("Usar UV", "NodeSocketFloat", 1.0, 0, 1),
    ("Metros por UV", "NodeSocketFloat", 1.0, 0.01, 1000),
    # colores
    ("Color oscuro", "NodeSocketColor", lin("4f4537"), None, None),
    ("Color medio", "NodeSocketColor", lin("8a7b63"), None, None),
    ("Color claro", "NodeSocketColor", lin("b5a689"), None, None),
    ("Color surco", "NodeSocketColor", lin("2a2219"), None, None),
    ("Color interior", "NodeSocketColor", lin("b0603f"), None, None),
    ("Color musgo", "NodeSocketColor", lin("5f6b37"), None, None),
    ("Variacion color", "NodeSocketFloat", 0.6, 0, 1),
    ("Claro en lomos", "NodeSocketFloat", 0.4, 0, 1),
    ("Parches", "NodeSocketFloat", 0.0, 0, 1),
    ("Musgo", "NodeSocketFloat", 0.0, 0, 1),
    # forma
    ("Densidad", "NodeSocketFloat", 9.0, 0.1, 200),          # lomos (o placas) por metro, de lado
    ("Alargamiento", "NodeSocketFloat", 6.0, 1.0, 40),       # largo/ancho de cada lomo
    ("Irregularidad", "NodeSocketFloat", 0.7, 0, 2),
    ("Bordes rotos", "NodeSocketFloat", 0.5, 0, 1),          # cantos mordidos de lomos y bloques
    ("Torsion", "NodeSocketFloat", 0.0, -2, 2),
    ("Ancho surco", "NodeSocketFloat", 0.5, 0, 1),
    ("Cortes", "NodeSocketFloat", 0.25, 0, 1),               # lomos partidos en bloques
    ("Capas", "NodeSocketFloat", 0.0, 0, 1),                 # bordes en capas apiladas
    ("Escamas", "NodeSocketFloat", 0.8, 0, 1),               # costra craquelada sobre los lomos
    ("Tamano escamas (cm)", "NodeSocketFloat", 1.5, 0.2, 20),
    ("Fibra", "NodeSocketFloat", 0.3, 0, 1),
    ("Costra", "NodeSocketFloat", 0.4, 0, 1),                # superficie aspera, en cm
    ("Detalle micro", "NodeSocketFloat", 0.5, 0, 1),
    # relieve
    ("Profundidad (m)", "NodeSocketFloat", 0.03, 0, 0.2),
    ("Oclusion", "NodeSocketFloat", 0.6, 0, 1),
    ("Rugosidad", "NodeSocketFloat", 0.88, 0, 1),
]

SALIDAS = (("Base Color", "NodeSocketColor"), ("Roughness", "NodeSocketFloat"),
           ("Normal", "NodeSocketVector"), ("Altura", "NodeSocketFloat"),
           ("AO", "NodeSocketFloat"), ("Surco", "NodeSocketFloat"))

def crear_grupo(nombre="Corteza"):
    """Arma el grupo de cero. Si ya existia, los materiales que lo usan pasan al nuevo
    conservando sus valores y conexiones (vaciar el grupo viejo los desconectaria)."""
    viejo = bpy.data.node_groups.get(nombre)
    ng = bpy.data.node_groups.new(nombre + "_nuevo", "ShaderNodeTree")
    _armar_grupo(ng)
    if viejo:
        for arbol in [m.node_tree for m in bpy.data.materials if m.node_tree] + list(bpy.data.node_groups):
            for nd in arbol.nodes:
                if nd.type == 'GROUP' and nd.node_tree == viejo: _cambiar_grupo(arbol, nd, ng)
        bpy.data.node_groups.remove(viejo)
    ng.name = nombre
    return ng

def _cambiar_grupo(arbol, nd, ng):
    valores = {s.name: s.default_value[:] if hasattr(s.default_value, "__len__") else s.default_value
               for s in nd.inputs if hasattr(s, "default_value") and not s.is_linked}
    entran = [(l.from_socket, l.to_socket.name) for l in arbol.links if l.to_node == nd]
    salen = [(l.from_socket.name, l.to_socket) for l in arbol.links if l.from_node == nd]
    nd.node_tree = ng
    for s in nd.inputs:
        if s.name in valores and hasattr(s, "default_value"): s.default_value = valores[s.name]
    for a, nombre in entran:
        if nombre in nd.inputs: arbol.links.new(a, nd.inputs[nombre])
    for nombre, b in salen:
        if nombre in nd.outputs: arbol.links.new(nd.outputs[nombre], b)

def _armar_grupo(ng):
    for n_, tipo, val, lo, hi in ENTRADAS:
        s = ng.interface.new_socket(n_, in_out='INPUT', socket_type=tipo)
        if val is not None: s.default_value = val
        if lo is not None: s.min_value = lo; s.max_value = hi
        if tipo == "NodeSocketVector": s.hide_value = True
    for n_, tipo in SALIDAS:
        ng.interface.new_socket(n_, in_out='OUTPUT', socket_type=tipo)

    g = G(ng)
    gi = g.n("NodeGroupInput"); go = g.n("NodeGroupOutput")
    I = lambda n_: gi.outputs[n_]
    tc = g.n("ShaderNodeTexCoord")
    geo = g.n("ShaderNodeNewGeometry")
    sep = lambda col: (lambda s: (s.outputs[0], s.outputs[1], s.outputs[2]))(_sep(g, col))

    # --- P en metros con la veta en Z. UV: X = alrededor (U), Z = a lo largo (V).
    # El atributo de cara "veta_u" (si existe, ver material_quenoa.py) gira las islas
    # empacadas de costado.
    uvm = g.vmath('SCALE', I("UV"), escala=I("Metros por UV"))
    su = g.n("ShaderNodeSeparateXYZ"); g.link(uvm, su.inputs[0])
    p_uv = g.xyz(su.outputs["X"], 0.0, su.outputs["Y"])
    p_uv_rot = g.xyz(su.outputs["Y"], 0.0, g.math('MULTIPLY', su.outputs["X"], -1.0))
    veta = g.n("ShaderNodeAttribute", attribute_type='GEOMETRY', attribute_name="veta_u")
    p_uv = g.mezclav(g.out(veta, "Fac"), p_uv, p_uv_rot)
    P = g.mezclav(I("Usar UV"), tc.outputs["Object"], p_uv)
    sp = g.n("ShaderNodeSeparateXYZ"); g.link(P, sp.inputs[0])
    px, py, pz = sp.outputs["X"], sp.outputs["Y"], sp.outputs["Z"]
    # torsion: los lomos suben en espiral (metros de desplazamiento por metro de altura)
    px = g.math('ADD', px, g.math('MULTIPLY', pz, I("Torsion")))
    P = g.xyz(px, py, pz)

    dens = I("Densidad")
    alarg = I("Alargamiento")
    irr = I("Irregularidad")
    Q = g.vmath('MULTIPLY', P, g.xyz(dens, dens, g.math('DIVIDE', dens, alarg)))  # 1 = un lomo
    # deformacion en dos escalas: la grande ondula los lomos, la chica les quita lo recto
    _, dcol = g.ruido(g.vmath('MULTIPLY', Q, (0.5, 0.5, 1.0)), 1.0, detalle=4.0, rugosidad=0.6)
    _, dcol2 = g.ruido(g.vmath('ADD', g.vmath('MULTIPLY', Q, (2.0, 2.0, 2.0)), (3.7, 1.3, 8.2)), 1.0, detalle=2.0)
    Qd = g.vmath('ADD', Q, g.vmath('SCALE', g.vmath('SUBTRACT', dcol, (0.5, 0.5, 0.5)), escala=irr))
    Qd = g.vmath('ADD', Qd, g.vmath('SCALE', g.vmath('SUBTRACT', dcol2, (0.5, 0.5, 0.5)),
                                    escala=g.math('MULTIPLY', irr, 0.3)))
    # bordes rotos: ruido en trozos (casi redondo; estirado con la veta parece pelo) que
    # muerde el canto de lomos y bloques
    rn, _ = g.ruido(g.vmath('MULTIPLY', P, g.xyz(g.math('MULTIPLY', dens, 5.0), g.math('MULTIPLY', dens, 5.0),
                                                g.math('MULTIPLY', dens, 3.0))), 1.0, detalle=2.0, rugosidad=0.5)
    roto = g.math('MULTIPLY', g.math('SUBTRACT', rn, 0.5), g.math('MULTIPLY', I("Bordes rotos"), 0.18))

    # ================================================================ altura
    # 1) lomos y surcos. Las celdas estiradas se tocan en bordes que se juntan y se
    # separan (el dibujo de rombos de los robles); el surco es una V con hombro redondo,
    # y su ancho cambia a lo largo: lentes que se abren y se cierran en punta.
    vf1 = g.voronoi(Qd, 'F1'); ve = g.voronoi(Qd, 'DISTANCE_TO_EDGE')
    azar, azar2, _ = sep(g.out(vf1, "Color"))
    borde = g.math('ADD', g.out(ve, "Distance"), roto)
    wn, _ = g.ruido(g.vmath('MULTIPLY', Q, (0.8, 0.8, 0.8)), 1.0, detalle=2.0)
    w = g.math('MAXIMUM', g.math('MULTIPLY', g.math('MULTIPLY', I("Ancho surco"), 0.4),
                                 g.rango(wn, 0.3, 0.7, 0.6, 1.3)), 0.001)
    s = g.rango(borde, 0.0, w, suave=False)
    u = g.math('SUBTRACT', 1.0, s)
    hombro = g.math('SUBTRACT', 1.0, g.math('MULTIPLY', g.math('MULTIPLY', u, u), u))
    domo = g.rango(borde, w, 0.5)
    h = g.math('ADD', g.math('MULTIPLY', hombro, 0.85), g.math('MULTIPLY', domo, 0.15))
    h = g.math('MULTIPLY', h, g.rango(azar2, 0.0, 1.0, 0.8, 1.0, suave=False))
    hay_s = g.math('MINIMUM', g.math('MULTIPLY', I("Ancho surco"), 6.0), 1.0)
    h = g.mezclaf(hay_s, 1.0, h)                      # "Ancho surco" = 0 -> corteza lisa

    # 2) cortes: los lomos se parten en bloques cortos, cada uno a su altura. Los cortes
    # van casi horizontales (como ladrillos), desfasados de un lomo al otro y con su propia
    # inclinacion; "Cortes" decide cuantos aparecen (todos a la vez se ven como zigzag).
    sq = g.n("ShaderNodeSeparateXYZ"); g.link(Qd, sq.inputs[0])
    nc, _ = g.ruido(g.vmath('MULTIPLY', Q, (0.35, 0.35, 3.0)), 1.0, detalle=2.0)   # en X lento: si no, zigzag
    paso = g.math('MULTIPLY', g.math('DIVIDE', alarg, 1.8), g.math('ADD', 0.6, azar2))   # cada lomo a su ritmo
    tc_ = g.math('ADD', g.math('MULTIPLY', sq.outputs["Z"], paso),
                 g.math('ADD', g.math('MULTIPLY', azar, 7.31), g.math('MULTIPLY', sq.outputs["X"], g.math('MULTIPLY', g.math('SUBTRACT', azar2, 0.5), 0.25))))
    tc_ = g.math('ADD', tc_, g.math('MULTIPLY', g.math('SUBTRACT', nc, 0.5), 0.6))
    fc = g.math('FRACT', tc_)
    dcorte = g.math('MINIMUM', fc, g.math('SUBTRACT', 1.0, fc))
    wb = g.n("ShaderNodeTexWhiteNoise", noise_dimensions='3D')
    g.link(g.xyz(g.math('FLOOR', tc_), azar, azar2), g.inp(wb, "Vector"))
    azarB = g.out(wb, "Value")
    corte = g.rango(g.math('ADD', dcorte, g.math('MULTIPLY', roto, 0.1)), 0.0, 0.14)   # punta redondeada
    wc = g.n("ShaderNodeTexWhiteNoise", noise_dimensions='3D')
    g.link(g.xyz(g.math('FLOOR', g.math('ADD', tc_, 0.5)), azar, 3.3), g.inp(wc, "Vector"))
    hay_corte = g.rango(g.out(wc, "Value"), g.math('MULTIPLY', I("Cortes"), 1.1),
                        g.math('ADD', g.math('MULTIPLY', I("Cortes"), 1.1), 0.02))
    corte_0 = corte                                   # para fundir el escalon entre bloques
    corte = g.math('MAXIMUM', corte, hay_corte)
    cortes = I("Cortes")
    # ranura de hondo parejo que no entra en la pared del surco (si entra, deja muescas en
    # V en la punta de cada celda)
    h = g.math('SUBTRACT', h, g.math('MULTIPLY', g.math('MULTIPLY', cortes, 0.75),
                                     g.math('MULTIPLY', g.math('SUBTRACT', 1.0, corte), g.rango(s, 0.3, 0.9))))
    h = g.math('ADD', h, g.math('MULTIPLY', g.math('MULTIPLY', g.math('SUBTRACT', azarB, 0.5), 0.25),
                                g.math('MULTIPLY', g.math('MULTIPLY', cortes, hombro), corte_0)))

    # 3) capas: las paredes del surco se vuelven laminas apiladas como paginas (pino).
    # La cara de la placa queda entera; el canto de cada lamina es fibroso.
    NC = 7.0
    nl, _ = g.ruido(g.vmath('MULTIPLY', Q, (2.0, 2.0, 2.0)), 1.0, detalle=3.0, rugosidad=0.6)
    fray, _ = g.ruido(g.vmath('MULTIPLY', Q, (10.0, 10.0, 3.0)), 1.0, detalle=3.0, rugosidad=0.7)
    desf = g.math('ADD', g.math('MULTIPLY', nl, 1.5), g.math('MULTIPLY', g.math('SUBTRACT', fray, 0.5), 1.8))
    t = g.math('ADD', g.math('MULTIPLY', h, NC), desf)
    fr = g.math('FRACT', t)
    peldano = g.rango(fr, 0.72, 1.0)                  # la contrahuella de cada lamina
    terr = g.math('DIVIDE', g.math('SUBTRACT', g.math('ADD', g.math('FLOOR', t), peldano), desf), NC)
    capas = g.math('MULTIPLY', I("Capas"), g.rango(hombro, 0.95, 0.7))   # solo en la pared empinada
    h = g.mezclaf(capas, h, terr)
    labio = g.math('MULTIPLY', g.rango(fr, 0.16, 0.0), capas)   # canto de la lamina de arriba
    hueco = g.math('MULTIPLY', peldano, capas)                  # sombra bajo ese canto

    # 4) escamas: costra craquelada sobre los lomos, en cm reales. Grietas de ancho
    # dispar, algunas apenas marcadas, escamas partidas en dos o tres, cada una un poco
    # inclinada (agarran la luz distinto). Sin domo: eso las hacia parecer piel de reptil.
    esc = g.math('DIVIDE', 100.0, I("Tamano escamas (cm)"))
    Pe = g.vmath('MULTIPLY', P, g.xyz(esc, esc, g.math('MULTIPLY', esc, 0.7)))
    _, dce = g.ruido(g.vmath('SCALE', Pe, escala=0.5), 1.0, detalle=3.0, rugosidad=0.55)
    Ped = g.vmath('ADD', Pe, g.vmath('SCALE', g.vmath('SUBTRACT', dce, (0.5, 0.5, 0.5)), escala=0.7))
    vfe = g.voronoi(Ped, 'F1'); vee = g.voronoi(Ped, 'DISTANCE_TO_EDGE')
    azarE, azarE2, azarE3 = sep(g.out(vfe, "Color"))
    azarE = g.math('ADD', 0.5, g.math('MULTIPLY', g.math('SUBTRACT', azarE, 0.5), 0.6))
    mk, _ = g.ruido(g.vmath('ADD', g.vmath('SCALE', Pe, escala=0.6), (2.2, 9.1, 4.4)), 1.0, detalle=2.0)
    lw = g.math('ADD', 0.025, g.math('MULTIPLY', azarE3, 0.05))
    linea_e = g.math('MULTIPLY', g.rango(g.out(vee, "Distance"), lw, 0.0),
                     g.rango(mk, 0.3, 0.5, 0.25, 1.0))
    vee2 = g.voronoi(g.vmath('ADD', g.vmath('SCALE', Ped, escala=2.3), (5.5, 2.1, 7.7)), 'DISTANCE_TO_EDGE')
    mk2, _ = g.ruido(g.vmath('ADD', g.vmath('SCALE', Pe, escala=1.3), (8.8, 0.4, 3.1)), 1.0, detalle=2.0)
    linea2 = g.math('MULTIPLY', g.rango(g.out(vee2, "Distance"), 0.035, 0.0), g.rango(mk2, 0.45, 0.6))
    linea_e = g.math('MAXIMUM', linea_e, g.math('MULTIPLY', linea2, 0.6))
    dpos = _sep(g, g.vmath('SUBTRACT', Ped, g.out(vfe, "Position")))
    incl = g.math('ADD', g.math('MULTIPLY', dpos.outputs[0], g.math('MULTIPLY', g.math('SUBTRACT', azarE2, 0.5), 0.15)),
                  g.math('MULTIPLY', dpos.outputs[2], g.math('MULTIPLY', g.math('SUBTRACT', azarE3, 0.5), 0.15)))
    sobre = g.rango(h, 0.25, 0.6)                     # solo arriba de los surcos
    peso_e = g.math('MULTIPLY', I("Escamas"), sobre)
    de = g.math('ADD', g.math('MULTIPLY', g.math('SUBTRACT', azarE, 0.5), 0.07),
                g.math('SUBTRACT', incl, g.math('MULTIPLY', linea_e, 0.09)))
    h = g.math('ADD', h, g.math('MULTIPLY', de, peso_e))

    # 5) fibra: estrias a lo largo, sobre todo en el fondo de los surcos
    fq = g.vmath('MULTIPLY', P, g.xyz(g.math('MULTIPLY', dens, 9.0), g.math('MULTIPLY', dens, 9.0),
                                       g.math('MULTIPLY', dens, 0.35)))
    fib, _ = g.ruido(fq, 1.0, detalle=5.0, rugosidad=0.6)
    fib = g.rango(fib, 0.3, 0.7, -1.0, 1.0, suave=False)
    h = g.math('ADD', h, g.math('MULTIPLY', g.math('MULTIPLY', fib, I("Fibra")),
                                g.math('SUBTRACT', 0.05, g.math('MULTIPLY', sobre, 0.025))))

    # 6) costra: la superficie aspera de la corteza vieja, en cm reales y algo estirada
    # con la veta (redonda parece estuco)
    cq = g.vmath('MULTIPLY', P, (1.0, 1.0, 0.45))
    c1, _ = g.ruido(cq, 35.0, detalle=6.0, rugosidad=0.65)
    c2, _ = g.ruido(g.vmath('ADD', cq, (4.1, 7.3, 1.9)), 120.0, detalle=3.0, rugosidad=0.6)
    costra = g.math('ADD', g.math('MULTIPLY', g.rango(c1, 0.3, 0.7, -1.0, 1.0, suave=False), 0.65),
                    g.math('MULTIPLY', g.rango(c2, 0.3, 0.7, -1.0, 1.0, suave=False), 0.35))
    dc = g.math('MULTIPLY', g.math('MULTIPLY', costra, I("Costra")),
                g.math('ADD', 0.03, g.math('MULTIPLY', sobre, 0.05)))
    h = g.math('ADD', h, dc)

    # 7) micro: grano de ~1 cm algo estirado; suave, mas que esto parece estuco
    mic, _ = g.ruido(g.vmath('MULTIPLY', P, (1.0, 1.0, 0.35)), 110.0, detalle=3.0, rugosidad=0.6)
    mic = g.rango(mic, 0.3, 0.7, -1.0, 1.0, suave=False)
    h = g.math('ADD', h, g.math('MULTIPLY', g.math('MULTIPLY', mic, I("Detalle micro")), 0.015))

    # 8) parches: donde se cayo la capa de afuera queda la de adentro, mas lisa y un
    # escalon mas abajo, con el borde mordido (los rojizos del pino, los claros de la foto 2)
    pn, _ = g.ruido(g.vmath('MULTIPLY', Q, (1.0, 1.0, 1.6)), 1.3, detalle=3.0, rugosidad=0.55)
    pj, _ = g.ruido(g.vmath('MULTIPLY', Q, (7.0, 7.0, 5.0)), 1.0, detalle=4.0, rugosidad=0.7)
    pn = g.math('ADD', pn, g.math('MULTIPLY', g.math('SUBTRACT', pj, 0.5), 0.1))
    um = g.math('SUBTRACT', 0.66, g.math('MULTIPLY', I("Parches"), 0.22))
    parche = g.math('MULTIPLY', g.rango(pn, um, g.math('ADD', um, 0.01)), g.rango(h, 0.45, 0.65))
    parche = g.math('MULTIPLY', parche, g.math('MINIMUM', g.math('MULTIPLY', I("Parches"), 5.0), 1.0))
    quita = g.math('ADD', g.math('ADD', 0.05, g.math('MULTIPLY', de, peso_e)), g.math('MULTIPLY', dc, 0.6))
    h = g.math('SUBTRACT', h, g.math('MULTIPLY', parche, quita))
    alto = g.math('MAXIMUM', h, 0.0)

    # ================================================================ color
    vc, _ = g.ruido(P, 0.9, detalle=2.0)
    m1, _ = g.ruido(P, 9.0, detalle=4.0, rugosidad=0.6)               # manchas de ~10 cm
    m2, _ = g.ruido(g.vmath('MULTIPLY', P, (1.0, 1.0, 0.6)), 35.0, detalle=3.0)
    tcol = g.math('ADD', g.math('MULTIPLY', vc, 0.4), g.math('MULTIPLY', azar, 0.2))
    tcol = g.math('ADD', tcol, g.math('ADD', g.math('MULTIPLY', g.math('SUBTRACT', m1, 0.5), 0.6),
                                      g.math('MULTIPLY', g.math('SUBTRACT', m2, 0.5), 0.3)))
    tcol = g.math('ADD', tcol, g.math('MULTIPLY', g.math('MULTIPLY', g.math('SUBTRACT', azarE, 0.5), 0.5),
                                      g.math('MULTIPLY', I("Variacion color"), g.math('ADD', 0.3, peso_e))))
    tcol = g.math('ADD', tcol, g.math('MULTIPLY', g.math('SUBTRACT', azarB, 0.5), g.math('MULTIPLY', cortes, 0.3)))
    tcol = g.mezclaf(I("Variacion color"), 0.5, g.math('ADD', tcol, 0.1))
    col = g.mezcla(g.rango(tcol, 0.15, 0.55), I("Color oscuro"), I("Color medio"))
    col = g.mezcla(g.rango(tcol, 0.55, 0.9), col, I("Color claro"))
    col = g.mezcla(g.math('MULTIPLY', g.rango(alto, 0.8, 1.05), I("Claro en lomos")), col, I("Color claro"))
    col = g.mezcla(1.0, col, g.rango(fib, -1.0, 1.0, 0.94, 1.05, suave=False), 'MULTIPLY')
    # costra: lo hundido mas oscuro, lo que sobresale mas claro
    cav = g.mezclaf(I("Costra"), 1.0, g.rango(costra, -1.0, 1.0, 0.75, 1.15, suave=False))
    col = g.mezcla(1.0, col, g.xyz(cav, cav, cav), 'MULTIPLY')
    col = g.mezcla(g.math('MULTIPLY', linea_e, g.math('MULTIPLY', peso_e, 0.2)), col, I("Color surco"))
    # laminas: pardas, con el canto claro (entre interior y claro) y la sombra bajo el canto
    col = g.mezcla(g.math('MULTIPLY', capas, 0.85), col, g.mezcla(0.55, I("Color interior"), I("Color surco")))
    col = g.mezcla(g.math('MULTIPLY', labio, 0.7), col, g.mezcla(0.5, I("Color interior"), I("Color claro")))
    col = g.mezcla(g.math('MULTIPLY', hueco, 0.6), col, I("Color surco"))
    col_i = g.mezcla(1.0, I("Color interior"), g.rango(g.math('ADD', vc, g.math('MULTIPLY', m1, 0.5)),
                                                     0.2, 1.3, 0.75, 1.2, suave=False), 'MULTIPLY')
    col_i = g.mezcla(g.rango(m2, 0.3, 0.75, 0.0, 0.5), col_i, col)   # restos de la capa de afuera
    col = g.mezcla(g.math('MULTIPLY', parche, 0.85), col, col_i)
    # el fondo del surco oscuro; donde hay laminas, menos (se ven iluminadas en la foto 1)
    surco = g.rango(alto, 0.4, 0.0)
    col = g.mezcla(g.math('MULTIPLY', surco, g.math('SUBTRACT', 0.9, g.math('MULTIPLY', capas, 0.5))),
                   col, I("Color surco"))
    ao = g.math('MULTIPLY', g.math('SUBTRACT', 1.0, g.math('MULTIPLY', g.rango(alto, 0.55, 0.0), I("Oclusion"))),
                I("AO horneado"))
    col = g.mezcla(1.0, col, g.xyz(ao, ao, ao), 'MULTIPLY')
    # musgo: manchas grandes que prefieren los surcos y las caras que miran arriba
    mg, _ = g.ruido(P, 2.5, detalle=5.0, rugosidad=0.7)
    snz = g.n("ShaderNodeSeparateXYZ"); g.link(geo.outputs["Normal"], snz.inputs[0])
    musgo = g.math('ADD', mg, g.math('MULTIPLY', g.math('SUBTRACT', 1.0, alto), 0.3))
    musgo = g.math('ADD', musgo, g.math('MULTIPLY', snz.outputs["Z"], 0.15))
    umg = g.math('SUBTRACT', 0.95, g.math('MULTIPLY', I("Musgo"), 0.5))
    musgo = g.rango(musgo, umg, g.math('ADD', umg, 0.06))
    musgo = g.math('MULTIPLY', musgo, g.math('MINIMUM', g.math('MULTIPLY', I("Musgo"), 4.0), 1.0))
    col_mg = g.mezcla(1.0, I("Color musgo"), g.rango(mic, -1.0, 1.0, 0.75, 1.15, suave=False), 'MULTIPLY')
    col = g.mezcla(musgo, col, col_mg)
    alto_n = g.math('ADD', alto, g.math('MULTIPLY', musgo, 0.08))

    # ================================================================ rugosidad y normal
    rugo = g.math('ADD', I("Rugosidad"), g.math('MULTIPLY', surco, 0.08))
    rugo = g.math('SUBTRACT', rugo, g.math('MULTIPLY', parche, 0.12))
    rugo = g.mezclaf(musgo, rugo, 0.95)
    rugo = g.math('MINIMUM', g.math('MAXIMUM', rugo, 0.0), 1.0)
    # la altura (~0-1) por la profundidad en metros: inclinacion real del relieve
    bump = g.n("ShaderNodeBump")
    g.set(bump, "Strength", 1.0)
    g.set(bump, "Distance", I("Profundidad (m)"))
    g.link(alto_n, g.inp(bump, "Height")); g.link(I("Normal base"), g.inp(bump, "Normal"))

    g.link(col, go.inputs["Base Color"])
    g.link(rugo, go.inputs["Roughness"])
    g.link(bump.outputs["Normal"], go.inputs["Normal"])
    g.link(g.math('MINIMUM', alto_n, 1.0), go.inputs["Altura"])
    g.link(ao, go.inputs["AO"])
    g.link(surco, go.inputs["Surco"])
    _ordenar(ng)

def _sep(g, col):
    nd = g.n("ShaderNodeSeparateColor"); g.link(col, nd.inputs[0])
    return nd

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

# ---------------------------------------------------------------- presets

PRESETS = {
    # foto 3: lomos sinuosos que se juntan y se separan (rombos), surcos en V hondos,
    # costra craquelada fina encima; gris-beige parejo
    "Roble": {
        "Color oscuro": "6f6352", "Color medio": "9c8e76", "Color claro": "c4b89f",
        "Color surco": "3a3024", "Color interior": "c9b393", "Densidad": 18.0, "Alargamiento": 5.0,
        "Irregularidad": 0.8, "Bordes rotos": 0.35, "Torsion": 0.0, "Ancho surco": 0.3, "Cortes": 0.15,
        "Capas": 0.0, "Escamas": 0.45, "Tamano escamas (cm)": 0.6, "Fibra": 0.1, "Costra": 0.35,
        "Detalle micro": 0.5, "Profundidad (m)": 0.03, "Parches": 0.0, "Musgo": 0.0,
        "Variacion color": 0.5, "Claro en lomos": 0.35, "Oclusion": 0.6,
    },
    # foto 2: tiras angostas partidas en bloques cortos, muy costrosa, calida, con claros
    # donde salto la corteza
    "Fisurada": {
        "Color oscuro": "6f5a45", "Color medio": "9e8669", "Color claro": "c9b395",
        "Color surco": "2a1d14", "Color interior": "dcc6a4", "Densidad": 35.0, "Alargamiento": 8.0,
        "Irregularidad": 0.6, "Bordes rotos": 0.5, "Torsion": 0.0, "Ancho surco": 0.45, "Cortes": 0.6,
        "Capas": 0.0, "Escamas": 0.1, "Tamano escamas (cm)": 0.8, "Fibra": 0.4, "Costra": 1.0,
        "Detalle micro": 0.5, "Profundidad (m)": 0.02, "Parches": 0.2, "Musgo": 0.0,
        "Variacion color": 0.8, "Claro en lomos": 0.35, "Oclusion": 0.6,
    },
    # foto 1: placas grandes gris claro, surcos anchos llenos de laminas apiladas pardas,
    # parches rosado-anaranjados donde se cayo la capa gris
    "Pino": {
        "Color oscuro": "7a7166", "Color medio": "a39b90", "Color claro": "d2ccc2",
        "Color surco": "3b281b", "Color interior": "c27a55", "Densidad": 7.0, "Alargamiento": 1.6,
        "Irregularidad": 0.9, "Bordes rotos": 0.6, "Torsion": 0.0, "Ancho surco": 0.6, "Cortes": 0.2,
        "Capas": 0.9, "Escamas": 0.5, "Tamano escamas (cm)": 3.5, "Fibra": 0.15, "Costra": 0.3,
        "Detalle micro": 0.5, "Profundidad (m)": 0.05, "Parches": 0.45, "Musgo": 0.0,
        "Variacion color": 0.6, "Claro en lomos": 0.35, "Oclusion": 0.55,
    },
    # hebras pardo-rojizas retorcidas, cintas de madera gris (Polylepis)
    "Quenoa": {
        "Color oscuro": "4a2516", "Color medio": "8a3a1e", "Color claro": "a85c34",
        "Color surco": "1e0f09", "Color interior": "857f76", "Densidad": 28.0, "Alargamiento": 14.0,
        "Irregularidad": 0.3, "Bordes rotos": 0.3, "Torsion": 0.3, "Ancho surco": 0.45, "Cortes": 0.1, "Capas": 0.0,
        "Escamas": 0.1, "Tamano escamas (cm)": 1.0, "Fibra": 0.8, "Costra": 0.2, "Detalle micro": 0.5,
        "Profundidad (m)": 0.012, "Parches": 0.35, "Musgo": 0.0, "Variacion color": 0.6,
        "Claro en lomos": 0.3, "Oclusion": 0.6,
    },
    # corteza lisa que se desprende en parches (platano, eucalipto, arrayan)
    "Lisa_manchas": {
        "Color oscuro": "55553f", "Color medio": "77745a", "Color claro": "959076",
        "Color surco": "3a382c", "Color interior": "b8ac85", "Densidad": 4.0, "Alargamiento": 1.3,
        "Irregularidad": 1.0, "Bordes rotos": 0.0, "Torsion": 0.0, "Ancho surco": 0.0, "Cortes": 0.0, "Capas": 0.0,
        "Escamas": 0.0, "Tamano escamas (cm)": 2.0, "Fibra": 0.3, "Costra": 0.1, "Detalle micro": 0.5,
        "Profundidad (m)": 0.004, "Parches": 0.9, "Musgo": 0.0, "Variacion color": 0.8,
        "Claro en lomos": 0.2, "Oclusion": 0.1,
    },
}

def _nodo_grupo(m):
    return next(n for n in m.node_tree.nodes if n.type == 'GROUP' and n.node_tree
                and n.node_tree.name.startswith("Corteza"))

def aplicar_preset(m, nombre):
    grp = _nodo_grupo(m)
    for k, v in PRESETS[nombre].items():
        grp.inputs[k].default_value = lin(v) if isinstance(v, str) else v

# ---------------------------------------------------------------- material

def metros_por_uv(ob, uv):
    import bmesh
    bm = bmesh.new(); bm.from_mesh(ob.data); capa = bm.loops.layers.uv[uv]
    a3 = auv = 0.0
    for f in bm.faces:
        a3 += f.calc_area(); p = [l[capa].uv for l in f.loops]
        auv += abs(sum(p[i].x * p[i-1].y - p[i-1].x * p[i].y for i in range(len(p)))) / 2
    bm.free()
    sx, sy, sz = ob.matrix_world.to_scale()
    return ((a3 / auv) ** 0.5) * (abs(sx * sy * sz) ** (1 / 3)) if auv > 0 else 1.0

def _uv_de(ob, uv):
    """UV a usar, o None si la malla final no tiene (p. ej. un Skin modifier no genera
    UV aunque la malla base tenga una): entonces van coordenadas de objeto."""
    ev = ob.evaluated_get(bpy.context.evaluated_depsgraph_get())
    me = ev.to_mesh()
    finales = {u.name for u in me.uv_layers}
    ev.to_mesh_clear()
    if not finales: return None
    capas = ob.data.uv_layers
    if uv and uv in capas: return uv
    if UV_POR_DEFECTO in capas: return UV_POR_DEFECTO
    return capas.active.name if capas.active else None

def crear_material(ob=None, uv=None, preset=PRESET_INICIAL):
    ob = ob or bpy.context.object
    uv = _uv_de(ob, uv)
    ng = bpy.data.node_groups.get("Corteza") or crear_grupo()
    nombre = "M_Corteza_" + ob.name
    m = bpy.data.materials.get(nombre) or bpy.data.materials.new(nombre)
    m.use_nodes = True; nt = m.node_tree; nt.nodes.clear()
    g = G(nt)
    uvn = g.n("ShaderNodeUVMap", location=(-600, 100))
    if uv: uvn.uv_map = uv
    geo = g.n("ShaderNodeNewGeometry", location=(-600, -150))
    grp = g.n("ShaderNodeGroup", location=(-300, 0)); grp.node_tree = ng; grp.width = 240
    g.link(uvn.outputs["UV"], grp.inputs["UV"])
    # para sumar un normal horneado de alta: Normal Map -> "Normal base" en vez de esto
    g.link(geo.outputs["Normal"], grp.inputs["Normal base"])
    grp.inputs["Usar UV"].default_value = 1.0 if uv else 0.0
    if uv: grp.inputs["Metros por UV"].default_value = metros_por_uv(ob, uv)
    bsdf = g.n("ShaderNodeBsdfPrincipled", location=(100, 0))
    for s in ("Base Color", "Roughness", "Normal"): g.link(grp.outputs[s], bsdf.inputs[s])
    bsdf.inputs["Specular IOR Level"].default_value = 0.3     # corteza seca, casi sin brillo
    out = g.n("ShaderNodeOutputMaterial", location=(400, 0))
    g.link(bsdf.outputs[0], out.inputs["Surface"])
    if preset: aplicar_preset(m, preset)
    # el material anterior queda guardado (fake user) por si se quiere volver
    for s in ob.material_slots:
        if s.material and s.material != m: s.material.use_fake_user = True
    if not ob.material_slots: ob.data.materials.append(m)
    ob.material_slots[0].material = m
    return m

# ---------------------------------------------------------------- bakes para unity

def carpeta():
    base = os.path.dirname(bpy.data.filepath) or bpy.app.tempdir
    d = os.path.join(base, "Texturas"); os.makedirs(d, exist_ok=True)
    return d

def _imagen(nombre, res, color):
    im = bpy.data.images.get(nombre)
    if im is None or tuple(im.size) != (res, res):
        if im: bpy.data.images.remove(im)
        im = bpy.data.images.new(nombre, res, res, alpha=False, float_buffer=not color)
    im.colorspace_settings.name = 'sRGB' if color else 'Non-Color'
    return im

def _guardar(im, d):
    im.filepath_raw = os.path.join(d, im.name + ".png")
    im.file_format = 'PNG'
    im.save()

def hornear_unity(ob=None, uv=None, res=RES):
    """Base Color, Roughness, Altura y AO salen del grupo (emision); el Normal es el del
    material completo en espacio tangente OpenGL (Y+), que es lo que espera Unity."""
    ob = ob or bpy.context.object
    uv = _uv_de(ob, uv)
    if uv is None:
        raise RuntimeError(f"{ob.name} no tiene UV en la malla final: hay que aplicar los "
                           "modificadores (Skin no genera UV) y desplegarla antes de hornear")
    m = ob.material_slots[0].material; nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    out = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
    grp = _nodo_grupo(m)
    sc = bpy.context.scene
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    previo = (sc.render.engine, sc.cycles.samples, ob.data.uv_layers.active.name,
              list(bpy.context.selected_objects), bpy.context.view_layer.objects.active)
    sc.render.engine = 'CYCLES'; sc.cycles.samples = 1
    ob.data.uv_layers.active = ob.data.uv_layers[uv]
    for o in previo[3]: o.select_set(False)
    ob.select_set(True); bpy.context.view_layer.objects.active = ob
    b = sc.render.bake
    b.use_selected_to_active = False; b.margin = 16; b.use_clear = True
    b.normal_space = 'TANGENT'
    b.normal_r, b.normal_g, b.normal_b = 'POS_X', 'POS_Y', 'POS_Z'
    dest = nt.nodes.new("ShaderNodeTexImage"); em = nt.nodes.new("ShaderNodeEmission")
    d = carpeta(); pref = ob.name; hechos = []
    try:
        for sufijo, salida, color in (("BaseColor", "Base Color", True), ("Roughness", "Roughness", False),
                                      ("Altura", "Altura", False), ("AO", "AO", False)):
            im = _imagen(f"{pref}_{sufijo}", res, color); dest.image = im; nt.nodes.active = dest
            nt.links.new(grp.outputs[salida], em.inputs["Color"])
            nt.links.new(em.outputs[0], out.inputs["Surface"])
            bpy.ops.object.bake(type='EMIT'); _guardar(im, d); hechos.append(im.filepath_raw)
        nt.links.new(bsdf.outputs[0], out.inputs["Surface"])
        im = _imagen(f"{pref}_Normal", res, False); dest.image = im; nt.nodes.active = dest
        bpy.ops.object.bake(type='NORMAL'); _guardar(im, d); hechos.append(im.filepath_raw)
    finally:
        nt.links.new(bsdf.outputs[0], out.inputs["Surface"])
        nt.nodes.remove(dest); nt.nodes.remove(em)
        sc.render.engine, sc.cycles.samples = previo[0], previo[1]
        ob.data.uv_layers.active = ob.data.uv_layers[previo[2]]
        ob.select_set(False)
        for o in previo[3]: o.select_set(True)
        bpy.context.view_layer.objects.active = previo[4]
    return hechos

# ---------------------------------------------------------------- panel de presets
# Propiedades del material > "Corteza": un boton por preset. Despues se retoca a mano en
# el nodo del grupo (colores, Densidad, etc.). Dura hasta cerrar Blender: volver a correr
# el script para tenerlo de nuevo.

class CORTEZA_OT_preset(bpy.types.Operator):
    bl_idname = "corteza.preset"; bl_label = "Aplicar preset"; bl_options = {'REGISTER', 'UNDO'}
    nombre: bpy.props.StringProperty()
    def execute(self, context):
        m = context.object.active_material
        aplicar_preset(m, self.nombre)
        return {'FINISHED'}

class CORTEZA_OT_material(bpy.types.Operator):
    bl_idname = "corteza.material"; bl_label = "Crear material de corteza"; bl_options = {'REGISTER', 'UNDO'}
    def execute(self, context):
        crear_material(context.object)
        return {'FINISHED'}

class CORTEZA_PT_panel(bpy.types.Panel):
    bl_label = "Corteza"; bl_space_type = 'PROPERTIES'; bl_region_type = 'WINDOW'; bl_context = "material"
    @classmethod
    def poll(cls, context): return context.object and context.object.type == 'MESH'
    def draw(self, context):
        col = self.layout.column(align=True)
        m = context.object.active_material
        try: _nodo_grupo(m)
        except (StopIteration, AttributeError):
            col.operator("corteza.material"); return
        for nombre in PRESETS: col.operator("corteza.preset", text=nombre).nombre = nombre

CLASES = (CORTEZA_OT_preset, CORTEZA_OT_material, CORTEZA_PT_panel)

def registrar():
    for c in CLASES:
        viejo = getattr(bpy.types, c.__name__, None)
        if viejo: bpy.utils.unregister_class(viejo)
        bpy.utils.register_class(c)

if __name__ == "__main__":
    registrar()
    crear_grupo()       # rehace el grupo con lo que diga este script; los materiales siguen
    if bpy.context.object and bpy.context.object.type == 'MESH':
        crear_material()
