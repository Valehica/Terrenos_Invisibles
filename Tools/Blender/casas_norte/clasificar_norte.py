# Clasifica las caras de las casas de Norte (PackCasas.blend) según la geometría:
# las puertas y ventanas están modeladas como rectángulos hundidos con marco.
#   muro    planos grandes de la fachada, parapetos, base
#   marco   caras angostas (< ANCHO_MARCO): jambas, dinteles, alféizares, marco que sobresale
#   puerta  panel rodeado de marco que llega abajo
#   vidrio  panel rodeado de marco que no llega abajo
#   radier  losa/escalón de entrada (todo por debajo de Z_RADIER) y caras de arriba
#           (losa del techo plano, coronamiento de parapetos): cemento
import bpy, bmesh

ANCHO_MARCO = 0.15
Z_RADIER = 0.32
Z_PUERTA = 0.35        # una abertura cuyo borde inferior está más abajo es puerta
BORDE_MARCO = 0.75     # fracción del borde de una isla que debe ser marco para ser panel
CLASES = ['muro', 'marco', 'puerta', 'vidrio', 'radier']


def ancho(f):
    """Ancho aproximado de la cara: área / arista más larga."""
    larga = max(e.calc_length() for e in f.edges)
    return f.calc_area() / larga if larga > 1e-6 else 0.0


def clasificar(ob):
    """Devuelve {indice_cara: clase}. Usa coordenadas de mundo (escala sin aplicar)."""
    mw = ob.matrix_world
    bm = bmesh.new(); bm.from_mesh(ob.data)
    bm.transform(mw)                     # trabajar en metros reales
    bm.normal_update()
    zmin = min(v.co.z for v in bm.verts)
    # plano principal de cada fachada (por dirección de la normal): el de más área
    planos = {}
    for f in bm.faces:
        if abs(f.normal.z) > 0.3:
            continue
        k = (round(f.normal.x, 2), round(f.normal.y, 2))
        d = round(f.calc_center_median().dot(f.normal) / 0.005) * 0.005
        planos.setdefault(k, {}).setdefault(d, 0.0)
        planos[k][d] += f.calc_area()
    principal = {k: max(v.items(), key=lambda kv: kv[1])[0] for k, v in planos.items()}

    def en_plano(f):
        if abs(f.normal.z) > 0.3:
            return False
        k = (round(f.normal.x, 2), round(f.normal.y, 2))
        return abs(f.calc_center_median().dot(f.normal) - principal[k]) < 0.004

    clase = {}
    for f in bm.faces:
        z0 = min(v.co.z for v in f.verts) - zmin
        z1 = max(v.co.z for v in f.verts) - zmin
        if f.normal.z < -0.99 and z1 < 0.02:
            clase[f.index] = 'muro'                     # base, no se ve
        elif z1 < Z_RADIER and f.calc_area() < 6.0:
            clase[f.index] = 'radier'
        elif f.normal.z > 0.9 and z0 > 1.0:
            clase[f.index] = 'radier'                   # losa y coronamiento: cemento
        elif ancho(f) < ANCHO_MARCO and not en_plano(f):    # una faja a ras del muro es muro
            clase[f.index] = 'marco'
    # paneles: los muros están cortados en cuadrícula alrededor de las aberturas, así
    # que un panel son varias caras coplanares (hundidas, a ras o salidas). Se juntan
    # en islas de caras con la misma normal y solo cuentan si todo su borde da contra
    # marco (o el radier): el muro, en cambio, sigue en las otras fachadas.
    hundidas = {f for f in bm.faces if f.index not in clase and abs(f.normal.z) < 0.3}
    vistas = set()
    for f in hundidas:
        if f in vistas:
            continue
        isla, pila = [], [f]
        vistas.add(f)
        while pila:
            g = pila.pop(); isla.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h in hundidas and h not in vistas and h.normal.dot(g.normal) > 0.999:
                        vistas.add(h); pila.append(h)
        s = set(isla)
        largo = enmarcado = 0.0
        for g in isla:
            for e in g.edges:
                for h in e.link_faces:
                    if h not in s:
                        largo += e.calc_length()
                        if clase.get(h.index) in ('marco', 'radier'):
                            enmarcado += e.calc_length()
        # algunas puertas no tienen marco abajo: basta con que casi todo el borde sea marco
        if largo and enmarcado / largo >= BORDE_MARCO:
            z0 = min(v.co.z for g in isla for v in g.verts) - zmin
            for g in isla:
                clase[g.index] = 'puerta' if z0 < Z_PUERTA else 'vidrio'
    for f in bm.faces:
        clase.setdefault(f.index, 'muro')
    bm.free()
    return clase
