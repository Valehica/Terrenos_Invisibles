# Animaciones del gaviotin sobre el rig de Rigify (Gabiortin9.blend).
#
# Se corre dentro de Blender:
#   exec(open(r".../Tools/Blender/gaviotin_animaciones.py").read())
#   crear_todas()            # o crear("Idle"), crear("Planear"), ...
#
# Cada animacion es una lista de poses clave. Una pose es un dict de "ajustes"
# que se aplican sobre la pose de reposo (alas extendidas) y se insertan como
# keyframes de todos los controles que usa el script, asi cada accion queda
# completa y no hereda nada de la anterior.
#
# Convenciones: cabeza hacia -Y, alas en X (.L = +X), arriba +Z, 24 fps.
# Los giros se dan en grados alrededor de ejes del MUNDO medidos en la pose de
# reposo de cada hueso (ver _girar): asi "Z" siempre es el eje vertical aunque
# el roll de cada control sea distinto.

import math

import bpy
from mathutils import Matrix, Quaternion, Vector

RIG = "rig.001"
FPS = 24

# Controles que el script toca (se les pone key en cada pose).
CONTROLES = [
    "root", "torso", "chest", "hips", "neck", "head",
    "spine_fk.004", "spine_fk.005", "spine_fk.006",
    "tweak_spine.004", "tweak_spine.005",
    "panza_ctrl", "alas_ctrl",
    "beak.001.T", "beak_001.B", "tongue.001.L",
    "n_feather.001.L", "n_feather.001.R",
    "spine_master.002", "spine.002", "spine.001", "spine", "t_feather.L", "t_feather.R",
]
for _l in ("L", "R"):
    CONTROLES += [f"shoulder.{_l}", f"Wing.{_l}", f"Wing.001.{_l}", f"Wing.002.{_l}",
                  f"w_feather.001.{_l}", f"w_feather.002.{_l}", f"w_feather.003.{_l}", f"w_feather.004.{_l}",
                  f"foot_ik.{_l}", f"thigh_ik_target.{_l}", f"toe.{_l}"]


def _rig():
    return bpy.data.objects[RIG]


def _espejo_nombre(n):
    if n.endswith(".L"):
        return n[:-2] + ".R"
    if n.endswith(".R"):
        return n[:-2] + ".L"
    return n


def _espejo_vec(v):
    return Vector((-v[0], v[1], v[2]))


def reposo():
    rig = _rig()
    for pb in rig.pose.bones:
        pb.location = (0, 0, 0)
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.rotation_euler = (0, 0, 0)
        pb.scale = (1, 1, 1)


def _q_rest(pb, eje, grados):
    """Cuaternion local que gira el hueso `grados` alrededor de un eje del mundo (en su reposo)."""
    R = pb.bone.matrix_local.to_3x3()
    q = Quaternion(Vector(eje).normalized(), math.radians(grados))
    return (R.inverted() @ q.to_matrix() @ R).to_quaternion()


def _girar(pb, eje, grados):
    q = _q_rest(pb, eje, grados)
    if pb.rotation_mode == "QUATERNION":
        pb.rotation_quaternion = q @ pb.rotation_quaternion
    else:
        pb.rotation_euler = (q @ pb.rotation_euler.to_quaternion()).to_euler(pb.rotation_mode)


def _mover(pb, v):
    """Traslada el control `v` en ejes del mundo (convertido a su espacio local de reposo)."""
    R = pb.bone.matrix_local.to_3x3()
    pb.location = pb.location + R.inverted() @ Vector(v)


def _apuntar(pb, destino, dorsal, cabeza=None):
    """Orienta el hueso para que su cola apunte a `destino` (armadura) y su lado de
    arriba (el Z del mundo en reposo) mire hacia `dorsal`. Sin `cabeza` solo gira;
    con `cabeza` tambien lleva el hueso ahi (para controles sueltos como foot_ik)."""
    bpy.context.view_layer.update()
    R0 = pb.bone.matrix_local.to_3x3()
    y0 = R0.col[1].normalized()
    up0 = Vector((0, 0, 1))
    z0 = (up0 - y0 * up0.dot(y0)).normalized()
    x0 = y0.cross(z0)
    mover = cabeza is not None
    cabeza = Vector(cabeza) if mover else pb.head.copy()
    y1 = (Vector(destino) - cabeza).normalized()
    d = Vector(dorsal)
    z1 = (d - y1 * d.dot(y1)).normalized()
    x1 = y1.cross(z1)
    B0 = Matrix((x0, y0, z0)).transposed()
    B1 = Matrix((x1, y1, z1)).transposed()
    rot = B1 @ B0.inverted()                       # giro en espacio armadura
    M = (rot @ R0).to_4x4()
    M.translation = cabeza
    pb.matrix = M
    bpy.context.view_layer.update()
    if not mover:
        pb.location = (0, 0, 0)


def foto(ruta, mira=(0, 0.3, 1.0), desde=(5, -4, 3), lente=50, res=(640, 480), ortho=None):
    """Render Workbench rapido desde una camara temporal (para revisar poses sin la ventana)."""
    sc = bpy.context.scene
    cam_data = bpy.data.cameras.new("TMP_cam")
    cam = bpy.data.objects.new("TMP_cam", cam_data)
    sc.collection.objects.link(cam)
    cam.location = desde
    d = Vector(mira) - Vector(desde)
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    cam_data.lens = lente
    if ortho:
        cam_data.type = "ORTHO"
        cam_data.ortho_scale = ortho
    viejo = (sc.camera, sc.render.engine, sc.render.resolution_x, sc.render.resolution_y,
             sc.render.resolution_percentage, sc.render.filepath, sc.render.film_transparent)
    ocultos = []
    for o in sc.objects:
        # el high poly esta oculto en el viewport pero no en render
        if (o.type == "ARMATURE" or o.hide_viewport or o.hide_get()) and not o.hide_render:
            o.hide_render = True
            ocultos.append(o)
    try:
        sc.camera = cam
        sc.render.engine = "BLENDER_WORKBENCH"
        sc.render.resolution_x, sc.render.resolution_y = res
        sc.render.resolution_percentage = 100
        sc.render.film_transparent = False
        sc.render.filepath = ruta
        bpy.ops.render.render(write_still=True)
    finally:
        (sc.camera, sc.render.engine, sc.render.resolution_x, sc.render.resolution_y,
         sc.render.resolution_percentage, sc.render.filepath, sc.render.film_transparent) = viejo
        for o in ocultos:
            o.hide_render = False
        bpy.data.objects.remove(cam)
        bpy.data.cameras.remove(cam_data)


# ---------------------------------------------------------------- piezas de pose

ALTURA_VUELO = 0.6     # en vuelo el cuerpo va 0.6 m sobre el root (todo en el lugar)


def _pb(n):
    return _rig().pose.bones[n]


def _g(nombre, eje, grados, lado=None):
    """Gira un control; con lado, `nombre` va sin sufijo y el eje se espeja para .R
    (un giro es un vector axial: al espejar X cambian el signo Y y Z)."""
    if lado is None:
        _girar(_pb(nombre), eje, grados)
        return
    if lado == "R":
        eje = (eje[0], -eje[1], -eje[2])
    _girar(_pb(f"{nombre}.{lado}"), eje, grados)


def _P(v, lado):
    return Vector((v[0] if lado == "L" else -v[0], v[1], v[2]))


X, Y, Z = (1, 0, 0), (0, 1, 0), (0, 0, 1)


def _pecho():
    """(punto, direccion) -> llevados del reposo a la pose actual del pecho (ORG-spine.005,
    de donde cuelga el hombro). Las alas plegada y media se dan en coordenadas del
    reposo: si fueran absolutas, al agacharse o cabecear el cuerpo el ala quedaba
    torcida respecto del lomo y el shape key Alas_Plegadas (calculado en Idle) no
    calzaba (Rascar_Panza tenia 27 pliegues contra 6 en Idle)."""
    bpy.context.view_layer.update()
    pb = _pb("ORG-spine.005")
    T = pb.matrix @ pb.bone.matrix_local.inverted()
    R = T.to_3x3()
    return (lambda v: T @ Vector(v)), (lambda d: R @ Vector(d))


def ala_plegada(lado):
    """Ala cerrada sobre el costado y el lomo, primarias cruzadas sobre la cola."""
    punto, dir_ = _pecho()
    P = lambda v: punto(_P(v, lado))
    pb = _pb
    # Todo el ala plegada mira hacia el mismo lado (afuera y arriba, apoyada entre
    # el costado y el lomo). Mezclar dorso arriba en el humero con dorso afuera en el
    # antebrazo torcia ~70 grados la zona de las secundarias y la arrugaba.
    dorso = dir_(_P((0.55, 0, 1), lado))
    _apuntar(pb(f"Wing.{lado}"), P((0.54, 0.36, 1.26)), dorso)
    _apuntar(pb(f"Wing.001.{lado}"), P((0.56, -0.26, 1.30)), dorso)
    _apuntar(pb(f"Wing.002.{lado}"), P((0.36, 1.15, 1.33)), dorso)
    # el abanico se cierra: todas las plumas quedan hacia la cola
    _apuntar(pb(f"w_feather.004.{lado}"), P((0.42, 0.72, 1.34)), dorso)
    _apuntar(pb(f"w_feather.003.{lado}"), P((0.50, 0.78, 1.22)), dorso)
    _apuntar(pb(f"w_feather.002.{lado}"), P((0.34, 1.05, 1.31)), dorso)
    _apuntar(pb(f"w_feather.001.{lado}"), P((0.20, 1.85, 1.26)), dorso)


def ala_media(lado, alto=0.0):
    """Ala a medio abrir, codo separado del cuerpo (paso intermedio al desplegar/plegar
    para que el ala no atraviese el lomo). alto: 0..1 la levanta."""
    punto, dir_ = _pecho()
    P = lambda v: punto(_P(v, lado))
    pb = _pb
    dz = 0.55 * alto
    dorso = dir_(_P((0.3, -0.2, 1), lado))
    _apuntar(pb(f"Wing.{lado}"), P((0.95, 0.15, 1.45 + dz * 0.6)), dorso)
    _apuntar(pb(f"Wing.001.{lado}"), P((1.30, -0.50, 1.60 + dz)), dorso)
    _apuntar(pb(f"Wing.002.{lado}"), P((1.75, 0.85, 1.75 + dz * 1.4)), dorso)
    _apuntar(pb(f"w_feather.004.{lado}"), P((0.85, 0.75, 1.40 + dz * 0.5)), dorso)
    _apuntar(pb(f"w_feather.003.{lado}"), P((1.15, 0.30, 1.50 + dz)), dorso)
    _apuntar(pb(f"w_feather.002.{lado}"), P((1.45, 0.75, 1.65 + dz * 1.2)), dorso)
    _apuntar(pb(f"w_feather.001.{lado}"), P((1.95, 1.55, 1.85 + dz * 1.6)), dorso)


def ala_vuelo(lado, batido=0.0, flexion=0.0, barrido=0.0, torsion=0.0):
    """Ala extendida desde el reposo.
    batido: grados, + baja la punta (downstroke), - la sube (upstroke).
    flexion: 0..1 pliega la mano hacia atras (upstroke, frenado).
    barrido: grados, + lleva el ala hacia atras, - hacia adelante.
    torsion: grados de cabeceo del ala (+ borde de ataque arriba)."""
    _g("Wing", Z, barrido, lado)
    # el batido se reparte con el hombro: todo en el humero abria una grieta en el lomo
    _g("shoulder", Y, batido * 0.3, lado)
    _g("Wing", Y, batido * 0.7, lado)
    _g("Wing", X, -torsion, lado)
    # en el upstroke la mano se pliega hacia atras y un poco hacia abajo
    _g("Wing.001", Z, 12 * flexion, lado)
    _g("Wing.002", Z, 45 * flexion, lado)
    _g("Wing.002", Y, 18 * flexion, lado)
    # la punta sigue al batido con retraso (flexibilidad de las primarias)
    _g("Wing.002", Y, batido * 0.25, lado)
    _g("w_feather.001", Y, batido * 0.25, lado)


def patas_paradas(agache=0.0):
    """Patas apoyadas (IK en el suelo); agache baja el cuerpo."""
    _mover(_pb("torso"), (0, 0.04 * agache, -0.16 * agache))


# Pose de las patas en vuelo que dejo el usuario (2026-10-08, Planear frame 1),
# relativa al hueso torso: asi sigue al cuerpo cuando cabecea o se inclina.
PATAS_VUELO = {
    "foot_ik.L": Matrix(((1.00000, 0.00000, 0.00000, 0.17000), (0.00000, -0.58334, -0.81223, 0.86564), (-0.00000, 0.81223, -0.58334, -0.57716), (0.00000, 0.00000, 0.00000, 1.00000))),
    "foot_ik.R": Matrix(((1.00000, -0.00000, -0.00000, -0.17000), (-0.00000, -0.58334, -0.81223, 0.86564), (0.00000, 0.81223, -0.58334, -0.57716), (0.00000, 0.00000, 0.00000, 1.00000))),
    "thigh_ik_target.L": Matrix(((0.94198, -0.02189, -0.33494, 0.10317), (0.02189, -0.99174, 0.12639, 1.32185), (-0.33494, -0.12639, -0.93373, -0.84630), (0.00000, 0.00000, 0.00000, 1.00000))),
    "thigh_ik_target.R": Matrix(((0.94198, 0.02189, 0.33494, -0.10317), (-0.02189, -0.99174, 0.12639, 1.32185), (0.33494, -0.12639, -0.93373, -0.84630), (0.00000, 0.00000, 0.00000, 1.00000))),
    "toe.L": Matrix(((-1.00000, -0.00000, -0.00001, 0.17000), (0.00001, 0.67446, -0.73833, 0.73303), (0.00001, -0.73831, -0.67449, -0.43217), (0.00000, 0.00000, 0.00000, 1.00000))),
    "toe.R": Matrix(((-1.00000, 0.00000, 0.00001, -0.17000), (-0.00001, 0.67446, -0.73833, 0.73303), (-0.00001, -0.73831, -0.67449, -0.43217), (0.00000, 0.00000, 0.00000, 1.00000))),
}


def _rel_reposo(n):
    """Matriz de reposo del control n relativa al torso en reposo."""
    b = _rig().data.bones
    return b["torso"].matrix_local.inverted() @ b[n].matrix_local


def _mezcla(M0, M1, t):
    l0, q0, _ = M0.decompose()
    l1, q1, _ = M1.decompose()
    M = q0.slerp(q1, t).to_matrix().to_4x4()
    M.translation = l0.lerp(l1, t)
    return M


def patas_recogidas(lado, recogida=1.0):
    """Patas en el aire, relativas al torso. recogida 0 = como estaban paradas
    (recien despegado: los pies siguen al cuerpo desde donde empujaron el suelo),
    1 = la pose de vuelo del usuario (PATAS_VUELO). Antes recogida 0 era la pata
    parada subida h, con los dedos hacia adelante, y al mezclarla con la recogida
    (dedos hacia atras) el pie y los dedos se cruzaban en zig-zag."""
    bpy.context.view_layer.update()
    torso = _pb("torso").matrix.copy()
    for n in ("foot_ik", "thigh_ik_target", "toe"):
        nombre = f"{n}.{lado}"
        _pb(nombre).matrix = torso @ _mezcla(_rel_reposo(nombre), PATAS_VUELO[nombre], recogida)
        bpy.context.view_layer.update()


def patas_aterrizaje(lado, alto):
    """Patas estiradas hacia abajo y adelante para tocar el suelo. alto: cuanto
    falta para el suelo (m)."""
    P = lambda v: _P(v, lado)
    talon = P((0.177, 0.12, 0.10 + alto))
    punta = P((0.177, -0.08, 0.02 + alto))
    _apuntar(_pb(f"foot_ik.{lado}"), talon, Vector((0, 0.3, 1)), cabeza=punta)


def cuerpo(sube=0.0, cabeceo=0.0, alabeo=0.0, adelante=0.0):
    """cabeceo: + baja el pecho (pico hacia abajo). alabeo: + inclina hacia .L"""
    t = _pb("torso")
    _mover(t, (0, -adelante, sube))
    _girar(t, X, cabeceo)
    _girar(t, Y, alabeo)


def cabeza(cabeceo=0.0, giro=0.0, ladeo=0.0, cuello=0.0, cuello_giro=0.0):
    """cabeceo: + baja el pico. giro: + mira hacia .L. ladeo: + inclina hacia .L.
    cuello: + dobla el cuello hacia adelante/abajo."""
    _girar(_pb("neck"), X, cuello)
    _girar(_pb("neck"), Z, cuello_giro)
    h = _pb("head")
    _girar(h, Z, giro)
    _girar(h, X, cabeceo)
    _girar(h, Y, ladeo)


def _direccion(az, el):
    a, e = math.radians(az), math.radians(el)
    return Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))


def _marco(y, dorsal):
    y = Vector(y).normalized()
    d = Vector(dorsal)
    z = (d - y * d.dot(y)).normalized()
    return Matrix((y.cross(z), y, z)).transposed()


def cabeza_a(punta, dorsal=(1, 0, 0.5)):
    """Orienta la cabeza para que el pico apunte a `punta` (armadura), con la
    coronilla hacia `dorsal`. El pico no va alineado con el hueso head: se usa
    la relacion rigida medida en reposo entre el hueso y la linea pivote->punta
    (iterar sobre el hueso divergia en algunos objetivos)."""
    rig = _rig()
    hb = rig.data.bones["head"]
    pivote0 = hb.head_local
    punta0 = rig.data.bones["DEF-beak.002.T"].tail_local
    H0 = hb.matrix_local.to_3x3()
    B0 = _marco(punta0 - pivote0, (0, 0, 1))
    O = B0.inverted() @ H0
    h = _pb("head")
    bpy.context.view_layer.update()
    pivote = h.head.copy()
    M = (_marco(Vector(punta) - pivote, dorsal) @ O).to_4x4()
    M.translation = pivote
    h.matrix = M
    bpy.context.view_layer.update()


def pico(abre=0.0):
    """abre: 0..1 (1 = boca bien abierta)."""
    _girar(_pb("beak_001.B"), X, 24 * abre)
    _girar(_pb("beak.001.T"), X, -5 * abre)
    _girar(_pb("tongue.001.L"), X, 10 * abre)


def cola(alza=0.0, abre=0.0, giro=0.0):
    """alza: grados (+ arriba). abre: grados de apertura de la horquilla. giro: timon."""
    m = _pb("spine_master.002")
    _girar(m, X, alza)
    _girar(m, Z, giro)
    _g("t_feather", Z, -abre, "L")
    _g("t_feather", Z, -abre, "R")


def respira(f):
    """f: -1..1. Pecho y vientre se inflan con la escala de los tweaks."""
    for n, k in (("tweak_spine.004", 0.012), ("tweak_spine.005", 0.018)):
        s = 1 + k * f
        _pb(n).scale = (s, 1, s)
    _mover(_pb("chest"), (0, 0, 0.006 * f))


def cresta(alza=0.0):
    """Plumas de la nuca (n_feather): + se erizan."""
    _g("n_feather.001", X, alza, "L")
    _g("n_feather.001", X, alza, "R")


def shape_keys(panza=0.0, alas=0.0):
    """Mueve los huesos de control de los shape keys (0.1 m por su Y local = 1)."""
    for n, v in (("panza_ctrl", panza), ("alas_ctrl", alas)):
        pb = _rig().pose.bones.get(n)
        if pb:
            pb.location = (0, 0.1 * max(0.0, min(1.0, v)), 0)


# ---------------------------------------------------------------- pose generica

def pose(alas="plegada", batido=0.0, flexion=0.0, barrido=0.0, torsion=0.0, alto=0.0,
         patas="paradas", agache=0.0, recogida=1.0, suelo=0.0,
         sube=0.0, cabeceo=0.0, alabeo=0.0, adelante=0.0,
         cab=0.0, giro=0.0, ladeo=0.0, cuello=0.0, cuello_giro=0.0,
         boca=0.0, cola_alza=0.0, cola_abre=0.0, cola_giro=0.0, aire=0.0, erizo=0.0,
         pico_a=None, pico_dir=None, dorsal_cab=(1, 0, 0.5), panza=0.0, alas_sk=None, plegado=0.0):
    """Arma una pose completa desde el reposo.
    alas: plegada | media (alto) | vuelo (batido, flexion, barrido, torsion)
    patas: paradas (agache) | aire (recogida, con el cuerpo subido `sube`) |
           aterrizaje (suelo = distancia de los pies al suelo)
    pico_a: si se da, la cabeza se orienta para que la punta del pico llegue ahi.
    pico_dir: (azimut, elevacion) en grados: el pico apunta en esa direccion desde la
    base de la cabeza (azimut 0 = adelante -Y, 90 = hacia +X; elevacion + = arriba).
    plegado: 0..1, con alas="vuelo": lleva el ala de esa pose de vuelo a la plegada
    (slerp de las rotaciones locales). Es la unica transicion abierta <-> plegada que
    los correctivos cubren (Alas_Medias en 0.75, ver gaviotin_shapekeys).
    panza, alas_sk: 0..1, huesos panza_ctrl y alas_ctrl. alas_sk es el plegado: por
    defecto 1 con las alas plegadas, `plegado` en vuelo y 0 con el ala media."""
    reposo()
    cuerpo(sube=sube, cabeceo=cabeceo, alabeo=alabeo, adelante=adelante)
    if patas == "paradas":
        patas_paradas(agache)
    for l in ("L", "R"):
        if alas == "plegada":
            ala_plegada(l)
        elif alas == "media":
            ala_media(l, alto)
        else:
            ala_vuelo(l, batido=batido, flexion=flexion, barrido=barrido, torsion=torsion)
        if patas == "aire":
            patas_recogidas(l, recogida)
        elif patas == "aterrizaje":
            patas_aterrizaje(l, suelo)
    if alas == "vuelo" and plegado > 0:
        _plegar(plegado)
    cabeza(cabeceo=cab, giro=giro, ladeo=ladeo, cuello=cuello, cuello_giro=cuello_giro)
    if pico_a is not None:
        cabeza_a(pico_a, dorsal_cab)
    elif pico_dir is not None:
        bpy.context.view_layer.update()
        cabeza_a(_pb("head").head + _direccion(*pico_dir), dorsal_cab)
    pico(boca)
    cola(alza=cola_alza, abre=cola_abre, giro=cola_giro)
    respira(aire)
    cresta(erizo)
    if alas_sk is None:
        alas_sk = {"plegada": 1.0, "vuelo": plegado}.get(alas, 0.0)
    shape_keys(panza=panza, alas=alas_sk)


ALAS = [f"{n}.{l}" for l in ("L", "R") for n in
        ("shoulder", "Wing", "Wing.001", "Wing.002",
         "w_feather.001", "w_feather.002", "w_feather.003", "w_feather.004")]


def _plegar(f):
    """Mezcla las alas de la pose actual (de vuelo) con las plegadas en fraccion f."""
    rig = _rig()
    A = {n: (rig.pose.bones[n].rotation_quaternion.copy(), rig.pose.bones[n].location.copy()) for n in ALAS}
    for n in ALAS:
        rig.pose.bones[n].rotation_quaternion = (1, 0, 0, 0)
        rig.pose.bones[n].location = (0, 0, 0)
    for l in ("L", "R"):
        ala_plegada(l)
    for n in ALAS:
        pb = rig.pose.bones[n]
        pb.rotation_quaternion = A[n][0].slerp(pb.rotation_quaternion, f)
        pb.location = A[n][1].lerp(pb.location, f)
    bpy.context.view_layer.update()


IDLE = dict(alas="plegada", cab=-2)
VUELO = dict(alas="vuelo", barrido=4, patas="aire", recogida=1.0, sube=ALTURA_VUELO,
             cabeceo=4, cab=3, cuello=-4, cola_abre=8, cola_alza=-2, panza=1.0)


def k(base, **cambios):
    d = dict(base)
    d.update(cambios)
    return d


def _seno(f, periodo, fase=0.0):
    return math.sin(2 * math.pi * (f / periodo + fase))


# ---------------------------------------------------------------- animaciones
# Cada una devuelve (ultimo frame, ciclica, [(frame, parametros de pose), ...]).
# Las que empiezan o terminan en reposo usan IDLE o VUELO tal cual, asi encadenan.

def _anim_idle():
    keys = []
    mirada = {1: (0, 0), 25: (14, 4), 41: (14, 4), 57: (-10, -3), 77: (-10, -3), 97: (0, 0)}
    ks = sorted(mirada)

    def mira(f):
        f0 = max(x for x in ks if x <= f)
        f1 = min(x for x in ks if x >= f)
        t = 0 if f1 == f0 else (f - f0) / (f1 - f0)
        return [mirada[f0][i] + (mirada[f1][i] - mirada[f0][i]) * t for i in (0, 1)]

    def base(f, **kw):
        g, l = mira(f)
        return k(IDLE, aire=_seno(f - 1, 48), giro=g, ladeo=l, alabeo=1.2 * _seno(f - 1, 96), **kw)

    for f in sorted(set(list(range(1, 98, 12)) + ks)):
        keys.append((f, base(f)))
    # sacudida corta de la cola
    keys += [(66, base(66)), (69, base(69, cola_alza=9)), (72, base(72, cola_alza=-2))]
    return 97, True, keys


def _anim_rascar():
    # El pico (0.92 m) es casi la mitad del cuerpo y el cuello es corto: apuntado al
    # cuerpo lo atraviesa (antes, en los frames 22-46 el pico entero quedaba adentro del
    # flanco y la cabeza se veia como una bola). Se acicala el costado del pecho con el
    # pico de lado y hacia abajo (pico_dir 90, -30), medido para que ningun vertice del
    # pico entre al cuerpo; frota moviendo esa direccion unos grados.
    abajo = dict(alas="plegada", cabeceo=8, cuello=20, agache=0.2, erizo=14, dorsal_cab=(1, 0, 0.5))
    keys = [(1, k(IDLE)),
            (10, k(IDLE, cabeceo=4, cuello=10, agache=0.1, erizo=6, pico_dir=(55, -15))),
            (18, k(abajo, pico_dir=(90, -30)))]
    f, signo = 22, 1
    while f <= 54:
        keys.append((f, k(abajo, pico_dir=(90 + RASCA_AZ * signo, -30 + RASCA_EL * signo),
                         boca=0.25 if signo > 0 else 0.0, aire=0.3 * signo)))
        f += 4
        signo = -signo
    keys += [(62, k(IDLE, cabeceo=4, cuello=10, agache=0.08, erizo=8, pico_dir=(45, -10))),
             # sacudon de plumas al terminar
             (66, k(IDLE, erizo=20, aire=0.8, ladeo=6)),
             (69, k(IDLE, erizo=4, ladeo=-5, cab=-6)),
             (73, k(IDLE))]
    return 73, False, keys


RASCA_AZ, RASCA_EL = 6, 5     # grados de frotado alrededor de (90, -30)


def _anim_gorjear():
    arriba = dict(alas="plegada", cabeceo=-5, cab=-28, cuello=-12, erizo=18, cola_alza=4)
    keys = [(1, k(IDLE)),
            (7, k(arriba, boca=0.1, aire=0.6)),
            (11, k(arriba, boca=1.0, aire=1.0, giro=-3)),
            (16, k(arriba, boca=0.15, aire=0.2)),
            (21, k(arriba, boca=0.9, aire=1.0, giro=3)),
            (26, k(arriba, boca=0.1, aire=0.0)),
            (32, k(arriba, boca=1.0, aire=1.0, cab=-34, giro=-4)),
            (40, k(arriba, boca=0.95, aire=0.8, cab=-34, giro=5)),
            (46, k(arriba, boca=0.1, aire=-0.3)),
            (54, k(IDLE, cab=4, aire=-0.4)),
            (61, k(IDLE))]
    return 61, False, keys


def _anim_planear():
    keys = []
    for f in range(1, 98, 12):
        fr = (f - 1) / 96
        sn = math.sin(2 * math.pi * fr)
        keys.append((f, k(VUELO, alabeo=7 * sn, giro=9 * sn, cola_giro=5 * sn,
                         batido=-6 * abs(sn), torsion=2 * abs(sn),
                         sube=ALTURA_VUELO + 0.03 * math.sin(4 * math.pi * fr),
                         aire=0.4 * _seno(f - 1, 48))))
    return 97, True, keys


def _tramo(f0, f1, p0, p1, curva=lambda t: t):
    """Una key por frame entre f0 y f1 (sin los extremos), mezclando los parametros
    numericos de p0 y p1 (los no numericos salen de p1). Para las transiciones de
    plegado: entre keys sueltas los cuaterniones no siguen el slerp de _plegar y el
    ala se sale del camino que cubren los correctivos."""
    keys = []
    for f in range(f0 + 1, f1):
        t = curva((f - f0) / (f1 - f0))
        d = dict(p1)
        for c, b in p1.items():
            a = p0.get(c, 0.0)
            if isinstance(b, (int, float)) and isinstance(a, (int, float)):
                d[c] = a + (b - a) * t
        for c, a in p0.items():
            if c not in p1 and isinstance(a, (int, float)):
                d[c] = a * (1 - t)
        keys.append((f, d))
    return keys


def _suave(t):
    return t * t * (3 - 2 * t)


# Las transiciones abierta <-> plegada van con alas="vuelo" y `plegado` (sin flexion:
# con la mano flexionada los correctivos no calzan, 20-30 pliegues). El paso por
# alas="media" daba ~40 pliegues y ya no se usa.
PLEGADA_V = dict(alas="vuelo", plegado=1.0)


def _anim_despegue():
    h = ALTURA_VUELO
    v = lambda **kw: k(VUELO, **kw)
    agachado = k(IDLE, agache=1.0, cabeceo=12, cuello=10, cab=6, cola_alza=6,
                 alas="vuelo", batido=-25, plegado=0.0)
    keys = [(1, k(IDLE, **PLEGADA_V)), (7, agachado)]
    keys += _tramo(1, 7, k(IDLE, **PLEGADA_V, batido=-25), agachado, _suave)
    keys += [(10, k(IDLE, agache=1.0, cabeceo=14, cuello=8, cab=4, alas="vuelo", batido=-35, flexion=0.9,
                    barrido=10, torsion=6, cola_alza=8)),
             # impulso: las patas se estiran mientras el ala sube
             (14, k(IDLE, agache=-0.15, cabeceo=8, alas="vuelo", batido=-55, flexion=0.7, torsion=8,
                    cola_abre=14)),
             (18, v(sube=0.30, recogida=0.0, batido=26, torsion=-6, cabeceo=10, cola_abre=16)),
             (22, v(sube=0.40, recogida=0.35, batido=-50, flexion=0.8, torsion=10, cabeceo=8, cola_abre=14)),
             (26, v(sube=0.50, recogida=0.65, batido=24, torsion=-5, cabeceo=7, cola_abre=12)),
             (30, v(sube=0.56, recogida=0.85, batido=-42, flexion=0.7, torsion=8, cabeceo=6)),
             (34, v(sube=0.60, recogida=1.0, batido=20, torsion=-4, cabeceo=5)),
             (40, v(sube=h + 0.02, batido=-8, flexion=0.2, torsion=2)),
             (49, k(VUELO))]
    return 49, False, keys


def _anim_aterrizaje():
    h = ALTURA_VUELO
    v = lambda **kw: k(VUELO, **kw)
    # se posa con las alas arriba, las estira (sin flexion) y las pliega despacio
    arriba = k(IDLE, agache=0.6, cabeceo=5, alas="vuelo", batido=-25, cola_abre=6, cola_alza=2, cab=3)
    plegada = k(IDLE, agache=0.1, **PLEGADA_V, cab=-3)
    keys = [(1, k(VUELO)),
            # se encabrita: cuerpo arriba, alas adelante con mucho angulo
            (8, v(cabeceo=-14, barrido=-12, torsion=18, batido=-8, recogida=0.5, sube=h - 0.04, cab=-8)),
            (13, v(cabeceo=-18, barrido=-8, torsion=14, batido=-50, flexion=0.6, patas="aterrizaje",
                   suelo=0.5, sube=0.50, cab=-12, cola_abre=18, cola_alza=8)),
            (17, v(cabeceo=-20, barrido=-20, torsion=24, batido=22, patas="aterrizaje", suelo=0.32,
                   sube=0.38, cab=-14, cola_abre=20, cola_alza=10)),
            (21, v(cabeceo=-16, barrido=-10, torsion=14, batido=-46, flexion=0.6, patas="aterrizaje",
                   suelo=0.16, panza=0.5, sube=0.22, cab=-10, cola_abre=18, cola_alza=8)),
            (25, v(cabeceo=-12, barrido=-16, torsion=20, batido=16, patas="aterrizaje", suelo=0.04, panza=0.2,
                   sube=0.07, cab=-6, cola_abre=14, cola_alza=6)),
            # toca el suelo: amortigua con las patas, alas arriba
            (29, k(IDLE, agache=0.9, cabeceo=8, alas="vuelo", batido=-38, flexion=0.4, torsion=6,
                   cola_abre=10, cola_alza=4, cab=6)),
            (34, arriba),
            (48, plegada)]
    keys += _tramo(34, 48, arriba, plegada, _suave)
    keys += [(52, k(IDLE, cab=-4)),
             (56, k(IDLE, aire=0.5)),
             (61, k(IDLE))]
    return 61, False, keys


ANIMACIONES = {
    "Idle": _anim_idle,
    "Rascar_Panza": _anim_rascar,
    "Gorjear": _anim_gorjear,
    "Planear": _anim_planear,
    "Despegue": _anim_despegue,
    "Aterrizaje": _anim_aterrizaje,
}


# ---------------------------------------------------------------- horneado a acciones

def _keyear(f, previos):
    rig = _rig()
    for n in CONTROLES:
        pb = rig.pose.bones.get(n)
        if pb is None:
            continue
        pb.keyframe_insert("location", frame=f, group=n)
        if pb.rotation_mode == "QUATERNION":
            q = pb.rotation_quaternion.copy()
            if n in previos and q.dot(previos[n]) < 0:   # mismo hemisferio que la key anterior
                q.negate()
                pb.rotation_quaternion = q
            previos[n] = q
            pb.keyframe_insert("rotation_quaternion", frame=f, group=n)
        else:
            pb.keyframe_insert("rotation_euler", frame=f, group=n)
        pb.keyframe_insert("scale", frame=f, group=n)


def _borrar_controles(act):
    """Quita de la accion solo las curvas de los CONTROLES del script; las que el
    usuario agrego a mano en otros huesos (p. ej. beak.002.B en Gorjear) quedan."""
    prefijos = tuple(f'pose.bones["{n}"]' for n in CONTROLES)
    for layer in act.layers:
        for strip in layer.strips:
            for cb in strip.channelbags:
                for fc in [fc for fc in cb.fcurves if fc.data_path.startswith(prefijos)]:
                    cb.fcurves.remove(fc)


def crear(nombre):
    rig = _rig()
    frames, ciclica, keys = ANIMACIONES[nombre]()
    act = bpy.data.actions.get(nombre)
    if act:
        _borrar_controles(act)
    else:
        act = bpy.data.actions.new(nombre)
    act.use_fake_user = True
    rig.animation_data_create()
    rig.animation_data.action = act
    previos = {}
    for f, params in sorted(keys, key=lambda x: x[0]):
        pose(**params)
        bpy.context.view_layer.update()
        _keyear(f, previos)
    act.use_frame_range = True
    act.frame_start, act.frame_end = 1, frames
    act.use_cyclic = ciclica
    return {"frames": frames, "keys": len(keys)}


def crear_todas():
    out = {n: crear(n) for n in ANIMACIONES}
    _rig().animation_data.action = bpy.data.actions["Idle"]
    bpy.context.scene.frame_set(1)
    bpy.ops.ed.undo_push(message="Animaciones gaviotin")
    return out
