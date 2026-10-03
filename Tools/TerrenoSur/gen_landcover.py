# Arma landcover.pkl desde osm2.json: los poligonos de uso de suelo que pintan el
# splatmap (gen_terrain.py) y aplanan el pueblo. Es el paso que faltaba en el repo.
#
# Salida: dict categoria -> lista de anillos cerrados, cada uno un array Nx2 en
# metros LOCALES (x este, y norte) respecto de LAT0/LON0, igual que la costa.
# Categorias: wood, scrub, heath, bare_rock, beach, wetland, grassland, residential.
#
# Las relaciones multipolygon traen sus 'outer' partidos en varios ways; se unen
# por los extremos hasta cerrar el anillo. Los 'inner' (claros dentro del bosque)
# se descartan: con la resolucion del splat no se notan.
exec(open('gen_common.py').read())
import json, pickle
import numpy as np

CATS = {'wood': 'wood', 'scrub': 'scrub', 'heath': 'heath', 'bare_rock': 'bare_rock',
        'beach': 'beach', 'wetland': 'wetland', 'grassland': 'grassland',
        'forest': 'wood', 'grass': 'grassland', 'meadow': 'grassland', 'residential': 'residential'}

def local(geom):
    return [((p['lon'] - LON0) * M_LON, (p['lat'] - LAT0) * M_LAT) for p in geom]

def unir(tramos, tol=0.5):
    """Une tramos abiertos por sus extremos hasta formar anillos cerrados."""
    tramos = [list(t) for t in tramos if len(t) >= 2]
    anillos = []
    while tramos:
        a = tramos.pop()
        cambio = True
        while cambio and np.hypot(a[0][0] - a[-1][0], a[0][1] - a[-1][1]) > tol:
            cambio = False
            for i, b in enumerate(tramos):
                if np.hypot(a[-1][0] - b[0][0], a[-1][1] - b[0][1]) <= tol: a += b[1:]
                elif np.hypot(a[-1][0] - b[-1][0], a[-1][1] - b[-1][1]) <= tol: a += b[::-1][1:]
                elif np.hypot(a[0][0] - b[-1][0], a[0][1] - b[-1][1]) <= tol: a = b[:-1] + a
                elif np.hypot(a[0][0] - b[0][0], a[0][1] - b[0][1]) <= tol: a = b[::-1][:-1] + a
                else: continue
                tramos.pop(i); cambio = True; break
        if len(a) >= 4 and np.hypot(a[0][0] - a[-1][0], a[0][1] - a[-1][1]) <= tol:
            anillos.append(np.asarray(a))
    return anillos

d = json.load(open('osm2.json'))
out = {k: [] for k in set(CATS.values())}
for e in d['elements']:
    t = e.get('tags', {})
    cat = CATS.get(t.get('natural')) or CATS.get(t.get('landuse'))
    if cat is None: continue
    if e['type'] == 'way':
        g = e.get('geometry') or []
        if len(g) >= 4 and g[0]['lat'] == g[-1]['lat'] and g[0]['lon'] == g[-1]['lon']:
            out[cat].append(np.asarray(local(g)))
    elif e['type'] == 'relation':
        outers = [local(m['geometry']) for m in e.get('members', [])
                  if m.get('role') == 'outer' and m.get('geometry')]
        out[cat] += unir(outers)

for k, v in sorted(out.items()):
    area = sum(0.5 * abs(np.dot(p[:, 0], np.roll(p[:, 1], 1)) - np.dot(p[:, 1], np.roll(p[:, 0], 1))) for p in v)
    print("  %-11s %3d poligonos  %8.0f m2" % (k, len(v), area))
pickle.dump(out, open('landcover.pkl', 'wb'))
