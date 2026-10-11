import numpy as np, math, sys
from PIL import Image
S=float(sys.argv[1]); ROT=float(sys.argv[2])
im=np.asarray(Image.open('sat/z17b.jpg').convert('RGB')).astype(np.float32)
CPX,CPY=1015.528+256,1008.979+256   # iglesia en z17b (R=4)
MPP=1.127543
Cx,Cz=-27.8,-45.9
N=512
xs=-250+(np.arange(N)+0.5)/N*500; zs=-248+(np.arange(N)+0.5)/N*500
X,Z=np.meshgrid(xs,zs)  # [z,x]
dx=(X-Cx)/S; dz=(Z-Cz)/S
c,s=math.cos(math.radians(ROT)),math.sin(math.radians(ROT))
e=dx*c+dz*s; n=-dx*s+dz*c
px=CPX+e/MPP; py=CPY-n/MPP
def bil(img,py,px):
    H,W=img.shape[:2]; px=px.clip(0,W-1.001); py=py.clip(0,H-1.001)
    x0=px.astype(int); y0=py.astype(int); fx=(px-x0)[...,None]; fy=(py-y0)[...,None]
    return (img[y0,x0]*(1-fx)*(1-fy)+img[y0,x0+1]*fx*(1-fy)+img[y0+1,x0]*(1-fx)*fy+img[y0+1,x0+1]*fx*fy)
out=bil(im,py,px)
np.save('sat_scene.npy',out)
Image.fromarray(out[::-1].clip(0,255).astype(np.uint8)).save('sat_scene.png')  # norte arriba
