using UnityEngine;

namespace Delvefolk
{
    // Único MonoBehaviour de la partida: vive en el objeto Bootstrap de la escena y avanza la simulación.
    // Las entidades son clases C# normales; nadie más decide jugabilidad en un Update().
    // Pruebas: GameLoop.AutoRun = false y Step(1f / 60f) manual, con la entrada inyectada en Game.Input.
    [DefaultExecutionOrder(-100)]
    public sealed class GameLoop : MonoBehaviour
    {
        public static GameLoop Instance { get; private set; }
        // false en las pruebas: no se avanza solo ni se leen los dispositivos reales
        public static bool AutoRun = true;
        // Semilla de la aleatoriedad de juego (0 = según la hora)
        public static uint SeedOverride;

        public Game Game { get; private set; }

        void Awake()
        {
            Instance = this;
            uint seed = SeedOverride != 0 ? SeedOverride : (uint)System.Environment.TickCount;
            Game = new Game(this, seed);
            Game.Input.ReadDevices = AutoRun;
        }

        void Update()
        {
            if (Game == null) return;
            Game.Input.ReadDevices = AutoRun;
            // Fundido entre escenas: el único temporizador en tiempo real
            Game.UpdateRealTime(Time.unscaledDeltaTime);
            if (AutoRun) Game.Frame(Mathf.Min(Time.deltaTime, 0.05f));
            Game.LateVisuals(Time.unscaledDeltaTime);
        }

        // Avance manual (pruebas): un frame de simulación con el dt indicado
        public void Step(float dt)
        {
            Game.Frame(dt);
        }

        public void Steps(int n, float dt = 1f / 60f)
        {
            for (int i = 0; i < n; i++) Game.Frame(dt);
        }

        void OnApplicationFocus(bool focus)
        {
            // Al perder el foco se sueltan todas las teclas: evita movimiento "fantasma"
            if (!focus && Game != null) Game.Input.Clear();
        }

        void OnDestroy()
        {
            if (Game != null) Game.Dispose();
            if (Instance == this) Instance = null;
        }
    }
}
