# Madera gastada de pasarela (Blender, via exec). Genera texturas tileables con numpy,
# una capa UV "UV_Madera" por caja (veta a lo largo del eje mas largo, envuelve el perimetro)
# y el material M_Madera_Pasarela.
# Uso: exec(open(ruta).read(), {"OBJ": "Cube", "OUT": carpeta_texturas})
# Textura: W x H texeles = TU x TV unidades (U = veta). Todo periodico (FFT), sin costuras.
import bpy, bmesh, os, random
import numpy as np

OBJ = globals().get("OBJ", "Cube")
OUT = globals().get("OUT", "//Texturas")
SEED = globals().get("SEED", 7)
W, H = globals().get("W", 4096), globals().get("H", 1024)
TU, TV = globals().get("TU", 4.4), globals().get("TV", 1.1)
RINGS = globals().get("RINGS", 18)            # anillos por tile en V
GEN_TEX = globals().get("GEN_TEX", True)
NORMAL_K = globals().get("NORMAL_K", 9.0)     # pendiente por unidad de altura/texel
MAT = "M_Madera_Pasarela"
UVNAME = "UV_Madera"

rng = np.random.default_rng(SEED)
ku = np.fft.rfftfreq(W) * W                   # ciclos por tile en U
kv = np.fft.fftfreq(H) * H                    # ciclos por tile en V
KU, KV = np.meshgrid(ku, kv)

def fnoise(fu, fv, seed=None):
    r = np.random.default_rng(seed if seed is not None else rng.integers(1 << 30))
    F = np.fft.rfft2(r.standard_normal((H, W)))
    F *= np.exp(-((KU / fu) ** 2 + (KV / fv) ** 2))
    n = np.fft.irfft2(F, s=(H, W))
    return (n - n.mean()) / (n.std() + 1e-9)

def blur(a, px):
    g = np.exp(-0.5 * ((KU / W * 2 * np.pi * px) ** 2 + (KV / H * 2 * np.pi * px) ** 2))
    return np.fft.irfft2(np.fft.rfft2(a) * g, s=(H, W))

def sstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)

def mix(a, b, t):
    return a + (b - a) * t[..., None]

def save(name, rgb, noncolor):
    path = bpy.path.abspath(os.path.join(OUT, name + ".png"))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img = bpy.data.images.get(name) or bpy.data.images.new(name, W, H, alpha=False)
    if img.size[0] != W or img.size[1] != H: img.scale(W, H)
    px = np.ones((H, W, 4), np.float32)
    px[..., :3] = np.clip(rgb, 0, 1)
    img.colorspace_settings.name = 'Non-Color' if noncolor else 'sRGB'
    img.pixels.foreach_set(px.ravel())
    img.filepath_raw = path; img.file_format = 'PNG'; img.save()
    img.filepath = bpy.path.relpath(path) if bpy.data.filepath else path
    img.reload()
    return img

# ---------------- texturas ----------------
if GEN_TEX:
    v = (np.arange(H)[:, None] + 0.5) / H * np.ones((1, W))
    u = (np.arange(W)[None, :] + 0.5) / W * np.ones((H, 1))
    # fase de anillos: bandas a lo largo de U, onduladas (veta "catedral" suave)
    D = 1.5 * fnoise(1.6, 1.5) + 0.22 * fnoise(5, 4) + 0.05 * fnoise(30, 20)
    # nudos: anillos que rodean el nudo + nucleo oscuro
    knot_core = np.zeros((H, W)); knot_halo = np.zeros((H, W))
    for _ in range(4):
        u0, v0 = rng.random(), rng.random()
        rk = rng.uniform(0.012, 0.03)                          # radio en unidades
        du = (np.mod(u - u0 + 0.5, 1) - 0.5) * TU / 1.9        # alargado en la veta
        dv = (np.mod(v - v0 + 0.5, 1) - 0.5) * TV
        d = np.sqrt(du ** 2 + dv ** 2)
        D += 3.5 * np.exp(-(d / (3.5 * rk)) ** 2)
        knot_core = np.maximum(knot_core, sstep(1.0, 0.75, d / rk))
        knot_halo = np.maximum(knot_halo, np.exp(-(d / (2.2 * rk)) ** 2))
    P = v * RINGS + D
    r = np.mod(P, 1.0)
    late = sstep(0.42, 0.86, r) * sstep(1.0, 0.985, r) + sstep(0.012, 0.0, r)   # leño tardío (denso)
    fib = 0.6 * fnoise(70, 260) + 0.4 * fnoise(160, 520)                        # fibras largas
    pores = sstep(1.6, 2.4, fnoise(500, 700))                                    # poros/rayitas finas
    wear = sstep(-0.3, 1.1, fnoise(3, 2.5))                                      # zonas pulidas por pisadas
    grime = sstep(-0.8, 1.2, fnoise(6, 4))                                       # suciedad / humedad
    grey = sstep(-1.0, 1.0, fnoise(4, 3))                                        # agrisado por intemperie

    # grietas (fendas) siguiendo el limite de anillos
    crack = np.zeros((H, W))
    Dsm = blur(D, 2.0)
    for _ in range(45):
        Lpx = int(rng.uniform(0.12, 1.4) / TU * W)
        c0, r0 = rng.integers(W), rng.integers(H)
        cols = (c0 + np.arange(Lpx)) % W
        p0 = np.floor(P[r0, c0]) + rng.uniform(-0.04, 0.04)
        vc = np.full(Lpx, (r0 + 0.5) / H)
        for _it in range(3):
            rows = np.round(vc * H - 0.5).astype(int) % H
            vc_new = (p0 - Dsm[rows, cols]) / RINGS
            vc = vc + ((vc_new - vc + 0.5) % 1.0 - 0.5)
        t = np.linspace(0, 1, Lpx)
        wpx = rng.uniform(1.8, 5.5) * np.sin(np.pi * t) ** 0.6 + 0.3
        wpx *= 1 + 0.35 * np.sin(t * rng.uniform(8, 30) + rng.uniform(0, 6))
        ctr = vc * H - 0.5
        offs = np.arange(-8, 9)
        rr = np.round(ctr)[:, None] + offs[None, :]
        dist = np.abs(rr - ctr[:, None])
        val = np.clip(1 - dist / wpx[:, None], 0, 1) ** 0.7
        R = rr.astype(int) % H; C = np.repeat(cols[:, None], len(offs), 1)
        np.maximum.at(crack, (R.ravel(), C.ravel()), val.ravel())
    # grietas radiales en los nudos
    crack = np.maximum(crack, knot_core * sstep(1.2, 2.2, fnoise(30, 60)))

    # altura: el leño temprano se erosiona, el tardío queda en relieve
    h = 0.55 * late + 0.10 * fib - 0.06 * pores
    h = h + 0.12 * knot_halo + 0.08 * knot_core
    hs = blur(h, 3.0)
    h = h + (hs - h) * 0.65 * wear                     # pisadas suavizan el relieve
    h = blur(h, 0.9)
    h = h - 0.9 * blur(crack, 0.6)
    # cavidad -> AO
    cav = np.clip(blur(h, 6.0) - h, 0, None)
    ao = np.clip(1 - 2.2 * cav - 0.55 * crack, 0.25, 1)

    # color (sRGB): cafe oscuro poco saturado, agrisado, sucio en grietas
    early = np.array([0.335, 0.270, 0.215]); latec = np.array([0.225, 0.175, 0.135])
    greyc = np.array([0.33, 0.305, 0.275]); dirt = np.array([0.15, 0.14, 0.11])
    mossc = np.array([0.20, 0.215, 0.165]); crackc = np.array([0.075, 0.062, 0.05])
    col = mix(early[None, None, :] * np.ones((H, W, 3)), latec, late)
    col *= (1 + 0.07 * fib - 0.12 * pores)[..., None]
    col = mix(col, greyc * (1 - 0.15 * late[..., None]), 0.22 * grey + 0.06)
    col = mix(col, col * 1.12 + 0.015, 0.35 * wear)                              # pulido, mas claro
    col = mix(col, dirt, 0.28 * grime * (1 - wear))
    col = mix(col, latec * 0.6, 0.85 * knot_core + 0.25 * knot_halo)
    col = mix(col, mossc, np.clip(0.9 * cav * grime * 6, 0, 0.35))
    col = mix(col, crackc, np.clip(crack * 1.2, 0, 1))
    col *= (0.55 + 0.45 * ao)[..., None]

    rough = 0.80 + 0.06 * (1 - late) - 0.20 * wear + 0.08 * grime * (1 - wear) + 0.1 * crack + 0.03 * fib
    rough = np.clip(rough, 0.45, 1.0)

    # normal (OpenGL, Y+ = V+; igual que Blender y Unity)
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5
    n = np.dstack([-NORMAL_K * gx, -NORMAL_K * gy, np.ones_like(h)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)

    hn = (h - h.min()) / (np.ptp(h) + 1e-9)
    imgs = {
        "base": save("Madera_Pasarela_BaseColor", col, False),
        "nrm": save("Madera_Pasarela_Normal", n * 0.5 + 0.5, True),
        "rgh": save("Madera_Pasarela_Roughness", np.repeat(rough[..., None], 3, 2), True),
        "ao": save("Madera_Pasarela_AO", np.repeat(ao[..., None], 3, 2), True),
        "hgt": save("Madera_Pasarela_Height", np.repeat(hn[..., None], 3, 2), True),
    }
else:
    imgs = {k: bpy.data.images[n] for k, n in (("base", "Madera_Pasarela_BaseColor"), ("nrm", "Madera_Pasarela_Normal"),
            ("rgh", "Madera_Pasarela_Roughness"), ("ao", "Madera_Pasarela_AO"), ("hgt", "Madera_Pasarela_Height"))}

# ---------------- UVs por caja ----------------
obj = bpy.data.objects[OBJ]
if obj.mode != 'OBJECT':
    bpy.context.view_layer.objects.active = obj; bpy.ops.object.mode_set(mode='OBJECT')
me = obj.data
bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
uvl = bm.loops.layers.uv.get(UVNAME) or bm.loops.layers.uv.new(UVNAME)
# auxiliares para la humedad: UV_Borde = (posicion a lo ancho de la cara, ancho), UV_Largo = (posicion a lo largo, largo)
uvb = bm.loops.layers.uv.get("UV_Borde") or bm.loops.layers.uv.new("UV_Borde")
uvg = bm.loops.layers.uv.get("UV_Largo") or bm.loops.layers.uv.new("UV_Largo")
var = bm.faces.layers.float.get("madera_var") or bm.faces.layers.float.new("madera_var")
var2 = bm.faces.layers.float.get("madera_var2") or bm.faces.layers.float.new("madera_var2")
pr = random.Random(SEED)
seen = set()
for f0 in bm.faces:
    if f0.index in seen: continue
    comp, stack = [], [f0]
    while stack:
        g = stack.pop()
        if g.index in seen: continue
        seen.add(g.index); comp.append(g)
        stack += [h for e in g.edges for h in e.link_faces if h.index not in seen]
    V = np.array([v.co[:] for g in comp for v in g.verts])
    lo, hi = V.min(0), V.max(0); ctr = (lo + hi) / 2; dim = hi - lo
    L = int(np.argmax(dim))
    others = [i for i in range(3) if i != L]
    b = 2 if L != 2 else 1                              # eje "vertical" de la seccion
    a = [i for i in others if i != b][0]
    A, B = dim[a], dim[b]
    ou, ov = pr.random(), pr.random(); fv = pr.random(); fv2 = pr.random()
    flip = pr.random() < 0.5
    for g in comp:
        nrm = np.array(g.normal[:]); ax = int(np.argmax(np.abs(nrm))); sg = np.sign(nrm[ax])
        g[var] = fv; g[var2] = fv2
        for lp in g.loops:
            p = np.array(lp.vert.co[:]) - ctr
            x = -p[L] if flip else p[L]
            if ax == L:                                 # testa: proyeccion simple
                U, Vv = (p[a] + A / 2) / TU, (p[b] + B / 2) / TV
            else:                                       # perimetro continuo, costura abajo en (+a,-b)
                if ax == a and sg > 0:   s = p[b] + B / 2
                elif ax == b and sg > 0: s = B + (A / 2 - p[a])
                elif ax == a:            s = B + A + (B / 2 - p[b])
                else:                    s = 2 * B + A + (p[a] + A / 2)
                U, Vv = (x + dim[L] / 2) / TU, s / TV
            lp[uvl].uv = (U + ou, Vv + ov)
            if ax == L:   lp[uvb].uv = (p[a] + A / 2, A); lp[uvg].uv = (0.0, dim[L])
            elif ax == a: lp[uvb].uv = (p[b] + B / 2, B); lp[uvg].uv = (p[L] + dim[L] / 2, dim[L])
            else:         lp[uvb].uv = (p[a] + A / 2, A); lp[uvg].uv = (p[L] + dim[L] / 2, dim[L])
bm.to_mesh(me); bm.free()
me.uv_layers[UVNAME].active = True
me.uv_layers[UVNAME].active_render = True

# ---------------- material ----------------
mat = bpy.data.materials.get(MAT) or bpy.data.materials.new(MAT)
mat.use_nodes = True
nt = mat.node_tree; nt.nodes.clear()
N_ = nt.nodes.new; Lk = nt.links.new
out = N_("ShaderNodeOutputMaterial"); out.location = (900, 0)
bsdf = N_("ShaderNodeBsdfPrincipled"); bsdf.location = (550, 0)
uvn = N_("ShaderNodeUVMap"); uvn.uv_map = UVNAME; uvn.location = (-1100, 0)
mp = N_("ShaderNodeMapping"); mp.location = (-900, 0); mp.label = "Escala de la veta"
Lk(uvn.outputs["UV"], mp.inputs["Vector"])
def tex(img, y, label):
    t = N_("ShaderNodeTexImage"); t.image = img; t.location = (-600, y); t.label = label
    t.interpolation = 'Linear'; t.extension = 'REPEAT'
    Lk(mp.outputs["Vector"], t.inputs["Vector"]); return t
tb = tex(imgs["base"], 300, "BaseColor"); tr = tex(imgs["rgh"], 0, "Roughness")
tn = tex(imgs["nrm"], -300, "Normal"); ta = tex(imgs["ao"], 600, "AO")
attr = N_("ShaderNodeAttribute"); attr.attribute_type = 'GEOMETRY'; attr.attribute_name = "madera_var"
attr.location = (-600, 850); attr.label = "Variacion por pieza"
# variacion por pieza: valor (madera_var) y saturacion (madera_var2) independientes, leve corrimiento de tono
mr = N_("ShaderNodeMapRange"); mr.location = (-350, 850)
mr.inputs["To Min"].default_value = 0.80; mr.inputs["To Max"].default_value = 1.10
Lk(attr.outputs["Fac"], mr.inputs["Value"])
hr = N_("ShaderNodeMapRange"); hr.location = (-350, 650)
hr.inputs["To Min"].default_value = 0.485; hr.inputs["To Max"].default_value = 0.512
Lk(attr.outputs["Fac"], hr.inputs["Value"])
hsv = N_("ShaderNodeHueSaturation"); hsv.location = (-100, 400)
attr2 = N_("ShaderNodeAttribute"); attr2.attribute_type = 'GEOMETRY'; attr2.attribute_name = "madera_var2"
attr2.location = (-600, 1050); attr2.label = "Variacion por pieza 2"
sr = N_("ShaderNodeMapRange"); sr.location = (-350, 1050)
sr.inputs["To Min"].default_value = 0.65; sr.inputs["To Max"].default_value = 1.12
Lk(attr2.outputs["Fac"], sr.inputs["Value"]); Lk(sr.outputs["Result"], hsv.inputs["Saturation"])
Lk(tb.outputs["Color"], hsv.inputs["Color"]); Lk(mr.outputs["Result"], hsv.inputs["Value"])
Lk(hr.outputs["Result"], hsv.inputs["Hue"])
aom = N_("ShaderNodeMix"); aom.data_type = 'RGBA'; aom.blend_type = 'MULTIPLY'; aom.location = (150, 450)
aom.inputs["Factor"].default_value = 0.5; aom.label = "AO extra"
Lk(hsv.outputs["Color"], aom.inputs["A"]); Lk(ta.outputs["Color"], aom.inputs["B"])
Lk(aom.outputs["Result"], bsdf.inputs["Base Color"])
Lk(tr.outputs["Color"], bsdf.inputs["Roughness"])
nm = N_("ShaderNodeNormalMap"); nm.location = (150, -300); nm.uv_map = UVNAME
nm.inputs["Strength"].default_value = 1.0
Lk(tn.outputs["Color"], nm.inputs["Color"]); Lk(nm.outputs["Normal"], bsdf.inputs["Normal"])
bsdf.inputs["Specular IOR Level"].default_value = 0.4
Lk(bsdf.outputs["BSDF"], out.inputs["Surface"])
# ---------------- humedad y musgo (procedural, en coordenadas de objeto: no se repite con el tile) ----------------
# Humedad: manchas grandes + cantos y puntas (la testa chupa agua) + pie de los pilotes + grietas de la textura.
# Musgo: solo dentro de lo humedo, en cantos/grietas y abajo; poco y cortado por ruido fino.
HUMEDAD = globals().get("HUMEDAD", True)
if HUMEDAD:
    def sk(node, name, out=False):              # socket habilitado por nombre (los Mix repiten nombres)
        return [x for x in (node.outputs if out else node.inputs) if x.name == name and x.enabled][0]
    def mth(op, a, b=None, x=0, y=0, clamp=False):
        m = N_("ShaderNodeMath"); m.operation = op; m.location = (x, y); m.use_clamp = clamp
        for i, v_ in enumerate((a, b)):
            if v_ is None: continue
            if isinstance(v_, (int, float)): m.inputs[i].default_value = v_
            else: Lk(v_, m.inputs[i])
        return m.outputs[0]
    def mad(a, b, c, x=0, y=0):                 # a * b + c
        m = mth('MULTIPLY_ADD', a, b, x, y).node
        if isinstance(c, (int, float)): m.inputs[2].default_value = c
        else: Lk(c, m.inputs[2])
        return m.outputs[0]
    def rango(val, f0, f1, t0=0.0, t1=1.0, x=0, y=0):
        r_ = N_("ShaderNodeMapRange"); r_.interpolation_type = 'SMOOTHSTEP'; r_.location = (x, y)
        r_.inputs["From Min"].default_value = f0; r_.inputs["From Max"].default_value = f1
        r_.inputs["To Min"].default_value = t0; r_.inputs["To Max"].default_value = t1
        Lk(val, r_.inputs["Value"]); return r_.outputs["Result"]
    def ruido(scale, detail, rough, x, y, w=0.0):
        n_ = N_("ShaderNodeTexNoise"); n_.location = (x, y); n_.noise_dimensions = '4D'
        n_.inputs["Scale"].default_value = scale; n_.inputs["Detail"].default_value = detail
        n_.inputs["Roughness"].default_value = rough; n_.inputs["W"].default_value = w
        Lk(tco.outputs["Object"], n_.inputs["Vector"]); return n_.outputs["Fac"]
    X0, Y0 = -1800, -900
    tco = N_("ShaderNodeTexCoord"); tco.location = (X0 - 300, Y0)
    geo = N_("ShaderNodeNewGeometry"); geo.location = (X0 - 300, Y0 - 300)
    # distancia al canto mas cercano (a lo ancho) y a la punta de la pieza
    ub = N_("ShaderNodeUVMap"); ub.uv_map = "UV_Borde"; ub.location = (X0, Y0 + 300)
    ul = N_("ShaderNodeUVMap"); ul.uv_map = "UV_Largo"; ul.location = (X0, Y0 + 150)
    sb = N_("ShaderNodeSeparateXYZ"); sb.location = (X0 + 200, Y0 + 300); Lk(ub.outputs["UV"], sb.inputs[0])
    sl = N_("ShaderNodeSeparateXYZ"); sl.location = (X0 + 200, Y0 + 150); Lk(ul.outputs["UV"], sl.inputs[0])
    d_canto = mth('MINIMUM', sb.outputs["X"], mth('SUBTRACT', sb.outputs["Y"], sb.outputs["X"], X0 + 400, Y0 + 350), X0 + 600, Y0 + 300)
    d_punta = mth('MINIMUM', sl.outputs["X"], mth('SUBTRACT', sl.outputs["Y"], sl.outputs["X"], X0 + 400, Y0 + 150), X0 + 600, Y0 + 150)
    canto = rango(d_canto, 0.0, 0.16, 1.0, 0.0, X0 + 800, Y0 + 300)
    punta = rango(d_punta, 0.0, 0.45, 1.0, 0.0, X0 + 800, Y0 + 150)
    # pie de los pilotes (zona de salpicadura), con borde irregular
    so = N_("ShaderNodeSeparateXYZ"); so.location = (X0, Y0 - 100); Lk(tco.outputs["Object"], so.inputs[0])
    zr = mth('ADD', so.outputs["Z"], mth('MULTIPLY', mth('SUBTRACT', ruido(1.5, 3, 0.5, X0 + 200, Y0 - 250, 3.0), 0.5, X0 + 400, Y0 - 250), 0.7, X0 + 600, Y0 - 250), X0 + 800, Y0 - 100)
    bajo = rango(zr, -2.3, -1.2, 1.0, 0.0, X0 + 1000, Y0 - 100)
    # grietas/surcos de la textura (AO) y cara hacia arriba (el agua se queda)
    cav = rango(sk(ta, "Color", True), 0.5, 0.97, 1.0, 0.0, X0 + 1000, Y0 + 500)
    sn = N_("ShaderNodeSeparateXYZ"); sn.location = (X0, Y0 - 450); Lk(geo.outputs["Normal"], sn.inputs[0])
    arriba = rango(sn.outputs["Z"], 0.4, 0.9, 0.0, 1.0, X0 + 200, Y0 - 450)
    manchas = ruido(0.55, 4, 0.55, X0 + 200, Y0 - 650, 0.0)
    # humedad
    h1 = mad(canto, 0.35, mth('MULTIPLY', punta, 0.45, X0 + 1000, Y0 + 150), X0 + 1200, Y0 + 250)
    h2 = mad(bajo, 0.9, h1, X0 + 1400, Y0 + 100)
    h3 = mad(mth('MULTIPLY', cav, arriba, X0 + 1200, Y0 + 500), 0.3, h2, X0 + 1600, Y0 + 200)
    h4 = mad(mth('SUBTRACT', manchas, 0.5, X0 + 400, Y0 - 650), 1.5, h3, X0 + 1800, Y0 + 100)
    wet = rango(h4, 0.28, 0.62, 0.0, 1.0, X0 + 2000, Y0 + 100)
    wet_r = N_("NodeReroute"); wet_r.location = (X0 + 2200, Y0 + 100); wet_r.label = "Humedad"; Lk(wet, wet_r.inputs[0])
    W_ = wet_r.outputs[0]
    # musgo: dentro de lo humedo, en cantos, grietas y abajo
    musg_n = ruido(1.4, 5, 0.6, X0 + 200, Y0 - 850, 7.0)
    fino = ruido(28.0, 4, 0.7, X0 + 200, Y0 - 1050, 11.0)
    m1 = mth('ADD', mth('MAXIMUM', canto, punta, X0 + 1000, Y0 - 300), mth('MULTIPLY', cav, 0.6, X0 + 1200, Y0 - 300), X0 + 1400, Y0 - 300)
    m2 = mad(bajo, 0.3, m1, X0 + 1600, Y0 - 300)
    m3 = mad(mth('SUBTRACT', musg_n, 0.5, X0 + 400, Y0 - 850), 2.2, m2, X0 + 1800, Y0 - 300)
    m4 = mad(mth('SUBTRACT', fino, 0.5, X0 + 400, Y0 - 1050), 0.9, m3, X0 + 2000, Y0 - 300)
    m5 = mth('MULTIPLY', rango(m4, 0.92, 1.22, 0.0, 1.0, X0 + 2200, Y0 - 300), W_, X0 + 2400, Y0 - 300)
    moss_r = N_("NodeReroute"); moss_r.location = (X0 + 2600, Y0 - 300); moss_r.label = "Musgo"; Lk(m5, moss_r.inputs[0])
    M_ = moss_r.outputs[0]
    # color: madera mojada = mas oscura y un poco mas saturada; musgo oliva apagado en dos tonos
    wcol = N_("ShaderNodeMix"); wcol.data_type = 'RGBA'; wcol.blend_type = 'MULTIPLY'; wcol.location = (300, 700)
    wcol.label = "Mojado"; Lk(W_, sk(wcol, "Factor"))
    Lk(sk(aom, "Result", True), sk(wcol, "A")); sk(wcol, "B").default_value = (0.42, 0.38, 0.34, 1)
    cr = N_("ShaderNodeValToRGB"); cr.location = (0, 1000); Lk(fino, cr.inputs["Fac"])
    cr.color_ramp.elements[0].position = 0.35; cr.color_ramp.elements[0].color = (0.045, 0.062, 0.016, 1)
    cr.color_ramp.elements[1].position = 0.70; cr.color_ramp.elements[1].color = (0.115, 0.130, 0.040, 1)
    mcol = N_("ShaderNodeMix"); mcol.data_type = 'RGBA'; mcol.location = (450, 850); mcol.label = "Musgo"
    Lk(M_, sk(mcol, "Factor")); Lk(sk(wcol, "Result", True), sk(mcol, "A")); Lk(cr.outputs["Color"], sk(mcol, "B"))
    Lk(sk(mcol, "Result", True), bsdf.inputs["Base Color"])
    # rugosidad: mojado ~0.42, charquitos en los surcos ~0.24 (mas bajo parece plastico), musgo mate
    r1 = N_("ShaderNodeMix"); r1.data_type = 'FLOAT'; r1.location = (300, 150)
    Lk(mth('MULTIPLY', W_, 0.85, 100, 250), sk(r1, "Factor")); Lk(tr.outputs["Color"], sk(r1, "A")); sk(r1, "B").default_value = 0.42
    r2 = N_("ShaderNodeMix"); r2.data_type = 'FLOAT'; r2.location = (400, 50)
    Lk(mth('MULTIPLY', W_, cav, 200, 100), sk(r2, "Factor")); Lk(sk(r1, "Result", True), sk(r2, "A")); sk(r2, "B").default_value = 0.24
    r3 = N_("ShaderNodeMix"); r3.data_type = 'FLOAT'; r3.location = (450, -50)
    Lk(M_, sk(r3, "Factor")); Lk(sk(r2, "Result", True), sk(r3, "A")); sk(r3, "B").default_value = 0.92
    Lk(sk(r3, "Result", True), bsdf.inputs["Roughness"])
    # relieve: el agua aplana un poco la veta; el musgo suma un bulto esponjoso
    Lk(rango(W_, 0.0, 1.0, 1.0, 0.75, -100, -450), nm.inputs["Strength"])
    bump = N_("ShaderNodeBump"); bump.location = (380, -350); bump.inputs["Distance"].default_value = 0.004
    Lk(mth('MULTIPLY', fino, M_, 200, -500), bump.inputs["Height"]); Lk(M_, bump.inputs["Strength"])
    Lk(nm.outputs["Normal"], bump.inputs["Normal"]); Lk(bump.outputs["Normal"], bsdf.inputs["Normal"])

if not obj.data.materials: obj.data.materials.append(mat)
else:
    for i in range(len(obj.data.materials)): obj.data.materials[i] = mat
# cantos gastados: bisel no destructivo (caras planas siguen planas con Harden Normals)
BEVEL = globals().get("BEVEL", 0.035)
if BEVEL:
    bv = obj.modifiers.get("Bisel_Madera") or obj.modifiers.new("Bisel_Madera", 'BEVEL')
    bv.width = BEVEL; bv.segments = 3; bv.limit_method = 'ANGLE'
    bv.harden_normals = True; bv.miter_outer = 'MITER_ARC'
    for p_ in me.polygons: p_.use_smooth = True
result = {"ok": True, "uv": UVNAME, "mat": MAT}
