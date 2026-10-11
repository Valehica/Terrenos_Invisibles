# Arma albedo / normal / AO de la vicuña a partir de los bakes de vicuna_bake.py
# (.npy, filas de abajo hacia arriba). Uso, con python del sistema (numpy + Pillow):
#   python vicuna_componer.py <carpeta_bakes> <T_Vicuña.png pintada> [fuerza_pelo=0.20]
# Deja T_Vicuna_Albedo/Normal/AO.png en la carpeta de bakes; copiarlas a
# Assets/Models/Norte/Vicuña y correr Terrenos > Norte > Aplicar textura de la vicuña.
#
# Las costuras se arreglan mezclando, cerca de cada borde de isla, el color con un
# desenfoque hecho en el espacio 3D del modelo (no en la UV): los dos lados de una
# costura caen en el mismo punto 3D y terminan con el mismo color.
import numpy as np, sys
from PIL import Image

D = sys.argv[1].rstrip("/\\") + "/"
SRC = sys.argv[2]
EXT = np.array([0.2196, 0.7202, 0.9077])          # tamaño del bbox en metros (igual que en el bake)
VOX = 0.0035                                      # voxel de 3,5 mm
SIGMA = 1.8                                       # desenfoque 3D, en voxeles
BORDE = 30                                        # px (a 2048) de mezcla junto a cada costura
FUR = float(sys.argv[3]) if len(sys.argv) > 3 else 0.20

flip = lambda a: a[::-1]                          # bottom-up -> top-down
pos = flip(np.load(D + "pos.npy"))
hgt = flip(np.load(D + "height.npy"))[..., 0]
nrm = flip(np.load(D + "normal.npy"))
ao = flip(np.load(D + "ao.npy"))[..., 0]
R = pos.shape[0]
cov = pos[..., :3].sum(-1) > 0
P = pos[..., :3] * EXT                            # posición en metros

def s2l(c): return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
def l2s(c): c = np.clip(c, 0, 1); return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)

src = Image.open(SRC).convert("RGB").resize((R, R), Image.LANCZOS)
col = s2l(np.asarray(src, np.float32) / 255.0)

# --- distancia (px) al borde de isla ---
DMAX = 120
dist = np.where(cov, DMAX, 0).astype(np.float32)
inside = cov.copy()
for i in range(DMAX):
    e = inside.copy()
    e[1:] &= inside[:-1]; e[:-1] &= inside[1:]; e[:, 1:] &= inside[:, :-1]; e[:, :-1] &= inside[:, 1:]
    dist[inside & ~e] = np.minimum(dist[inside & ~e], i)
    inside = e
# ancho de mezcla por isla: nunca mas del 35% de su radio, asi las islas chicas
# (interior de las orejas) conservan su color en el centro
isla = np.rint(flip(np.load(D + "isla.npy")) * 64).astype(int)
isla[~cov] = 0
rad = np.zeros(isla.max() + 1); np.maximum.at(rad, isla[cov], dist[cov])
BW = np.minimum(BORDE, 0.35 * rad[isla]).clip(4, None)

# --- área de superficie por texel (para que todas las islas pesen igual) ---
dPu = np.gradient(P, axis=1); dPv = np.gradient(P, axis=0)
area = np.linalg.norm(np.cross(dPu, dPv), axis=-1)
area = np.clip(area, 0, np.percentile(area[cov], 95))
area[~cov] = 0

# --- splat al grid 3D, gauss separable, muestreo trilineal ---
G = np.ceil(EXT / VOX).astype(int) + 3
g = P / VOX + 1.0
idx = np.clip(np.floor(g + 0.5).astype(int), 0, G - 1)
lin = np.ravel_multi_index((idx[..., 0][cov], idx[..., 1][cov], idx[..., 2][cov]), G)
# normal en espacio objeto: separa por direccion para que las caras opuestas
# de laminas finas (orejas) no se mezclen; en la costura la normal es la misma
nob = flip(np.load(D + "onormal.npy"))[..., :3] * 2 - 1
nob /= np.maximum(np.linalg.norm(nob, axis=-1, keepdims=True), 1e-6)
near = cov & (dist < BW)
gp = g[near]; p0 = np.floor(gp).astype(int); fr = gp - p0
r = int(np.ceil(3 * SIGMA)); k = np.exp(-0.5 * (np.arange(-r, r + 1) / SIGMA) ** 2); k /= k.sum()
num = np.zeros((near.sum(), 3)); den = np.zeros(near.sum())
for ax in range(3):
    for sg in (1, -1):
        bw = np.maximum(nob[..., ax] * sg, 0) ** 2           # suma 1 sobre los 6 bins
        w = (area * bw)[cov]
        acc = np.zeros((4, np.prod(G)), np.float32)
        for c in range(3): acc[c] = np.bincount(lin, weights=col[..., c][cov] * w, minlength=acc.shape[1])
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
        bt = bw[near]
        num += bt[:, None] * sv[:, :3]; den += bt * sv[:, 3]
        del acc, out

blur = col.copy()
ok = den > 1e-12
tmpc = blur[near]; tmpc[ok] = num[ok] / den[ok][:, None]; blur[near] = tmpc
t = np.clip(1 - dist / BW, 0, 1); t = t * t * (3 - 2 * t)
alb = col * (1 - t[..., None]) + blur * t[..., None]

# --- ojos y nariz: la parte oscura de esas islas es humeda, sin pelo ---
# (puntos UV dentro de cada isla; cambian solo si se rehacen las UVs de la cabeza)
OJOS = [(0.527, 0.717), (0.867, 0.116)]
NARIZ = [(0.70, 0.90)]
id_en = lambda u, v: isla[int((1 - v) * R), int(u * R)]
lum = l2s(col) @ np.array([0.2126, 0.7152, 0.0722])
osc = np.clip((0.5 - lum) / 0.2, 0, 1); osc = osc * osc * (3 - 2 * osc)
ojo = np.isin(isla, [id_en(*p) for p in OJOS]) * osc
nariz = np.isin(isla, [id_en(*p) for p in NARIZ]) * osc
alb *= (1 - 0.55 * ojo)[..., None]                # ojo casi negro, como en la foto
sinpelo = np.maximum(ojo, nariz)

# --- detalle de pelo (mismo ruido 3D que la normal) ---
hv = hgt[cov]; lo, hi = np.percentile(hv, [2, 98])
hn = np.clip((hgt - lo) / (hi - lo), 0, 1)
alb *= (1 + (1 - sinpelo) * (FUR * hn - FUR / 2))[..., None]

# --- relleno de gutters (evita que el mip traiga el fondo de grilla) ---
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
alb = dilate(alb, cov)
Image.fromarray((l2s(alb) * 255 + 0.5).astype(np.uint8)).save(D + "T_Vicuna_Albedo.png")

# normal: Cycles ya trae margen de 32 px; fuera de eso, normal plana
nm = nrm[..., :3].copy(); has = nrm[..., :3].sum(-1) > 0
nm[~has] = (0.5, 0.5, 1.0)
k = np.maximum(ojo, 0.8 * nariz)[..., None]
nm = nm * (1 - k) + np.array([0.5, 0.5, 1.0]) * k
Image.fromarray((np.clip(nm, 0, 1) * 255 + 0.5).astype(np.uint8)).save(D + "T_Vicuna_Normal.png")

a = ao.copy(); a[nrm[..., :3].sum(-1) <= 0] = 1.0
Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)).resize((R // 2, R // 2), Image.LANCZOS).save(D + "T_Vicuna_AO.png")
# metallic/smoothness al estilo URP Lit: R = metal (0), A = smoothness.
# Pelo 0.12, ojo 0.9 (humedo), nariz 0.5. En Blender: roughness = 1 - A.
sm = 0.12 + 0.78 * ojo + 0.38 * nariz
sm = dilate(sm[..., None], cov)[..., 0]
ms = np.zeros((R, R, 4), np.uint8); ms[..., 3] = (np.clip(sm, 0, 1) * 255 + 0.5).astype(np.uint8)
Image.fromarray(ms).resize((R // 2, R // 2), Image.LANCZOS).save(D + "T_Vicuna_MetalSmooth.png")
print("ok", cov.mean(), "seam px", near.sum(), "ojo px", int((ojo > 0.5).sum()), "nariz px", int((nariz > 0.5).sum()))
