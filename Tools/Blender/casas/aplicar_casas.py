# Ejecutar desde el Text Editor de Blender (o con exec) para rehacer los
# materiales de las casas. Re-correr es seguro: los grupos y materiales se
# reconstruyen en el lugar con el mismo nombre.
#   ASIGNAR = True  -> además re-separa las caras del cuerpo, recalcula la veta,
#                      aplica shade smooth por ángulo y RESETEA los colores por casa.
import sys, os, importlib
DIR = r"C:\Users\valeh\Terrenos_Invisibles\Tools\Blender\casas"
ASIGNAR = False
if DIR not in sys.path:
    sys.path.insert(0, DIR)
import nodos_util, grupos_casas
importlib.reload(nodos_util); importlib.reload(grupos_casas)
exec(open(os.path.join(DIR, "materiales_casas.py"), encoding="utf-8").read(), {"__name__": "materiales"})
if ASIGNAR:
    exec(open(os.path.join(DIR, "asignar_casas.py"), encoding="utf-8").read(), {"__name__": "asignar"})
