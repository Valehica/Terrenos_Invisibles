# Materiales de las casas de Norte (PackCasas.blend). Re-correr es seguro: los grupos
# y materiales se rehacen en el lugar con el mismo nombre. Todos llevan prefijo "N_".
import bpy
from nodos_util import B, col, auto_layout
import grupos_norte, grupos_catedral, grupos_casas

G = grupos_norte.crear_grupos()
G['barro'] = grupos_catedral.grupo_barro()
G['puerta'] = grupos_catedral.grupo_puerta()
G['pintura'] = grupos_catedral.grupo_pintura()
G['calamina'] = grupos_casas.grupo_calamina()


def hacer(nombre, g, color_vista, **valores):
    m = bpy.data.materials.get(nombre) or bpy.data.materials.new(nombre)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    b = B(nt)
    out = b.n('ShaderNodeOutputMaterial')
    bsdf = b.n('ShaderNodeBsdfPrincipled')
    b.L(bsdf.outputs[0], out.inputs['Surface'])
    nd = b.n('ShaderNodeGroup')
    nd.node_tree = G[g]
    for k, v in valores.items():
        nd.inputs[k.replace('_', ' ')].default_value = v
    b.L(nd.outputs['Base Color'], bsdf.inputs['Base Color'])
    b.L(nd.outputs['Roughness'], bsdf.inputs['Roughness'])
    if 'Metallic' in nd.outputs:
        b.L(nd.outputs['Metallic'], bsdf.inputs['Metallic'])
    bp = b.n('ShaderNodeBump')
    bp.inputs['Distance'].default_value = 1.0
    b.L(nd.outputs['Height'], bp.inputs['Height'])
    b.L(bp.outputs['Normal'], bsdf.inputs['Normal'])
    auto_layout(nt)
    m.diffuse_color = color_vista
    return m


M = {}
# ---- muros
M['piedra'] = hacer('N_Muro_PiedraCanteada', 'piedra_canteada', col(0.45, 0.44, 0.42),
                    Color_Piedra=col(0.21, 0.205, 0.19), Color_Mortero=col(0.26, 0.25, 0.23),
                    Variacion=0.7, Desgaste=0.75, Suciedad=0.7)
M['pirca'] = hacer('N_Muro_Pirca', 'pirca', col(0.3, 0.28, 0.25),
                   Color_Piedra=col(0.15, 0.14, 0.13), Color_Mortero=col(0.22, 0.18, 0.14),
                   Variacion=0.7, Desgaste=0.75, Suciedad=0.65)
M['ladrillo'] = hacer('N_Muro_Ladrillo', 'ladrillo', col(0.65, 0.3, 0.2),
                      Color_Ladrillo=col(0.28, 0.085, 0.04), Color_Mortero=col(0.33, 0.31, 0.27),
                      Variacion=0.65, Desgaste=0.7, Suciedad=0.6)
M['bloque'] = hacer('N_Muro_Bloque', 'bloque', col(0.62, 0.62, 0.6),
                    Color_Bloque=col(0.40, 0.39, 0.37), Variacion=0.5, Desgaste=0.7, Suciedad=0.6)
M['gris'] = hacer('N_Muro_RevoqueGris', 'revoque', col(0.6, 0.6, 0.58),
                  Color_Revoque=col(0.36, 0.355, 0.34), Desgaste=0.65, Suciedad=0.7,
                  Altura_Zocalo=0.35, Color_Bajo_Revoque=col(0.42, 0.14, 0.07))
M['beige'] = hacer('N_Muro_RevoqueBeige', 'revoque', col(0.75, 0.68, 0.55),
                   Color_Revoque=col(0.55, 0.43, 0.28), Desgaste=0.7, Suciedad=0.7,
                   Altura_Zocalo=0.5, Color_Bajo_Revoque=col(0.30, 0.23, 0.16))
M['barro'] = hacer('N_Muro_Barro', 'barro', col(0.55, 0.47, 0.38),
                   Color_Barro=col(0.34, 0.25, 0.16), Suciedad=0.7, Desgaste=0.8,
                   Altura_Zocalo=0.3)
# ---- puertas
M['madera'] = hacer('N_Puerta_Madera', 'puerta', col(0.35, 0.18, 0.1),
                    Color_Pintura=col(0.17, 0.07, 0.035), Desgaste=0.6, Suciedad=0.65,
                    Ancho_Tabla=0.12, Altura_Travesanos=0.0, Separacion_Travesanos=0.7)
M['gastada'] = hacer('N_Puerta_Gastada', 'puerta', col(0.4, 0.3, 0.22),
                     Color_Pintura=col(0.22, 0.12, 0.06), Desgaste=0.95, Suciedad=0.75,
                     Ancho_Tabla=0.10, Altura_Travesanos=0.0, Separacion_Travesanos=0.8)
M['azul'] = hacer('N_Puerta_MetalAzul', 'puerta', col(0.2, 0.45, 0.8),
                  Color_Pintura=col(0.03, 0.22, 0.55), Desgaste=0.4, Suciedad=0.6,
                  Ancho_Tabla=0.45, Altura_Travesanos=0.0, Separacion_Travesanos=0.3)
M['gris_metal'] = hacer('N_Puerta_MetalGris', 'puerta', col(0.35, 0.36, 0.37),
                        Color_Pintura=col(0.16, 0.17, 0.17), Desgaste=0.5, Suciedad=0.6,
                        Ancho_Tabla=0.45, Altura_Travesanos=0.0, Separacion_Travesanos=0.6)
# ---- marcos, vidrio, cemento
M['marco_madera'] = hacer('N_Marco_Madera', 'pintura', col(0.3, 0.17, 0.1),
                          Color=col(0.15, 0.075, 0.04), Fondo=col(0.28, 0.25, 0.21),
                          Desgaste=0.7, Suciedad=0.6, Rugosidad=0.75)
M['marco_blanco'] = hacer('N_Marco_Blanco', 'pintura', col(0.85, 0.85, 0.83),
                          Color=col(0.62, 0.62, 0.60), Fondo=col(0.28, 0.25, 0.21),
                          Desgaste=0.6, Suciedad=0.6, Rugosidad=0.65)
M['vidrio'] = hacer('N_Vidrio', 'vidrio', col(0.05, 0.06, 0.07), Polvo=0.75)
M['cemento'] = hacer('N_Cemento', 'revoque', col(0.6, 0.6, 0.58),
                     Color_Revoque=col(0.40, 0.39, 0.37), Desgaste=0.3, Suciedad=0.65,
                     Altura_Zocalo=-10.0, Color_Bajo_Revoque=col(0.32, 0.31, 0.29))
# ---- techos de calamina
M['galv'] = hacer('N_Calamina', 'calamina', col(0.7, 0.71, 0.72), Oxido=0.5, Pintado=0.0)
M['vieja'] = hacer('N_Calamina_Vieja', 'calamina', col(0.5, 0.45, 0.4), Oxido=0.9, Pintado=0.0)
M['azul_techo'] = hacer('N_Calamina_Azul', 'calamina', col(0.2, 0.35, 0.75), Oxido=0.45, Pintado=1.0,
                        Color_Pintura=col(0.035, 0.11, 0.45))
