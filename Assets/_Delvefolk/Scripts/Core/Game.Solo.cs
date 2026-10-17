using UnityEngine;

namespace Delvefolk
{
    // Jugar solo: la mazmorra aprieta más (enemigos y jefes con más vida, golpes más fuertes) y una niebla
    // gris y densa se cierra alrededor del jugador para dar sensación de soledad. Si se une el J2, se va.
    public static class SoloConfig
    {
        public static bool Enabled = true;            // las pruebas lo apagan para no cambiar los números
        public const float EnemyHpMult = 1.4f;
        public const float BossHpMult = 1.3f;
        public const float DamageTakenMult = 1.35f;
        public const int MistColor = 0x23252e;        // gris azulado de la niebla
        public const float MistTint = 0.6f;           // cuánto tiñe la niebla el color del piso
        public const float FogNearOffset = -4f;       // respecto a la distancia de la cámara al jugador
        public const float FogFarOffset = 8f;
    }

    public sealed partial class Game
    {
        public int LevelFogColor = 0x050407;

        // ¿Partida en solitario (con la dificultad y la niebla del modo solo)?
        public bool SoloHard { get { return SoloConfig.Enabled && Players.Count == 1 && State != GameState.Title && State != GameState.Lobby; } }

        // Niebla de cada frame: normal, o densa y gris pegada al jugador si va solo
        void UpdateFog()
        {
            var cam = CameraCtl;
            if (SoloHard)
            {
                float d = cam.Distance + cam.Extra;
                RenderSettings.fogStartDistance = d + SoloConfig.FogNearOffset;
                RenderSettings.fogEndDistance = d + SoloConfig.FogFarOffset;
                var c = Color.Lerp(MathUtil.Hex(LevelFogColor), MathUtil.Hex(SoloConfig.MistColor), SoloConfig.MistTint);
                RenderSettings.fogColor = c;
                if (cam.Cam) cam.Cam.backgroundColor = c;
            }
            else
            {
                RenderSettings.fogStartDistance = 24f + cam.Extra;
                RenderSettings.fogEndDistance = 46f + cam.Extra * 1.2f;
                var c = MathUtil.Hex(LevelFogColor);
                RenderSettings.fogColor = c;
                if (cam.Cam) cam.Cam.backgroundColor = c;
            }
        }

        void AnnounceSolo()
        {
            if (!SoloHard) return;
            Ui.Toast("Solo ante la mazmorra: la niebla no deja ver y todo pega más fuerte.", 4.5f);
        }
    }
}
