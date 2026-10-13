# Grupos de nodos procedurales para las casas: tinglado pintado, calamina (latón),
# madera y aluminio. Todo en metros, coordenadas alineadas al mundo y relativas
# al origen del objeto (mover la casa no hace "nadar" la textura).
import bpy
from math import pi
from nodos_util import B, col, auto_layout, nuevo_grupo, coords

F = 'NodeSocketFloat'
C = 'NodeSocketColor'
V = 'NodeSocketVector'


# ---------------------------------------------------------------- tinglado
def grupo_tinglado():
    g, gi, go = nuevo_grupo('TI_Tinglado_Pintado', [
        ('Color Pintura', C, col(0.55, 0.12, 0.08)),
        ('Desgaste', F, 0.35, 0.0, 1.0),
        ('Suciedad', F, 0.5, 0.0, 1.0),
        ('Ancho Tabla', F, 0.16, 0.05, 0.5),
        ('Largo Tabla', F, 3.2, 0.5, 10.0),
        ('Relieve', F, 1.0, 0.0, 4.0),
    ], [('Base Color', C), ('Roughness', F), ('Height', F)])
    b = B(g)
    I = gi.outputs
    c = coords(b)
    P, U = c['P'], c['U']
    _, _, Z = b.sep(P)

    # tabla: índice b y posición f (0 = canto inferior, 1 = canto superior)
    zb = b.m('DIVIDE', Z, I['Ancho Tabla'])
    fila = b.floor(zb)
    f = b.sub(zb, fila)
    r_fila = b.white(w=fila, dims='1D')

    # perfil del traslape: grueso abajo, delgado arriba, canto inferior redondeado
    perfil = b.madd(f, -0.85, 1.0)
    canto = b.mr(f, 0.0, 0.05, 0.55, 1.0, 'SMOOTHSTEP')
    h_perfil = b.mul(b.mul(perfil, canto), 0.02)

    # juntas de tope entre tablas (desfasadas por fila)
    su = b.m('DIVIDE', b.madd(r_fila, 7.31, U), I['Largo Tabla'])
    sf = b.fract(su)
    d_junta = b.mul(b.minf(sf, b.sub(1.0, sf)), I['Largo Tabla'])
    junta = b.sub(1.0, b.ss(d_junta, 0.001, 0.0045))
    id_tabla = b.xyz(fila, b.floor(su), 0.0)
    r_tabla = b.white(vec=id_tabla)

    # veta de la madera bajo la pintura (estirada a lo largo de la tabla)
    vg = b.xyz(b.mul(U, 0.9), b.mul(Z, 28.0), b.mul(r_tabla, 23.0))
    veta = b.noise(vg, 1.2, 6.0, 0.6, 0.3)
    veta_l = b.ss(veta, 0.38, 0.66)

    # desconchado de la pintura
    abajo = b.sub(1.0, b.ss(Z, 0.0, 0.8))
    canto_inf = b.sub(1.0, b.ss(f, 0.0, 0.14))
    desg = b.mul(I['Desgaste'], 0.62)
    desg = b.madd(abajo, 0.22, desg)
    desg = b.madd(canto_inf, 0.12, desg)
    desg = b.madd(junta, 0.2, desg)
    pv = b.v('MULTIPLY', P, (1.0, 1.0, 1.6))
    n_chip = b.noise(pv, 1.5, 7.0, 0.58, 0.25)
    n_chip = b.madd(veta, 0.1, n_chip)
    umbral = b.madd(desg, -0.34, 0.8)
    chip = b.mr(b.sub(n_chip, umbral), 0.0, 0.03)

    # color de la pintura: variación por tabla + decoloración por sol
    pint = b.hsv(I['Color Pintura'], b.madd(r_tabla, 0.016, 0.492), b.madd(r_tabla, 0.08, 0.96), b.madd(r_tabla, 0.1, 0.95))
    n_sol = b.noise(b.v('MULTIPLY', P, (0.35, 0.35, 0.9)), 1.0, 3.0, 0.5)
    sol = b.mul(b.ss(n_sol, 0.4, 0.75), b.madd(I['Desgaste'], 0.3, 0.05))
    pint_dec = b.mix(0.22, pint, col(0.62, 0.60, 0.56), blend='SCREEN')
    pint_dec = b.hsv(pint_dec, 0.5, 0.75, 1.0)
    pint = b.mix(sol, pint, pint_dec)
    pint = b.mix(b.mul(veta_l, 0.12), pint, col(0.0, 0.0, 0.0), blend='MULTIPLY')

    # madera expuesta: gris plateado de intemperie
    mad = b.mix(veta_l, col(0.11, 0.09, 0.07), col(0.27, 0.23, 0.18))
    color = b.mix(chip, pint, mad)

    # sombra en la junta y bajo el traslape (cavidad)
    cav = b.maxf(junta, b.sub(1.0, b.ss(f, 0.0, 0.035)))
    color = b.mix(b.mul(cav, 0.55), color, col(0.08, 0.07, 0.06), blend='MULTIPLY')

    # suciedad: salpicado del suelo, chorreado vertical y algo de alga verde (clima lluvioso)
    n_suelo = b.noise(b.v('MULTIPLY', P, (3.0, 3.0, 3.0)), 1.0, 6.0, 0.6)
    suelo = b.sub(1.0, b.ss(b.madd(n_suelo, 0.45, Z), 0.15, 0.95))
    chorreo_v = b.xyz(b.mul(U, 9.0), b.mul(Z, 0.35), 0.0)
    chorreo = b.ss(b.noise(chorreo_v, 1.0, 5.0, 0.55), 0.52, 0.72)
    suc = b.clamp01(b.madd(chorreo, 0.4, b.mul(suelo, 0.95)))
    suc = b.mul(suc, I['Suciedad'])
    color = b.mix(b.mul(suc, 0.75), color, col(0.42, 0.36, 0.28), blend='MULTIPLY')
    n_alga = b.noise(b.v('MULTIPLY', P, (4.0, 4.0, 4.0)), 1.0, 5.0, 0.55)
    alga = b.mul(b.mul(b.sub(1.0, b.ss(Z, 0.0, 0.45)), b.ss(n_alga, 0.45, 0.7)), b.mul(I['Suciedad'], 0.7))
    color = b.mix(alga, color, col(0.13, 0.16, 0.07))

    # rugosidad
    rough = b.madd(sol, 0.18, 0.5)
    rough = b.lerp(chip, rough, 0.86)
    rough = b.lerp(suc, rough, 0.92)

    # altura (metros)
    h = b.madd(junta, -0.003, h_perfil)
    h = b.madd(veta, 0.0008, h)
    h = b.madd(b.sub(1.0, chip), 0.0004, h)
    h = b.madd(b.mul(chip, veta_l), 0.0005, h)
    h = b.mul(h, I['Relieve'])

    b.L(color, go.inputs['Base Color'])
    b.L(rough, go.inputs['Roughness'])
    b.L(h, go.inputs['Height'])
    auto_layout(g)
    return g


# ---------------------------------------------------------------- calamina
def grupo_calamina():
    g, gi, go = nuevo_grupo('TI_Calamina', [
        ('Oxido', F, 0.4, 0.0, 1.0),
        ('Color Pintura', C, col(0.45, 0.08, 0.05)),
        ('Pintado', F, 0.0, 0.0, 1.0),
        ('Paso Onda', F, 0.076, 0.02, 0.3),
        ('Relieve', F, 1.0, 0.0, 4.0),
        ('Suavizar Lejos', F, 1.0, 0.0, 1.0),
    ], [('Base Color', C), ('Metallic', F), ('Roughness', F), ('Height', F)])
    b = B(g)
    I = gi.outputs
    c = coords(b)
    P, U, S = c['P'], c['U'], c['S']

    # ondulación (a lo largo de la cumbrera, las ondas bajan por la pendiente)
    fase = b.m('DIVIDE', U, I['Paso Onda'])
    onda = b.madd(b.sin(b.mul(fase, 2 * pi)), 0.5, 0.5)       # 1 = cresta
    # antialias: lejos de la cámara la onda es más chica que un pixel y produce
    # muaré; se apaga el relieve y se compensa con rugosidad (poner 0 al hornear)
    cam = b.n('ShaderNodeCameraData')
    lejos = b.mul(b.ss(cam.outputs['View Distance'], 7.0, 22.0), I['Suavizar Lejos'])
    h = b.mul(b.mul(onda, 0.018), b.madd(lejos, -0.85, 1.0))
    # en los cantos (caras no horizontales) la onda se atenúa: si no, rayas cebra
    _, _, nz = b.sep(c['N'])
    plano = b.mr(b.absf(nz), 0.55, 0.85, 0.0, 1.0, 'SMOOTHSTEP')
    h = b.mul(h, plano)

    # traslapes horizontales entre planchas (cada 2.5 m pendiente abajo)
    sp = b.fract(b.m('DIVIDE', S, 2.5))
    h = b.madd(b.sub(1.0, sp), 0.0018, h)
    linea_tras = b.sub(1.0, b.ss(sp, 0.0, 0.012))

    # tornillos en las crestas, cada 3 ondas y cada 0.9 m
    uu = b.sub(fase, 0.25)
    cu = b.mul(b.sub(b.fract(b.madd(uu, 1.0 / 3.0, 0.5)), 0.5), b.mul(I['Paso Onda'], 3.0))
    cs = b.mul(b.sub(b.fract(b.madd(S, 1.0 / 0.9, 0.5)), 0.5), 0.9)
    d = b.v('LENGTH', b.xyz(cu, cs, 0.0))
    torn = b.sub(1.0, b.ss(d, 0.0055, 0.0075))
    h = b.madd(torn, 0.003, h)
    # chorreo de óxido bajo cada tornillo
    ancho = b.sub(1.0, b.ss(b.absf(cu), 0.002, 0.011))
    largo = b.mul(b.ss(cs, 0.0, 0.01), b.sub(1.0, b.ss(cs, 0.0, 0.38)))
    chorro_t = b.mul(b.mul(ancho, largo), b.ss(b.noise(b.xyz(b.mul(U, 40.0), b.mul(S, 3.0), 0.0), 1.0, 3.0, 0.5), 0.35, 0.6))
    halo = b.sub(1.0, b.ss(d, 0.007, 0.02))

    # zinc galvanizado con flor (spangle)
    _, cel = b.voronoi(b.v('MULTIPLY', P, (1.0, 1.0, 1.0)), 28.0)
    _, _, celv = b.sep(cel)
    zinc = b.mix(celv, col(0.56, 0.57, 0.58), col(0.70, 0.71, 0.72))
    rough = b.madd(celv, 0.12, 0.36)
    metal = 1.0

    # envejecido: zinc mate (óxido blanco)
    n_mate = b.noise(b.v('MULTIPLY', P, (0.8, 0.8, 0.8)), 1.0, 5.0, 0.6)
    mate = b.ss(n_mate, 0.4, 0.7)
    zinc = b.mix(mate, zinc, col(0.50, 0.51, 0.50))
    rough = b.lerp(mate, rough, 0.68)

    # óxido: manchas estiradas pendiente abajo, más en valles y traslapes
    vox = b.xyz(b.mul(U, 2.2), b.mul(S, 0.75), 0.0)
    n_ox = b.noise(vox, 1.3, 10.0, 0.66, 0.2)
    valle = b.sub(1.0, onda)
    ox_m = b.madd(valle, 0.08, n_ox)
    ox_m = b.madd(linea_tras, 0.15, ox_m)
    umbral = b.madd(I['Oxido'], -0.36, 0.82)
    ox = b.mr(b.sub(ox_m, umbral), 0.0, 0.06)
    ox = b.maxf(ox, b.mul(chorro_t, b.madd(I['Oxido'], 0.6, 0.4)))
    # chorreados largos bajando por la pendiente
    vlar = b.xyz(b.mul(U, 6.0), b.mul(S, 0.35), 0.0)
    n_lar = b.noise(vlar, 1.0, 6.0, 0.6, 0.4)
    largo_ox = b.mul(b.mr(n_lar, b.madd(I['Oxido'], -0.08, 0.78), 0.86), b.ss(I['Oxido'], 0.2, 0.7))
    ox = b.maxf(ox, b.mul(largo_ox, 0.75))
    # zonas pardas difusas (óxido ralo que tiñe el zinc)
    n_tin = b.noise(b.v('MULTIPLY', P, (0.5, 0.5, 0.5)), 1.0, 4.0, 0.55)
    tinte = b.mul(b.ss(n_tin, 0.42, 0.7), b.mul(I['Oxido'], 0.55))
    ox = b.maxf(ox, b.mul(halo, b.mul(I['Oxido'], 0.9)))
    n_oxc = b.noise(b.v('MULTIPLY', P, (6.0, 6.0, 6.0)), 1.0, 6.0, 0.6)
    oxc = b.mix(n_oxc, col(0.075, 0.035, 0.018), col(0.17, 0.08, 0.04))

    # pintura opcional (calamina pintada)
    pint = b.mix(b.mul(mate, 0.1), I['Color Pintura'], col(0.6, 0.6, 0.6), blend='SCREEN')
    # pintura decolorada por el sol y pelada (deja ver el zinc), más con más vejez
    n_dec = b.noise(b.v('MULTIPLY', P, (0.6, 0.6, 0.6)), 1.0, 3.0, 0.5)
    dec = b.mul(b.ss(n_dec, 0.4, 0.75), b.madd(I['Oxido'], 0.3, 0.05))
    pint = b.mix(dec, pint, b.hsv(b.mix(0.15, pint, col(0.55, 0.53, 0.5), blend='SCREEN'), 0.5, 0.75, 1.0))
    n_pel = b.noise(b.v('MULTIPLY', P, (1.6, 1.6, 1.6)), 1.0, 9.0, 0.62, 0.2)
    n_pel = b.madd(valle, 0.06, n_pel)
    pel = b.mr(b.sub(n_pel, b.madd(I['Oxido'], -0.3, 0.8)), 0.0, 0.03)
    pf = b.mul(b.mul(I['Pintado'], b.sub(1.0, ox)), b.sub(1.0, pel))
    base = b.mix(pf, zinc, pint)
    metal = b.lerp(pf, 1.0, 0.0)
    rough = b.lerp(pf, rough, 0.55)

    base = b.mix(tinte, base, col(0.33, 0.22, 0.14), blend='MULTIPLY')
    rough = b.lerp(tinte, rough, 0.75)
    base = b.mix(ox, base, oxc)
    metal = b.lerp(ox, metal, 0.0)
    rough = b.lerp(ox, rough, 0.88)
    torn_c = b.mix(torn, base, col(0.25, 0.25, 0.26))
    base = torn_c

    rough = b.madd(lejos, 0.14, rough)
    h = b.madd(ox, 0.0004, h)
    h = b.madd(n_oxc, b.mul(ox, 0.0006), h)
    h = b.mul(h, I['Relieve'])

    b.L(base, go.inputs['Base Color'])
    b.L(metal, go.inputs['Metallic'])
    b.L(rough, go.inputs['Roughness'])
    b.L(h, go.inputs['Height'])
    auto_layout(g)
    return g


# ---------------------------------------------------------------- madera
def grupo_madera():
    g, gi, go = nuevo_grupo('TI_Madera', [
        ('Veta', V, (0.0, 0.0, 1.0)),
        ('Color Claro', C, col(0.42, 0.25, 0.13)),
        ('Color Oscuro', C, col(0.18, 0.09, 0.04)),
        ('Gris', F, 0.2, 0.0, 1.0),
        ('Anillos por metro', F, 45.0, 5.0, 300.0),
        ('Rugosidad', F, 0.6, 0.0, 1.0),
        ('Semilla', F, 0.0),
        ('Suciedad', F, 0.0, 0.0, 1.0),
    ], [('Base Color', C), ('Roughness', F), ('Height', F)])
    b = B(g)
    I = gi.outputs
    c = coords(b)
    P = b.v('ADD', c['P'], b.xyz(b.mul(I['Semilla'], 1.37), b.mul(I['Semilla'], 2.11), 0.0))
    A = b.v('NORMALIZE', b.v('ADD', I['Veta'], (0.0001, 0.0002, 0.0003)))
    along = b.v('DOT_PRODUCT', P, A)
    across = b.v('SUBTRACT', P, b.v('SCALE', A, scale=along))
    across_c = b.v('ADD', across, (0.61, 1.17, 0.83))  # centro del tronco fuera del objeto

    # anillos con distorsión lenta a lo largo de la veta
    vd = b.v('ADD', b.v('SCALE', across, scale=3.0), b.v('SCALE', A, scale=b.mul(along, 0.25)))
    dist = b.noise(vd, 1.4, 4.0, 0.55)
    r = b.madd(dist, 0.05, b.v('LENGTH', across_c))
    an = b.fract(b.mul(r, I['Anillos por metro']))
    tardio = b.pw(an, 3.0)

    # fibras finas
    vf = b.v('ADD', b.v('SCALE', across, scale=90.0), b.v('SCALE', A, scale=b.mul(along, 1.5)))
    fib = b.noise(vf, 1.0, 8.0, 0.6)
    fib_l = b.ss(fib, 0.3, 0.7)

    t = b.clamp01(b.madd(fib_l, 0.35, b.sub(0.85, b.mul(tardio, 0.75))))
    color = b.mix(t, I['Color Oscuro'], I['Color Claro'])

    # grietas finas a lo largo de la veta (solo con intemperie)
    vg = b.v('ADD', b.v('SCALE', across, scale=22.0), b.v('SCALE', A, scale=b.mul(along, 0.7)))
    ng = b.noise(vg, 1.0, 2.0, 0.5)
    grieta = b.mul(b.sub(1.0, b.ss(b.absf(b.sub(ng, 0.5)), 0.0, 0.012)), b.ss(I['Gris'], 0.2, 0.8))

    # intemperie: gris plateado, fibras levantadas
    gris = b.mix(fib_l, col(0.16, 0.15, 0.135), col(0.36, 0.34, 0.31))
    n_g = b.noise(b.v('MULTIPLY', P, (2.0, 2.0, 2.0)), 1.0, 4.0, 0.5)
    fg = b.clamp01(b.madd(b.sub(n_g, 0.5), 0.8, I['Gris']))
    color = b.mix(fg, color, gris)
    color = b.mix(b.mul(grieta, 0.8), color, col(0.05, 0.04, 0.03), blend='MULTIPLY')

    rough = b.madd(fg, 0.25, I['Rugosidad'])
    rough = b.madd(tardio, -0.08, rough)

    # suciedad: salpicado del suelo, barro en caras de arriba, chorreado y manchas
    _, _, Z = b.sep(c['P'])
    _, _, nz = b.sep(c['N'])
    n_s = b.noise(b.v('MULTIPLY', P, (3.0, 3.0, 3.0)), 1.0, 6.0, 0.6)
    suelo = b.sub(1.0, b.ss(b.madd(n_s, 0.5, Z), 0.15, 1.0))
    arriba = b.mul(b.ss(nz, 0.5, 0.9), b.ss(n_s, 0.35, 0.6))
    vch = b.xyz(b.mul(c['U'], 11.0), b.mul(Z, 0.4), 0.0)
    chorreo = b.ss(b.noise(vch, 1.0, 5.0, 0.55), 0.5, 0.72)
    manchas = b.ss(b.noise(b.v('MULTIPLY', P, (1.3, 1.3, 1.3)), 1.0, 7.0, 0.65), 0.48, 0.78)
    suc = b.madd(suelo, 0.85, b.mul(chorreo, 0.55))
    suc = b.madd(manchas, 0.45, suc)
    suc = b.mul(b.clamp01(suc), I['Suciedad'])
    color = b.mix(b.mul(suc, 0.85), color, col(0.36, 0.31, 0.25), blend='MULTIPLY')
    barro = b.mul(b.clamp01(b.madd(suelo, 0.6, arriba)), I['Suciedad'])
    color = b.mix(b.mul(barro, 0.75), color, col(0.10, 0.085, 0.06))
    rough = b.lerp(b.maxf(suc, barro), rough, 0.92)

    h = b.mul(tardio, 0.0004)
    h = b.madd(fib, b.madd(I['Gris'], 0.0012, 0.0003), h)
    h = b.madd(grieta, -0.0015, h)

    b.L(color, go.inputs['Base Color'])
    b.L(b.clamp01(rough), go.inputs['Roughness'])
    b.L(h, go.inputs['Height'])
    auto_layout(g)
    return g


# ---------------------------------------------------------------- aluminio
def grupo_aluminio():
    g, gi, go = nuevo_grupo('TI_Aluminio', [
        ('Veta', V, (0.0, 0.0, 1.0)),
        ('Color', C, col(0.80, 0.81, 0.82)),
        ('Desgaste', F, 0.1, 0.0, 1.0),
        ('Hollin', F, 0.0, 0.0, 1.0),
        ('Rugosidad', F, 0.24, 0.0, 1.0),
    ], [('Base Color', C), ('Metallic', F), ('Roughness', F), ('Height', F)])
    b = B(g)
    I = gi.outputs
    c = coords(b)
    P = c['P']
    _, _, Z = b.sep(P)
    A = b.v('NORMALIZE', b.v('ADD', I['Veta'], (0.0001, 0.0002, 0.0003)))
    along = b.v('DOT_PRODUCT', P, A)
    across = b.v('SUBTRACT', P, b.v('SCALE', A, scale=along))

    # cepillado en la dirección de la pieza
    vb = b.v('ADD', b.v('SCALE', across, scale=260.0), b.v('SCALE', A, scale=b.mul(along, 2.0)))
    cep = b.noise(vb, 1.0, 3.0, 0.5)
    base = b.mix(b.mul(cep, 0.25), I['Color'], col(0.55, 0.56, 0.57))
    rough = b.madd(cep, 0.14, I['Rugosidad'])
    metal = 1.0

    # oxidación blanca/mate y manchas
    n_ox = b.noise(b.v('MULTIPLY', P, (2.5, 2.5, 2.5)), 1.0, 8.0, 0.65)
    ox = b.mr(b.sub(n_ox, b.madd(I['Desgaste'], -0.35, 0.72)), 0.0, 0.08)
    base = b.mix(ox, base, col(0.56, 0.56, 0.54))
    rough = b.lerp(ox, rough, 0.72)
    metal = b.lerp(ox, metal, 0.35)

    # chorreado oscuro vertical
    vch = b.xyz(b.mul(b.v('DOT_PRODUCT', P, c['T']), 14.0), b.mul(Z, 0.4), 0.0)
    ch = b.mul(b.ss(b.noise(vch, 1.0, 5.0, 0.55), 0.5, 0.75), I['Desgaste'])
    base = b.mix(b.mul(ch, 0.4), base, col(0.45, 0.43, 0.40), blend='MULTIPLY')
    rough = b.lerp(ch, rough, 0.8)

    # hollín arriba (coordenada generada = 0..1 en la caja del objeto)
    tc = b.n('ShaderNodeTexCoord')
    _, _, gz = b.sep(tc.outputs['Generated'])
    n_h = b.noise(b.v('MULTIPLY', P, (5.0, 5.0, 1.5)), 1.0, 6.0, 0.6)
    hol = b.mul(b.ss(b.madd(n_h, 0.35, gz), 0.75, 1.15), I['Hollin'])
    base = b.mix(hol, base, col(0.025, 0.022, 0.02))
    rough = b.lerp(hol, rough, 0.9)
    metal = b.lerp(hol, metal, 0.0)

    # abolladuras suaves
    n_ab = b.noise(b.v('MULTIPLY', P, (1.8, 1.8, 1.8)), 1.0, 2.0, 0.5)
    h = b.mul(n_ab, b.mul(I['Desgaste'], 0.004))
    h = b.madd(cep, 0.00008, h)
    h = b.madd(ox, 0.0002, h)

    b.L(base, go.inputs['Base Color'])
    b.L(metal, go.inputs['Metallic'])
    b.L(b.clamp01(rough), go.inputs['Roughness'])
    b.L(h, go.inputs['Height'])
    auto_layout(g)
    return g


def crear_grupos():
    return dict(tinglado=grupo_tinglado(), calamina=grupo_calamina(),
                madera=grupo_madera(), aluminio=grupo_aluminio())
