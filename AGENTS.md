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

### Concepto y Core Loop
- **Género**: Automatización y gestión de fábrica en **Primera Persona 3D**.
- **Bucle Principal**:
  1. **Extracción / Recolección**: Obtención de nieve virgen (manual o mediante Extractores de Nieve).
  2. **Transporte Logístico**: Movimiento físico de recursos sobre **Cintas Transportadoras (Conveyors)** conectadas en cuadrícula (`Grid`).
  3. **Procesamiento en Máquinas**: Transformación de nieve suelta en bloques compactos/hielo industrial (`SnowCompactor`, etc.).
  4. **Entrega y Economía**: Recepción en muelle de venta (`DeliveryHub`) para generar créditos (`SnowCredits`) y construir/expandir la fábrica.

### Perspectiva y Controles
- **Primera Persona 3D**: `CharacterController` + `InputSystem` (`WASD`, salto, sprint, vista ratón con `CinemachineCamera`).
- **Interacción y Construcción en Grid**:
  - Raycast central para interactuar con máquinas/recursos.
  - Modo Construcción (`Build Mode`) con previsualización holográfica ajustada al Grid (`Ghost Preview`), rotación de 90° (`R`), validación de posición/coste y colocación/demolición.

### Stack y Paquetes Base
- `com.unity.render-pipelines.universal` (`17.3.0`)
- `com.unity.inputsystem` (`1.20.1`)
- `com.unity.cinemachine` (`3.1.7`)
- `com.unity.ai.navigation` (`2.0.16`)
- `com.unity.ugui` (`2.0.0` - Canvas + TextMeshPro)
- `com.unity.pipeline` (`0.10.0-exp.1`)

### Estructura Modular (`Assets/_Project/`)
- `Assets/_Project/Scripts/Core/` — Interfaces base (`IInteractable`, `ITickable`), bus de eventos desacoplado, bootstrap.
- `Assets/_Project/Scripts/Managers/` — `GridManager`, `BuildManager`, `EconomyManager`.
- `Assets/_Project/Scripts/Gameplay/` — `FirstPersonController`, `PlayerInteractor`, máquinas (`SnowExtractor`, `ConveyorBelt`, `SnowCompactor`, `DeliveryHub`), datos (`ScriptableObjects`).
- `Assets/_Project/Scripts/UI/` — HUD en uGUI/TextMeshPro, hotbar de construcción, retícula contextual.
- `Assets/_Project/Scenes/` — `Boot.unity`, `MainGame.unity`, `TestSandbox.unity`.
- `Assets/_Project/Prefabs/`, `Materials/`, `Audio/`, `Settings/`.

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
- **Fase 1 — Entorno Base Cryo-Industrial y Controlador en Primera Persona**:
  - Creación de `Palette.cs` (`Assets/_Project/Scripts/Core/Palette.cs`) como fuente única de verdad visual (estilo *Cryo-Industrial Alpine*).
  - Creación de `FirstPersonController.cs` (`Assets/_Project/Scripts/Gameplay/FirstPersonController.cs`) con `CharacterController` y nuevo `InputSystem` (`WASD`, Sprint `LeftShift`, Salto `Space`, rotación de cámara y bloqueo/desbloqueo de cursor con `Esc`).
  - Configuración de `MainGame.unity` con materiales URP Lit/Emissive, Skybox ártico procedural (`M_ArcticSkybox.mat`), iluminación direccional cálida con sombras suaves, ambiente Trilight y `SnowFactoryVolumeProfile.asset` (ACES Tonemapping, Bloom HDR, Vignette y Color Adjustments).
  - Construcción de la plataforma industrial de hormigón (`24x24`), patio exterior nevado (`60x60`), muro norte con pilares y balizas luminosas, silos criogénicos y montículos de extracción de nieve.
  - Configuración del jugador con `CinemachineBrain` y `CinemachineCamera` (Cinemachine 3.1, FOV 68°).
  - *Aprendizaje clave 4*: En `com.unity.pipeline` (`0.10.0-exp.1`), cuando el Editor está sin foco en Windows, `script recompile --focus true` fuerza la recarga inmediata del dominio. Para scripts constructores complejos de escena, `unity command script run --file Temp/<Builder>.cs --entry <Type.Method>` compila y ejecuta en memoria (~1.5 s) sin provocar recarga de dominio ni problemas de escapado de comillas de PowerShell.
  - *Aprendizaje clave 5*: El comando `capture game_view` en `com.unity.pipeline 0.10.0` utiliza el parámetro `--output-path <ruta>` (y `--source camera` cuando la pestaña Game View no está visible en primer plano), no `--save_path`.
