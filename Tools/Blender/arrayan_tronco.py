# Tronco del arrayan grande (Arboles_Centro_Collection9.blend), parecido a la foto de
# referencia: un solo tronco grueso de tallos fundidos (canales que giran), raices
# tabulares, un nudo bajo, un anillo abultado al frente y el "puno" de nudos colgantes.
#
# Siempre parte del respaldo (coleccion Respaldo_Arrayan_v1: Arrayan_v1, Hojas_v1), asi
# que se puede re-correr cambiando parametros:
#   1. junta los tres troncos de la v1 hacia un eje (y deforma las hojas igual, para que
#      sigan calzando con las ramas) y corta todo lo que esta bajo Z_CORTE; el tallo que
#      tenia el bloque que hacia de nudo se corta mas arriba (Z_CORTE_BLOQUE);
#   2. los tallos que quedaron con el borde abierto alto bajan como tubo hasta el tronco;
#   3. genera el tronco por anillos: cada anillo sale de un SDF 2D (union suave de cinco
#      tallos virtuales + nucleo), recorrido desde afuera en N_LADOS direcciones; desde
#      Z_UNION el radio se interpola hacia la silueta de los troncos v1 que siguen arriba;
#      luego bultos radiales (anillo, nudo, panza);
#   4. el puno: dedos = tubos de pocos lados que salen del tronco, hacen el nudillo y
#      cuelgan pegados a la corteza, mas una masa achatada que tapa las bases.
# Corteza = M_TroncoArrayan; las UV las hace el postprocesador de Unity (UV Box).
import bpy, bmesh, math, random
from mathutils import Vector

OBJ, HOJAS = "Arrayan", "Hojas.001"
RESP_OBJ, RESP_HOJAS = "Arrayan_v1", "Hojas_v1"

Z_CORTE = 5.0            # se borra la v1 bajo esta altura
Z_CORTE_BLOQUE = 9.0     # ... y el tallo del bloque, bajo esta (el bloque sube hasta ~8.9)
EJE_BLOQUE, RADIO_BLOQUE = Vector((0.62, 1.0)), 1.7  # en coords de la v1, sin juntar
JUNTAR = 0.62            # factor de junta de los troncos bajo Z_JUNTA0 (1 = sin cambio)
Z_JUNTA0, Z_JUNTA1 = 5.0, 8.6
EJE_JUNTA = Vector((0.45, 0.30))
CONVERGER = 0.55         # cuanto se acercan al eje los tallos que bajan como tubo

Z_TOPE = 6.35            # donde termina el tronco nuevo (adentro de los troncos v1)
Z_UNION = 5.0            # desde aca el tronco se va afinando hacia la silueta de los tallos
N_LADOS = 16
ANILLOS = [-0.45, -0.18, 0.05, 0.3, 0.6, 0.9, 1.15, 1.45, 1.8, 1.95, 2.1, 2.3, 2.8,
           3.4, 4.0, 4.6, 5.1, 5.5, 5.85, 6.1, Z_TOPE]
C_BASE, C_TOPE = Vector((0.50, 0.25)), Vector((0.47, 0.30))

# tallos virtuales: angulo (grados, 0 = +X), largo de raiz, grosor relativo
TALLOS = [(10, 1.8, 1.10), (82, 1.0, 0.95), (150, 1.6, 1.12), (222, 0.9, 0.88), (292, 1.4, 1.0)]
GIRO = 0.20              # rad/m que giran los canales
D_TALLO, R_TALLO = 0.37, 0.31
SUAVE = 0.04             # suavizado de la union (menos = canales mas marcados)
DECAE_RAIZ = 0.42        # m en que se apaga la raiz hacia arriba
FRENTE = 90              # grados: lado del puno y del anillo (+Y, donde estaba el bloque)

# puno: desfase angular (rad), radio del dedo, altura de la punta, salida del nudillo
DEDOS = [(-0.62, 0.22, 4.25, 0.30), (-0.24, 0.27, 3.85, 0.36), (0.14, 0.28, 3.70, 0.38),
         (0.50, 0.24, 3.95, 0.32), (0.82, 0.17, 4.35, 0.22)]
Z_NUDILLO = 5.15
LADOS_DEDO, PASOS_DEDO = 6, 6
SEMILLA = 7


def ss(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def smin(a, b, k):
    h = max(k - abs(a - b), 0.0) / k
    return min(a, b) - h * h * k * 0.25


def gauss(x):
    return math.exp(-x * x)


def angd(a, b):
    return (a - b + math.pi) % (2 * math.pi) - math.pi


def partes(bm):
    pid, k = {}, 0
    for v in bm.verts:
        if v in pid:
            continue
        st = [v]; pid[v] = k
        while st:
            x = st.pop()
            for e in x.link_edges:
                y = e.other_vert(x)
                if y not in pid:
                    pid[y] = k; st.append(y)
        k += 1
    return pid, k


def juntar(p):
    s = JUNTAR + (1 - JUNTAR) * ss(Z_JUNTA0, Z_JUNTA1, p.z)
    q = EJE_JUNTA + (Vector((p.x, p.y)) - EJE_JUNTA) * s
    return Vector((q.x, q.y, p.z))


def lazos_abiertos(bm):
    usados, out = set(), []
    for e in bm.edges:
        if not e.is_boundary or e in usados:
            continue
        st, lazo = [e], []
        usados.add(e)
        while st:
            x = st.pop(); lazo.append(x)
            for v in x.verts:
                for y in v.link_edges:
                    if y.is_boundary and y not in usados:
                        usados.add(y); st.append(y)
        out.append(lazo)
    return out


def bajar(bm, mw, mwi, lazo, z_fin, pasos=3, achicar=0.9):
    """Extruye un borde abierto hacia abajo, en pasos, hasta z_fin (queda adentro del tronco)."""
    vs = list({v for e in lazo for v in e.verts})
    P0 = [mw @ v.co for v in vs]
    c0 = sum(P0, Vector()) / len(P0)
    # el tubo converge al eje del tronco, si no la union arma un "puno de camisa" ancho
    c1 = Vector((c0.x + (C_TOPE.x - c0.x) * CONVERGER, c0.y + (C_TOPE.y - c0.y) * CONVERGER, z_fin))
    for k in range(1, pasos + 1):
        t = k / pasos
        r = bmesh.ops.extrude_edge_only(bm, edges=lazo)
        nuevos = [g for g in r['geom'] if isinstance(g, bmesh.types.BMVert)]
        lazo = [g for g in r['geom'] if isinstance(g, bmesh.types.BMEdge) and g.is_boundary]
        c = c0.lerp(c1, t)
        for v in nuevos:
            p = mw @ v.co
            q = p - (c0.lerp(c1, (k - 1) / pasos))       # relativo al centro del paso anterior
            q.z *= (1 - t)                                # el borde se aplana al bajar
            q.x *= 1 + (achicar - 1) / pasos; q.y *= 1 + (achicar - 1) / pasos
            v.co = mwi @ (c + q)


def preparar(bm, mw, mwi):
    pid, _ = partes(bm)
    # se corta todo bajo Z_CORTE; el tallo 2 (el del bloque que hacia de nudo) bajo
    # Z_CORTE_BLOQUE, pero solo cerca de su eje (no las ramas que salgan de ahi)
    def cortar(p, parte):
        if p.z < Z_CORTE:
            return True
        return parte == 2 and p.z < Z_CORTE_BLOQUE and \
            (Vector((p.x, p.y)) - EJE_BLOQUE).length < RADIO_BLOQUE
    borrar = [v for v in bm.verts if cortar(mw @ v.co, pid[v])]
    for v in bm.verts:
        v.co = mwi @ juntar(mw @ v.co)
    bmesh.ops.delete(bm, geom=borrar, context='VERTS')
    # los tallos que quedaron con el borde abierto alto se rehacen como tubo hasta el tronco
    for lazo in lazos_abiertos(bm):
        vs = {v for e in lazo for v in e.verts}
        zc = sum((mw @ v.co).z for v in vs) / len(vs)
        if zc > 6.0:
            bajar(bm, mw, mwi, lazo, 5.6, pasos=3 if zc > 7.5 else 2)


def secciones(bm, mw, zs):
    """Por cada pieza: [(z, cx, cy, radio)] cortando aristas a cada altura."""
    pid, k = partes(bm)
    W = {v: mw @ v.co for v in bm.verts}
    out = {}
    for z in zs:
        por = {}
        for e in bm.edges:
            a, b = e.verts
            A, B = W[a], W[b]
            if (A.z - z) * (B.z - z) < 0:
                por.setdefault(pid[a], []).append(A.lerp(B, (z - A.z) / (B.z - A.z)))
        for p, pts in por.items():
            if len(pts) < 3:
                continue
            cx = sum(q.x for q in pts) / len(pts); cy = sum(q.y for q in pts) / len(pts)
            ds = sorted(math.hypot(q.x - cx, q.y - cy) for q in pts)
            out.setdefault(p, []).append((z, cx, cy, min(ds[-1], ds[len(ds) // 2] * 1.25)))
    return out


def eje(z):
    c = C_BASE.lerp(C_TOPE, ss(-0.5, Z_TOPE, z))
    return Vector((c.x + 0.10 * math.sin(z * 0.85 + 0.4), c.y + 0.07 * math.sin(z * 0.6 + 2.0)))


def hacer_sdf(tallos):
    def tallos_en(z):
        out = []
        for lst in tallos.values():
            lst = sorted(lst)
            if z < lst[0][0] - 0.5:
                continue
            s = lst[0] if z <= lst[0][0] else lst[-1]
            for a, b in zip(lst, lst[1:]):
                if a[0] <= z <= b[0]:
                    t = (z - a[0]) / (b[0] - a[0])
                    s = tuple(a[i] + (b[i] - a[i]) * t for i in range(4))
                    break
            out.append(s[1:])
        return out

    def sdf_tronco(q, z):
        """Tallos virtuales fundidos (canales) + nucleo; en la base se abren en raices."""
        c = eje(z)
        raiz = math.exp(-(z + 0.45) / DECAE_RAIZ)
        d = (q - c).length - 0.50
        k = SUAVE + 0.22 * raiz
        for ang, largo, grosor in TALLOS:
            a = math.radians(ang) + GIRO * z
            dd = D_TALLO + largo * raiz
            rr = R_TALLO * grosor * (1 - 0.55 * raiz)
            cc = c + Vector((math.cos(a), math.sin(a))) * dd
            d = smin(d, (q - cc).length - rr, k)
        return d

    def sdf_tallos(q, z):
        """Silueta de los troncos de la v1 que salen arriba, apenas por dentro."""
        d = 1e9
        for sx, sy, sr in tallos_en(z):
            d = min(d, (q - Vector((sx, sy))).length - (sr - 0.04))
        return d
    return sdf_tronco, sdf_tallos


def bulto(th, z):
    fr = math.radians(FRENTE)
    frente = max(0.0, math.cos(angd(th, fr)))
    b = 0.16 * gauss((z - 1.95) / 0.14) * (0.15 + 0.85 * frente ** 1.5)       # anillo
    b += 0.17 * gauss((z - 1.05) / 0.16) * gauss(angd(th, math.radians(52)) / 0.38)  # nudo bajo
    b += 0.06 * gauss((z - 3.1) / 0.4) * gauss(angd(th, fr) / 0.8)            # panza bajo el puno
    return b


def borde(sdf, c, dv, z, minimo):
    """Desde afuera hacia el centro, primer punto adentro (el anillo queda estrellado)."""
    r = 3.4
    while r > minimo and sdf(c + dv * r, z) > 0:
        r -= 0.01
    return r


def tronco(bm, mwi, sdfs, rnd):
    sdf_tronco, sdf_tallos = sdfs
    anillos, radios = [], {}
    for z in ANILLOS:
        c = eje(z); anillo, rr = [], []
        # arriba el radio pasa del tronco acanalado a la silueta de los tallos (sin
        # mezclar los SDF: la union suave inflaba un "puno de camisa" alrededor)
        t = ss(Z_UNION, Z_TOPE, z)
        for i in range(N_LADOS):
            th = 2 * math.pi * i / N_LADOS + 0.5 * GIRO * z
            dv = Vector((math.cos(th), math.sin(th)))
            r = borde(sdf_tronco, c, dv, z, 0.02)
            if t > 0:
                r += (borde(sdf_tallos, c, dv, z, 0.15) - r) * t
            r = (r + bulto(th, z)) * (1 + rnd.uniform(-0.02, 0.02))
            anillo.append(bm.verts.new(mwi @ Vector((c.x + dv.x * r, c.y + dv.y * r, z))))
            rr.append((th, r))
        anillos.append(anillo); radios[z] = (c, rr)
    caras = []
    for a, b in zip(anillos, anillos[1:]):
        for i in range(N_LADOS):
            j = (i + 1) % N_LADOS
            caras.append(bm.faces.new((a[i], a[j], b[j], b[i])))
    ct, cb = eje(ANILLOS[-1]), eje(ANILLOS[0])
    vt = bm.verts.new(mwi @ Vector((ct.x, ct.y, ANILLOS[-1] + 0.03)))
    vb = bm.verts.new(mwi @ Vector((cb.x, cb.y, ANILLOS[0] - 0.05)))
    for i in range(N_LADOS):
        j = (i + 1) % N_LADOS
        caras.append(bm.faces.new((anillos[-1][i], anillos[-1][j], vt)))
        caras.append(bm.faces.new((anillos[0][j], anillos[0][i], vb)))
    return caras, radios


def radio_en(radios, th, z):
    """Centro y radio del tronco generado en (angulo, altura), interpolando."""
    zs = sorted(radios)
    z = min(max(z, zs[0]), zs[-1])
    a, b = zs[0], zs[1]
    for a, b in zip(zs, zs[1:]):
        if a <= z <= b:
            break
    t = (z - a) / (b - a) if b > a else 0

    def en(zz):
        c, rr = radios[zz]
        n = len(rr)
        i0 = min(range(n), key=lambda i: abs(angd(th, rr[i][0])))
        i1 = (i0 + 1) % n if angd(th, rr[i0][0]) > 0 else (i0 - 1) % n
        s = abs(angd(th, rr[i0][0])) / max(1e-6, abs(angd(rr[i1][0], rr[i0][0])))
        return c, rr[i0][1] * (1 - s) + rr[i1][1] * s
    ca, ra = en(a); cb, rb = en(b)
    return ca.lerp(cb, t), ra + (rb - ra) * t


def tubo(bm, mwi, pts, radios_tubo, lados, punta=True):
    """Tubo por una polilinea (mundo), tapado en la punta con un polo."""
    caras, anillos = [], []
    for i, p in enumerate(pts):
        tg = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        ref = Vector((0, 0, 1)) if abs(tg.z) < 0.9 else Vector((1, 0, 0))
        u = tg.cross(ref).normalized(); w = tg.cross(u)
        anillo = []
        for k in range(lados):
            a = 2 * math.pi * k / lados
            anillo.append(bm.verts.new(mwi @ (p + (u * math.cos(a) + w * math.sin(a)) * radios_tubo[i])))
        anillos.append(anillo)
    for a, b in zip(anillos, anillos[1:]):
        for k in range(lados):
            j = (k + 1) % lados
            caras.append(bm.faces.new((a[k], a[j], b[j], b[k])))
    if punta:
        tg = (pts[-1] - pts[-2]).normalized()
        polo = bm.verts.new(mwi @ (pts[-1] + tg * radios_tubo[-1] * 0.8))
        for k in range(lados):
            caras.append(bm.faces.new((anillos[-1][k], anillos[-1][(k + 1) % lados], polo)))
    return caras


def catmull(ctrl, n):
    out = []
    P = [ctrl[0]] + ctrl + [ctrl[-1]]
    segs = len(ctrl) - 1
    for s in range(n):
        u = s / (n - 1) * segs
        i = min(int(u), segs - 1); t = u - i
        p0, p1, p2, p3 = P[i], P[i + 1], P[i + 2], P[i + 3]
        out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
                          + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t))
    return out


def puno(bm, mwi, radios, rnd):
    fr = math.radians(FRENTE)
    caras = []

    def punto(th, z, salida):
        c, r = radio_en(radios, th, z)
        return Vector((c.x + math.cos(th) * (r + salida), c.y + math.sin(th) * (r + salida), z))
    for off, rf, zpunta, sal in DEDOS:
        th = fr + off
        zn = Z_NUDILLO - abs(off) * 0.35 + rnd.uniform(-0.05, 0.05)
        ctrl = [punto(th, zn - 0.25, -0.15),
                punto(th, zn + 0.02, sal * 0.55),
                punto(th + off * 0.05, zn - 0.30, sal),
                punto(th + off * 0.10, (zn - 0.3 + zpunta) / 2, sal * 0.80),
                punto(th + off * 0.12, zpunta + 0.08, rf * 0.55)]
        pts = catmull(ctrl, PASOS_DEDO)
        # grosor parejo con la punta abultada (los nudos de la foto terminan en bola)
        rad = [rf * (1.0 - 0.12 * t + 0.18 * gauss((t - 0.85) / 0.15))
               for t in (i / (len(pts) - 1) for i in range(len(pts)))]
        caras += tubo(bm, mwi, pts, rad, LADOS_DEDO)
    # masa que tapa las bases de los dedos (esfera achatada, 8 x 5)
    zc = Z_NUDILLO - 0.30
    c, r = radio_en(radios, fr, zc)
    cen = Vector((c.x, c.y, zc)) + Vector((math.cos(fr), math.sin(fr), 0)) * (r + 0.02)
    radial = Vector((math.cos(fr), math.sin(fr), 0)); tang = Vector((-math.sin(fr), math.cos(fr), 0))
    SX, SY, SZ = 0.80, 0.45, 0.50
    filas = []
    for j in range(1, 5):
        ph = math.pi * j / 5
        fila = []
        for i in range(8):
            a = 2 * math.pi * i / 8
            p = cen + tang * (math.cos(a) * math.sin(ph) * SX) + radial * (math.sin(a) * math.sin(ph) * SY) \
                + Vector((0, 0, math.cos(ph) * SZ))
            fila.append(bm.verts.new(mwi @ p))
        filas.append(fila)
    sup = bm.verts.new(mwi @ (cen + Vector((0, 0, SZ))))
    inf = bm.verts.new(mwi @ (cen - Vector((0, 0, SZ))))
    for a, b in zip(filas, filas[1:]):
        for i in range(8):
            j = (i + 1) % 8
            caras.append(bm.faces.new((a[i], a[j], b[j], b[i])))
    for i in range(8):
        j = (i + 1) % 8
        caras.append(bm.faces.new((filas[0][j], filas[0][i], sup)))
        caras.append(bm.faces.new((filas[-1][i], filas[-1][j], inf)))
    return caras


def main():
    rnd = random.Random(SEMILLA)
    ob, resp = bpy.data.objects[OBJ], bpy.data.objects[RESP_OBJ]
    mw = ob.matrix_world; mwi = mw.inverted()
    bm = bmesh.new(); bm.from_mesh(resp.data)
    preparar(bm, mw, mwi)
    tallos = secciones(bm, mw, [4.9 + 0.15 * i for i in range(12)])
    caras_t, radios = tronco(bm, mwi, hacer_sdf(tallos), rnd)
    caras_p = puno(bm, mwi, radios, rnd)
    bmesh.ops.recalc_face_normals(bm, faces=caras_t + caras_p)
    for f in bm.faces:
        f.smooth = True
    bm.to_mesh(ob.data); bm.free()
    ob.data.update()
    # hojas: la misma junta que los troncos
    h, hr = bpy.data.objects[HOJAS], bpy.data.objects[RESP_HOJAS]
    hw = h.matrix_world; hwi = hw.inverted()
    for v, vr in zip(h.data.vertices, hr.data.vertices):
        v.co = hwi @ juntar(hw @ vr.co)
    h.data.update()
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    return {"tris": tris, "tris_v1": sum(len(p.vertices) - 2 for p in resp.data.polygons),
            "tronco": len(caras_t), "puno": len(caras_p)}


if __name__ == "__main__":
    print(main())
