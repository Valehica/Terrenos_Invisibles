# UVs "de madera" para un arbol (Blender, correr en la consola de Python o via exec).
# Cada rama y el tronco salen como un tubo abierto con la veta en V (vertical en la UV):
# 1) distancia geodesica suavizada desde la base (heat method)  -> V del tronco
# 2) arbol de uniones de los superniveles                        -> puntas de rama y jerarquia
# 3) cuello de cada rama: desde su punta, donde el perimetro crece > FLARE m/m;
#    luego se extiende NECK_EXT radios hacia el padre (el ensanche va con la rama)
# 4) costura vertical por tubo, del borde con el padre a la punta, en el lado escondido
#    (ramas: hacia el eje del arbol; tronco: HIDDEN_TRUNK)
# 5) theta alrededor del tubo por minimos cuadrados (sigue N x grad V, salta 1 en la costura),
#    U = theta * ancho local (con dC/dV limitado a SLOPE para que las rayas no se inclinen)
# Escribe la capa UV "UV_Madera" y marca las costuras. Despues: UV > Pack Islands sin rotar.
# Uso: exec(open(ruta).read(), {"OBJ": "MiArbol"})  (malla cerrada, de una pieza, base abajo)
import bpy, bmesh, heapq
import numpy as np

OBJ = globals().get("OBJ", "Arbol_RemeshBase Remeshed")
MIN_PERS = globals().get("MIN_PERS", 0.30)          # m: ramas mas cortas no se cortan
FLARE = globals().get("FLARE", 2.5)                 # dP/dV (m/m) que marca el cuello de una rama
HEAT_T = globals().get("HEAT_T", 25.0)                # >1 suaviza la distancia (isolineas mas perpendiculares en curvas)
SLOPE = globals().get("SLOPE", 0.5)                 # dC/dV maximo del ancho de isla
NECK_EXT = globals().get("NECK_EXT", 1.0)           # extender el corte N radios hacia el padre (el ensanche va con la rama)
DEBUG_SEG = globals().get("DEBUG_SEG", None)
FLARE_W = globals().get("FLARE_W", 3)               # pasos de media arista para medirlo
HIDDEN_TRUNK = np.array(globals().get("HIDDEN_TRUNK", (0.0, 1.0, 0.0)))
UVNAME = "UV_Madera"

obj = bpy.data.objects[OBJ]
if obj.mode != 'OBJECT':
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='OBJECT')
me = obj.data
bm = bmesh.new(); bm.from_mesh(me)
bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()
mw = obj.matrix_world
n = len(bm.verts); nf = len(bm.faces)
P = np.array([(mw @ v.co)[:] for v in bm.verts])
N = np.array([(mw.to_3x3() @ v.normal).normalized()[:] for v in bm.verts])

def face_tris(f):
    vs = [v.index for v in f.verts]
    if len(vs) == 3: return [vs]
    if len(vs) == 4:
        a, b, c, d_ = vs
        if np.linalg.norm(P[a]-P[c]) <= np.linalg.norm(P[b]-P[d_]): return [[a, b, c], [a, c, d_]]
        return [[a, b, d_], [b, c, d_]]
    return [[vs[0], vs[i], vs[i+1]] for i in range(1, len(vs)-1)]
FT = [face_tris(f) for f in bm.faces]
T = np.array([t for ts in FT for t in ts])

def cot(a, b, c, Q):  # cot del angulo en c del triangulo (a,b,c)
    u = Q[a]-Q[c]; w = Q[b]-Q[c]
    return np.dot(u, w)/max(np.linalg.norm(np.cross(u, w)), 1e-12)

# ---------- 1) heat method ----------
L = np.zeros((n, n)); M = np.zeros(n)
for i, j, k in T:
    for a, b, c in ((i, j, k), (j, k, i), (k, i, j)):
        w = 0.5*cot(a, b, c, P); L[a, b] += w; L[b, a] += w
    M[[i, j, k]] += np.linalg.norm(np.cross(P[j]-P[i], P[k]-P[i]))/6
L[np.diag_indices(n)] = -L.sum(1)

zmin = P[:, 2].min()
low = np.where(P[:, 2] < zmin+0.05)[0]
cen = P[low].mean(0)
root = int(low[np.argmin(np.linalg.norm(P[low, :2]-cen[:2], axis=1))])
el = np.mean([e.calc_length() for e in bm.edges])
Hinv = np.linalg.inv(np.diag(M) - HEAT_T*el*el*L)
Lpinv = np.linalg.pinv(L)
TC = [(i, j, k, cot(i, j, k, P), cot(j, k, i, P), cot(k, i, j, P)) for i, j, k in T]
def geodesic(sources):
    delta = np.zeros(n); delta[list(sources)] = M[list(sources)]
    uh = Hinv @ delta
    div = np.zeros(n)
    for i, j, k, ck, ci, cj in TC:  # ck: angulo en k, ci: en i, cj: en j
        nrm = np.cross(P[j]-P[i], P[k]-P[i]); A2 = np.linalg.norm(nrm); nh = nrm/A2
        g = (uh[i]*np.cross(nh, P[k]-P[j]) + uh[j]*np.cross(nh, P[i]-P[k]) + uh[k]*np.cross(nh, P[j]-P[i]))/A2
        gn = np.linalg.norm(g); X = -g/gn if gn > 0 else g
        div[i] += 0.5*(ck*np.dot(P[j]-P[i], X) + cj*np.dot(P[k]-P[i], X))
        div[j] += 0.5*(ci*np.dot(P[k]-P[j], X) + ck*np.dot(P[i]-P[j], X))
        div[k] += 0.5*(cj*np.dot(P[i]-P[k], X) + ci*np.dot(P[j]-P[k], X))
    phi = Lpinv @ div
    if phi[list(sources)].mean() > phi.mean(): phi = -phi
    return np.maximum(phi - phi[list(sources)].mean(), 0)
d = geodesic([root])

# ---------- 2) arbol de uniones ----------
nbr = [[e.other_vert(v).index for e in v.link_edges] for v in bm.verts]
par = np.full(n, -1)
def find(x):
    while par[x] != x:
        par[x] = par[par[x]]; x = par[x]
    return x
cseg = {}; carea = {}; ctipd = {}; alias = {}; segtip = []; vseg0 = np.full(n, -1); segparent = {}
done = np.zeros(n, bool)
for v in np.argsort(-d):
    v = int(v)
    roots = {find(u) for u in nbr[v] if done[u]}
    done[v] = True
    if not roots:
        par[v] = v; cseg[v] = len(segtip); segtip.append(v); carea[v] = M[v]; ctipd[v] = d[v]
    else:
        rs = sorted(roots, key=lambda r: carea[r], reverse=True); sv = rs[0]
        for r in rs[1:]:
            if ctipd[r]-d[v] < MIN_PERS: alias[cseg[r]] = cseg[sv]
            else: segparent[cseg[r]] = cseg[sv]
            par[r] = sv; carea[sv] += carea[r]; ctipd[sv] = max(ctipd[sv], ctipd[r])
        par[v] = sv; carea[sv] += M[v]
    vseg0[v] = cseg[find(v)]
def resolve(s):
    while s in alias: s = alias[s]
    return s
vseg = np.array([resolve(int(s)) for s in vseg0])
trunk = int(vseg[root])

# cuello de cada rama: isolinea de la distancia desde su punta, justo antes de
# derramarse sobre el padre (anillo perpendicular a la rama)
branches = sorted(set(vseg.tolist()) - {trunk})
children = {}
for b in branches:
    p = segparent.get(b); p = resolve(p) if p is not None else trunk
    children.setdefault(p, []).append(b)
def desc(b):
    out = {b}; st = [b]
    while st:
        x = st.pop()
        for c in children.get(x, []): out.add(c); st.append(c)
    return out
def flood(s0, mask):
    seen = {s0}; st = [s0]
    while st:
        x = st.pop()
        for y in nbr[x]:
            if mask[y] and y not in seen: seen.add(y); st.append(y)
    return np.array(sorted(seen))
VF = {trunk: d}; REG = {}; DBG = {}
assigned = np.zeros(n, bool); NECK = {}
def isoline_len(g, c, R_mask):
    gt = g[T]; tri = np.where((gt.min(1) < c) & (gt.max(1) > c) & free_tri & R_mask[T].any(1))[0]
    L_ = 0.0
    for ti in tri:
        a, b, cc = T[ti]; pts = []
        for x, y in ((a, b), (b, cc), (cc, a)):
            if (g[x]-c)*(g[y]-c) < 0:
                r = (c-g[x])/(g[y]-g[x]); pts.append(P[x]+r*(P[y]-P[x]))
        if len(pts) == 2: L_ += np.linalg.norm(pts[0]-pts[1])
    return L_
def postorder(x):
    for c in sorted(children.get(x, [])): yield from postorder(c)
    yield x
step = el*0.5
for b in postorder(trunk):
    if b == trunk: continue
    tipv = int(max(np.where(vseg == b)[0], key=lambda i: d[i]))
    if assigned[tipv]: continue
    g = geodesic([tipv])
    free_tri = ~assigned[T].any(1)
    mask = ~assigned
    cs = np.arange(step, g.max(), step); ps = []; Rs = []
    cut = None
    for i, c in enumerate(cs):
        R = flood(tipv, (g < c) & mask)
        if root in set(R.tolist()): break
        Rm = np.zeros(n, bool); Rm[R] = True
        ps.append(isoline_len(g, c, Rm)); Rs.append(R)
        if cut is None:
            k = len(ps)-1-FLARE_W
            if k >= 1 and (ps[-1]-ps[k])/(FLARE_W*step) > FLARE:
                cut = k; break
    if cut is None: cut = len(Rs)-1
    c_cut = cs[cut]; R = Rs[cut]
    NECK[b] = (tipv, g, c_cut, ps[cut])
    REG[b] = set(R.tolist()); VF[b] = c_cut - g; assigned[R] = True
    DBG[b] = dict(cut=round(float(c_cut), 2), reg=len(R), tip=[round(float(x), 2) for x in P[tipv]],
                  per=[round(x, 2) for x in ps[max(0, cut-3):cut+FLARE_W+1]])
branches = list(REG.keys())
# 2a pasada: extender cada corte hacia el padre (el ensanche de la base va con la rama,
# asi el agujero en el padre queda plano y no se comprime en la UV)
parent_of = {c: p for p, cl in children.items() for c in cl}
if NECK_EXT > 0:
    lab = np.full(n, trunk)
    for b in branches: lab[list(REG[b])] = b
    for b in postorder(trunk):
        if b not in NECK: continue
        anc = set(); x = b
        while x in parent_of: x = parent_of[x]; anc.add(x)
        anc.add(trunk)
        tipv, g, c_cut, pk = NECK[b]
        target = c_cut + NECK_EXT*pk/(2*np.pi)
        ok_ = (lab == b) | ((g < target) & np.isin(lab, list(anc)))
        R = flood(tipv, ok_)
        lab[R] = b; VF[b] = target - g
    for b in branches: REG[b] = set(np.where(lab == b)[0].tolist())

# segmento por cara: la region mas chica que la contiene
fseg = np.full(nf, trunk)
for b in sorted(branches, key=lambda b: -len(REG[b])):
    for f in bm.faces:
        ids = [v.index for v in f.verts]
        if sum(i in REG[b] for i in ids)*2 > len(ids): fseg[f.index] = b
fnb = [[l.link_loop_radial_next.face.index for l in f.loops] for f in bm.faces]
for _ in range(4):  # alisar bordes
    ch = 0
    for f in range(nf):
        vals, cnt = np.unique([fseg[g] for g in fnb[f]], return_counts=True)
        k = np.argmax(cnt)
        if vals[k] != fseg[f] and cnt[k] >= 3: fseg[f] = vals[k]; ch += 1
    if not ch: break
for _ in range(5):  # cada segmento conexo
    ch = 0
    for s in set(fseg.tolist()):
        fs = set(np.where(fseg == s)[0].tolist()); comps = []
        while fs:
            st = [fs.pop()]; comp = set(st)
            while st:
                x = st.pop()
                for g in fnb[x]:
                    if g in fs: fs.discard(g); comp.add(g); st.append(g)
            comps.append(comp)
        comps.sort(key=len, reverse=True)
        for comp in comps[1:]:
            for x in comp:
                o = [fseg[g] for g in fnb[x] if fseg[g] != s]
                if o: fseg[x] = max(set(o), key=o.count); ch += 1
    if not ch: break
segs = sorted(set(fseg.tolist()))

vsegs = [set(fseg[f.index] for f in v.link_faces) for v in bm.verts]
boundary_edges = set(e.index for e in bm.edges if fseg[e.link_faces[0].index] != fseg[e.link_faces[1].index])

# ---------- 3) costuras verticales ----------
cxy = P[root, :2]
def edge_between(a, b):
    for e in bm.verts[a].link_edges:
        if e.other_vert(bm.verts[a]).index == b: return e
seam_edges = set(); info = {}
UV = {}  # (face, vert) -> (U,V)
for s in segs:
    sfaces = np.where(fseg == s)[0]
    Vs = VF[s]
    interior = [i for i in range(n) if vsegs[i] == {s}]
    interior_set = set(interior)
    tip = max(interior, key=lambda i: Vs[i])
    if s == trunk:
        sources = [root]
        hd = lambda i: HIDDEN_TRUNK
    else:
        # la costura arranca en el borde exterior (el que no es agujero de una hija):
        # el vecino que no es hija y con el que comparte mas vertices
        kids = {c for c, p_ in parent_of.items() if p_ == s}
        cnt = {}
        for i in range(n):
            if s in vsegs[i]:
                for o_ in vsegs[i] - {s} - kids: cnt[o_] = cnt.get(o_, 0) + 1
        sources = []
        if cnt:
            outer = max(cnt, key=cnt.get)
            sources = [i for i in range(n) if s in vsegs[i] and outer in vsegs[i]]
        if not sources: sources = [i for i in range(n) if s in vsegs[i] and len(vsegs[i]) > 1]
        def hd(i):
            h = np.array([cxy[0]-P[i, 0], cxy[1]-P[i, 1], 0.0]); nn = np.linalg.norm(h)
            return h/nn if nn > 1e-6 else HIDDEN_TRUNK
    dist = {x: 0.0 for x in sources}; prev = {}
    pq = [(0.0, x) for x in sources]; heapq.heapify(pq)
    while pq:
        c0, a = heapq.heappop(pq)
        if c0 > dist.get(a, 1e18): continue
        if a == tip: break
        for b in nbr[a]:
            if b not in interior_set: continue
            l = np.linalg.norm(P[b]-P[a]); along = np.clip((Vs[b]-Vs[a])/l, -1, 1)
            vis = (1-np.dot(N[b], hd(b)))/2
            c1 = c0 + l*(0.15 + 8*(1-along) + 1.5*vis)
            if c1 < dist.get(b, 1e18): dist[b] = c1; prev[b] = a; heapq.heappush(pq, (c1, b))
    path = [tip]
    while path[-1] in prev: path.append(prev[path[-1]])
    path.reverse()
    pedges = [edge_between(path[i], path[i+1]) for i in range(len(path)-1)]
    for e in pedges: seam_edges.add(e.index)
    # lados de la costura
    side = {}  # vert -> set de caras del lado L
    apex = {tip}
    if s == trunk: apex.add(root)
    for idx, v in enumerate(path):
        if v in apex: continue
        e_out = pedges[idx]; e_in = pedges[idx-1] if idx > 0 else None
        bv = bm.verts[v]
        l = next(l for l in bv.link_loops if l.edge == e_out and fseg[l.face.index] == s)
        Ls = set()
        for _ in range(64):
            Ls.add(l.face.index)
            e = l.link_loop_prev.edge
            if e == e_in or e.index in boundary_edges: break
            l = l.link_loop_prev.link_loop_radial_next
        side[v] = Ls
    # nodos
    node = {}
    def nid(f, v):
        key = (v, 'L' if f in side[v] else 'R') if v in side else (v,)
        if key not in node: node[key] = len(node)
        return node[key]
    corner = {}
    for f in sfaces:
        for v in bm.faces[f].verts: corner[(int(f), v.index)] = nid(int(f), v.index)
    m_ = len(node); Q = np.zeros((m_, 3)); Dn = np.zeros(m_)
    for (f, v), k in corner.items(): Q[k] = P[v]; Dn[k] = Vs[v]
    # perimetro por nivel (ancho real del tubo a cada altura)
    lo, hi = Dn.min(), Dn.max(); levels = np.linspace(lo, hi, 96); per = np.zeros(len(levels))
    stris = [tuple(corner[(int(f), x)] for x in t) for f in sfaces for t in FT[f]]
    for a, b, c in stris:
        mn, mx = min(Dn[a], Dn[b], Dn[c]), max(Dn[a], Dn[b], Dn[c])
        for li in np.where((levels > mn) & (levels < mx))[0]:
            t0 = levels[li]; pts = []
            for x, y in ((a, b), (b, c), (c, a)):
                if (Dn[x]-t0)*(Dn[y]-t0) < 0:
                    r = (t0-Dn[x])/(Dn[y]-Dn[x]); pts.append(Q[x]+r*(Q[y]-Q[x]))
            if len(pts) == 2: per[li] += np.linalg.norm(pts[0]-pts[1])
    per = np.convolve(np.pad(per, 3, mode='edge'), np.ones(7)/7, mode='valid')
    if s != trunk:  # rama: el ancho no crece hacia la punta (evita colas en el corte)
        per = np.maximum.accumulate(per[::-1])[::-1]
    # limitar dC/dV: si el ancho cambia rapido, las rayas cerca de la costura se inclinan
    dl = levels[1]-levels[0]
    for _ in range(4000):
        if np.abs(np.diff(per)).max() <= SLOPE*dl: break
        per = np.convolve(np.pad(per, 1, mode='edge'), [0.25, 0.5, 0.25], mode='valid')
    C = np.maximum(np.interp(Dn, levels, per), 1e-3)
    # theta alrededor del tubo: salta 0->1 en la costura y su gradiente sigue N x grad V / C
    Ls_ = np.zeros((m_, m_)); rhs = np.zeros(m_); Xs = []
    for a, b, c in stris:
        ck, ca, cb = cot(a, b, c, Q), cot(b, c, a, Q), cot(c, a, b, Q)
        for x, y, w in ((a, b, ck), (b, c, ca), (c, a, cb)):
            w = max(0.5*w, 1e-4); Ls_[x, y] += w; Ls_[y, x] += w
        nrm = np.cross(Q[b]-Q[a], Q[c]-Q[a]); A2 = np.linalg.norm(nrm); nh = nrm/A2
        gv = (Dn[a]*np.cross(nh, Q[c]-Q[b]) + Dn[b]*np.cross(nh, Q[a]-Q[c]) + Dn[c]*np.cross(nh, Q[b]-Q[a]))/A2
        gl = np.linalg.norm(gv)
        X = np.cross(nh, gv)/gl/((C[a]+C[b]+C[c])/3) if gl > 0 else np.zeros(3)
        Xs.append(X)
        rhs[a] += 0.5*(ck*np.dot(Q[b]-Q[a], X) + cb*np.dot(Q[c]-Q[a], X))
        rhs[b] += 0.5*(ca*np.dot(Q[c]-Q[b], X) + ck*np.dot(Q[a]-Q[b], X))
        rhs[c] += 0.5*(cb*np.dot(Q[a]-Q[c], X) + ca*np.dot(Q[b]-Q[c], X))
    Ls_[np.diag_indices(m_)] = -Ls_.sum(1)
    fixed = {k: (0.0 if key[1] == 'L' else 1.0) for key, k in node.items() if len(key) == 2}
    free = [k for k in range(m_) if k not in fixed]
    fk = np.array(list(fixed.keys())); fv = np.array(list(fixed.values()))
    A_ = Ls_[np.ix_(free, free)]
    def solve_theta(sign):
        th = np.zeros(m_); th[fk] = fv
        th[free] = np.linalg.solve(A_, sign*rhs[free] - Ls_[np.ix_(free, fk)] @ fv)
        return th
    th0 = solve_theta(0.0)
    al = 0.0
    for (a, b, c), X in zip(stris, Xs):
        nrm = np.cross(Q[b]-Q[a], Q[c]-Q[a]); A2 = np.linalg.norm(nrm); nh = nrm/A2
        gt = (th0[a]*np.cross(nh, Q[c]-Q[b]) + th0[b]*np.cross(nh, Q[a]-Q[c]) + th0[c]*np.cross(nh, Q[b]-Q[a]))/A2
        al += np.dot(gt, X)*A2
    sgn = 1.0 if al >= 0 else -1.0
    # costura periodica: theta_R = theta_L + 1, sin fijar su valor (las rayas no copian la costura)
    red = {}
    ridx = np.full(m_, -1); off = np.zeros(m_)
    for key, k in node.items():
        if len(key) == 1 or key[1] == 'L': ridx[k] = len(red); red[k] = ridx[k]
    for key, k in node.items():
        if len(key) == 2 and key[1] == 'R': ridx[k] = ridx[node[(key[0], 'L')]]; off[k] = 1.0
    nr = len(red)
    Sm = np.zeros((m_, nr)); Sm[np.arange(m_), ridx] = 1.0
    K = -Ls_
    A_r = Sm.T @ K @ Sm; b_r = -Sm.T @ (K @ off + sgn*rhs)
    A_r[0, :] = 0; A_r[0, 0] = 1; b_r[0] = 0
    u = Sm @ np.linalg.solve(A_r, b_r) + off
    thL = np.array([u[k] for key, k in node.items() if len(key) == 2 and key[1] == 'L'])
    U = (u - (thL.mean()+0.5))*C; V = Dn.copy()
    # sin espejo
    sa = 0.0
    for a, b, c in stris:
        sa += (U[b]-U[a])*(V[c]-V[a]) - (U[c]-U[a])*(V[b]-V[a])
    if sa < 0: U = -U
    for (f, v), k in corner.items(): UV[(f, v)] = (U[k], V[k])
    if DEBUG_SEG is not None and s == DEBUG_SEG:
        bins = np.linspace(lo, hi, 12)
        DBGSEG = []
        for i in range(len(bins)-1):
            m = (Dn >= bins[i]) & (Dn < bins[i+1])
            if m.any(): DBGSEG.append((round(float(bins[i]), 2), round(float(u[m].min()), 2), round(float(u[m].max()), 2), round(float(np.interp(bins[i], levels, per)), 2)))
        info['dbgseg'] = dict(rows=DBGSEG, sgn=sgn, seam=len(path), thL=[round(float(x), 2) for x in thL[:20]])
    info[int(s)] = dict(faces=len(sfaces), chi=None, len=round(float(hi-lo), 2), seam_len=len(path),
                        tip_z=round(float(P[tip, 2]), 2))

# Euler por segmento
for s in segs:
    fs = set(np.where(fseg == s)[0].tolist())
    vs = set(v.index for f in fs for v in bm.faces[f].verts)
    es = set(e.index for f in fs for e in bm.faces[f].edges)
    info[int(s)]["chi"] = len(vs)-len(es)+len(fs)

# compresion de los agujeros del tronco: largo UV / largo 3D del borde (1 = sin compresion)
holes = {}
for e in bm.edges:
    f0, f1 = e.link_faces
    if fseg[f0.index] == fseg[f1.index]: continue
    ft = f0 if fseg[f0.index] == trunk else (f1 if fseg[f1.index] == trunk else None)
    if ft is None: continue
    other = fseg[f1.index] if ft is f0 else fseg[f0.index]
    a_, b_ = e.verts
    u0 = np.array(UV[(ft.index, a_.index)]); u1 = np.array(UV[(ft.index, b_.index)])
    h = holes.setdefault(int(other), [0.0, 0.0]); h[0] += np.linalg.norm(u1-u0); h[1] += e.calc_length()
hole_ratio = {k: round(v[0]/v[1], 2) for k, v in holes.items()}
extrema = {}
for s_ in segs:
    Vs_ = VF[s_]; cnt = 0
    for i in range(n):
        if vsegs[i] != {s_} or i == root: continue
        nb = [Vs_[j] for j in nbr[i]]
        if Vs_[i] > max(nb) or Vs_[i] < min(nb): cnt += 1
    extrema[int(s_)] = cnt

# ---------- escribir ----------
for e in bm.edges: e.seam = (e.index in boundary_edges) or (e.index in seam_edges)
uvl = bm.loops.layers.uv.get(UVNAME) or bm.loops.layers.uv.new(UVNAME)
for f in bm.faces:
    for l in f.loops: l[uvl].uv = UV[(f.index, l.vert.index)]
bm.to_mesh(me); me.update()
me.uv_layers.active = me.uv_layers[UVNAME]
# distorsion
ratios = []
for f in bm.faces:
    for l in f.loops:
        a, b = l, l.link_loop_next
        l3 = (a.vert.co-b.vert.co).length
        l2 = (a[uvl].uv-b[uvl].uv).length
        if l3 > 1e-6: ratios.append(l2/l3)
ratios = np.array(ratios); ratios /= np.median(ratios)
bm.free()
_res = dict(extrema=extrema, holes=hole_ratio, dbg=DBG, segments=len(segs), trunk=trunk, root=root, info=info,
            stretch_p05=round(float(np.percentile(ratios, 5)), 3), stretch_p95=round(float(np.percentile(ratios, 95)), 3),
            seams=len(boundary_edges)+len(seam_edges), dmax=round(float(d.max()), 2))
