# Generacion del terreno de Puerto Eden (escena Sur)

El terreno de `Assets/Scenes/Sur.unity` **no esta esculpido a mano**: sale de datos
reales y se regenera con estos scripts. Si hay que cambiar el relieve, se edita aca
y se vuelve a correr; editar el heightmap en el editor de Unity se pierde en la
siguiente regeneracion.

## Datos de origen

Ninguno de los dos se guarda en el repo (pesan ~100 MB); los scripts esperan
encontrarlos en el directorio de trabajo.

1. **Altimetria** — SRTM 1 arcsec (30 m lat / 20 m lon a esta latitud), 4 tiles:

   ```bash
   for t in S50/S50W075 S49/S49W075 S50/S50W074 S49/S49W074; do
     curl -o "$(basename $t).hgt.gz" \
       "https://s3.amazonaws.com/elevation-tiles-prod/skadi/$t.hgt.gz"
     gunzip "$(basename $t).hgt.gz"
   done
   ```

2. **Costa y uso de suelo** — OpenStreetMap via Overpass, a `osm2.json`:

   ```bash
   curl -A "TerrenosInvisibles/1.0" -G "https://overpass-api.de/api/interpreter" \
     --data-urlencode 'data=[out:json][timeout:120];(node(-49.145,-74.45,-49.105,-74.38);way(-49.145,-74.45,-49.105,-74.38););out geom;' \
     -o osm2.json
   ```

   (Sin `-A` la API responde 406.) El rectangulo tiene que coincidir con `BBOX` de
   `gen_common.py`. De ahi sale `landcover.pkl` con **`python gen_landcover.py`**
   (poligonos de uso de suelo en metros locales; las multipolygon se unen por sus
   extremos).

## Por que hacen falta las dos fuentes

El SRTM en los fiordos **no distingue agua de tierra**: el radar sobre agua calma
devuelve ruido de 0 a 20 m, y toda la caja de la escena tiene un maximo de 41 m.
La linea de costa —que es lo que hace reconocible a Puerto Eden— sale de OSM, como
campo de distancia con signo (positivo en tierra, usando la convencion de que la
tierra queda a la izquierda del sentido del way). El DEM solo aporta el relieve
macro y, sobre todo, las montanas del fondo.

## Marco de coordenadas

- Origen del mundo Unity = local (120, 40) m respecto de -49.126383, -74.415707.
- Terrain: 1600 x 1600 m, `position (-800, -90, -800)`, rango de altura 180 m
  (de -90 a +90). El nivel del mar es y = 0.
- `EXAG = 1.6` exagera el relieve del DEM. Con 1.0 el cerro del Mirador queda en
  41 m y la escena se ve plana; con 1.6 llega a ~69 m.

## Salidas

| archivo | destino en Unity |
|---|---|
| `heights.bin` | `TerrainData_Sur.asset` (float32, 1025x1025, normalizado) |
| `splat.bin`   | alphamaps, 1024x1024x6 capas |
| `fondo.bin`   | `Assets/Meshes/FondoSur.asset` (malla polar 176x384) |

`gen_fondo.py` lee `heights.bin`, asi que **siempre correr `gen_terrain.py`
primero**: el anillo 0 de la malla de fondo se fuerza a la altura real del borde
del Terrain (la diferencia sin corregir llega a 20 m) y la correccion se reparte
hacia afuera. Sin eso queda un escalon visible en todo el perimetro.

## Hundido del borde (2026-09-20)

La tierra llegaba hasta el borde del cuadrado de 1600 m. Desde dentro de la
escena eso se ve como un tajo recto: ahi se termina la geometria detallada del
Terrain y empieza `MontanasLejanas`, que es otra malla con otro shader, y la
juntura queda a la vista por encima del agua.

La franja del perimetro se hunde ahora por debajo del nivel del mar
(`W_BORDE = 150` m de ancho medio, `D_BORDE = 28` m de profundidad en el borde
mismo), asi que la juntura queda debajo del agua, donde no se ve. La isla
grande del centro no se toca: la franja mas ancha llega a 219 m y el nucleo
jugable esta a mas de 400 m de cualquier borde.

**El ancho esta modulado con ruido a proposito.** Con un ancho constante la
costa nueva seria un rectangulo perfecto — otro tajo, solo que mas disimulado.
El ruido se evalua en **coordenadas de mundo**, no locales, para que el Terrain
y la malla de fondo usen el mismo campo y la costa no se corte en la juntura.

Las funciones viven en `gen_common.py` (`mascara_borde_terrain`,
`mascara_borde_fondo`, `hundir_borde`) y las usan los dos generadores:

- `gen_terrain.py` la aplica **antes** del splat, asi la franja hundida se pinta
  sola como lecho marino.
- `gen_fondo.py` la aplica **antes** de forzar el anillo 0, para que ese ajuste
  vuelva a ser lo que era: una correccion chica. La malla tarda `FUERA_BORDE`
  = 1.7 veces mas en recuperar el relieve real, si no queda una pared de tierra
  pegada al borde del Terrain.

Efecto medido: tierra 37.2% -> 29.0% de la caja, punto mas alto del perimetro
-27.9 m, pendiente p99 43.0 -> 40.4 grados (no empeora).

### Aplicarlo sin regenerar

`TerrainData_Sur.asset` es binario, asi que no se puede parchear a mano. El
menu **Terrenos > Sur > Hundir bordes del terreno**
(`Assets/Editor/BordesDeTerreno.cs`) lee `mascara_bordes.bin` — la misma
mascara, exportada a 1025x1025 — y aplica alturas y splatmap. Es idempotente:
la primera pasada guarda `heights_previo.bin` y las siguientes trabajan sobre
ese original. **Terrenos > Sur > Restaurar bordes del terreno** lo deshace.

Estos assets ya vienen con el hundido aplicado, porque son texto y se parchearon
directo:

| archivo | por que hace falta tocarlo |
|---|---|
| `Assets/Textures/TerrainHeight_Sur.asset` | es el `_ShoreHeightMap` del agua; sin actualizarlo el shader sigue creyendo que hay tierra ahi y pinta espuma de orilla en medio del mar |
| `Assets/Meshes/FondoSur.asset` | anillo 0 forzado a -28 m para que coincida con el borde nuevo |


## Terreno de 3200 m (2026-09-21)

A pedido del usuario el Terrain paso de 1600 a **3200 m** para cubrir mas de la
zona real alrededor de Puerto Eden. Cambios:

- `HALF = 1600`, heightmap **2049**, splat y detalles **2048**: 1.56 m por muestra,
  la misma densidad de antes, asi que los radios en pixeles no cambian.
- **Rango de altura -90 a +330 m** (`RANGE_Y = 420`): dentro de 3.2 km los cerros
  llegan a ~300 m y con el rango viejo (180) quedaban cortados en mesetas.
- **La costa se arma en anillos cerrados** (`coast_rings`/`land_mask` en
  `gen_common.py`). El signo "tierra a la izquierda del tramo mas cercano" se
  rompia con el area grande: los tramos que quedan abiertos en el borde de la
  descarga dibujaban cunas rectas de tierra o de mar. Ahora las cadenas abiertas
  se cierran caminando el borde del `BBOX` en sentido antihorario.
- **La pintura es la misma en toda la tierra** (`USAR_COBERTURA_OSM = False`): el
  usuario pidio que todo se vea como la isla del jugador (46% musgo, 29% matorral,
  20% roca). Los poligonos de cobertura de OSM ya no pintan salvo el pueblo y las
  playas; con el area grande traian 19 km2 de `bare_rock`.
- **Sin hundido de borde** (`HUNDIR_BORDE = False`): con 3200 m la tierra del oeste
  y del norte llega al borde, y hundirla dejaba un foso con acantilados. El anillo
  0 del fondo se sigue forzando a la altura real del borde. `Hundir bordes del
  terreno` (BordesDeTerreno.cs) y `mascara_bordes.bin` quedan obsoletos: la mascara
  es de 1025 y el menu la rechaza.
- `SEA_R = 2400`: la malla del mar de Sur es propia (`OceanGrid_Sur.asset`, 5000 m).

**Aplicar en Unity**, en orden: `gen_landcover.py`, `gen_terrain.py`,
`gen_fondo.py`, menu **Terrenos > Sur > Aplicar terreno generado**
(`AplicarTerrenoSur.cs`: alturas, splat, `TerrainHeight_Sur`, fondo y malla del
mar), **Terrenos > Agua > Bakear distancia a la costa**, **Terrenos > Sur >
Poblar vegetacion**. Revisar que el Player y el NPC queden sobre el suelo.

Respaldo de la version de 1600 m en `respaldo_1600/` (assets y escena) y de los
scripts en `*_previo.py.bak`.
