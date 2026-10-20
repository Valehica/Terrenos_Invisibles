# Grupos de nodos procedurales para la iglesia de Isluga (Norte): cal sobre
# adobe con zócalo de piedra, teja colonial, barro de la casucha y pintura.
# Todo en metros. Las alturas (zócalo, salpicado) usan la Z de mundo: el suelo
# está en z = 0. El resto usa coordenadas relativas al origen del objeto.
import bpy
from math import pi
from nodos_util import B, col, auto_layout, nuevo_grupo, coords

F = 'NodeSocketFloat'
C = 'NodeSocketColor'


def _zw(b, c):
    _, _, z = b.sep(c['geo'].outputs['Position'])
    return z


def _voronoi_borde(b, vec, escala, dims='2D'):
    """Distancia al borde de la celda (para juntas) y color aleatorio por celda."""
    nd = b.n('ShaderNodeTexVoronoi', voronoi_dimensions=dims, feature='DISTANCE_TO_EDGE')
    b.put(nd.inputs['Vector'], vec)
    nd.inputs['Scale'].default_value = escala
    nd2 = b.n('ShaderNodeTexVoronoi', voronoi_dimensions=dims, feature='F1')
    b.put(nd2.inputs['Vector'], vec)
    nd2.inputs['Scale'].default_value = escala
    return nd.outputs['Distance'], nd2.outputs['Color']


# ---------------------------------------------------------------- cal
def grupo_cal():
    g, gi, go = nuevo_grupo('TI_Cal', [
        ('Color Cal', C, col(0.70, 0.69, 0.64)),
        ('Desgaste', F, 0.35, 0.0, 1.0),
        ('Suciedad', F, 0.5, 0.0, 1.0),
        ('Altura Zocalo', F, 0.2, -5.0, 5.0),
        ('Altura Salpicado', F, 0.7, 0.0, 3.0),
        ('Relieve', F, 1.0, 0.0, 4.0),
    ], [('Base Color', C), ('Roughness', F), ('Height', F)])
    b = B(g)
    I = gi.outputs
    c = coords(b)
    P, U = c['P'], c['U']
    Zw = _zw(b, c)
    _, _, nz = b.sep(c['N'])
    vertical = b.sub(1.0, b.ss(b.absf(nz), 0.5, 0.9))
    arriba = b.ss(nz, 0.6, 0.95)

    # adobe irregular bajo la cal y pinceladas de la lechada
    bulto = b.noise(b.v('MULTIPLY', P, (1.6, 1.6, 1.6)), 1.0, 4.0, 0.55)
    pincel = b.noise(b.v('MULTIPLY', P, (7.0, 7.0, 7.0)), 1.0, 6.0, 0.6, 0.6)

    # color de la cal: manos viejas grisáceas y variación suave
    n_var = b.noise(b.v('MULTIPLY', P, (0.6, 0.6, 0.6)), 1.0, 3.0, 0.5)
    cal = b.hsv(I['Color Cal'], 0.5, 1.0, b.madd(n_var, 0.14, 0.9))
    n_vieja = b.noise(b.v('MULTIPLY', P, (1.1, 1.1, 1.1)), 1.0, 5.0, 0.6)
    vieja = b.mul(b.ss(n_vieja, 0.5, 0.72), b.madd(I['Desgaste'], 0.5, 0.15))
    cal = b.mix(vieja, cal, col(0.50, 0.50, 0.48))
    cal = b.mix(b.mul(pincel, 0.08), cal, col(0.0, 0.0, 0.0), blend='MULTIPLY')

    # desconchado: la cal se cae y deja ver una mano vieja gris y el adobe
    abajo = b.sub(1.0, b.ss(b.sub(Zw, I['Altura Zocalo']), 0.0, 1.4))
    n_chip = b.noise(b.v('MULTIPLY', P, (1.3, 1.3, 1.3)), 1.0, 9.0, 0.62, 0.15)
    n_chip = b.madd(pincel, 0.05, n_chip)
    desg = b.madd(abajo, 0.25, b.mul(I['Desgaste'], 0.6))
    umbral = b.madd(desg, -0.3, 0.78)
    chip1 = b.mr(b.sub(n_chip, umbral), 0.0, 0.015)
    chip2 = b.mr(b.sub(n_chip, b.add(umbral, 0.05)), 0.0, 0.02)
    adobe = b.mix(bulto, col(0.25, 0.21, 0.16), col(0.34, 0.29, 0.22))
    color = b.mix(chip1, cal, col(0.42, 0.41, 0.38))
    color = b.mix(chip2, color, adobe)
    borde_chip = b.mul(b.mr(b.sub(n_chip, umbral), -0.012, 0.0), b.sub(1.0, chip1))
    color = b.mix(b.mul(borde_chip, 0.35), color, col(0.3, 0.3, 0.3), blend='MULTIPLY')

    # chorreado vertical gris (lluvia arrastra polvo desde los aleros y cornisas),
    # en manchones: no en toda la pared, si no parece veta de madera
    zona = b.ss(b.noise(b.v('MULTIPLY', P, (0.45, 0.45, 0.2)), 1.0, 2.0, 0.5), 0.45, 0.62)
    vch = b.xyz(b.mul(U, 5.0), b.mul(Zw, 0.3), 0.0)
    ch = b.ss(b.noise(vch, 1.0, 8.0, 0.62, 0.35), 0.5, 0.72)
    vch2 = b.xyz(b.mul(U, 11.0), b.mul(Zw, 0.8), 3.1)
    ch2 = b.ss(b.noise(vch2, 1.0, 8.0, 0.65, 0.5), 0.6, 0.74)
    chorreo = b.mul(b.maxf(ch, b.mul(ch2, 0.3)), zona)
    chorreo = b.mul(chorreo, b.mul(vertical, I['Suciedad']))
    color = b.mix(b.mul(chorreo, 0.6), color, col(0.42, 0.41, 0.39), blend='MULTIPLY')

    # salpicado de tierra abajo y polvo en las caras de arriba
    n_s = b.noise(b.v('MULTIPLY', P, (3.0, 3.0, 3.0)), 1.0, 6.0, 0.6)
    zrel = b.madd(n_s, 0.35, b.sub(Zw, I['Altura Zocalo']))
    suelo = b.sub(1.0, b.ss(zrel, 0.0, I['Altura Salpicado']))
    suelo = b.mul(suelo, I['Suciedad'])
    color = b.mix(b.mul(suelo, 0.7), color, col(0.55, 0.47, 0.37), blend='MULTIPLY')
    polvo = b.mul(b.mul(arriba, b.ss(n_s, 0.3, 0.65)), I['Suciedad'])
    color = b.mix(b.mul(polvo, 0.55), color, col(0.46, 0.40, 0.32))

    # zócalo de piedra (pirca con mortero de barro y manchas de cal)
    wob = b.mul(b.noise(b.xyz(b.mul(U, 2.0), 0.0, 0.0), 1.0, 2.0, 0.5), 0.06)
    zoc = b.sub(1.0, b.ss(b.sub(Zw, b.add(I['Altura Zocalo'], b.sub(wob, 0.03))), -0.006, 0.006))
    vp = b.xyz(b.mul(U, 3.6), b.mul(Zw, 6.0), 0.0)
    junta_d, cel = _voronoi_borde(b, vp, 1.0)
    _, gcel, _ = b.sep(cel)
    piedra = b.mix(gcel, col(0.07, 0.065, 0.06), col(0.17, 0.155, 0.135))
    n_p = b.noise(b.v('MULTIPLY', P, (9.0, 9.0, 9.0)), 1.0, 8.0, 0.65)
    piedra = b.mix(b.mul(n_p, 0.5), piedra, col(0.0, 0.0, 0.0), blend='MULTIPLY')
    junta = b.sub(1.0, b.ss(junta_d, 0.01, 0.045))
    piedra = b.mix(b.mul(junta, 0.8), piedra, col(0.15, 0.13, 0.11))
    n_emb = b.noise(b.v('MULTIPLY', P, (2.0, 2.0, 2.0)), 1.0, 7.0, 0.6)
    emb = b.mul(b.ss(n_emb, 0.55, 0.64), b.madd(I['Desgaste'], -0.4, 0.6))
    piedra = b.mix(emb, piedra, col(0.45, 0.44, 0.41))
    color = b.mix(zoc, color, piedra)

    # rugosidad
    rough = b.madd(chip2, 0.06, 0.86)
    rough = b.lerp(zoc, rough, 0.8)

    # altura (metros)
    h = b.mul(bulto, 0.006)
    h = b.madd(pincel, 0.0008, h)
    h = b.madd(chip1, -0.0025, h)
    h = b.madd(chip2, -0.003, h)
    hp = b.madd(b.ss(junta_d, 0.0, 0.15), 0.012, b.mul(n_p, 0.002))
    h = b.lerp(zoc, h, hp)
    h = b.mul(h, I['Relieve'])

    b.L(color, go.inputs['Base Color'])
    b.L(b.clamp01(rough), go.inputs['Roughness'])
    b.L(h, go.inputs['Height'])
    auto_layout(g)
    return g


# ---------------------------------------------------------------- teja
def grupo_teja():
    g, gi, go = nuevo_grupo('TI_Teja', [
        ('Color Teja', C, col(0.38, 0.10, 0.05)),
        ('Variacion', F, 0.6, 0.0, 1.0),
        ('Liquen', F, 0.4, 0.0, 1.0),
        ('Suciedad', F, 0.5, 0.0, 1.0),
        ('Ancho Par', F, 0.30, 0.1, 1.0),
        ('Largo Teja', F, 0.38, 0.1, 1.0),
        ('Relieve', F, 1.0, 0.0, 4.0),
    ], [('Base Color', C), ('Roughness', F), ('Height', F)])
    b = B(g)
    I = gi.outputs
    c = coords(b)
    P, U, S = c['P'], c['U'], c['S']
    _, _, nz = b.sep(c['N'])
    plano = b.ss(nz, 0.25, 0.45)    # caras del faldón (no los cantos)

    # columnas: una cobija (convexa, arriba) y un canal (cóncavo) por "par"
    fase = b.m('DIVIDE', U, I['Ancho Par'])
    col_i = b.floor(fase)
    f = b.fract(fase)
    dc = b.absf(b.sub(f, 0.25))                     # 0 en el centro de la cobija
    es_cob = b.sub(1.0, b.ss(dc, 0.215, 0.245))
    xc = b.m('DIVIDE', dc, 0.25)
    domo = b.sub(1.0, b.mul(xc, xc))
    xa = b.m('DIVIDE', b.absf(b.sub(f, 0.75)), 0.25)
    cuenco = b.sub(1.0, b.mul(xa, xa))
    h = b.lerp(es_cob, b.mul(cuenco, -0.012), b.madd(domo, 0.03, 0.004))
    # cobija: sombra hacia los lados (cara lateral del medio cilindro)
    lado = b.mul(b.ss(dc, 0.13, 0.24), es_cob)

    # filas a lo largo de la pendiente (cobijas desfasadas media teja)
    r_col = b.white(w=b.add(col_i, b.mul(es_cob, 0.5)), dims='1D')
    s = b.add(b.m('DIVIDE', S, I['Largo Teja']), b.madd(es_cob, 0.5, b.mul(r_col, 0.2)))
    fila = b.floor(s)
    t = b.fract(s)
    h = b.madd(t, 0.01, h)
    sombra_lab = b.sub(1.0, b.ss(t, 0.0, 0.09))
    id_teja = b.xyz(b.add(col_i, b.mul(es_cob, 0.5)), fila, 0.0)
    r = b.white(vec=id_teja)
    id2 = b.v('ADD', id_teja, (17.3, 5.1, 0.0))
    r2 = b.white(vec=id2)

    # color por teja: matiz, saturación y valor; algunas más oscuras o desteñidas
    var = I['Variacion']
    tono = b.madd(b.sub(r, 0.5), b.mul(var, 0.025), 0.5)
    satv = b.madd(b.sub(r2, 0.5), b.mul(var, 0.2), 0.9)
    valv = b.madd(b.sub(r, 0.5), b.mul(var, 0.2), 1.0)
    teja = b.hsv(I['Color Teja'], tono, satv, valv)
    oscura = b.mul(b.ss(r2, 0.9, 0.93), var)
    teja = b.mix(b.mul(oscura, 0.45), teja, col(0.12, 0.05, 0.03))
    destenida = b.mul(b.ss(r, 0.06, 0.03), var)
    teja = b.mix(b.mul(destenida, 0.5), teja, col(0.45, 0.28, 0.19))
    # volumen pintado: cresta de la cobija más clara, fondo del canal más oscuro
    teja = b.mix(b.mul(b.mul(domo, es_cob), 0.1), teja, col(0.75, 0.6, 0.5), blend='SCREEN')
    teja = b.mix(b.mul(b.madd(cuenco, 0.4, 0.6), b.sub(1.0, es_cob)), teja, col(0.45, 0.38, 0.34), blend='MULTIPLY')
    # chorreado oscuro de lluvia bajando por los canales
    vr = b.xyz(b.mul(U, 9.0), b.mul(S, 0.5), 7.7)
    lluvia = b.mul(b.ss(b.noise(vr, 1.0, 6.0, 0.6), 0.55, 0.75), b.sub(1.0, es_cob))
    teja = b.mix(b.mul(b.mul(lluvia, I['Suciedad']), 0.6), teja, col(0.3, 0.25, 0.22), blend='MULTIPLY')
    # grano de la arcilla y manchas grandes de humedad/sol
    n_gr = b.noise(b.v('MULTIPLY', P, (25.0, 25.0, 25.0)), 1.0, 4.0, 0.6)
    teja = b.mix(b.mul(n_gr, 0.18), teja, col(0.0, 0.0, 0.0), blend='MULTIPLY')
    n_gran = b.noise(b.v('MULTIPLY', P, (0.35, 0.35, 0.35)), 1.0, 3.0, 0.5)
    teja = b.mix(b.mul(b.ss(n_gran, 0.42, 0.68), 0.55), teja, col(0.42, 0.32, 0.27), blend='MULTIPLY')

    # suciedad en los canales, sombra bajo cada labio y a los lados de la cobija
    n_s = b.noise(b.v('MULTIPLY', P, (2.5, 2.5, 2.5)), 1.0, 5.0, 0.6)
    canal_suc = b.mul(b.mul(b.sub(1.0, es_cob), b.ss(n_s, 0.35, 0.65)), I['Suciedad'])
    teja = b.mix(b.mul(canal_suc, 0.6), teja, col(0.16, 0.11, 0.08))
    sombra = b.maxf(b.mul(sombra_lab, 0.6), b.mul(lado, 0.35))
    teja = b.mix(sombra, teja, col(0.05, 0.03, 0.02), blend='MULTIPLY')

    # líquenes y costras minerales blanquecinas, más en las crestas
    n_l = b.noise(b.v('MULTIPLY', P, (6.0, 6.0, 6.0)), 1.0, 8.0, 0.65)
    n_l2 = b.noise(b.v('MULTIPLY', P, (0.8, 0.8, 0.8)), 1.0, 3.0, 0.5)
    liq = b.mul(b.ss(b.madd(n_l2, 0.3, n_l), 0.78, 0.86), b.madd(es_cob, 0.6, 0.4))
    liq = b.mul(liq, I['Liquen'])
    teja = b.mix(liq, teja, col(0.52, 0.49, 0.40))
    liq_n = b.mul(b.mul(b.ss(n_l, 0.2, 0.15), b.ss(n_l2, 0.5, 0.65)), I['Liquen'])
    teja = b.mix(b.mul(liq_n, 0.8), teja, col(0.05, 0.045, 0.04))

    # cantos (alero, testeros): arcilla lisa con mortero de cal
    canto = b.mix(b.mul(n_gr, 0.25), I['Color Teja'], col(0.0, 0.0, 0.0), blend='MULTIPLY')
    canto = b.mix(b.ss(n_s, 0.5, 0.6), canto, col(0.55, 0.53, 0.48))
    color = b.mix(plano, canto, teja)

    rough = b.madd(liq, 0.1, b.madd(r, 0.08, 0.74))
    h = b.mul(h, plano)
    h = b.madd(n_gr, 0.0004, h)
    h = b.madd(liq, 0.0008, h)
    h = b.mul(h, I['Relieve'])

    b.L(color, go.inputs['Base Color'])
    b.L(b.clamp01(rough), go.inputs['Roughness'])
    b.L(h, go.inputs['Height'])
    auto_layout(g)
    return g


# ---------------------------------------------------------------- barro
def grupo_barro():
    """Revoque de barro con paja (muros de la casucha)."""
    g, gi, go = nuevo_grupo('TI_Barro', [
        ('Color Barro', C, col(0.24, 0.20, 0.16)),
        ('Suciedad', F, 0.5, 0.0, 1.0),
        ('Desgaste', F, 0.4, 0.0, 1.0),
        ('Altura Zocalo', F, 0.3, -5.0, 5.0),
        ('Relieve', F, 1.0, 0.0, 4.0),
    ], [('Base Color', C), ('Roughness', F), ('Height', F)])
    b = B(g)
    I = gi.outputs
    c = coords(b)
    P, U = c['P'], c['U']
    Zw = _zw(b, c)

    bulto = b.noise(b.v('MULTIPLY', P, (1.2, 1.2, 1.2)), 1.0, 5.0, 0.6)
    llana = b.noise(b.xyz(b.mul(U, 2.5), b.mul(Zw, 9.0), 0.0), 1.0, 4.0, 0.55, 0.4)
    n_var = b.noise(b.v('MULTIPLY', P, (0.5, 0.5, 0.5)), 1.0, 3.0, 0.5)
    barro = b.hsv(I['Color Barro'], b.madd(n_var, 0.02, 0.49), 1.0, b.madd(n_var, 0.3, 0.85))
    barro = b.mix(b.mul(llana, 0.25), barro, col(0.0, 0.0, 0.0), blend='MULTIPLY')
    barro = b.mix(b.mul(b.ss(llana, 0.6, 0.8), 0.15), barro, col(0.36, 0.31, 0.25))

    # paja picada
    vp = b.v('MULTIPLY', P, (1.0, 1.0, 1.0))
    _, cp = b.voronoi(vp, 60.0)
    dpaja, _ = b.voronoi(vp, 60.0)
    _, gp, _ = b.sep(cp)
    paja = b.mul(b.sub(1.0, b.ss(dpaja, 0.05, 0.12)), b.ss(gp, 0.82, 0.86))
    barro = b.mix(b.mul(paja, 0.7), barro, col(0.45, 0.37, 0.22))

    # grietas de retracción
    n_gr = b.noise(b.v('MULTIPLY', P, (2.2, 2.2, 2.2)), 1.0, 3.0, 0.5, 0.3)
    grieta = b.sub(1.0, b.ss(b.absf(b.sub(n_gr, 0.5)), 0.0, 0.005))
    grieta = b.mul(grieta, b.ss(b.noise(b.v('MULTIPLY', P, (0.7, 0.7, 0.7)), 1.0, 2.0, 0.5), 0.55, 0.68))
    grieta = b.mul(grieta, I['Desgaste'])
    barro = b.mix(b.mul(grieta, 0.5), barro, col(0.08, 0.07, 0.06), blend='MULTIPLY')

    # revoque caído: asoman adobes (filas de 0.12 m, largos de 0.4 m)
    abajo = b.sub(1.0, b.ss(Zw, 0.0, 1.5))
    n_c = b.noise(b.v('MULTIPLY', P, (0.9, 0.9, 0.9)), 1.0, 7.0, 0.6)
    caido = b.mr(b.sub(b.madd(abajo, 0.12, n_c), b.madd(I['Desgaste'], -0.2, 0.8)), 0.0, 0.02)
    fz = b.m('DIVIDE', Zw, 0.12)
    filaz = b.floor(fz)
    fu = b.m('DIVIDE', b.madd(b.white(w=filaz, dims='1D'), 0.4, U), 0.4)
    jz = b.sub(1.0, b.ss(b.mul(b.minf(b.fract(fz), b.sub(1.0, b.fract(fz))), 0.12), 0.006, 0.014))
    ju = b.sub(1.0, b.ss(b.mul(b.minf(b.fract(fu), b.sub(1.0, b.fract(fu))), 0.4), 0.006, 0.014))
    jad = b.maxf(jz, ju)
    r_ad = b.white(vec=b.xyz(filaz, b.floor(fu), 0.0))
    adobe = b.hsv(col(0.30, 0.22, 0.15), 0.5, 1.0, b.madd(r_ad, 0.3, 0.8))
    adobe = b.mix(b.mul(jad, 0.6), adobe, col(0.14, 0.11, 0.08))
    color = b.mix(caido, barro, adobe)

    # zócalo: base más clara (sales y polvo), humedad oscura justo encima del suelo
    n_s = b.noise(b.v('MULTIPLY', P, (3.0, 3.0, 3.0)), 1.0, 6.0, 0.6)
    zr = b.madd(n_s, 0.25, b.sub(Zw, I['Altura Zocalo']))
    zoc = b.sub(1.0, b.ss(zr, -0.1, 0.12))
    color = b.mix(b.mul(zoc, 0.6), color, col(0.44, 0.39, 0.32))
    hum = b.mul(b.sub(1.0, b.ss(b.madd(n_s, 0.1, Zw), 0.0, 0.14)), I['Suciedad'])
    color = b.mix(b.mul(hum, 0.5), color, col(0.4, 0.36, 0.32), blend='MULTIPLY')
    vch = b.xyz(b.mul(U, 6.0), b.mul(Zw, 0.25), 0.0)
    ch = b.mul(b.ss(b.noise(vch, 1.0, 5.0, 0.6), 0.55, 0.75), I['Suciedad'])
    color = b.mix(b.mul(ch, 0.35), color, col(0.55, 0.5, 0.45), blend='MULTIPLY')

    rough = b.madd(caido, -0.04, 0.93)
    h = b.mul(bulto, 0.008)
    h = b.madd(llana, 0.002, h)
    h = b.madd(grieta, -0.002, h)
    h = b.lerp(caido, h, b.madd(jad, -0.006, -0.008))
    h = b.mul(h, I['Relieve'])

    b.L(color, go.inputs['Base Color'])
    b.L(b.clamp01(rough), go.inputs['Roughness'])
    b.L(h, go.inputs['Height'])
    auto_layout(g)
    return g


# ---------------------------------------------------------------- pintura
def grupo_pintura():
    """Pintura (o esmalte) sobre un fondo, gastada y con chorreado."""
    g, gi, go = nuevo_grupo('TI_Pintura', [
        ('Color', C, col(0.45, 0.04, 0.03)),
        ('Fondo', C, col(0.62, 0.61, 0.57)),
        ('Desgaste', F, 0.35, 0.0, 1.0),
        ('Suciedad', F, 0.4, 0.0, 1.0),
        ('Rugosidad', F, 0.6, 0.0, 1.0),
        ('Metalico', F, 0.0, 0.0, 1.0),
    ], [('Base Color', C), ('Metallic', F), ('Roughness', F), ('Height', F)])
    b = B(g)
    I = gi.outputs
    c = coords(b)
    P, U = c['P'], c['U']
    Zw = _zw(b, c)

    n_d = b.noise(b.v('MULTIPLY', P, (1.5, 1.5, 1.5)), 1.0, 3.0, 0.5)
    sol = b.mul(b.ss(n_d, 0.35, 0.7), b.madd(I['Desgaste'], 0.5, 0.1))
    pint = b.mix(sol, I['Color'], b.hsv(b.mix(0.2, I['Color'], col(0.6, 0.58, 0.55), blend='SCREEN'), 0.5, 0.7, 1.0))
    n_chip = b.noise(b.v('MULTIPLY', P, (4.0, 4.0, 4.0)), 1.0, 8.0, 0.62, 0.2)
    chip = b.mr(b.sub(n_chip, b.madd(I['Desgaste'], -0.35, 0.82)), 0.0, 0.03)
    color = b.mix(chip, pint, I['Fondo'])
    vch = b.xyz(b.mul(U, 14.0), b.mul(Zw, 0.4), 0.0)
    ch = b.mul(b.ss(b.noise(vch, 1.0, 5.0, 0.55), 0.5, 0.75), I['Suciedad'])
    color = b.mix(b.mul(ch, 0.45), color, col(0.42, 0.40, 0.37), blend='MULTIPLY')

    metal = b.lerp(chip, I['Metalico'], 0.0)
    rough = b.lerp(chip, I['Rugosidad'], 0.88)
    rough = b.lerp(ch, rough, 0.85)
    h = b.mul(b.sub(1.0, chip), 0.0004)
    h = b.madd(n_d, 0.0005, h)

    b.L(color, go.inputs['Base Color'])
    b.L(metal, go.inputs['Metallic'])
    b.L(b.clamp01(rough), go.inputs['Roughness'])
    b.L(h, go.inputs['Height'])
    auto_layout(g)
    return g


# ---------------------------------------------------------------- puerta
def grupo_puerta():
    """Puerta de tablas verticales pintadas, con dos travesaños y la pintura
    saltada (asoma madera gris de intemperie)."""
    g, gi, go = nuevo_grupo('TI_Puerta', [
        ('Color Pintura', C, col(0.05, 0.16, 0.36)),
        ('Desgaste', F, 0.4, 0.0, 1.0),
        ('Suciedad', F, 0.5, 0.0, 1.0),
        ('Ancho Tabla', F, 0.11, 0.03, 0.5),
        ('Altura Travesanos', F, 0.25, 0.0, 2.0),
        ('Separacion Travesanos', F, 0.5, 0.1, 3.0),
    ], [('Base Color', C), ('Roughness', F), ('Height', F)])
    b = B(g)
    I = gi.outputs
    c = coords(b)
    P, U = c['P'], c['U']
    Zw = _zw(b, c)

    # tablas verticales con ranura entre ellas
    tb = b.m('DIVIDE', U, I['Ancho Tabla'])
    ft = b.fract(tb)
    dt = b.mul(b.minf(ft, b.sub(1.0, ft)), I['Ancho Tabla'])
    ranura = b.sub(1.0, b.ss(dt, 0.0015, 0.004))
    rt = b.white(w=b.floor(tb), dims='1D')

    # travesaños horizontales (bandas en relieve)
    tz = b.sub(Zw, I['Altura Travesanos'])
    ftz = b.fract(b.m('DIVIDE', tz, I['Separacion Travesanos']))
    dtz = b.absf(b.mul(b.sub(ftz, 0.5), I['Separacion Travesanos']))
    trav = b.mul(b.ss(dtz, 0.06, 0.05), b.ss(tz, -0.03, -0.02))
    borde_trav = b.mul(b.sub(1.0, b.ss(b.absf(b.sub(dtz, 0.055)), 0.0, 0.006)), b.ss(tz, -0.03, -0.02))

    # veta de la madera (estirada a lo largo de la tabla)
    vv = b.xyz(b.mul(U, 40.0), b.mul(Zw, 1.5), b.mul(rt, 13.0))
    veta = b.noise(vv, 1.0, 6.0, 0.6, 0.4)
    veta_l = b.ss(veta, 0.35, 0.65)
    madera = b.mix(veta_l, col(0.12, 0.11, 0.10), col(0.30, 0.28, 0.25))

    # pintura azul: variación por tabla, decolorada por el sol y con brochazos
    pint = b.hsv(I['Color Pintura'], b.madd(rt, 0.02, 0.49), b.madd(rt, 0.15, 0.9), b.madd(rt, 0.2, 0.92))
    n_sol = b.noise(b.v('MULTIPLY', P, (1.2, 1.2, 0.6)), 1.0, 3.0, 0.5)
    sol = b.mul(b.ss(n_sol, 0.35, 0.7), b.madd(I['Desgaste'], 0.5, 0.15))
    pint = b.mix(sol, pint, b.hsv(b.mix(0.25, pint, col(0.55, 0.6, 0.65), blend='SCREEN'), 0.5, 0.7, 1.0))
    pint = b.mix(b.mul(veta_l, 0.12), pint, col(0.0, 0.0, 0.0), blend='MULTIPLY')
    # el travesaño sobresale: algo más claro y con su propia veta horizontal
    vt = b.xyz(b.mul(U, 1.5), b.mul(Zw, 40.0), 5.0)
    veta_t = b.ss(b.noise(vt, 1.0, 6.0, 0.6, 0.4), 0.35, 0.65)
    pint_t = b.mix(b.mul(veta_t, 0.12), b.mix(0.035, pint, col(1.0, 1.0, 1.0), blend='SCREEN'), col(0.0, 0.0, 0.0), blend='MULTIPLY')
    pint = b.mix(trav, pint, pint_t)

    # pintura saltada: más abajo, en los cantos de las tablas y de los travesaños
    abajo = b.sub(1.0, b.ss(Zw, 0.0, 0.5))
    canto = b.sub(1.0, b.ss(dt, 0.0, 0.012))
    n_chip = b.noise(b.v('MULTIPLY', P, (6.0, 6.0, 2.5)), 1.0, 8.0, 0.62, 0.2)
    n_chip = b.madd(veta, 0.12, n_chip)
    desg = b.mul(I['Desgaste'], 0.6)
    desg = b.madd(abajo, 0.3, desg)
    desg = b.madd(canto, 0.2, desg)
    desg = b.madd(borde_trav, 0.2, desg)
    chip = b.mr(b.sub(n_chip, b.madd(desg, -0.35, 0.8)), 0.0, 0.03)
    color = b.mix(chip, pint, madera)

    # sombras: ranuras y borde inferior de los travesaños
    sombra = b.maxf(b.mul(ranura, b.sub(1.0, trav)), b.mul(borde_trav, 0.6))
    color = b.mix(b.mul(sombra, 0.45), color, col(0.05, 0.05, 0.06), blend='MULTIPLY')

    # suciedad: salpicado abajo y chorreado vertical
    n_s = b.noise(b.v('MULTIPLY', P, (3.0, 3.0, 3.0)), 1.0, 6.0, 0.6)
    suelo = b.mul(b.sub(1.0, b.ss(b.madd(n_s, 0.25, Zw), 0.05, 0.45)), I['Suciedad'])
    color = b.mix(b.mul(suelo, 0.7), color, col(0.45, 0.39, 0.31), blend='MULTIPLY')
    vch = b.xyz(b.mul(U, 12.0), b.mul(Zw, 0.4), 0.0)
    ch = b.mul(b.ss(b.noise(vch, 1.0, 5.0, 0.55), 0.55, 0.75), I['Suciedad'])
    color = b.mix(b.mul(ch, 0.35), color, col(0.5, 0.48, 0.45), blend='MULTIPLY')

    rough = b.lerp(chip, 0.55, 0.85)
    rough = b.madd(sol, 0.15, rough)
    rough = b.lerp(suelo, rough, 0.9)

    h = b.mul(ranura, -0.004)
    h = b.madd(trav, 0.008, h)
    h = b.madd(veta, b.madd(chip, 0.0008, 0.0002), h)
    h = b.madd(b.sub(1.0, chip), 0.0004, h)

    b.L(color, go.inputs['Base Color'])
    b.L(b.clamp01(rough), go.inputs['Roughness'])
    b.L(h, go.inputs['Height'])
    auto_layout(g)
    return g


def crear_grupos():
    return dict(cal=grupo_cal(), teja=grupo_teja(), barro=grupo_barro(),
                pintura=grupo_pintura(), puerta=grupo_puerta())
