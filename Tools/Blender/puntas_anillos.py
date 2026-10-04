# Rehace las puntas de rama como tubos de anillos limpios (misma topologia que las puntas de referencia):
# corte = el borde mas cercano al cuello que sea plano (FLAT_OK); anillos = curvas de nivel de la distancia
# a la punta sobre la superficie vieja, remuestreadas a M lados y alineadas para no torcerse;
# anillo diminuto en la punta + tapa de quads en escalera (M siempre es par en una malla de quads).
# Las puntas con caras seleccionadas se toman como referencia y no se tocan.
# BUILD=False solo analiza; BUILD=True modifica la malla (modo edicion, objeto activo).
import bpy, bmesh, heapq
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

BUILD = globals().get("BUILD", False)
ONLY = globals().get("ONLY", None)            # indices de punta a rehacer (None = todas las validas)
MIN_LEN = globals().get("MIN_LEN", 0.35)      # m: puntas mas cortas se ignoran (bultos)
FLARE = globals().get("FLARE", 2.5)           # dP/dt que marca el cuello
SPACING_K = globals().get("SPACING_K", 2.4)   # separacion entre anillos = K * arista alrededor
TIP_SCALE = globals().get("TIP_SCALE", 0.3)   # radio del anillo final respecto al anterior
FLAT_MAX = globals().get("FLAT_MAX", 2.0)
NECK_W = globals().get("NECK_W", 0.25)
FLAT_OK = globals().get("FLAT_OK", 0.15)      # borde "plano" aceptable (desviacion / radio)        # preferencia por cortar cerca del cuello vs. borde plano     # cuanto puede moverse un vertice del corte (en aristas)

o = bpy.context.view_layer.objects.active
me = o.data
bm = bmesh.from_edit_mesh(me)
bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()
n = len(bm.verts)
P = np.array([v.co[:] for v in bm.verts])
nbr = [[e.other_vert(v).index for e in v.link_edges] for v in bm.verts]
el = float(np.mean([e.calc_length() for e in bm.edges]))
FV = [[v.index for v in f.verts] for f in bm.faces]
TRI = np.array([(vs[0], vs[i], vs[i+1]) for vs in FV for i in range(1, len(vs)-1)])
FTRI = [[(vs[0], vs[i], vs[i+1]) for i in range(1, len(vs)-1)] for vs in FV]

def dijkstra(srcs, limit=1e9):
    dist = np.full(n, np.inf); pq = []
    for s in srcs: dist[s] = 0.0; pq.append((0.0, s))
    heapq.heapify(pq)
    while pq:
        c, a = heapq.heappop(pq)
        if c > dist[a] or c > limit: continue
        for b in nbr[a]:
            c1 = c + np.linalg.norm(P[a]-P[b])
            if c1 < dist[b]: dist[b] = c1; heapq.heappush(pq, (c1, b))
    return dist

root = int(np.argmin(P[:, 2]))
d = dijkstra([root])

# --- puntas: maximos de d con persistencia ---
par = np.full(n, -1)
def find(x):
    while par[x] != x: par[x] = par[par[x]]; x = par[x]
    return x
ctip = {}; done = np.zeros(n, bool); tips = []
for v in np.argsort(-d):
    v = int(v); roots = {find(u) for u in nbr[v] if done[u]}; done[v] = True
    if not roots: par[v] = v; ctip[v] = v; continue
    rs = sorted(roots, key=lambda r: d[ctip[r]], reverse=True); sv = rs[0]
    for r in rs[1:]:
        if d[ctip[r]] - d[v] >= MIN_LEN: tips.append(ctip[r])
        par[r] = sv
    par[v] = sv
tips.append(ctip[find(root)])

sel_faces = {f.index for f in bm.faces if f.select}
sel_verts = {i for f in sel_faces for i in FV[f]}
vfaces = [[f.index for f in v.link_faces] for v in bm.verts]

def isoline(g, c):
    gt = g[TRI]; m = (gt.min(1) < c) & (gt.max(1) > c)
    pts = []; L = 0.0
    for a, b, cc in TRI[m]:
        seg = []
        for x, y in ((a, b), (b, cc), (cc, a)):
            if (g[x]-c)*(g[y]-c) < 0:
                r = (c-g[x])/(g[y]-g[x]); seg.append(P[x]+r*(P[y]-P[x]))
        if len(seg) == 2: L += np.linalg.norm(seg[0]-seg[1]); pts += seg
    return L, (np.array(pts) if pts else np.zeros((0, 3)))

def region(g, c, tv):
    ok = lambda f: all(g[i] < c for i in FV[f])
    start = [f for f in vfaces[tv] if ok(f)]
    seen = set(start); st = list(start)
    while st:
        f = st.pop()
        for e in bm.faces[f].edges:
            for h in e.link_faces:
                if h.index not in seen and ok(h.index): seen.add(h.index); st.append(h.index)
    return seen

def boundary(R):
    out = {}
    for f in R:
        for e in bm.faces[f].edges:
            if sum(h.index in R for h in e.link_faces) == 1: out[e.index] = e
    return list(out.values())

def ordered(es, R):
    """borde en orden: dentro de R la cara recorre B[j]->B[j+1]... invertido para que la cara nueva lo use igual"""
    nxt = {}
    for e in es:
        fin = [h for h in e.link_faces if h.index in R][0]
        for l in fin.loops:
            if l.edge is e: a, b = l.vert.index, l.link_loop_next.vert.index
        if a in nxt: return None            # vertice tocado dos veces: el borde no es un anillo simple
        nxt[a] = b
    start = next(iter(nxt)); out = [start]
    while True:
        x = nxt.get(out[-1])
        if x is None: return None
        if x == start: break
        out.append(x)
        if len(out) > len(nxt): return None
    return out if len(out) == len(nxt) else None

plans = []; report = []; CANDS = {}
for ti, tv in enumerate(tips):
    tip = [round(x, 2) for x in P[tv]]
    if ONLY is not None and ti not in ONLY: continue
    if tv in sel_verts: report.append(dict(i=ti, tip=tip, skip="referencia")); continue
    g = dijkstra([tv], limit=6.0)
    others = [t for t in tips if t != tv]
    gmax_ok = min([g[t] for t in others] + [g[root]])
    cs = np.arange(2*el, min(gmax_ok, 5.0), el*0.5); ps = []; ck = None
    for c in cs:
        ps.append(isoline(g, c)[0])
        k = len(ps)-4
        if k >= 1 and (ps[-1]-ps[k])/(3*el*0.5) > FLARE: ck = cs[k]; break
    if ck is None and ps: ck = cs[len(ps)-1]
    if ck is None: report.append(dict(i=ti, tip=tip, skip="sin cuello")); continue
    best = None; allc = []
    for c in np.linspace(0.6*ck, ck, 12):
        R = region(g, c, tv)
        if not R or (R & sel_faces): continue
        es = boundary(R); B = ordered(es, R)
        if B is None or len(B) < 6: continue
        Q = P[B]; cq = Q.mean(0)
        w_, U_ = np.linalg.eigh(np.cov((Q-cq).T))
        flat = float(np.sqrt(w_[0])/max(np.linalg.norm(Q-cq, axis=1).mean(), 1e-6))   # 0 = anillo plano
        score = flat - NECK_W*c/ck
        CANDS.setdefault(ti, []).append((round(float(c), 2), round(flat, 3), len(B)))
        allc.append((score, c, R, B, flat))
    ok_ = [x for x in allc if x[4] < FLAT_OK]
    if ok_: best = max(ok_, key=lambda x: x[1])[:4]
    elif allc: best = min(allc, key=lambda x: x[0])[:4]
    if best is None: report.append(dict(i=ti, tip=tip, skip="borde no es un anillo")); continue
    score, c, R, B = best
    # el corte en un borde: la cara de fuera recorre B al reves que la de dentro; las nuevas caras van por dentro
    plans.append(dict(i=ti, tv=tv, g=g, c=c, R=R, B=B))
    report.append(dict(i=ti, tip=tip, lados=len(B), largo=round(float(c), 2), caras=len(R), zigzag=round(score, 2)))

# ---------------- construir ----------------
built = []
if BUILD and plans:
    bvh = BVHTree.FromBMesh(bm)                 # superficie original (antes de borrar)
    new_faces_all = []
    # coordenadas y datos de cada plan se toman antes de borrar nada (los indices cambian al borrar)
    prepared = []
    for pl in plans:
        g, c, R, B, tv = pl["g"], pl["c"], pl["R"], pl["B"], pl["tv"]
        M = len(B)
        ts = np.linspace(c*0.98, el*0.5, 24)
        cen = [P[B].mean(0)]; rad = [float(np.linalg.norm(P[B]-P[B].mean(0), axis=1).mean())]
        for t in ts:
            L, pts = isoline(g, t)
            if len(pts) >= 6:
                cc = pts.mean(0); cen.append(cc); rad.append(float(np.linalg.norm(pts-cc, axis=1).mean()))
        cen.append(P[tv]); rad.append(0.0)
        cen = np.array(cen)
        for _ in range(3):
            cen[1:-1] = 0.25*cen[:-2] + 0.5*cen[1:-1] + 0.25*cen[2:]
        s_acc = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(cen, axis=0), axis=1))])
        Ls = float(s_acc[-1])
        def at(s, cen=cen, s_acc=s_acc, rad=rad):
            return np.array([np.interp(s, s_acc, cen[:, k]) for k in range(3)]), float(np.interp(s, s_acc, rad))
        def tangent(s, at=at, Ls=Ls):
            a, _ = at(max(s-0.05, 0)); b, _ = at(min(s+0.05, Ls)); t = b-a; return t/np.linalg.norm(t)
        around = sum(np.linalg.norm(P[B[j]]-P[B[(j+1) % M]]) for j in range(M))/M
        spacing = float(np.clip(SPACING_K*around, 0.08, 0.35))
        K = max(3, int(round(Ls/spacing)))
        T0 = tangent(0.0); c0 = cen[0]
        # enderezar el anillo de corte: llevar cada vertice al plano perpendicular a la rama
        # (sin pasarse de FLAT_MAX aristas) y pegarlo otra vez a la superficie original
        for j in range(M):
            b = P[B[j]]; off = np.dot(b-c0, T0)*T0
            lim = FLAT_MAX*around
            if np.linalg.norm(off) > lim: off = off/np.linalg.norm(off)*lim
            q_ = b - off
            nn = bvh.find_nearest(Vector(q_))
            if nn[0] is not None: q_ = np.array(nn[0][:])
            P[B[j]] = q_; bm.verts[B[j]].co = Vector(q_)
        c0 = P[B].mean(0); cen[0] = c0
        dirs = []
        for j in range(M):
            v = P[B[j]]-c0; v = v - np.dot(v, T0)*T0; dirs.append(v/np.linalg.norm(v))
        dirs = np.array(dirs)
        # anillos = curvas de nivel de la distancia a la punta, dentro de la zona vieja
        tris = [tuple(t) for t in TRI if max(g[t[0]], g[t[1]], g[t[2]]) < c*1.6]
        def iso_loops(t):
            seg_of = {}; segs = []
            for (a, b_, cc) in tris:
                pts = []
                for x, y in ((a, b_), (b_, cc), (cc, a)):
                    if (g[x]-t)*(g[y]-t) < 0:
                        r = (t-g[x])/(g[y]-g[x]); pts.append(((min(x, y), max(x, y)), P[x]+r*(P[y]-P[x])))
                if len(pts) == 2:
                    k = len(segs); segs.append(pts)
                    for key, _ in pts: seg_of.setdefault(key, []).append(k)
            used = set(); loops = []
            for k0 in range(len(segs)):
                if k0 in used: continue
                used.add(k0); (ka, pa), (kb, pb) = segs[k0]
                chain = [pa, pb]; key = kb; start = ka
                while True:
                    nx = [k for k in seg_of.get(key, []) if k not in used]
                    if not nx: break
                    k = nx[0]; used.add(k)
                    (k1, p1), (k2, p2) = segs[k]
                    if k1 == key: key = k2; chain.append(p2)
                    else: key = k1; chain.append(p1)
                    if key == start: break
                if key == start: loops.append(np.array(chain[:-1]))   # solo anillos cerrados
            return loops
        def resample(L_, m):
            L_ = np.vstack([L_, L_[:1]]) if np.linalg.norm(L_[0]-L_[-1]) > 1e-9 else L_
            sd = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(L_, axis=0), axis=1))])
            u = np.linspace(0, sd[-1], m, endpoint=False)
            return np.array([[np.interp(x, sd, L_[:, k]) for k in range(3)] for x in u])
        def align(prev, cur):
            best_ = None
            for flip in (False, True):
                cc = cur[::-1] if flip else cur
                for sh in range(len(cc)):
                    rr = np.roll(cc, -sh, axis=0); cost = np.linalg.norm(rr-prev, axis=1).sum()
                    if best_ is None or cost < best_[0]: best_ = (cost, rr)
            return best_[1]
        rings = []; prev = np.array([bm.verts[i].co[:] for i in B]); prev_c = prev.mean(0)
        dt = c/K
        levels = [c - k*dt for k in range(1, K)]
        for t in levels:
            loops = iso_loops(t)
            loops = [l for l in loops if len(l) >= 4]
            if not loops: continue
            lp = min(loops, key=lambda l: np.linalg.norm(l.mean(0)-prev_c))
            ring = align(prev, resample(lp, M))
            rings.append(list(ring)); prev = ring; prev_c = ring.mean(0)
        # anillo diminuto final, hacia la punta
        tipp = P[tv]
        dvec = tipp - prev_c; nd = np.linalg.norm(dvec)
        cpt = prev_c + dvec*0.75 if nd > 1e-6 else prev_c
        rings.append(list(cpt + TIP_SCALE*(prev - prev_c)))
        prepared.append(dict(i=pl["i"], R=[bm.faces[f] for f in R], B=[bm.verts[i] for i in B], rings=rings, M=M, Ls=Ls))
    for pr in prepared:
        R, B, rings, M = pr["R"], pr["B"], pr["rings"], pr["M"]
        sm = any(h.smooth for h in B[0].link_faces)
        old_verts = {v for f in R for v in f.verts} - set(B)
        bmesh.ops.delete(bm, geom=R, context='FACES_ONLY')
        bmesh.ops.delete(bm, geom=[v for v in old_verts if v.is_valid and not v.link_faces], context='VERTS')
        loose_e = [e for e in bm.edges if e.is_valid and not e.link_faces and any(v in old_verts for v in e.verts)]
        if loose_e: bmesh.ops.delete(bm, geom=loose_e, context='EDGES')
        vr = [B] + [[bm.verts.new(Vector(p)) for p in ring] for ring in rings]
        nf = []
        for a_, b_ in zip(vr[:-1], vr[1:]):
            for j in range(M):
                nf.append(bm.faces.new((a_[j], a_[(j+1) % M], b_[(j+1) % M], b_[j])))
        Lr = vr[-1]
        for i in range(M//2 - 1):
            nf.append(bm.faces.new((Lr[i], Lr[i+1], Lr[M-2-i], Lr[M-1-i])))
        for f in nf: f.smooth = sm
        new_faces_all += nf
        built.append(dict(i=pr["i"], lados=M, anillos=len(vr), caras=len(nf), largo=round(pr["Ls"], 2)))
    bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()
    for f in bm.faces: f.select = False
    for e in bm.edges: e.select = False
    for v in bm.verts: v.select = False
    for f in new_faces_all: f.select = True
    bm.select_flush_mode()
    bmesh.update_edit_mesh(me)

_res = dict(cands=CANDS, ck={}, tips=len(tips), report=report, built=built)
