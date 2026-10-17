using UnityEngine;

namespace Delvefolk
{
    // Resultados posibles de la ruleta del Crupier
    public enum RouletteOutcome { Rojo, Verde, Negro, Dorado, Azul, Morado }

    [System.Serializable]
    public sealed class RouletteSegment
    {
        public RouletteOutcome Outcome;
        [Tooltip("Peso relativo. Los negativos cuentan como 0; con 0 nunca sale")]
        public float Weight = 1f;
        [Tooltip("Ángulo del centro del segmento (grados, alrededor del eje desde la dirección de referencia). Solo si 'Uniform Angles' está desactivado")]
        public float AngleDeg;
        public Color Color = Color.white;
        [Tooltip("Calculado: % = peso / suma de los pesos positivos (solo lectura)")]
        public float Probability;
    }

    // Configuración de la ruleta del Crupier (pesos, giro, modelos y herramientas de prueba).
    // El juego la carga de Resources/CrupierRoulette; si no existe usa estos valores por defecto.
    [CreateAssetMenu(fileName = "CrupierRoulette", menuName = "Delvefolk/Ruleta del Crupier")]
    public sealed class RouletteConfig : ScriptableObject
    {
        [Header("Probabilidades (se normalizan con la suma de los pesos positivos)")]
        public RouletteSegment[] Segments = DefaultSegments();
        [Tooltip("Reparte los segmentos por igual (360/n) en el orden de la lista; desactívalo para usar AngleDeg de cada uno")]
        public bool UniformAngles = true;

        [Header("Secuencia")]
        [Tooltip("Anuncio y preparación antes de girar (s)")] public float PrepareTime = 1.0f;
        [Tooltip("Duración total del giro (s). Se ignora si 'Initial Speed' > 0")] public float SpinDuration = 3.2f;
        [Tooltip("Velocidad inicial (grados/s). 0 = la deduce de la duración y la curva")] public float InitialSpeed = 0f;
        public int MinTurns = 3, MaxTurns = 5;
        [Tooltip("Progreso del giro (0→1) frente al tiempo (0→1). Si se aplana al final, la rueda frena poco a poco")]
        public AnimationCurve Deceleration = DefaultCurve();
        [Tooltip("Pausa con el resultado a la vista antes de activar la habilidad (s)")] public float RevealPause = 0.8f;
        [Tooltip("Descanso entre tiradas (s)")] public float Cooldown = 6f;
        [Tooltip("Multiplicador del descanso en la fase 2")] public float Phase2CooldownMult = 0.6f;
        [Tooltip("Tope de seguridad para una habilidad que no termina (s)")] public float MaxEffectTime = 15f;

        [Header("Geometría (espacio local de la pieza que gira)")]
        [Tooltip("Eje de rotación. Por defecto +Z: la rueda mira a la cámara con su cara −Z")] public Vector3 SpinAxis = Vector3.forward;
        [Tooltip("Dirección del ángulo 0 (perpendicular al eje)")] public Vector3 ReferenceDir = Vector3.up;
        [Tooltip("Sentido positivo del ángulo (con el eje por defecto: horario visto de frente)")] public bool Clockwise = true;
        [Tooltip("Ángulo en el que está el indicador (grados, mismo criterio que los segmentos)")] public float PointerAngle = 0f;

        [Header("Modelos (vacío = placeholder hecho por código)")]
        public GameObject CrupierModel;
        public float CrupierHeight = 2.6f;
        public GameObject WheelModel;
        [Tooltip("Ruta del hijo que gira dentro de WheelModel (vacío = el propio modelo)")] public string WheelSpinnerPath = "";
        [Tooltip("Posición de la rueda respecto al Crupier")] public Vector3 WheelOffset = new Vector3(0f, 4.5f, 0f);
        [Tooltip("Rotación extra de la rueda (grados) sobre la orientación hacia la cámara")] public Vector3 WheelEuler = Vector3.zero;
        public float WheelScale = 1f;
        [Tooltip("Con WheelModel: radio interior y exterior de los segmentos de color que dibuja el juego (espacio local de la pieza que gira; 0 = no dibuja)")]
        public float WheelSegmentsInner, WheelSegmentsOuter;
        [Tooltip("Desplazamiento de esos segmentos a lo largo del eje (negativo = hacia la cámara)")] public float WheelSegmentsDepth;

        [Header("PRUEBAS (solo en el editor o en builds de desarrollo)")]
        [Tooltip("Muestra en la consola pesos, probabilidades y el resultado de cada tirada")] public bool LogResults = true;
        [Tooltip("PRUEBA: fuerza siempre el resultado de abajo. Se ignora en builds normales")] public bool ForceOutcomeEnabled;
        public RouletteOutcome ForcedOutcome;
        [Tooltip("PRUEBA: gira la ruleta pero no activa la habilidad")] public bool SpinWithoutAbility;

        public static RouletteSegment[] DefaultSegments()
        {
            return new[]
            {
                new RouletteSegment { Outcome = RouletteOutcome.Rojo, AngleDeg = 0, Weight = 30, Color = MathUtil.Hex(0xd8282a), Probability = 30 },
                new RouletteSegment { Outcome = RouletteOutcome.Verde, AngleDeg = 60, Weight = 25, Color = MathUtil.Hex(0x2fae4a), Probability = 25 },
                new RouletteSegment { Outcome = RouletteOutcome.Negro, AngleDeg = 120, Weight = 20, Color = MathUtil.Hex(0x1c1c22), Probability = 20 },
                new RouletteSegment { Outcome = RouletteOutcome.Dorado, AngleDeg = 180, Weight = 10, Color = MathUtil.Hex(0xf2c230), Probability = 10 },
                new RouletteSegment { Outcome = RouletteOutcome.Azul, AngleDeg = 240, Weight = 10, Color = MathUtil.Hex(0x2f6fd8), Probability = 10 },
                new RouletteSegment { Outcome = RouletteOutcome.Morado, AngleDeg = 300, Weight = 5, Color = MathUtil.Hex(0x8a3fd0), Probability = 5 },
            };
        }

        // Arranca rápido (pendiente 3) y termina plana, como 1 − (1 − t)³
        public static AnimationCurve DefaultCurve()
        {
            return new AnimationCurve(new Keyframe(0f, 0f, 3f, 3f), new Keyframe(0.5f, 0.875f, 0.75f, 0.75f), new Keyframe(1f, 1f, 0f, 0f));
        }

        public float AngleOf(int i)
        {
            return UniformAngles ? i * 360f / Mathf.Max(1, Segments.Length) : Segments[i].AngleDeg;
        }

        public int IndexOf(RouletteOutcome o)
        {
            for (int i = 0; i < Segments.Length; i++) if (Segments[i] != null && Segments[i].Outcome == o) return i;
            return -1;
        }

        // Los forzados del Inspector solo valen en el editor o en builds de desarrollo
        public bool ForceAllowed { get { return ForceOutcomeEnabled && (Application.isEditor || Debug.isDebugBuild); } }

        void OnValidate()
        {
            if (Segments == null) return;
            foreach (var s in Segments) if (s != null && s.Weight < 0f) s.Weight = 0f;
            if (MinTurns < 0) MinTurns = 0;
            if (MaxTurns < MinTurns) MaxTurns = MinTurns;
            var p = RouletteMath.Probabilities(Segments);
            for (int i = 0; i < Segments.Length; i++) if (Segments[i] != null) Segments[i].Probability = Mathf.Round(p[i] * 1000f) / 10f;
        }

        static RouletteConfig cached;
        public static RouletteConfig Load()
        {
            if (!cached) cached = Resources.Load<RouletteConfig>("CrupierRoulette");
            if (!cached) { cached = CreateInstance<RouletteConfig>(); cached.name = "CrupierRoulette (por defecto)"; }
            return cached;
        }
    }

    // Cálculos puros de la ruleta (sin estado: se prueban en EditMode)
    public static class RouletteMath
    {
        // Pesos efectivos: negativos y NaN cuentan como 0
        public static float[] Weights(RouletteSegment[] segs)
        {
            var w = new float[segs != null ? segs.Length : 0];
            for (int i = 0; i < w.Length; i++)
            {
                float v = segs[i] != null ? segs[i].Weight : 0f;
                w[i] = float.IsNaN(v) || v < 0f ? 0f : v;
            }
            return w;
        }

        public static float[] Probabilities(RouletteSegment[] segs)
        {
            var w = Weights(segs);
            float sum = 0f; foreach (var v in w) sum += v;
            var p = new float[w.Length];
            if (sum > 0f) for (int i = 0; i < w.Length; i++) p[i] = w[i] / sum;
            return p;
        }

        // Selección ponderada: r01 en [0, 1). Devuelve −1 si no hay ningún peso positivo
        public static int Pick(float[] w, float r01)
        {
            float sum = 0f; int last = -1;
            for (int i = 0; i < w.Length; i++) if (w[i] > 0f) { sum += w[i]; last = i; }
            if (last < 0) return -1;
            float x = Mathf.Clamp01(r01) * sum, acc = 0f;
            for (int i = 0; i < w.Length; i++)
            {
                if (w[i] <= 0f) continue;
                acc += w[i];
                if (x < acc) return i;
            }
            return last;   // r01 = 1 o redondeo: el último con peso
        }

        public static float Mod(float a, float m) { float r = a % m; return r < 0f ? r + m : r; }

        // Avance total (grados, desde current) para que el segmento de ángulo segAngle acabe bajo el indicador.
        // Con la pieza girada sign·φ, el centro del segmento queda en segAngle + sign·φ: hay que llegar a φ ≡ sign·(pointer − segAngle)
        public static float Target(float current, float segAngle, float pointer, int sign, int turns)
        {
            float want = Mod(sign * (pointer - segAngle), 360f);
            float delta = Mod(want - Mod(current, 360f), 360f);
            return current + delta + 360f * Mathf.Max(0, turns);
        }

        // Segmento cuyo centro queda más cerca del indicador con la pieza girada sign·forward
        public static int IndexAt(float forward, float[] angles, float pointer, int sign)
        {
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < angles.Length; i++)
            {
                float d = Mathf.Abs(Mathf.DeltaAngle(angles[i] + sign * forward, pointer));
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        // Pendiente inicial de la curva normalizada (para deducir la duración a partir de la velocidad inicial)
        public static float StartSlope(AnimationCurve c)
        {
            float end = c != null ? c.Evaluate(1f) : 1f;
            if (c == null || Mathf.Abs(end) < 1e-4f) return 3f;
            return Mathf.Max(0.05f, (c.Evaluate(0.02f) - c.Evaluate(0f)) / 0.02f / end);
        }

        // Progreso normalizado 0→1 (exacto en los extremos aunque la curva no termine en 1)
        public static float Progress(AnimationCurve c, float k)
        {
            if (k <= 0f) return 0f;
            if (k >= 1f) return 1f;
            if (c == null || c.length == 0) return 1f - (1f - k) * (1f - k) * (1f - k);
            float end = c.Evaluate(1f);
            return Mathf.Abs(end) < 1e-4f ? k : c.Evaluate(k) / end;
        }
    }
}
