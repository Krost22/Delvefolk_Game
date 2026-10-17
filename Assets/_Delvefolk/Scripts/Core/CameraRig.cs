using UnityEngine;

namespace Delvefolk
{
    // Cámara isométrica fija que sigue a los jugadores con suavizado y vibración por "trauma" (CameraController.js).
    // El web la pone al "sureste" mirando al "noroeste"; en Unity el sur es −Z, de ahí el signo de offset.z.
    public sealed class CameraRig
    {
        public readonly Camera Cam;
        public readonly float Yaw = Mathf.PI / 4f;           // orientación fija (diagonal)
        public readonly float Pitch = 54f * Mathf.Deg2Rad;   // inclinación hacia abajo
        public float Distance = 22f;
        public const float MinDist = 15f, MaxDist = 30f;
        public float TargetX, TargetZ;        // punto suavizado que mira la cámara
        public float LookAheadX, LookAheadZ;
        public float Extra;                   // distancia extra para encuadrar a varios jugadores
        public float Trauma;
        public float Time;

        public CameraRig(Camera cam) { Cam = cam; }

        public Vector3 Offset()
        {
            float cp = Mathf.Cos(Pitch), d = Distance + Extra;
            return new Vector3(Mathf.Sin(Yaw) * cp * d, Mathf.Sin(Pitch) * d, -Mathf.Cos(Yaw) * cp * d);
        }

        // Vectores de movimiento: dirección de la cámara proyectada en el plano horizontal.
        // right = (f.z, 0, −f.x) (en el web era (−f.z, f.x) por la quiralidad)
        public void GetBasis(out Vector2 forward, out Vector2 right)
        {
            Vector3 f = Cam ? Cam.transform.forward : -Offset();
            float l = Mathf.Sqrt(f.x * f.x + f.z * f.z);
            if (l == 0f) l = 1f;
            forward = new Vector2(f.x / l, f.z / l);
            right = new Vector2(forward.y, -forward.x);
        }

        public void SnapTo(float x, float z)
        {
            TargetX = x; TargetZ = z;
            LookAheadX = 0f; LookAheadZ = 0f;
            Apply();
        }

        public void AddShake(float amount) { Trauma = Mathf.Min(1f, Trauma + amount); }

        public void Zoom(float wheelDelta)
        {
            if (wheelDelta == 0f) return;
            // Rueda hacia el usuario (abajo) aleja, como deltaY > 0 en el navegador
            Distance = MathUtil.Clamp(Distance - Mathf.Sign(wheelDelta) * 1.5f, MinDist, MaxDist);
        }

        // spread: separación entre jugadores (la cámara se aleja para que quepan)
        public void Update(float dt, float x, float z, float vx, float vz, float spread = 0f)
        {
            Time += dt;
            Extra = MathUtil.Damp(Extra, MathUtil.Clamp(spread * 0.6f - 4f, 0f, 10f), 3f, dt);
            // Pequeño adelanto hacia donde se mueve el jugador, suavizado lentamente (no vibra)
            float ax = MathUtil.Clamp(vx * 0.18f, -1.5f, 1.5f);
            float az = MathUtil.Clamp(vz * 0.18f, -1.5f, 1.5f);
            LookAheadX = MathUtil.Damp(LookAheadX, ax, 2.5f, dt);
            LookAheadZ = MathUtil.Damp(LookAheadZ, az, 2.5f, dt);
            TargetX = MathUtil.Damp(TargetX, x + LookAheadX, 7f, dt);
            TargetZ = MathUtil.Damp(TargetZ, z + LookAheadZ, 7f, dt);
            Trauma = Mathf.Max(0f, Trauma - dt * 1.7f);
            Apply();
        }

        // Solo la vibración (durante el hitstop la cámara sigue temblando)
        public void Apply()
        {
            if (!Cam) return;
            var off = Offset();
            float s = Trauma * Trauma * 0.55f;
            float t = Time * 38f;
            float sx = s * (Mathf.Sin(t * 1.1f) + Mathf.Sin(t * 2.3f) * 0.5f);
            float sy = s * (Mathf.Sin(t * 1.7f + 1f) + Mathf.Sin(t * 2.9f) * 0.5f) * 0.6f;
            float sz = s * (Mathf.Sin(t * 1.3f + 2f) + Mathf.Sin(t * 3.1f) * 0.5f);
            var tr = Cam.transform;
            tr.position = new Vector3(TargetX + off.x + sx, off.y + sy, TargetZ + off.z + sz);
            tr.LookAt(new Vector3(TargetX + sx, 0.6f + sy, TargetZ + sz));
        }
    }
}
