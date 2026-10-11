import math, urllib.request, io, sys
from PIL import Image
lat,lon=-19.2513632,-68.7223199
z=int(sys.argv[1]); R=int(sys.argv[2]); out=sys.argv[3]
n=2**z
x=(lon+180)/360*n; y=(1-math.log(math.tan(math.radians(lat))+1/math.cos(math.radians(lat)))/math.pi)/2*n
cx,cy=int(x),int(y)
img=Image.new('RGB',((2*R+1)*256,(2*R+1)*256))
for dy in range(-R,R+1):
  for dx in range(-R,R+1):
    u=f"https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{cy+dy}/{cx+dx}"
    d=urllib.request.urlopen(urllib.request.Request(u,headers={'User-Agent':'Mozilla/5.0'}),timeout=30).read()
    img.paste(Image.open(io.BytesIO(d)),((dx+R)*256,(dy+R)*256))
mpp=156543.03392*math.cos(math.radians(lat))/n
px=(x-cx+R)*256; py=(y-cy+R)*256
print("m/px",mpp,"church px",px,py,"size",img.size)
img.save(out)
