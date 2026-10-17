using UnityEngine;

namespace Delvefolk
{
    // Esquema de control de un jugador: qué teclas/mando le pertenecen
    public sealed class ControlScheme
    {
        public string Label, AttackKey, InteractKey, AbilityKey, JumpKey;
        public string[] Up, Down, Left, Right, Attack, Interact, Ability, Jump;
        public int Pad;
        // Navegación de menús: dirección de teclado + la del mando
        public string[] MenuUp, MenuDown, MenuLeft, MenuRight;

        public ControlScheme Init()
        {
            string gp = "GP" + Pad + ":";
            MenuUp = Concat(Up, gp + "UP"); MenuDown = Concat(Down, gp + "DOWN");
            MenuLeft = Concat(Left, gp + "LEFT"); MenuRight = Concat(Right, gp + "RIGHT");
            return this;
        }

        static string[] Concat(string[] a, string b)
        {
            var r = new string[a.Length + 1];
            a.CopyTo(r, 0); r[a.Length] = b;
            return r;
        }
    }

    public static class ControlSchemes
    {
        public static readonly ControlScheme[] All =
        {
            // Jugador 1: WASD + Espacio + E (o mando 1)
            new ControlScheme
            {
                Label = "J1", AttackKey = "ESPACIO", InteractKey = "E", AbilityKey = "Q", JumpKey = "SHIFT izq.",
                Up = new[] { "KeyW" }, Down = new[] { "KeyS" }, Left = new[] { "KeyA" }, Right = new[] { "KeyD" },
                Attack = new[] { "Space", "GP0:A" }, Interact = new[] { "KeyE", "GP0:X" }, Ability = new[] { "KeyQ", "GP0:Y", "GP0:RB" },
                Jump = new[] { "ShiftLeft", "KeyF", "GP0:B", "GP0:LB" }, Pad = 0,
            }.Init(),
            // Jugador 2: flechas + Enter + Shift derecho (o IJKL + O + U, o mando 2)
            new ControlScheme
            {
                Label = "J2", AttackKey = "ENTER", InteractKey = "SHIFT", AbilityKey = "CTRL der.", JumpKey = "Num3",
                Up = new[] { "ArrowUp", "KeyI" }, Down = new[] { "ArrowDown", "KeyK" }, Left = new[] { "ArrowLeft", "KeyJ" }, Right = new[] { "ArrowRight", "KeyL" },
                Attack = new[] { "Enter", "NumpadEnter", "Numpad0", "KeyO", "GP1:A" }, Interact = new[] { "ShiftRight", "Numpad1", "KeyU", "GP1:X" },
                Ability = new[] { "ControlRight", "Numpad2", "KeyH", "GP1:Y", "GP1:RB" },
                Jump = new[] { "Numpad3", "Slash", "KeyY", "GP1:B", "GP1:LB" }, Pad = 1,
            }.Init(),
        };
    }

    // Controles de un jugador sobre el InputHub compartido
    public sealed class PlayerControls
    {
        public readonly InputHub Input;
        public readonly ControlScheme S;
        // En solitario el J1 también puede moverse con las flechas (esquema alternativo)
        public ControlScheme Alt;

        public PlayerControls(InputHub input, ControlScheme scheme) { Input = input; S = scheme; }

        bool Dir(string[] keys, string[] alt) { return Input.AnyDown(keys) || (alt != null && Input.AnyDown(alt)); }

        public Vector2 Axis()
        {
            var a = Alt;
            float x = (Dir(S.Right, a != null ? a.Right : null) ? 1f : 0f) - (Dir(S.Left, a != null ? a.Left : null) ? 1f : 0f);
            float y = (Dir(S.Up, a != null ? a.Up : null) ? 1f : 0f) - (Dir(S.Down, a != null ? a.Down : null) ? 1f : 0f);
            var pad = Input.Pads[S.Pad];
            // El stick del mando, si se mueve, sustituye al teclado
            if (pad.Connected && (pad.X != 0f || pad.Y != 0f)) { x = pad.X; y = pad.Y; }
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
