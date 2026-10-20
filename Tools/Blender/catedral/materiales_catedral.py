# Crea (o rehace en el lugar) los materiales de la iglesia de Isluga, la torre,
# las barandas, los arcos de entrada y la casucha. "Muro" y "Techo" son los
# materiales que ya tenía la iglesia: se les reemplazan los nodos, no el nombre.
import bpy
from nodos_util import B, col, auto_layout
import grupos_catedral
import grupos_casas

G = grupos_catedral.crear_grupos()
G['calamina'] = grupos_casas.grupo_calamina()


def material(nombre):
    m = bpy.data.materials.get(nombre) or bpy.data.materials.new(nombre)
    m.use_nodes = True
    m.node_tree.nodes.clear()
    b = B(m.node_tree)
    out = b.n('ShaderNodeOutputMaterial')
    bsdf = b.n('ShaderNodeBsdfPrincipled')
    b.L(bsdf.outputs[0], out.inputs['Surface'])
    return m, b, bsdf


def grupo(b, g, **valores):
    nd = b.n('ShaderNodeGroup')
    nd.node_tree = g
    for k, v in valores.items():
        nd.inputs[k.replace('_', ' ')].default_value = v
    return nd


def conectar(b, nd, bsdf):
    b.L(nd.outputs['Base Color'], bsdf.inputs['Base Color'])
    b.L(nd.outputs['Roughness'], bsdf.inputs['Roughness'])
    if 'Metallic' in nd.outputs:
        b.L(nd.outputs['Metallic'], bsdf.inputs['Metallic'])
    bp = b.n('ShaderNodeBump')
    bp.inputs['Strength'].default_value = 1.0
    bp.inputs['Distance'].default_value = 1.0
    b.L(nd.outputs['Height'], bp.inputs['Height'])
    b.L(bp.outputs['Normal'], bsdf.inputs['Normal'])


def hacer(nombre, g, color_vista, **valores):
    m, b, bsdf = material(nombre)
    nd = grupo(b, G[g], **valores)
    conectar(b, nd, bsdf)
    auto_layout(m.node_tree)
    m.diffuse_color = color_vista      # color en el viewport sólido
    return m


CAL = col(0.70, 0.69, 0.64)
M = {}
# iglesia: cal gastada con zócalo de pirca bajo
M['muro'] = hacer('Muro', 'cal', col(0.85, 0.84, 0.8),
                  Color_Cal=CAL, Desgaste=0.4, Suciedad=0.6,
                  Altura_Zocalo=0.18, Altura_Salpicado=0.8)
M['techo'] = hacer('Techo', 'teja', col(0.55, 0.22, 0.12),
                   Color_Teja=col(0.31, 0.115, 0.05), Variacion=0.6, Liquen=0.45,
                   Suciedad=0.5)
# torre: cal, chapitel de revoque crema, cornisas color ladrillo
M['torre'] = hacer('Torre_Cal', 'cal', col(0.9, 0.9, 0.86),
                   Color_Cal=CAL, Desgaste=0.3, Suciedad=0.65,
                   Altura_Zocalo=0.14, Altura_Salpicado=0.9)
M['chapitel'] = hacer('Torre_Chapitel', 'cal', col(0.78, 0.7, 0.58),
                      Color_Cal=col(0.55, 0.47, 0.36), Desgaste=0.25, Suciedad=0.45,
                      Altura_Zocalo=-10.0, Altura_Salpicado=0.0)
M['cornisa'] = hacer('Cornisa', 'pintura', col(0.5, 0.25, 0.18),
                     Color=col(0.30, 0.10, 0.06), Fondo=col(0.55, 0.53, 0.49),
                     Desgaste=0.35, Suciedad=0.5, Rugosidad=0.8)
M['ceramica'] = hacer('Ceramica', 'pintura', col(0.55, 0.28, 0.18),
                      Color=col(0.33, 0.12, 0.06), Fondo=col(0.40, 0.30, 0.22),
                      Desgaste=0.25, Suciedad=0.4, Rugosidad=0.55)
M['hierro'] = hacer('Hierro', 'pintura', col(0.15, 0.15, 0.15),
                    Color=col(0.045, 0.04, 0.038), Fondo=col(0.09, 0.04, 0.02),
                    Desgaste=0.3, Suciedad=0.2, Rugosidad=0.5, Metalico=0.6)
# barandas de arquitos y arcos: más gastadas, zócalo de piedra hasta el plinto
M['baranda'] = hacer('Baranda_Cal', 'cal', col(0.86, 0.85, 0.8),
                     Color_Cal=col(0.66, 0.65, 0.60), Desgaste=0.45, Suciedad=0.75,
                     Altura_Zocalo=0.1, Altura_Salpicado=0.35)
M['roja'] = hacer('Pintura_Roja', 'pintura', col(0.6, 0.08, 0.06),
                  Color=col(0.42, 0.035, 0.025), Fondo=CAL,
                  Desgaste=0.3, Suciedad=0.35, Rugosidad=0.55)
# casucha: revoque de barro y calamina sin pintar
M['barro'] = hacer('Barro_Casucha', 'barro', col(0.45, 0.4, 0.34),
                   Color_Barro=col(0.25, 0.19, 0.14), Suciedad=0.5, Desgaste=0.4,
                   Altura_Zocalo=0.3)
M['calamina'] = hacer('Calamina_Casucha', 'calamina', col(0.75, 0.75, 0.76),
                      Oxido=0.2, Pintado=0.0)
# puerta de la iglesia: tablas pintadas de azul (las caras las asignó el usuario
# a mano al slot "Puerta" de Cube)
M['puerta'] = hacer('Puerta', 'puerta', col(0.15, 0.35, 0.65),
                    Color_Pintura=col(0.06, 0.16, 0.30), Desgaste=0.5, Suciedad=0.55,
                    Ancho_Tabla=0.11, Altura_Travesanos=0.0, Separacion_Travesanos=0.5)

