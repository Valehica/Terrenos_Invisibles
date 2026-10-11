# Shape keys correctivos del gaviotin (Gabiortin9.blend, Pajaro_LowPoly) y sus drivers.
#
#   exec(open(r".../Tools/Blender/gaviotin_shapekeys.py").read())
#   crear_todo()
#
# - Panza_Vuelo: sube y afina el vientre (en vuelo se veia mucho la panza).
# - Alas_Plegadas: correctivo del ala plegada. Con las alas cerradas el skinning
#   lineal mezcla huesos que giran ~150 grados distinto y la malla se pliega y
#   muestra el reves de las caras. El objetivo se construye como una "hoja"
#   continua sobre el costado (cada vertice del ala se mapea por envergadura,
#   cuerda y grosor, asi no hay solapes ni caras invertidas) y se pasa a reposo
#   invirtiendo el skinning de cada vertice.
# Cada shape key lo maneja un hueso (panza_ctrl, alas_ctrl): subirlo 0.1 m por
# su Y local = 1. El exportador FBX muestrea el valor en cada frame, asi que lo
# que hace el driver llega a Unity como animacion de blend shape.
#
# La malla es la mitad -X (lado .R) con Mirror antes del Armature: el shape key
# se calcula en la mitad y el Mirror lo copia al otro lado.

import math

import bpy
import numpy as np
from mathutils import Matrix, Vector

MALLA = "Pajaro_LowPoly"
RIG = "rig.001"
RECORRIDO = 0.1      # m que sube el hueso de control para llegar a 1


def _ob():
    return bpy.data.objects[MALLA]


def _rig():
    return bpy.data.objects[RIG]


def _reposo_co():
    me = _ob().data
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    return co.reshape(-1, 3)


def _pesos():
    """Matriz (n_vertices x n_huesos_deform) con pesos normalizados como el Armature."""
    ob, rig = _ob(), _rig()
    deform = [b.name for b in rig.data.bones if b.use_deform]
    idx = {n: i for i, n in enumerate(deform)}
    grupos = {g.index: idx[g.name] for g in ob.vertex_groups if g.name in idx}
    W = np.zeros((len(ob.data.vertices), len(deform)))
    for v in ob.data.vertices:
        for g in v.groups:
            j = grupos.get(g.group)
            if j is not None:
                W[v.index, j] += g.weight
    s = W.sum(1, keepdims=True)
    W = np.divide(W, s, out=np.zeros_like(W), where=s > 0)
    return W, deform


def _matrices(deform):
    """Matriz de deformacion de cada hueso (pose @ reposo^-1), 4x4, espacio objeto."""
    rig = _rig()
    bpy.context.view_layer.update()
    M = np.zeros((len(deform), 4, 4))
    for i, n in enumerate(deform):
        pb = rig.pose.bones[n]
        M[i] = np.array(pb.matrix @ pb.bone.matrix_local.inverted())
    return M


def skinning(co_reposo=None):
    """Devuelve (A 3x3 por vertice, t por vertice, posicion deformada) de la pose actual."""
    W, deform = _pesos()
    M = _matrices(deform)
    A = np.einsum("vb,bij->vij", W, M[:, :3, :3])
    t = np.einsum("vb,bi->vi", W, M[:, :3, 3])
    co = _reposo_co() if co_reposo is None else co_reposo
    p = np.einsum("vij,vj->vi", A, co) + t
    return A, t, p


# ---------------------------------------------------------------- ala plegada

ALA_X0, ALA_X1 = 0.50, 3.84      # raiz y punta del ala (|x| en reposo)
MEMBRANA = 0.25                  # u hasta donde la raiz es membrana armonica
APLASTE = 0.15                   # fraccion del grosor del ala plegada en su frente (u = MEMBRANA)
U_GROSOR = 0.55                  # u donde el ala plegada recupera todo su grosor
U_RELAJAR = 0.5                  # hasta donde llega el suavizado fuerte del frente
RELAJAR = 30                     # pasadas de ese suavizado
GROSOR = 0.5                     # escala del grosor del ala en la hoja plegada
GRUPO_ALA = "Correctivo_Ala"     # vertex group con los vertices del ala (ver _ala)


def _ala():
    """bool por vertice: es ala. Sale del vertex group GRUPO_ALA y no de |x|: al
    editar la Basis el flanco se ensancho y 9 vertices del hombro pasaron x = 0.5;
    tratados como ala abrian una rendija en el lomo. La primera vez el grupo se crea
    con |x| > ALA_X0."""
    ob = _ob()
    vg = ob.vertex_groups.get(GRUPO_ALA)
    n = len(ob.data.vertices)
    if vg is None:
        co = _reposo_co()
        vg = ob.vertex_groups.new(name=GRUPO_ALA)
        vg.add([int(i) for i in np.where(np.abs(co[:, 0]) > ALA_X0)[0]], 1.0, "REPLACE")
    m = np.zeros(n, dtype=bool)
    for v in ob.data.vertices:
        for g in v.groups:
            if g.group == vg.index and g.weight > 0.5:
                m[v.index] = True
    return m


def _cuerpo_estricto(ala):
    """Cuerpo sin el anillo que toca el ala (el muñon de la raiz no cuenta como cuerpo)."""
    junto = np.zeros_like(ala)
    for a, b in (e.vertices for e in _ob().data.edges):
        if ala[a]:
            junto[b] = True
        if ala[b]:
            junto[a] = True
    return ~ala & ~junto


def _bvh_posado(p_cuerpo):
    """BVH del cuerpo (sin alas) en la pose, con las dos mitades y normales hacia afuera."""
    from mathutils.bvhtree import BVHTree
    cuerpo = _cuerpo_estricto(_ala())
    n = len(p_cuerpo)
    verts = [Vector(c) for c in p_cuerpo] + [Vector((-c[0], c[1], c[2])) for c in p_cuerpo]
    caras = []
    for p in _ob().data.polygons:
        vs = list(p.vertices)
        if cuerpo[vs].all():
            caras.append(vs)
            caras.append([i + n for i in reversed(vs)])
    return BVHTree.FromPolygons(verts, caras)


def _vecinos():
    me = _ob().data
    vec = [set() for _ in me.vertices]
    for e in me.edges:
        a, b = e.vertices
        vec[a].add(b)
        vec[b].add(a)
    return [list(v) for v in vec]


def objetivo_ala(p_skin, p_cuerpo=None, peso_ala=None):
    """Posicion limpia de cada vertice del ala plegada (espacio objeto, pose actual).
    Devuelve (objetivo, mezcla 0..1 por vertice)."""
    co = _reposo_co()
    ax = np.abs(co[:, 0])
    s = np.sign(co[:, 0])                       # la mitad es -X: s = -1
    s[s == 0] = -1
    u = np.clip((ax - ALA_X0) / (ALA_X1 - ALA_X0), 0, 1)
    ala = _ala()
    hoja = ala
    ax_p = np.maximum(ax, ALA_X0 + 0.05)          # los polinomios no se extrapolan

    # Parametros suaves (con valores crudos por vecindario la hoja salia con bultos):
    # bordes de ataque/fuga como polinomios ajustados a las tajadas, sin recortar
    # v (las puntas de las plumas siguen el mapeo), y grosor contra una superficie
    # media ajustada.
    bins = np.arange(ALA_X0, ALA_X1 + 0.15, 0.15)
    cen, ymin, ymax = [], [], []
    for b0 in bins:
        m = ala & (ax >= b0) & (ax < b0 + 0.15) & (co[:, 2] > 1.0)
        if m.sum() >= 3:
            cen.append(b0 + 0.075)
            ymin.append(co[m, 1].min())
            ymax.append(np.percentile(co[m, 1], 85))
    y_le = np.polyval(np.polyfit(cen, ymin, 4), ax_p)
    y_te = np.polyval(np.polyfit(cen, ymax, 4), ax_p)
    v = (co[:, 1] - y_le) / np.maximum(y_te - y_le, 0.05)
    # sin recortar: recortar v aplastaba el borde de ataque contra si mismo

    # superficie media z = f(x, y) cuadratica; w = altura sobre ella (+ = dorso)
    xa, ya = ax[ala], co[ala, 1]
    base = np.stack([np.ones_like(xa), xa, ya, xa * xa, xa * ya, ya * ya], 1)
    coef, *_ = np.linalg.lstsq(base, co[ala, 2], rcond=None)
    w = np.zeros(len(co))
    # (en la franja w = 0: es piel del cuerpo)
    # la planta se comprime ~2x al plegar: el grosor tambien, o queda abultado
    w[ala] = (co[ala, 2] - base @ coef) * GROSOR

    # hoja sobre el costado (lado +X; se espeja con s). Borde de ataque abajo y
    # borde de fuga arriba e inclinado hacia atras: asi el mapeo conserva la
    # orientacion de las caras (con el borde de ataque arriba saldria reflejado,
    # con todas las caras al reves) y se sigue viendo el dorso del ala.
    def suave(x, a, b):
        t = np.clip((x - a) / (b - a), 0, 1)
        return t * t * (3 - 2 * t)

    # el borde de ataque nace donde esta el de la raiz real (y -0.40, z 1.27), baja
    # por el costado y vuelve a subir hacia la cola; si nace mas abajo queda un
    # "bolsillo" abierto en el hombro
    Y = -0.40 + 2.22 * u + 0.35 * v * (1 - 0.5 * u)
    z_bajo = 1.27 - 0.15 * suave(u, 0.0, 0.35) + 0.10 * suave(u, 0.5, 1.0)
    alto = 0.30 * (1 - u) + 0.06
    Z = z_bajo + alto * v
    # hacia la punta las primarias se cruzan sobre la cola
    cruce = np.clip((u - 0.70) / 0.30, 0, 1)
    cruce = cruce * cruce * (3 - 2 * cruce)
    # minimo lateral: donde se termina el costado la hoja pasa por encima del lomo
    x_min = 0.20 * (1 - cruce) + 0.13 * cruce

    # Ancho del cuerpo en (Y, Z) desde una grilla suavizada del maximo |x| de los
    # vertices del cuerpo cercanos (los rayos contra la malla fallaban: sin las caras
    # del ala el cuerpo queda con un agujero en la raiz).
    margen = 0.02
    medio = max(0.03, float(-np.percentile(w[ala], 3)))   # la cara de abajo no entra al cuerpo
    cuerpo = co[_cuerpo_estricto(ala)]
    gy = np.arange(-0.8, 2.4, 0.02)
    gz = np.arange(0.7, 1.8, 0.02)
    grilla = np.zeros((len(gy), len(gz)))
    for a_, yy in enumerate(gy):
        cy = cuerpo[np.abs(cuerpo[:, 1] - yy) < 0.07]
        for b_, zz in enumerate(gz):
            c = cy[np.abs(cy[:, 2] - zz) < 0.07]
            grilla[a_, b_] = np.abs(c[:, 0]).max() if len(c) else 0.0
    for _ in range(3):                                      # desenfoque 3x3
        g = np.pad(grilla, 1, mode="edge")
        grilla = sum(g[1 + i:1 + i + len(gy), 1 + j:1 + j + len(gz)]
                     for i in (-1, 0, 1) for j in (-1, 0, 1)) / 9.0
    def ancho_cuerpo(Y, Z):
        iy = np.clip((Y - gy[0]) / 0.02, 0, len(gy) - 1.001)
        iz = np.clip((Z - gz[0]) / 0.02, 0, len(gz) - 1.001)
        y0, z0 = iy.astype(int), iz.astype(int)
        fy, fz = iy - y0, iz - z0
        return (grilla[y0, z0] * (1 - fy) * (1 - fz) + grilla[y0 + 1, z0] * fy * (1 - fz)
                + grilla[y0, z0 + 1] * (1 - fy) * fz + grilla[y0 + 1, z0 + 1] * fy * fz)

    # El grosor y la separacion del cuerpo crecen de APLASTE a 1 entre la raiz y
    # U_GROSOR: con el canto entero desde la raiz, el frente del ala plegada era un
    # rollo grueso (el antebrazo tiene 25 cm de alto en reposo).
    h = APLASTE + (1 - APLASTE) * suave(u, MEMBRANA, U_GROSOR)
    X = np.maximum(x_min * h, ancho_cuerpo(Y, Z) + margen + medio * h) + w * h

    obj = p_skin.copy()
    obj[hoja, 0] = s[hoja] * X[hoja]
    obj[hoja, 1] = Y[hoja]
    obj[hoja, 2] = Z[hoja]

    # El frente de la hoja (u < U_GROSOR) se apoya en la superficie del cuerpo
    # posado: el ancho por grilla lo dejaba flotando 2-8 cm y se veia el canto
    # cortado del ala, como el puño de una manga.
    if p_cuerpo is not None:
        bvh = _bvh_posado(p_cuerpo)
        g = suave(u, MEMBRANA, U_GROSOR)
        for i in np.where(hoja & (u >= MEMBRANA) & (u < U_GROSOR))[0]:
            q, nrm, _, _ = bvh.find_nearest(Vector(obj[i]))
            if q is not None:
                apoyado = np.array(q + nrm * (margen * 0.5 + (medio + w[i]) * h[i]))
                obj[i] = (1 - g[i]) * apoyado + g[i] * obj[i]

    mezcla = hoja.astype(float)

    vec = _vecinos()
    # Raiz como membrana armonica: los vertices del ala con u < MEMBRANA se resuelven
    # como promedio de sus vecinos, con borde fijo en el cuerpo y en la hoja. El
    # cuerpo que tiene peso de ala va entero a su skinning sin ala (se mantiene su
    # forma; promediar posiciones del cuerpo encogia el pecho y el cuello).
    if p_cuerpo is not None and peso_ala is not None:
        cuerpo_ala = ~hoja & (peso_ala > 0.01)
        obj[cuerpo_ala] = p_cuerpo[cuerpo_ala]
        mezcla[cuerpo_ala] = 1.0
        libre = hoja & (u < MEMBRANA)
        ids = np.where(libre)[0]
        for _ in range(400):
            nuevo = obj[ids].copy()
            for k, i in enumerate(ids):
                if vec[i]:
                    nuevo[k] = obj[vec[i]].mean(0)
            obj[ids] = nuevo

    # Frente relajado: la raiz y el comienzo de la hoja (u < U_RELAJAR) se suavizan
    # con mas fuerza (desvaneciendose hasta U_RELAJAR); el cuerpo queda fijo. Ahi se
    # junta la membrana con la hoja y quedaba un bulto con forma de puño.
    fr = suave(u, U_RELAJAR, 0.6 * U_RELAJAR)                 # 1 adelante, 0 en U_RELAJAR
    ids = np.where(hoja & (fr > 0))[0]
    for _ in range(RELAJAR):
        nuevo = obj[ids].copy()
        for k, i in enumerate(ids):
            if vec[i]:
                nuevo[k] = obj[i] + 0.5 * fr[i] * (obj[vec[i]].mean(0) - obj[i])
        obj[ids] = nuevo

    # suavizado de Laplace leve en el ala
    idx = np.where(mezcla > 0)[0]
    for _ in range(2):
        nuevo = obj.copy()
        for i in idx:
            if vec[i]:
                nuevo[i] = obj[i] * 0.5 + obj[vec[i]].mean(0) * 0.5
        obj[idx] = nuevo[idx]
    return obj, mezcla


def _a_reposo(obj, A, t, co, regul=1e-3):
    """Delta en reposo d tal que A (co + d) + t = obj (minimos cuadrados regularizados)."""
    b = obj - t
    At = np.transpose(A, (0, 2, 1))
    AtA = At @ A + regul * np.eye(3)[None]
    rhs = np.einsum("vij,vj->vi", At, b) + regul * co
    x = np.linalg.solve(AtA, rhs[..., None])[..., 0]
    return x - co


def _shape_key(nombre, delta):
    ob = _ob()
    if ob.data.shape_keys is None:
        ob.shape_key_add(name="Basis", from_mix=False)
    kb = ob.data.shape_keys.key_blocks.get(nombre) or ob.shape_key_add(name=nombre, from_mix=False)
    co = _reposo_co() + delta
    kb.data.foreach_set("co", co.astype(np.float32).ravel())
    kb.slider_min, kb.slider_max = 0.0, 1.0
    ob.data.update()
    return kb


def calcular_alas(pose_plegada):
    """pose_plegada(): deja el rig con las alas cerradas. Crea/actualiza Alas_Plegadas."""
    ob = _ob()
    if ob.data.shape_keys:
        for kb in ob.data.shape_keys.key_blocks:
            kb.value = 0.0
    pose_plegada()
    bpy.context.view_layer.update()
    co = _reposo_co()
    A, t, p = skinning(co)
    p_cuerpo, peso_ala = skinning_sin_ala(co)
    obj, mezcla = objetivo_ala(p, p_cuerpo, peso_ala)
    d = _a_reposo(obj, A, t, co)
    d[mezcla == 0] = 0
    d[np.abs(co[:, 0]) < 1e-4, 0] = 0.0      # costura del Mirror: si se mueve en X se abre
    return _shape_key("Alas_Plegadas", d), float(np.abs(d).max())


# Transicion abierta <-> plegada. alas_ctrl da el plegado f (0 abierta, 1 plegada) y el
# rig va por slerp de las rotaciones locales del ala entre las dos poses (pose(...,
# plegado=f) en gaviotin_animaciones). Hasta F_LIMPIO el skinning solo esta limpio; de
# ahi en adelante ningun valor de Alas_Plegadas sirve (13-60 pliegues), asi que en
# F_MEDIA hay un segundo correctivo, Alas_Medias, que lleva el ala a la mezcla de
# posiciones entre los dos estados limpios (F_LIMPIO sin correctivo y 1 con
# Alas_Plegadas). Los drivers reparten f entre los dos: ver _EXPRESIONES.
F_LIMPIO, F_MEDIA = 0.4, 0.8     # medidos con la Basis original (ala abierta): con 0.5 el inicio ya tenia 19 pliegues


def _correctivos_en(valores):
    keys = _ob().data.shape_keys
    for kb in keys.key_blocks:
        kb.value = valores.get(kb.name, 0.0)


def calcular_medias(pose_f):
    """pose_f(f): deja el rig en la transicion con el ala plegada en f. Crea/actualiza
    Alas_Medias (Alas_Plegadas tiene que estar calculado antes)."""
    co = _reposo_co()
    n = len(co)
    _correctivos_en({})
    pose_f(F_LIMPIO)
    bpy.context.view_layer.update()
    P0 = _evaluado()[:n]
    _correctivos_en({"Alas_Plegadas": 1.0})
    pose_f(1.0)
    bpy.context.view_layer.update()
    P1 = _evaluado()[:n]
    lam = (F_MEDIA - F_LIMPIO) / (1.0 - F_LIMPIO)
    obj = (1 - lam) * P0 + lam * P1
    _correctivos_en({})
    pose_f(F_MEDIA)
    bpy.context.view_layer.update()
    A, t, p = skinning(co)
    d = _a_reposo(obj, A, t, co)
    W, deform = _pesos()
    es_ala = np.array([any(k in nm for k in ("shoulder", "Wing", "w_feather")) for nm in deform])
    d[W[:, es_ala].sum(1) < 0.01] = 0          # solo lo que mueve el ala
    d[np.abs(co[:, 0]) < 1e-4, 0] = 0.0       # costura del Mirror
    return _shape_key("Alas_Medias", d), float(np.abs(d).max())


def diagnostico_alas(pose_plegada, valor=1.0):
    """Cuenta en el ala deformada: pliegues (caras vecinas con normales opuestas) y
    caras metidas en el cuerpo (paridad de rayos en 3 direcciones, por mayoria)."""
    from collections import defaultdict
    from mathutils.bvhtree import BVHTree
    ob = _ob()
    me = ob.data
    kb = me.shape_keys.key_blocks.get("Alas_Plegadas") if me.shape_keys else None
    co = _reposo_co()
    ax = np.abs(co[:, 0])
    n = len(co)
    u = np.clip((ax - ALA_X0) / (ALA_X1 - ALA_X0), 0, 1)
    polys = [list(p.vertices) for p in me.polygons]
    ala = _ala()
    cuerpo = _cuerpo_estricto(ala)
    ala_f = [i for i, vs in enumerate(polys) if ala[vs].mean() > 0.5]
    cuerpo_f = [vs for vs in polys if cuerpo[vs].all()]
    if kb:
        kb.value = valor
    pose_plegada()
    bpy.context.view_layer.update()
    P = _evaluado()[:n]
    bvh = BVHTree.FromPolygons([Vector(c) for c in P], cuerpo_f)

    def normal(vs):
        a, b, c = P[vs[0]], P[vs[1]], P[vs[2]]
        v = np.cross(b - a, c - a)
        return v / (np.linalg.norm(v) + 1e-12)

    N = {i: normal(polys[i]) for i in ala_f}
    P_pose = P
    P = co
    N0 = {i: normal(polys[i]) for i in ala_f}
    P = P_pose
    aristas = defaultdict(list)
    for i, vs in enumerate(polys):
        for a, b in zip(vs, vs[1:] + vs[:1]):
            aristas[tuple(sorted((a, b)))].append(i)
    # pliegue = el angulo entre caras vecinas se cerro mucho respecto del reposo
    # (en bordes finos las caras de arriba y abajo ya son opuestas en reposo)
    pliegues = set()
    for fs in aristas.values():
        if len(fs) == 2 and fs[0] in N and fs[1] in N:
            if np.dot(N[fs[0]], N[fs[1]]) < np.dot(N0[fs[0]], N0[fs[1]]) - 1.0:
                pliegues.add(fs[0])

    def paridad(c, d):
        k, o = 0, c.copy()
        while True:
            h = bvh.ray_cast(o, d, 5)
            if not h[0]:
                return k % 2
            k += 1
            o = h[0] + d * 1e-4

    dentro = []
    for i in ala_f:
        c = Vector(P[polys[i]].mean(0))
        votos = sum(paridad(c, Vector(d)) for d in ((0, 0, 1), (0, 0, -1), (0, 1, 0)))
        if votos >= 2:
            dentro.append(i)
    return {"pliegues": len(pliegues), "pliegues_u": sorted(round(float(u[polys[i]].mean()), 2) for i in pliegues),
            "dentro": len(dentro), "dentro_u": sorted(round(float(u[polys[i]].mean()), 2) for i in dentro)[:30]}


def skinning_sin_ala(co):
    """Posicion de cada vertice con el skinning sin los huesos del ala (renormalizado)
    y el peso total que tenia de ellos."""
    W, deform = _pesos()
    M = _matrices(deform)
    es_ala = np.array([any(k in n for k in ("shoulder", "Wing", "w_feather")) for n in deform])
    peso_ala = W[:, es_ala].sum(1)
    W2 = W.copy()
    W2[:, es_ala] = 0
    s = W2.sum(1, keepdims=True)
    pecho = deform.index("DEF-spine.005")
    sin = s[:, 0] < 1e-6
    W2[sin, pecho] = 1.0
    s[sin] = 1.0
    W2 /= s
    A = np.einsum("vb,bij->vij", W2, M[:, :3, :3])
    t = np.einsum("vb,bi->vi", W2, M[:, :3, 3])
    return np.einsum("vij,vj->vi", A, co) + t, peso_ala


def _evaluado():
    ob = _ob()
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = ev.to_mesh()
    co = np.array([v.co[:] for v in me.vertices])
    ev.to_mesh_clear()
    return co


# ---------------------------------------------------------------- panza

PANZA_SUBE = 0.40        # fraccion que se acorta la caida del vientre bajo la linea media
PANZA_ANGOSTA = 0.10     # fraccion que se angosta el vientre


def calcular_panza():
    """Panza_Vuelo: el vientre sube y se angosta (en reposo; las patas no se tocan)."""
    co = _reposo_co()
    ax = np.abs(co[:, 0])
    W, deform = _pesos()
    pierna = np.array([any(k in n for k in ("thigh", "shin", "foot", "toe", "t_"))
                       and "feather" not in n for n in deform])
    peso_pierna = W[:, pierna].sum(1)
    ala = _ala()
    cuerpo = ~ala & (ax < 0.45) & (co[:, 2] > 0.5)
    # linea media del cuerpo por tajadas en y
    ys = np.arange(-0.9, 2.2, 0.1)
    zc = []
    for y in ys:
        m = cuerpo & (np.abs(co[:, 1] - y) < 0.08)
        zc.append(0.5 * (co[m, 2].min() + co[m, 2].max()) if m.sum() > 3 else np.nan)
    zc = np.array(zc)
    ok = ~np.isnan(zc)
    zc_v = np.interp(co[:, 1], ys[ok], zc[ok])
    caida = np.clip(zc_v - co[:, 2], 0, None)
    f = np.clip(caida / 0.25, 0, 1)
    f = f * f * (3 - 2 * f)
    # solo de la quilla a la cloaca, y nada en la cabeza ni la cola
    fy = np.clip((co[:, 1] + 0.55) / 0.25, 0, 1) * np.clip((1.45 - co[:, 1]) / 0.3, 0, 1)
    f *= fy * (1 - np.clip(peso_pierna * 1.5, 0, 1)) * ~ala
    d = np.zeros_like(co)
    d[:, 2] = caida * PANZA_SUBE * f
    d[:, 0] = -co[:, 0] * PANZA_ANGOSTA * f
    return _shape_key("Panza_Vuelo", d), float(np.abs(d).max())


# ---------------------------------------------------------------- drivers

CONTROLES_SK = {"Panza_Vuelo": "panza_ctrl", "Alas_Plegadas": "alas_ctrl", "Alas_Medias": "alas_ctrl"}
# f = Y local / RECORRIDO (0..1). Alas_Medias es un triangulo F_LIMPIO -> F_MEDIA -> 1 y
# Alas_Plegadas sube de F_MEDIA a 1. Con f = 1 (todas las poses de ala plegada) solo
# queda Alas_Plegadas, como antes.
_F = f"min(max(y / {RECORRIDO}, 0.0), 1.0)"
_EXPRESIONES = {
    "Panza_Vuelo": _F,
    "Alas_Plegadas": f"min(max(({_F} - {F_MEDIA}) / {1 - F_MEDIA}, 0.0), 1.0)",
    "Alas_Medias": f"max(0.0, min(({_F} - {F_LIMPIO}) / {F_MEDIA - F_LIMPIO}, (1.0 - {_F}) / {1 - F_MEDIA}))",
}


def conectar_drivers():
    """Cada shape key = Y local de su hueso / RECORRIDO (0..1). El hueso solo se
    mueve en su Y local entre 0 y RECORRIDO."""
    ob, rig = _ob(), _rig()
    keys = ob.data.shape_keys
    for sk, hueso in CONTROLES_SK.items():
        kb = keys.key_blocks.get(sk)
        if kb is None:
            continue
        kb.value = 0.0
        ruta = f'key_blocks["{sk}"].value'
        keys.driver_remove(ruta)
        fc = keys.driver_add(ruta)
        drv = fc.driver
        drv.type = "SCRIPTED"
        var = drv.variables.new()
        var.name = "y"
        var.type = "TRANSFORMS"
        t = var.targets[0]
        t.id = rig
        t.bone_target = hueso
        t.transform_type = "LOC_Y"
        t.transform_space = "LOCAL_SPACE"
        drv.expression = _EXPRESIONES[sk]
        for m in list(fc.modifiers):
            fc.modifiers.remove(m)
        pb = rig.pose.bones[hueso]
        pb.lock_location = (True, False, True)
        pb.lock_rotation = (True, True, True)
        pb.lock_rotation_w = True
        pb.lock_scale = (True, True, True)
        c = pb.constraints.get("Recorrido") or pb.constraints.new("LIMIT_LOCATION")
        c.name = "Recorrido"
        c.owner_space = "LOCAL"
        c.use_min_y = c.use_max_y = True
        c.min_y, c.max_y = 0.0, RECORRIDO
        c.use_transform_limit = True


def crear_todo(pose_plegada, pose_f=None):
    """Calcula los shape keys y los conecta a sus huesos. Los drivers se quitan
    antes: con el driver puesto no se puede dejar el valor en 0 para medir.
    pose_f(f): la transicion (para Alas_Medias); sin ella Alas_Medias no se toca."""
    keys = _ob().data.shape_keys
    if keys and keys.animation_data:
        for sk in CONTROLES_SK:
            keys.driver_remove(f'key_blocks["{sk}"].value')
    _, a = calcular_alas(pose_plegada)
    if pose_f is not None:
        calcular_medias(pose_f)
    _, p = calcular_panza()
    conectar_drivers()
    bpy.ops.ed.undo_push(message="Shape keys gaviotin")
    return {"alas_max": round(a, 3), "panza_max": round(p, 3)}
