# Lleva las 15 casas de Norte (PackCasas.blend) a Unity, cada una por separado:
# copias horneables, UV, horneado y un FBX por casa. No toca los objetos originales:
# trabaja sobre copias en la colección "Exportar_Unity" (casa + techo juntados en
# un objeto "Norte_CasaN_Vm" con el origen abajo al centro).
#
# Orden (cada paso se puede repetir solo):
#   preparar()  rehace las copias (borra las anteriores)
#   uv()        atlas por casa (Smart UV Project, base apoyada en el suelo encogida)
#   hornear()   Cycles CPU -> Texturas/Norte_CasaN_Vm_{BaseColor,MetallicSmoothness,Normal,AO}.png
#   exportar()  un FBX por casa (material "M_Norte_CasaN_Vm") + copia a Unity
# Después, en Unity: menú "Terrenos/Norte/Otras casas: materiales y prefabs".
import bpy, os, time, shutil, math
from mathutils import Vector
import unity_catedral as UC     # utilidades de horneado (no usa sus rutas)

BASE = r"C:\Users\valeh\OneDrive\Desktop\Personal\Memoria\Concept\Norte\OtrasCasas"
CARPETA_TEX = os.path.join(BASE, "Texturas")
CARPETA_FBX = os.path.join(BASE, "Fbx")
UNITY = r"C:\Users\valeh\Terrenos_Invisibles\Assets\Models\Norte\OtrasCasas"
COLECCION = "Exportar_Unity"
ESCALA = 1.0
# resolución del atlas por diseño (las casas largas llevan más textura)
RES = {"Casa1": 1024, "Casa2": 2048, "Casa3": 1024, "Casa4": 1024, "Casa5": 2048}
RES_AO = 1024
VARIANTES = {"V1": "", "V2": ".001", "V3": ".002"}


def casas():
    """(nombre de exportación, [objetos fuente], resolución)"""
    for casa, res in RES.items():
        for v, suf in VARIANTES.items():
            fuentes = [casa + suf]
            techo = casa.replace("Casa", "Techo") + suf
            if bpy.data.objects.get(techo):
                fuentes.append(techo)
            yield f"Norte_{casa}_{v}", fuentes, res


def _coleccion():
    col = bpy.data.collections.get(COLECCION)
    if not col:
        col = bpy.data.collections.new(COLECCION)
        bpy.context.scene.collection.children.link(col)
    return col


def mostrar_copias(si=True):
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


def _copias():
    col = bpy.data.collections.get(COLECCION)
    return [o for o in col.objects if o.type == 'MESH'] if col else []


# ------------------------------------------------------------------ copias
def preparar():
    UC._modo_objeto()
    col = _coleccion()
    for o in list(col.objects):
        me = o.data
        bpy.data.objects.remove(o)
        if me and me.users == 0:
            bpy.data.meshes.remove(me)
    dg = bpy.context.evaluated_depsgraph_get()
    res = {}
    for nombre, fuentes, _ in casas():
        partes = []
        for n in fuentes:
            src = bpy.data.objects[n]
            me = bpy.data.meshes.new_from_object(src.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
            me.transform(src.matrix_world)
            ob = bpy.data.objects.new("_tmp_" + n, me)
            col.objects.link(ob)
            partes.append(ob)
        _seleccionar(partes)
        if len(partes) > 1:
            bpy.ops.object.join()
        ob = bpy.context.view_layer.objects.active
        ob.name = ob.data.name = nombre
        UC._triangular_ngonos(ob.data)
        # origen abajo al centro: el prefab se apoya en el suelo
        vs = [v.co for v in ob.data.vertices]
        off = Vector(((min(v.x for v in vs) + max(v.x for v in vs)) / 2,
                      (min(v.y for v in vs) + max(v.y for v in vs)) / 2,
                      min(v.z for v in vs)))
        ob.data.transform(__import__('mathutils').Matrix.Translation(-off))
        ob.location = off
        res[nombre] = len(ob.data.polygons)
    mostrar_copias(False)
    return res


# ------------------------------------------------------------------ UV
def uv(solo=None):
    UC._modo_objeto()
    res = {}
    for nombre, _, r in casas():
        if solo and nombre not in solo:
            continue
        ob = bpy.data.objects[nombre]
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
        UC._encoger_ocultas(ob)
        bpy.ops.uv.select_all(action='SELECT')
        bpy.ops.uv.pack_islands(udim_source='CLOSEST_UDIM', rotate=True, rotate_method='AXIS_ALIGNED',
                                scale=True, merge_overlap=False, margin_method='FRACTION',
                                margin=0.004, shape_method='CONCAVE')
        bpy.ops.object.mode_set(mode='OBJECT')
        res[nombre] = r
    mostrar_copias(False)
    return res


# ------------------------------------------------------------------ horneado
def _guardar(img):
    os.makedirs(CARPETA_TEX, exist_ok=True)
    img.filepath_raw = os.path.join(CARPETA_TEX, img.name + ".png")
    img.file_format = 'PNG'
    img.save()


def hornear_casa(nombre, res, ao=True):
    UC._modo_objeto()
    sc = bpy.context.scene
    ob = bpy.data.objects[nombre]
    mats = UC._mats(ob)
    viejo = (sc.render.engine, sc.cycles.samples, sc.cycles.device)
    # fuera del render todo lo demás (originales superpuestos y las otras copias)
    ocultos = [o for o in sc.objects if o.type == 'MESH' and o is not ob and not o.hide_render]
    sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'
    t0 = time.time(); hechos = []
    try:
        for o in ocultos:
            o.hide_render = True
        UC._preparar_mats(mats)
        _seleccionar([ob])
        img = UC._imagen(nombre + "_BaseColor", res, False); UC._destino(mats, img)
        UC._modo_emision(mats, 'Base Color'); UC._hornear('EMIT', 4); _guardar(img); hechos.append(img.name)
        im_m = UC._imagen("_tmp_metal", res, True); UC._destino(mats, im_m)
        UC._modo_emision(mats, 'Metallic'); UC._hornear('EMIT', 2)
        im_r = UC._imagen("_tmp_rough", res, True); UC._destino(mats, im_r)
        UC._modo_emision(mats, 'Roughness'); UC._hornear('EMIT', 4)
        pm, pr = UC._pix(im_m), UC._pix(im_r)
        ms = pm.copy()
        ms[:, 1] = ms[:, 2] = ms[:, 0]
        ms[:, 3] = 1.0 - pr[:, 0]
        im_ms = UC._imagen(nombre + "_MetallicSmoothness", res, True, alpha=True)
        im_ms.pixels.foreach_set(ms.ravel())
        _guardar(im_ms); hechos.append(im_ms.name)
        bpy.data.images.remove(im_m); bpy.data.images.remove(im_r)
        UC._modo_emision(mats, None)
        img = UC._imagen(nombre + "_Normal", res, True); UC._destino(mats, img)
        UC._hornear('NORMAL', 4, normal_space='TANGENT'); _guardar(img); hechos.append(img.name)
        if ao:
            w = sc.world
            dist_vieja = w.light_settings.distance
            w.light_settings.distance = 1.0
            img = UC._imagen(nombre + "_AO", min(res, RES_AO), True); UC._destino(mats, img)
            UC._hornear('AO', 48); _guardar(img); hechos.append(img.name)
            w.light_settings.distance = dist_vieja
        for n in hechos:            # liberar memoria: ya están en disco
            if bpy.data.images.get(n):
                bpy.data.images.remove(bpy.data.images[n])
    finally:
        UC._modo_emision(mats, None)
        UC._limpiar_mats(mats)
        for o in ocultos:
            o.hide_render = False
        mostrar_copias(False)
        sc.render.engine, sc.cycles.samples, sc.cycles.device = viejo
    return {"casa": nombre, "seg": round(time.time() - t0, 1)}


def hornear(solo=None):
    return [hornear_casa(n, r) for n, _, r in casas() if not solo or n in solo]


# ------------------------------------------------------------------ FBX
def exportar(solo=None):
    UC._modo_objeto()
    os.makedirs(CARPETA_FBX, exist_ok=True)
    tex_u = os.path.join(UNITY, "Texturas")
    os.makedirs(tex_u, exist_ok=True)
    hechos = []
    for nombre, _, _ in casas():
        if solo and nombre not in solo:
            continue
        ob = bpy.data.objects[nombre]
        me = ob.data
        idx = [0] * len(me.polygons)
        me.polygons.foreach_get('material_index', idx)
        mats = list(me.materials)
        loc = ob.location.copy()
        tmp = bpy.data.materials.new("M_" + nombre)
        ruta = os.path.join(CARPETA_FBX, nombre + ".fbx")
        try:
            me.materials.clear(); me.materials.append(tmp)
            me.polygons.foreach_set('material_index', [0] * len(me.polygons))
            ob.location = (0.0, 0.0, 0.0)
            bpy.context.view_layer.update()
            _seleccionar([ob])
            bpy.ops.export_scene.fbx(
                filepath=ruta, use_selection=True, object_types={'MESH'},
                global_scale=ESCALA, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                use_mesh_modifiers=True, mesh_smooth_type='FACE',
                add_leaf_bones=False, bake_anim=False, path_mode='AUTO', embed_textures=False)
        finally:
            ob.location = loc
            me.materials.clear()
            for m in mats:
                me.materials.append(m)
            me.polygons.foreach_set('material_index', idx)
            me.update()
            bpy.data.materials.remove(tmp)
            mostrar_copias(False)
        shutil.copy2(ruta, os.path.join(UNITY, nombre + ".fbx"))
        for sufijo in ("BaseColor", "MetallicSmoothness", "Normal", "AO"):
            f = f"{nombre}_{sufijo}.png"
            if os.path.exists(os.path.join(CARPETA_TEX, f)):
                shutil.copy2(os.path.join(CARPETA_TEX, f), os.path.join(tex_u, f))
        hechos.append(nombre)
    return hechos
