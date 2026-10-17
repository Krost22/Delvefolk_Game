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

El juego es jugable de principio a fin: título → lobby → pisos → pisos bonus → siguiente piso.

## Hecho (fase 7)
- Tienda tras cada bonus (`Scripts/Shop/ShopFloor.cs`, `Core/Game.Shop.cs`), Duende Tendero (`Entities/ShopKeeper.cs`),
  armas especiales y escopeta (`Items/SpecialWeapons.cs`), barra de jefe del HUD (`UI/HudBoss.cs`).
- Pruebas `ShopTests` (5). Totales: EditMode 47/47, PlayMode 78/78.

## Pendiente
- **Fase 8 jefes**: `Game.Bonus.cs` ya llama a `InitShopAndBosses`, `BossSystemsUpdate`, `BossDescend` (parciales vacíos/no
  implementados). Falta Boss, BossFloor, Golem, Twins, Chef, Bell, telegrafías, oleadas, proyectiles.
- **Fase 9** (pulido, rendimiento, build): la hace el usuario a mano.
- Pregunta §11 abierta: el rodillo de Último Aliento dice "sáltalo con {J}" pero el original nunca comprueba el salto.
  Se mantiene el comportamiento original; propuesta: permitir esquivar con `JumpY > 0.51`.
- Detalles visuales menores conocidos: el rótulo 😋 de la viñeta 4 del banquete queda fuera de cuadro (igual en el
  original); las mallas de las viñetas no se liberan (caché por aspecto, fuga acotada).

## Convenciones
- Coordenadas: web (x, z) → Unity (x, −z); ángulo web θ → yaw Unity π − θ (objetos planos asimétricos: −θ).
- Brazo: `ProcMesh.EulerYXZ` = orden 'YXZ' del web (`Cosmetics.js`).
- Texto al jugador y comentarios en español; identificadores en inglés.
- Commits: título en español + lista con "+", sin línea Co-Authored-By.
- Pruebas: `GameTestBase` (AutoRun off, semilla 1234, transiciones instantáneas).
