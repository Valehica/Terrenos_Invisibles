# Material PBR del gaviotin: hornea del high poly al low poly y arma M_Gabiotin.
#
# Se corre dentro de Blender, con Gabiortin9.blend abierto:
#   exec(open(r".../Tools/Blender/gaviotin_material.py").read())
#   hornear("NORMAL"); hornear("AO"); hornear("MASCARAS")
#   componer(); armar_material()
#
# Salida en <carpeta del blend>/Texturas:
#   T_Gabiotin_BaseColor.png  blanco, para pintar encima
#   T_Gabiotin_Normal.png     tangente, OpenGL (Y+), 16 bits
#   T_Gabiotin_Roughness.png  escala de grises
#   T_Gabiotin_AO.png         oclusion, para el mapa Occlusion de Unity
#   T_Gabiotin_MetalSmooth.png  R = metalico (0), A = 1 - rugosidad (URP Lit)
#
# Los bakes crudos (float) quedan en %TEMP%/bakes_gaviotin/*.npy, asi componer()
# se puede re-correr para ajustar la rugosidad sin volver a hornear.
#
# Modelo: cabeza hacia -Y, cola hacia +Y, alas en X. El low poly es la mitad
# -X con Mirror (las dos mitades comparten UV); al hornear se corre la UV de
# la mitad espejada fuera de la imagen para que solo escriba una mitad.

import os
import time

import bpy
import numpy as np

ALTA = "Pajado_HighPoly"
BAJA = "Pajaro_LowPoly"
MATERIAL = "M_Gabiotin"
RES = 2048

# Distancias medidas: el high poly queda entre -5.6 y +5.4 cm del low poly.
EXTRUSION = 0.07
MAX_RAYO = 0.15
DIST_AO = 0.25
# En el lomo y la base de la cola hay plumas superpuestas a ~2.4 cm sobre el
# cuerpo: la jaula de 7 cm las atraviesa y la normal sale torcida. Ahi se usa
# un segundo bake con jaula corta (NORMAL_CORTO), solo donde el primero falla.
EXTRUSION_CORTA = 0.015
MAX_RAYO_CORTO = 0.06
# Caras interiores del pico (la boca: el low poly tiene las mandibulas
# separadas y el high poly es macizo). Quedan con normal plana.
CARAS_BOCA = range(1018, 1035)

# Regiones (espacio objeto, el high poly esta en el origen sin transformar).
PICO_Y = (-0.85, -0.88)          # sin plumas desde y < -0.88
OJO = (0.257, -0.627, 1.665)     # |x|, y, z
OJO_R = (0.030, 0.042)           # radio pleno, radio donde se apaga
PATAS_Z = (0.58, 0.52)           # patas por debajo del vientre (a 0.62 ya pisaba la panza)

# Rugosidad base por region.
RUG_PLUMAS = 0.66
RUG_PICO = 0.34
RUG_OJO = 0.06
RUG_PATAS = 0.48


def _carpeta():
    out = os.path.join(os.path.dirname(bpy.data.filepath), "Texturas")
    os.makedirs(out, exist_ok=True)
    return out


def _bakes():
    # 256 MB de float: van a %TEMP% y no a la carpeta del blend (que esta en OneDrive)
    out = os.path.join(os.environ.get("TEMP", os.path.dirname(bpy.data.filepath)), "bakes_gaviotin")
    os.makedirs(out, exist_ok=True)
    return out


def _imagen(nombre, float_buffer=True):
    img = bpy.data.images.get(nombre)
    if img and (img.size[0] != RES or img.is_float != float_buffer):
        bpy.data.images.remove(img)
        img = None
    if img is None:
        img = bpy.data.images.new(nombre, RES, RES, alpha=False, float_buffer=float_buffer)
    img.colorspace_settings.name = "Non-Color"
    return img


def _material_mascaras():
    """Material temporal del high poly para el bake EMIT.

    R = rugosidad base por region (con ruido), G = mascara de plumas,
    B = pointiness crudo (cavidades de las plumas).
    """
    mat = bpy.data.materials.get("TMP_Gabiotin_Mascaras") or bpy.data.materials.new("TMP_Gabiotin_Mascaras")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    N, L = nt.nodes, nt.links

    def math(op, a=None, b=None, c=None, clamp=False):
        n = N.new("ShaderNodeMath")
        n.operation = op
        n.use_clamp = clamp
        for i, v in enumerate((a, b, c)):
            if v is None:
                continue
            if isinstance(v, (int, float)):
                n.inputs[i].default_value = v
            else:
                L.new(v, n.inputs[i])
        return n.outputs[0]

    def smooth(x, e0, e1):
        # 0 en e0, 1 en e1 (sirve en cualquier sentido)
        n = N.new("ShaderNodeMapRange")
        n.interpolation_type = "SMOOTHSTEP"
        n.inputs["From Min"].default_value = e0
        n.inputs["From Max"].default_value = e1
        L.new(x, n.inputs["Value"])
        return n.outputs[0]

    geo = N.new("ShaderNodeNewGeometry")
    sep = N.new("ShaderNodeSeparateXYZ")
    L.new(geo.outputs["Position"], sep.inputs[0])
    x = math("ABSOLUTE", sep.outputs[0])
    y, z = sep.outputs[1], sep.outputs[2]

    pico = smooth(y, PICO_Y[0], PICO_Y[1])
    patas = smooth(z, PATAS_Z[0], PATAS_Z[1])

    comb = N.new("ShaderNodeCombineXYZ")
    L.new(x, comb.inputs[0]); L.new(y, comb.inputs[1]); L.new(z, comb.inputs[2])
    dist = N.new("ShaderNodeVectorMath")
    dist.operation = "DISTANCE"
    L.new(comb.outputs[0], dist.inputs[0])
    dist.inputs[1].default_value = OJO
    ojo = smooth(dist.outputs["Value"], OJO_R[1], OJO_R[0])

    # Ruido de variacion: manchas grandes en las plumas, vetas a lo largo del pico.
    ruido = N.new("ShaderNodeTexNoise")
    ruido.inputs["Scale"].default_value = 5.0
    ruido.inputs["Detail"].default_value = 4.0
    L.new(geo.outputs["Position"], ruido.inputs["Vector"])
    vetas = N.new("ShaderNodeTexNoise")
    vetas.inputs["Scale"].default_value = 40.0
    vetas.inputs["Detail"].default_value = 2.0
    estira = N.new("ShaderNodeVectorMath")
    estira.operation = "MULTIPLY"
    estira.inputs[1].default_value = (1.0, 0.08, 1.0)  # alargado en Y (largo del pico)
    L.new(geo.outputs["Position"], estira.inputs[0])
    L.new(estira.outputs[0], vetas.inputs["Vector"])

    r_plumas = math("MULTIPLY_ADD", math("SUBTRACT", ruido.outputs["Fac"], 0.5), 0.12, RUG_PLUMAS)
    r_pico = math("MULTIPLY_ADD", math("SUBTRACT", vetas.outputs["Fac"], 0.5), 0.16, RUG_PICO)

    def mezcla(f, a, b):
        n = N.new("ShaderNodeMix")
        n.data_type = "FLOAT"
        L.new(f, n.inputs["Factor"])
        L.new(a, n.inputs["A"]) if not isinstance(a, float) else n.inputs["A"].__setattr__("default_value", a)
        L.new(b, n.inputs["B"]) if not isinstance(b, float) else n.inputs["B"].__setattr__("default_value", b)
        return n.outputs["Result"]

    rug = mezcla(pico, r_plumas, r_pico)
    rug = mezcla(patas, rug, RUG_PATAS)
    rug = mezcla(ojo, rug, RUG_OJO)

    no_plumas = math("MAXIMUM", math("MAXIMUM", pico, patas), ojo)
    plumas = math("SUBTRACT", 1.0, no_plumas)

    rgb = N.new("ShaderNodeCombineColor")
    L.new(rug, rgb.inputs[0])
    L.new(plumas, rgb.inputs[1])
    L.new(geo.outputs["Pointiness"], rgb.inputs[2])
    em = N.new("ShaderNodeEmission")
    L.new(rgb.outputs[0], em.inputs["Color"])
    out = N.new("ShaderNodeOutputMaterial")
    L.new(em.outputs[0], out.inputs["Surface"])
    return mat


def _nodo_destino(img):
    mat = bpy.data.materials[MATERIAL]
    nt = mat.node_tree
    n = nt.nodes.get("BAKE_DESTINO") or nt.nodes.new("ShaderNodeTexImage")
    n.name = n.label = "BAKE_DESTINO"
    n.image = img
    n.location = (-900, -600)
    for m in nt.nodes:
        m.select = False
    n.select = True
    nt.nodes.active = n
    return n


def hornear(tipo):
    """tipo: NORMAL, NORMAL_CORTO, AO o MASCARAS. Guarda %TEMP%/bakes_gaviotin/<tipo>.npy."""
    t0 = time.time()
    sc = bpy.context.scene
    alta, baja = bpy.data.objects[ALTA], bpy.data.objects[BAJA]
    if bpy.context.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")

    estado = dict(engine=sc.render.engine, samples=sc.cycles.samples,
                  alta_hv=alta.hide_viewport, alta_h=alta.hide_get(), baja_h=baja.hide_get())
    espejo = baja.modifiers.get("Mirror")
    espejo_estado = espejo.mirror_offset_u if espejo else None

    img = _imagen("BAKE_" + tipo)
    destino = _nodo_destino(img)
    mat_tmp = None
    try:
        sc.render.engine = "CYCLES"
        sc.cycles.device = "CPU"
        alta.hide_viewport = False
        alta.hide_set(False)
        baja.hide_set(False)
        if espejo:
            # El Mirror queda encendido (si se apaga, las normales de la costura
            # central cambian y el espacio tangente no coincide con el modelo final);
            # la mitad espejada se corre a u+1, fuera de la imagen, y no escribe.
            espejo.mirror_offset_u = 1.0

        if tipo == "MASCARAS":
            mat_tmp = _material_mascaras()
            alta.data.materials.clear()
            alta.data.materials.append(mat_tmp)
        if tipo == "AO" and sc.world:
            sc.world.light_settings.distance = DIST_AO

        sc.cycles.samples = {"NORMAL": 8, "NORMAL_CORTO": 8, "AO": 48, "MASCARAS": 4}[tipo]
        bpy.ops.object.select_all(action="DESELECT")
        alta.select_set(True)
        baja.select_set(True)
        bpy.context.view_layer.objects.active = baja

        corto = tipo == "NORMAL_CORTO"
        bpy.ops.object.bake(
            type={"NORMAL": "NORMAL", "NORMAL_CORTO": "NORMAL", "AO": "AO", "MASCARAS": "EMIT"}[tipo],
            normal_space="TANGENT",
            use_selected_to_active=True,
            cage_extrusion=EXTRUSION_CORTA if corto else EXTRUSION,
            max_ray_distance=MAX_RAYO_CORTO if corto else MAX_RAYO,
            margin=16,
            margin_type="EXTEND",
            use_clear=True,
            target="IMAGE_TEXTURES",
        )
        px = np.empty(RES * RES * 4, dtype=np.float32)
        img.pixels.foreach_get(px)
        np.save(os.path.join(_bakes(), tipo + ".npy"), px.reshape(RES, RES, 4))
    finally:
        if mat_tmp:
            alta.data.materials.clear()
            bpy.data.materials.remove(mat_tmp)
        if espejo:
            espejo.mirror_offset_u = espejo_estado
        alta.select_set(False)
        alta.hide_viewport = estado["alta_hv"]
        alta.hide_set(estado["alta_h"])
        baja.hide_set(estado["baja_h"])
        sc.render.engine = estado["engine"]
        sc.cycles.samples = estado["samples"]
        bpy.data.materials[MATERIAL].node_tree.nodes.remove(destino)
    return round(time.time() - t0, 1)


def _png(ruta, a, bits):
    """PNG escrito a mano: sin la transformacion de vista de la escena (save_render la aplica)."""
    import struct
    import zlib
    a = np.flipud(np.clip(a, 0.0, 1.0))  # Blender guarda de abajo hacia arriba
    if a.ndim == 2:
        a = a[..., None]
    h, w, c = a.shape
    if bits == 16:
        datos = np.round(a * 65535).astype(">u2")
    else:
        datos = np.round(a * 255).astype(np.uint8)
    filas = np.concatenate([np.zeros((h, 1), np.uint8), datos.reshape(h, -1).view(np.uint8)], axis=1)

    def trozo(tipo, d):
        return struct.pack(">I", len(d)) + tipo + d + struct.pack(">I", zlib.crc32(tipo + d) & 0xFFFFFFFF)

    tipo_color = {1: 0, 3: 2, 4: 6}[c]
    with open(ruta, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(trozo(b"IHDR", struct.pack(">IIBBBBB", w, h, bits, tipo_color, 0, 0, 0)))
        f.write(trozo(b"IDAT", zlib.compress(filas.tobytes(), 6)))
        f.write(trozo(b"IEND", b""))


def _guardar(nombre, rgba, colorspace="Non-Color", bits="8", modo="RGB"):
    """Escribe el PNG y lo recarga en la imagen existente (los nodos que la usan siguen conectados)."""
    ruta = os.path.join(_carpeta(), nombre + ".png")
    canales = {"BW": rgba[..., 0], "RGB": rgba[..., :3], "RGBA": rgba}[modo]
    _png(ruta, canales, int(bits))
    img = bpy.data.images.get(nombre)
    if img is None:
        img = bpy.data.images.load(ruta, check_existing=False)
        img.name = nombre
    img.filepath = bpy.path.relpath(ruta)
    img.reload()
    img.colorspace_settings.name = colorspace
    return img


def _desenfoque(a, r):
    # caja separable, suficiente para suavizar la cavidad
    k = 2 * r + 1
    for eje in (0, 1):
        c = np.cumsum(np.pad(a, [(r + 1, r) if i == eje else (0, 0) for i in range(2)], mode="edge"), axis=eje)
        a = (np.take(c, range(k, c.shape[eje]), axis=eje) - np.take(c, range(0, c.shape[eje] - k), axis=eje)) / k
    return a


def _mascara_caras(caras):
    """1 en los pixeles UV de esas caras del low poly."""
    me = bpy.data.objects[BAJA].data
    uv = me.uv_layers["UVMap"].data
    m = np.zeros((RES, RES), dtype=np.float32)
    for fi in caras:
        f = me.polygons[fi]
        pts = np.array([uv[i].uv[:] for i in f.loop_indices]) * RES - 0.5
        for i in range(1, len(pts) - 1):
            a, b, c = pts[0], pts[i], pts[i + 1]
            x0, y0 = np.floor(np.minimum(np.minimum(a, b), c)).astype(int)
            x1, y1 = np.ceil(np.maximum(np.maximum(a, b), c)).astype(int)
            xs, ys = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
            det = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
            if abs(det) < 1e-12:
                continue
            l1 = ((b[1] - c[1]) * (xs - c[0]) + (c[0] - b[0]) * (ys - c[1])) / det
            l2 = ((c[1] - a[1]) * (xs - c[0]) + (a[0] - c[0]) * (ys - c[1])) / det
            dentro = (l1 >= -0.01) & (l2 >= -0.01) & (l1 + l2 <= 1.01)
            xs, ys = xs[dentro], ys[dentro]
            ok = (xs >= 0) & (xs < RES) & (ys >= 0) & (ys < RES)
            m[ys[ok], xs[ok]] = 1.0
    return m


def _normal_corregida(nor, corto):
    """Repara los pixeles donde la jaula larga atraveso plumas superpuestas, y aplana la boca."""
    # Mas de ~53 grados respecto del low poly: artefacto (en este modelo no hay relieve asi).
    malo = (nor[..., 2] < 0.6).astype(np.float32)
    malo = np.clip(_desenfoque(malo, 4) * 4.0, 0.0, 1.0)  # agranda y suaviza el borde
    nor = nor * (1.0 - malo[..., None]) + corto * malo[..., None]
    boca = np.clip(_desenfoque(_mascara_caras(CARAS_BOCA), 3) * 3.0, 0.0, 1.0)[..., None]
    plana = np.array([0.5, 0.5, 1.0, 1.0], dtype=np.float32)
    nor = nor * (1.0 - boca) + plana * boca
    # lo que siga roto (casi todo margen entre islas) queda plano
    nor[nor[..., 2] < 0.6] = plana
    # re-normaliza el vector codificado
    v = nor[..., :3] * 2.0 - 1.0
    v /= np.linalg.norm(v, axis=-1, keepdims=True) + 1e-8
    nor[..., :3] = v * 0.5 + 0.5
    return nor, float((malo > 0.5).mean())


def componer():
    """Rugosidad = base por region + cavidades (AO y pointiness), y las demas texturas."""
    b = _bakes()
    nor, reparado = _normal_corregida(np.load(os.path.join(b, "NORMAL.npy")),
                                      np.load(os.path.join(b, "NORMAL_CORTO.npy")))
    ao = np.load(os.path.join(b, "AO.npy"))[..., 0]
    mas = np.load(os.path.join(b, "MASCARAS.npy"))
    base, plumas, punt = mas[..., 0], mas[..., 1], mas[..., 2]

    # pointiness: convexo > 0, concavo < 0, normalizado por percentiles
    valido = base > 0.01
    p = punt - _desenfoque(punt, 24)
    esc = np.percentile(np.abs(p[valido]), 95) + 1e-6
    p = np.clip(p / esc, -1.0, 1.0)

    no_ojo = np.clip((base - RUG_OJO) / 0.1, 0.0, 1.0)  # el ojo queda liso y sin suciedad
    cav = np.clip(-p, 0, 1)
    borde = np.clip(p, 0, 1)
    rug = (base
           + no_ojo * 0.28 * (1.0 - ao)          # entre plumas: polvo, aire, sombra -> mas aspero
           + no_ojo * 0.10 * cav
           - plumas * 0.08 * borde)              # raquis y bordes de pluma un poco mas pulidos
    rug = np.clip(rug, 0.03, 0.95)

    uno = np.ones((RES, RES), dtype=np.float32)
    gris = lambda a: np.dstack([a, a, a, uno])

    out = {}
    # El BaseColor solo se crea la primera vez: despues es la pintura del usuario.
    if not os.path.exists(os.path.join(_carpeta(), "T_Gabiotin_BaseColor.png")):
        out["base"] = _guardar("T_Gabiotin_BaseColor", gris(uno), colorspace="sRGB")
    nor[..., 3] = 1.0
    out["normal"] = _guardar("T_Gabiotin_Normal", nor, bits="16")
    out["rug"] = _guardar("T_Gabiotin_Roughness", gris(rug), modo="BW")
    out["ao"] = _guardar("T_Gabiotin_AO", gris(ao), modo="BW")
    cero = np.zeros_like(rug)
    out["ms"] = _guardar("T_Gabiotin_MetalSmooth", np.dstack([cero, cero, cero, 1.0 - rug]), modo="RGBA")

    stats = {k: [round(float(np.percentile(v[valido], q)), 3) for q in (5, 50, 95)]
             for k, v in (("rug", rug), ("ao", ao), ("base", base))}
    stats["normal_reparado_%"] = round(reparado * 100, 3)
    return stats


def armar_material():
    """Conecta las texturas en M_Gabiotin. La grilla UV queda desconectada como respaldo."""
    mat = bpy.data.materials[MATERIAL]
    nt = mat.node_tree
    N, L = nt.nodes, nt.links
    bsdf = next(n for n in N if n.type == "BSDF_PRINCIPLED")
    for n in [n for n in N if n.name.startswith("GAB_")]:
        N.remove(n)
    for l in list(bsdf.inputs["Base Color"].links):
        L.remove(l)
    grilla = next((n for n in N if n.type == "TEX_IMAGE" and n.image and n.image.name == "T_Gabiotin"), None)
    if grilla:
        grilla.label = "Grilla UV (respaldo)"
        grilla.location = (-900, 500)

    def tex(nombre, img, y):
        n = N.new("ShaderNodeTexImage")
        n.name = nombre
        n.label = img.name
        n.image = bpy.data.images[img.name]
        n.location = (-700, y)
        n.width = 260
        return n

    uv = N.new("ShaderNodeUVMap"); uv.name = "GAB_UV"; uv.uv_map = "UVMap"; uv.location = (-1000, 0)
    t_base = tex("GAB_BaseColor", bpy.data.images["T_Gabiotin_BaseColor"], 300)
    t_rug = tex("GAB_Roughness", bpy.data.images["T_Gabiotin_Roughness"], 0)
    t_nor = tex("GAB_Normal", bpy.data.images["T_Gabiotin_Normal"], -300)
    t_ao = tex("GAB_AO", bpy.data.images["T_Gabiotin_AO"], -600)
    t_ao.label = "T_Gabiotin_AO (para Unity)"
    for t in (t_base, t_rug, t_nor, t_ao):
        L.new(uv.outputs["UV"], t.inputs["Vector"])
    nm = N.new("ShaderNodeNormalMap"); nm.name = "GAB_NormalMap"; nm.uv_map = "UVMap"; nm.location = (-350, -300)

    L.new(t_base.outputs["Color"], bsdf.inputs["Base Color"])
    L.new(t_rug.outputs["Color"], bsdf.inputs["Roughness"])
    L.new(t_nor.outputs["Color"], nm.inputs["Color"])
    L.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    bsdf.inputs["Metallic"].default_value = 0.0
    bsdf.location = (0, 200)
    for n in N:
        n.select = False
    nt.nodes.active = t_base  # para que Texture Paint pinte sobre el BaseColor
    bpy.ops.ed.undo_push(message="Material PBR gaviotin")
    return [n.name for n in N]
