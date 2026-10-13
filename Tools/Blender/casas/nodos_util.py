# Utilidades para construir árboles de nodos de shader de forma compacta.
import bpy
from math import pi

class B:
    def __init__(self, tree):
        self.t = tree
        self.nodes = tree.nodes
        self.links = tree.links

    # --- básicos ---
    def n(self, tipo, label=None, **props):
        nd = self.nodes.new(tipo)
        for k, v in props.items():
            setattr(nd, k, v)
        if label:
            nd.label = label
        return nd

    def L(self, a, b):
        self.links.new(a, b)

    def put(self, sock, v):
        if v is None:
            return
        if isinstance(v, bpy.types.NodeSocket):
            self.L(v, sock)
        else:
            sock.default_value = v

    # --- math ---
    def m(self, op, a, b=None, c=None, clamp=False, label=None):
        nd = self.n('ShaderNodeMath', label, operation=op, use_clamp=clamp)
        self.put(nd.inputs[0], a)
        if b is not None: self.put(nd.inputs[1], b)
        if c is not None: self.put(nd.inputs[2], c)
        return nd.outputs[0]

    def add(self, a, b): return self.m('ADD', a, b)
    def sub(self, a, b): return self.m('SUBTRACT', a, b)
    def mul(self, a, b): return self.m('MULTIPLY', a, b)
    def fract(self, a): return self.m('FRACT', a)
    def floor(self, a): return self.m('FLOOR', a)
    def absf(self, a): return self.m('ABSOLUTE', a)
    def clamp01(self, a): return self.m('MULTIPLY', a, 1.0, clamp=True)
    def madd(self, a, b, c): return self.m('MULTIPLY_ADD', a, b, c)
    def minf(self, a, b): return self.m('MINIMUM', a, b)
    def maxf(self, a, b): return self.m('MAXIMUM', a, b)
    def sin(self, a): return self.m('SINE', a)
    def pw(self, a, b): return self.m('POWER', a, b)

    def v(self, op, a, b=None, scale=None, label=None):
        nd = self.n('ShaderNodeVectorMath', label, operation=op)
        self.put(nd.inputs[0], a)
        if b is not None: self.put(nd.inputs[1], b)
        if scale is not None: self.put(nd.inputs['Scale'], scale)
        if op in ('DOT_PRODUCT', 'LENGTH', 'DISTANCE'):
            return nd.outputs['Value']
        return nd.outputs['Vector']

    def xyz(self, x=0.0, y=0.0, z=0.0):
        nd = self.n('ShaderNodeCombineXYZ')
        self.put(nd.inputs[0], x); self.put(nd.inputs[1], y); self.put(nd.inputs[2], z)
        return nd.outputs[0]

    def sep(self, vec):
        nd = self.n('ShaderNodeSeparateXYZ')
        self.put(nd.inputs[0], vec)
        return nd.outputs[0], nd.outputs[1], nd.outputs[2]

    def mr(self, val, a, b, c=0.0, d=1.0, interp='LINEAR', clamp=True):
        nd = self.n('ShaderNodeMapRange', interpolation_type=interp, clamp=clamp)
        self.put(nd.inputs['Value'], val)
        self.put(nd.inputs['From Min'], a); self.put(nd.inputs['From Max'], b)
        self.put(nd.inputs['To Min'], c); self.put(nd.inputs['To Max'], d)
        return nd.outputs['Result']

    def ss(self, val, a, b):
        return self.mr(val, a, b, 0.0, 1.0, 'SMOOTHSTEP')

    # --- texturas ---
    def noise(self, vec, scale=1.0, detail=4.0, rough=0.5, distortion=0.0, lac=2.0, label=None):
        nd = self.n('ShaderNodeTexNoise', label, noise_dimensions='3D')
        self.put(nd.inputs['Vector'], vec)
        self.put(nd.inputs['Scale'], scale); self.put(nd.inputs['Detail'], detail)
        self.put(nd.inputs['Roughness'], rough); self.put(nd.inputs['Distortion'], distortion)
        self.put(nd.inputs['Lacunarity'], lac)
        return nd.outputs['Fac']

    def white(self, vec=None, w=None, dims='3D'):
        nd = self.n('ShaderNodeTexWhiteNoise', noise_dimensions=dims)
        if dims == '1D':
            self.put(nd.inputs['W'], w)
        else:
            self.put(nd.inputs['Vector'], vec)
        return nd.outputs['Value']

    def voronoi(self, vec, scale=10.0, rand=1.0):
        nd = self.n('ShaderNodeTexVoronoi')
        self.put(nd.inputs['Vector'], vec)
        self.put(nd.inputs['Scale'], scale); self.put(nd.inputs['Randomness'], rand)
        return nd.outputs['Distance'], nd.outputs['Color']

    # --- color ---
    def mix(self, fac, a, b, blend='MIX', dtype='RGBA', clamp_res=False):
        nd = self.n('ShaderNodeMix', data_type=dtype, blend_type=blend, clamp_result=clamp_res)
        if dtype == 'RGBA':
            ia, ib, out = 6, 7, 2
        elif dtype == 'FLOAT':
            ia, ib, out = 2, 3, 0
        else:
            ia, ib, out = 4, 5, 1
        self.put(nd.inputs[0], fac)
        self.put(nd.inputs[ia], a)
        self.put(nd.inputs[ib], b)
        return nd.outputs[out]

    def lerp(self, fac, a, b):
        return self.mix(fac, a, b, dtype='FLOAT')

    def hsv(self, col, h=0.5, s=1.0, v=1.0):
        nd = self.n('ShaderNodeHueSaturation')
        self.put(nd.inputs['Hue'], h); self.put(nd.inputs['Saturation'], s)
        self.put(nd.inputs['Value'], v); self.put(nd.inputs['Color'], col)
        return nd.outputs['Color']


def col(r, g, b, a=1.0):
    return (r, g, b, a)


def auto_layout(tree, dx=230, dy=190):
    """Ordena los nodos en columnas según profundidad (de entradas a salida)."""
    nodes = list(tree.nodes)
    prev = {n: [] for n in nodes}
    for l in tree.links:
        prev[l.to_node].append(l.from_node)
    depth = {}
    def d(n, stack=()):
        if n in depth: return depth[n]
        if n in stack: return 0
        depth[n] = 0 if not prev[n] else 1 + max(d(p, stack + (n,)) for p in prev[n])
        return depth[n]
    for n in nodes: d(n)
    cols = {}
    for n in nodes:
        if n.type == 'FRAME': continue
        cols.setdefault(depth[n], []).append(n)
    for k, lst in cols.items():
        for i, n in enumerate(lst):
            n.location = (k * dx, -i * dy)


def nuevo_grupo(nombre, entradas, salidas):
    """entradas/salidas: lista de (nombre, tipo, default, min, max)."""
    g = bpy.data.node_groups.get(nombre)
    if g:
        g.nodes.clear()
        g.interface.clear()
    else:
        g = bpy.data.node_groups.new(nombre, 'ShaderNodeTree')
    for e in entradas:
        nombre_s, tipo = e[0], e[1]
        s = g.interface.new_socket(nombre_s, in_out='INPUT', socket_type=tipo)
        if len(e) > 2 and e[2] is not None:
            s.default_value = e[2]
        if len(e) > 3: s.min_value = e[3]
        if len(e) > 4: s.max_value = e[4]
    for s in salidas:
        g.interface.new_socket(s[0], in_out='OUTPUT', socket_type=s[1])
    gi = g.nodes.new('NodeGroupInput')
    go = g.nodes.new('NodeGroupOutput')
    return g, gi, go


def coords(b):
    """P local (alineado al mundo, sin la posición del objeto), N verdadera,
    T tangente horizontal sobre la cara, D dirección pendiente abajo,
    U coordenada horizontal sobre la cara, S coordenada pendiente abajo."""
    geo = b.n('ShaderNodeNewGeometry')
    oi = b.n('ShaderNodeObjectInfo')
    P = b.v('SUBTRACT', geo.outputs['Position'], oi.outputs['Location'])
    N = geo.outputs['True Normal']
    t = b.v('CROSS_PRODUCT', N, (0.0, 0.0, 1.0))
    t = b.v('ADD', t, (0.0004, 0.0003, 0.0))
    T = b.v('NORMALIZE', t)
    D = b.v('CROSS_PRODUCT', N, T)
    U = b.v('DOT_PRODUCT', P, T)
    S = b.v('DOT_PRODUCT', P, D)
    return dict(P=P, N=N, T=T, D=D, U=U, S=S, geo=geo, oi=oi)
