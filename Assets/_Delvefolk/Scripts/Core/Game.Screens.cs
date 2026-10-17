using UnityEngine;
using UnityEngine.Rendering;

namespace Delvefolk
{
    // Título → sala de preparación → partida, y la pantalla de pausa (Game.js + main.js)
    public sealed partial class Game
    {
        public LobbyScreen Lobby { get; private set; }
        static readonly string[] TitleOpen1 = { "Space", "KeyX" };
        static readonly string[] TitleOpen2 = { "Enter", "NumpadEnter" };

        partial void InitMoreSystems()
        {
            Lobby = new LobbyScreen(this);
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
            InitLaterSystems();
        }

        partial void InitLaterSystems();

        // Al arrancar: solo la pantalla de título sobre la mazmorra de fondo
        partial void OnTitleShown()
        {
            Ui.ShowHud(false);
            Ui.ShowTitle(true);
        }

        partial void DisposeScreens()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            if (Lobby != null) Lobby.Dispose();
        }

        // Título → sala de preparación (elegir habilidad y aspecto)
        public void OpenLobby(int numPlayers = 1) { OpenLobby(numPlayers, DeviceBinding.KeyA); }

        // j1: dispositivo del J1 (teclado izquierdo o el mando con el que se pulsó)
        public void OpenLobby(int numPlayers, DeviceBinding j1)
        {
            if (State != GameState.Title) return;
            Bindings[0] = j1;
            if (numPlayers > 1) Bindings[1] = j1.Equals(DeviceBinding.KeyB) ? DeviceBinding.KeyA : DeviceBinding.KeyB;
            Ui.ShowTitle(false);
            Input.Clear();
            State = GameState.Lobby;
            Ui.HideHeads();
            Lobby.Open(numPlayers);
        }

        public void BackToTitle()
        {
            Lobby.Close();
            State = GameState.Title;
            Ui.HideHeads();
            Input.Clear();
            Ui.ShowTitle(true);
        }

        // Espacio / A del mando 1 → sala con 1 jugador; Enter / A del mando 2 → sala con 2
        partial void TitleInput(ref bool handled)
        {
            if (Input.ConsumeAny(TitleOpen1)) { OpenLobby(1); handled = true; return; }
            // A o START en un mando: el J1 juega con ese mando
            for (int n = 0; n < ControlSchemes.MaxPads; n++)
                if (Input.ConsumeAny(ControlSchemes.Pad(n).Join)) { OpenLobby(1, DeviceBinding.PadSlot(n)); handled = true; return; }
            else if (Input.ConsumeAny(TitleOpen2)) { OpenLobby(2); handled = true; }
        }

        partial void LobbyUpdate(float dt) { Lobby.Update(dt); }

        partial void OnPauseChanged(bool paused) { Ui.ShowPause(paused); }

        partial void OnRunStart()
        {
            Ui.ShowTitle(false);
            if (Lobby != null && Lobby.IsOpen) Lobby.Close();
            Ui.ShowHud(true);
            OnRunStartMore();
        }

        partial void OnRunStartMore();

        // La vista previa de la sala se pinta con su propia luz y su ambiente (sin la luna del mapa)
        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            var pv = Lobby != null ? Lobby.PreviewFor(cam) : null;
            if (pv == null) return;
            if (moon) moon.enabled = false;
            pv.Key.enabled = true;
            Mats.SetAmbient(MathUtil.Hex(0x8a80a0), 0.7f, MathUtil.Hex(0x8090b0), MathUtil.Hex(0x2a1d14), 0.4f);
        }

        void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
        {
            var pv = Lobby != null ? Lobby.PreviewFor(cam) : null;
            if (pv == null) return;
            pv.Key.enabled = false;
            if (moon) moon.enabled = true;
            Lighting.ApplyAmbient();
        }
    }
}
