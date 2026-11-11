# AGENTS.md — Snow Factory (Conocimiento del Proyecto y Reglas del Agente)

> **Regla de Mantenimiento Continuo**: Este archivo es la memoria viva del proyecto **Snow Factory**. **En cada hito, resolución de problemas o nuevo commit**, el agente DEBE actualizar este documento añadiendo lo aprendido, las decisiones de arquitectura adoptadas y el registro de evolución del proyecto, incluyéndolo en el commit.

---

## 1. Entorno Local y Particularidades de la Máquina (Windows + Unity 6)

- **Motor de Unity**: `6000.3.27f2` (`D:\Archivos de Programa\Unity\6000.3.27f2\Editor\Unity.exe`).
- **Render Pipeline**: Universal Render Pipeline (URP `17.3.0`).
- **Ruta del Proyecto**: `D:\Unity Antigravity\Snow Factory`
- **Repositorio GitHub**: `https://github.com/akle5/snow-factory.git` (Rama principal: `main`).
- **Identidad Git**: `akle5` (`akle5@users.noreply.github.com`).

### ⚠️ Regla Crítica 1: Registro en Unity Hub (MSIX / WindowsApps)
El Unity Hub del usuario se ejecuta como paquete MSIX (`WindowsApps`), por lo que lee su base de datos desde la caché virtualizada del paquete y **no** desde `%APPDATA%\UnityHub`.
- Siempre que se cree o modifique el registro de un proyecto con `unity-cli`, se debe ejecutar con `$env:APPDATA` redirigido:
  ```powershell
  $env:APPDATA = "C:\Users\Alex\AppData\Local\Packages\UnityTechnologies.UnityHub_2vrhnee42bhxm\LocalCache\Roaming"
  unity projects add "D:\Unity Antigravity\Snow Factory" --format json
  ```

### ⚠️ Regla Crítica 2: Gestión de Instancias del Editor y `UnityLockfile`
- **No bloquear nunca el Editor del usuario**: Si el usuario va a abrir o ya tiene abierto el proyecto desde Unity Hub, conectarse directamente a su instancia activa mediante `unity status --project-path "D:\Unity Antigravity\Snow Factory"`.
- **Limpieza tras modo Batch**: Si en algún momento se lanza una instancia headless (`-batchmode`) porque no había Editor abierto, **cerrar siempre el proceso `Unity.exe` y verificar que `Temp\UnityLockfile` no quede bloqueado** antes de pedir al usuario que abra o revise el proyecto en Unity Hub.
- **Subprocesos en PowerShell**: Cada llamada a `run_command` limpia los procesos hijos al terminar salvo que el Editor haya sido abierto por el propio usuario desde Unity Hub. Preferir siempre operar sobre la instancia GUI abierta por el usuario.

### ⚠️ Regla Crítica 3: Creación de Escenas y Assets vía Pipeline
- Siempre pasar `--project-path "D:\Unity Antigravity\Snow Factory" --caller plugin --skill unity-cli` a cada comando `unity command`.
- Para crear escenas URP nuevas sin disparar diálogos modales que bloqueen el hilo principal, copiar una escena plantilla válida con `unity command asset copy` y añadirla al build con `unity command scene add_to_build`.
- Al construir gameplay, estética, iluminación, audio o UI, cargar el skill `craft-game-floors` antes de implementar.

---

## 2. Arquitectura y Diseño Técnico de *Snow Factory*

### Concepto y Core Loop Actual (Base Jugable: Pala + Montones de Nieve + Chimenea con Fuego)
- **Perspectiva**: **Primera Persona 3D** con herramienta equipada en mano (`ShovelPivot` bajo `Player/CameraRoot`).
- **Bucle Principal**:
  1. **Recolección Gradual con Pala**: El jugador lleva equipada una pala 3D (`ShovelController`) y al acercarse a los montones de nieve (`SnowDepositMound_Main` y `SnowDepositMound_Sub`, con componente `SnowPile`) recoge nieve palada a palada (`Clic Izquierdo` o `E`).
  2. **Deformación / Reducción Visual del Montón**: Cada palada reduce gradualmente la escala (`localScale`) y altura del montón de nieve (`SnowPile.TryScoop()`) hasta agotarlo, mostrando un bloque de nieve cargado sobre la hoja de la pala (`HeldSnowVisual`).
  3. **Depósito en la Chimenea con Fuego**: El jugador lleva la pala cargada hasta la chimenea encendida (`FireplaceHearth`) y arroja la nieve al fuego (`Clic Izquierdo` o `E`), provocando un destello del fuego, una bocanada de vapor (`SteamBurstParticles`) e incrementando el contador de paladas en el HUD (`FactoryHudController`).

### Perspectiva y Controles
- **Primera Persona 3D**: `CharacterController` + `InputSystem` (`WASD`, salto `Space`, sprint `LeftShift`, vista ratón con `CinemachineCamera`, `Esc` para bloquear/liberar cursor).
- **Interacción con Pala Equipada**:
  - `Clic Izquierdo` o `E` apuntando a un `SnowPile` (con la pala vacía o capacidad libre): excava el estrato de nieve/hielo y carga grumos en la hoja.
  - `Clic Izquierdo` o `E` apuntando a `Wheelbarrow`: transfiere la nieve de la pala dentro de la cuba de la carretilla conservando su multiplicador térmico (`x1 / x2 / x3`).
  - `Clic Izquierdo` o `E` apuntando a `FireplaceHearth` (con la pala cargada): arroja la nieve directamente al fuego de la chimenea.
- **Conducción y Volcado de Carretilla (`WheelbarrowController`)**:
  - `[F]` cerca/apuntando a la carretilla (o `[E]` con la pala vacía): agarra los mangos y activa el modo conducción en primera persona (la pala se guarda temporalmente).
  - `[WASD + Ratón]` + `[LeftShift]`: empuja y gira suavemente la carretilla por el patio o las laderas nevadas (adaptación de inclinación al terreno, amortiguación por peso de carga, giro físico de la rueda delantera y freno ante muros).
  - `[Clic Izquierdo / E]` cerca de `FireplaceHearth`: vuelca toda la carga de la carretilla de golpe al fuego, disparando un torrente parabólico de pellas, reacción de llama y Monedas de Calor.

### Stack y Paquetes Base
- `com.unity.render-pipelines.universal` (`17.3.0`)
- `com.unity.inputsystem` (`1.20.1`)
- `com.unity.cinemachine` (`3.1.7`)
- `com.unity.ai.navigation` (`2.0.16`)
- `com.unity.modules.uielements` (UI Toolkit `UIDocument` + `.uxml` / `.uss` — sin rectángulos gigantes `Canvas` en el Scene View 3D)
- `com.unity.pipeline` (`0.10.0-exp.1`)

### Estructura Modular (`Assets/_Project/`)
- `Assets/_Project/Scripts/Core/` — `Palette.cs` (colores base y emisivos del entorno, pala y chimenea), `FactoryEvents.cs` (eventos desacoplados del HUD).
- `Assets/_Project/Scripts/Gameplay/` — `FirstPersonController.cs`, `ShovelController.cs`, `SnowPile.cs`, `FireplaceHearth.cs`, `WheelbarrowController.cs`, `ProceduralSnowMeshUtility.cs`.
- `Assets/_Project/Scripts/UI/` — `FactoryHudController.cs` (controlador UI Toolkit enlazado a `UIDocument`).
- `Assets/_Project/UI/` — `FactoryHud.uxml`, `FactoryHud.uss`, `FactoryHudPanelSettings.asset`.
- `Assets/_Project/Scenes/` — `Boot.unity`, `MainGame.unity`, `TestSandbox.unity`.
- `Assets/_Project/Prefabs/` — `PF_Wheelbarrow.prefab`.
- `Assets/_Project/Models/` — `LowPolySnowIsland/`, `ShovelSnow/`, `Wheelbarrow/`.
- `Assets/_Project/Materials/`, `Audio/`, `Settings/`.

---

## 3. Estándares de Calidad y Flujo de Trabajo Git

1. **Un `MonoBehaviour` por archivo**: El nombre del archivo `.cs` debe coincidir exactamente con el nombre de la clase para evitar corrupción de escenas en builds.
2. **Compilación estricta**: Ejecutar `unity command script recompile` y `script recompile_status` tras cada cambio en scripts. Cero errores y cero warnings antes de continuar.
3. **Verificación activa en PlayMode**: Validar en PlayMode que el loop avanza (`playerLoopTicking: true`), revisar consola (`console get`) y tomar captura (`capture screenshot` / `capture game_view`) antes de dar un hito por terminado.
4. **Commits atómicos y actualización de `AGENTS.md`**:
   - Verificar `git status` (nunca incluir `Library/`, `Temp/`, `Logs/`, `UserSettings/`).
   - Actualizar la sección de **Bitácora de Aprendizaje y Commits** de este `AGENTS.md` en cada commit.

---

## 4. Bitácora de Aprendizaje y Evolución (Changelog & Learnings)

- **`97f065d` — Inicialización del proyecto y estructura base**:
  - Creación de `Snow Factory` en Unity `6000.3.27f2` (URP 3D).
  - Generación de jerarquía `Assets/_Project/` y escenas `Boot`, `MainGame`, `TestSandbox` añadidas a Build Settings.
  - Configuración de `.gitignore` oficial y `.gitattributes` con Git LFS y Unity Smart Merge.
  - Creación y vinculación automática del repositorio remoto en GitHub (`akle5/snow-factory`) mediante `unity projects link vcs`.
  - *Aprendizaje clave 1*: Sincronización obligatoria con la base de datos MSIX de Unity Hub (`LocalCache\Roaming\UnityHub\hub.db`) para que el proyecto sea visible en la interfaz gráfica de Unity Hub.
  - *Aprendizaje clave 2*: Liberación inmediata de instancias `-batchmode` y `Temp\UnityLockfile` para evitar el error de instancia duplicada cuando el usuario abre el proyecto desde Unity Hub.
  - *Aprendizaje clave 3*: Creación de escenas URP mediante copia de plantilla (`asset copy`) en lugar de `scene create` para evitar bloqueos por diálogos modales nativos.
- **`05886fc` — Documentación viva (`AGENTS.md`) y Diseño Técnico**:
  - Creación de `AGENTS.md` en la raíz del repositorio y en la raíz del workspace (`D:\Unity Antigravity\AGENTS.md`).
  - Definición de la arquitectura de automatización en Primera Persona 3D con construcción en Grid.
- **`ecabf1c` — Fase 1: Entorno Base Cryo-Industrial y Controlador en Primera Persona**:
  - Creación de `Palette.cs` (`Assets/_Project/Scripts/Core/Palette.cs`) como fuente única de verdad visual (estilo *Cryo-Industrial Alpine*).
  - Creación de `FirstPersonController.cs` (`Assets/_Project/Scripts/Gameplay/FirstPersonController.cs`) con `CharacterController` y nuevo `InputSystem` (`WASD`, Sprint `LeftShift`, Salto `Space`, rotación de cámara y bloqueo/desbloqueo de cursor con `Esc`).
  - Configuración de `MainGame.unity` con materiales URP Lit/Emissive, Skybox ártico procedural (`M_ArcticSkybox.mat`), iluminación direccional cálida con sombras suaves, ambiente Trilight y `SnowFactoryVolumeProfile.asset` (ACES Tonemapping, Bloom HDR, Vignette y Color Adjustments).
  - Construcción de la plataforma industrial de hormigón (`24x24`), patio exterior nevado (`60x60`), muro norte con pilares y balizas luminosas, silos criogénicos y montículos de extracción de nieve.
  - Configuración del jugador con `CinemachineBrain` y `CinemachineCamera` (Cinemachine 3.1, FOV 68°).
  - *Aprendizaje clave 4*: En `com.unity.pipeline` (`0.10.0-exp.1`), cuando el Editor está sin foco en Windows, `script recompile --focus true` fuerza la recarga inmediata del dominio. Para scripts constructores complejos de escena, `unity command script run --file Temp/<Builder>.cs --entry <Type.Method>` compila y ejecuta en memoria (~1.5 s) sin provocar recarga de dominio ni problemas de escapado de comillas de PowerShell.
  - *Aprendizaje clave 5*: El comando `capture game_view` en `com.unity.pipeline 0.10.0` utiliza el parámetro `--output-path <ruta>` (y `--source camera` cuando la pestaña Game View no está visible en primer plano), no `--save_path`.
- **Base Jugable Completa (Grid + Construcción/Demolición + Bucle de Máquinas + HUD Minimalista)**:
  - Consolidación de las fases restantes en una única **Base Jugable Funcional** lista para iterar, hacer y deshacer libremente.
  - Scripts creados (1 `MonoBehaviour`/`ScriptableObject` por archivo):
    - `FactoryTypes.cs` y `FactoryEvents.cs` (`Assets/_Project/Scripts/Core/`): Tipos de máquinas/ítems y bus de eventos desacoplado.
    - `GridManager.cs` (`Assets/_Project/Scripts/Managers/`): Cuadrícula de `12x12` celdas de `2x2 m` sobre la plataforma de hormigón (`24x24 m`), auto-registro de máquinas pre-colocadas en `Start()` y consulta de vecinos por orientación.
    - `EconomyManager.cs` (`Assets/_Project/Scripts/Managers/`): Gestión de créditos (`$250` iniciales), cobro al construir y reembolso del 75% al demoler.
    - `BuildManager.cs` (`Assets/_Project/Scripts/Managers/`): Selección con teclas `1–4`, holograma `BuildGhostPreview` (cian válido / rojo inválido), rotación de 90° (`R`), colocación (`Clic Izquierdo`), demolición (`Clic Derecho` o `X`), depósito manual de nieve (`E`) y cambio de modo (`B`/`Tab`).
    - `BuildableMachineSO.cs` y `PlacedMachine.cs` (`Assets/_Project/Scripts/Gameplay/`): 4 máquinas base (`PF_SnowExtractor`, `PF_ConveyorBelt`, `PF_SnowCompactor`, `PF_DeliveryHub`) y sus `ScriptableObjects` en `Assets/_Project/Settings/`, con movimiento visual de bloques de nieve (`RawSnow`) y bloques de hielo compactado (`CompactedIce`).
    - `FactoryHudController.cs` (`Assets/_Project/Scripts/UI/`): HUD minimalista con contador de créditos, máquina seleccionada y barra inferior de atajos.
  - Línea de producción inicial (`StarterFactoryLine`) pre-instalada en el centro de la planta para que el bucle económico funcione desde el segundo 1 en PlayMode.
  - *Aprendizaje clave 6*: En Unity 6, `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")` es la fuente built-in válida para `UnityEngine.UI.Text` (en lugar de `Arial.ttf`, que lanza excepción en versiones recientes).
  - *Aprendizaje clave 7*: En `FirstPersonController`, inicializar `_cameraPitch` en `Awake()` (ej. `initialPitchAngle = 14f`) e ignorar los primeros frames de salto de delta del ratón (`_ignoreMouseFrames`) cuando `Cursor.lockState == CursorLockMode.Locked` evita que el re-centrado inicial del cursor de Windows gire bruscamente la cámara hacia el cielo al entrar en PlayMode.
- **Pivote de Diseño — Eliminación de Máquinas/Modo Construcción e Implementación de Pala + Montón de Nieve + Chimenea con Fuego**:
  - A petición del usuario, se eliminaron por completo las máquinas industriales, el sistema de cuadrícula y el modo construcción (`StarterFactoryLine`, `FactoryManagers`, `BuildManager`, `GridManager`, `EconomyManager`, `PlacedMachine`, `BuildableMachineSO`, prefabs `PF_*` y assets `SO_*`).
  - Se implementó el bucle en primera persona centrado en:
    - `SnowPile.cs` (`Assets/_Project/Scripts/Gameplay/SnowPile.cs`): Asociado a `SnowDepositMound_Main` (`12` paladas) y `SnowDepositMound_Sub` (`8` paladas). Reduce gradualmente la escala y altura del montón con cada palada hasta agotarse.
    - `ShovelController.cs` (`Assets/_Project/Scripts/Gameplay/ShovelController.cs`): Pala 3D equipada en primera persona (`Player/CameraRoot/ShovelPivot`) con animación procedural de palada/lanzamiento y nieve visible sobre la hoja (`HeldSnowVisual`).
    - `FireplaceHearth.cs` (`Assets/_Project/Scripts/Gameplay/FireplaceHearth.cs`): Chimenea de ladrillo y piedra con brasas emisivas (`M_FireEmber`), llamas animadas (`FlameCore` con `M_FireFlame`), luz puntual cálida parpadeante (`FirePointLight`) y partículas de vapor (`SteamBurstParticles`) al arrojar nieve al fuego.
- **Refinamiento Visual de la Pala en Primera Persona (`ScoopHead`)**:
  - Se rediseñó la geometría y orientación de la pala equipada (`Player/CameraRoot/ShovelPivot/ScoopHead`) para eliminar la ilusión óptica en primera persona donde la pared trasera tapaba el interior de la hoja: ahora la cuchara cóncava (`BladePanRear` + `BladePanFront`), sus paredes laterales acampanadas y el filo metálico delantero (`BladeCuttingEdge`) están orientados hacia arriba y claramente visibles desde el ángulo de la cámara.
- **Identidad Visual Propia "Premium Alpine Snow" (`PremiumSnowURP.shader` + Deformación Orgánica por Vértices)**:
  - Creación de `PremiumSnowURP.shader` (`Assets/_Project/Materials/PremiumSnowURP.shader`) en HLSL nativo para URP:
    - **Subsurface Scattering (SSS) y Rampa Glaciar**: Transición suave de 3 paradas entre sombra azul glaciar profunda, tono alpino medio y cresta iluminada por el sol, además de recibir el resplandor cálido anaranjado de la chimenea cercana.
    - **Micro-destellos de cristal (*View-Dependent Sparkles*)**: Celdas cristalinas procedurales 3D en espacio de mundo moduladas por el vector de vista (`ViewDir`) y dirección de luz, generando destellos vivos al mover la cámara (inspirado en *Journey* y *Horizon Zero Dawn: The Frozen Wilds*).
    - **Micro-relieve procedural FBM**: Perturbación de normales por diferencias finitas para simular nieve en polvo y dunas barridas por el viento sin costuras de UV.
  - Evolución de `SnowPile.cs` (`Assets/_Project/Scripts/Gameplay/SnowPile.cs`):
    - Generación procedural de mallas orgánicas de ventisqueros (`BuildOrganicSnowDriftMesh`) con falda ondulada y perfil acolchado natural en lugar de esferas primitivas.
    - **Excavación localizada real por vértices (`CarveCraterAtWorldPoint`)**: Cada palada hunde un cráter real en el punto exacto de impacto (`hit.point`), sombrea el interior de la cavidad excavada con oclusión azul glaciar en el color de vértice (`vertexCol.r`), actualiza el `MeshCollider` y dispara una bocanada de polvo de nieve (`SnowScoopBurstParticles`).
  - Incorporación de nevada ambiental suave (`AmbientSnowfallParticles`) en el patio.
- **Bucle Incremental: Capacidad Acumulable en la Pala + Monedas de Calor y Mejoras**:
  - `ShovelController.cs` (`Assets/_Project/Scripts/Gameplay/ShovelController.cs`):
    - Capacidad inicial de `3` paladas acumulables (`maxSnowCapacity`).
    - **Opción A (Llenado Orgánico de la Cuchara + Vuelo Parabólico)**: En lugar de estirar una esfera hacia arriba (que a niveles altos parecía un huevo gigante), la cuchara (`ScoopHead`) contiene una cama de 9 grumos orgánicos de bajo perfil (`ClumpBedRoot`) que van llenando el interior cóncavo de la hoja de atrás hacia adelante y de lado a lado. Además, al excavar, un trozo de nieve (`FlyingSnowClump`) salta físicamente en parábola desde el punto de impacto (`hit.point`) hasta el interior de la pala; y al arrojar a la chimenea, las pellas de nieve vuelan en arco desde la pala hasta caer dentro del fuego.
    - **Opción B (Mejora de Pala Ancha con `[U]`)**: Al mejorar la pala con `[U]`, la cuchara metálica (`ScoopHead`) **se ensancha físicamente** (`ApplyWideBladeUpgradeVisual`) y excava cráteres de mayor radio (`radiusMultiplier`) en los montones de nieve.
    - Atajo `[R]` para invocar una nueva nevada que regenera al 100% los ventisqueros (`SnowPile.RefillPile()`).
    - Atajo de Testing `[I]` (`infiniteTestingMode`): activa/desactiva el modo de **Pala con Excavación Infinita** (sin límite de carga en la pala, sin agotar los montones y permitiendo mantener pulsado `Clic Izquierdo` o `E` para cavar trincheras de forma continua).
  - `PremiumSnowURP.shader` (`Assets/_Project/Materials/PremiumSnowURP.shader`):
    - Corrección de los destellos de cristal para usar puntos circulares con caída radial (`length(cellLocal - glintOffset)`) dentro de cada celda 3D, eliminando por completo los artefactos de cuadraditos planos cuando la nieve está cerca de la cámara.
  - `FireplaceHearth.cs` (`Assets/_Project/Scripts/Gameplay/FireplaceHearth.cs`):
    - Convierte las paladas fundidas en **Monedas de Calor** (`10` monedas base por palada + **Bonus de Pila** progresivo cuando entregas varias paladas acumuladas de una sola vez).
  - `FactoryHudController.cs` (`Assets/_Project/Scripts/UI/FactoryHudController.cs`):
    - Muestra en tiempo real `MONEDAS DE CALOR: X | PALA: carga/capacidad | FUNDIDAS: total`.
- **Importación Directa de Modelos glTF de Sketchfab (`Low Poly Snow Island`) y Excavación en Terrenos 3D Importados**:
  - Se extrajo `low_poly_snow_island.zip` en `Assets/_Project/Models/LowPolySnowIsland/` y se convirtieron sus buffers binarios (`scene.gltf` + `scene.bin`) a assets nativos `Mesh` de Unity (`SnowIsland_TerrainMesh.asset` con `10.396` vértices facetados y `SnowIsland_CaveMesh.asset` con `2.362` vértices) sin requerir paquetes externos.
  - Se colocó el objeto `Sketchfab_LowPolySnowIsland` con `MeshCollider` en el lateral derecho del patio (`(32.5, 3.6, 14.0)`).
  - Se amplió `SnowPile.cs` con el modo **`useExistingMesh` (`ConfigureCustomMeshTerrain`)** y cálculo de normales facetadas por triángulo (`RecalculateFlatNormals`): ahora cualquier terreno o modelo 3D importado de Sketchfab se puede excavar con la pala en coordenadas de mundo, hundiendo las caras low-poly en el punto exacto donde clavas la pala, manteniendo su estética facetada ("flat shading") intacta y regenerándose al pulsar `[R]`.
  - **Refinamiento de Excavación de Picos, Pilares y Sub-mallas en Terrenos Low-Poly (`SnowPile.cs` + `ShovelController.cs`)**:
    - *Causa raíz de las agujas/pilares al excavar*:
      1. El empuje de borde (`rimPush`) elevaba los vértices del anillo exterior en cada palada; al mantener pulsado el clic en modo infinito (`[I]`), cientos de paladas acumulaban `rimPush` extruyendo agujas verticales de 15 metros.
      2. El filtro vertical (`Mathf.Abs(dy) < worldRadius * 1.4f`) ignoraba las cumbres situadas más de `3 m` por encima del punto de impacto cuando el jugador cavaba la base o ladera de un pico alto, dejando agujas imposibles de bajar desde el suelo.
      3. Los modelos low-poly facetados tienen vértices no compartidos por triángulo (duplicados en la misma coordenada), por lo que relajar vértices individualmente abría micro-fisuras entre caras o dejaba láminas aisladas.
      4. La sub-malla secundaria `SnowIsland_Cave` (`Plane.001_0`) no tenía `SnowPile` configurado.
    - *Solución implementada*:
      - Eliminación total de `rimPush` hacia arriba y bloqueo contra elevación por encima de la altura actual/original (`Mathf.Clamp(newWorldY, minWorldFloorY, vWorld.y)`).
      - **Colapso de picos y voladizos (*Overhang / Pillar Collapse*)**: al excavar en la base o ladera de un pico de nieve/hielo (`dy > worldRadius * 0.65f`), toda la columna superior dentro del radio cede hacia abajo de forma natural.
      - **Relajación Topológica con Soldadura Virtual de Clusters (`BuildTopologicalNeighbors` + `_weldedVertexClusters`)**: agrupa espacialmente los vértices duplicados de las facetas low-poly para construir el grafo de vecindad real entre triángulos adyacentes y mueve cada cluster soldado al unísono, eliminando láminas afiladas sin abrir ni una sola fisura entre triángulos.
      - Configuración de `SnowPile` tanto en `SnowIsland_Terrain` como en `SnowIsland_Cave` y propagación automática de la excavación a sub-mallas hermanas en `ShovelController.cs`.
  - **Eliminación de Triángulos Negros Solapados (Z-Fighting) y Detección de Sub-mallas Bajo el Suelo (`SnowPile.cs` + `ShovelController.cs`)**:
    - *Causa raíz de los triángulos negros solapados al excavar toda la isla*:
      1. Ese pico interior oscuro (`SnowIsland_Cave`) tiene paredes verticales y techo de cueva; cuando sus vértices superiores bajan hasta aplastarse contra la base, el orden de giro (*winding order*) del triángulo se invierte respecto a `+Y` y su normal apunta hacia abajo (`-Y`), haciendo que el shader lo ilumine como **negro puro**.
      2. El tope de suelo anterior (`minWorldFloorY = 0.03f`) frenaba los vértices `3 cm` por encima de `SnowYardFloor` (`Y = 0.0f`), dejando una lámina pegada encima del suelo haciendo *z-fighting* (esos bordes dentados negros/blancos).
      3. Al usar `Physics.Raycast` simple en `ShovelController`, en cuanto la nieve rozaba el suelo `SnowYardFloor`, el rayo chocaba primero contra el `BoxCollider` del suelo e impedía terminar de hundir esos últimos triángulos.
    - *Solución implementada*:
      - En `SnowPile.RecalculateFlatNormals`: si tras aplastar una cueva o voladizo la normal en espacio de mundo apunta hacia abajo (`worldNormal.y < 0f`), se invierte automáticamente (`faceNormal = -faceNormal`) para que ninguna cara colapsada se vuelva negra jamás.
      - Se bajó `minWorldFloorY` a `-0.06f` (`6 cm` por debajo de `SnowYardFloor`) para que al excavar hasta el fondo toda la malla quede limpiamente enterrada bajo la nieve del suelo sin *z-fighting*.
      - En `ShovelController.UpdateHoverAndInteract`: se reemplazó `Physics.Raycast` por `Physics.RaycastAll`, priorizando cualquier `SnowPile` en el rayo aunque esté a ras de `SnowYardFloor`.
- **Migración del HUD de uGUI (`Canvas` `Screen Space - Overlay`) a UI Toolkit (`UIDocument`)**:
  - Se eliminó el antiguo `Canvas` de uGUI que proyectaba un rectángulo gigante de `1920x1080` metros en mitad de la pestaña `Scene` 3D.
  - Se crearon [`FactoryHud.uxml`](file:///D:/Unity%20Antigravity/Snow%20Factory/Assets/_Project/UI/FactoryHud.uxml), [`FactoryHud.uss`](file:///D:/Unity%20Antigravity/Snow%20Factory/Assets/_Project/UI/FactoryHud.uss) y `FactoryHudPanelSettings.asset` en `Assets/_Project/UI/`, y se actualizó [`FactoryHudController.cs`](file:///D:/Unity%20Antigravity/Snow%20Factory/Assets/_Project/Scripts/UI/FactoryHudController.cs) para usar `UIDocument` (`UnityEngine.UIElements`), dejando la vista `Scene` 3D 100% limpia de cajas de interfaz.
- **Mecánica de Capas de Dureza en el Terreno (Nieve Blanda → Hielo Compacto → Núcleo Glaciar Azul) y Mejora de Filo Térmico `[T]`**:
  - [`PremiumSnowURP.shader`](file:///D:/Unity%20Antigravity/Snow%20Factory/Assets/_Project/Materials/PremiumSnowURP.shader): Utiliza el canal verde del color de vértice (`vertexCol.g`) para interpolar entre 3 estratos visuales con más destellos cristalinos al profundizar:
    1. **Capa Superficial (`G = 1.0`, Blanca)**: Nieve Polvo blanda (valor `x1` Moneda de Calor).
    2. **Capa Media y Picos Helados (`G ≈ 0.5`, Azul Claro Cian)**: Hielo Compacto (valor `x2` Monedas de Calor; la pala básica de nivel 1 apenas lo hunde al `24%`).
    3. **Núcleo Glaciar Profundo (`G ≈ 0.0`, Azul Cobalto/Zafiro)**: Glaciar profundo que emerge al excavar hondo o en las cumbres más altas (valor **triple `x3`** Monedas de Calor; requiere Filo Térmico).
  - [`ShovelController.cs`](file:///D:/Unity%20Antigravity/Snow%20Factory/Assets/_Project/Scripts/Gameplay/ShovelController.cs) y [`FireplaceHearth.cs`](file:///D:/Unity%20Antigravity/Snow%20Factory/Assets/_Project/Scripts/Gameplay/FireplaceHearth.cs):
    - Tecla **`[T]` (`Filo Térmico`)**: Mejora el filo de la pala (`Nv.1 → Nv.2 → Nv.3`, iluminando el borde metálico `BladeCuttingEdge` con emisión incandescente naranja) para cortar el Hielo Compacto y el Núcleo Glaciar sin penalización de dureza (en modo test `[I]` se puede mejorar gratis para probar).
    - Acumulación real del valor por capa (`_accumulatedValueUnits`) al recoger paladas y entrega con multiplicador `x1 / x2 / x3` en la chimenea.
- **Integración del Modelo 3D de Sketchfab `Shovel snow` (`Assets/_Project/Models/ShovelSnow/`)**:
  - Se trasladó `shovel_snow.zip` desde `E:\Descargas 3TB v2\shovel_snow.zip` al disco `D:\` dentro del proyecto (`Assets/_Project/Models/ShovelSnow/`), extrayendo `scene.gltf`, `scene.bin`, `license.txt` (CC-BY-4.0, autor Klimuk Sergey) y `textures/Logo_white_baseColor.png`.
  - Se convirtieron las primitivas glTF a assets nativos `Mesh` de Unity y se separó la malla metálica en el tubo del mango (`ShovelSnow_MetalShaft.asset`) y el filo cortante delantero (`ShovelSnow_CuttingLip.asset`), junto con la cuchara de plástico negro nervado (`ShovelSnow_BlackPlastic.asset`, `21.138` vértices) y el logo del mango (`ShovelSnow_LogoWhite.asset`).
  - Se reemplazó la pala primitiva bajo `Player/CameraRoot/ShovelPivot/ScoopHead` por el nuevo modelo 3D manteniendo todas las mecánicas conectadas: cama de 9 grumos de nieve (`ClumpBedRoot`) ajustada al interior cóncavo nervado, ensanchamiento físico con `[U]` y brillo incandescente exclusivo en el labio metálico delantero (`BladeCuttingEdge`) al mejorar el Filo Térmico con `[T]`.
- **Reversión del Material PBR Triplanar `Snow Material` en `SnowIsland_Terrain` (`f404656` revertido en `d4213f2`)**:
  - Por preferencia estética del usuario, se revirtió el commit `f404656` eliminando `Assets/_Project/Materials/SketchfabSnowMaterial/` y restaurando `M_SnowIslandTerrain`, `M_SnowIslandRock`, `PremiumSnowURP.shader`, `M_ShovelLogoWhite.mat` y `MainGame.unity` al estilo estilizado alpino de `c5c265f`, conservando intacta la pala 3D de Sketchfab (`Shovel snow`).
- **Carretilla Conducible con Carga Acumulable de Nieve y Volcado en la Chimenea (`Wheelbarrow_13_MB` — `87a28d8`)**:
  - Se extrajo `wheelbarrow_13_mb.zip` en [`Assets/_Project/Models/Wheelbarrow/`](file:///D:/Unity%20Antigravity/Snow%20Factory/Assets/_Project/Models/Wheelbarrow/) (`scene.gltf`, `scene.bin`, `license.txt` bajo licencia CC-BY-4.0 de Mehdi Shahsavan, y texturas PBR) eliminando el `.zip` original tras la extracción.
  - Se generó el asset separado del chasis/cuba (`Wheelbarrow_Base.asset`) y la rueda delantera (`Wheelbarrow_Wheel.asset`) orientados con `+Z` al frente y pivote común en el eje de la rueda (`(0.0175, 0.1981, 0.3621)`), junto al material PBR URP de doble cara [`M_Wheelbarrow.mat`](file:///D:/Unity%20Antigravity/Snow%20Factory/Assets/_Project/Models/Wheelbarrow/M_Wheelbarrow.mat) con mapa metálico/suavidad convertido desde canales glTF (`B -> R`, `1-G -> A`) y mapa normal.
  - Implementación de [`WheelbarrowController.cs`](file:///D:/Unity%20Antigravity/Snow%20Factory/Assets/_Project/Scripts/Gameplay/WheelbarrowController.cs) y creación de [`PF_Wheelbarrow.prefab`](file:///D:/Unity%20Antigravity/Snow%20Factory/Assets/_Project/Prefabs/PF_Wheelbarrow.prefab) instanciado en [`MainGame.unity`](file:///D:/Unity%20Antigravity/Snow%20Factory/Assets/_Project/Scenes/MainGame.unity):
    - Capacidad de **`24` paladas** con cama visual de `14` grumos de nieve en el interior de la cuba que se tiñen gradualmente según la proporción de Hielo Compacto (`x2`) y Núcleo Glaciar (`x3`) acumulado.
    - Conducción en primera persona (`[F]` cerca o `[E]` con pala vacía): elevación de mangos `+6°`, seguimiento suave delante del jugador, giro físico de la rueda delantera según la distancia recorrida, adaptación automática de inclinación al terreno y comprobación frontal de obstáculos (`CheckFrontObstacle`).
    - Carga desde la pala (`ShovelController.SpawnPourChunksToWheelbarrow`) con transferencia en parábola y multiplicadores preservados, y volcado directo en la hoguera (`[Clic Izq / E]` en rango de `FireplaceHearth`) con animación frontal `+38°` y ráfaga múltiple al fuego.
  - Ampliación de [`FactoryEvents.cs`](file:///D:/Unity%20Antigravity/Snow%20Factory/Assets/_Project/Scripts/Core/FactoryEvents.cs), [`FirstPersonController.cs`](file:///D:/Unity%20Antigravity/Snow%20Factory/Assets/_Project/Scripts/Gameplay/FirstPersonController.cs), [`ShovelController.cs`](file:///D:/Unity%20Antigravity/Snow%20Factory/Assets/_Project/Scripts/Gameplay/ShovelController.cs), [`FactoryHudController.cs`](file:///D:/Unity%20Antigravity/Snow%20Factory/Assets/_Project/Scripts/UI/FactoryHudController.cs) y [`FactoryHud.uxml`](file:///D:/Unity%20Antigravity/Snow%20Factory/Assets/_Project/UI/FactoryHud.uxml) con indicador permanente `CARRETILLA: X/24`.
- **Nieve Volumétrica Continua Adaptada a Carretilla/Pala y Normalización en Espacio de Objeto del Shader URP**:
  - Se añadió [`ProceduralSnowMeshUtility.cs`](file:///D:/Unity%20Antigravity/Snow%20Factory/Assets/_Project/Scripts/Gameplay/ProceduralSnowMeshUtility.cs) con generación procedural de mallas de nieve volumétricas continuas contorneadas al interior real de la cuba de la carretilla (`WheelbarrowSnowMound`) y de la cuchara de la pala (`ShovelSnowMound`), además de variantes orgánicas de grumos en vuelo (`FlyingSnowClump` y `WheelbarrowDumpChunk`).
  - Se extrajeron los helpers compartidos de materiales runtime (`CarriedSnowMaterialProfile`, `FindSceneSnowMaterial`, `GetOrCreateCarriedSnowMaterial`, `ApplyCarriedSnowHardnessTint`) en [`ProceduralSnowMeshUtility.cs`](file:///D:/Unity%20Antigravity/Snow%20Factory/Assets/_Project/Scripts/Gameplay/ProceduralSnowMeshUtility.cs), eliminando la creación y tintado duplicado de materiales entre [`WheelbarrowController.cs`](file:///D:/Unity%20Antigravity/Snow%20Factory/Assets/_Project/Scripts/Gameplay/WheelbarrowController.cs) y [`ShovelController.cs`](file:///D:/Unity%20Antigravity/Snow%20Factory/Assets/_Project/Scripts/Gameplay/ShovelController.cs).
  - En [`PremiumSnowURP.shader`](file:///D:/Unity%20Antigravity/Snow%20Factory/Assets/_Project/Materials/PremiumSnowURP.shader), la rama `_UseObjectSpace > 0.5` evalúa las coordenadas en espacio de objeto métrico (`positionOS_local * objScale`), calcula derivadas FBM 3D completas rotadas exclusivamente por ejes ortonormales (`dirX`, `dirY`, `dirZ`) sin amplificación `1 / scale` y proyecta el gradiente sobre el plano tangente a `normalWS`, eliminando tanto el estiramiento en la pala ensanchada como las bandas diagonales cian-blanco en los trozos pequeños en vuelo.
