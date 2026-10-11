# Esculpido procedural de plumas sobre el gaviotin (Gabiortin5.blend, "Pajado_HighPoly").
# Correr en Blender (Scripting > Run, o exec por MCP). No toca los vertices base: escribe
# el desplazamiento en el shape key "Plumas" (relativo a "Basis"), recalculado desde
# Basis cada vez, asi que se puede re-correr tras cambiar parametros. Para hornearlo:
# shape key Plumas > "Apply to Basis" / o Object > Apply > "Visual Geometry to Mesh".
#
# Ejes del modelo: cabeza hacia -Y, cola hacia +Y, alas a lo largo de X, Z arriba.
# Todo se desplaza por la normal del vertice; unidades en metros (arista media ~3 mm).
import bpy, numpy as np

OBJ, KEY = "Pajado_HighPoly", "Plumas"
FUERZA = globals().get("FUERZA_PLUMAS", 1.0)

OJO = np.array([0.257, -0.627, 1.665])      # centro del ojo (lado +X; el otro es espejo)
BASE_PICO = np.array([0.0, -0.86, 1.63])    # origen del flujo de plumas de cabeza y cuerpo
EJE = np.array([0.0, 1.0, -0.3]) / np.hypot(1.0, 0.3)   # hacia la cola
PIVOTE_MANO = (1.9, -1.1)                    # (|x|, y) desde donde se abren las primarias
PIVOTE_COLA = 0.40                           # y desde donde se abren las rectrices

o = bpy.data.objects[OBJ]; me = o.data
if me.shape_keys is None:
    o.shape_key_add(name="Basis", from_mix=False)
kb = me.shape_keys.key_blocks
n = len(me.vertices)
co = np.empty(n * 3, np.float32); kb["Basis"].data.foreach_get("co", co)
co = co.reshape(-1, 3).astype(np.float64)
nr = np.empty(n * 3, np.float32); me.vertex_normals.foreach_get("vector", nr)
nr = nr.reshape(-1, 3).astype(np.float64)
X, Y, Z = co[:, 0], co[:, 1], co[:, 2]
S = np.abs(X)

def ss(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)

def azar(i, j, k=0.0):
    return np.modf(np.abs(np.sin(i * 12.9898 + j * 78.233 + k * 37.719) * 43758.5453))[0]

def ondas(p, f, sem):
    # ruido barato y suave (suma de senos 3D) en [-1, 1]
    r = np.zeros(len(p))
    for i, (a, b, c) in enumerate([(1.0, 0.7, 0.3), (-0.4, 1.1, 0.8), (0.6, -0.5, 1.2)]):
        r += np.sin(f * (a * p[:, 0] + b * p[:, 1] + c * p[:, 2]) + sem * (i + 1) * 1.7)
    return r / 3

def tejas(a, b, L, W, lobulo=0.45, sem=0.0, La=None, periodo=None):
    """Plumas en tejado: filas a lo largo de `a` (crece hacia la punta de la pluma),
    columnas en `b` desfasadas media pluma por fila. La fila anterior (r-1) queda
    encima y su punta redondeada invade la fila r. Devuelve altura 0..1, raquis, barbas.
    La: largo de una fila en metros, si `a` no esta en metros (solo para las barbas).
    periodo(k): columnas por vuelta si `b` es un angulo (para que el azar no corte en +-pi)."""
    La = L if La is None else La
    r = np.floor(a / L)
    res = None
    for k in (r - 1, r):
        rnd = azar(k, 3.1, sem)
        Wk = W(k) if callable(W) else W                     # ancho segun la fila
        cb = b / Wk + 0.5 * np.mod(k, 2) + 0.15 * (rnd - 0.5)
        c = np.floor(cb); fb = cb - c
        rnd2 = azar(k, np.mod(c, periodo(k)) if periodo else c, sem)
        t = (a - k * L) / L                                  # 0 base .. 1 inicio del lobulo
        lob = lobulo * (0.75 + 0.5 * rnd2) * np.sqrt(np.clip(1 - (2 * fb - 1) ** 2, 0, 1))
        dentro = (t >= 0) & (t < 1 + lob)
        tn = np.clip(t / (1 + lob), 0, 1)
        h = tn ** 1.3 * (0.75 + 0.5 * rnd2)
        h *= 1 - 0.55 * ss(0.86, 1.0, tn)                    # canto redondeado de la punta
        h += 0.22 * (1 - (2 * fb - 1) ** 2)                  # abombado lateral
        raq = np.exp(-((fb - 0.5) / 0.06) ** 2) * ss(0.0, 0.25, tn) * (1 - 0.6 * tn)
        barb = np.sin(2 * np.pi * (t * La + np.abs(fb - 0.5) * Wk * 1.3) / 0.028) * ss(0.1, 0.5, tn)
        if res is None:
            res = [dentro, h, raq, barb]
        else:  # fila r: solo donde la anterior no tapa
            usa = ~res[0]
            res = [res[0] | dentro] + [np.where(usa, v, rv) for v, rv in zip((h, raq, barb), res[1:])]
    return res[1], res[2], res[3]

def remeras(t, b, W, sem=0.0):
    """Plumas largas de una sola fila (remeras, rectrices): t 0..1 de base a punta,
    columnas en b. Solape lateral en escalon + raquis + surco entre plumas."""
    cb = b / W
    c = np.floor(cb); fb = cb - c
    rnd = azar(c, 7.7, sem)
    h = fb * (0.7 + 0.6 * rnd)                               # escalon lateral
    h *= 1 - 0.4 * ss(0.85, 1.0, fb)
    raq = np.exp(-((fb - 0.45) / 0.07) ** 2) * (1 - 0.5 * t)
    surco = np.exp(-(np.minimum(fb, 1 - fb) / 0.05) ** 2) * (0.4 + 0.6 * t)
    barb = np.sin(2 * np.pi * (t * 0.5 + np.abs(fb - 0.45) * W * 1.2) / 0.028)
    return h, raq, surco, barb

disp = np.zeros(n)
ruido = ondas(co, 6.0, 1.0)

# ---------------- alas ----------------
ala = (S > 0.55) & (Z > 1.0)
bins = np.arange(0.55, 3.86, 0.02)
idx = np.clip(np.digitize(S[ala], bins) - 1, 0, len(bins) - 1)
yle = np.full(len(bins), np.nan); yte = np.full(len(bins), np.nan)
ya = Y[ala]
for i in range(len(bins)):
    m = idx == i
    if m.sum() > 20:
        yle[i] = np.percentile(ya[m], 0.5); yte[i] = np.percentile(ya[m], 99.5)
ok = ~np.isnan(yle)
yle = np.interp(bins, bins[ok], yle[ok]); yte = np.interp(bins, bins[ok], yte[ok])
k = np.ones(7) / 7
yle = np.convolve(np.pad(yle, 3, mode="edge"), k, "valid"); yte = np.convolve(np.pad(yte, 3, mode="edge"), k, "valid")
yle[bins < 0.9] = np.interp(0.9, bins, yle); yte[bins < 0.9] = np.interp(0.9, bins, yte)

w_ala = ss(0.62, 0.85, S) * ss(1.0, 1.12, Z)
wa = w_ala > 0
s, y = S[wa], Y[wa]
le, te = np.interp(s, bins, yle), np.interp(s, bins, yte)
u = np.clip((y - le) / np.maximum(te - le, 0.05), 0, 1)
sw, yp = PIVOTE_MANO
b_ala = np.where(s < sw, s - sw, np.arctan2(s - sw, y - yp) * 1.0)
u_rem = 0.55 - 0.33 * ss(2.2, 3.6, s)
cuerda = te - le

# cobertoras: 6 filas, mas densas cerca del borde de ataque
v = np.clip(u / u_rem, 0, 1.6)
q = 6.0 * v ** 0.75 + 0.25 * ruido[wa]
fila = np.clip(np.floor(q), 0, 5)
hc, rc, bc = tejas(q, b_ala, 1.0, lambda k: 0.05 + 0.012 * np.clip(k, 0, 5), lobulo=0.5, sem=2.0,
                    La=0.06 + 0.01 * fila)
amp_c = 0.004 + 0.0011 * fila
d_cob = amp_c * (hc - 0.5) + 0.0018 * rc + 0.0002 * bc
d_cob *= 0.35 + 0.65 * ss(0.0, 0.07, u)                     # borde de ataque suave

# remeras: secundarias y primarias
t_rem = np.clip((u - u_rem) / np.maximum(1 - u_rem, 0.05), 0, 1)
W_rem = 0.12 + 0.03 * ss(1.6, 2.4, s)
hr, rr, gr, br = remeras(t_rem, b_ala, W_rem, sem=5.0)
d_rem = 0.008 * (hr - 0.5) + 0.0028 * rr - 0.0035 * gr + 0.0004 * br
d_rem *= 1 - 0.55 * ss(0.92, 1.0, u)                        # borde de fuga fino
# la base de las remeras queda bajo los lobulos de las cobertoras mayores
sobre_rem = u > u_rem
mez = ss(0.0, 0.08, t_rem)
d_ala = np.where(sobre_rem, (1 - mez) * d_cob + mez * d_rem, d_cob)
d_ala *= np.where(nr[wa, 2] < 0, 0.7, 1.0)                  # cara inferior mas suave
disp[wa] += w_ala[wa] * d_ala

# ---------------- cabeza y cuerpo ----------------
# Un solo flujo: anillos de distancia a la base del pico, las plumas apuntan hacia
# afuera (atras en el lomo, abajo en el pecho). Columnas por angulo alrededor de EJE,
# con un numero entero de plumas por anillo para que no haya costura en la panza.
rel = co - BASE_PICO
dist = np.linalg.norm(rel, axis=1)
arriba = np.array([0.0, 0.0, 1.0]) - EJE[2] * EJE; arriba /= np.linalg.norm(arriba)
th = np.arctan2(rel[:, 0], rel @ arriba)
rperp = np.linalg.norm(rel - np.outer(rel @ EJE, EJE), axis=1)

def largo(d):                                 # largo de pluma: 3 cm en la cabeza, 8 en el cuerpo
    return 0.028 + 0.05 * ss(0.15, 0.75, d)
dd = np.arange(0.0, 3.0, 0.002)
qtab = np.concatenate([[0], np.cumsum(0.002 / largo(dd[:-1] + 0.001))])
tronco = (Y > -0.86) & (S < 0.62)
cnt = np.bincount((dist[tronco] / 0.02).astype(int), minlength=150)[:150]
sr = np.bincount((dist[tronco] / 0.02).astype(int), rperp[tronco], minlength=150)[:150]
dtab = np.arange(150) * 0.02 + 0.01
okr = cnt > 50
rtab = np.interp(dtab, dtab[okr], sr[okr] / cnt[okr])
def cols(k):                                  # plumas por anillo k
    dk = np.interp(k + 0.5, qtab, dd)
    return np.maximum(np.round(2 * np.pi * np.interp(dk, dtab, rtab) / (0.9 * largo(dk))), 6)

q_cu = np.interp(dist, dd, qtab) + 0.12 * ruido
hb, rb, _ = tejas(q_cu, th + 0.03 * ruido, 1.0, lambda k: 2 * np.pi / cols(k), lobulo=0.55,
                   sem=9.0, La=largo(dist), periodo=cols)
amp = 0.0028 + 0.004 * ss(0.15, 0.75, dist)
d_cu_ca = amp * (hb - 0.5) + 0.22 * amp * rb
d_cu_ca *= ss(-0.88, -0.78, Y)                               # pico liso
ojo = np.minimum(np.linalg.norm(co - OJO, axis=1),
                 np.linalg.norm(co - OJO * np.array([-1, 1, 1]), axis=1))
d_cu_ca *= ss(0.075, 0.12, ojo)

# ---------------- cola ----------------
w_cola = ss(0.95, 1.15, Y) * (S < 0.5)
tw = w_cola > 0
t_cola = np.clip((Y[tw] - 1.0) / 1.12, 0, 1)
b_cola = np.arctan2(X[tw], Y[tw] - PIVOTE_COLA) * 1.2
hq, rq, gq, bq = remeras(t_cola, b_cola, 0.085, sem=17.0)
d_cola = np.zeros(n)
d_cola[tw] = 0.006 * (hq - 0.5) + 0.002 * rq - 0.0025 * gq + 0.0003 * bq

# ---------------- patas (escamas del tarso) ----------------
w_pata = 1 - ss(0.47, 0.62, Z)
fz = np.mod(Z / 0.028 + 0.1 * ruido, 1.0)
d_pata = 0.0028 * (fz ** 2 - 0.4) * ss(0.06, 0.12, Z)

w_tronco = (1 - w_ala) * (1 - w_cola) * (1 - w_pata)
disp += w_tronco * d_cu_ca + (1 - w_ala) * w_cola * d_cola + w_pata * d_pata

# suavizado leve del escalar (quita el diente de sierra de 1 vertice en los cantos)
ev = np.empty(len(me.edges) * 2, np.int32); me.edges.foreach_get("vertices", ev); ev = ev.reshape(-1, 2)
grado = np.bincount(ev.ravel(), minlength=n).astype(np.float64)
for _ in range(3):
    vec = np.bincount(ev[:, 0], disp[ev[:, 1]], n) + np.bincount(ev[:, 1], disp[ev[:, 0]], n)
    disp = 0.5 * disp + 0.5 * vec / np.maximum(grado, 1)

nuevo = co + nr * (disp * FUERZA)[:, None]
sk = kb.get(KEY) or o.shape_key_add(name=KEY, from_mix=False)
sk.data.foreach_set("co", nuevo.astype(np.float32).ravel())
sk.value = 1.0
o.active_shape_key_index = list(kb).index(sk)
me.update()
result = {"disp_min": float(disp.min()), "disp_max": float(disp.max()),
          "disp_std": float(disp.std()), "verts_ala": int(wa.sum()), "verts_cola": int(tw.sum())}
