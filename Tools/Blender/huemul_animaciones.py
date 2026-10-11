# Animaciones del huemul sobre el rig de Rigify (Concept/Sur/Ñandu1.blend, "rig").
#
# Se corre dentro de Blender:
#   exec(open(r".../Tools/Blender/huemul_animaciones.py", encoding="utf-8").read())
#   crear_todas()            # o crear("Idle"), crear("Walk"), crear("Run")
#   foto("prueba", "Walk", [1, 9, 17, 25])   # hoja de contactos para revisar
#
# Todo va en el lugar (sin root motion), 24 fps, ciclos cerrados: el ultimo
# frame es igual al primero. Cada frame se calcula con una funcion de la fase
# del ciclo y se le pone key a todos los CONTROLES, asi cada accion queda
# completa y no hereda nada de otra.
#
# Convenciones del mundo: cabeza hacia +Y, arriba +Z, .L en -X (izquierda real).
# Giros en grados alrededor de ejes del MUNDO medidos en la pose de reposo de
# cada hueso (ver _girar): X = cabeceo (+ levanta la nariz), Y = alabeo, Z = giro.

import math

import bpy
from mathutils import Quaternion, Vector

RIG = "rig"
FPS = 24

# Las patas van en FK, resueltas por pata_fk(): la cadena de pezuña de Rigify
# (IK doble con la caña siguiendo al 60 %) dobla el carpo al reves en cuanto la
# pata se adelanta, y estira los huesos.
# PATAS: nombre -> (delantera?, cadena FK de arriba hasta la pezuña)
PATAS = {}
for _l in ("L", "R"):
    PATAS["del." + _l] = (True, [f"upper_arm_fk.{_l}", f"forearm_fk.{_l}", f"forefoot_fk.{_l}",
                                 f"f_toe_fk.{_l}", f"f_hoof.{_l}"])
    PATAS["tra." + _l] = (False, [f"thigh_fk.{_l}", f"lower_leg_fk.{_l}", f"hind_foot_fk.{_l}",
                                  f"r_toe_fk.{_l}", f"r_hoof.{_l}"])
IKFK = [f"{n}_parent.{l}" for n in ("upper_arm", "thigh") for l in ("L", "R")]
CONTROLES = ["torso", "hips", "chest", "neck", "head", "jaw", "jaw.001",
             "ear.L", "ear.R", "ear.L.001", "ear.R.001",
             "shoulder.L", "shoulder.R"] + [b for _, c in PATAS.values() for b in c]


def _rig():
    return bpy.data.objects[RIG]


def reposo():
    for pb in _rig().pose.bones:
        pb.location = (0, 0, 0)
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.rotation_euler = (0, 0, 0)
        pb.scale = (1, 1, 1)


def _girar(pb, eje, grados):
    """Gira el hueso `grados` alrededor de un eje del mundo (medido en su reposo)."""
    R = pb.bone.matrix_local.to_3x3()
    q = Quaternion(Vector(eje).normalized(), math.radians(grados))
    q = (R.inverted() @ q.to_matrix() @ R).to_quaternion()
    pb.rotation_quaternion = q @ pb.rotation_quaternion


def _mover(pb, d):
    """Desplaza el hueso d metros (vector del mundo) desde su reposo."""
    pb.location = pb.bone.matrix_local.to_3x3().inverted() @ Vector(d)


def _ss(t):
    t = min(max(t, 0.0), 1.0)
    return t * t * (3 - 2 * t)


def _onda(fase, ciclos=1.0, desfase=0.0):
    return math.sin(2 * math.pi * (fase * ciclos + desfase))


# ---------------------------------------------------------------- patas ----
def pezuña(fase, apoyo, paso, alto, delantera):
    """Trayectoria de la punta de una pezuña, en el lugar.
    fase 0 = pisa adelante. Durante el apoyo (fraccion `apoyo` del ciclo)
    retrocede `paso` metros pegada al suelo; en el vuelo vuelve adelante
    levantada `alto`. Devuelve (dy, dz, h, s): h = cabeceo de la pezuña
    (grados, - = punta abajo) y s = avance del vuelo 0..1 (None en el apoyo)."""
    fase %= 1.0
    if fase < apoyo:
        t = fase / apoyo
        # al final del apoyo despega el talon: gira sobre la punta
        return paso / 2 - paso * t, 0.0, -25 * _ss((t - 0.72) / 0.28), None
    s = (fase - apoyo) / (1 - apoyo)
    dy = -paso / 2 + paso * _ss(s)
    pico = 0.4 if delantera else 0.5             # la delantera sube antes
    dz = alto * (math.sin(math.pi / 2 * s / pico) if s < pico
                 else math.cos(math.pi / 2 * (s - pico) / (1 - pico)))
    return dy, dz, -25 * (1 - _ss(s / 0.6)), s


def _ang(v):
    """Angulo de un vector en el plano YZ (giro alrededor de +X)."""
    return math.atan2(v.z, v.y)


def _rx(v, a):
    c, s_ = math.cos(a), math.sin(a)
    return Vector((v.x, c * v.y - s_ * v.z, s_ * v.y + c * v.z))


def pata_fk(rig, cadena, delantera, dy, dz, h, s, plegar):
    """Pone la cadena FK de una pata para que la punta de la pezuña quede en
    reposo + (dy, dz), con la pezuña girada h grados.
    plegar = (caña, cuartilla): grados de pliegue a mitad del vuelo.
    Devuelve cuanto le falto al hombro/cadera para alcanzar (m)."""
    pbs = [rig.pose.bones[n] for n in cadena]
    rest = [pb.bone.head_local.copy() for pb in pbs] + [pbs[-1].bone.tail_local.copy()]
    S = pbs[0].head.copy()                       # hombro/cadera, ya movidos por el cuerpo
    T = rest[5] + Vector((0, dy, dz))            # punta de la pezuña
    # caña: sigue el 60 % de la inclinacion de la linea hombro->pezuña (en el
    # apoyo la pata va casi recta); en el vuelo se pliega
    base = 0.6 * (_ang(S - T) - _ang(rest[0] - rest[5]))
    f = math.sin(math.pi * min(s / 0.85, 1.0)) if s is not None else 0.0
    sg = -1 if delantera else 1                  # delantera: la caña se pliega hacia atras
    c = base + math.radians(sg * plegar[0] * f)
    ha = math.radians(h - plegar[1] * f)
    p = 0.5 * (c + ha)                           # la cuartilla, a medio camino
    H1 = T - _rx(rest[5] - rest[4], ha)
    F = H1 - _rx(rest[4] - rest[3], p)
    K = F - _rx(rest[3] - rest[2], c)
    # dos huesos S-E-K, con el codo / la babilla del mismo lado que en reposo
    a = (rest[1] - rest[0]).length; b = (rest[2] - rest[1]).length
    u0 = rest[2] - rest[0]; e0 = rest[1] - rest[0]
    lado = 1 if (u0.y * e0.z - u0.z * e0.y) > 0 else -1
    d = K - S; D = d.length
    falta = max(0.0, D - (a + b - 1e-3))
    D = min(max(D, abs(a - b) + 1e-3), a + b - 1e-3)
    alfa = math.acos((a * a + D * D - b * b) / (2 * a * D))
    E = S + _rx(d.normalized(), lado * alfa) * a
    puntos = [S, E, S + d.normalized() * D, F, H1, T]
    for i, pb in enumerate(pbs):
        giro = _ang(puntos[i + 1] - puntos[i]) - _ang(rest[i + 1] - rest[i])
        M = (Quaternion((1, 0, 0), giro).to_matrix() @ pb.bone.matrix_local.to_3x3()).to_4x4()
        M.translation = pb.head
        pb.matrix = M
        bpy.context.view_layer.update()
    return falta


# ---------------------------------------------------------------- poses ----
def _cuerpo(rig, torso=(0, 0, 0), torso_rot=(0, 0, 0), hips=(0, 0, 0), chest=(0, 0, 0),
            neck=(0, 0, 0), head=(0, 0, 0), jaw=0.0, orejas=((0, 0), (0, 0))):
    """Rotaciones como (cabeceo X, alabeo Y, giro Z) en grados."""
    pb = rig.pose.bones
    _mover(pb["torso"], torso)
    for nombre, rot in (("torso", torso_rot), ("hips", hips), ("chest", chest), ("neck", neck), ("head", head)):
        for eje, g in zip(((1, 0, 0), (0, 1, 0), (0, 0, 1)), rot):
            if g:
                _girar(pb[nombre], eje, g)
    if jaw:
        _girar(pb["jaw"], (1, 0, 0), -jaw)
        _girar(pb["jaw.001"], (1, 0, 0), -jaw * 0.5)
    # orejas: (hacia atras, hacia afuera) en grados; la .L esta en -X
    for lado, (atras, afuera) in zip(("L", "R"), orejas):
        sx = -1 if lado == "L" else 1
        if atras:
            _girar(pb[f"ear.{lado}"], (1, 0, 0), -atras)
        if afuera:
            _girar(pb[f"ear.{lado}"], (0, 1, 0), afuera * sx)


def _key(rig, frame):
    for n in IKFK:
        rig.pose.bones[n].keyframe_insert('["IK_FK"]', frame=frame)
    for n in CONTROLES:
        pb = rig.pose.bones[n]
        pb.keyframe_insert("location", frame=frame)
        pb.keyframe_insert("rotation_quaternion", frame=frame)


# ---------------------------------------------------------------- ciclos ---
def pose_idle(fase):
    """4 s: respira, rumia, mira a los costados y mueve las orejas."""
    resp = _onda(fase, 3)                                       # 3 respiraciones
    # mirada: a la izquierda (0.15-0.4), al frente, a la derecha (0.6-0.8)
    mira = _ss((fase - 0.12) / 0.1) - _ss((fase - 0.38) / 0.1) \
        - 0.7 * (_ss((fase - 0.58) / 0.1) - _ss((fase - 0.8) / 0.1))
    baja = 0.5 - 0.5 * math.cos(2 * math.pi * fase)             # baja la cabeza a mitad
    rumia = max(0.0, _onda(fase, 10)) * _ss((fase - 0.42) / 0.04) * (1 - _ss((fase - 0.56) / 0.04))
    # orejas: dos sacudidas rapidas y siguen la mirada
    sac = lambda c: math.exp(-((fase - c) / 0.012) ** 2)
    oL = (6 * mira + 25 * sac(0.3), 8 * sac(0.3))
    oR = (-6 * mira + 25 * sac(0.72), 8 * sac(0.72))
    peso = _onda(fase, 1, 0.25)                                 # cambia el peso de lado
    return dict(torso=(0.008 * peso, 0, 0.004 * resp), torso_rot=(0, 0.6 * peso, 0),
                hips=(0, 1.2 * peso, 0), chest=(0.8 * resp, 0, 0),
                neck=(-6 * baja, 0, 10 * mira), head=(-4 * baja, 4 * mira, 14 * mira),
                jaw=4 * rumia, orejas=(oL, oR))


# plegar_* = (caña, cuartilla): grados de pliegue a mitad del vuelo
WALK = dict(apoyo=0.64, paso=0.56, alto_d=0.12, alto_t=0.10, plegar_d=(70, 45), plegar_t=(25, 35),
            # secuencia lateral: TI, DI, TD, DD
            fases={"tra.L": 0.0, "del.L": 0.25, "tra.R": 0.5, "del.R": 0.75})
RUN = dict(apoyo=0.36, paso=0.95, alto_d=0.30, alto_t=0.24, plegar_d=(110, 60), plegar_t=(45, 50),
           # galope transverso: traseras juntas, delanteras juntas, con suspension
           fases={"tra.L": 0.0, "tra.R": 0.1, "del.L": 0.42, "del.R": 0.52})


def pose_walk(fase):
    c = WALK
    bob = math.cos(4 * math.pi * fase)                          # dos subidas por ciclo
    lado = _onda(fase, 1, 0.1)                                  # balanceo lateral
    return dict(torso=(0.012 * lado, 0, -0.012 + 0.012 * bob), torso_rot=(0.8 * bob, 1.0 * lado, 1.2 * lado),
                hips=(0, 3.0 * _onda(fase, 1), 2.5 * _onda(fase, 1)),
                chest=(0, -2.5 * _onda(fase, 1, 0.25), -2.0 * _onda(fase, 1, 0.25)),
                neck=(-4 + 3 * math.cos(4 * math.pi * fase + 0.6), 0, -2.5 * lado),
                head=(4 - 3 * math.cos(4 * math.pi * fase + 0.6), 0, 1.5 * lado),
                orejas=((6, 0), (6, 0)), patas=c)


def pose_run(fase):
    c = RUN
    # recogido (patas bajo el cuerpo) ~0.25 y ~0.75; suspension extendida tras las delanteras
    ola = _onda(fase, 1, 0.1)
    alto = 0.05 * math.cos(2 * math.pi * (fase - 0.75))         # en el aire despues de las delanteras
    # la pelvis se mete (grupa abajo) cuando las traseras pisan adelante, en fase 0
    return dict(torso=(0, 0, -0.03 + alto), torso_rot=(5 * ola, 0, 0),
                hips=(7 * _onda(fase, 1, 0.25), 0, 0), chest=(6 * _onda(fase, 1, 0.6), 0, 0),
                neck=(-14 - 8 * _onda(fase, 1, 0.55), 0, 0), head=(10 + 8 * _onda(fase, 1, 0.55), 0, 0),
                orejas=((35, 0), (35, 0)), patas=c)


ACCIONES = {  # nombre: (frames del ciclo, funcion)
    "Idle": (96, pose_idle),
    "Walk": (32, pose_walk),
    "Run": (14, pose_run),
}


def aplicar(nombre, fase):
    """Pone la pose del frame; devuelve lo que le falto a cada pata para alcanzar."""
    rig = _rig()
    reposo()
    for n in IKFK:
        rig.pose.bones[n]["IK_FK"] = 1.0
    p = ACCIONES[nombre][1](fase)
    patas = p.pop("patas", None)
    _cuerpo(rig, **p)
    if patas:
        # la escapula acompaña: adelanta el hombro cuando la pata va adelante
        for l in ("L", "R"):
            dy = pezuña(fase - patas["fases"]["del." + l], patas["apoyo"], patas["paso"], 0, True)[0]
            _girar(rig.pose.bones["shoulder." + l], (1, 0, 0), 25 * dy)
    bpy.context.view_layer.update()
    faltas = {}
    for n, (delantera, cadena) in PATAS.items():
        if patas:
            dy, dz, h, s = pezuña(fase - patas["fases"][n], patas["apoyo"], patas["paso"],
                                  patas["alto_d" if delantera else "alto_t"], delantera)
            plegar = patas["plegar_d" if delantera else "plegar_t"]
        else:
            dy = dz = h = 0.0; s = None; plegar = (0, 0)
        faltas[n] = pata_fk(rig, cadena, delantera, dy, dz, h, s, plegar)
    return faltas


def _curvas(act):
    """Todas las F-curves de una accion (Blender 5: viven en capas/strips/channelbags)."""
    return [fc for layer in act.layers for strip in layer.strips
            for cb in strip.channelbags for fc in cb.fcurves]


def crear(nombre):
    rig = _rig()
    n, _ = ACCIONES[nombre]
    act = bpy.data.actions.get(nombre) or bpy.data.actions.new(nombre)
    # solo las curvas de los CONTROLES; las keys que el usuario agregue en otros huesos quedan
    prefijos = tuple(f'pose.bones["{c}"]' for c in CONTROLES + IKFK)
    for layer in act.layers:
        for strip in layer.strips:
            for cb in strip.channelbags:
                for fc in [fc for fc in cb.fcurves if fc.data_path.startswith(prefijos)]:
                    cb.fcurves.remove(fc)
    act.use_fake_user = True
    rig.animation_data_create()
    rig.animation_data.action = act
    peor = 0.0
    for f in range(n + 1):
        peor = max([peor] + list(aplicar(nombre, f / n).values()))
        _key(rig, f + 1)
    for fc in _curvas(act):
        for k in fc.keyframe_points:
            k.interpolation = "LINEAR" if nombre == "Run" else "BEZIER"
    act.use_frame_range = True
    act.frame_range = (1, n + 1)
    act.use_cyclic = True
    print(nombre, "lo maximo que le falto a una pata para alcanzar:", round(peor, 3), "m")
    return peor


def crear_todas():
    for n in ACCIONES:
        crear(n)
    sc = bpy.context.scene
    sc.render.fps = FPS
    usar("Idle")


def usar(nombre):
    rig = _rig()
    rig.animation_data.action = bpy.data.actions[nombre]
    sc = bpy.context.scene
    sc.frame_start, sc.frame_end = 1, ACCIONES[nombre][0] + 1
    sc.frame_set(1)


# ---------------------------------------------------------------- revisar --
def foto(archivo, accion, frames, vista="lado", res=500):
    """Hoja de contactos Workbench (con textura) de `frames` de `accion`.
    Usa una escena temporal: la ventana de Blender a veces no redibuja."""
    import os, tempfile
    import numpy as np
    rig = _rig(); malla = bpy.data.objects["Plane"]
    usar(accion)
    esc = bpy.data.scenes.new("_foto")
    for o in (rig, malla):
        esc.collection.objects.link(o)
    cd = bpy.data.cameras.new("_c"); cd.type = "ORTHO"; cd.ortho_scale = 2.6
    cam = bpy.data.objects.new("_c", cd); esc.collection.objects.link(cam); esc.camera = cam
    if vista == "lado":
        cam.location = (-4, 0.4, 1.05); cam.rotation_euler = (math.pi / 2, 0, -math.pi / 2)
    elif vista == "frente":
        cam.location = (0, 4, 1.05); cam.rotation_euler = (math.pi / 2, 0, math.pi)
    else:  # tres cuartos
        cam.location = (-3, 3, 1.6); cam.rotation_euler = (math.radians(75), 0, math.radians(-135))
    esc.render.engine = "BLENDER_WORKBENCH"
    esc.display.shading.light = "STUDIO"; esc.display.shading.color_type = "TEXTURE"
    esc.render.resolution_x = esc.render.resolution_y = res
    esc.render.film_transparent = False
    rig.hide_render = True
    d = os.path.join(tempfile.gettempdir(), "bakes_huemul")
    tiras = []
    try:
        for f in frames:
            bpy.context.scene.frame_set(f)          # la escena temporal sola no evalua la accion
            esc.frame_set(f)
            esc.render.filepath = os.path.join(d, "_f.png")
            bpy.ops.render.render(write_still=True, scene=esc.name)
            im = bpy.data.images.load(esc.render.filepath, check_existing=False)
            a = np.array(im.pixels[:], np.float32).reshape(res, res, 4)[::-1]
            tiras.append(a); bpy.data.images.remove(im)
    finally:
        bpy.data.objects.remove(cam); bpy.data.cameras.remove(cd); bpy.data.scenes.remove(esc)
    hoja = np.concatenate(tiras, axis=1)
    img = bpy.data.images.new("_hoja", hoja.shape[1], hoja.shape[0])
    img.pixels = hoja[::-1].ravel(); img.filepath_raw = os.path.join(d, archivo + ".png"); img.file_format = "PNG"
    img.save(); bpy.data.images.remove(img)
    return os.path.join(d, archivo + ".png")


def estiramiento(nombre, huesos=("DEF-upper_arm", "DEF-forearm", "DEF-thigh", "DEF-lower_leg")):
    """Maximo estiramiento del IK (largo actual / reposo) en todos los frames de la accion."""
    rig = _rig(); usar(nombre); peor = (1.0, None, None)
    for f in range(1, ACCIONES[nombre][0] + 2):
        bpy.context.scene.frame_set(f)
        for pb in rig.pose.bones:
            if pb.name.startswith(huesos):
                r = (pb.tail - pb.head).length / pb.bone.length
                if r > peor[0]:
                    peor = (round(r, 3), pb.name, f)
    return peor
