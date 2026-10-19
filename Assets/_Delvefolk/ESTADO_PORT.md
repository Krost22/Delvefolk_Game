# Estado del port de Delvefolk a Unity (rama GameJam)

Spec: `C:\Users\eduar\OneDrive\Documentos\Proyectos juegos web\Delvefolk web\docs\PROMPT_UNITY.md`
Original (three.js): `C:\Users\eduar\OneDrive\Documentos\Proyectos juegos web\Delvefolk web\js`

## Hecho (fases 0–6)
- 0–1 núcleo: bucle único determinista (`GameLoop.Steps`), física de círculos, entrada inyectable, RNG mulberry32.
- 2 combate y mazmorra · 3 cooperativo · 4 presentación (HUD, lobby, audio sintetizado) · 5 habilidades.
- 6 equipo y pisos bonus: `Scripts/Bonus/*` (6 desafíos: minas, puente, botín maldito, arena, banquete, último aliento),
  recompensas (`BonusRewardService`), HUD bonus e intros ilustradas (`Scripts/UI/HudBonus.cs`, `BonusIllustrations.cs`).
- Corrección pedida: el panel "DERRIBADO" se quita al revivir (`Player.Revive`).
- Pruebas: EditMode 47/47, PlayMode 73/73 (al cerrar la fase 6).

El juego es jugable de principio a fin: título → lobby → pisos → bonus → tienda → jefes cada 2 pisos → victoria.

## Hecho (fase 7)
- Tienda tras cada bonus (`Scripts/Shop/ShopFloor.cs`, `Core/Game.Shop.cs`), Duende Tendero (`Entities/ShopKeeper.cs`),
  armas especiales y escopeta (`Items/SpecialWeapons.cs`), barra de jefe del HUD (`UI/HudBoss.cs`).
- Pruebas `ShopTests` (5). Totales: EditMode 47/47, PlayMode 78/78.

## Hecho (fase 8)
- Núcleo de jefes (`Scripts/Bosses/BossCore.cs`: avisos, ondas, proyectiles devolvibles, `Boss`, registro), `BossFloor.cs`,
  `Core/Game.Boss.cs`. Jefes cada 2 pisos en el orden Gólem → Gemelos → Chef → Campana (`MatchSettings.bosses`).
- Pruebas: `BossTests` 11, `TwinsTests` 8, `ChefTests` 8, `BellTests` 10. Totales: EditMode 47/47, PlayMode 115/115.
- Cambio de prueba: en `BellTests` el barril abre la campana en el primer frame (el original lo veía aún en "opening").

## Hecho (tras la fase 8)
- §11 resuelta: el rodillo de Último Aliento se esquiva saltando (`JumpY > DodgeHeight·0.6` ≈ 0.51), como dice su viñeta.
  Prueba `UltimoAliento_ElRodilloSeEsquivaSaltando`. Totales: EditMode 47/47, PlayMode 116/116.

## Demo: un solo jefe final, El Crupier (2026-10-17)
- Los jefes normales están apartados, no borrados: `BossConfig.DemoFinalBoss = "crupier"` (`Bosses/BossCore.cs`) hace que
  solo haya un jefe, al bajar del último piso (`LevelData.Levels.Length`), justo antes de la victoria. Para volver a
  Gólem → Gemelos → Chef → Campana cada 2 pisos, ponlo a `null`. Las pruebas lo ponen a `null` en `GameTestBase`.
- Código en `Scripts/Bosses/Crupier/`: `RouletteConfig` (ScriptableObject + `RouletteMath`), `RouletteWheel` (visual),
  `RouletteController` (estados Idle → Preparing → Spinning → Revealing → ApplyingEffect → Cooldown, `IRouletteAbility`,
  `RouletteAbilityHandler`), `CrupierBoss` (rastrillo, abanico de cartas, ruleta) y `CrupierAbilities` (sala de casino y las
  6 habilidades: rojo quema las casillas rojas, verde escudo rompible, negro ráfagas de fichas, dorado fichas con premio,
  azul mesas que suben, morado ilusión).
- La ruleta va con el bucle del juego (no coroutines) y la semilla de la partida (`G.Rand`): el resultado se elige antes de
  girar y la rueda se detiene en su segmento; si el indicador no coincide, `LogError` "[Ruleta] El indicador señala…".
- Configuración: `Resources/CrupierRoulette.asset` (pesos, tiempos, curva, eje, sentido, ángulo del indicador y de cada
  segmento, modelos y herramientas de PRUEBA: forzar resultado y girar sin habilidad, solo en editor / build de desarrollo).
- **Modelo del Crupier pendiente (placeholder por código)**: asígnalo en `CrupierRoulette.asset`: `CrupierModel`
  (+ `CrupierHeight`). La rueda ya usa el modelo real (`WheelModel` (+ `WheelSpinnerPath` si solo gira un hijo). Con la rueda real hay que
  ajustar `SpinAxis`, `ReferenceDir`, `PointerAngle`, `Clockwise` y los ángulos (o `UniformAngles`) a su orientación;
  la prueba `Ruleta_SeDetieneEnElSegmentoElegidoYFrenaPocoAPoco` y el aviso de desajuste lo comprueban.
- **Sala del trono (escenario del jefe final)**: `CasinoRoom` monta la sala de la referencia con los modelos de Meshy de
  `Assets/_Custom/Jefe final escenario models`: trono y dos leones al fondo con cortinas, alfombra octogonal con corona
  (girada 45°), pasillos de tréboles en cruz, cuatro columnas de picas, mesas de casino en las esquinas con candelabros y
  luces, y tragaperras en las paredes. Las casillas con alfombra roja son las que quema el resultado ROJO. Sin los
  modelos, solo se dibuja la alfombra roja por código.
- `Delvefolk/Preparar sala del Crupier` (`Editor/CasinoSetup.cs`): materiales URP con textura y normales (en
  `Art/JefeFinal`, reasignados en cada FBX), catálogo `Resources/CasinoArt` y la rueda de la fortuna partida en marco
  fijo + centro que gira (`RuedaCrupier.prefab`); los segmentos de color los dibuja el juego dentro del marco. Repetirlo
  si cambian los modelos.
- Pruebas: `RouletteLogicTests` (5, EditMode) y `CrupierTests` (14, PlayMode). Totales: EditMode 52/52, PlayMode 130/130.

## Añadidos de la demo (2026-10-17)
- **TNT en la mano** (`Player.Tnt.cs`, `Barrel.cs`): el barril de pólvora se coge con {E}; en la mano arde una mecha de 3 s
  con cuenta atrás; {A} la lanza lejos (puntería asistida a enemigos) y explota al chocar con enemigos, compañeros o muros;
  {E} la suelta (sigue ardiendo). Si no se lanza, explota en la mano.
- **Modo solitario** (`Core/Game.Solo.cs`, `SoloConfig`): con un jugador, enemigos ×1,4 de vida, jefes ×1,3, daño recibido
  ×1,35 y niebla gris densa pegada al jugador. Si se une el J2, se va. Las pruebas lo apagan en `GameTestBase`.
- **Menos texto**: pantalla de inicio sin el párrafo de consejos (una línea) y ficha de habilidad con una ventaja y una
  desventaja, sin subtítulo ni línea de compañeros.
- **Premio del Crupier** (`Bosses/Crupier/CrupierPrize.cs`): al vencerle, lluvia de monedas y ruleta del premio sobre la
  corona; cada jugador la gira una vez ({E} junto a la corona): dorado jackpot, verde vida llena, rojo/azul botín raro,
  morado dos botines raros, negro unas monedas.
- Pruebas `TntTests` (7). Totales: EditMode 52/52, PlayMode 137/137.

## Controles y hasta 4 jugadores (2026-10-17)
- **J1 en teclado**: WASD mover · ESPACIO saltar · X atacar · Z habilidad · E recoger. J2 (teclado derecho) sin cambios.
- **Dispositivos** (`Core/PlayerControls.cs`): `DeviceBinding` = teclado izquierdo, teclado derecho o mando (hueco 0..3).
  Solo 2 jugadores en el teclado; J3 y J4 con mando. `Game.Bindings[i]` guarda el de cada jugador.
- **Input System** (`Core/InputHub.cs`): hasta 4 mandos de cualquier tipo (Xbox, PlayStation, Switch Pro, genéricos y
  joysticks HID); cada uno ocupa un hueco fijo (GP0..GP3) desde que se conecta. Mando: A atacar · X recoger · B saltar ·
  Y/RB habilidad · START unirse. Si se desconecta el mando de un jugador, pausa.
- **Elegir dispositivo**: en el título, ESPACIO/X → J1 con teclado; A en un mando → J1 con ese mando; ENTER → dos en
  teclado. En la sala (4 columnas), un dispositivo libre se une con su botón; cada uno cambia de control con su tecla de
  habilidad (Z / CTRL der. / Y) o el botón "Cambiar control"; INTERACTUAR sale. Ajuste de habilidades exclusivas: tecla V.
  A mitad de partida se une cualquier dispositivo libre (ENTER, A o START), hasta 4.
- HUD: paneles J1 arriba izq., J2 arriba dcha., J3 abajo izq., J4 abajo dcha.; los avisos usan las teclas de cada uno.
- En pantallas y minijuegos, confirmar = atacar o saltar. Pruebas `PlayersTests` (5). Totales: EditMode 52/52, PlayMode 142/142.

## Armas elementales, afinidad de piso y tragaperras (2026-10-18)
- **Diagnóstico de "armas que se caen"**: las armas del juego no usan física de Unity (la física es `CirclePhysics`);
  el modelo de la mano es hijo de `Modelo/Brazo/Mano` (`CharacterParts.Holder`) y sigue al brazo. Los FBX de Meshy, en
  cambio, traen giro 270° y escala ×100, el material sin textura y, si se instancian tal cual en la escena (con
  colisionador/Rigidbody o en espacio global), se quedan atrás o caen. Ahora siempre pasan por `Art.Spawn` (quita
  colisionadores y Rigidbody) y cuelgan de la mano. Todos los personajes usan el mismo muñeco (`CharacterModel`), así que
  el anclaje es único; los bípedos de `Assets/_Custom/Characters` no los usa el juego todavía.
- **Agarre** (`Items/WeaponModels.cs`, `WeaponGrip.cs`, `Resources/WeaponModels.asset`): cada modelo se orienta solo
  (eje largo → +Z, cara plana arriba, mango = extremo más estrecho en el origen) y se ajusta por arma en el Inspector:
  `Length`, `HandleBack`, `FlipLength` (mango al revés), `GripOffset`, `GripEuler` (z = girar sobre el eje), `Scale`.
  En el editor el cambio se ve al momento, también en Play. `Delvefolk/Preparar armas de Meshy` (`Editor/WeaponSetup.cs`)
  crea los materiales URP con textura y normales (`Art/Armas`), activa Read/Write en los FBX y rellena el catálogo.
- **Armas** (`Items/ElementalWeapons.cs`, base `ElementalWeapon`): tocar ATACAR = básico; mantener ATACAR 0,45 s y soltar =
  especial (barra de carga sobre la cabeza, aviso en el suelo, enfriamiento en el panel del arma). Sin acciones nuevas.
  - 🌊 `mop` Trapeador: barrido amplio con salpicadura · Torbellino: daño y empuje en círculo + suelo mojado que ralentiza.
  - 🔥 `hellpan` Sartén Infernal: arco frontal metálico, interrumpe · Cocina volcánica: onda de calor + quemadura.
  - 🌿 `rake` Rastrillo: barrido estrecho largo · Ven aquí, maleza: engancha y arrastra (nunca a través de muros ni a fosos);
    ligeros inmovilizados (más tiempo en piso de planta), medianos ralentizados, pesados y jefes resisten.
  - 🍄 `parasol` Paraguas de Esporas: golpe lento con esporas leves · Lluvia de esporas: nube de veneno (más larga en piso de veneno).
  - 🌀 `vacuum` Aspiradora Cuántica: mantener = succión en cono con energía propia (ligeros rápido, medianos lento, pesados y
    jefes no; capacidad 3; sin IA mientras están dentro) · tocar = Descarga: salen disparados y hacen ¡STRIKE! a otros; sin
    nada dentro, chorro de energía. Al soltarla o cambiarla, lo de dentro sale a sus pies.
- **Efectos** (`Combat/ElementalEffects.cs`, `Game.Elemental`): quemadura/veneno renuevan sin acumularse (tope 5 s), lentitud y
  raíz con límites, jefes casi inmunes; zonas con vida y tope; todo se limpia al cambiar de piso. Enemigos: solo dos
  ganchos (`StatusMoveMult` multiplica su movimiento; `Captured` = dentro de la aspiradora); su IA y modelos no cambian.
- **Afinidad** (`World/FloorAffinity.cs`, `FloorThemeCatalog.cs`, `Resources/FloorThemes.asset`): tinte por
  `MaterialPropertyBlock` (`_Recolor`) solo en `DungeonView.Surfaces` (suelo, muros, fosos, arcos) y en lo que lleve
  `EnvironmentSurface`; detalles planos (charcos, grietas, musgo, hongos, paneles) en `TemaAfinidad_*`. No toca geometría,
  luces, salida, antorchas, jugadores, armas, enemigos ni UI. `Game.SetAffinity(Element.None)` vuelve al aspecto base.
- **Tragaperras** (`World/SlotMachine.cs`, `SlotMachineConfig.cs`, `Resources/SlotMachine.asset`): en la sala inicial de los
  pisos normales (no bonus/tienda/jefe; por defecto desde el piso 2, 75 %), contra un muro sin tapar pasos. {E} gira una vez:
  los tres resultados se eligen antes de la animación con pesos (negativos = 0; todo a 0 = no gira y avisa). Rodillo 1:
  recompensa en monedas por tipo de enemigo existente (no los cambia). Rodillo 2: mejora del equipo de siempre
  (`buff_speed`, `buff_cook`, `buff_headstrong`, `item_shield`) o curación por eliminación. Rodillo 3: afinidad del piso + su arma
  junto a la máquina. Inspector: aparición, pisos, obligatoria, tiempos, pesos, volver a girar (apagado) y forzar resultados.
  Usa un generador propio por piso: no altera el azar de la partida. Independiente de la ruleta del Crupier.
- **Solo armas nuevas en la partida**: las armas del port web (escoba, sartén, cubo, palo, tomo, pata de silla, espada,
  lanza, hacha, mazo y escopeta) ya no salen: `WeaponRegistry.Create` las sustituye (`LegacyReplacement`) en el mapa, los
  muebles, la tienda (vacuum/rake/hellpan/parasol) y el trofeo del Duende (vacuum). Escoba y cubo → trapeador, sartén →
  sartén infernal, palo y pata → rastrillo, tomo → paraguas. La comida sigue igual. Su código sigue dentro (sin usarse)
  porque las pruebas del original lo comprueban con `WeaponRegistry.LegacyWeapons = true`.
- Pruebas: `SlotMachineLogicTests` (5, EditMode) y `ElementalTests` (13, PlayMode). Las antiguas apagan la tragaperras en
  `GameTestBase` (`Game.SlotMachinesEnabled`).
- **Pendiente de asignar**: modelo del **trapeador** (no hay FBX; usa uno hecho por código): pon su FBX en
  `WeaponModels.asset → mop → Prefab` y repite `Preparar armas de Meshy`. Sin sonidos ni animaciones nuevas (se reutilizan
  los sintetizados y las poses por código). "Ataque rápido", "esquiva" y "proyectiles rebotadores" no están en la tragaperras.

## Personajes personalizados (2026-10-18)
- **Modelos**: 8 personajes de Meshy en `Assets/_Custom/Characters/*/..._biped` (los `*_texture_fbx` son la misma figura sin
  esqueleto y no se usan). Todos con rig **Generic** y Avatar propio ("Create From This Model"): esqueleto Mixamo
  (`mixamorig:*`) pero distinto en cada uno (54–93 huesos, dedos diferentes). Solo traen la animación de **correr** (0,67 s,
  en el sitio); se ha puesto en bucle. Cada FBX trae además una esfera suelta ("Icosphere") que se oculta al instanciar.
- `Delvefolk/Preparar personajes de Meshy` (`Editor/HeroSetup.cs`): material URP con textura y normales (`Art/Personajes`),
  Generic + Avatar, bucle del clip y `Resources/Heroes.asset` (rellena los huesos del brazo y la mano de cada esqueleto,
  comprobándolos). Repetible: conserva los ajustes.
- **Selección**: nueva fila **Personaje** en la sala (tras Habilidad): Aventurero clásico + los 8. Se guarda con el resto de
  la elección (`PlayerLoadout.Hero`) y la vista previa monta el personaje. Las habilidades no cambian (son independientes); los
  cosméticos solo se aplican al clásico y la armadura de recompensa no se dibuja sobre los personajes (está hecha a su medida).
- **Esqueleto en partida** (`Player/HeroModel.cs`, `HeroRig.cs`, `HeroCatalog.cs`): `CharacterModel.Build(model, cosméticos, hero)`
  devuelve las mismas `CharacterParts`. `HeroRig` (LateUpdate): reproduce el correr según la velocidad (quieto: el fotograma
  más neutro), y después coloca el brazo del arma con **IK de dos huesos** hacia la pose del juego (`ArmPivot`), así los
  ataques y especiales usan las mismas poses que el clásico. El **socket** (`SocketArma`) es hijo del hueso de la mano
  (`mixamorig:RightHand` en los 8) y el arma cuelga de él; los huesos se resuelven una vez al montar (error claro si faltan).
- **Ajustes en el Inspector** (`Resources/Heroes.asset`, por personaje): `Hand` (derecha/izquierda: la izquierda refleja la
  pose), `UpperArmBone`/`ForeArmBone`/`HandBone`, `SocketOffset`/`SocketEuler`/`SocketScale`, `WeaponOffsets` (ajuste por arma),
  `Scale`, `Yaw` y `RunSpeedRef`. Se suma al agarre general de cada arma (`WeaponModels.asset`).
- Pruebas `HeroTests` (4): catálogo/Avatar/clip, cada personaje con cada arma (socket en su mano, sin física, corre y ataca
  sin soltarla), cambiar de piso sin duplicados y la fila de la sala. `PresentationTests` adaptada a la nueva fila.
  Totales: EditMode 58/58, PlayMode 159/159.
- **Pendiente**: animaciones de reposo, ataque, golpe y muerte para los personajes (solo hay correr; los ataques se hacen con
  el brazo por IK y el reposo es un fotograma del correr). Revisar a ojo el agarre de cada combinación personaje/arma
  y retocar `WeaponOffsets` si alguna no convence.

## Escala, selección, tragaperras en pantalla e Idle (2026-10-18)
- **Escala**: los FBX de Meshy vienen en metros reales (1,63–1,88 m, cada uno distinto) y se ponían 1:1 en un mundo de
  casillas de 2 u y figuras rechonchas vistas desde lejos: se veían pequeños y desiguales. Ahora `HeroModel` mide la malla
  (horneada en la pose de reposo, una vez por modelo) y la lleva a `HeroCatalog.TargetHeight` = **2,3** (un único valor, en
  `Resources/Heroes.asset`; `HeroEntry.Scale` queda como ajuste fino a 1). Pies a ras de suelo, estrellas y caparazón a su
  altura. La colisión (círculo de radio 0,42) no cambia. Medido: los 8 miden 2,30.
- **Selección**: la sala solo ofrece los 8 personajes personalizados (el clásico solo existe para las pruebas antiguas:
  `HeroCatalog.ClassicAllowed`). Cada hueco enseña solo **modelo** (vista previa más grande y cercana), **nombre** (◀ ▶) y
  **habilidad** (icono, nombre y una línea), más «Listo». La habilidad es la del personaje (`HeroEntry.Ability`: Azur
  terremoto, Alquimista explosión, Forajido imán, Sombra intercambio, Rosaviento y Pícara Carmesí carga, Elfa Rosa grito,
  Doncella último recurso); en modo exclusivo se saltan los personajes de habilidad ocupada. Cada jugador empieza con uno distinto.
- **Tragaperras en pantalla** (`World/SlotOverlay.cs`, `UI/HudSlot.cs`): al girar se abre un panel con los tres rodillos
  (Enemigos, Habilidad, Afinidad) que giran y frenan con *ease-out* hasta caer en el resultado ya elegido (no se vuelve a
  sortear). Mientras, la simulación espera (como las presentaciones de bonus); la animación avanza también en pausa. Al
  pararse se aplican los resultados (una vez) y se explican; se cierra sola (`RevealPause`) o al confirmar. Una sola tirada a
  la vez; el cierre (con `try/finally`) siempre oculta el panel y limpia la entrada. El juego no usa `Time.timeScale`.
- **Idle**: `Male/Meshy_AI_Azure_Wayfarer_biped/Meshy_AI_Azure_Wayfarer_biped_Animation_Idle_11_frame_rate_30.fbx`, clip
  "target_character|…" de 1,87 s, rig Generic, en un FBX aparte (trae su malla). Puesto en bucle. El Viajero Azur lo usa tal
  cual. `Delvefolk/Integrar Idle en los personajes` (`Editor/HeroSetup.cs`) hornea para los demás una copia por esqueleto
  (`Art/Personajes/Idle_<id>.anim`): misma rotación relativa a la pose de la **piel** (bindposes; la pose por defecto de los
  FBX de Meshy es un fotograma cualquiera) y altura de cadera proporcional; nunca copia posiciones ni escalas de otros
  huesos. Se valida comparando brazos, piernas, columna y cabeza fotograma a fotograma (media ≤ 20°, pico ≤ 30°).
  Compatibles: Doncella Élfica, Pícara Carmesí, Elfa Rosa, Sombra Carmesí, Alquimista de Ascuas, Forajido Emplumado (+ Azur).
  **Incompatible: Pícara Rosaviento** (pico de 41°: un brazo quedaba en cruz) → sigue con su fotograma neutro del correr.
  `HeroRig` mezcla reposo ↔ correr con un `AnimationMixerPlayable` según la velocidad (no había Animator Controller: el
  juego anima por código; no se crean controladores).
- Pruebas: `HeroTests` (+Escala, +Idle), `ElementalTests` (+tragaperras con pausa), `PresentationTests` y
  `PresentationLogicTests` adaptadas. Totales: EditMode 59/59, PlayMode 162/162.

## Nuevo modelo de El Crupier (2026-10-18)
- **Modelo**: `Assets/_Custom/jefeModelo/Meshy_AI_Crimson_Aristocrat_biped/Meshy_AI_Crimson_Aristocrat_biped_Animation_all_frame_rate_30.fbx`
  (el `*_texture_fbx` es la misma malla sin esqueleto). Rig Generic, 66 huesos Mixamo, 1,62 m, clips **Walking** (0,83 s) y
  **Running** (0,67 s); **sin Idle**. Traía la misma esfera suelta que los personajes (se oculta).
- `Delvefolk/Preparar modelo del Crupier` (`Editor/BossModelSetup.cs`): material URP con su textura
  (`Art/JefeFinal/Crupier_Crimson_Aristocrat.mat`), Generic + Avatar propio, clips con nombre y en bucle, **crea el Idle**
  (`Art/JefeFinal/Crupier_Idle.anim`, 3,2 s en bucle para su esqueleto: pose de pie = pose de la piel con los brazos bajados,
  respiración, leve vaivén de cadera, cabeza que mira a los lados y hombros/brazos que se mecen; giro máx. 5,7°, cadera
  ±0,6 cm; se comprueba antes de guardarlo) y lo asigna en `Resources/CrupierRoulette.asset` (`CrupierModel`, `CrupierIdle`,
  `CrupierWalk`, `CrupierRun`; `CrupierHeight` 2,6 sin cambiar). **Revertir**: vaciar `CrupierModel` (vuelve el placeholder).
- **Solo visual** (`Bosses/Crupier/CrupierBody.cs`): cuelga del `Modelo` del jefe (la raíz, el círculo de colisión, la IA,
  los patrones, el daño y los puntos de salida de cartas/fichas/rastrillo no se tocan). Mezcla reposo ↔ andar ↔ correr con
  Playables según la velocidad que ya tenía el jefe; sin root motion. Durante los ataques sigue con su animación de base
  (no hay clips de ataque): el reposo no interrumpe nada y los avisos de siempre (suelo, ruleta) siguen. La ilusión
  (Morado) usa el mismo cuerpo. Se probó un gesto de brazos copiado del placeholder y se quitó (en este rig parecía una T).
- Pruebas `CrupierModelTests` (2): modelo nuevo visible y firma de combate (radio, masa, vida, velocidad, daño, patrones)
  idéntica a la del placeholder; reposo/andar/reposo sin desplazar la raíz, altura y pies en el suelo. `CrupierTests` (14)
  siguen en verde con el modelo. Totales: EditMode 59/59, PlayMode 164/164.

## Pendiente
- **Fase 9** (pulido, rendimiento, build): la hace el usuario a mano.
- Detalles visuales menores conocidos: el rótulo 😋 de la viñeta 4 del banquete queda fuera de cuadro (igual en el
  original); las mallas de las viñetas no se liberan (caché por aspecto, fuga acotada).

## Convenciones
- Coordenadas: web (x, z) → Unity (x, −z); ángulo web θ → yaw Unity π − θ (objetos planos asimétricos: −θ).
- Brazo: `ProcMesh.EulerYXZ` = orden 'YXZ' del web (`Cosmetics.js`).
- Texto al jugador y comentarios en español; identificadores en inglés.
- Commits: título en español + lista con "+", sin línea Co-Authored-By.
- Pruebas: `GameTestBase` (AutoRun off, semilla 1234, transiciones instantáneas).
