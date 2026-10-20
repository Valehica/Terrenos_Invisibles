# Asigna los materiales de cada variante. Variante1 = "CasaN", Variante2 = "CasaN.001",
# Variante3 = "CasaN.002" (y sus "TechoN"). Las caras se clasifican por geometría
# (clasificar_norte.py) en muro, marco, puerta, vidrio y radier.
import bpy
import clasificar_norte

# por casa, tres variantes: (muro, puerta, marco, techo)
VARIANTES = {
    'Casa1': [('piedra', 'madera', 'marco_madera', 'galv'),
              ('barro', 'gastada', 'marco_madera', 'vieja'),
              ('beige', 'madera', 'marco_blanco', 'galv')],
    'Casa2': [('beige', 'madera', 'marco_madera', 'galv'),
              ('piedra', 'gastada', 'marco_madera', 'vieja'),
              ('ladrillo', 'gris_metal', 'marco_blanco', 'azul_techo')],
    'Casa3': [('barro', 'gastada', 'marco_madera', 'vieja'),
              ('pirca', 'madera', 'marco_madera', 'galv'),
              ('gris', 'gris_metal', 'marco_blanco', 'azul_techo')],
    'Casa4': [('bloque', 'azul', 'marco_blanco', None),
              ('ladrillo', 'gris_metal', 'marco_blanco', None),
              ('gris', 'azul', 'marco_madera', None)],
    'Casa5': [('ladrillo', 'gris_metal', 'marco_blanco', 'azul_techo'),
              ('beige', 'madera', 'marco_madera', 'galv'),
              ('piedra', 'gastada', 'marco_madera', 'vieja')],
}
SUFIJOS = ['', '.001', '.002']


def asignar(M):
    hechos = []
    for casa, vars_ in VARIANTES.items():
        for suf, (muro, puerta, marco, techo) in zip(SUFIJOS, vars_):
            ob = bpy.data.objects.get(casa + suf)
            if ob:
                cl = clasificar_norte.clasificar(ob)
                me = ob.data
                me.materials.clear()
                por_clase = {'muro': M[muro], 'marco': M[marco], 'puerta': M[puerta],
                             'vidrio': M['vidrio'], 'radier': M['cemento']}
                for k in clasificar_norte.CLASES:
                    me.materials.append(por_clase[k])
                me.polygons.foreach_set('material_index',
                                        [clasificar_norte.CLASES.index(cl[i]) for i in range(len(me.polygons))])
                me.update()
                hechos.append(ob.name)
            tob = bpy.data.objects.get(casa.replace('Casa', 'Techo') + suf)
            if tob and techo:
                tob.data.materials.clear()
                tob.data.materials.append(M[techo])
                tob.data.polygons.foreach_set('material_index', [0] * len(tob.data.polygons))
                hechos.append(tob.name)
    return hechos
