# Asigna los materiales por cara según la geometría. No toca la iglesia (Cube),
# que ya tiene sus slots Techo y Muro. Las alturas son las del modelo de
# Catedral2.blend (suelo en z = 0); si se remodela la torre, revisar los cortes.
import bpy
from mathutils import Vector

MAT = bpy.data.materials

TORRE = 'Cube.001'
REMATES = 'Cube.002'
CASUCHA = 'Cube.018'
ARCOS = ['Cube.004', 'Cube.016']          # Cube.007 comparte malla con Cube.004
BARANDAS = ['Cube.003', 'Cube.005', 'Cube.006', 'Cube.008', 'Cube.010', 'Cube.011',
            'Cube.012', 'Cube.013', 'Cube.014', 'Cube.015', 'Cube.017']


def slots(me, nombres):
    me.materials.clear()
    for n in nombres:
        me.materials.append(MAT[n])


def caras_mundo(o):
    mw = o.matrix_world
    rot = mw.to_3x3()
    for p in o.data.polygons:
        zs = [(mw @ o.data.vertices[v].co).z for v in p.vertices]
        yield p, min(zs), max(zs), (mw @ p.center).z, (rot @ p.normal).normalized()


# ---- torre: 0 cal, 1 cornisa, 2 chapitel, 3 cerámica (bola), 4 hierro (cruz)
o = bpy.data.objects[TORRE]
slots(o.data, ['Torre_Cal', 'Cornisa', 'Torre_Chapitel', 'Ceramica', 'Hierro'])
for p, z0, z1, zc, n in caras_mundo(o):
    i = 0
    if z0 >= 4.775:
        i = 4
    elif z0 >= 4.565:
        i = 3
    elif z0 >= 3.565 and n.z > 0.2:
        i = 2
    elif (z0 >= 1.99 and z1 <= 2.29) or (z0 >= 3.40 and z1 <= 3.58):
        i = 1
    p.material_index = i

# ---- remates de las esquinas de la torre: cerámica
slots(bpy.data.objects[REMATES].data, ['Ceramica'])

# ---- barandas (la malla base; el Array por nodos conserva el material)
for n in BARANDAS:
    slots(bpy.data.objects[n].data, ['Baranda_Cal'])

# ---- arcos de entrada: 0 cal, 1 pintura roja en plinto, imposta, intradós y remate
for n in ARCOS:
    me = bpy.data.objects[n].data
    slots(me, ['Baranda_Cal', 'Pintura_Roja'])
    ys = [v.co.y for v in me.vertices]
    xs = [v.co.x for v in me.vertices]
    yc = (min(ys) + max(ys)) / 2
    eje = Vector(((min(xs) + max(xs)) / 2, yc, 0.55))
    for p in me.polygons:
        c, nn = p.center, p.normal
        roja = c.z < 0.1 or (0.36 < c.z < 0.41 and nn.z < -0.2) or c.z > 0.915
        if not roja and 0.5 < c.z < 0.915 and abs(nn.x) < 0.35:
            hacia = Vector((0.0, eje.y - c.y, eje.z - c.z))
            if hacia.length > 1e-4 and nn.dot(hacia.normalized()) > 0.6:
                roja = True
        p.material_index = 1 if roja else 0

# ---- casucha: 0 barro, 1 calamina, 2 cantos del techo (rojo ladrillo apagado)
o = bpy.data.objects[CASUCHA]
slots(o.data, ['Barro_Casucha', 'Calamina_Casucha', 'Cornisa'])
for p, z0, z1, zc, n in caras_mundo(o):
    i = 0
    if zc > 1.3 and abs(n.z) > 0.8:
        i = 1
    elif zc > 1.3 and p.area < 0.8:
        i = 2
    p.material_index = i

for o in bpy.data.objects:
    if o.type == 'MESH':
        o.data.update()
