using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Rueda visual de la ruleta: aparece sobre el Crupier mirando a la cámara, gira con la curva de la
    // configuración y se detiene exactamente en el ángulo pedido. Si RouletteConfig.WheelModel está vacío
    // construye un placeholder (segmentos de colores, aro dorado e indicador fijo arriba).
    public sealed class RouletteWheel
    {
        readonly Game G;
        readonly RouletteConfig cfg;
        public readonly Transform Root;      // sigue al Crupier y mira a la cámara
        readonly Transform orient;           // rotación extra y escala de la configuración
        readonly Transform spinner;          // la pieza que gira
        readonly Quaternion spinnerBase;
        readonly List<GameObject> glow = new List<GameObject>();
        readonly float[] angles;
        public readonly Vector3 Axis, Ref;
        public readonly int Sign;
        public readonly bool Placeholder;
        public float Forward;                // avance acumulado (grados)
        public float Windup;                 // desfase visual de la preparación (grados)
        float from, to, dur, t;
        public bool Spinning { get; private set; }
        float show, showTarget, glowT;
        int highlight = -1, lastTickSeg = -1;
        const float R = 1.6f;
        static readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();

        public RouletteWheel(Game g, RouletteConfig config, Transform parent)
        {
            G = g; cfg = config;
            Axis = cfg.SpinAxis.sqrMagnitude > 1e-6f ? cfg.SpinAxis.normalized : Vector3.forward;
            var r = Vector3.ProjectOnPlane(cfg.ReferenceDir, Axis);
            if (r.sqrMagnitude < 1e-6f) r = Vector3.ProjectOnPlane(Vector3.up, Axis);
            if (r.sqrMagnitude < 1e-6f) r = Vector3.ProjectOnPlane(Vector3.right, Axis);
            Ref = r.normalized;
            Sign = cfg.Clockwise ? 1 : -1;
            angles = new float[cfg.Segments.Length];
            for (int i = 0; i < angles.Length; i++) angles[i] = cfg.AngleOf(i);

            Root = new GameObject("Ruleta").transform;
            Root.SetParent(parent, false);
            orient = new GameObject("Orientacion").transform;
            orient.SetParent(Root, false);
            orient.localRotation = Quaternion.Euler(cfg.WheelEuler);
            if (Art.Has(cfg.WheelModel))
            {
                var go = Art.Spawn(cfg.WheelModel, orient);
                var sp = string.IsNullOrEmpty(cfg.WheelSpinnerPath) ? null : go.transform.Find(cfg.WheelSpinnerPath);
                if (!sp && !string.IsNullOrEmpty(cfg.WheelSpinnerPath))
                    Debug.LogWarning("[Ruleta] No encuentro '" + cfg.WheelSpinnerPath + "' dentro de " + cfg.WheelModel.name + ": gira el modelo entero.");
                spinner = sp ? sp : go.transform;
            }
            else
            {
                Placeholder = true;
                spinner = BuildPlaceholder();
            }
            spinnerBase = spinner.localRotation;
            Root.localScale = Vector3.zero;
            Root.gameObject.SetActive(false);
        }

        public float AngleOf(int i) { return angles[i]; }
        public float[] Angles { get { return angles; } }

        // ---------- Placeholder: rueda de feria con segmentos de colores ----------
        Vector3 Dir(float deg) { return Quaternion.AngleAxis(deg, Axis) * Ref; }

        float HalfWidth(int i)
        {
            if (angles.Length < 2) return 180f;
            float m = 360f;
            for (int j = 0; j < angles.Length; j++) if (j != i) m = Mathf.Min(m, Mathf.Abs(Mathf.DeltaAngle(angles[i], angles[j])));
            return Mathf.Max(1f, m / 2f);
        }

        // Cuña plana entre a0 y a1 (grados), visible por las dos caras, desplazada "depth" a lo largo del eje
        MeshData Wedge(float a0, float a1, float r0, float r1, float depth)
        {
            var md = new MeshData();
            int seg = Mathf.Max(2, Mathf.CeilToInt((a1 - a0) / 6f));
            Vector3 off = Axis * depth, nF = -Axis, nB = Axis;
            for (int side = 0; side < 2; side++)
            {
                int b = md.Count;
                for (int k = 0; k <= seg; k++)
                {
                    var d = Dir(Mathf.Lerp(a0, a1, k / (float)seg));
                    md.Add(off + d * r0, side == 0 ? nF : nB, Vector2.zero);
                    md.Add(off + d * r1, side == 0 ? nF : nB, Vector2.zero);
                }
                for (int k = 0; k < seg; k++)
                {
                    int i0 = b + k * 2, i1 = i0 + 1, i2 = i0 + 2, i3 = i0 + 3;
                    if (side == 0) { md.I.Add(i0); md.I.Add(i1); md.I.Add(i3); md.I.Add(i0); md.I.Add(i3); md.I.Add(i2); }
                    else { md.I.Add(i0); md.I.Add(i3); md.I.Add(i1); md.I.Add(i0); md.I.Add(i2); md.I.Add(i3); }
                }
            }
            return md;
        }

        Transform BuildPlaceholder()
        {
            var face = Quaternion.FromToRotation(Vector3.forward, Axis);   // el toro y el disco de three.js miran a +Z
            var up = Quaternion.FromToRotation(Vector3.up, Axis);          // los cilindros van a lo largo de +Y
            Color wood = MathUtil.Hex(0x5a2e1a), gold = MathUtil.Hex(0xe8c050);
            // Marco fijo (no gira): soporte trasero y aro dorado
            var frame = new MeshBuilder();
            frame.Add(ProcMesh.Cylinder(R + 0.25f, R + 0.25f, 0.14f, 32), Matrix4x4.TRS(Axis * 0.1f, up, Vector3.one), wood);
            frame.Add(ProcMesh.Torus(R + 0.12f, 0.1f, 6, 40), Matrix4x4.TRS(-Axis * 0.02f, face, Vector3.one), gold);
            CharacterModel.MeshObject(orient, "Marco", frame.ToMesh("marcoRuleta"), Mats.Lit, Vector3.zero);
            // Indicador fijo en PointerAngle, con la punta hacia el centro
            var pd = Dir(cfg.PointerAngle);
            var pointer = new MeshBuilder().Add(ProcMesh.Cone(0.22f, 0.55f, 3), Matrix4x4.TRS(pd * (R + 0.3f) - Axis * 0.16f, Quaternion.FromToRotation(Vector3.up, -pd), Vector3.one), MathUtil.Hex(0xfff2c0));
            CharacterModel.MeshObject(orient, "Indicador", pointer.ToMesh("indicadorRuleta"), Mats.Unlit, Vector3.zero);

            var sp = new GameObject("Giro").transform;
            sp.SetParent(orient, false);
            var segs = new MeshBuilder();
            var lines = new MeshBuilder();
            var glowMesh = new List<Mesh>();
            for (int i = 0; i < angles.Length; i++)
            {
                float a = angles[i], h = HalfWidth(i);
                segs.Add(Wedge(a - h, a + h, 0.32f, R, -0.04f), Vector3.zero, cfg.Segments[i].Color);
                // Separador dorado en el borde del segmento
                var e = Dir(a + h);
                lines.Add(ProcMesh.Box(0.06f, R - 0.3f, 0.04f), Matrix4x4.TRS(e * (R + 0.32f) / 2f - Axis * 0.07f, Quaternion.LookRotation(Axis, e), Vector3.one), gold);
                // Marca blanca en el centro (para leer el ángulo del segmento)
                lines.Add(ProcMesh.Sphere(0.09f, 6, 4), Dir(a) * (R - 0.22f) - Axis * 0.08f, MathUtil.Hex(0xfff6e0));
                var gm = new MeshBuilder().Add(Wedge(a - h, a + h, 0.32f, R + 0.05f, -0.1f), Vector3.zero, Color.white).ToMesh("brilloRuleta");
                var go = CharacterModel.MeshObject(sp, "Brillo" + i, gm, Mats.Additive, Vector3.zero);
                go.SetActive(false);
                glow.Add(go);
            }
            lines.Add(ProcMesh.Cylinder(0.34f, 0.34f, 0.2f, 16), Matrix4x4.TRS(-Axis * 0.06f, up, Vector3.one), gold);
            lines.Add(ProcMesh.Sphere(0.16f, 8, 6), -Axis * 0.2f, MathUtil.Hex(0xfff2c0));
            CharacterModel.MeshObject(sp, "Segmentos", segs.ToMesh("segmentosRuleta"), Mats.Unlit, Vector3.zero);
            CharacterModel.MeshObject(sp, "Adornos", lines.ToMesh("adornosRuleta"), Mats.Lit, Vector3.zero);
            return sp;
        }

        // ---------- Giro ----------
        public void StartSpin(float target, float duration)
        {
            from = Forward; to = target; t = 0f;
            dur = Mathf.Max(0.05f, duration);
            Spinning = true;
            lastTickSeg = SegmentAtForward();
            Highlight(-1);
        }

        // Avanza el giro; devuelve true en el frame en que se detiene (ya en el ángulo exacto)
        public bool Tick(float dt)
        {
            if (!Spinning) return false;
            t += dt;
            float k = t / dur;
            Forward = k >= 1f ? to : from + (to - from) * RouletteMath.Progress(cfg.Deceleration, k);
            int s = SegmentAtForward();
            if (s != lastTickSeg) { lastTickSeg = s; G.Audio.Play("tick"); }
            if (k >= 1f) { Spinning = false; Apply(); return true; }
            return false;
        }

        public void Stop() { Spinning = false; }

        int SegmentAtForward() { return RouletteMath.IndexAt(Forward, angles, cfg.PointerAngle, Sign); }

        void Apply()
        {
            spinner.localRotation = spinnerBase * Quaternion.AngleAxis(Sign * (Forward + Windup), Axis);
        }

        // Segmento que señala el indicador según la orientación REAL de la pieza (para detectar desajustes)
        public int IndexUnderPointer()
        {
            Apply();
            var parentRot = spinner.parent ? spinner.parent.rotation : Quaternion.identity;
            var pointerW = parentRot * spinnerBase * Dir(cfg.PointerAngle);
            int best = -1; float bestDot = -2f;
            for (int i = 0; i < angles.Length; i++)
            {
                float d = Vector3.Dot(spinner.rotation * Dir(angles[i]), pointerW);
                if (d > bestDot) { bestDot = d; best = i; }
            }
            return best;
        }

        public void Highlight(int i)
        {
            highlight = i; glowT = 0f;
            for (int k = 0; k < glow.Count; k++) if (glow[k]) glow[k].SetActive(k == i);
        }

        public void Show(bool on) { showTarget = on ? 1f : 0f; if (on) Root.gameObject.SetActive(true); }
        // Sin animación (al cancelar: el Crupier ya no actualiza la rueda)
        public void HideNow() { showTarget = 0f; show = 0f; Highlight(-1); if (Root) { Root.localScale = Vector3.zero; Root.gameObject.SetActive(false); } }
        public bool Visible { get { return Root && Root.gameObject.activeSelf; } }

        // Cada frame: sigue al Crupier, mira a la cámara, aparece/desaparece y hace latir el segmento ganador
        public void Update(float dt, float x, float z)
        {
            if (!Root) return;
            Apply();
            show = MathUtil.Damp(show, showTarget, 9f, dt);
            if (showTarget == 0f && show < 0.02f) { show = 0f; if (Root.gameObject.activeSelf) Root.gameObject.SetActive(false); return; }
            var cam = G.CameraCtl.Cam;
            var fwd = cam ? cam.transform.forward : -G.CameraCtl.Offset().normalized;
            Root.position = new Vector3(x, 0f, z) + cfg.WheelOffset;
            Root.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            Root.localScale = Vector3.one * cfg.WheelScale * show;
            if (highlight >= 0 && highlight < glow.Count && glow[highlight])
            {
                glowT += dt;
                var rr = glow[highlight].GetComponent<Renderer>();
                rr.GetPropertyBlock(mpb);
                mpb.SetColor(Mats.BaseColorId, new Color(1f, 1f, 1f, 0.35f + 0.3f * Mathf.Sin(glowT * 12f)));
                rr.SetPropertyBlock(mpb);
            }
        }

        public void Destroy()
        {
            if (Root) Object.Destroy(Root.gameObject);
        }
    }
}
