using System;
using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Mezclador del audio sintetizado: vive junto al AudioListener y suma las voces a la mezcla final
    // (hilo de audio). Ganancia maestra y compresor como el DynamicsCompressorNode del navegador:
    // umbral −14 dB, rodilla 30 dB, ratio 4, ataque 3 ms, relajación 250 ms y ganancia de compensación.
    public sealed class SynthMixer : MonoBehaviour
    {
        struct Voice { public float[] Data; public double Pos; public float Step; }

        const int MaxVoices = 48;
        readonly List<Voice> voices = new List<Voice>(MaxVoices);
        readonly object gate = new object();
        public volatile bool Muted;
        float master = 0.55f;

        // Compresor
        float linearThreshold, kneeThreshold, kneeThresholdDb, ykneeThresholdDb, slope, k, makeup;
        float env, gain = 1f, attackCoef, releaseCoef, envRelease;

        public void Init(float masterGain)
        {
            master = masterGain;
            SetupCompressor(-14f, 30f, 4f);
            int sr = AudioSynth.SampleRate;
            attackCoef = Mathf.Exp(-1f / (0.003f * sr));
            releaseCoef = Mathf.Exp(-1f / (0.25f * sr));
            envRelease = Mathf.Exp(-1f / (0.05f * sr));
        }

        public void Add(float[] pcm, float step)
        {
            lock (gate)
            {
                if (voices.Count >= MaxVoices) voices.RemoveAt(0);
                voices.Add(new Voice { Data = pcm, Pos = 0, Step = step });
            }
        }

        public int VoiceCount { get { lock (gate) return voices.Count; } }

        void OnAudioFilterRead(float[] data, int channels)
        {
            int frames = data.Length / channels;
            float m = Muted ? 0f : master;
            lock (gate)
            {
                if (voices.Count == 0 && env < 1e-5f) return;
                for (int f = 0; f < frames; f++)
                {
                    float s = 0f;
                    for (int v = voices.Count - 1; v >= 0; v--)
                    {
                        var vc = voices[v];
                        int i = (int)vc.Pos;
                        if (i + 1 >= vc.Data.Length) { voices.RemoveAt(v); continue; }
                        float fr = (float)(vc.Pos - i);
                        s += vc.Data[i] + (vc.Data[i + 1] - vc.Data[i]) * fr;
                        vc.Pos += vc.Step;
                        voices[v] = vc;
                    }
                    s *= m;
                    // Detector de pico y curva estática con rodilla suave
                    float a = Math.Abs(s);
                    env = a > env ? a : env * envRelease + a * (1f - envRelease);
                    float target = env > linearThreshold ? Saturate(env) / env : 1f;
                    gain = target < gain ? target + (gain - target) * attackCoef : target + (gain - target) * releaseCoef;
                    float o = Mathf.Clamp(s * gain * makeup, -1f, 1f);
                    for (int c = 0; c < channels; c++) data[f * channels + c] += o;
                }
            }
        }

        // ---------- Curva del compresor (DynamicsCompressorKernel de Chromium) ----------
        static float ToDb(float x) { return 20f * Mathf.Log10(Mathf.Max(1e-9f, x)); }
        static float FromDb(float db) { return Mathf.Pow(10f, db / 20f); }

        float KneeCurve(float x, float kk)
        {
            if (x < linearThreshold) return x;
            return linearThreshold + (1f - Mathf.Exp(-kk * (x - linearThreshold))) / kk;
        }

        float Saturate(float x)
        {
            if (x < kneeThreshold) return KneeCurve(x, k);
            float yDb = ykneeThresholdDb + slope * (ToDb(x) - kneeThresholdDb);
            return FromDb(yDb);
        }

        float SlopeAt(float x, float kk)
        {
            if (x < linearThreshold) return 1f;
            float x2 = x * 1.001f;
            float xDb = ToDb(x), x2Db = ToDb(x2);
            float yDb = ToDb(KneeCurve(x, kk)), y2Db = ToDb(KneeCurve(x2, kk));
            return (y2Db - yDb) / (x2Db - xDb);
        }

        void SetupCompressor(float thresholdDb, float kneeDb, float ratio)
        {
            linearThreshold = FromDb(thresholdDb);
            slope = 1f / ratio;
            // k tal que la pendiente al final de la rodilla sea 1/ratio
            float x = FromDb(thresholdDb + kneeDb), minK = 0.1f, maxK = 10000f, kk = 5f;
            for (int i = 0; i < 15; i++)
            {
                if (SlopeAt(x, kk) < slope) maxK = kk; else minK = kk;
                kk = Mathf.Sqrt(minK * maxK);
            }
            k = kk;
            kneeThresholdDb = thresholdDb + kneeDb;
            kneeThreshold = FromDb(kneeThresholdDb);
            ykneeThresholdDb = ToDb(KneeCurve(kneeThreshold, k));
            // Ganancia de compensación (ajuste perceptual del navegador)
            makeup = Mathf.Pow(1f / Saturate(1f), 0.6f);
        }
    }
}
