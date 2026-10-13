# Crea los materiales de las casas, separa las caras del cuerpo por material,
# calcula el atributo de veta por cara y aplica shade smooth por ángulo.
import bpy, bmesh, math
from mathutils import Vector
from collections import defaultdict
from nodos_util import B, col, auto_layout, coords
import grupos_casas

G = grupos_casas.crear_grupos()


def material(nombre):
    m = bpy.data.materials.get(nombre) or bpy.data.materials.new(nombre)
    m.use_nodes = True
    m.node_tree.nodes.clear()
    b = B(m.node_tree)
    out = b.n('ShaderNodeOutputMaterial')
    bsdf = b.n('ShaderNodeBsdfPrincipled')
    b.L(bsdf.outputs[0], out.inputs['Surface'])
    return m, b, bsdf


def grupo(b, g):
    nd = b.n('ShaderNodeGroup')
    nd.node_tree = g
    return nd


def bump(b, bsdf, h, fuerza=1.0):
    bp = b.n('ShaderNodeBump')
    bp.inputs['Strength'].default_value = fuerza
    bp.inputs['Distance'].default_value = 1.0
    b.put(bp.inputs['Height'], h)
    b.L(bp.outputs['Normal'], bsdf.inputs['Normal'])


def attr_obj(b, nombre):
    return b.n('ShaderNodeAttribute', attribute_type='OBJECT', attribute_name=nombre)


def attr_veta(b):
    return b.n('ShaderNodeAttribute', attribute_type='GEOMETRY', attribute_name='veta').outputs['Vector']


def conectar(b, nd, bsdf, metal=True):
    b.L(nd.outputs['Base Color'], bsdf.inputs['Base Color'])
    b.L(nd.outputs['Roughness'], bsdf.inputs['Roughness'])
    if metal and 'Metallic' in nd.outputs:
        b.L(nd.outputs['Metallic'], bsdf.inputs['Metallic'])
    bump(b, bsdf, nd.outputs['Height'])


M = {}

# --- pared: tinglado pintado, color y desgaste desde propiedades del objeto
m, b, bsdf = material('M_Casa_Pared')
nd = grupo(b, G['tinglado'])
b.L(attr_obj(b, 'color_pintura').outputs['Color'], nd.inputs['Color Pintura'])
b.L(attr_obj(b, 'desgaste_pintura').outputs['Fac'], nd.inputs['Desgaste'])
b.L(attr_obj(b, 'suciedad').outputs['Fac'], nd.inputs['Suciedad'])
conectar(b, nd, bsdf)
auto_layout(m.node_tree); M['pared'] = m

# --- marco de puerta: madera barnizada algo gastada
m, b, bsdf = material('M_Casa_MarcoPuerta')
nd = grupo(b, G['madera'])
b.L(attr_veta(b), nd.inputs['Veta'])
nd.inputs['Color Claro'].default_value = col(0.40, 0.24, 0.12)
nd.inputs['Color Oscuro'].default_value = col(0.17, 0.085, 0.035)
nd.inputs['Gris'].default_value = 0.5
nd.inputs['Rugosidad'].default_value = 0.55
nd.inputs['Semilla'].default_value = 3.0
nd.inputs['Suciedad'].default_value = 0.75
conectar(b, nd, bsdf)
auto_layout(m.node_tree); M['marco_puerta'] = m

# --- hoja de puerta: tablas verticales machihembradas
m, b, bsdf = material('M_Casa_Puerta')
nd = grupo(b, G['madera'])
nd.inputs['Veta'].default_value = (0.0, 0.0, 1.0)
nd.inputs['Color Claro'].default_value = col(0.33, 0.19, 0.09)
nd.inputs['Color Oscuro'].default_value = col(0.13, 0.06, 0.025)
nd.inputs['Gris'].default_value = 0.45
nd.inputs['Rugosidad'].default_value = 0.55
nd.inputs['Semilla'].default_value = 7.0
nd.inputs['Suciedad'].default_value = 0.8
c = coords(b)
tab = b.m('DIVIDE', c['U'], 0.115)
ft = b.fract(tab)
dt = b.mul(b.minf(ft, b.sub(1.0, ft)), 0.115)
ranura = b.sub(1.0, b.ss(dt, 0.0012, 0.0035))
rt = b.white(w=b.floor(tab), dims='1D')
colp = b.hsv(nd.outputs['Base Color'], 0.5, 1.0, b.madd(rt, 0.25, 0.88))
colp = b.mix(b.mul(ranura, 0.7), colp, col(0.05, 0.03, 0.02), blend='MULTIPLY')
b.L(colp, bsdf.inputs['Base Color'])
b.L(nd.outputs['Roughness'], bsdf.inputs['Roughness'])
bump(b, bsdf, b.madd(ranura, -0.004, nd.outputs['Height']))
auto_layout(m.node_tree); M['hoja'] = m

# --- marco de ventana: aluminio anodizado natural, limpio
m, b, bsdf = material('M_Casa_MarcoVentana')
nd = grupo(b, G['aluminio'])
b.L(attr_veta(b), nd.inputs['Veta'])
nd.inputs['Desgaste'].default_value = 0.4
nd.inputs['Color'].default_value = col(0.72, 0.73, 0.74)
nd.inputs['Rugosidad'].default_value = 0.38
nd.inputs['Hollin'].default_value = 0.0
conectar(b, nd, bsdf)
auto_layout(m.node_tree); M['marco_ventana'] = m

# --- vidrio: opaco oscuro y reflectante (no deja ver el interior vacío)
m, b, bsdf = material('M_Casa_Vidrio')
c = coords(b)
manchas = b.noise(b.v('MULTIPLY', c['P'], (3.0, 3.0, 3.0)), 1.0, 6.0, 0.6)
polvo = b.ss(manchas, 0.55, 0.8)
fondo = b.noise(b.v('MULTIPLY', c['P'], (0.7, 0.7, 0.7)), 1.0, 3.0, 0.5)
base = b.mix(fondo, col(0.010, 0.013, 0.016), col(0.035, 0.038, 0.036))
base = b.mix(b.mul(polvo, 0.12), base, col(0.22, 0.21, 0.19))
b.L(base, bsdf.inputs['Base Color'])
b.L(b.lerp(polvo, 0.03, 0.16), bsdf.inputs['Roughness'])
bsdf.inputs['IOR'].default_value = 1.52
bsdf.inputs['Specular IOR Level'].default_value = 0.6
auto_layout(m.node_tree); M['vidrio'] = m

# --- latón (calamina de zinc) para techos y alero
m, b, bsdf = material('M_Casa_Laton')
nd = grupo(b, G['calamina'])
b.L(attr_obj(b, 'oxido_techo').outputs['Fac'], nd.inputs['Oxido'])
b.L(attr_obj(b, 'color_techo').outputs['Color'], nd.inputs['Color Pintura'])
b.L(attr_obj(b, 'techo_pintado').outputs['Fac'], nd.inputs['Pintado'])
conectar(b, nd, bsdf)
auto_layout(m.node_tree); M['laton'] = m

# --- aluminio gastado: chimenea y tubo
m, b, bsdf = material('M_Casa_AluminioGastado')
nd = grupo(b, G['aluminio'])
b.L(attr_veta(b), nd.inputs['Veta'])
nd.inputs['Color'].default_value = col(0.50, 0.50, 0.49)
nd.inputs['Desgaste'].default_value = 0.85
nd.inputs['Hollin'].default_value = 1.0
nd.inputs['Rugosidad'].default_value = 0.48
conectar(b, nd, bsdf)
auto_layout(m.node_tree); M['aluminio_gastado'] = m

# --- madera oscura: escalera
m, b, bsdf = material('M_Casa_MaderaOscura')
nd = grupo(b, G['madera'])
b.L(attr_veta(b), nd.inputs['Veta'])
nd.inputs['Color Claro'].default_value = col(0.20, 0.11, 0.05)
nd.inputs['Color Oscuro'].default_value = col(0.06, 0.03, 0.014)
nd.inputs['Gris'].default_value = 0.4
nd.inputs['Rugosidad'].default_value = 0.65
nd.inputs['Semilla'].default_value = 11.0
nd.inputs['Suciedad'].default_value = 0.8
conectar(b, nd, bsdf)
auto_layout(m.node_tree); M['madera_oscura'] = m

# --- pilares: madera a la intemperie
m, b, bsdf = material('M_Casa_Pilares')
nd = grupo(b, G['madera'])
b.L(attr_veta(b), nd.inputs['Veta'])
nd.inputs['Color Claro'].default_value = col(0.30, 0.19, 0.10)
nd.inputs['Color Oscuro'].default_value = col(0.13, 0.08, 0.04)
nd.inputs['Gris'].default_value = 0.55
nd.inputs['Rugosidad'].default_value = 0.7
nd.inputs['Semilla'].default_value = 5.0
nd.inputs['Suciedad'].default_value = 0.85
conectar(b, nd, bsdf)
auto_layout(m.node_tree); M['pilares'] = m
