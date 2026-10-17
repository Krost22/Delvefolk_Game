using UnityEngine;

namespace Delvefolk
{
    // Pose del pivote del brazo: yaw (negativo = lado derecho), pitch (positivo = punta hacia abajo),
    // roll (giro sobre el eje del arma) y reach (desplazamiento adelante del brazo)
    public struct Pose
    {
        public float Yaw, Pitch, Roll, Reach;
        public Pose(float yaw, float pitch, float roll = 0f, float reach = 0f) { Yaw = yaw; Pitch = pitch; Roll = roll; Reach = reach; }

        public static Pose Mix(Pose a, Pose b, float t)
        {
            return new Pose(Mathf.Lerp(a.Yaw, b.Yaw, t), Mathf.Lerp(a.Pitch, b.Pitch, t), Mathf.Lerp(a.Roll, b.Roll, t), Mathf.Lerp(a.Reach, b.Reach, t));
        }
    }

    public enum AttackPhase { Windup, Active, Recovery }

    // Estado de un objeto entre pisos: { id, durability, broken } (+ munición de la escopeta)
    [System.Serializable]
    public sealed class ItemState
    {
        public string id = "fists";
        public float durability = float.PositiveInfinity;
        public bool broken;
        public int ammo = -1;
        public ItemState() { }
        public ItemState(string id) { this.id = id; }
    }

    // Clase base de las armas improvisadas (y de la comida: ocupa la mano igual que un arma).
    // Para añadir un arma: heredar, ajustar parámetros, modelo (frente +Z) y poses, y registrarla en WeaponRegistry.
    public class Weapon
    {
        public string Id = "base", Name = "Nada", Icon = "✋", Style = "";
        public float Damage = 5f;
        public float AttackRange = 1.5f;     // alcance desde el centro del jugador
        public float AttackArc = 70f;        // grados
        public float AttackCooldown = 0.4f;  // tiempo mínimo entre ataques
        public float Windup = 0.1f;          // preparación (sin daño)
        public float AttackDuration = 0.1f;  // ventana activa (hace daño)
        public float Recovery = 0.15f;       // recuperación
        public float KnockbackForce = 6f;
        public float Stun = 0.25f;
        public float Hitstop = 0.05f;
        public float Shake = 0.15f;
        public float Lunge = 2f;             // pequeño paso adelante al golpear
        public float MoveMult = 0.4f;        // velocidad de movimiento mientras ataca
        public int Color = 0xffffff;         // color del arco del ataque
        public string HitSound = "hit";
        public string HitText;
        // Durabilidad: cada ataque que acierta gasta un uso. A 0 se rompe (pero se sigue pudiendo usar, muy mal).
        public float MaxDurability = float.PositiveInfinity;
        public float Durability = float.PositiveInfinity;
        public bool Broken;
        public string BrokenName;
        // Comida
        public bool IsFood;
        public float Heal, EatTime, EatMoveMult = 0.35f;

        public bool HasDurability { get { return !float.IsInfinity(MaxDurability); } }
        public float ArcRad { get { return AttackArc * Mathf.Deg2Rad; } }
        public float Total { get { return Windup + AttackDuration + Recovery; } }

        // Gasta un uso. Devuelve true si el arma se acaba de romper.
        public bool Wear()
        {
            if (Broken || !HasDurability) return false;
            Durability--;
            if (Durability <= 0) { BreakWeapon(); return true; }
            return false;
        }

        // Versión rota: muy poco daño y empuje, sin efectos especiales
        public virtual void BreakWeapon()
        {
            Broken = true;
            Durability = 0;
            Name = BrokenName ?? (Name + " rota");
            Style = "Rota · apenas hace daño";
            Damage = Mathf.Max(1f, Mathf.Round(Damage * 0.18f));
            KnockbackForce *= 0.35f;
            AttackRange *= 0.75f;
            Stun = 0.1f; Hitstop = 0.03f; Shake = 0.08f;
            HitText = null; HitSound = "thwack";
            Color = 0x8a8a8a;
        }

        public virtual ItemState GetState() { return new ItemState { id = Id, durability = Durability, broken = Broken }; }
        public virtual void LoadState(ItemState s) { }

        // Modelo que se ve en la mano / en el suelo según el estado (frente en +Z)
        public GameObject BuildDisplayModel(Transform parent) { return Broken ? BuildBrokenModel(parent) : BuildModel(parent); }

        public virtual GameObject BuildBrokenModel(Transform parent)
        {
            var m = BuildModel(parent);
            var s = m.transform.localScale; s.z *= 0.5f; m.transform.localScale = s;
            return m;
        }

        public virtual GameObject BuildModel(Transform parent)
        {
            var go = new GameObject(Id);
            go.transform.SetParent(parent, false);
            return go;
        }

        // Ayuda para modelos hechos con primitivas: una sola malla con color por vértice
        protected static GameObject MeshModel(Transform parent, string name, MeshBuilder b)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            CharacterModel.MeshObject(go.transform, "malla", b.ToMesh(name), Mats.Lit, Vector3.zero);
            return go;
        }

        public virtual Pose IdlePose() { return new Pose(-0.6f, 0.6f); }
        public virtual Pose PoseFor(AttackPhase phase, float t) { return IdlePose(); }
        public virtual Pose EatPose(float t) { return IdlePose(); }

        // Dirección del empuje: mezcla entre la dirección hacia el objetivo y hacia donde mira el jugador
        public virtual Vector2 KnockDirection(float fx, float fz, float dx, float dz)
        {
            float d = MathUtil.Hypot(dx, dz); if (d == 0f) d = 1f;
            float x = dx / d * 0.55f + fx * 0.45f, z = dz / d * 0.55f + fz * 0.45f;
            float l = MathUtil.Hypot(x, z); if (l == 0f) l = 1f;
            return new Vector2(x / l, z / l);
        }
    }

    // Sin arma: bofetadas. Funciona, pero da vergüenza.
    public sealed class FistsWeapon : Weapon
    {
        public FistsWeapon()
        {
            Id = "fists"; Name = "Manos desnudas"; Icon = "✋"; Style = "Bofetada · casi inútil";
            Damage = 3; AttackRange = 1.55f; AttackArc = 80; AttackCooldown = 0.38f;
            Windup = 0.07f; AttackDuration = 0.08f; Recovery = 0.16f;
            KnockbackForce = 5; Stun = 0.2f; Hitstop = 0.04f; Shake = 0.1f;
            Lunge = 1.5f; MoveMult = 0.55f;
            Color = 0xffd6b0;
            HitSound = "thwack"; HitText = "¡PLAS!";
        }

        public override GameObject BuildModel(Transform parent)
        {
            var b = new MeshBuilder();
            b.Add(ProcMesh.Icosahedron(0.17f), new Vector3(0, 0, 0.15f), MathUtil.Hex(0xf0c08a));
            return MeshModel(parent, "puño", b);
        }

        public override Pose IdlePose() { return new Pose(-1.2f, 0.9f); }

        public override Pose PoseFor(AttackPhase phase, float t)
        {
            var back = new Pose(-0.5f, 0.3f, 0, -0.2f);
            var outp = new Pose(-0.1f, 0.2f, 0, 0.7f);
            if (phase == AttackPhase.Windup) return Pose.Mix(IdlePose(), back, t);
            if (phase == AttackPhase.Active) return Pose.Mix(back, outp, MathUtil.EaseOutCubic(t));
            return Pose.Mix(outp, IdlePose(), t);
        }
    }
}
