# Version lejana (muy baja topologia) de cada arbol de coigue_collection.blend.
# Tronco decimado a TRIS_TRONCO triangulos + una fraccion de las tarjetas de hojas,
# elegidas lo mas repartidas posible y agrandadas para que la copa no quede rala.
# Cada LOD es un solo objeto (corteza + hojas como submallas) con el mismo pivote,
# rotacion y escala que el tronco original, asi las coordenadas de objeto de la
# corteza coinciden. Se deja corrido en X (DESPLAZAMIENTO) para comparar.
# Re-correr rehace los LOD (borra los anteriores de la coleccion).
import bpy, bmesh, math
from mathutils import Vector, Matrix

COLECCION = "LOD lejanos"
DESPLAZAMIENTO = Vector((25, 0, 0))

# tronco, objetos de hojas (o coleccion), triangulos del tronco, fraccion de hojas
ARBOLES = [
    dict(tronco="ArbolGrande",   hojas_col="Hojas",  tris=250, frac=0.33),
    dict(tronco="Arbol Pequeño", hojas_col="Hojas 1", tris=160, frac=0.40),
    # el tronco del cipres son muchas piezas sueltas (fuste + un tubo abierto por rama),
    # el decimado no las reduce: se decima el fuste y cada rama pasa a un tubo de 3 lados
    dict(tronco="CipresGrande",  hojas_hijas=True,   tris=70, frac=0.33, ramas=30),
    dict(tronco="CipresPequeño", hojas_hijas=True,   tris=60, frac=0.35, ramas=24),
]


def malla_mundo(ob, ratio=None):
    """Copia de la malla evaluada de ob, en coordenadas de mundo."""
    mod = None
    if ratio is not None and ratio < 1:
        mod = ob.modifiers.new("TMP_decimar", 'DECIMATE')
        mod.ratio = ratio
        mod.use_collapse_triangulate = True
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    if mod:
        ob.modifiers.remove(mod)
    me.transform(ob.matrix_world)
    return me


def tris(ob):
    me = ob.data
    me.calc_loop_triangles()
    return len(me.loop_triangles)


def preparar(me, mats, mats_mundo):
    """Uniforma la capa UV a 'UVMap' y pasa material_index a la lista global."""
    if me.uv_layers:
        me.uv_layers[0].name = "UVMap"
        while len(me.uv_layers) > 1:
            me.uv_layers.remove(me.uv_layers[1])
    else:
        me.uv_layers.new(name="UVMap")
    for p in me.polygons:
        m = mats[p.material_index] if p.material_index < len(mats) else None
        p.material_index = mats_mundo.index(m)


def islas(bm):
    bm.verts.ensure_lookup_table()
    visto, res = set(), []
    for v in bm.verts:
        if v.index in visto:
            continue
        pila, isla = [v], []
        visto.add(v.index)
        while pila:
            x = pila.pop()
            isla.append(x)
            for e in x.link_edges:
                o = e.other_vert(x)
                if o.index not in visto:
                    visto.add(o.index)
                    pila.append(o)
        res.append(isla)
    return res


def tubo_rama(cos):
    """Eje principal de la rama, y radio y centro de cada punta -> (p0, r0, p1, r1, eje)."""
    import numpy as np
    P = np.array([tuple(c) for c in cos])
    m = P.mean(axis=0)
    _, _, vt = np.linalg.svd(P - m)
    eje = Vector(vt[0])
    t = (P - m) @ vt[0]
    lo, hi = np.quantile(t, 0.2), np.quantile(t, 0.8)
    puntas = []
    for sel, t_punta in ((t <= lo, t.min()), (t >= hi, t.max())):
        Q = P[sel]
        c = Vector(Q.mean(axis=0))
        r = float(np.mean([((Vector(q) - c) - eje * (Vector(q) - c).dot(eje)).length for q in Q]))
        # llevar el centro hasta la punta real de la rama
        c = c + eje * (t_punta - (c - Vector(m)).dot(eje))
        puntas.append((c, r))
    return puntas[0][0], puntas[0][1], puntas[1][0], puntas[1][1], eje


def tronco_por_piezas(tronco, tris_fuste, n_ramas, mats_mundo):
    """bmesh en mundo: fuste decimado + las n_ramas mas grandes como tubos de 3 lados."""
    me = malla_mundo(tronco)
    preparar(me, [s.material for s in tronco.material_slots], mats_mundo)
    mat_idx = me.polygons[0].material_index
    bt = bmesh.new()
    bt.from_mesh(me)
    bpy.data.meshes.remove(me)
    piezas = islas(bt)
    alto = lambda isla: max(v.co.z for v in isla) - min(v.co.z for v in isla)
    fuste = max(piezas, key=alto)
    ramas = [p for p in piezas if p is not fuste]

    # ramas: las mas grandes (largo x radio)
    tubos = []
    for p in ramas:
        p0, r0, p1, r1, eje = tubo_rama([v.co.copy() for v in p])
        tubos.append(((p1 - p0).length * (r0 + r1), p0, r0, p1, r1, eje))
    tubos.sort(key=lambda x: -x[0])

    # fuste: objeto temporal para decimarlo
    quitar = [v for p in ramas for v in p]
    bmesh.ops.delete(bt, geom=quitar, context='VERTS')
    me = bpy.data.meshes.new("tmp_fuste")
    bt.to_mesh(me)
    bt.free()
    tmp = bpy.data.objects.new("tmp_fuste", me)
    bpy.context.scene.collection.objects.link(tmp)
    me.calc_loop_triangles()
    dec = malla_mundo(tmp, tris_fuste / max(1, len(me.loop_triangles)))
    bpy.data.objects.remove(tmp)
    bpy.data.meshes.remove(me)
    bm = bmesh.new()
    bm.from_mesh(dec)
    bpy.data.meshes.remove(dec)

    # anillos en sentido antihorario alrededor del eje (p0 -> p1): las caras miran hacia afuera
    for _, p0, r0, p1, r1, eje in tubos[:n_ramas]:
        a = eje.orthogonal().normalized()
        b = eje.cross(a).normalized()
        anillos = []
        for c, r in ((p0, r0), (p1, r1)):
            anillos.append([bm.verts.new(c + (a * math.cos(k * 2 * math.pi / 3) + b * math.sin(k * 2 * math.pi / 3)) * r)
                            for k in range(3)])
        for k in range(3):
            f = bm.faces.new((anillos[0][k], anillos[0][(k + 1) % 3], anillos[1][(k + 1) % 3], anillos[1][k]))
            f.material_index = mat_idx
            f.smooth = True
    return bm


def elegir_repartidas(centros, areas, k):
    """Muestreo por punto mas lejano, empezando por la tarjeta mas grande."""
    n = len(centros)
    if k >= n:
        return list(range(n))
    elegidas = [max(range(n), key=lambda i: areas[i])]
    dmin = [(c - centros[elegidas[0]]).length for c in centros]
    while len(elegidas) < k:
        # pesa un poco el area para preferir tarjetas grandes a igual distancia
        i = max(range(n), key=lambda j: dmin[j] * (0.7 + 0.3 * areas[j] / max(areas)))
        elegidas.append(i)
        for j in range(n):
            dmin[j] = min(dmin[j], (centros[j] - centros[i]).length)
    return elegidas


def hacer_lod(cfg, col_lod):
    tronco = bpy.data.objects[cfg["tronco"]]
    if cfg.get("hojas_hijas"):
        hojas = [c for c in tronco.children if c.type == 'MESH']
    else:
        hojas = [o for o in bpy.data.collections[cfg["hojas_col"]].all_objects if o.type == 'MESH']

    mats_mundo = []
    for ob in [tronco] + hojas:
        for s in ob.material_slots:
            if s.material not in mats_mundo:
                mats_mundo.append(s.material)

    if cfg.get("ramas"):
        bm = tronco_por_piezas(tronco, cfg["tris"], cfg["ramas"], mats_mundo)
    else:
        bm = bmesh.new()
        me = malla_mundo(tronco, cfg["tris"] / max(1, tris(tronco)))
        preparar(me, [s.material for s in tronco.material_slots], mats_mundo)
        bm.from_mesh(me)
        bpy.data.meshes.remove(me)
    n_tronco = len(bm.faces)

    # hojas: todas juntas, despues se borran las que no se usan
    bh = bmesh.new()
    for ob in hojas:
        me = malla_mundo(ob)
        preparar(me, [s.material for s in ob.material_slots], mats_mundo)
        bh.from_mesh(me)
        bpy.data.meshes.remove(me)
    bh.faces.ensure_lookup_table()
    caras = list(bh.faces)
    k = max(3, round(len(caras) * cfg["frac"]))
    centros = [f.calc_center_median() for f in caras]
    areas = [f.calc_area() for f in caras]
    elegidas = set(elegir_repartidas(centros, areas, k))
    bmesh.ops.delete(bh, geom=[f for i, f in enumerate(caras) if i not in elegidas], context='FACES')
    # agrandar cada tarjeta sobre su centro para conservar cobertura
    escala = min(1.6, (1 / cfg["frac"]) ** 0.4)
    for f in bh.faces:
        c = f.calc_center_median()
        for v in f.verts:
            v.co = c + (v.co - c) * escala
    me = bpy.data.meshes.new("tmp_hojas")
    bh.to_mesh(me)
    bh.free()
    bm.from_mesh(me)
    bpy.data.meshes.remove(me)

    # a espacio local del tronco
    bm.transform(tronco.matrix_world.inverted())
    nombre = tronco.name + "_LOD"
    viejo = bpy.data.objects.get(nombre)
    if viejo:
        viejo_me = viejo.data
        bpy.data.objects.remove(viejo)
        bpy.data.meshes.remove(viejo_me)
    me = bpy.data.meshes.new(nombre)
    bm.to_mesh(me)
    bm.free()
    for m in mats_mundo:
        me.materials.append(m)
    for p in me.polygons:
        p.use_smooth = p.index < n_tronco  # corteza suave, tarjetas de hojas planas
    ob = bpy.data.objects.new(nombre, me)
    ob.matrix_world = Matrix.Translation(DESPLAZAMIENTO) @ tronco.matrix_world
    col_lod.objects.link(ob)
    me.calc_loop_triangles()
    orig = tris(tronco) + sum(tris(h) for h in hojas)
    return dict(lod=nombre, tris=len(me.loop_triangles), tris_original=orig,
                hojas=f"{k}/{len(caras)}", escala_hojas=round(escala, 2))


col_lod = bpy.data.collections.get(COLECCION)
if col_lod is None:
    col_lod = bpy.data.collections.new(COLECCION)
    bpy.context.scene.collection.children.link(col_lod)
informe = [hacer_lod(c, col_lod) for c in ARBOLES]
print(informe)
