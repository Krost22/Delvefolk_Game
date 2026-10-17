using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Delvefolk
{
    // Estado de un mando ya interpretado (zona muerta, cruceta como stick, Y hacia arriba positiva)
    public sealed class PadState
    {
        public bool Connected;
        public float X, Y;
        public readonly bool[] Buttons = new bool[InputHub.PadButtonNames.Length];
    }

    // Teclado + mandos. Guarda teclas mantenidas (down) y pulsaciones de este frame (pressed).
    // Cada tecla y botón es un código virtual ("KeyE", "Space", "GP0:A") para tratarlos igual.
    // Las pruebas inyectan la entrada con Press/Hold/Release y desactivan la lectura de dispositivos.
    public sealed class InputHub
    {
        public static readonly string[] PadButtonNames = { "A", "B", "X", "Y", "LB", "RB", "START", "UP", "DOWN", "LEFT", "RIGHT" };
        const int BA = 0, BB = 1, BX = 2, BY = 3, BLB = 4, BRB = 5, BSTART = 6, BUP = 7, BDOWN = 8, BLEFT = 9, BRIGHT = 10;

        // Códigos con el mismo nombre que KeyboardEvent.code del original (posición física de la tecla)
        static readonly KeyValuePair<Key, string>[] KeyMap = BuildKeyMap();
        static readonly string[][] PadCodes = { BuildPadCodes(0), BuildPadCodes(1) };

        readonly HashSet<string> down = new HashSet<string>();
        readonly HashSet<string> pressed = new HashSet<string>();
        public readonly PadState[] Pads = { new PadState(), new PadState() };

        // false en las pruebas: solo cuenta la entrada inyectada
        public bool ReadDevices = true;
        // Rueda del ratón en este frame (positiva = hacia arriba, alejándose del usuario)
        public float Wheel;
        public event Action<int> PadDisconnected;

        static KeyValuePair<Key, string>[] BuildKeyMap()
        {
            var list = new List<KeyValuePair<Key, string>>();
            for (Key k = Key.A; k <= Key.Z; k++) list.Add(new KeyValuePair<Key, string>(k, "Key" + k.ToString()));
            Key[] digits = { Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
            for (int i = 0; i < digits.Length; i++) list.Add(new KeyValuePair<Key, string>(digits[i], "Digit" + i));
            Key[] numpad = { Key.Numpad0, Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9 };
            for (int i = 0; i < numpad.Length; i++) list.Add(new KeyValuePair<Key, string>(numpad[i], "Numpad" + i));
            void Add(Key k, string code) { list.Add(new KeyValuePair<Key, string>(k, code)); }
            Add(Key.Space, "Space"); Add(Key.Enter, "Enter"); Add(Key.NumpadEnter, "NumpadEnter");
            Add(Key.LeftShift, "ShiftLeft"); Add(Key.RightShift, "ShiftRight");
            Add(Key.LeftCtrl, "ControlLeft"); Add(Key.RightCtrl, "ControlRight");
            Add(Key.LeftAlt, "AltLeft"); Add(Key.RightAlt, "AltRight");
            Add(Key.UpArrow, "ArrowUp"); Add(Key.DownArrow, "ArrowDown"); Add(Key.LeftArrow, "ArrowLeft"); Add(Key.RightArrow, "ArrowRight");
            Add(Key.Escape, "Escape"); Add(Key.Tab, "Tab"); Add(Key.Backspace, "Backspace");
            Add(Key.Slash, "Slash"); Add(Key.Minus, "Minus"); Add(Key.Period, "Period"); Add(Key.Comma, "Comma");
            Add(Key.NumpadPlus, "NumpadAdd"); Add(Key.NumpadMinus, "NumpadSubtract");
            return list.ToArray();
        }

        static string[] BuildPadCodes(int pad)
        {
            var c = new string[PadButtonNames.Length];
            for (int i = 0; i < c.Length; i++) c[i] = "GP" + pad + ":" + PadButtonNames[i];
            return c;
        }

        // Suelta todo, también los ejes y botones de los mandos (se vuelven a leer en el siguiente Poll)
        public void Clear()
        {
            down.Clear(); pressed.Clear();
            foreach (var p in Pads)
            {
                p.X = 0f; p.Y = 0f;
                for (int b = 0; b < p.Buttons.Length; b++) p.Buttons[b] = false;
            }
        }
        public bool IsDown(string code) { return down.Contains(code); }
        public bool WasPressed(string code) { return pressed.Contains(code); }
        public bool Consume(string code) { return pressed.Remove(code); }

        public bool AnyDown(string[] codes)
        {
            for (int i = 0; i < codes.Length; i++) if (down.Contains(codes[i])) return true;
            return false;
        }

        public bool AnyPressed(string[] codes)
        {
            for (int i = 0; i < codes.Length; i++) if (pressed.Contains(codes[i])) return true;
            return false;
        }

        public bool ConsumeAny(string[] codes)
        {
            bool had = false;
            for (int i = 0; i < codes.Length; i++) if (pressed.Remove(codes[i])) had = true;
            return had;
        }

        public void EndFrame() { pressed.Clear(); Wheel = 0f; }

        // ---------- Inyección (pruebas) ----------
        public void Press(string code) { pressed.Add(code); }
        public void Hold(string code) { down.Add(code); }
        public void Release(string code) { down.Remove(code); }
        // Pulsación completa: cuenta como pulsada este frame y mantenida hasta Release
        public void Tap(string code) { pressed.Add(code); down.Add(code); }

        // Lee teclado, ratón y mandos (una vez por frame)
        public void Poll()
        {
            if (!ReadDevices) return;
            var kb = Keyboard.current;
            if (kb != null)
            {
                for (int i = 0; i < KeyMap.Length; i++)
                {
                    KeyControl ctrl = kb[KeyMap[i].Key];
                    if (ctrl == null) continue;
                    string code = KeyMap[i].Value;
                    bool isDown = ctrl.isPressed;
                    if (ctrl.wasPressedThisFrame || (isDown && !down.Contains(code))) pressed.Add(code);
                    if (isDown) down.Add(code); else down.Remove(code);
                }
            }
            var mouse = Mouse.current;
            if (mouse != null) Wheel += mouse.scroll.ReadValue().y;
            PollPads();
        }

        void PollPads()
        {
            var all = Gamepad.all;
            for (int i = 0; i < 2; i++)
            {
                Gamepad gp = i < all.Count ? all[i] : null;
                PadState cur = Pads[i];
                bool wasConnected = cur.Connected;
                string[] codes = PadCodes[i];
                if (gp != null)
                {
                    cur.Connected = true;
                    Vector2 st = gp.leftStick.ReadValue();
                    float x = st.x, y = st.y;
                    if (Mathf.Sqrt(x * x + y * y) < 0.25f) { x = 0; y = 0; }
                    if (gp.dpad.up.isPressed) y = 1; if (gp.dpad.down.isPressed) y = -1;
                    if (gp.dpad.left.isPressed) x = -1; if (gp.dpad.right.isPressed) x = 1;
                    cur.X = x; cur.Y = y;
                    SetButton(cur, codes, BA, gp.buttonSouth.isPressed);
                    SetButton(cur, codes, BB, gp.buttonEast.isPressed);
                    SetButton(cur, codes, BX, gp.buttonWest.isPressed);
                    SetButton(cur, codes, BY, gp.buttonNorth.isPressed);
                    SetButton(cur, codes, BLB, gp.leftShoulder.isPressed);
                    SetButton(cur, codes, BRB, gp.rightShoulder.isPressed);
                    SetButton(cur, codes, BSTART, gp.startButton.isPressed);
                    // Cruceta y stick como pulsaciones virtuales (para menús)
                    SetButton(cur, codes, BUP, cur.Y > 0.6f);
                    SetButton(cur, codes, BDOWN, cur.Y < -0.6f);
                    SetButton(cur, codes, BLEFT, x < -0.6f);
                    SetButton(cur, codes, BRIGHT, x > 0.6f);
                }
                else
                {
                    cur.Connected = false; cur.X = 0; cur.Y = 0;
                    for (int b = 0; b < cur.Buttons.Length; b++) { cur.Buttons[b] = false; down.Remove(codes[b]); }
                }
                // Un mando que estaba conectado desaparece: se avisa al juego (pausa automática)
                if (wasConnected && !cur.Connected && PadDisconnected != null) PadDisconnected(i);
            }
        }

        void SetButton(PadState pad, string[] codes, int b, bool on)
        {
            if (on)
            {
                if (!pad.Buttons[b]) pressed.Add(codes[b]);
                down.Add(codes[b]);
            }
            else down.Remove(codes[b]);
            pad.Buttons[b] = on;
        }
    }
}
