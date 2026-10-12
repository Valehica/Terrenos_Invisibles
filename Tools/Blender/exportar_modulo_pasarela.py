# Exporta el modulo de la pasarela (PasarelaBase) a JSON para Unity (Assets/Editor/PasarelaCurvaEditor.cs lo lee
# a traves de PasarelaCurva). Cada isla de malla (tablon, viga o pilote) sale como una pieza aparte; Unity la
# clasifica por su caja.
# Sale dos veces: "piezas" con el bisel aplicado (de cerca) y "piezasLod" sin bisel (de lejos, ~6 veces menos
# triangulos). Las dos listas van ordenadas por el centro de cada pieza, asi la pieza i es la misma en ambas.
# Los ejes ya salen en Unity: Blender (x, y, z) -> Unity (x, z, y), X = ancho, Y = arriba, Z = avance.
# Ese cambio es un espejo, asi que se invierte el orden de cada triangulo para que no quede del reves.
# Uso: exec(open(ruta).read(), {"OBJ": "PasarelaBase", "OUT": ruta_json})
import bpy, json, os

OBJ = globals().get("OBJ", "PasarelaBase")
OUT = globals().get("OUT", r"C:\Users\valeh\Terrenos_Invisibles\Assets\Models\Sur\Pasarela\PasarelaModulo.json")
UV = "UV_Madera"


def piezas_de(obj):
    dg = bpy.context.evaluated_depsgraph_get(); dg.update()
    ev = obj.evaluated_get(dg)
    me = ev.to_mesh()
    me.calc_loop_triangles()

    padre = list(range(len(me.vertices)))      # islas por vertice (union-find sobre aristas)
    def raiz(i):
        while padre[i] != i:
            padre[i] = padre[padre[i]]; i = padre[i]
        return i
    for e in me.edges:
        a, b = raiz(e.vertices[0]), raiz(e.vertices[1])
        if a != b: padre[a] = b

    uv = me.uv_layers[UV].data if UV in me.uv_layers else me.uv_layers.active.data
    normales = me.corner_normals

    piezas = {}
    for t in me.loop_triangles:
        isla = raiz(me.loops[t.loops[0]].vertex_index)
        p = piezas.setdefault(isla, {"v": [], "n": [], "uv": [], "t": [], "mapa": {}})
        idx = []
        for li in t.loops:
            if li not in p["mapa"]:
                co = me.vertices[me.loops[li].vertex_index].co
                n = normales[li].vector
                u = uv[li].uv
                p["mapa"][li] = len(p["v"]) // 3
                p["v"] += [round(co.x, 5), round(co.z, 5), round(co.y, 5)]
                p["n"] += [round(n.x, 4), round(n.z, 4), round(n.y, 4)]
                p["uv"] += [round(u.x, 5), round(u.y, 5)]
            idx.append(p["mapa"][li])
        p["t"] += [idx[0], idx[2], idx[1]]
    ev.to_mesh_clear()

    def centro(p):
        v = p["v"]
        return tuple(round((min(v[i::3]) + max(v[i::3])) / 2, 1) for i in (2, 0, 1))
    lista = sorted(piezas.values(), key=centro)
    return [{"v": p["v"], "n": p["n"], "uv": p["uv"], "t": p["t"]} for p in lista]


obj = bpy.data.objects[OBJ]
oculto = obj.hide_viewport
obj.hide_viewport = False                      # un objeto oculto no se evalua y el bisel no se aplica
biseles = [m for m in obj.modifiers if m.type == 'BEVEL']
try:
    completo = piezas_de(obj)
    for m in biseles: m.show_viewport = False
    lod = piezas_de(obj)
finally:
    for m in biseles: m.show_viewport = True
    obj.hide_viewport = oculto

salida = {"objeto": OBJ, "archivo": bpy.data.filepath, "piezas": completo, "piezasLod": lod}
os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w") as f:
    json.dump(salida, f, separators=(",", ":"))
result = {"piezas": len(completo), "piezasLod": len(lod),
          "tris": sum(len(p["t"]) for p in completo) // 3, "trisLod": sum(len(p["t"]) for p in lod) // 3, "out": OUT}
