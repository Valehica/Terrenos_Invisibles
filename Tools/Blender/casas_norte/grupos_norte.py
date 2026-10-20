# Grupos de nodos de muro para las casas de Norte (Isluga), a partir de fotos del
# pueblo: piedra canteada, pirca, ladrillo, bloque de cemento y revoque de cemento.
# El revoque de barro, la pintura, la puerta y la cal vienen de Tools/Blender/catedral;
# la calamina, de Tools/Blender/casas. Todo en metros; alturas con Z de mundo (suelo z=0).
import bpy
from nodos_util import B, col, auto_layout, nuevo_grupo, coords
from grupos_catedral import _zw, _voronoi_borde

F = 'NodeSocketFloat'
C = 'NodeSocketColor'


def _hiladas(b, U, Zw, alto, largo_min, largo_max):
    """Aparejo de hiladas con juntas desfasadas. Devuelve (dist. a la junta en m,
    id de la pieza como vector, aleatorio de la pieza, posición vertical 0..1 en la hilada)."""
    fz = b.m('DIVIDE', Zw, alto)
    fila = b.floor(fz)
    rf = b.white(w=fila, dims='1D')
    rf2 = b.white(w=b.add(fila, 31.7), dims='1D')
    largo = b.madd(rf2, largo_max - largo_min, largo_min)
    fu = b.m('DIVIDE', b.madd(rf, 1.7, U), largo)
    col_i = b.floor(fu)
    tz = b.fract(fz); tu = b.fract(fu)
    dz = b.mul(b.minf(tz, b.sub(1.0, tz)), alto)
    du = b.mul(b.minf(tu, b.sub(1.0, tu)), largo)
    d = b.minf(dz, du)
    pid = b.xyz(fila, col_i, 0.0)
    r = b.white(vec=pid)
    return d, pid, r, tz


def _suciedad_base(b, P, U, Zw, color, suciedad, alto=0.7):
    """Salpicado de tierra abajo y chorreado vertical, comunes a todos los muros."""
    n_s = b.noise(b.v('MULTIPLY', P, (3.0, 3.0, 3.0)), 1.0, 6.0, 0.6)
    suelo = b.mul(b.sub(1.0, b.ss(b.madd(n_s, 0.3, Zw), 0.0, alto)), suciedad)
    color = b.mix(b.mul(suelo, 0.65), color, col(0.55, 0.47, 0.37), blend='MULTIPLY')
    zona = b.ss(b.noise(b.v('MULTIPLY', P, (0.45, 0.45, 0.2)), 1.0, 2.0, 0.5), 0.45, 0.62)
    vch = b.xyz(b.mul(U, 5.0), b.mul(Zw, 0.3), 0.0)
    ch = b.mul(b.mul(b.ss(b.noise(vch, 1.0, 8.0, 0.62, 0.35), 0.5, 0.72), zona), suciedad)
    color = b.mix(b.mul(ch, 0.4), color, col(0.45, 0.43, 0.40), blend='MULTIPLY')
    return color, suelo


def _desgaste(b, P, Zw, d, color, h, desg, liquen=0.0):
    """Desgaste común de mampostería: cantos rotos junto a las juntas, picado,
    manchas oscuras, eflorescencias abajo, polvo del altiplano y (en piedra) líquenes.
    d = distancia a la junta en metros. Devuelve (color, altura, rugosidad extra)."""
    n_c = b.noise(b.v('MULTIPLY', P, (7.0, 7.0, 7.0)), 1.0, 6.0, 0.6)
    junto = b.sub(1.0, b.ss(d, 0.0, 0.035))
    canto = b.mul(junto, b.ss(n_c, b.madd(desg, -0.25, 0.72), b.madd(desg, -0.25, 0.76)))
    color = b.mix(b.mul(canto, 0.6), color, col(0.32, 0.30, 0.27), blend='MULTIPLY')
    h = b.madd(canto, -0.008, h)
    # picado: poros y golpes
    dp, cp = b.voronoi(P, 45.0)
    _, gp, _ = b.sep(cp)
    pico = b.mul(b.sub(1.0, b.ss(dp, 0.08, 0.2)), b.ss(gp, b.madd(desg, -0.3, 0.85), 0.9))
    color = b.mix(b.mul(pico, 0.5), color, col(0.2, 0.2, 0.2), blend='MULTIPLY')
    h = b.madd(pico, -0.002, h)
    # manchas oscuras de humedad y hollín
    n_m = b.noise(b.v('MULTIPLY', P, (0.8, 0.8, 0.8)), 1.0, 5.0, 0.6)
    mancha = b.mul(b.ss(n_m, 0.58, 0.72), desg)
    color = b.mix(b.mul(mancha, 0.5), color, col(0.38, 0.36, 0.33), blend='MULTIPLY')
    # eflorescencia (sal) abajo y por las juntas
    n_e = b.noise(b.v('MULTIPLY', P, (1.5, 1.5, 1.5)), 1.0, 6.0, 0.62)
    abajo = b.sub(1.0, b.ss(b.madd(n_e, 0.6, Zw), 0.2, 1.4))
    sal = b.mul(b.mul(abajo, b.ss(n_e, 0.42, 0.62)), b.mul(desg, 0.8))
    sal = b.maxf(sal, b.mul(b.mul(junto, b.ss(n_e, 0.55, 0.65)), b.mul(desg, 0.4)))
    color = b.mix(b.mul(sal, 0.55), color, col(0.62, 0.60, 0.55))
    # polvo: velo beige que apaga el color
    n_p = b.noise(b.v('MULTIPLY', P, (0.5, 0.5, 0.5)), 1.0, 3.0, 0.5)
    polvo = b.mul(b.madd(n_p, 0.2, 0.15), desg)
    color = b.mix(polvo, color, col(0.48, 0.42, 0.34))
    # líquenes (naranja y gris verdoso), solo en piedra
    n_l = b.noise(b.v('MULTIPLY', P, (9.0, 9.0, 9.0)), 1.0, 8.0, 0.65)
    n_l2 = b.noise(b.v('MULTIPLY', P, (0.9, 0.9, 0.9)), 1.0, 3.0, 0.5)
    zona = b.mul(b.ss(n_l2, 0.5, 0.65), liquen)
    naranja = b.mul(b.ss(n_l, 0.7, 0.74), zona)
    gris_v = b.mul(b.ss(n_l, 0.24, 0.2), zona)
    color = b.mix(naranja, color, col(0.42, 0.20, 0.05))
    color = b.mix(gris_v, color, col(0.42, 0.44, 0.36))
    h = b.madd(b.maxf(naranja, gris_v), 0.0006, h)
    rough = b.mul(b.add(canto, pico), 0.05)
    return color, h, rough


# ---------------------------------------------------------------- piedra canteada
def grupo_piedra_canteada():
    """Bloques de piedra volcánica gris, cortados a mano en hiladas (foto 1)."""
    g, gi, go = nuevo_grupo('TI_Piedra_Canteada', [
        ('Color Piedra', C, col(0.30, 0.29, 0.27)),
        ('Color Mortero', C, col(0.36, 0.34, 0.30)),
        ('Variacion', F, 0.5, 0.0, 1.0),
        ('Desgaste', F, 0.6, 0.0, 1.0),
        ('Suciedad', F, 0.5, 0.0, 1.0),
        ('Alto Hilada', F, 0.24, 0.08, 0.6),
    ], [('Base Color', C), ('Roughness', F), ('Height', F)])
    b = B(g); I = gi.outputs
    c = coords(b); P, U = c['P'], c['U']
    Zw = _zw(b, c)
    # juntas onduladas: la piedra se talla a mano, no es cerámica
    n_w = b.noise(b.v('MULTIPLY', P, (2.5, 2.5, 2.5)), 1.0, 3.0, 0.5)
    n_w2 = b.noise(b.v('ADD', b.v('MULTIPLY', P, (2.5, 2.5, 2.5)), (11.0, 3.0, 7.0)), 1.0, 3.0, 0.5)
    Uw = b.madd(b.sub(n_w, 0.5), 0.07, U)
    Zww = b.madd(b.sub(n_w2, 0.5), 0.045, Zw)
    d, pid, r, tz = _hiladas(b, Uw, Zww, I['Alto Hilada'], 0.26, 0.6)
    r2 = b.white(vec=b.v('ADD', pid, (3.3, 7.1, 0.0)))
    piedra = b.hsv(I['Color Piedra'], b.madd(r2, 0.04, 0.48), b.madd(r, 0.6, 0.7),
                   b.madd(b.sub(r, 0.5), b.mul(I['Variacion'], 0.8), 1.0))
    # algunas piedras más pardas (óxido del basalto)
    parda = b.mul(b.ss(r2, 0.8, 0.85), I['Variacion'])
    piedra = b.mix(b.mul(parda, 0.5), piedra, col(0.33, 0.25, 0.18))
    # textura de la piedra: poros y golpes de cincel
    n_f = b.noise(b.v('MULTIPLY', P, (14.0, 14.0, 14.0)), 1.0, 8.0, 0.65)
    n_g = b.noise(b.v('ADD', b.v('MULTIPLY', P, (3.0, 3.0, 3.0)), b.v('SCALE', pid, scale=5.3)), 1.0, 4.0, 0.6)
    piedra = b.mix(b.mul(n_f, 0.35), piedra, col(0.0, 0.0, 0.0), blend='MULTIPLY')
    piedra = b.mix(b.mul(b.ss(n_g, 0.55, 0.75), 0.25), piedra, col(0.6, 0.58, 0.55), blend='SCREEN')
    # junta: mortero hundido, cantos de la piedra algo más oscuros
    j = b.sub(1.0, b.ss(b.madd(n_g, 0.006, d), 0.006, 0.012))
    canto = b.mul(b.sub(1.0, b.ss(d, 0.01, 0.03)), b.sub(1.0, j))
    canto = b.mul(canto, b.ss(n_g, 0.4, 0.6))          # solo algunos cantos desportillados
    color = b.mix(b.mul(canto, 0.3), piedra, col(0.05, 0.05, 0.05), blend='MULTIPLY')
    color = b.mix(j, color, I['Color Mortero'])
    # relieve: piedra abombada y de cara irregular, junta hundida
    h = b.mul(b.ss(d, 0.006, 0.03), 0.008)
    h = b.madd(n_g, 0.01, h)
    h = b.madd(n_f, 0.002, h)
    h = b.madd(j, -0.006, h)
    color, h, r_d = _desgaste(b, P, Zw, d, color, h, I['Desgaste'], 1.0)
    color, suelo = _suciedad_base(b, P, U, Zw, color, I['Suciedad'])
    rough = b.add(b.madd(j, 0.05, 0.85), r_d)
    b.L(color, go.inputs['Base Color']); b.L(b.clamp01(rough), go.inputs['Roughness']); b.L(h, go.inputs['Height'])
    auto_layout(g)
    return g


# ---------------------------------------------------------------- pirca
def grupo_pirca():
    """Pirca: piedras irregulares asentadas en barro (foto 2, izquierda)."""
    g, gi, go = nuevo_grupo('TI_Pirca', [
        ('Color Piedra', C, col(0.16, 0.15, 0.14)),
        ('Color Mortero', C, col(0.33, 0.28, 0.22)),
        ('Variacion', F, 0.6, 0.0, 1.0),
        ('Desgaste', F, 0.6, 0.0, 1.0),
        ('Suciedad', F, 0.5, 0.0, 1.0),
        ('Tamano Piedra', F, 0.3, 0.08, 1.0),
    ], [('Base Color', C), ('Roughness', F), ('Height', F)])
    b = B(g); I = gi.outputs
    c = coords(b); P, U = c['P'], c['U']
    Zw = _zw(b, c)
    esc = b.m('DIVIDE', 1.0, I['Tamano Piedra'])
    vp = b.xyz(b.mul(U, b.mul(esc, 0.75)), b.mul(Zw, b.mul(esc, 1.15)), 0.0)
    n_d = b.noise(b.v('MULTIPLY', P, (3.0, 3.0, 3.0)), 1.0, 4.0, 0.6)
    vp = b.v('ADD', vp, b.xyz(b.mul(b.sub(n_d, 0.5), 0.35), b.mul(b.sub(n_d, 0.5), -0.25), 0.0))
    junta_d, cel = _voronoi_borde(b, vp, 1.0)
    rr, gg, bb = b.sep(cel)
    piedra = b.hsv(I['Color Piedra'], b.madd(bb, 0.05, 0.47), b.madd(rr, 0.5, 0.6),
                   b.madd(b.sub(gg, 0.5), b.mul(I['Variacion'], 1.2), 1.0))
    clara = b.mul(b.ss(rr, 0.82, 0.86), I['Variacion'])
    piedra = b.mix(b.mul(clara, 0.6), piedra, col(0.38, 0.35, 0.31))
    n_p = b.noise(b.v('MULTIPLY', P, (9.0, 9.0, 9.0)), 1.0, 8.0, 0.65)
    piedra = b.mix(b.mul(n_p, 0.45), piedra, col(0.0, 0.0, 0.0), blend='MULTIPLY')
    n_m = b.noise(b.v('MULTIPLY', P, (4.0, 4.0, 4.0)), 1.0, 5.0, 0.6)
    j = b.sub(1.0, b.ss(b.madd(n_m, 0.03, junta_d), 0.025, 0.06))
    mort = b.hsv(I['Color Mortero'], 0.5, 1.0, b.madd(n_m, 0.4, 0.6))
    # sombra en la junta: el barro queda hundido entre las piedras
    piedra = b.mix(b.mul(b.sub(1.0, b.ss(junta_d, 0.04, 0.12)), 0.5), piedra, col(0.1, 0.1, 0.1), blend='MULTIPLY')
    color = b.mix(j, piedra, mort)
    h = b.mul(b.ss(junta_d, 0.0, 0.2), 0.03)
    h = b.madd(n_p, 0.005, h)
    color, h, r_d = _desgaste(b, P, Zw, b.mul(junta_d, I['Tamano Piedra']), color, h, I['Desgaste'], 1.0)
    color, suelo = _suciedad_base(b, P, U, Zw, color, I['Suciedad'])
    rough = b.add(b.madd(j, 0.06, 0.86), r_d)
    b.L(color, go.inputs['Base Color']); b.L(b.clamp01(rough), go.inputs['Roughness']); b.L(h, go.inputs['Height'])
    auto_layout(g)
    return g


# ---------------------------------------------------------------- ladrillo
def grupo_ladrillo():
    """Ladrillo fiscal a la vista (foto 3): piezas hechas a mano, juntas gruesas e
    irregulares, cocción despareja, cantos rotos y sal."""
    g, gi, go = nuevo_grupo('TI_Ladrillo', [
        ('Color Ladrillo', C, col(0.30, 0.09, 0.04)),
        ('Color Mortero', C, col(0.33, 0.31, 0.27)),
        ('Variacion', F, 0.6, 0.0, 1.0),
        ('Desgaste', F, 0.6, 0.0, 1.0),
        ('Suciedad', F, 0.5, 0.0, 1.0),
    ], [('Base Color', C), ('Roughness', F), ('Height', F)])
    b = B(g); I = gi.outputs
    c = coords(b); P, U = c['P'], c['U']
    Zw = _zw(b, c)
    # juntas a mano: ni rectas ni del mismo grosor
    n_w = b.noise(b.v('MULTIPLY', P, (3.0, 3.0, 3.0)), 1.0, 3.0, 0.5)
    n_w2 = b.noise(b.v('ADD', b.v('MULTIPLY', P, (3.0, 3.0, 3.0)), (5.0, 9.0, 2.0)), 1.0, 3.0, 0.5)
    Uw = b.madd(b.sub(n_w, 0.5), 0.02, U)
    Zww = b.madd(b.sub(n_w2, 0.5), 0.01, Zw)
    alto, largo = 0.09, 0.29          # pieza + junta
    fz = b.m('DIVIDE', Zww, alto)
    fila = b.floor(fz)
    par = b.m('MODULO', fila, 2.0)
    rf = b.white(w=fila, dims='1D')
    fu = b.m('DIVIDE', b.add(b.madd(par, largo * 0.5, Uw), b.mul(b.sub(rf, 0.5), 0.05)), largo)
    tz = b.fract(fz); tu = b.fract(fu)
    dz = b.mul(b.minf(tz, b.sub(1.0, tz)), alto)
    du = b.mul(b.minf(tu, b.sub(1.0, tu)), largo)
    d = b.minf(dz, du)
    pid = b.xyz(fila, b.floor(fu), 0.0)
    r = b.white(vec=pid)
    r2 = b.white(vec=b.v('ADD', pid, (5.5, 1.3, 0.0)))
    var = I['Variacion']
    lad = b.hsv(I['Color Ladrillo'], b.madd(b.sub(r2, 0.5), b.mul(var, 0.03), 0.5),
                b.madd(r, b.mul(var, 0.3), 0.75), b.madd(b.sub(r, 0.5), b.mul(var, 0.45), 1.0))
    # cocción despareja: manchas dentro de cada pieza, algunas quemadas, otras crudas
    n_q = b.noise(b.v('ADD', b.v('MULTIPLY', P, (12.0, 12.0, 12.0)), b.v('SCALE', pid, scale=3.1)), 1.0, 5.0, 0.6)
    lad = b.mix(b.mul(b.ss(n_q, 0.45, 0.75), 0.45), lad, col(0.12, 0.05, 0.03), blend='MULTIPLY')
    quemado = b.mul(b.ss(r2, 0.86, 0.9), var)
    lad = b.mix(b.mul(quemado, 0.6), lad, col(0.10, 0.045, 0.03))
    crudo = b.mul(b.ss(r2, 0.08, 0.04), var)
    lad = b.mix(b.mul(crudo, 0.6), lad, col(0.42, 0.25, 0.15))
    n_f = b.noise(b.v('MULTIPLY', P, (40.0, 40.0, 40.0)), 1.0, 5.0, 0.6)
    lad = b.mix(b.mul(n_f, 0.3), lad, col(0.0, 0.0, 0.0), blend='MULTIPLY')
    # canto redondeado de cada pieza (más oscuro)
    lad = b.mix(b.mul(b.sub(1.0, b.ss(d, 0.007, 0.013)), 0.3), lad, col(0.2, 0.2, 0.2), blend='MULTIPLY')
    # mortero: grueso, granulado y a ratos corrido sobre el ladrillo
    n_b = b.noise(b.v('MULTIPLY', P, (2.0, 2.0, 2.0)), 1.0, 6.0, 0.6)
    j = b.sub(1.0, b.ss(b.madd(b.sub(n_b, 0.5), 0.006, d), 0.006, 0.0095))
    mort = b.hsv(I['Color Mortero'], 0.5, 1.0, b.madd(n_f, 0.4, 0.75))
    reb = b.mul(b.sub(1.0, b.ss(d, 0.008, 0.03)), b.ss(n_b, 0.58, 0.72))
    color = b.mix(b.maxf(j, b.mul(reb, 0.7)), lad, mort)
    # relieve: cara de la pieza algo alabeada, junta hundida y áspera
    n_a = b.noise(b.v('ADD', b.v('MULTIPLY', P, (6.0, 6.0, 6.0)), b.v('SCALE', pid, scale=7.7)), 1.0, 3.0, 0.5)
    h = b.mul(b.ss(d, 0.006, 0.012), 0.008)
    h = b.madd(n_a, 0.002, h)
    h = b.madd(n_f, 0.0008, h)
    h = b.madd(b.mul(j, n_f), 0.0015, h)
    color, h, r_d = _desgaste(b, P, Zw, d, color, h, I['Desgaste'])
    color, suelo = _suciedad_base(b, P, U, Zw, color, I['Suciedad'])
    rough = b.add(b.madd(j, 0.06, 0.82), r_d)
    b.L(color, go.inputs['Base Color']); b.L(b.clamp01(rough), go.inputs['Roughness']); b.L(h, go.inputs['Height'])
    auto_layout(g)
    return g


# ---------------------------------------------------------------- bloque de cemento
def grupo_bloque():
    """Bloque de cemento de 40x20, sin revocar (foto 4, edificio alto)."""
    g, gi, go = nuevo_grupo('TI_Bloque', [
        ('Color Bloque', C, col(0.42, 0.41, 0.39)),
        ('Color Mortero', C, col(0.36, 0.35, 0.33)),
        ('Variacion', F, 0.4, 0.0, 1.0),
        ('Desgaste', F, 0.6, 0.0, 1.0),
        ('Suciedad', F, 0.4, 0.0, 1.0),
    ], [('Base Color', C), ('Roughness', F), ('Height', F)])
    b = B(g); I = gi.outputs
    c = coords(b); P, U = c['P'], c['U']
    Zw = _zw(b, c)
    alto, largo = 0.20, 0.40
    fz = b.m('DIVIDE', Zw, alto)
    fila = b.floor(fz)
    par = b.m('MODULO', fila, 2.0)
    fu = b.m('DIVIDE', b.madd(par, largo * 0.5, U), largo)
    tz = b.fract(fz); tu = b.fract(fu)
    d = b.minf(b.mul(b.minf(tz, b.sub(1.0, tz)), alto), b.mul(b.minf(tu, b.sub(1.0, tu)), largo))
    pid = b.xyz(fila, b.floor(fu), 0.0)
    r = b.white(vec=pid)
    blq = b.hsv(I['Color Bloque'], 0.5, 1.0, b.madd(b.sub(r, 0.5), b.mul(I['Variacion'], 0.3), 1.0))
    # poros del hormigón vibrado
    _, cp = b.voronoi(b.v('MULTIPLY', P, (1.0, 1.0, 1.0)), 90.0)
    dp, _ = b.voronoi(b.v('MULTIPLY', P, (1.0, 1.0, 1.0)), 90.0)
    _, gp, _ = b.sep(cp)
    poro = b.mul(b.sub(1.0, b.ss(dp, 0.05, 0.15)), b.ss(gp, 0.7, 0.75))
    n_f = b.noise(b.v('MULTIPLY', P, (20.0, 20.0, 20.0)), 1.0, 6.0, 0.6)
    blq = b.mix(b.mul(n_f, 0.2), blq, col(0.0, 0.0, 0.0), blend='MULTIPLY')
    blq = b.mix(b.mul(poro, 0.6), blq, col(0.15, 0.15, 0.14))
    # eflorescencias y manchas de humedad
    n_e = b.noise(b.v('MULTIPLY', P, (1.0, 1.0, 1.0)), 1.0, 5.0, 0.6)
    blq = b.mix(b.mul(b.ss(n_e, 0.6, 0.75), 0.3), blq, col(0.6, 0.6, 0.58))
    j = b.sub(1.0, b.ss(d, 0.005, 0.009))
    color = b.mix(j, blq, I['Color Mortero'])
    h = b.mul(b.ss(d, 0.004, 0.01), 0.004)
    h = b.madd(poro, -0.0015, h)
    h = b.madd(n_f, 0.0006, h)
    color, h, r_d = _desgaste(b, P, Zw, d, color, h, I['Desgaste'])
    color, suelo = _suciedad_base(b, P, U, Zw, color, I['Suciedad'])
    rough = b.add(b.madd(poro, 0.05, 0.88), r_d)
    b.L(color, go.inputs['Base Color']); b.L(b.clamp01(rough), go.inputs['Roughness']); b.L(h, go.inputs['Height'])
    auto_layout(g)
    return g


# ---------------------------------------------------------------- revoque de cemento
def grupo_revoque():
    """Revoque de cemento (pintado o no) con zócalo de pirca opcional (fotos 3 y 4)."""
    g, gi, go = nuevo_grupo('TI_Revoque', [
        ('Color Revoque', C, col(0.50, 0.48, 0.44)),
        ('Desgaste', F, 0.35, 0.0, 1.0),
        ('Suciedad', F, 0.5, 0.0, 1.0),
        ('Altura Zocalo', F, 0.45, -5.0, 5.0),
        ('Color Bajo Revoque', C, col(0.42, 0.14, 0.07)),
    ], [('Base Color', C), ('Roughness', F), ('Height', F)])
    b = B(g); I = gi.outputs
    c = coords(b); P, U = c['P'], c['U']
    Zw = _zw(b, c)
    # llana: manchas suaves y marcas en arco
    n_l = b.noise(b.v('MULTIPLY', P, (1.2, 1.2, 1.2)), 1.0, 4.0, 0.55)
    n_ll = b.noise(b.v('MULTIPLY', P, (5.0, 5.0, 5.0)), 1.0, 5.0, 0.6, 0.8)
    rev = b.hsv(I['Color Revoque'], 0.5, 1.0, b.madd(n_l, 0.16, 0.92))
    rev = b.mix(b.mul(n_ll, 0.1), rev, col(0.0, 0.0, 0.0), blend='MULTIPLY')
    # grietas finas
    n_g = b.noise(b.v('MULTIPLY', P, (1.6, 1.6, 1.6)), 1.0, 3.0, 0.5, 0.4)
    grieta = b.sub(1.0, b.ss(b.absf(b.sub(n_g, 0.5)), 0.0, 0.004))
    grieta = b.mul(grieta, b.mul(b.ss(b.noise(b.v('MULTIPLY', P, (0.6, 0.6, 0.6)), 1.0, 2.0, 0.5), 0.55, 0.65), I['Desgaste']))
    rev = b.mix(b.mul(grieta, 0.6), rev, col(0.1, 0.1, 0.1), blend='MULTIPLY')
    # piedras de la mampostería: se ven en el zócalo y donde el revoque se cayó
    vp = b.xyz(b.mul(U, 3.0), b.mul(Zw, 4.0), 0.0)
    jd, cel = _voronoi_borde(b, vp, 1.0)
    rr, gg, _ = b.sep(cel)
    piedra = b.hsv(col(0.17, 0.16, 0.15), 0.5, 1.0, b.madd(gg, 0.9, 0.6))
    piedra = b.mix(b.mul(b.ss(rr, 0.8, 0.85), 0.6), piedra, col(0.36, 0.33, 0.29))
    jz = b.sub(1.0, b.ss(jd, 0.02, 0.06))
    # revoque caído: deja ver la piedra asentada en barro, más abajo y en manchones
    abajo = b.sub(1.0, b.ss(Zw, 0.0, 1.8))
    n_c = b.noise(b.v('MULTIPLY', P, (1.0, 1.0, 1.0)), 1.0, 8.0, 0.62)
    caido = b.mr(b.sub(b.madd(abajo, 0.15, n_c), b.madd(I['Desgaste'], -0.3, 0.86)), 0.0, 0.02)
    bajo = b.mix(jz, piedra, b.hsv(I['Color Bajo Revoque'], 0.5, 1.0, b.madd(n_ll, 0.4, 0.8)))
    color = b.mix(caido, rev, bajo)
    borde = b.mul(b.mr(b.sub(b.madd(abajo, 0.12, n_c), b.madd(I['Desgaste'], -0.22, 0.86)), -0.01, 0.0), b.sub(1.0, caido))
    color = b.mix(b.mul(borde, 0.4), color, col(0.2, 0.2, 0.2), blend='MULTIPLY')
    color, _, _ = _desgaste(b, P, Zw, 1.0, color, 0.0, I['Desgaste'])
    color, suelo = _suciedad_base(b, P, U, Zw, color, I['Suciedad'], 0.9)
    # zócalo de pirca con borde superior irregular
    wob = b.mul(b.noise(b.xyz(b.mul(U, 1.5), 0.0, 0.0), 1.0, 2.0, 0.5), 0.12)
    zoc = b.sub(1.0, b.ss(b.sub(Zw, b.add(I['Altura Zocalo'], b.sub(wob, 0.06))), -0.008, 0.008))
    piedra = b.mix(jz, piedra, b.mix(0.5, b.hsv(I['Color Revoque'], 0.5, 0.8, 0.7), col(0.22, 0.18, 0.14)))
    color = b.mix(zoc, color, piedra)
    rough = b.lerp(caido, 0.88, 0.92)
    rough = b.lerp(zoc, rough, 0.85)
    h = b.mul(n_l, 0.003)
    h = b.madd(n_ll, 0.0008, h)
    h = b.madd(grieta, -0.001, h)
    h = b.lerp(caido, h, b.sub(b.mul(b.ss(jd, 0.0, 0.15), 0.02), 0.012))
    h = b.lerp(zoc, h, b.add(b.mul(b.ss(jd, 0.0, 0.15), 0.02), 0.008))
    b.L(color, go.inputs['Base Color']); b.L(b.clamp01(rough), go.inputs['Roughness']); b.L(h, go.inputs['Height'])
    auto_layout(g)
    return g


# ---------------------------------------------------------------- vidrio
def grupo_vidrio():
    g, gi, go = nuevo_grupo('TI_Vidrio', [
        ('Polvo', F, 0.4, 0.0, 1.0),
    ], [('Base Color', C), ('Roughness', F), ('Height', F)])
    b = B(g); I = gi.outputs
    c = coords(b); P = c['P']
    manchas = b.noise(b.v('MULTIPLY', P, (3.0, 3.0, 3.0)), 1.0, 6.0, 0.6)
    polvo = b.mul(b.ss(manchas, 0.45, 0.8), I['Polvo'])
    fondo = b.noise(b.v('MULTIPLY', P, (0.7, 0.7, 0.7)), 1.0, 3.0, 0.5)
    base = b.mix(fondo, col(0.010, 0.013, 0.016), col(0.035, 0.038, 0.036))
    base = b.mix(b.mul(polvo, 0.3), base, col(0.30, 0.27, 0.22))
    b.L(base, go.inputs['Base Color'])
    b.L(b.lerp(polvo, 0.05, 0.5), go.inputs['Roughness'])
    b.L(b.mul(polvo, 0.0002), go.inputs['Height'])
    auto_layout(g)
    return g


def crear_grupos():
    return dict(piedra_canteada=grupo_piedra_canteada(), pirca=grupo_pirca(),
                ladrillo=grupo_ladrillo(), bloque=grupo_bloque(), revoque=grupo_revoque(),
                vidrio=grupo_vidrio())
