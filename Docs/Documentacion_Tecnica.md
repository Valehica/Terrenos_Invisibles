# Terrenos Invisibles — Documentación técnica

*Estado del proyecto al 22-09-2026. Unity 6000.0.62f1.*

Este documento dice **qué tecnologías usa el juego y en qué momento**, y **qué assets
de la Asset Store usa y en qué momento**. Lo de "en qué momento" se refiere a la escena
(zona) y al tramo del recorrido del jugador en que entra en juego cada cosa.

Las listas de assets por escena no están hechas a ojo: salen de seguir las referencias
(GUID) desde cada escena hacia prefabs, materiales, capas de terreno y texturas, incluso
dentro de los archivos binarios de terreno (ver [Método](#8-método-cómo-se-obtuvo-esta-lista)).

---

## 1. El juego en una página

Juego narrativo de exploración en tercera persona. Maite recorre Chile de norte a sur
visitando los lugares que visitaron sus padres, guiada por postales antiguas. Cada zona
es un nivel autocontenido con el mismo ciclo:

> llegada y diálogo con un NPC → minijuego cultural → escaneo de fauna con el teléfono → postal → transición

| Orden | Escena | Lugar | Cultura | Minijuego |
|---|---|---|---|---|
| 1 | `Assets/Scenes/Norte.unity` | Isluga | Aymara | Chaku (arreo de vicuñas) |
| 2 | `Assets/Scenes/Centro.unity` | Isla Mocha | Mapuche Lafkenche | Recolección de luga |
| 3 | `Assets/Scenes/Sur.unity` | Puerto Edén | Kawésqar | Travesía en canoa |

> **Ojo:** en *Build Settings* solo está `Norte.unity`. Para una build completa hay que
> agregar `Centro` y `Sur`. `Centro_respaldo.unity` es una copia de seguridad, no va en la build.

---

## 2. Stack tecnológico

### 2.1 Motor y paquetes de Unity en uso

| Tecnología | Versión | Para qué se usa | Dónde |
|---|---|---|---|
| **Unity 6** | 6000.0.62f1 | Motor | Todo |
| **URP** (Universal Render Pipeline) | 17.0.4 | Pipeline de render activo. Dos perfiles: `PC_RPAsset` y `Mobile_RPAsset`. El renderer de PC usa **Forward+** y Native Render Pass | Todas las escenas |
| **URP Render Graph** | (URP 17) | API con que están escritos los dos *Renderer Features* propios (estilo Ghibli y rayos de luz) | `PC_Renderer` |
| **Volume / Post-processing de URP** | (URP 17) | Grading por escena (`PostPro_Centro`, `PostPro_Sur`, `SampleSceneProfile` en Norte) + destello de sol (`DestelloSol`) | Todas |
| **SSAO** de URP | (URP 17) | Oclusión ambiental en pantalla | `PC_Renderer` |
| **Input System** | 1.14.2 | Todo el control: jugador, cámara, interacción, linterna, minijuegos, intro | Todas |
| **TextMesh Pro** (uGUI 2.0) | — | Diálogos, HUD de minijuegos, escáner, contador de FPS, textos de la intro | Todas |
| **uGUI** | 2.0.0 | Canvas de UI (teléfono, mira, paneles) | Todas |
| **Terrain** + **Terrain Tools** | 5.3.3 | Terrenos de las tres zonas; esculpido y pintado | Todas |
| **Navegación (NavMeshAgent)** / AI Navigation | 2.0.9 | Movimiento de las vicuñas y del rebaño del chaku | Norte |
| **Animator / Mecanim** | — | Jugador (`PlayerV2Animator`), NPC (`NPC_Idle`), vicuña (`VicunaController`) | Todas |
| **Física** (Rigidbody, colliders, Physic Material) | — | Jugador, canoa con flotación (`CanoaCasco.physicMaterial`) | Todas |
| **Sistema de partículas** | — | Efectos de Tenkoku (lluvia, niebla), espuma de la canoa | Todas |
| **Audio** | — | Ambiente día/noche, lluvia y truenos (todos de Tenkoku) | Todas |

### 2.2 Paquetes instalados que el juego **no** usa

| Paquete | Situación |
|---|---|
| HDRP 17.0.4 (+ `Assets/HDRPDefaultResources`, `Assets/Samples/High Definition Render Pipeline`) | Instalado pero inactivo: los dos niveles de calidad apuntan a assets de URP. Candidato a quitar (pesa y alarga las compilaciones de shaders). |
| Visual Scripting 1.9.7 | Sin grafos en el proyecto. |
| Timeline 1.8.9 | Sin *Timeline assets*; la intro está hecha por script. |
| Multiplayer Center 1.0.0 | Viene con la plantilla; no aplica. |
| Shader Graph 17.0.4 | Dependencia de URP. Los shaders propios están escritos en HLSL, no en Shader Graph. |

### 2.3 Herramientas fuera de Unity

| Herramienta | Momento de producción |
|---|---|
| **Blender** | Modelado (jugador, catedral, queñoa, vicuña) |
| **Photoshop / Illustrator** | Texturas, sprites de UI, frames de la intro |
| **Mixamo** | Personaje del NPC y su animación `Idle.fbx` (texturas `Ch37_*`) |
| **Python 3** (`Tools/TerrenoSur/`) | Genera el terreno de Puerto Edén desde datos reales |
| **SRTM 1 arcsec** (4 tiles `.hgt`) | Relieve de Puerto Edén |
| **OpenStreetMap / Overpass** (`osm2.json`) | Línea de costa y uso de suelo de Puerto Edén |
| **MCP for Unity** (`com.coplaydev.unity-mcp`) | Automatizar el Editor desde un asistente de IA. Solo en el Editor, no llega a la build |
| **Rider / Visual Studio / VS Code** | IDE (paquetes de integración instalados) |

---

## 3. Tecnologías por momento del juego

### Momento 0 — Arranque de cada zona
| Qué pasa | Script | Tecnología |
|---|---|---|
| Intro narrada con animación por frames (solo Norte) | `Intro/IntroSequenceController.cs` | uGUI + TMP + Input System; sprites de `Sprites/Animaciones/Inicio/Frames` |
| Fundido desde negro (Centro y Sur, sin intro) | `Intro/SceneFadeIn.cs` | uGUI |
| Cielo, sol, nubes, hora del día, ambiente sonoro | Prefab `Tenkoku DynamicSky` + `Environment/CloudLayerFollow.cs` | **Tenkoku** (Asset Store) |
| Estilo visual | `Rendering/RenderShaderURP.cs` + `EstiloGhibliVolumen.cs` + `RayosDeLuzFeature.cs` | URP Renderer Features (Render Graph) + shaders HLSL propios |

### Momento 1 — Exploración (todas las zonas)
| Qué pasa | Script | Tecnología |
|---|---|---|
| Caminar, correr, agacharse | `Player/PlayerController.cs` | Input System, Animator, física |
| Cámara en tercera persona | `Camera/ThirdPersonCameraController.cs` | Input System |
| Linterna (Centro, Sur) | `Player/Flashlight.cs` | Luz spot + Input System |
| Resaltar objetos interactuables | `Interaction/OutlineHighlight.cs` + `Shaders/OutlineHighlight.shader` | Shader propio |
| Interactuar | `Player/PlayerInteractor.cs`, `Interaction/Interactable.cs` | Input System |
| Terreno y vegetación | Terrain (+ detalles) | Terrain de Unity; texturas de la Asset Store (ver §4) |
| Mar (Centro, Sur) | `Shaders/OceanWaterRealista.shader` | Shader HLSL propio: olas de Gerstner, rompientes que siguen la costa (textura `ShoreDistance_*` bakeada), cáusticas |
| Montañas del fondo (Sur) | `Shaders/MontanasLejanas.shader` + malla `FondoSur` | Shader propio, malla generada en Python |
| Marea (Centro) | `Environment/MareaController.cs` | Lee la hora de **Tenkoku** para subir y bajar el mar |
| Medición de rendimiento | `UI/FpsCounter.cs` | TMP |

### Momento 2 — Diálogo con el NPC
| Qué pasa | Script | Tecnología |
|---|---|---|
| Conversación y lanzamiento del minijuego de la zona | `Dialogue/DialogueManager.cs` → `Minigame/MinigameBase.cs` | uGUI + TMP |

### Momento 3 — Minijuego cultural
| Zona | Minijuego | Scripts | Tecnología |
|---|---|---|---|
| Norte | **Chaku**: formar una cadena y arrear vicuñas a un corral | `ChakuMinigameManager`, `ChainController`, `VicunaHerdGroup`, `VicunaHerdMember`, `BoundaryFenceVisualizer`, `MinigameCamera`, `Animals/VicunaBehavior` | **NavMeshAgent**, Input System, TMP |
| Centro | **Recolección de luga**: distinguir luga de piedras y otras algas en el intermareal, esquivando las olas | `Luga/LugaMinigameManager`, `Luga/LugaPatch`, `Player/PlayerStun` | Lee la marea/hora de **Tenkoku**; Input System, TMP |
| Sur | **Travesía en canoa** por los canales, con viento y corrientes, hasta un faro | `Canoa/CanoaController`, `CanoaCamera`, `CanoaMinigameManager`, `CanoaVientoCorriente`, `OleajeMuestreo` | Física con flotación; `OleajeMuestreo` reproduce en C# las ondas de Gerstner del shader del mar para que la canoa suba y baje con las olas que se ven |

### Momento 4 — Escaneo de fauna con el teléfono
| Qué pasa | Script | Tecnología |
|---|---|---|
| Apuntar con el teléfono y registrar animales | `Scanning/AnimalScanner.cs`, `Scannable.cs`, `AnimalCodex.cs` | uGUI + TMP; sprites `Telefono.png`, `UIX/Mira.png` |

> El escáner está en las tres escenas; los animales escaneables (vicuñas) solo están en Norte por ahora.

---

## 4. Assets de la Asset Store: qué se usa y en qué momento

### 4.1 En uso dentro del juego

| Paquete (carpeta) | Qué se usa | Norte | Centro | Sur | Momento |
|---|---|:-:|:-:|:-:|---|
| **Tenkoku – Dynamic Sky** (`TENKOKU - DYNAMIC SKY`) | Prefab `Tenkoku DynamicSky`: cielo, sol/luna, estrellas, capa de nubes URP, lluvia/niebla/nieve, sonidos de ambiente y truenos | ✔ | ✔ | ✔ | Toda la partida. Además alimenta la **marea** (Centro) y el **minijuego de la luga** |
| **Terrain Sample Asset Pack** de Unity (`TerrainSampleAssets`) | Texturas de terreno (BaseColor/MaskMap/Normal). Centro: Grass A/B/Dry/Moss, Rock, Sand, Soil Rocks, Tidal Pools. Sur: Grass Moss, Heather, Pebbles B, Snow, Tidal Pools | | ✔ | ✔ | Exploración: el suelo de Isla Mocha y Puerto Edén (capas `Terrain/Layers/Centro_*` y `Sur_*`) |
| **FlipGameDev – Terrain & Grass Pack** (`FlipGameDev`) | Norte: 6 capas de terreno (Mud, Mud Leaves 1/2, Mud Pebbles, Road, RockSmooth). Sur: 10 rocas (`Rock_1…12`, malla + material + texturas), colocadas por `GenerarTravesiaCanoa.cs` | ✔ | | ✔ | Norte: suelo del altiplano. Sur: rocas del recorrido de la **canoa** |
| **ADG Textures – Ground vol.1** (`ADG_Textures`) | `ground1` y `ground3` (diffuse, normal, height) en las capas `Layer-Terrain/NewLayer*` | ✔ | | | Exploración: suelo de Isluga |
| **4 Snow Materials** (`Materials Collection`) | Nieve: `snow_1` en Norte (capa `NewLayer 3`), `snow_3` en Sur (capa `Sur_Nieve`) | ✔ | | ✔ | Exploración: cumbres nevadas |
| **Grass and Flowers Pack 1** (`Grass And Flowers Pack 1`) | Origen de las texturas de pasto, flor y arbusto copiadas y retocadas en `Textures/Detalles/Det_*` (Bush 6, Flower 9, Grass 1–3, Grass Flower 1 y 5) | | ✔ | ✔ | Exploración: vegetación del terreno. *Indirecto: el juego usa las copias retocadas, no los archivos del paquete* |
| **ALP – Grass Flowers FREE** (`ALP_Assets`) | Origen de `Det_grass02` y `Det_grassFlower03` | | ✔ | ✔ | Igual que el anterior (indirecto) |
| **Bush Grass Bend** (`BushGrassBend`) | Herramienta de Editor *Tools > Prepare detail for bend*: generó las mallas `Meshes/Detalles/bgb_Arbusto` y `bgb_Helecho`, para que el arbusto y el helecho se doblen con el viento del terreno | | ✔ | ✔ | Producción (Editor). En el juego viven las mallas que generó |
| **TextMesh Pro** (Essential Resources) | Fuente `LiberationSans SDF` | ✔ | ✔ | ✔ | Toda la UI |

### 4.2 Importados pero **sin uso** en ninguna escena

| Paquete (carpeta) | Nota |
|---|---|
| **Suimono – Water System 2** | No es compatible con URP (verificado). El agua se reemplazó por shaders propios. Candidato a borrar. |
| **PBR Grass Textures** (`PBR_Grass_Textures`) | Sin referencias. |
| **Terrain Textures Pack Free** (`TerrainTexturesPackFree`) | Sin referencias. |
| **Yughues Free Cobble Materials** | Sin referencias. |
| HDRP Samples / SRP Core Samples (`Assets/Samples`) | Muestras de paquetes; sin referencias. |
| Escenas de demo de los paquetes | `DemoBGB.unity`, `example_scene.unity`, `Sc_YFCM_Preview.unity`, `All materials (snow).unity`, etc. No van en la build. |

> Borrar lo de §4.2 no rompe ninguna escena. Antes de hacerlo conviene hacer commit, y
> revisar que no haya un prefab nuevo que los use desde la fecha de este documento.

---

## 5. Assets propios y generados

| Tipo | Assets | Escena |
|---|---|---|
| Modelos 3D (Blender) | `PlayerV2.fbx` (Maite), `Catedral.fbx`, `Queñoa.fbx`, `Vicuña.fbx` | Player en todas; el resto en Norte |
| Modelos descargados | `abandoned_cottage.fbx`, `RuralStall.FBX`, `Idle.fbx` (Mixamo, NPC) | Norte (NPC en todas) |
| Shaders HLSL | `OceanWaterRealista`, `MontanasLejanas`, `OutlineHighlight`, `RenderShaderURP` (pase Ghibli), `RayosDeLuz` | Ver §3 |
| Shaders antiguos sin uso | `OceanWater.shader`, `OceanWaterStylized.shader` | Reemplazados por `OceanWaterRealista` |
| Texturas de agua | `Textures/Agua/*` (espuma, olas, ondas, cáusticas) | Centro, Sur |
| Datos bakeados | `ShoreDistance_*` (distancia a la costa), `TerrainHeight_*` | Centro, Sur |
| Mallas generadas | `OceanGrid`, `OceanGrid_Sur`, `FondoSur` | Centro, Sur |
| Terreno generado desde datos reales | `Terrain/TerrainData_Sur.asset` (3200 m) | Sur |
| Canoa y faro | `Assets/Minigame/Canoa/*` | Sur |
| UI | `Telefono.png`, `Mira.png`, frames de la intro | Todas / Norte |

---

## 6. Herramientas de Editor propias (momento de producción)

Scripts en `Assets/Editor/`. Se ejecutan en el Editor, no en el juego.

| Script | Qué hace | Cuándo correrlo |
|---|---|---|
| `AplicarTerrenoSur.cs` | Carga `heights.bin` y `splat.bin` de `Tools/TerrenoSur` en `TerrainData_Sur` | Después de regenerar el terreno en Python |
| `VegetacionSur.cs` | Puebla el terreno de Sur con pastos, flores, arbusto y helecho según reglas por capa | Después de `AplicarTerrenoSur` |
| `BakeDistanciaCosta.cs` | Bakea `ShoreDistance_*` para que las rompientes del mar sigan la costa | Cada vez que cambia la costa |
| `GenerarTravesiaCanoa.cs` | Arma el minijuego de la canoa (ruta, rocas de FlipGameDev, faro, manager) y lo conecta al NPC | Al rehacer el minijuego |
| `BordesDeTerreno.cs` | Hundía el borde del terreno de Sur bajo el mar | Obsoleto desde el terreno de 3200 m |

**Terreno de Puerto Edén (`Tools/TerrenoSur/`):** `gen_landcover.py` → `gen_terrain.py` →
`gen_fondo.py`, con SRTM + OSM. El procedimiento completo está en
[Tools/TerrenoSur/README.md](../Tools/TerrenoSur/README.md).

---

## 7. Resumen por escena

| | Norte (Isluga) | Centro (Isla Mocha) | Sur (Puerto Edén) |
|---|---|---|---|
| **Entrada** | Intro por frames | Fundido | Fundido |
| **Cielo** | Tenkoku | Tenkoku | Tenkoku |
| **Suelo** | FlipGameDev + ADG + 4 Snow | Terrain Sample Pack | Terrain Sample Pack + 4 Snow; relieve SRTM/OSM |
| **Vegetación** | Queñoa (propia) | Detalles (Grass & Flowers, ALP, Bush Grass Bend) | Igual que Centro, colocada por reglas |
| **Agua** | — | `OceanWaterRealista` + marea | `OceanWaterRealista` |
| **Rocas** | — | — | FlipGameDev |
| **Minijuego** | Chaku (NavMesh) | Luga (Tenkoku + olas) | Canoa (física + Gerstner) |
| **Post-pro** | `SampleSceneProfile` | `PostPro_Centro` | `PostPro_Sur` |

---

## 8. Método: cómo se obtuvo esta lista

Para cada escena se siguieron las referencias por GUID de forma transitiva: escena →
prefabs → materiales → capas de terreno → texturas, leyendo tanto los archivos YAML como
los binarios (`Norte.unity` y los `TerrainData` están serializados en binario, así que sus
referencias se buscaron en su tabla de externos). Luego se agrupó cada asset por su carpeta
de origen.

El origen de las texturas de `Textures/Detalles/` se dedujo por nombre: son copias
retocadas (no idénticas byte a byte) de los paquetes indicados.

Para actualizar este documento después de cambios grandes, conviene repetir ese recorrido
en lugar de editar las tablas a mano.
