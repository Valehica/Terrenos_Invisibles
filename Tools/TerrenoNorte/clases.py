import numpy as np
from PIL import Image, ImageFilter
a=np.load('sat_scene.npy')/255.0  # [z,x] z hacia el norte
r,g,b=a[...,0],a[...,1],a[...,2]
mx=a.max(-1); mn=a.min(-1); v=mx; s=np.where(mx>0,(mx-mn)/np.maximum(mx,1e-4),0)
h=np.zeros_like(v); d=np.maximum(mx-mn,1e-5)
h=np.where(mx==r,((g-b)/d)%6,np.where(mx==g,(b-r)/d+2,(r-g)/d+4))*60
def blur(m,rad):
    im=Image.fromarray((m.clip(0,1)*255).astype(np.uint8)); return np.asarray(im.filter(ImageFilter.GaussianBlur(rad)))/255.0
def ss(e0,e1,x): t=np.clip((x-e0)/(e1-e0),0,1); return t*t*(3-2*t)
verde=ss(48,62,h)*ss(0.18,0.30,s)            # bofedal (verde oliva / verde oscuro)
amar=ss(36,44,h)*(1-ss(50,60,h))*ss(0.30,0.42,s)*ss(0.40,0.55,v)  # pajonal amarillo
claro=(1-ss(0.10,0.20,s))*ss(0.55,0.70,v)     # techos / suelo pelado del pueblo / camino
rojo=(1-ss(26,33,h))*ss(0.25,0.35,s)*(1-ss(0.55,0.65,v))   # cerro rojizo
oscuro=(1-ss(0.25,0.40,v))*(1-verde)          # arroyo / sombra / tola densa
# densidad de tola: varianza local de luminancia en la pampa
lum=0.3*r+0.59*g+0.11*b
loc=blur(lum,3); var=blur((lum-loc)**2*40,4)
np.savez('clases.npz',verde=blur(verde,2.5),amar=blur(amar,3),claro=blur(claro,2),rojo=blur(rojo,5),oscuro=blur(oscuro,1.2),tola=var.clip(0,1))
for k,m in [('verde',blur(verde,2.5)),('amar',blur(amar,3)),('claro',blur(claro,2)),('rojo',blur(rojo,5)),('tola',var.clip(0,1))]:
    print(k, round(float(m.mean()),3))
rgb=np.stack([blur(rojo,5)*0.8+blur(amar,3),blur(verde,2.5)+blur(amar,3)*0.8,blur(claro,2)],-1)
Image.fromarray((rgb[::-1].clip(0,1)*255).astype(np.uint8)).save('clases.png')
print('pampa sample hsv', np.median(h[ (s>0.2)&(s<0.4)]), np.median(s), np.median(v))
