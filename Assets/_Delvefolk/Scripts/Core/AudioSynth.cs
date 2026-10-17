using UnityEngine;

namespace Delvefolk
{
    // Sonidos sintetizados (AudioManager.js). La síntesis completa llega en la fase de presentación;
    // de momento solo existe la interfaz para que el resto del juego pueda pedir sonidos.
    public sealed partial class AudioSynth
    {
        public bool Muted { get; private set; }

        public bool ToggleMute()
        {
            Muted = !Muted;
            AudioListener.volume = Muted ? 0f : 1f;
            return Muted;
        }

        public void Play(string id, float volume = 1f) { OnPlay(id, volume); }
        partial void OnPlay(string id, float volume);
    }
}
