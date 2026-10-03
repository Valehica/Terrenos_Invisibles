# Vista previa de la costa OSM sobre un cuadrado de mundo, para comparar con satelite.
import sys, zlib, struct
exec(open('gen_common.py').read())
import numpy as np, pickle
HALFV=float(sys.argv[1]) if len(sys.argv)>1 else 1600.0
R=400
w=np.linspace(-HALFV,HALFV,R); WX,WZ=np.meshgrid(w,w)
d=sdf_grid(WX+ORIGIN[0],WZ+ORIGIN[1],coast_segments())
img=np.zeros((R,R,3),np.uint8)
img[d>0]=(70,110,60); img[d<=0]=(20,30,60)
poly=pickle.load(open('landcover.pkl','rb'))
def marca(x,z,col,r=3):
    i=int((x+HALFV)/(2*HALFV)*R); j=int((z+HALFV)/(2*HALFV)*R)
    img[max(0,j-r):j+r,max(0,i-r):i+r]=col
for t in np.linspace(-800,800,400):
    for (x,z) in ((t,-800),(t,800),(-800,t),(800,t)): marca(x,z,(255,140,0),1)
marca(169,16,(60,150,255),4)   # jugador
img=img[::-1]  # norte arriba
def png(a,fn):
    h,wd,_=a.shape; raw=b''.join(b'\x00'+a[y].tobytes() for y in range(h))
    ch=lambda t,dat: struct.pack('>I',len(dat))+t+dat+struct.pack('>I',zlib.crc32(t+dat)&0xffffffff)
    open(fn,'wb').write(b'\x89PNG\r\n\x1a\n'+ch(b'IHDR',struct.pack('>IIBBBBB',wd,h,8,2,0,0,0))+ch(b'IDAT',zlib.compress(raw))+ch(b'IEND',b''))
png(img,'../../Captures/costa_%d.png'%int(2*HALFV))
print("tierra %.1f%%"%(100*(d>0).mean()))
