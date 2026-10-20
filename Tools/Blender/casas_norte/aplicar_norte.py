# Ejecutar desde el Text Editor de Blender (o con exec) en Concept/Norte/OtrasCasas/
# PackCasas.blend para rehacer los materiales de las casas. Re-correr es seguro.
#   ASIGNAR = True -> además reclasifica las caras y reasigna los materiales de las
#                     3 variantes de cada casa (tabla VARIANTES en asignar_norte.py).
import sys, os, importlib
DIR = r"C:\Users\valeh\Terrenos_Invisibles\Tools\Blender\casas_norte"
BLENDER = os.path.dirname(DIR)
ASIGNAR = True
for d in (os.path.join(BLENDER, "casas"), os.path.join(BLENDER, "catedral"), DIR):
    if d not in sys.path:
        sys.path.insert(0, d)
import nodos_util, grupos_casas, grupos_catedral, grupos_norte, clasificar_norte, asignar_norte
for m in (nodos_util, grupos_casas, grupos_catedral, grupos_norte, clasificar_norte, asignar_norte):
    importlib.reload(m)
ctx = {"__name__": "materiales"}
exec(open(os.path.join(DIR, "materiales_norte.py"), encoding="utf-8").read(), ctx)
if ASIGNAR:
    print(asignar_norte.asignar(ctx["M"]))
