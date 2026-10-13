# Asigna materiales a las casas: separa caras del cuerpo, calcula veta por cara,
# shade smooth por ángulo y propiedades de color por casa.
import bpy, bmesh, math
from mathutils import Vector
from collections import defaultdict

ORDEN_CUERPO = ['pared', 'marco_puerta', 'hoja', 'marco_ventana', 'vidrio', 'laton']
MAT = {
    'pared': 'M_Casa_Pared', 'marco_puerta': 'M_Casa_MarcoPuerta', 'hoja': 'M_Casa_Puerta',
    'marco_ventana': 'M_Casa_MarcoVentana', 'vidrio': 'M_Casa_Vidrio', 'laton': 'M_Casa_Laton',
}


def clasificar(bm):
    groups = defaultdict(float)
    for f in bm.faces:
        n = f.normal
        for ax in range(2):
            for s in (1, -1):
                if n[ax] * s > 0.999:
                    groups[(ax, s, round(f.calc_center_median()[ax], 3))] += f.calc_area()
    planes = {}
    for (ax, s, off), a in groups.items():
        k = (ax, s)
        if k not in planes or a > planes[k][1]:
            planes[k] = (off, a)
    lo = [planes[(0, -1)][0], planes[(1, -1)][0]]
    hi = [planes[(0, 1)][0], planes[(1, 1)][0]]
    techo_z = max(v.co.z for v in bm.verts)

    def dentro(c, t=0.02):
        return lo[0] - t <= c.x <= hi[0] + t and lo[1] - t <= c.y <= hi[1] + t

    cat = {}
    for f in bm.faces:
        n = f.normal; c = f.calc_center_median()
        pared = any(n[ax] * s > 0.999 and abs(c[ax] - off) < 0.004 for (ax, s), (off, a) in planes.items())
        if n.z < -0.999 and abs(c.z) < 0.004: pared = True
        if 0.3 < abs(n.z) < 0.95 and dentro(c) and c.z > 2.0: pared = True
        if pared: cat[f.index] = 'pared'

    vistos = set()
    aberturas = []
    for f in bm.faces:
        if f.index in cat or f.index in vistos: continue
        pila = [f]; isla = []; vistos.add(f.index)
        while pila:
            g = pila.pop(); isla.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in cat and h.index not in vistos:
                        vistos.add(h.index); pila.append(h)
        vs = [v.co for q in isla for v in q.verts]
        mn = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
        mx = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
        fuera = max(lo[0] - mn.x, mx.x - hi[0], lo[1] - mn.y, mx.y - hi[1])
        alto = mx.z - mn.z
        ancho = max(mx.x - mn.x, mx.y - mn.y)
        if fuera > 0.3: tipo = 'alero'
        elif mx.z > techo_z - 0.05: tipo = 'frontón'
        elif alto > 1.2 * ancho and alto > 1.2: tipo = 'puerta'
        else: tipo = 'ventana'
        for q in isla:
            n = q.normal; c = q.calc_center_median()
            hundida = any(n[ax] * s > 0.999 and (c[ax] - off) * s < -0.02 and q.calc_area() > 0.03
                          for (ax, s), (off, a) in planes.items())
            cat[q.index] = {
                'alero': 'laton', 'frontón': 'pared',
                'puerta': 'hoja' if hundida else 'marco_puerta',
                'ventana': 'vidrio' if hundida else 'marco_ventana',
            }[tipo]
        if tipo in ('puerta', 'ventana'):
            aberturas.append((tipo, mn, mx))

    # hoja/vidrio coplanar con la pared: cara del plano de pared encerrada por un marco
    for f in bm.faces:
        if cat.get(f.index) != 'pared' or abs(f.normal.z) > 0.01: continue
        c = f.calc_center_median()
        for tipo, mn, mx in aberturas:
            if all(mn[i] - 0.001 <= c[i] <= mx[i] + 0.001 for i in range(3)):
                vs = [v.co for v in f.verts]
                if all(mn[i] - 0.001 <= v[i] <= mx[i] + 0.001 for v in vs for i in range(3)):
                    cat[f.index] = 'hoja' if tipo == 'puerta' else 'vidrio'
                    break
    return cat


def poner_slots(ob, nombres):
    ob.data.materials.clear()
    for nm in nombres:
        ob.data.materials.append(bpy.data.materials[nm])


def veta_por_cara(ob):
    """Dirección de la arista más larga de cada cara (en espacio mundo, sin
    traslación): la veta de la madera / el cepillado sigue el largo de la pieza."""
    me = ob.data
    mw = ob.matrix_world.to_3x3()
    a = me.attributes.get('veta')
    if a and (a.domain != 'FACE' or a.data_type != 'FLOAT_VECTOR'):
        me.attributes.remove(a); a = None
    if not a:
        a = me.attributes.new('veta', 'FLOAT_VECTOR', 'FACE')
    co = [mw @ v.co for v in me.vertices]
    for p in me.polygons:
        vi = list(p.vertices)
        mejor = None; lmax = -1
        for i in range(len(vi)):
            d = co[vi[(i + 1) % len(vi)]] - co[vi[i]]
            if d.length > lmax:
                lmax = d.length; mejor = d
        d = mejor.normalized() if lmax > 1e-6 else Vector((0, 0, 1))
        k = max(range(3), key=lambda i: abs(d[i]))
        if d[k] < 0: d = -d
        a.data[p.index].vector = d


def suavizar(ob):
    for o in bpy.context.selected_objects: o.select_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(30), keep_sharp_edges=True)


COLORES = {   # color de pintura por casa (lineal)
    'Cuerpo':     (0.42, 0.055, 0.035, 1.0),   # rojo colonial
    'Cuerpo.001': (0.30, 0.42, 0.45, 1.0),   # celeste grisáceo
    'Cuerpo.002': (0.55, 0.40, 0.10, 1.0),   # amarillo ocre
    'Cuerpo.003': (0.13, 0.25, 0.14, 1.0),   # verde bosque
}
DESGASTE = {'Cuerpo': 0.65, 'Cuerpo.001': 0.58, 'Cuerpo.002': 0.72, 'Cuerpo.003': 0.62}


def prop(ob, nombre, valor, color=False, desc=''):
    ob[nombre] = valor
    ui = ob.id_properties_ui(nombre)
    if color:
        ui.update(subtype='COLOR', min=0.0, max=1.0, description=desc)
    else:
        ui.update(min=0.0, max=1.0, soft_min=0.0, soft_max=1.0, description=desc)


informe = {}
for ob in bpy.data.objects:
    if ob.type != 'MESH' or ob.name.startswith('_'): continue
    base = ob.name.split('.')[0]
    if base == 'Cuerpo':
        bm = bmesh.new(); bm.from_mesh(ob.data); bm.faces.ensure_lookup_table()
        cat = clasificar(bm); bm.free()
        poner_slots(ob, [MAT[k] for k in ORDEN_CUERPO])
        for p in ob.data.polygons:
            p.material_index = ORDEN_CUERPO.index(cat.get(p.index, 'pared'))
        cnt = defaultdict(int)
        for v in cat.values(): cnt[v] += 1
        informe[ob.name] = dict(cnt)
        prop(ob, 'color_pintura', COLORES.get(ob.name, (0.5, 0.5, 0.5, 1.0)), True, 'Color de la pintura del tinglado')
        prop(ob, 'desgaste_pintura', DESGASTE.get(ob.name, 0.35), desc='0 = pintura nueva, 1 = muy descascarada')
        prop(ob, 'suciedad', 0.8, desc='Salpicado de barro, chorreado y alga')
        # el alero del cuerpo usa el latón: comparte las propiedades del techo
        prop(ob, 'oxido_techo', 0.65, desc='Óxido de la calamina')
        prop(ob, 'color_techo', (0.40, 0.07, 0.04, 1.0), True, 'Color si la calamina está pintada')
        prop(ob, 'techo_pintado', 0.0, desc='0 = zinc desnudo, 1 = pintado')
    elif base == 'Techo':
        poner_slots(ob, ['M_Casa_Laton'])
        prop(ob, 'oxido_techo', 0.65, desc='Óxido de la calamina')
        prop(ob, 'color_techo', (0.40, 0.07, 0.04, 1.0), True, 'Color si la calamina está pintada')
        prop(ob, 'techo_pintado', 0.0, desc='0 = zinc desnudo, 1 = pintado')
    elif base in ('Chimenea', 'Tubo'):
        poner_slots(ob, ['M_Casa_AluminioGastado'])
    elif base == 'Pilares':
        poner_slots(ob, ['M_Casa_Pilares'])
    elif base == 'Escalera':
        poner_slots(ob, ['M_Casa_MaderaOscura'])
    else:
        continue
    veta_por_cara(ob)
    suavizar(ob)

result = informe
