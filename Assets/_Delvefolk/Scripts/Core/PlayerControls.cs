using UnityEngine;

namespace Delvefolk
{
    // Dispositivo de un jugador: uno de los dos "teclados" (mitad izquierda / mitad derecha) o un mando.
    // En el teclado solo caben dos jugadores; el J3 y el J4 juegan con mando. Cualquiera puede cambiar.
    public enum DeviceKind { KeyboardA, KeyboardB, Pad }

    public struct DeviceBinding : System.IEquatable<DeviceBinding>
    {
        public DeviceKind Kind;
        public int Pad;   // hueco de mando (0..3) si Kind == Pad

        public static DeviceBinding KeyA { get { return new DeviceBinding { Kind = DeviceKind.KeyboardA, Pad = -1 }; } }
        public static DeviceBinding KeyB { get { return new DeviceBinding { Kind = DeviceKind.KeyboardB, Pad = -1 }; } }
        public static DeviceBinding PadSlot(int n) { return new DeviceBinding { Kind = DeviceKind.Pad, Pad = n }; }

        public bool IsKeyboard { get { return Kind != DeviceKind.Pad; } }
        public string Label
        {
            get
            {
                if (Kind == DeviceKind.KeyboardA) return "⌨ Teclado (WASD)";
                if (Kind == DeviceKind.KeyboardB) return "⌨ Teclado (flechas)";
                return "🎮 Mando " + (Pad + 1);
            }
        }

        public bool Equals(DeviceBinding o) { return Kind == o.Kind && (Kind != DeviceKind.Pad || Pad == o.Pad); }
        public override bool Equals(object o) { return o is DeviceBinding && Equals((DeviceBinding)o); }
        public override int GetHashCode() { return (int)Kind * 10 + (Kind == DeviceKind.Pad ? Pad : 0); }
        public override string ToString() { return Label; }
    }

    // Esquema de control: qué teclas o botones son de un dispositivo
    public sealed class ControlScheme
    {
        public string Label, AttackKey, InteractKey, AbilityKey, JumpKey, MoveKeys;
        public string[] Up, Down, Left, Right, Attack, Interact, Ability, Jump;
        public int Pad = -1;   // hueco de mando cuyo stick mueve (−1: ninguno)
        // Navegación de menús: dirección de teclado o cruceta/stick del mando
        public string[] MenuUp, MenuDown, MenuLeft, MenuRight;
        // Unirse a la sala o a la partida desde este dispositivo
        public string[] Join;

        public ControlScheme Init()
        {
            if (Pad >= 0)
            {
                string gp = "GP" + Pad + ":";
                MenuUp = new[] { gp + "UP" }; MenuDown = new[] { gp + "DOWN" };
                MenuLeft = new[] { gp + "LEFT" }; MenuRight = new[] { gp + "RIGHT" };
                Join = new[] { gp + "A", gp + "START" };
            }
            else
            {
                MenuUp = Up; MenuDown = Down; MenuLeft = Left; MenuRight = Right;
                if (Join == null) Join = Attack;
            }
            return this;
        }
    }

    public static class ControlSchemes
    {
        public const int MaxPlayers = 4, MaxPads = 4;

        // Teclado A (J1): WASD mover · ESPACIO saltar · X atacar · Z habilidad · E recoger
        public static readonly ControlScheme KeyboardA = new ControlScheme
        {
            Label = "Teclado (WASD)", AttackKey = "X", InteractKey = "E", AbilityKey = "Z", JumpKey = "ESPACIO", MoveKeys = "WASD",
            Up = new[] { "KeyW" }, Down = new[] { "KeyS" }, Left = new[] { "KeyA" }, Right = new[] { "KeyD" },
            Attack = new[] { "KeyX" }, Interact = new[] { "KeyE" }, Ability = new[] { "KeyZ" }, Jump = new[] { "Space" },
            Join = new[] { "KeyX", "Space" },
        }.Init();

        // Teclado B (J2): flechas + Enter + Shift derecho (o IJKL + O + U)
        public static readonly ControlScheme KeyboardB = new ControlScheme
        {
            Label = "Teclado (flechas)", AttackKey = "ENTER", InteractKey = "SHIFT", AbilityKey = "CTRL der.", JumpKey = "Num3", MoveKeys = "FLECHAS",
            Up = new[] { "ArrowUp", "KeyI" }, Down = new[] { "ArrowDown", "KeyK" }, Left = new[] { "ArrowLeft", "KeyJ" }, Right = new[] { "ArrowRight", "KeyL" },
            Attack = new[] { "Enter", "NumpadEnter", "Numpad0", "KeyO" }, Interact = new[] { "ShiftRight", "Numpad1", "KeyU" },
            Ability = new[] { "ControlRight", "Numpad2", "KeyH" }, Jump = new[] { "Numpad3", "Slash", "KeyY" },
        }.Init();

        static readonly ControlScheme[] pads = BuildPads();

        static ControlScheme[] BuildPads()
        {
            var r = new ControlScheme[MaxPads];
            for (int n = 0; n < MaxPads; n++)
            {
                string gp = "GP" + n + ":";
                r[n] = new ControlScheme
                {
                    Label = "Mando " + (n + 1), AttackKey = "A", InteractKey = "X", AbilityKey = "Y", JumpKey = "B", MoveKeys = "Stick",
                    Up = new string[0], Down = new string[0], Left = new string[0], Right = new string[0],
                    Attack = new[] { gp + "A" }, Interact = new[] { gp + "X" }, Ability = new[] { gp + "Y", gp + "RB" }, Jump = new[] { gp + "B", gp + "LB" },
                    Pad = n,
                }.Init();
            }
            return r;
        }

        public static ControlScheme Pad(int n) { return pads[Mathf.Clamp(n, 0, MaxPads - 1)]; }

        public static ControlScheme For(DeviceBinding b)
        {
            if (b.Kind == DeviceKind.KeyboardA) return KeyboardA;
            if (b.Kind == DeviceKind.KeyboardB) return KeyboardB;
            return Pad(b.Pad);
        }

        // Dispositivo por defecto de cada hueco: J1 teclado izquierdo, J2 teclado derecho, J3 y J4 mandos 1 y 2
        public static DeviceBinding DefaultFor(int i)
        {
            if (i == 0) return DeviceBinding.KeyA;
            if (i == 1) return DeviceBinding.KeyB;
            return DeviceBinding.PadSlot(i - 2);
        }

        // Todos los dispositivos posibles, en el orden en que se recorren al pulsar "cambiar control"
        public static DeviceBinding[] AllDevices()
        {
            var r = new DeviceBinding[2 + MaxPads];
            r[0] = DeviceBinding.KeyA; r[1] = DeviceBinding.KeyB;
            for (int n = 0; n < MaxPads; n++) r[2 + n] = DeviceBinding.PadSlot(n);
            return r;
        }

        // Texto corto de controles para un dispositivo (paneles, sala)
        public static string Help(ControlScheme s)
        {
            return s.MoveKeys + " · " + s.AttackKey + " atacar · " + s.JumpKey + " saltar";
        }
    }

    // Controles de un jugador sobre el InputHub compartido (el dispositivo se puede cambiar en la sala)
    public sealed class PlayerControls
    {
        public readonly InputHub Input;
        public ControlScheme S { get; private set; }
        public DeviceBinding Binding { get; private set; }
        // En solitario con el teclado izquierdo, el J1 también puede moverse con las flechas
        public ControlScheme Alt;

        public PlayerControls(InputHub input, DeviceBinding binding) { Input = input; Bind(binding); }
        public PlayerControls(InputHub input, ControlScheme scheme) { Input = input; S = scheme; Binding = scheme.Pad >= 0 ? DeviceBinding.PadSlot(scheme.Pad) : scheme == ControlSchemes.KeyboardB ? DeviceBinding.KeyB : DeviceBinding.KeyA; }

        public void Bind(DeviceBinding b) { Binding = b; S = ControlSchemes.For(b); }

        bool Dir(string[] keys, string[] alt) { return Input.AnyDown(keys) || (alt != null && Input.AnyDown(alt)); }

        public Vector2 Axis()
        {
            var a = Alt;
            float x = (Dir(S.Right, a != null ? a.Right : null) ? 1f : 0f) - (Dir(S.Left, a != null ? a.Left : null) ? 1f : 0f);
            float y = (Dir(S.Up, a != null ? a.Up : null) ? 1f : 0f) - (Dir(S.Down, a != null ? a.Down : null) ? 1f : 0f);
            if (S.Pad >= 0)
            {
                var pad = Input.Pads[S.Pad];
                if (pad.X != 0f || pad.Y != 0f) { x = pad.X; y = pad.Y; }
            }
            return new Vector2(x, y);
        }

        public bool AttackPressed() { return Input.AnyPressed(S.Attack); }
        public bool AttackDown() { return Input.AnyDown(S.Attack); }
        public bool InteractPressed() { return Input.AnyPressed(S.Interact); }
        public bool InteractDown() { return Input.AnyDown(S.Interact); }
        public bool ConsumeInteract() { return Input.ConsumeAny(S.Interact); }
        public bool ConsumeAttack() { return Input.ConsumeAny(S.Attack); }
        public bool AbilityPressed() { return Input.AnyPressed(S.Ability); }
        public bool ConsumeAbility() { return Input.ConsumeAny(S.Ability); }
        public bool JumpPressed() { return Input.AnyPressed(S.Jump); }
        public bool ConsumeJump() { return Input.ConsumeAny(S.Jump); }
        // Confirmar en menús y pantallas: atacar o saltar
        public bool ConsumeConfirm() { bool a = ConsumeAttack(); bool j = ConsumeJump(); return a || j; }

        // Navegación de menús (sala de preparación): consume la pulsación de una dirección
        public bool MenuPressed(int dir)
        {
            switch (dir)
            {
                case 0: return Input.ConsumeAny(S.MenuUp);
                case 1: return Input.ConsumeAny(S.MenuDown);
                case 2: return Input.ConsumeAny(S.MenuLeft);
                default: return Input.ConsumeAny(S.MenuRight);
            }
        }
    }
}
