# Ajusta el metarig de ave de Rigify al gaviotin (Gabiortin9.blend, Pajaro_LowPoly).
#
# Se corre dentro de Blender:
#   exec(open(r".../Tools/Blender/gaviotin_metarig.py").read()); ajustar()
#
# Medidas sacadas del high poly (cabeza hacia -Y, alas en X, suelo en z=0):
# - ala extendida y barrida hacia adelante: el borde de ataque llega a y=-0.85
#   en x~1.7 (muneca) y la punta esta en x=3.84
# - tarso vertical en x=0.178, y~0.25, de z=0.07 a 0.53; vientre en z~0.56
# - tres dedos hacia adelante en una paleta palmeada plana, puntas en x 0.095 / 0.177 / 0.255, y~0.02
# - cola horquillada: centro hasta y~1.6 (fino), puntas en (0.42, 2.1, 1.23)
# - ojo en (0.257, -0.627, 1.665) sobre la superficie; pico hasta y=-1.425
#
# Solo se dan los huesos del centro y los .L: los .R se espejan en X.
# El roll se recalcula para conservar el eje Z que traia cada hueso del metarig.

import bpy
from mathutils import Vector

METARIG = "metarig"

# nombre: (head, tail)
HUESOS = {
    # columna: spine.003 es la raiz (cadera), hacia adelante spine.004..006
    "spine.003": ((0, 0.50, 1.17), (0, 0.15, 1.22)),
    "spine.004": ((0, 0.15, 1.22), (0, -0.15, 1.30)),
    "spine.005": ((0, -0.15, 1.30), (0, -0.36, 1.41)),
    "spine.006": ((0, -0.36, 1.41), (0, -0.44, 1.48)),
    "neck.001": ((0, -0.44, 1.48), (0, -0.47, 1.52)),
    "neck.002": ((0, -0.47, 1.52), (0, -0.50, 1.56)),
    "head": ((0, -0.50, 1.56), (0, -0.84, 1.70)),
    # cola, hacia atras desde la cadera. Desde y~1.35 la cola tiene dos capas: la de
    # abajo es el centro (termina en y~1.6, z~1.0) y la de arriba sigue en la horquilla.
    "spine.002": ((0, 0.50, 1.17), (0, 0.95, 1.10)),
    "spine.001": ((0, 0.95, 1.10), (0, 1.35, 1.02)),
    "spine": ((0, 1.35, 1.02), (0, 1.56, 1.005)),
    "t_feather.L": ((0.13, 1.38, 1.12), (0.41, 2.08, 1.232)),
    # pico: pivotes en la comisura (la boca del low poly empieza en y=-0.886)
    "beak.001.T": ((0, -0.86, 1.675), (0, -1.15, 1.640)),
    "beak.002.T": ((0, -1.15, 1.640), (0, -1.42, 1.596)),
    "beak_001.B": ((0, -0.80, 1.585), (0, -1.12, 1.586)),
    "beak.002.B": ((0, -1.12, 1.586), (0, -1.40, 1.586)),
    "tongue.001.L": ((0, -0.92, 1.600), (0, -0.98, 1.600)),
    "tongue.002.L": ((0, -0.98, 1.600), (0, -1.04, 1.598)),
    "tongue.003.L": ((0, -1.04, 1.598), (0, -1.09, 1.596)),
    "eye.L": ((0.20, -0.625, 1.665), (0.29, -0.645, 1.665)),
    "skull.006.L": ((0.04, -0.80, 1.62), (0.10, -0.86, 1.61)),
    "n_feather.001.L": ((0.07, -0.50, 1.80), (0.09, -0.28, 1.75)),
    # ala
    "shoulder.L": ((0.06, -0.20, 1.30), (0.42, -0.24, 1.34)),
    "Wing.L": ((0.46, -0.26, 1.34), (1.05, -0.53, 1.30)),
    "Wing.001.L": ((1.05, -0.53, 1.30), (1.75, -0.76, 1.23)),
    "Wing.002.L": ((1.75, -0.76, 1.23), (3.20, -0.33, 1.41)),
    "w_feather.004.L": ((0.70, -0.35, 1.34), (0.80, 0.33, 1.37)),   # terciarias
    "w_feather.003.L": ((1.30, -0.60, 1.27), (1.50, -0.06, 1.32)),  # secundarias (el borde de fuga va mas alto)
    "w_feather.002.L": ((2.00, -0.68, 1.28), (2.35, -0.13, 1.40)),  # primarias internas
    "w_feather.001.L": ((2.85, -0.42, 1.39), (3.80, -0.27, 1.40)),  # punta
    # pata
    "pelvis.L": ((0, 0.15, 1.22), (0.19, 0.14, 0.90)),
    "thigh.L": ((0.175, 0.14, 0.86), (0.178, 0.28, 0.53)),
    "shin.L": ((0.178, 0.28, 0.53), (0.177, 0.24, 0.075)),
    "foot.L": ((0.177, 0.24, 0.075), (0.177, 0.215, 0.035)),
    "toe.L": ((0.177, 0.215, 0.035), (0.177, 0.02, 0.004)),
    "toes_parent.L": ((0.177, 0.215, 0.035), (0.177, 0.17, 0.03)),
    "t_thumb.001.L": ((0.177, 0.235, 0.05), (0.177, 0.245, 0.045)),  # no hay hallux modelado: queda en el talon
    "t_thumb.002.L": ((0.177, 0.245, 0.045), (0.177, 0.25, 0.04)),
}


def _dedo(nombre, base, punta):
    """Tres falanges entre la base y la punta (x, y). El pie del low poly es una paleta
    palmeada y plana, asi que la altura sigue el centro de su grosor."""
    alturas = (0.035, 0.016, 0.006, 0.003)
    pts = []
    for t, z in zip((0.0, 0.36, 0.68, 1.0), alturas):
        x = base[0] + (punta[0] - base[0]) * t
        y = base[1] + (punta[1] - base[1]) * t
        pts.append((x, y, z))
    for i in range(3):
        HUESOS[f"{nombre}.00{i + 1}.L"] = (pts[i], pts[i + 1])


_dedo("t_middle", (0.177, 0.20), (0.177, 0.018))
_dedo("t_ring", (0.195, 0.205), (0.255, 0.020))   # externo
_dedo("t_index", (0.158, 0.205), (0.095, 0.022))  # interno


# Controles de shape keys (sin deformacion). Suben por su eje Y local:
# 0.1 m arriba = shape key en 1 (ver gaviotin_shapekeys.py).
CONTROLES_SK = {
    # nombre: (head, tail, padre)
    "panza_ctrl": ((0, 0.20, 0.42), (0, 0.20, 0.52), "spine.003"),
    "alas_ctrl": ((0, 0.45, 1.62), (0, 0.45, 1.72), "spine.005"),
}


def agregar_controles_sk():
    """Agrega los huesos de control de shape keys al metarig (basic.super_copy sin DEF),
    en la coleccion "Correctivos". Si ya estan, solo los recoloca."""
    ob = bpy.data.objects[METARIG]
    arm = ob.data
    col = arm.collections_all.get("Correctivos") or arm.collections.new("Correctivos")
    col.rigify_ui_row = 16
    col.rigify_color_set_name = "Special"
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    for n, (h, t, padre) in CONTROLES_SK.items():
        eb = arm.edit_bones.get(n) or arm.edit_bones.new(n)
        eb.head, eb.tail = h, t
        eb.roll = 0
        eb.parent = arm.edit_bones[padre]
        eb.use_connect = False
        eb.use_deform = False
        col.assign(eb)
    bpy.ops.object.mode_set(mode="OBJECT")
    for n in CONTROLES_SK:
        pb = ob.pose.bones[n]
        pb.rigify_type = "basic.super_copy"
        p = pb.rigify_parameters
        p.make_control = True
        p.make_deform = False
        p.make_widget = True
        p.super_copy_widget_type = "diamond"
    return list(CONTROLES_SK)


def _espejo(n):
    if n.endswith(".L"):
        return n[:-2] + ".R"
    return None


def ajustar():
    ob = bpy.data.objects[METARIG]
    if bpy.context.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    ob.hide_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob

    # eje Z original de cada hueso (para conservar la intencion del roll)
    ejes_z = {b.name: b.matrix_local.to_3x3().col[2].normalized() for b in ob.data.bones}

    if any(abs(s - 1.0) > 1e-6 for s in ob.scale):
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    pos = dict(HUESOS)
    for n, (h, t) in HUESOS.items():
        r = _espejo(n)
        if r:
            pos[r] = ((-h[0], h[1], h[2]), (-t[0], t[1], t[2]))

    bpy.ops.object.mode_set(mode="EDIT")
    eb = ob.data.edit_bones
    faltan = [b.name for b in eb if b.name not in pos]
    sobran = [n for n in pos if n not in eb]
    # primero desconectar para que mover un head no arrastre al padre
    conectados = {b.name: b.use_connect for b in eb}
    for b in eb:
        b.use_connect = False
    for n, (h, t) in pos.items():
        if n in eb:
            eb[n].head = h
            eb[n].tail = t
    for b in eb:
        b.use_connect = conectados[b.name]
        if b.name in ejes_z:
            b.align_roll(ejes_z[b.name])
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.ed.undo_push(message="Ajustar metarig gaviotin")
    return {"faltan": faltan, "sobran": sobran, "huesos": len(pos)}
