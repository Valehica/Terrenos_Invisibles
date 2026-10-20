# Ejecutar desde el Text Editor de Blender (o con exec) en Catedral2.blend para
# rehacer los materiales. Re-correr es seguro: grupos y materiales se rehacen en
# el lugar con el mismo nombre.
#   ASIGNAR = True -> además reasigna los slots y las caras de torre, remates,
#                     barandas, arcos y casucha (la iglesia no se toca).
import sys, os, importlib
DIR = r"C:\Users\valeh\Terrenos_Invisibles\Tools\Blender\catedral"
CASAS = os.path.join(os.path.dirname(DIR), "casas")
ASIGNAR = False
for d in (CASAS, DIR):
    if d not in sys.path:
        sys.path.insert(0, d)
import nodos_util, grupos_casas, grupos_catedral
importlib.reload(nodos_util); importlib.reload(grupos_casas); importlib.reload(grupos_catedral)
exec(open(os.path.join(DIR, "materiales_catedral.py"), encoding="utf-8").read(), {"__name__": "materiales"})
if ASIGNAR:
    exec(open(os.path.join(DIR, "asignar_catedral.py"), encoding="utf-8").read(), {"__name__": "asignar"})
