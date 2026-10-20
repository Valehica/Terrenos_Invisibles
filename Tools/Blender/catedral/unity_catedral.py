# Lleva la iglesia de Isluga a Unity: copias horneables, UV, horneado y FBX.
# No toca los objetos originales (siguen con sus materiales procedurales y sus
# modificadores): trabaja sobre copias en la colección "Exportar_Unity", con los
# modificadores aplicados (Array de barandas, Mirror de remates) y juntadas en
# cuatro partes. Cada parte lleva su propio atlas de texturas.
#
# Orden (cada paso se puede repetir solo):
#   preparar()   rehace las copias desde los originales (borra las anteriores)
#   uv()         atlas por parte (Smart UV Project, densidad pareja)
#   hornear()    Cycles CPU -> Texturas/Isluga_<Parte>_{BaseColor,MetallicSmoothness,Normal,AO}.png
#   exportar()   Isluga.fbx (un material "M_Isluga_<Parte>" por parte) + copia a Unity
# Después, en Unity: menú "Terrenos/Norte/Iglesia de Isluga: materiales y prefab".
import bpy, os, time, shutil, math
import numpy as np

BASE = r"C:\Users\valeh\OneDrive\Desktop\Personal\Memoria\Concept\Norte\Catedral"
CARPETA_TEX = os.path.join(BASE, "Texturas")
CARPETA_FBX = os.path.join(BASE, "Fbx")
UNITY = r"C:\Users\valeh\Terrenos_Invisibles\Assets\Models\Norte\Catedral\Isluga"
COLECCION = "Exportar_Unity"
ESCALA = 1.0     # igual que CatedralV2.fbx (1:1 con la escena de Blender)

BARANDAS = ['Cube.003', 'Cube.005', 'Cube.006', 'Cube.008', 'Cube.010', 'Cube.011',
            'Cube.012', 'Cube.013', 'Cube.014', 'Cube.015', 'Cube.017',
            'Cube.004', 'Cube.007', 'Cube.016']
PARTES = {
    "Iglesia": (['Cube'], 2048),
    "Torre": (['Cube.001', 'Cube.002'], 1024),
    "Barandas": (BARANDAS, 2048),
    "Casucha": (['Cube.018'], 1024),
}
RES_AO = 1024


def _objeto(parte):
    return bpy.data.objects.get("Isluga_" + parte)


def _coleccion():
    col = bpy.data.collections.get(COLECCION)
    if not col:
        col = bpy.data.collections.new(COLECCION)
        bpy.context.scene.collection.children.link(col)
    return col


def _modo_objeto():
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')


def mostrar_copias(si=True):
    """Las copias se superponen a los originales: quedan ocultas (ojo y render)
    salvo mientras se trabaja con ellas."""
    col = bpy.data.collections.get(COLECCION)
    lc = bpy.context.view_layer.layer_collection.children.get(COLECCION)
    if col:
        col.hide_render = not si
    if lc:
        lc.hide_viewport = not si


def _seleccionar(obs):
    mostrar_copias(True)
    for o in bpy.context.selected_objects:
        o.select_set(False)
    for o in obs:
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]


# ------------------------------------------------------------------ copias
def _triangular_ngonos(me):
    """Unity descarta los n-gonos con vértices repetidos (p. ej. el arco de la
    puerta) por "self-intersecting": se triangulan antes de exportar."""
    import bmesh
    bm = bmesh.new(); bm.from_mesh(me)
    ng = [f for f in bm.faces if len(f.verts) > 4]
    if ng:
        bmesh.ops.triangulate(bm, faces=ng, quad_method='BEAUTY', ngon_method='EAR_CLIP')
    bm.to_mesh(me); bm.free()
    me.update()


def preparar():
    _modo_objeto()
    col = _coleccion()
    for o in list(col.objects):
        me = o.data
        bpy.data.objects.remove(o)
        if me and me.users == 0:
            bpy.data.meshes.remove(me)
    dg = bpy.context.evaluated_depsgraph_get()
    res = {}
    for parte, (nombres, _) in PARTES.items():
        copias = []
        for n in nombres:
            src = bpy.data.objects[n]
            me = bpy.data.meshes.new_from_object(src.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
            me.transform(src.matrix_world)
            ob = bpy.data.objects.new("_tmp_" + n, me)
            col.objects.link(ob)
            copias.append(ob)
        _seleccionar(copias)
        if len(copias) > 1:
            bpy.ops.object.join()
        ob = bpy.context.view_layer.objects.active
        ob.name = ob.data.name = "Isluga_" + parte
        _triangular_ngonos(ob.data)
        res[parte] = len(ob.data.polygons)
    mostrar_copias(False)
    return res


# ------------------------------------------------------------------ UV
ESCALA_OCULTA = 0.05


def _encoger_ocultas(ob):
    """Las caras de abajo apoyadas en el suelo nunca se ven: se encogen en UV
    (por isla, alrededor de su centro) para no gastar textura. Modo edición."""
    import bmesh
    bm = bmesh.from_edit_mesh(ob.data)
    uvl = bm.loops.layers.uv.active
    zmin = min(v.co.z for v in bm.verts)     # las copias están en coordenadas de mundo
    ocultas = {f for f in bm.faces
               if f.normal.z < -0.99 and all(abs(v.co.z - zmin) < 0.06 for v in f.verts)}
    vistas = set()
    for f in ocultas:
        if f in vistas:
            continue
        isla, pila = [], [f]
        vistas.add(f)
        while pila:
            g = pila.pop(); isla.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h in ocultas and h not in vistas:
                        vistas.add(h); pila.append(h)
        loops = [l for g in isla for l in g.loops]
        cx = sum(l[uvl].uv.x for l in loops) / len(loops)
        cy = sum(l[uvl].uv.y for l in loops) / len(loops)
        for l in loops:
            u = l[uvl].uv
            l[uvl].uv = (cx + (u.x - cx) * ESCALA_OCULTA, cy + (u.y - cy) * ESCALA_OCULTA)
    bmesh.update_edit_mesh(ob.data)
    return len(ocultas)


def uv():
    _modo_objeto()
    res = {}
    for parte in PARTES:
        ob = _objeto(parte)
        me = ob.data
        while len(me.uv_layers) > 1:
            me.uv_layers.remove(me.uv_layers[-1])
        if not me.uv_layers:
            me.uv_layers.new(name="UVMap")
        me.uv_layers[0].name = "UVMap"
        _seleccionar([ob])
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=math.radians(60), margin_method='FRACTION',
                                 island_margin=0.004, area_weight=0.0,
                                 correct_aspect=True, scale_to_bounds=False)
        _encoger_ocultas(ob)
        bpy.ops.uv.select_all(action='SELECT')
        bpy.ops.uv.pack_islands(udim_source='CLOSEST_UDIM', rotate=True, rotate_method='AXIS_ALIGNED',
                                scale=True, merge_overlap=False, margin_method='FRACTION',
                                margin=0.004, shape_method='CONCAVE')
        bpy.ops.object.mode_set(mode='OBJECT')
        res[parte] = densidad(parte)
    mostrar_copias(False)
    return res


def densidad(parte):
    """Píxeles por metro del atlas (para revisar que las partes queden parejas)."""
    ob = _objeto(parte)
    me = ob.data
    uvl = me.uv_layers[0].data
    a3 = sum(p.area for p in me.polygons)
    a2 = 0.0
    for p in me.polygons:
        pts = [uvl[i].uv for i in p.loop_indices]
        for i in range(1, len(pts) - 1):
            a, b, c = pts[0], pts[i], pts[i + 1]
            a2 += abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)) / 2
    res = PARTES[parte][1]
    return round(res * math.sqrt(a2 / a3), 1) if a3 else 0


# ------------------------------------------------------------------ horneado
def _mats(ob):
    return [m for m in ob.data.materials if m and m.use_nodes]


def _nodos(m):
    nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    out = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL' and n.is_active_output)
    return nt, bsdf, out


def _preparar_mats(mats):
    for m in mats:
        nt, bsdf, out = _nodos(m)
        if '_Bake' not in nt.nodes:
            n = nt.nodes.new('ShaderNodeTexImage'); n.name = n.label = '_Bake'
            n.location = (out.location.x, out.location.y - 300)
        if '_BakeEmit' not in nt.nodes:
            e = nt.nodes.new('ShaderNodeEmission'); e.name = e.label = '_BakeEmit'
            e.location = (out.location.x, out.location.y + 200)
        for n in nt.nodes:   # sin antialias por distancia a la cámara (calamina)
            if n.type == 'GROUP' and 'Suavizar Lejos' in n.inputs:
                n.inputs['Suavizar Lejos'].default_value = 0.0


def _limpiar_mats(mats):
    for m in mats:
        nt, bsdf, out = _nodos(m)
        for k in ('_Bake', '_BakeEmit'):
            if k in nt.nodes:
                nt.nodes.remove(nt.nodes[k])
        nt.links.new(bsdf.outputs[0], out.inputs['Surface'])
        for n in nt.nodes:
            if n.type == 'GROUP' and 'Suavizar Lejos' in n.inputs:
                n.inputs['Suavizar Lejos'].default_value = 1.0


def _destino(mats, img):
    for m in mats:
        nt = m.node_tree
        nt.nodes['_Bake'].image = img
        nt.nodes.active = nt.nodes['_Bake']


def _modo_emision(mats, entrada):
    for m in mats:
        nt, bsdf, out = _nodos(m)
        if entrada is None:
            nt.links.new(bsdf.outputs[0], out.inputs['Surface'])
            continue
        e = nt.nodes['_BakeEmit']
        for l in list(e.inputs['Color'].links):
            nt.links.remove(l)
        s = bsdf.inputs[entrada]
        if s.is_linked:
            nt.links.new(s.links[0].from_socket, e.inputs['Color'])
        else:
            v = s.default_value
            e.inputs['Color'].default_value = tuple(v) if hasattr(v, '__len__') else (v, v, v, 1.0)
        e.inputs['Strength'].default_value = 1.0
        nt.links.new(e.outputs[0], out.inputs['Surface'])


def _imagen(nombre, res, datos, alpha=False):
    img = bpy.data.images.get(nombre)
    if img:
        bpy.data.images.remove(img)
    img = bpy.data.images.new(nombre, res, res, alpha=alpha, float_buffer=False)
    img.colorspace_settings.name = 'Non-Color' if datos else 'sRGB'
    return img


def _guardar(img):
    os.makedirs(CARPETA_TEX, exist_ok=True)
    img.filepath_raw = os.path.join(CARPETA_TEX, img.name + ".png")
    img.file_format = 'PNG'
    img.save()


def _hornear(tipo, muestras, **kw):
    bpy.context.scene.cycles.samples = muestras
    bpy.ops.object.bake(type=tipo, margin=8, margin_type='EXTEND', use_clear=True,
                        target='IMAGE_TEXTURES', **kw)


def _pix(img):
    a = np.empty(img.size[0] * img.size[1] * 4, dtype=np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(-1, 4)


def hornear_parte(parte, ao=True):
    _modo_objeto()
    sc = bpy.context.scene
    ob = _objeto(parte)
    res = PARTES[parte][1]
    mats = _mats(ob)
    viejo = (sc.render.engine, sc.cycles.samples, sc.cycles.device)
    # los originales ocupan el mismo lugar que las copias: fuera del render
    # mientras se hornea, si no tapan la oclusión
    ocultos = [o for o in sc.objects if o.type == 'MESH' and COLECCION not in [c.name for c in o.users_collection]
               and not o.hide_render]
    sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'
    t0 = time.time(); hechos = []
    base = "Isluga_" + parte
    try:
        for o in ocultos:
            o.hide_render = True
        _preparar_mats(mats)
        _seleccionar([ob])
        img = _imagen(base + "_BaseColor", res, False); _destino(mats, img)
        _modo_emision(mats, 'Base Color'); _hornear('EMIT', 4); _guardar(img); hechos.append(img.name)
        im_m = _imagen("_tmp_metal", res, True); _destino(mats, im_m)
        _modo_emision(mats, 'Metallic'); _hornear('EMIT', 2)
        im_r = _imagen("_tmp_rough", res, True); _destino(mats, im_r)
        _modo_emision(mats, 'Roughness'); _hornear('EMIT', 4)
        pm, pr = _pix(im_m), _pix(im_r)
        ms = np.empty_like(pm)
        ms[:, 0] = ms[:, 1] = ms[:, 2] = pm[:, 0]
        ms[:, 3] = 1.0 - pr[:, 0]
        im_ms = _imagen(base + "_MetallicSmoothness", res, True, alpha=True)
        im_ms.pixels.foreach_set(ms.ravel())
        _guardar(im_ms); hechos.append(im_ms.name)
        bpy.data.images.remove(im_m); bpy.data.images.remove(im_r)
        _modo_emision(mats, None)
        img = _imagen(base + "_Normal", res, True); _destino(mats, img)
        _hornear('NORMAL', 4, normal_space='TANGENT'); _guardar(img); hechos.append(img.name)
        if ao:
            w = sc.world
            dist_vieja = w.light_settings.distance
            w.light_settings.distance = 1.0
            img = _imagen(base + "_AO", min(res, RES_AO), True); _destino(mats, img)
            _hornear('AO', 48); _guardar(img); hechos.append(img.name)
            w.light_settings.distance = dist_vieja
    finally:
        _modo_emision(mats, None)
        _limpiar_mats(mats)
        for o in ocultos:
            o.hide_render = False
        mostrar_copias(False)
        sc.render.engine, sc.cycles.samples, sc.cycles.device = viejo
    return {"parte": parte, "imagenes": hechos, "seg": round(time.time() - t0, 1)}


def hornear():
    return [hornear_parte(p) for p in PARTES]


# ------------------------------------------------------------------ FBX
def exportar():
    _modo_objeto()
    obs = [_objeto(p) for p in PARTES]
    guard = {}
    temp = []
    for parte, ob in zip(PARTES, obs):
        me = ob.data
        idx = [0] * len(me.polygons)
        me.polygons.foreach_get('material_index', idx)
        guard[me] = (list(me.materials), idx)
        mat = bpy.data.materials.new("M_Isluga_" + parte)
        temp.append(mat)
        me.materials.clear()
        me.materials.append(mat)
        me.polygons.foreach_set('material_index', [0] * len(me.polygons))
    os.makedirs(CARPETA_FBX, exist_ok=True)
    ruta = os.path.join(CARPETA_FBX, "Isluga.fbx")
    try:
        _seleccionar(obs)
        bpy.ops.export_scene.fbx(
            filepath=ruta, use_selection=True, object_types={'MESH'},
            global_scale=ESCALA, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
            axis_forward='-Z', axis_up='Y', bake_space_transform=True,
            use_mesh_modifiers=True, mesh_smooth_type='FACE',
            add_leaf_bones=False, bake_anim=False, path_mode='AUTO', embed_textures=False)
    finally:
        for me, (mats, idx) in guard.items():
            me.materials.clear()
            for m in mats:
                me.materials.append(m)
            me.polygons.foreach_set('material_index', idx)
            me.update()
        for m in temp:
            bpy.data.materials.remove(m)
        mostrar_copias(False)
    copiados = []
    if UNITY:
        tex_u = os.path.join(UNITY, "Texturas")
        os.makedirs(tex_u, exist_ok=True)
        shutil.copy2(ruta, os.path.join(UNITY, "Isluga.fbx")); copiados.append("Isluga.fbx")
        for f in os.listdir(CARPETA_TEX):
            if f.startswith("Isluga_") and f.endswith(".png"):
                shutil.copy2(os.path.join(CARPETA_TEX, f), os.path.join(tex_u, f)); copiados.append(f)
    return {"fbx": ruta, "copiados": copiados}
