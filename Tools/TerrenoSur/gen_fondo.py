exec(open('gen_common.py').read())
import numpy as np, pickle, time, struct
t0=time.time()
NA=384; NR=176; RMAX=30000.0
EXAG=1.6

def boxblur(A,r,passes=3):
    B=A.astype(np.float64)
    for _ in range(passes):
        for ax in (0,1):
            B=np.moveaxis(B,ax,0)
            P=np.pad(B,((r+1,r),(0,0)),mode='edge'); C=np.cumsum(P,axis=0)
            B=(C[2*r+1:]-C[:-(2*r+1)])/(2*r+1); B=np.moveaxis(B,0,ax)
    return B
def ridged(X,Y,cell,oct,seed):
    return 1.0-np.abs(2.0*fbm(X,Y,cell,oct,0.55,2.0,seed)-1.0)

# ---- polar grid: ring 0 sits exactly on the terrain's square edge ----
th=np.linspace(0,2*np.pi,NA,endpoint=False)
r0=HALF/np.maximum(np.abs(np.cos(th)),np.abs(np.sin(th)))     # 800 .. 1131 m
u=np.linspace(0,1,NR)[:,None]
R=r0[None,:]*np.power(RMAX/r0[None,:],u)                      # [NR,NA] geometric rings
WX=R*np.cos(th)[None,:]; WZ=R*np.sin(th)[None,:]
LX=WX+ORIGIN[0]; LY=WZ+ORIGIN[1]
print("rings %d x %d, spacing %.0f m at r=%.0f, %.0f m at r=%.0f"%(
      NR,NA,R[1,0]-R[0,0],R[0,0],R[-1,0]-R[-2,0],R[-1,0]),flush=True)

# ---- far field: DEM, area-averaged, water flattened to zero ----
foot=np.clip(np.gradient(R,axis=0),25,900)
far=np.zeros_like(R); n=0
for ox in (-0.42,0,0.42):
    for oy in (-0.42,0,0.42):
        far+=dem_xy(LX+ox*foot,LY+oy*foot); n+=1
far/=n
far=np.maximum(far-11.0,0.0)
far=boxblur(far,1,2)
far*=1.06

# ---- near field: exactly the terrain's own formula, so the edge matches ----
S=coast_segments()
print("sdf...",flush=True)
d=sdf_grid(LX,LY,S)
print("  %.0fs"%(time.time()-t0),flush=True)
demv=dem_xy(LX,LY)
macro=boxblur(np.maximum(demv-9.0,0.0),3)
polys=pickle.load(open('landcover.pkl','rb'))
villa=np.zeros_like(R)          # residential flattening is inside the terrain only

shelf=7.0*(1.0-np.exp(-np.maximum(d,0)/26.0))
ramp=np.clip(np.maximum(d,0)/70.0,0,1)**0.7
isla=np.clip((boxblur((d>0).astype(float),4,2)-0.22)*2.4,0,1)
near=shelf+EXAG*macro*ramp*isla
kmod=np.clip(np.maximum(d,0)/38.0,0,1)*(0.25+0.75*isla)
knob=(ridged(LX,LY,115.0,3,seed=21)**2.2
     +0.50*ridged(LX,LY,44.0,2,seed=33)**2.5
     +0.20*ridged(LX,LY,17.0,2,seed=55)**2.8)/1.70
near+=kmod*12.5*knob
T=10.5
fr=near/T-np.floor(near/T); sm=np.clip((fr-0.34)/0.30,0,1); sm=sm*sm*(3-2*sm)
near=near*(1-0.20*kmod)+((np.floor(near/T)+sm)*T)*(0.20*kmod)
dmod=np.clip(np.maximum(d,0)/15.0,0,1)
near+=dmod*(2.8*(fbm(LX,LY,30.0,4,0.5,2.0,seed=44)-0.5)
           +1.1*(fbm(LX,LY,11.0,3,0.5,2.0,seed=88)-0.5))
ad=np.maximum(-d,0)
depth=2.2*(1.0-np.exp(-ad/18.0))+62.0*(1.0-np.exp(-ad/320.0))
depth+=np.clip(ad/60.0,0,1)*3.2*(fbm(LX,LY,110.0,4,0.5,2.0,seed=63)-0.5)
near=np.where(d>0,near,-depth)

# La costa OSM cubre el rectangulo BBOX (unos +-5 km); afuera land_mask da mar, asi
# que antes del borde del dato se pasa al DEM.
x0,y0,x1,y1=_rect_local()
margen=np.minimum(np.minimum(LX-x0,x1-LX),np.minimum(LY-y0,y1-LY))
valid=np.clip((margen-300.0)/1500.0,0,1)
w=np.clip((5500.0-R)/2500.0,0,1)*valid
H=near*w+far*(1-w)

# El mismo hundido que el Terrain, prolongado hacia afuera: si la malla
# conservara su relieve real justo al lado de un borde de Terrain hundido,
# habria una pared de tierra pegada a la juntura. Va ANTES de forzar el anillo
# 0, para que ese ajuste vuelva a ser lo que era: una correccion chica.
H=hundir_borde(H, mascara_borde_fondo(WX,WZ))

# Ring 0 cae justo sobre el borde del Terrain: se fuerza a la altura real del
# heightmap y la correccion se reparte hacia afuera, si no queda un escalon.
hn=np.fromfile('heights.bin',dtype=np.float32)
RESH=int(round(np.sqrt(hn.size))); hn=hn.reshape(RESH,RESH)
tH=hn.astype(np.float64)*420.0-90.0   # BASE_Y y RANGE_Y de gen_terrain.py
gx=np.clip((WX[0]+HALF)/(2*HALF)*(RESH-1),0,RESH-1.001)
gz=np.clip((WZ[0]+HALF)/(2*HALF)*(RESH-1),0,RESH-1.001)
i0=gx.astype(int); j0=gz.astype(int); fx=gx-i0; fz=gz-j0
edge=(tH[j0,i0]*(1-fx)*(1-fz)+tH[j0,i0+1]*fx*(1-fz)
     +tH[j0+1,i0]*(1-fx)*fz+tH[j0+1,i0+1]*fx*fz)
delta=edge-H[0]
print("  edge mismatch before fix: mean %.2f m, max %.2f m"%(np.abs(delta).mean(),np.abs(delta).max()))
K=16   # el fondo es mucho mas grueso que el Terrain: la correccion se reparte en ~450 m
H[:K]+=delta[None,:]*(1.0-np.arange(K)[:,None]/K)

# beyond the detailed sea plane the mesh IS the sea: park water just under y=0
SEA_R=2400.0   # la malla del mar de Sur (OceanGrid_Sur) mide 5000 m
H=np.where((H<=0.35)&(R>SEA_R),-0.6,H)
H=np.where((R>SEA_R)&(H<-0.6),-0.6,H)      # no seabed out there, only surface

# long swells of relief so the far ridges do not read as a smooth cone
H+= np.clip((R-2500.0)/4000.0,0,1)*np.clip(H/60.0,0,1)*22.0*(fbm(LX,LY,900.0,4,0.5,2.0,seed=171)-0.5)
H=np.where(R>SEA_R,np.maximum(H,-0.6),H)
print("fondo height: max %.0f  p99 %.0f  water frac %.2f"%(H.max(),np.percentile(H,99),(H<=-0.5).mean()))

with open('fondo.bin','wb') as f:
    f.write(struct.pack('<ii',NR,NA))
    np.ascontiguousarray(WX,dtype=np.float32).tofile(f)
    np.ascontiguousarray(WZ,dtype=np.float32).tofile(f)
    np.ascontiguousarray(H ,dtype=np.float32).tofile(f)
print("verts %d  tris %d   %.0fs"%(NR*NA,(NR-1)*NA*2,time.time()-t0))
