# Hornea los materiales procedurales de las casas a texturas para Unity (URP Lit).
# Por variante (colección):  <Casa>_<V>_BaseColor.png   (sRGB)
#                            <Casa>_<V>_MetallicSmoothness.png  (R = metal, A = suavidad)
# Por diseño (se comparten): <Casa>_Normal.png  (tangente, OpenGL = Unity)
#                            <Casa>_AO.png
# Requiere las UV de atlas (uv_atlas_casas.py). Las variantes comparten malla con V1.
import bpy, os, time
import numpy as np

CARPETA = r"C:\Users\valeh\OneDrive\Desktop\Personal\Memoria\Concept\Sur\Pasarela\Casas\Texturas"
RES = 2048
RES_AO = 1024
DISENOS = {"Casa1": "Casa1", "Casa1.001": "Casa2", "Casa3": "Casa3", "Casa4": "Casa4"}


def variantes():
    for col, nombre in DISENOS.items():
        yield col, nombre, "V1"
        for v in ("V2", "V3"):
            yield f"{col}_{v}", nombre, v


def mats():
    return [m for m in bpy.data.materials if m.name.startswith('M_Casa_') and m.use_nodes]


def nodos(m):
    nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    out = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
    return nt, bsdf, out


def preparar():
    for m in mats():
        nt, bsdf, out = nodos(m)
        if '_Bake' not in nt.nodes:
            n = nt.nodes.new('ShaderNodeTexImage'); n.name = n.label = '_Bake'
            n.location = (out.location.x, out.location.y - 300)
        if '_BakeEmit' not in nt.nodes:
            e = nt.nodes.new('ShaderNodeEmission'); e.name = e.label = '_BakeEmit'
            e.location = (out.location.x, out.location.y + 200)
        for n in nt.nodes:   # sin antialias por distancia a la cámara al hornear
            if n.type == 'GROUP' and 'Suavizar Lejos' in n.inputs:
                n.inputs['Suavizar Lejos'].default_value = 0.0


def limpiar():
    for m in mats():
        nt, bsdf, out = nodos(m)
        for k in ('_Bake', '_BakeEmit'):
            if k in nt.nodes: nt.nodes.remove(nt.nodes[k])
        nt.links.new(bsdf.outputs[0], out.inputs['Surface'])
        for n in nt.nodes:
            if n.type == 'GROUP' and 'Suavizar Lejos' in n.inputs:
                n.inputs['Suavizar Lejos'].default_value = 1.0


def destino(img):
    for m in mats():
        nt = m.node_tree
        nt.nodes['_Bake'].image = img
        nt.nodes.active = nt.nodes['_Bake']


def modo_emision(entrada):
    """entrada = nombre del input del Principled a volcar como emisión; None = shader normal."""
    for m in mats():
        nt, bsdf, out = nodos(m)
        if entrada is None:
            nt.links.new(bsdf.outputs[0], out.inputs['Surface']); continue
        e = nt.nodes['_BakeEmit']
        for l in list(e.inputs['Color'].links): nt.links.remove(l)
        s = bsdf.inputs[entrada]
        if s.is_linked:
            nt.links.new(s.links[0].from_socket, e.inputs['Color'])
        else:
            v = s.default_value
            e.inputs['Color'].default_value = tuple(v) if hasattr(v, '__len__') else (v, v, v, 1.0)
        e.inputs['Strength'].default_value = 1.0
        nt.links.new(e.outputs[0], out.inputs['Surface'])


def imagen(nombre, res, datos, alpha=False):
    img = bpy.data.images.get(nombre)
    if img and (img.size[0] != res or img.alpha_mode is None):
        bpy.data.images.remove(img); img = None
    if not img:
        img = bpy.data.images.new(nombre, res, res, alpha=alpha, float_buffer=False)
    img.colorspace_settings.name = 'Non-Color' if datos else 'sRGB'
    return img


def guardar(img, nombre):
    os.makedirs(CARPETA, exist_ok=True)
    img.filepath_raw = os.path.join(CARPETA, nombre + ".png")
    img.file_format = 'PNG'
    img.save()


def hornear(tipo, muestras, **kw):
    bpy.context.scene.cycles.samples = muestras
    bpy.ops.object.bake(type=tipo, margin=8, margin_type='EXTEND', use_clear=True,
                        target='IMAGE_TEXTURES', **kw)


def seleccionar(col):
    obs = [o for o in bpy.data.collections[col].objects if o.type == 'MESH']
    for o in bpy.context.selected_objects: o.select_set(False)
    for o in obs: o.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    return obs


def pix(img):
    a = np.empty(img.size[0] * img.size[1] * 4, dtype=np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(-1, 4)


def hornear_variante(col, nombre, v, compartidas=None):
    """compartidas: hornear también Normal y AO del diseño (por defecto solo en V1)."""
    if compartidas is None: compartidas = (v == "V1")
    sc = bpy.context.scene
    viejo = (sc.render.engine, sc.cycles.samples, sc.cycles.device)
    sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'
    t0 = time.time(); hechos = []
    try:
        preparar()
        seleccionar(col)
        base = f"{nombre}_{v}"
        # color base
        img = imagen(base + "_BaseColor", RES, False); destino(img)
        modo_emision('Base Color'); hornear('EMIT', 4); guardar(img, base + "_BaseColor"); hechos.append(img.name)
        # metal y rugosidad -> MetallicSmoothness
        im_m = imagen("_tmp_metal", RES, True); destino(im_m)
        modo_emision('Metallic'); hornear('EMIT', 2)
        im_r = imagen("_tmp_rough", RES, True); destino(im_r)
        modo_emision('Roughness'); hornear('EMIT', 4)
        pm, pr = pix(im_m), pix(im_r)
        ms = np.empty_like(pm)
        ms[:, 0] = ms[:, 1] = ms[:, 2] = pm[:, 0]
        ms[:, 3] = 1.0 - pr[:, 0]
        im_ms = imagen(base + "_MetallicSmoothness", RES, True, alpha=True)
        im_ms.pixels.foreach_set(ms.ravel())
        guardar(im_ms, base + "_MetallicSmoothness"); hechos.append(im_ms.name)
        bpy.data.images.remove(im_m); bpy.data.images.remove(im_r)
        if compartidas:
            modo_emision(None)
            img = imagen(nombre + "_Normal", RES, True); destino(img)
            hornear('NORMAL', 4, normal_space='TANGENT'); guardar(img, nombre + "_Normal"); hechos.append(img.name)
            w = sc.world
            dist_vieja = w.light_settings.distance
            w.light_settings.distance = 1.5
            img = imagen(nombre + "_AO", RES_AO, True); destino(img)
            hornear('AO', 48); guardar(img, nombre + "_AO"); hechos.append(img.name)
            w.light_settings.distance = dist_vieja
    finally:
        modo_emision(None)
        limpiar()
        sc.render.engine, sc.cycles.samples, sc.cycles.device = viejo
    return {"variante": col, "imagenes": hechos, "seg": round(time.time() - t0, 1)}


def hornear_todo():
    return [hornear_variante(c, n, v) for c, n, v in variantes()]


if __name__ == "__main__":
    print(hornear_todo())
