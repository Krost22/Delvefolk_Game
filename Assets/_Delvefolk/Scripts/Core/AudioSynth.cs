using System;
using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Sonidos sintetizados (AudioManager.js), sin archivos: cada receta se renderiza una vez a PCM
    // (osciladores con barrido exponencial de frecuencia, ruido blanco con filtro biquad que también barre,
    // envolventes exponenciales como las rampas de WebAudio) y un mezclador propio en el AudioListener
    // reproduce las voces con la ganancia maestra 0.55 y el compresor del original (umbral −14 dB, ratio 4).
    public sealed class AudioSynth
    {
        public const float MasterGain = 0.55f;
        public bool Muted { get; private set; }
        // Últimos sonidos pedidos (pruebas)
        public string LastPlayed { get; private set; }
        public int PlayedCount { get; private set; }

        readonly Dictionary<string, double> last = new Dictionary<string, double>();
        readonly Rng rand = new Rng(0x5eed1234u);   // variación de tono: no toca la aleatoriedad del juego
        SynthMixer mixer;

        // Las recetas que usan la variación de tono "r" del original
        static readonly HashSet<string> Pitched = new HashSet<string>
        {
            "swing", "hit", "clang", "bonk", "thwack", "slimeHurt", "goblinHurt", "eat", "shout", "jump", "stomp",
            "struggle", "ogreGrowl", "ogreHurt", "squeeze", "coin", "bossRoar", "mineReveal",
        };

        public static readonly string[] Ids =
        {
            "swing", "hit", "clang", "bonk", "thwack", "crash", "hurt", "slimeHurt", "goblinHurt", "slimeDie", "goblinDie",
            "pickup", "heal", "drop", "spikeArm", "spikes", "explosion", "barrelBreak", "alert", "enemyAttack", "fall", "gate",
            "descend", "death", "break", "eat", "grab", "throw", "join", "step", "quake", "charge", "shout", "swap", "magnet",
            "guard", "abilityReady", "denied", "jump", "stomp", "struggle", "breakFree", "ogreGrowl", "ogreHurt", "ogreDie",
            "squeeze", "coin", "shotgun", "reload", "bossRoar", "bonus", "mineReveal", "mineFlag", "chest", "curse", "crumble",
            "tick", "win", "lose",
        };

        // El mezclador vive junto al AudioListener (recibe la mezcla final en OnAudioFilterRead)
        public void Attach(GameObject listener)
        {
            if (mixer) return;
            mixer = listener.AddComponent<SynthMixer>();
            mixer.Init(MasterGain);
            mixer.Muted = Muted;
        }

        // Sintetiza de antemano todas las recetas (al arrancar; la caché es compartida)
        public static void Prewarm() { foreach (var id in Ids) Render(id); }

        public bool ToggleMute()
        {
            Muted = !Muted;
            if (mixer) mixer.Muted = Muted;
            return Muted;
        }

        // p: parámetro de la receta (solo el "swing" lo usa, para alargar el silbido de las armas grandes)
        public void Play(string id, float p = 1f)
        {
            if (Muted || string.IsNullOrEmpty(id)) return;
            // Evita saturar con el mismo sonido en el mismo instante
            double now = Time.realtimeSinceStartupAsDouble;
            double t;
            if (last.TryGetValue(id, out t) && now - t < 0.035) return;
            last[id] = now;
            float r = Pitched.Contains(id) ? 0.92f + rand.Next() * 0.16f : 1f;
            var pcm = Render(id, p);
            if (pcm == null) return;
            LastPlayed = id;
            PlayedCount++;
            if (mixer) mixer.Add(pcm, r);
        }

        // ---------- Renderizado de recetas (hilo principal; caché compartida) ----------
        static readonly Dictionary<string, float[]> cache = new Dictionary<string, float[]>();
        static int rate;

        public static int SampleRate
        {
            get
            {
                if (rate <= 0) { rate = AudioSettings.outputSampleRate; if (rate <= 0) rate = 48000; }
                return rate;
            }
        }

        public static float[] Render(string id, float p = 1f)
        {
            string key = id;
            if (id == "swing") { p = Mathf.Round(p * 20f) / 20f; key = "swing@" + p.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture); }
            float[] pcm;
            if (cache.TryGetValue(key, out pcm)) return pcm;
            var b = new Builder(SampleRate, Fnv(key));
            if (!Recipe(id, p, b)) return null;
            pcm = b.ToArray();
            cache[key] = pcm;
            return pcm;
        }

        // Semilla estable por receta (el ruido sale igual en cada arranque)
        static uint Fnv(string s)
        {
            uint h = 2166136261u;
            foreach (char c in s) { h ^= c; h *= 16777619u; }
            return h;
        }

        enum Wave { Sine, Square, Triangle, Sawtooth }
        enum Filter { Lowpass, Highpass, Bandpass }

        // Las recetas de AudioManager.play con r = 1 (la variación se aplica al reproducir)
        static bool Recipe(string name, float p, Builder b)
        {
            switch (name)
            {
                case "swing":
                    b.Noise(0.16f + 0.08f * p, 600, slide: 2600, type: Filter.Bandpass, q: 1.2f, vol: 0.18f);
                    break;
                case "hit":
                    b.Tone(140, 0.14f, Wave.Triangle, 0.5f, slide: 60);
                    b.Noise(0.09f, 2200, vol: 0.35f);
                    break;
                case "clang": // sartén
                    b.Tone(170, 0.25f, Wave.Square, 0.25f, slide: 70);
                    {
                        int[] fs = { 523, 1046, 1568, 2093 };
                        for (int i = 0; i < fs.Length; i++) b.Tone(fs[i] * 1.07f, 0.7f - i * 0.12f, Wave.Sine, 0.18f / (i + 1));
                    }
                    b.Noise(0.12f, 4000, type: Filter.Highpass, vol: 0.25f);
                    break;
                case "bonk": // cubo
                    b.Tone(320, 0.18f, Wave.Triangle, 0.45f, slide: 180);
                    b.Tone(640, 0.3f, Wave.Sine, 0.12f);
                    break;
                case "thwack": // palo / escoba
                    b.Noise(0.07f, 1500, type: Filter.Bandpass, q: 2, vol: 0.45f);
                    b.Tone(220, 0.08f, Wave.Triangle, 0.3f, slide: 120);
                    break;
                case "crash": // contra la pared
                    b.Noise(0.25f, 500, slide: 120, vol: 0.55f);
                    b.Tone(90, 0.2f, Wave.Sine, 0.5f, slide: 40);
                    break;
                case "hurt":
                    b.Tone(420, 0.22f, Wave.Square, 0.18f, slide: 160);
                    b.Noise(0.12f, 900, vol: 0.25f);
                    break;
                case "slimeHurt":
                    b.Tone(300, 0.15f, Wave.Sine, 0.3f, slide: 520);
                    break;
                case "goblinHurt":
                    b.Tone(700, 0.12f, Wave.Sawtooth, 0.12f, slide: 950);
                    break;
                case "slimeDie":
                    b.Tone(500, 0.35f, Wave.Sine, 0.35f, slide: 80);
                    b.Noise(0.3f, 700, slide: 150, vol: 0.3f);
                    break;
                case "goblinDie":
                    b.Tone(900, 0.4f, Wave.Sawtooth, 0.12f, slide: 200);
                    b.Tone(600, 0.3f, Wave.Square, 0.08f, slide: 90, delay: 0.08f);
                    break;
                case "pickup":
                    Arp(b, new[] { 523, 659, 784, 1046 }, 0.12f, Wave.Triangle, 0.2f, 0.05f);
                    break;
                case "heal":
                    Arp(b, new[] { 392, 523, 659 }, 0.18f, Wave.Sine, 0.25f, 0.07f);
                    break;
                case "drop":
                    b.Tone(200, 0.12f, Wave.Triangle, 0.25f, slide: 120);
                    break;
                case "spikeArm":
                    b.Tone(1200, 0.04f, Wave.Square, 0.08f);
                    break;
                case "spikes":
                    b.Noise(0.18f, 3500, type: Filter.Highpass, vol: 0.35f);
                    b.Tone(900, 0.1f, Wave.Sawtooth, 0.12f, slide: 300);
                    break;
                case "explosion":
                    b.Noise(0.9f, 900, slide: 60, vol: 0.9f);
                    b.Tone(70, 0.6f, Wave.Sine, 0.7f, slide: 30);
                    break;
                case "barrelBreak":
                    b.Noise(0.2f, 1300, type: Filter.Bandpass, q: 0.8f, vol: 0.5f);
                    b.Tone(160, 0.15f, Wave.Triangle, 0.3f, slide: 90);
                    break;
                case "alert":
                    b.Tone(880, 0.08f, Wave.Square, 0.1f);
                    b.Tone(1320, 0.1f, Wave.Square, 0.1f, delay: 0.08f);
                    break;
                case "enemyAttack":
                    b.Noise(0.12f, 900, slide: 2000, type: Filter.Bandpass, vol: 0.18f);
                    break;
                case "fall":
                    b.Tone(600, 0.8f, Wave.Sine, 0.25f, slide: 60);
                    break;
                case "gate":
                    b.Noise(0.8f, 300, type: Filter.Lowpass, vol: 0.4f);
                    for (int i = 0; i < 6; i++) b.Tone(140 + i * 8, 0.06f, Wave.Square, 0.08f, delay: i * 0.11f);
                    break;
                case "descend":
                    Arp(b, new[] { 784, 659, 523, 392, 262 }, 0.25f, Wave.Triangle, 0.2f, 0.09f);
                    break;
                case "death":
                    Arp(b, new[] { 392, 330, 262, 196 }, 0.35f, Wave.Square, 0.12f, 0.18f);
                    break;
                case "break": // arma rota
                    b.Noise(0.25f, 1800, type: Filter.Bandpass, q: 0.7f, vol: 0.6f);
                    b.Tone(300, 0.2f, Wave.Square, 0.15f, slide: 80);
                    b.Tone(900, 0.1f, Wave.Triangle, 0.15f, slide: 400, delay: 0.05f);
                    break;
                case "eat": // ñam
                    b.Noise(0.06f, 700, type: Filter.Bandpass, q: 3, vol: 0.35f);
                    b.Tone(180, 0.06f, Wave.Triangle, 0.15f);
                    break;
                case "grab":
                    b.Tone(260, 0.12f, Wave.Triangle, 0.3f, slide: 520);
                    break;
                case "throw":
                    b.Noise(0.3f, 400, slide: 3000, type: Filter.Bandpass, q: 1, vol: 0.3f);
                    b.Tone(500, 0.3f, Wave.Sine, 0.15f, slide: 1200);
                    break;
                case "join":
                    Arp(b, new[] { 392, 523, 784 }, 0.15f, Wave.Square, 0.12f, 0.07f);
                    break;
                case "step":
                    b.Noise(0.04f, 400, vol: 0.05f);
                    break;
                // ---- Habilidades especiales ----
                case "quake":
                    b.Noise(0.6f, 300, slide: 50, vol: 0.8f);
                    b.Tone(55, 0.5f, Wave.Sine, 0.7f, slide: 30);
                    break;
                case "charge":
                    b.Tone(110, 0.35f, Wave.Sawtooth, 0.15f, slide: 70);
                    b.Noise(0.4f, 600, slide: 200, vol: 0.3f);
                    break;
                case "shout":
                    b.Tone(220, 0.5f, Wave.Sawtooth, 0.2f, slide: 180);
                    b.Tone(330, 0.5f, Wave.Square, 0.1f, slide: 260);
                    b.Noise(0.5f, 1200, type: Filter.Bandpass, q: 0.8f, vol: 0.25f);
                    break;
                case "swap":
                    b.Tone(400, 0.18f, Wave.Sine, 0.25f, slide: 1400);
                    b.Tone(1400, 0.18f, Wave.Sine, 0.2f, slide: 400, delay: 0.12f);
                    break;
                case "magnet":
                    b.Tone(90, 0.8f, Wave.Square, 0.08f, slide: 180);
                    b.Tone(180, 0.8f, Wave.Sine, 0.15f, slide: 360);
                    break;
                case "guard":
                    Arp(b, new[] { 262, 330, 392 }, 0.3f, Wave.Triangle, 0.2f, 0.04f);
                    break;
                case "abilityReady":
                    b.Tone(880, 0.08f, Wave.Triangle, 0.12f);
                    b.Tone(1320, 0.12f, Wave.Triangle, 0.1f, delay: 0.06f);
                    break;
                case "denied":
                    b.Tone(160, 0.12f, Wave.Square, 0.1f);
                    break;
                // ---- Salto, forcejeo y Abrazón ----
                case "jump":
                    b.Tone(300, 0.12f, Wave.Triangle, 0.18f, slide: 620);
                    break;
                case "stomp":
                    b.Tone(200, 0.12f, Wave.Square, 0.18f, slide: 90);
                    b.Noise(0.08f, 900, vol: 0.35f);
                    break;
                case "struggle":
                    b.Noise(0.05f, 1100, type: Filter.Bandpass, q: 2, vol: 0.25f);
                    break;
                case "breakFree":
                    Arp(b, new[] { 440, 660, 880 }, 0.1f, Wave.Triangle, 0.15f, 0.05f);
                    break;
                case "ogreGrowl":
                    b.Tone(70, 0.6f, Wave.Sawtooth, 0.2f, slide: 55);
                    b.Noise(0.5f, 300, vol: 0.2f);
                    break;
                case "ogreHurt":
                    b.Tone(110, 0.2f, Wave.Sawtooth, 0.18f, slide: 80);
                    break;
                case "ogreDie":
                    b.Tone(90, 0.9f, Wave.Sawtooth, 0.2f, slide: 35);
                    b.Noise(0.6f, 400, slide: 100, vol: 0.3f);
                    break;
                case "squeeze":
                    b.Tone(500, 0.15f, Wave.Square, 0.1f, slide: 700);
                    break;
                // ---- Monedas, tienda y escopeta ----
                case "coin":
                    b.Tone(1320, 0.06f, Wave.Square, 0.06f);
                    b.Tone(1760, 0.1f, Wave.Triangle, 0.08f, delay: 0.04f);
                    break;
                case "shotgun":
                    b.Noise(0.35f, 1800, slide: 200, vol: 0.9f);
                    b.Tone(90, 0.25f, Wave.Square, 0.3f, slide: 40);
                    break;
                case "reload":
                    b.Noise(0.04f, 2500, type: Filter.Bandpass, q: 4, vol: 0.3f);
                    b.Noise(0.05f, 1600, type: Filter.Bandpass, q: 4, vol: 0.3f, delay: 0.12f);
                    break;
                case "bossRoar":
                    b.Tone(140, 0.6f, Wave.Sawtooth, 0.22f, slide: 70);
                    b.Tone(210, 0.5f, Wave.Square, 0.1f, slide: 100);
                    b.Noise(0.5f, 600, slide: 200, vol: 0.3f);
                    break;
                // ---- Pisos bonus ----
                case "bonus":
                    Arp(b, new[] { 523, 659, 784, 1046, 1318 }, 0.2f, Wave.Triangle, 0.18f, 0.08f);
                    break;
                case "mineReveal":
                    b.Noise(0.05f, 2500, type: Filter.Bandpass, q: 3, vol: 0.25f);
                    b.Tone(600, 0.06f, Wave.Triangle, 0.12f);
                    break;
                case "mineFlag":
                    b.Tone(700, 0.06f, Wave.Square, 0.08f);
                    b.Tone(1000, 0.08f, Wave.Square, 0.08f, delay: 0.05f);
                    break;
                case "chest":
                    b.Tone(200, 0.3f, Wave.Triangle, 0.3f, slide: 120);
                    b.Noise(0.25f, 900, type: Filter.Bandpass, vol: 0.3f);
                    break;
                case "curse":
                    Arp(b, new[] { 330, 311, 294, 277 }, 0.25f, Wave.Sawtooth, 0.1f, 0.12f);
                    break;
                case "crumble":
                    b.Noise(0.5f, 700, slide: 100, vol: 0.5f);
                    break;
                case "tick":
                    b.Tone(1600, 0.03f, Wave.Square, 0.06f);
                    break;
                case "win":
                    Arp(b, new[] { 523, 659, 784, 1046 }, 0.3f, Wave.Square, 0.12f, 0.12f);
                    break;
                case "lose":
                    Arp(b, new[] { 392, 349, 311, 262 }, 0.35f, Wave.Triangle, 0.2f, 0.16f);
                    break;
                default:
                    return false;
            }
            return true;
        }

        // [f0, f1...].forEach((f, i) => tone(f, dur, { type, vol, delay: i * step }))
        static void Arp(Builder b, int[] fs, float dur, Wave w, float vol, float step)
        {
            for (int i = 0; i < fs.Length; i++) b.Tone(fs[i], dur, w, vol, delay: i * step);
        }

        // Acumula capas (tonos y ruidos) en un búfer, con la semántica de las rampas de WebAudio
        sealed class Builder
        {
            readonly int sr;
            float[] buf = new float[4096];
            int length;
            uint noiseSeed;

            public Builder(int sampleRate, uint seed) { sr = sampleRate; noiseSeed = seed | 1u; }

            void Ensure(int n)
            {
                if (buf.Length < n) Array.Resize(ref buf, Math.Max(n, buf.Length * 2));
                if (length < n) length = n;
            }

            public float[] ToArray()
            {
                var r = new float[length];
                Array.Copy(buf, r, length);
                return r;
            }

            // tone(freq, dur, { type, vol, slide, delay, attack }): rampa exponencial 0.0001 → vol en "attack"
            // y vol → 0.0001 al final; la frecuencia barre exponencialmente hasta max(20, slide). Se para en dur + 0.05.
            public void Tone(float freq, float dur, Wave type, float vol, float slide = 0f, float delay = 0f, float attack = 0.005f)
            {
                int start = Mathf.RoundToInt(delay * sr), len = Mathf.CeilToInt((dur + 0.05f) * sr);
                Ensure(start + len);
                double target = slide > 0f ? Math.Max(20.0, slide) : freq;
                int nDur = Math.Max(1, Mathf.RoundToInt(dur * sr)), nAtk = Math.Max(1, Mathf.RoundToInt(attack * sr));
                // Rampas exponenciales como productos acumulados (sin potencias por muestra)
                double fMul = Math.Pow(target / freq, 1.0 / nDur);
                double gUp = Math.Pow(vol / 0.0001, 1.0 / nAtk), gDown = Math.Pow(0.0001 / vol, 1.0 / Math.Max(1, nDur - nAtk));
                double f = freq, g = 0.0001, phase = 0.0;
                for (int i = 0; i < len; i++)
                {
                    double dt = f / sr;
                    buf[start + i] += (float)(Osc(type, phase, dt) * g);
                    phase += dt;
                    if (phase >= 1.0) phase -= 1.0;
                    if (i < nDur) f *= fMul;
                    if (i < nAtk) g *= gUp;
                    else if (i < nDur) g *= gDown;
                    else g = 0.0001;
                }
            }

            // noise(dur, { freq, q, type, vol, slide, delay }): ruido blanco por un biquad; ganancia vol → 0.0001
            public void Noise(float dur, float freq, float slide = 0f, Filter type = Filter.Lowpass, float q = 1f, float vol = 0.3f, float delay = 0f)
            {
                int start = Mathf.RoundToInt(delay * sr), len = Mathf.CeilToInt((dur + 0.05f) * sr);
                Ensure(start + len);
                double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
                double b0 = 0, b1 = 0, b2 = 0, a1 = 0, a2 = 0;
                int nDur = Math.Max(1, Mathf.RoundToInt(dur * sr));
                double fMul = slide > 0f ? Math.Pow(slide / freq, 1.0 / nDur) : 1.0;
                double gMul = Math.Pow(0.0001 / vol, 1.0 / nDur);
                double f = freq, g = vol;
                for (int i = 0; i < len; i++)
                {
                    // Coeficientes recalculados cada 16 muestras mientras la frecuencia barre
                    if (i == 0 || (slide > 0f && i < nDur && (i & 15) == 0))
                        Coeffs(type, Math.Min(f, sr * 0.49), q, out b0, out b1, out b2, out a1, out a2);
                    double x = NextNoise();
                    double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                    x2 = x1; x1 = x; y2 = y1; y1 = y;
                    buf[start + i] += (float)(y * g);
                    if (i < nDur) { f *= fMul; g *= gMul; } else g = 0.0001;
                }
            }

            double NextNoise()
            {
                // xorshift32: ruido blanco uniforme en [−1, 1]
                uint s = noiseSeed;
                s ^= s << 13; s ^= s >> 17; s ^= s << 5;
                noiseSeed = s;
                return s / 2147483647.5 - 1.0;
            }

            // Biquad del estándar WebAudio (RBJ): en paso bajo/alto la Q va en dB; en paso banda es lineal
            void Coeffs(Filter type, double f, double q, out double b0, out double b1, out double b2, out double a1, out double a2)
            {
                double w0 = 2.0 * Math.PI * f / sr, cs = Math.Cos(w0), sn = Math.Sin(w0);
                double qLin = type == Filter.Bandpass ? Math.Max(1e-4, q) : Math.Pow(10.0, q / 20.0);
                double alpha = sn / (2.0 * qLin);
                double a0 = 1.0 + alpha;
                switch (type)
                {
                    case Filter.Highpass:
                        b0 = (1 + cs) / 2; b1 = -(1 + cs); b2 = (1 + cs) / 2; break;
                    case Filter.Bandpass:
                        b0 = alpha; b1 = 0; b2 = -alpha; break;
                    default:
                        b0 = (1 - cs) / 2; b1 = 1 - cs; b2 = (1 - cs) / 2; break;
                }
                a1 = -2 * cs; a2 = 1 - alpha;
                b0 /= a0; b1 /= a0; b2 /= a0; a1 /= a0; a2 /= a0;
            }

            // Osciladores con poco aliasing (PolyBLEP en cuadrada y diente de sierra)
            static double Osc(Wave w, double ph, double dt)
            {
                switch (w)
                {
                    case Wave.Square:
                        return (ph < 0.5 ? 1.0 : -1.0) + Blep(ph, dt) - Blep((ph + 0.5) % 1.0, dt);
                    case Wave.Sawtooth:
                        return 2.0 * ph - 1.0 - Blep(ph, dt);
                    case Wave.Triangle:
                        return 1.0 - 4.0 * Math.Abs(ph - 0.5);
                    default:
                        return Math.Sin(2.0 * Math.PI * ph);
                }
            }

            static double Blep(double t, double dt)
            {
                if (dt <= 0) return 0;
                if (t < dt) { t /= dt; return t + t - t * t - 1.0; }
                if (t > 1.0 - dt) { t = (t - 1.0) / dt; return t * t + t + t + 1.0; }
                return 0;
            }
        }
    }
}
