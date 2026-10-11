# Arma albedo / normal / rugosidad / AO del huemul a partir de los bakes de
# huemul_bake.py (.npy, filas de abajo hacia arriba). Uso, con python del sistema:
#   python huemul_componer.py <carpeta_bakes> <T_Huemul.png pintada> <carpeta_salida> [fuerza_pelo=0.18]
# Deja en la salida:
#   T_Huemul_Albedo.png      2048, sin costuras, con detalle de pelo/poros/estrias
#   T_Huemul_Normal.png      2048, normal tangente OpenGL (Y+), Blender y Unity
#   T_Huemul_Roughness.png   2048, gris, 1 = aspero (Blender, Painter, etc.)
#   T_Huemul_MetalSmooth.png 2048, URP Lit: R = metal (0), A = smoothness = 1 - rugosidad
#   T_Huemul_AO.png          1024
#
# Las costuras se arreglan como en vicuna_componer.py: cerca de cada borde de isla
# el color se mezcla con un desenfoque hecho en 3D (los dos lados de una costura
# caen en el mismo punto 3D). Los bordes que caen en el plano del Mirror no son
# costuras (el texel es el mismo a ambos lados) y se dejan intactos.
import numpy as np, sys, os
from PIL import Image

D = sys.argv[1].rstrip("/\\") + "/"
SRC = sys.argv[2]
OUT = sys.argv[3].rstrip("/\\") + "/"
FUR = float(sys.argv[4]) if len(sys.argv) > 4 else 0.18
MN = np.array([-0.01, -0.57, -0.01])              # igual que en huemul_bake.py
EXT = np.array([0.32, 2.03, 2.10])
VOX = 0.006                                       # voxel de 6 mm (el huemul mide ~2 m)
SIGMA = 2.0                                       # desenfoque 3D, en voxeles
BORDE = 40                                        # px (a 2048) de mezcla junto a cada costura
OJO, NARIZ = (0.507, 0.230), (0.631, 0.196)       # UV del ojo pintado y de la punta del hocico

flip = lambda a: a[::-1]                          # bottom-up -> top-down
pos = flip(np.load(D + "pos.npy"))
hgt = flip(np.load(D + "height.npy"))[..., 0]
nrm = flip(np.load(D + "normal.npy"))
ao = flip(np.load(D + "ao.npy"))[..., 0]
zon = flip(np.load(D + "zona.npy"))
R = pos.shape[0]
cov = pos[..., :3].sum(-1) > 0
P = pos[..., :3] * EXT + MN                       # posicion en metros
uña = (zon[..., 0] > 0.5) & cov
cuerno = (zon[..., 1] > 0.5) & cov
pelo = cov & ~uña & ~cuerno

def s2l(c): return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
def l2s(c): c = np.clip(c, 0, 1); return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)
def ss(x, a, b): t = np.clip((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)

src = Image.open(SRC).convert("RGB").resize((R, R), Image.LANCZOS)
col = s2l(np.asarray(src, np.float32) / 255.0)

def vecinos(m):
    e = m.copy(); e[1:] |= m[:-1]; e[:-1] |= m[1:]; e[:, 1:] |= m[:, :-1]; e[:, :-1] |= m[:, 1:]; return e

# --- distancia (px) a la costura mas cercana, medida dentro de la isla ---
# El borde que cae en el plano del Mirror no es costura (el texel es el mismo a
# ambos lados): se excluye, asi el hocico y la linea del lomo quedan intactos.
DMAX = 120
espejo = np.abs(P[..., 0]) < 0.004
nucleo = cov.copy()
nucleo[1:] &= cov[:-1]; nucleo[:-1] &= cov[1:]; nucleo[:, 1:] &= cov[:, :-1]; nucleo[:, :-1] &= cov[:, 1:]
frente = cov & ~nucleo & ~espejo
dist = np.full((R, R), DMAX, np.float32)
for i in range(DMAX):
    dist[frente & (dist == DMAX)] = i
    frente = vecinos(frente) & cov & (dist == DMAX)
# ancho de mezcla por isla: nunca mas del 35% de su radio (islas chicas conservan su color)
isla = np.rint(flip(np.load(D + "isla.npy")) * 64).astype(int)
isla[~cov] = 0
rad = np.zeros(isla.max() + 1); np.maximum.at(rad, isla[cov], dist[cov])
BW = np.minimum(BORDE, 0.35 * rad[isla]).clip(4, None)

# --- area de superficie por texel (todas las islas pesan igual) ---
dPu = np.gradient(P, axis=1); dPv = np.gradient(P, axis=0)
area = np.linalg.norm(np.cross(dPu, dPv), axis=-1)
area = np.clip(area, 0, np.percentile(area[cov], 95))
area[~cov] = 0

# --- splat al grid 3D, gauss separable, muestreo trilineal ---
# separado por direccion de la normal: las caras opuestas de laminas finas
# (orejas) no se mezclan; en una costura la normal es la misma a ambos lados
nob = flip(np.load(D + "onormal.npy"))[..., :3] * 2 - 1
nob /= np.maximum(np.linalg.norm(nob, axis=-1, keepdims=True), 1e-6)
r = int(np.ceil(3 * SIGMA)); k = np.exp(-0.5 * (np.arange(-r, r + 1) / SIGMA) ** 2); k /= k.sum()
near = cov & (dist < BW)
blur = col.copy()

def blur3d(sel):
    """Desenfoque 3D del color usando solo los texels de sel (una zona): asi la
    costura entre pelo y pezuña o cuerno no mezcla materiales distintos."""
    q = near & sel
    lo = P[sel].min(0) - (r + 2) * VOX
    G = np.ceil((P[sel].max(0) - lo) / VOX).astype(int) + r + 4
    g = (P - lo) / VOX
    idx = np.clip(np.floor(g + 0.5).astype(int), 0, G - 1)
    lin = np.ravel_multi_index((idx[..., 0][sel], idx[..., 1][sel], idx[..., 2][sel]), G)
    gp = g[q]; p0 = np.floor(gp).astype(int); fr = gp - p0
    num = np.zeros((q.sum(), 3)); den = np.zeros(q.sum())
    for ax in range(3):
        for sg in (1, -1):
            bw = np.maximum(nob[..., ax] * sg, 0) ** 2           # suma 1 sobre los 6 bins
            w = (area * bw)[sel]
            acc = np.zeros((4, np.prod(G)), np.float32)
            for c in range(3): acc[c] = np.bincount(lin, weights=col[..., c][sel] * w, minlength=acc.shape[1])
            acc[3] = np.bincount(lin, weights=w, minlength=acc.shape[1])
            acc = acc.reshape((4, *G))
            for a_ in (1, 2, 3):
                out = np.zeros_like(acc)
                for o, kv in zip(range(-r, r + 1), k): out += kv * np.roll(acc, o, axis=a_)
                acc = out
            sv = np.zeros((len(gp), 4))
            for dx in (0, 1):
                for dy in (0, 1):
                    for dz in (0, 1):
                        i = np.clip(p0 + [dx, dy, dz], 0, G - 1)
                        ww = (fr[:, 0] if dx else 1 - fr[:, 0]) * (fr[:, 1] if dy else 1 - fr[:, 1]) * (fr[:, 2] if dz else 1 - fr[:, 2])
                        sv += ww[:, None] * acc[:, i[:, 0], i[:, 1], i[:, 2]].T
            bt = bw[q]
            num += bt[:, None] * sv[:, :3]; den += bt * sv[:, 3]
            del acc, out
    ok = den > 1e-12
    tmpc = blur[q]; tmpc[ok] = num[ok] / den[ok][:, None]; blur[q] = tmpc

for zona_ in (pelo, uña, cuerno): blur3d(zona_)
t = ss(1 - dist / BW, 0, 1)
alb = col * (1 - t[..., None]) + blur * t[..., None]

# --- cuerno: las islas son tubos con V a lo largo; un desenfoque vertical dentro
# de la isla borra las pinceladas horizontales y deja solo el tono, para que las
# lineas sean las estrias longitudinales del relieve ---
def suavizar_v(img, m, rad=10):
    s_ = np.zeros_like(img); n_ = np.zeros(m.shape, np.float32)
    for dy in range(-rad, rad + 1):
        w_ = np.exp(-0.5 * (dy / (rad / 2)) ** 2)
        mm = np.roll(m, dy, 0) & m
        s_ += w_ * np.roll(img, dy, 0) * mm[..., None]; n_ += w_ * mm
    out_ = img.copy(); out_[m] = s_[m] / n_[m][:, None]; return out_
alb = suavizar_v(alb, cuerno)

# --- ojo y nariz: la parte oscura junto a cada punto es humeda y sin pelo ---
yy, xx = np.mgrid[0:R, 0:R]
def junto(uv, radio):
    d = np.hypot(xx - uv[0] * R, yy - (1 - uv[1]) * R)
    return ss(radio - d, 0, radio * 0.3)
lum = l2s(col) @ np.array([0.2126, 0.7152, 0.0722])
ojo = junto(OJO, 0.035 * R) * ss(0.42 - lum, 0, 0.15) * pelo
nariz = junto(NARIZ, 0.045 * R) * ss(0.36 - lum, 0, 0.12) * pelo
alb *= (1 - 0.6 * ojo)[..., None]                 # ojo casi negro
sinpelo = np.maximum(ojo, nariz)

# --- relieve normalizado por zona (el mismo que da la normal) ---
hn = np.zeros_like(hgt)
for m in (pelo, uña, cuerno):
    lo, hi = np.percentile(hgt[m], [2, 98]); hn[m] = np.clip((hgt[m] - lo) / (hi - lo), 0, 1)
alb *= (1 + pelo * (1 - sinpelo) * (FUR * hn - FUR / 2))[..., None]   # mechones claros/oscuros
alb *= (1 - uña * 0.30 * (1 - hn))[..., None]                         # poros mas oscuros
alb *= (1 - cuerno * 0.18 * (1 - hn))[..., None]                      # fondo de las estrias

# --- relleno de gutters (evita que el mip traiga el fondo) ---
def dilate(img, mask, iters=48):
    img = img.copy(); m = mask.copy()
    for _ in range(iters):
        s = np.zeros_like(img); n = np.zeros(m.shape, np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1)):
            sm = np.roll(np.roll(m, dy, 0), dx, 1); si = np.roll(np.roll(img, dy, 0), dx, 1)
            s += si * sm[..., None]; n += sm
        new = (~m) & (n > 0)
        img[new] = s[new] / n[new][:, None]; m = m | new
    img[~m] = img[mask].mean(0)
    return img
os.makedirs(OUT, exist_ok=True)
alb = dilate(alb, cov)
Image.fromarray((l2s(alb) * 255 + 0.5).astype(np.uint8)).save(OUT + "T_Huemul_Albedo.png")

# --- normal: Cycles ya trae margen de 32 px; fuera de eso, plana. Ojo liso, nariz casi ---
nm = nrm[..., :3].copy(); has = nrm[..., :3].sum(-1) > 0
nm[~has] = (0.5, 0.5, 1.0)
kk = np.maximum(ojo, 0.8 * nariz)[..., None]
nm = nm * (1 - kk) + np.array([0.5, 0.5, 1.0]) * kk
Image.fromarray((np.clip(nm, 0, 1) * 255 + 0.5).astype(np.uint8)).save(OUT + "T_Huemul_Normal.png")

# --- rugosidad ---
#   pelo   0.80-0.92 (las puntas de los mechones algo mas lisas, brillo de pelo)
#   pezuña 0.45-0.70 (queratina pulida; los poros mas asperos)
#   cuerno 0.50-0.68, las puntas pulidas bajan a ~0.35
#   ojo 0.05 (humedo, brilla), nariz 0.30
rug = np.full((R, R), 0.86, np.float32)
rug[pelo] = 0.92 - 0.12 * hn[pelo]
rug[uña] = 0.45 + 0.25 * (1 - hn[uña])
punta = ss(P[..., 2], 1.95, 2.07)
rug[cuerno] = (0.50 + 0.18 * (1 - hn[cuerno])) * (1 - punta[cuerno]) + 0.35 * punta[cuerno]
rug = rug * (1 - ojo) + 0.05 * ojo
rug = rug * (1 - nariz) + 0.30 * nariz
rug = dilate(rug[..., None], cov)[..., 0]
Image.fromarray((np.clip(rug, 0, 1) * 255 + 0.5).astype(np.uint8)).save(OUT + "T_Huemul_Roughness.png")
ms = np.zeros((R, R, 4), np.uint8); ms[..., 3] = (np.clip(1 - rug, 0, 1) * 255 + 0.5).astype(np.uint8)
Image.fromarray(ms).save(OUT + "T_Huemul_MetalSmooth.png")

a = ao.copy(); a[nrm[..., :3].sum(-1) <= 0] = 1.0
Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)).resize((R // 2, R // 2), Image.LANCZOS).save(OUT + "T_Huemul_AO.png")
print("ok", round(cov.mean(), 3), "costura px", int(near.sum()), "ojo px", int((ojo > 0.5).sum()),
      "nariz px", int((nariz > 0.5).sum()), "uña px", int(uña.sum()), "cuerno px", int(cuerno.sum()))
