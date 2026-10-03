import numpy as np, math, json, pickle, time
N=3601
LAT0=-49.126383; LON0=-74.415707
M_LAT=111132.0; M_LON=111320.0*math.cos(math.radians(LAT0))
# ---- scene frame: world = local - ORIGIN ----
ORIGIN=(120.0,40.0)          # local metres mapped to Unity world (0,0)
HALF=1600.0                  # terrain half-size -> 3200 x 3200 m (era 800 hasta el 2026-09-21)

_T={}
def _tile(la,lo):
    k=(la,lo)
    if k not in _T:
        a=np.fromfile(f"S{abs(la):02d}W{abs(lo):03d}.hgt",dtype='>i2').reshape(N,N).astype(np.float32)
        a[a<-1000]=0
        _T[k]=a
    return _T[k]

def dem(lat,lon):
    """vectorised bilinear DEM sample over the 4-tile mosaic"""
    lat=np.asarray(lat,dtype=np.float64); lon=np.asarray(lon,dtype=np.float64)
    out=np.zeros(lat.shape,dtype=np.float64)
    la=np.floor(lat).astype(int); lo=np.floor(lon).astype(int)
    for LA in (-50,-49):
        for LO in (-75,-74):
            m=(la==LA)&(lo==LO)
            if not m.any(): continue
            t=_tile(LA,LO)
            r=(LA+1-lat[m])*(N-1); c=(lon[m]-LO)*(N-1)
            r=np.clip(r,0,N-1.001); c=np.clip(c,0,N-1.001)
            r0=r.astype(int); c0=c.astype(int); fr=r-r0; fc=c-c0
            out[m]=(t[r0,c0]*(1-fr)*(1-fc)+t[r0+1,c0]*fr*(1-fc)
                   +t[r0,c0+1]*(1-fr)*fc+t[r0+1,c0+1]*fr*fc)
    return out

def dem_xy(X,Y):
    """X east / Y north in LOCAL metres"""
    return dem(LAT0+np.asarray(Y)/M_LAT, LON0+np.asarray(X)/M_LON)

# ---------- signed distance to the OSM coastline (land positive) ----------
def simplify(pts,tol):
    if len(pts)<3: return pts
    st=[(0,len(pts)-1)]; keep=np.zeros(len(pts),bool); keep[0]=keep[-1]=True
    P=np.asarray(pts)
    while st:
        i,j=st.pop()
        if j<=i+1: continue
        a=P[i]; b=P[j]; d=b-a; L=math.hypot(*d)
        seg=P[i+1:j]
        if L<1e-9: dist=np.hypot(seg[:,0]-a[0],seg[:,1]-a[1])
        else: dist=np.abs(d[0]*(a[1]-seg[:,1])-d[1]*(a[0]-seg[:,0]))/L
        k=int(np.argmax(dist))
        if dist[k]>tol:
            k+=i+1; keep[k]=True; st.append((i,k)); st.append((k,j))
    return P[keep]

def coast_segments(tol=4.0):
    d=json.load(open('osm2.json'))
    segs=[]
    for e in d['elements']:
        if e['type']!='way' or e.get('tags',{}).get('natural')!='coastline': continue
        g=e.get('geometry') or []
        if len(g)<2: continue
        pts=[((p['lon']-LON0)*M_LON,(p['lat']-LAT0)*M_LAT) for p in g]
        pts=simplify(pts,tol)
        for i in range(len(pts)-1):
            segs.append((pts[i][0],pts[i][1],pts[i+1][0],pts[i+1][1]))
    return np.array(segs,dtype=np.float64)

# Rectangulo de la descarga de Overpass (ver README). Dentro de el la costa esta
# completa; afuera no hay dato.
BBOX=(-49.175,-74.485,-49.075,-74.345)   # lat_min, lon_min, lat_max, lon_max

def _rect_local():
    la0,lo0,la1,lo1=BBOX
    return ((lo0-LON0)*M_LON,(la0-LAT0)*M_LAT,(lo1-LON0)*M_LON,(la1-LAT0)*M_LAT)

def coast_rings():
    """Anillos de tierra cerrados. Las ways de costa se encadenan y se recortan al
    rectangulo de la descarga; cada cadena abierta se cierra caminando el borde
    del rectangulo en sentido antihorario, que deja la tierra a la izquierda como
    en OSM. Sin esto, el signo 'tierra a la izquierda del tramo mas cercano' se
    rompe en los extremos abiertos y dibuja cunas rectas de tierra o de mar."""
    d=json.load(open('osm2.json'))
    ways=[]
    for e in d['elements']:
        if e['type']!='way' or e.get('tags',{}).get('natural')!='coastline': continue
        g=e.get('geometry') or []
        if len(g)>=2: ways.append([((p['lon']-LON0)*M_LON,(p['lat']-LAT0)*M_LAT) for p in g])
    # encadenar por extremos (misma direccion: el final de una es el inicio de otra)
    key=lambda p:(round(p[0],2),round(p[1],2))
    por_inicio={}
    for w in ways: por_inicio.setdefault(key(w[0]),[]).append(w)
    usados=set(); cadenas=[]
    for w in ways:
        if id(w) in usados: continue
        usados.add(id(w)); c=list(w)
        # hacia adelante
        while key(c[-1])!=key(c[0]):
            sig=[x for x in por_inicio.get(key(c[-1]),[]) if id(x) not in usados]
            if not sig: break
            usados.add(id(sig[0])); c+=sig[0][1:]
        cadenas.append(c)
    # unir cadenas abiertas cuyo final coincide con el inicio de otra
    cambio=True
    while cambio:
        cambio=False
        for i,a in enumerate(cadenas):
            if key(a[0])==key(a[-1]): continue
            for j,b in enumerate(cadenas):
                if i!=j and key(a[-1])==key(b[0]):
                    cadenas[i]=a+b[1:]; cadenas.pop(j); cambio=True; break
            if cambio: break

    x0,y0,x1,y1=_rect_local()
    P=2*(x1-x0)+2*(y1-y0)
    def dentro(p): return x0<=p[0]<=x1 and y0<=p[1]<=y1
    def t_de(p):   # posicion antihoraria sobre el perimetro, desde (x0,y0)
        x,y=p
        if abs(y-y0)<1e-6: return x-x0
        if abs(x-x1)<1e-6: return (x1-x0)+(y-y0)
        if abs(y-y1)<1e-6: return (x1-x0)+(y1-y0)+(x1-x)
        return 2*(x1-x0)+(y1-y0)+(y1-y)
    esquinas=[(x1-x0,(x1,y0)),((x1-x0)+(y1-y0),(x1,y1)),(2*(x1-x0)+(y1-y0),(x0,y1)),(P,(x0,y0))]
    def corte(a,b):  # interseccion del segmento a->b con el borde
        best=None
        for (ex,ey,fx,fy) in ((x0,y0,x1,y0),(x1,y0,x1,y1),(x1,y1,x0,y1),(x0,y1,x0,y0)):
            dx,dy=b[0]-a[0],b[1]-a[1]; gx,gy=fx-ex,fy-ey
            den=dx*gy-dy*gx
            if abs(den)<1e-12: continue
            t=((ex-a[0])*gy-(ey-a[1])*gx)/den; u=((ex-a[0])*dy-(ey-a[1])*dx)/den
            if 0<=t<=1 and 0<=u<=1 and (best is None or t<best[0]): best=(t,(a[0]+t*dx,a[1]+t*dy))
        return best[1] if best else None

    anillos=[]; tramos=[]
    for c in cadenas:
        cerrada=key(c[0])==key(c[-1])
        if all(dentro(p) for p in c):
            if cerrada: anillos.append(c)
            continue
        if cerrada:   # rotar para empezar afuera
            k=next(i for i,p in enumerate(c) if not dentro(p)); c=c[k:-1]+c[:k+1]
        actual=None
        for i in range(len(c)):
            p=c[i]
            if i>0:
                q=c[i-1]
                if dentro(q)!=dentro(p):
                    x=corte(q,p) if dentro(p) else corte(q,p)
                    if x is None: continue
                    if dentro(p): actual=[x]
                    elif actual is not None: actual.append(x); tramos.append(actual); actual=None
            if dentro(p) and actual is not None: actual.append(p)
            elif dentro(p) and actual is None and i==0: actual=[p]   # cadena que arranca adentro (dato incompleto)
    # cerrar por el borde
    pend=[t for t in tramos if len(t)>=2]
    while pend:
        ring=list(pend.pop(0)); inicio_t=t_de(ring[0])
        while True:
            t=t_de(ring[-1])
            cands=[(((t_de(x[0])-t)%P),k) for k,x in enumerate(pend)]
            dt_ini=(inicio_t-t)%P
            dmin,kmin=min(cands) if cands else (P+1,None)
            fin=dt_ini<=dmin
            dest=dt_ini if fin else dmin
            for (tc,pc) in esquinas+[(tc+P,pc) for tc,pc in esquinas]:
                if t<tc<t+dest: ring.append(pc)
            # ordenar las esquinas agregadas: ya salen en orden porque esquinas esta ordenada
            if fin: break
            ring+=pend.pop(kmin)
        anillos.append(ring)
    return anillos

_MASK_RINGS=None
def _scanline(xs,ys,rings):
    """Relleno par-impar sobre una grilla regular (xs columnas, ys filas)."""
    E=[]
    for r in rings:
        P=np.asarray(r)
        if len(P)<3: continue
        Q=np.roll(P,-1,axis=0); E.append(np.column_stack([P,Q]))
    E=np.vstack(E); ax,ay,bx,by=E[:,0],E[:,1],E[:,2],E[:,3]
    M=np.zeros((len(ys),len(xs)),bool)
    for j,yy in enumerate(ys):
        m=((ay<=yy)&(by>yy))|((by<=yy)&(ay>yy))
        if not m.any(): continue
        xi=np.sort(ax[m]+(yy-ay[m])*(bx[m]-ax[m])/(by[m]-ay[m]))
        # cuantos cruces hay a la izquierda de cada columna: impar = adentro
        M[j]=(np.searchsorted(xi,xs)%2)==1
    return M

def land_mask(X,Y):
    """True en tierra, por relleno par-impar de los anillos de coast_rings()."""
    global _MASK_RINGS
    if _MASK_RINGS is None: _MASK_RINGS=coast_rings()
    X=np.asarray(X); Y=np.asarray(Y)
    regular=X.ndim==2 and np.allclose(X,X[0:1,:]) and np.allclose(Y,Y[:,0:1])
    if regular:
        m=_scanline(X[0],Y[:,0],_MASK_RINGS).ravel()
        pts=np.column_stack([X.ravel(),Y.ravel()])
    else:
        from matplotlib.path import Path
        pts=np.column_stack([np.ravel(X),np.ravel(Y)])
        m=np.zeros(len(pts),bool)
        for r in _MASK_RINGS:
            if len(r)<3: continue
            R=np.asarray(r); lo=R.min(0); hi=R.max(0)
            k=np.where((pts[:,0]>=lo[0])&(pts[:,0]<=hi[0])&(pts[:,1]>=lo[1])&(pts[:,1]<=hi[1]))[0]
            if len(k): m[k]^=Path(R).contains_points(pts[k])
    x0,y0,x1,y1=_rect_local()
    fuera=(pts[:,0]<x0)|(pts[:,0]>x1)|(pts[:,1]<y0)|(pts[:,1]>y1)
    m[fuera]=False   # sin dato: mar (el fondo lejano usa el DEM ahi)
    return m.reshape(np.shape(X))

def sdf_grid(X,Y,S,block=48):
    """signed distance, + on land. El signo sale de land_mask (anillos cerrados),
    la magnitud de la distancia al tramo de costa mas cercano."""
    ny,nx=X.shape
    out=np.zeros((ny,nx))
    ax,ay,bx,by=S[:,0],S[:,1],S[:,2],S[:,3]
    dx=bx-ax; dy=by-ay; L2=dx*dx+dy*dy; L2[L2==0]=1e-9
    smnx=np.minimum(ax,bx); smxx=np.maximum(ax,bx)
    smny=np.minimum(ay,by); smxy=np.maximum(ay,by)
    MARGIN=900.0
    for j0 in range(0,ny,block):
        j1=min(j0+block,ny)
        for i0 in range(0,nx,block):
            i1=min(i0+block,nx)
            px=X[j0:j1,i0:i1].ravel(); py=Y[j0:j1,i0:i1].ravel()
            bx0,bx1=px.min()-MARGIN,px.max()+MARGIN
            by0,by1=py.min()-MARGIN,py.max()+MARGIN
            m=(smxx>=bx0)&(smnx<=bx1)&(smxy>=by0)&(smny<=by1)
            if m.sum()<8: m=np.ones(len(S),bool)
            iax,iay,idx,idy,iL2=ax[m],ay[m],dx[m],dy[m],L2[m]
            t=np.clip(((px[:,None]-iax)*idx+(py[:,None]-iay)*idy)/iL2,0,1)
            cx=iax+t*idx; cy=iay+t*idy
            d2=(px[:,None]-cx)**2+(py[:,None]-cy)**2
            k=np.argmin(d2,axis=1); r=np.arange(px.size)
            dist=np.sqrt(d2[r,k])
            cr=idx[k]*(py-iay[k])-idy[k]*(px-iax[k])
            out[j0:j1,i0:i1]=dist.reshape(j1-j0,i1-i0)
    return np.where(land_mask(X,Y),out,-out)

# ---------- value-noise fbm ----------
def _vnoise(X,Y,cell,seed):
    rng=np.random.default_rng(seed)
    gx=np.floor(X/cell).astype(int); gy=np.floor(Y/cell).astype(int)
    fx=X/cell-gx; fy=Y/cell-gy
    ox,oy=gx.min(),gy.min()
    G=rng.random((gy.max()-oy+2,gx.max()-ox+2))
    gx-=ox; gy-=oy
    u=fx*fx*(3-2*fx); v=fy*fy*(3-2*fy)
    a=G[gy,gx]; b=G[gy,gx+1]; c=G[gy+1,gx]; d=G[gy+1,gx+1]
    return (a*(1-u)+b*u)*(1-v)+(c*(1-u)+d*u)*v

def fbm(X,Y,cell,octaves=5,gain=0.5,lac=2.0,seed=7):
    t=np.zeros(X.shape); amp=1.0; tot=0.0; c=cell
    for o in range(octaves):
        t+=amp*_vnoise(X,Y,c,seed+o*131); tot+=amp; amp*=gain; c/=lac
    return t/tot

def blur(A,px,passes=3):
    """separable box blur, radius px, repeated"""
    px=max(1,int(px))
    B=A.astype(np.float64)
    for _ in range(passes):
        k=np.ones(2*px+1)/(2*px+1)
        B=np.apply_along_axis(lambda m:np.convolve(np.pad(m,px,mode='edge'),k,'valid'),1,B)
        B=np.apply_along_axis(lambda m:np.convolve(np.pad(m,px,mode='edge'),k,'valid'),0,B)
    return B

# ---------- hundimiento del borde del Terrain ----------
# El Terrain es un cuadrado de 1600 m y la tierra llegaba hasta el borde mismo:
# desde dentro de la escena eso se ve como un tajo recto, porque ahi termina la
# geometria detallada y empieza la malla de fondo, que es otra cosa. La franja
# del perimetro se hunde bajo el nivel del mar para que la juntura quede debajo
# del agua, donde no se ve. El ancho se modula con ruido; con un ancho constante
# la costa nueva seria un rectangulo, que es otro tajo, solo que mas disimulado.
W_BORDE = 150.0     # ancho medio de la franja, en metros
D_BORDE = 28.0      # profundidad a la que queda el borde del cuadrado
FUERA_BORDE = 1.7   # la malla de fondo tarda 1.7x mas en recuperar el relieve

def _hash2(ix, iy, seed):
    h = ix.astype(np.int64)*374761393 + iy.astype(np.int64)*668265263 + seed*1274126177
    h = (h ^ (h >> 13))*1274126177
    h = h & 0x7fffffff
    return ((h ^ (h >> 16)) % 65536).astype(np.float64)/65536.0

def _hnoise(x, y, cell, seed):
    gx = np.floor(x/cell); gy = np.floor(y/cell)
    fx = x/cell-gx; fy = y/cell-gy
    ix = gx.astype(np.int64); iy = gy.astype(np.int64)
    u = fx*fx*(3-2*fx); v = fy*fy*(3-2*fy)
    a = _hash2(ix,iy,seed);   b = _hash2(ix+1,iy,seed)
    c = _hash2(ix,iy+1,seed); d = _hash2(ix+1,iy+1,seed)
    return (a*(1-u)+b*u)*(1-v)+(c*(1-u)+d*u)*v

def ancho_borde(wx, wz, w0=None):
    """Ancho local de la franja que se hunde. En coordenadas de MUNDO, para que
    el Terrain y la malla de fondo usen el mismo ruido y la costa no se corte."""
    w0 = W_BORDE if w0 is None else w0
    n = 0.62*_hnoise(wx,wz,230.0,1771) + 0.38*_hnoise(wx,wz,85.0,1778)
    return w0*(0.52+1.02*n)

def _suave(t):
    t = np.clip(t,0.0,1.0); return t*t*(3.0-2.0*t)

def mascara_borde_terrain(wx, wz, w0=None):
    """0 en el borde del cuadrado -> 1 tierra adentro."""
    db = np.minimum(np.minimum(wx+HALF, HALF-wx), np.minimum(wz+HALF, HALF-wz))
    return _suave(db/ancho_borde(wx,wz,w0))

def mascara_borde_fondo(wx, wz, w0=None):
    """0 en el borde del cuadrado -> 1 mar afuera, sobre la malla polar."""
    r = np.hypot(wx,wz); th = np.arctan2(wz,wx)
    r0 = HALF/np.maximum(np.abs(np.cos(th)),np.abs(np.sin(th)))
    return _suave((r-r0)/(ancho_borde(wx,wz,w0)*FUERA_BORDE))

# Apagado el 2026-09-21 al pasar a 3200 m: con el cuadrado grande la tierra del
# oeste y del norte llega al borde, y hundirla dejaba un foso con acantilados entre
# el Terrain y la malla de fondo. El anillo 0 del fondo ya se fuerza a la altura
# real del borde (desfase medido: 0.06 m de media), asi que la juntura empalma sin
# hundir nada.
HUNDIR_BORDE = False

def hundir_borde(H, t, d=None):
    """Nunca levanta el fondo: solo baja lo que este por encima del objetivo."""
    if not HUNDIR_BORDE: return H
    d = D_BORDE if d is None else d
    return np.minimum(H, t*H + (1.0-t)*(-d))
