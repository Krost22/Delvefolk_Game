using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Delvefolk
{
    // Sala de preparación (LobbyScreen.js): cada jugador elige UNA habilidad especial y su aspecto, y marca "listo".
    // Controles de cada jugador (sus teclas o su mando): arriba/abajo fila · izquierda/derecha cambiar ·
    // ATACAR listo / no listo · INTERACTUAR (J2) salir de la sala. J2 se une con su ATACAR. Esc vuelve al título.
    // Ajustes del anfitrión (J1): pisos bonus (B), habilidades exclusivas (X), intercambio con aliados (C).
    // La partida empieza sola cuando todos los presentes están listos (cuenta atrás de 3 s, cancelable).
    public sealed class LobbyScreen
    {
        const float Px = Hud.Px;
        static readonly string[] RowIds = { "ability", "head", "body", "acc", "palette", "material", "fx", "ready" };
        static readonly string[] RowNames = { "Habilidad", "Cabeza", "Cuerpo", "Accesorio", "Colores", "Material", "Efecto", "Listo" };

        sealed class RowUi
        {
            public RectTransform R; public Image Sel; public TextMeshProUGUI Value;
        }

        sealed class Slot
        {
            public int I; public bool Joined, Ready, Pad; public int Row; public string Note = ""; public float NoteT;
            public Image Border, Bg; public TextMeshProUGUI Name, Conn;
            public RectTransform JoinBox, Body;
            public RawImage PreviewImg; public CharacterPreview Preview;
            public RowUi[] Rows; public Button ReadyBtn; public TextMeshProUGUI ReadyText; public Image ReadyFace;
            public TextMeshProUGUI CardIcon, CardName, CardShort, Pros, Cons, Allies, NoteText;
        }

        readonly Game g;
        readonly Hud ui;
        readonly RectTransform root;
        readonly Slot[] slots = new Slot[2];
        readonly TextMeshProUGUI status;
        readonly Button startBtn;
        readonly Dictionary<string, TextMeshProUGUI> settingTexts = new Dictionary<string, TextMeshProUGUI>();
        MatchSettings settings;
        string statusShown;
        public float? Countdown { get; private set; }
        public bool IsOpen { get; private set; }

        public LobbyScreen(Game game)
        {
            g = game;
            ui = game.Ui;
            root = ui.Overlay("Sala", 0.88f);
            var box = Hud.NewRect(root, "Sala");
            float W = 1040 * Px;
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 1f);
            box.pivot = new Vector2(0.5f, 1f);
            box.anchoredPosition = new Vector2(0, -14 * Px);
            box.sizeDelta = new Vector2(W, 660 * Px);
            var h2 = ui.Title(box, "Sala de preparación", 28, 0);
            h2.alignment = TextAlignmentOptions.Center;
            var sub = ui.Body(box, "Cada aventurero elige <b>una</b> habilidad especial y su aspecto. El aspecto es solo estético.", 13, 44 * Px, 20 * Px, MathUtil.Hex(0xc9b48d));
            sub.alignment = TextAlignmentOptions.Center;
            float slotW = (1040f - 14f) / 2f * Px;
            for (int i = 0; i < 2; i++) slots[i] = BuildSlot(box, i, i * (slotW + 14 * Px), 72 * Px, slotW);

            // Pie: ajustes del anfitrión, estado y botones
            float fy = 72 * Px + 446 * Px;
            var host = Hud.MakeText(box, "Anfitrion", "Anfitrión (J1):", 13, Fonts.Body, MathUtil.Hex(0xb39a72));
            Hud.PlaceIn(host.rectTransform, 140 * Px, fy + 6 * Px, 110 * Px, 20 * Px);
            host.alignment = TextAlignmentOptions.Right;
            SettingButton(box, "bonusMode", "Pisos bonus", "B", 260 * Px, fy, 196 * Px);
            SettingButton(box, "exclusiveAbilities", "Habilidades exclusivas", "X", 462 * Px, fy, 226 * Px);
            SettingButton(box, "swapAllies", "Intercambio con aliados", "C", 694 * Px, fy, 230 * Px);
            status = Hud.MakeText(box, "Estado", "", 15, Fonts.Body, MathUtil.Hex(0xffe2a0));
            status.fontStyle = FontStyles.Bold;
            Hud.PlaceIn(status.rectTransform, 0, fy + 40 * Px, W, 22 * Px);
            ui.MakeButton(box, "Volver <size=70%>(Esc)</size>", fy + 70 * Px, () => g.BackToTitle(), -120 * Px);
            startBtn = ui.MakeButton(box, "¡A la mazmorra!", fy + 70 * Px, () => { if (AllReady()) Begin(); }, 120 * Px);
            root.gameObject.SetActive(false);
        }

        // ---------- Construcción de un hueco ----------
        Slot BuildSlot(RectTransform parent, int i, float x, float y, float w)
        {
            var s = new Slot { I = i };
            var look = Player.Looks[i];
            var scheme = ControlSchemes.All[i];
            var pc = MathUtil.Hex(look.Ring);
            s.Border = Hud.MakeImage(parent, "Hueco" + (i + 1), pc);
            var r = s.Border.rectTransform;
            Hud.PlaceIn(r, x, y, w, 440 * Px);
            s.Bg = Hud.MakeImage(r, "Fondo", new Color(27 / 255f, 19 / 255f, 13 / 255f, 1f));
            Hud.Stretch(s.Bg.rectTransform, 3 * Px);
            var c = s.Bg.rectTransform;
            float iw = w - 6 * Px - 20 * Px;
            s.Name = Hud.MakeText(c, "Nombre", look.Name, 22, Fonts.Body, pc);
            s.Name.fontStyle = FontStyles.Bold; s.Name.characterSpacing = 8f; s.Name.alignment = TextAlignmentOptions.Left;
            Hud.Outline(s.Name, Color.black, 0.2f);
            Hud.PlaceIn(s.Name.rectTransform, 10 * Px, 6 * Px, iw * 0.5f, 30 * Px);
            s.Conn = Hud.MakeText(c, "Dispositivo", "", 12, Fonts.Body, MathUtil.Hex(0xc9b48d));
            s.Conn.alignment = TextAlignmentOptions.Right;
            Hud.PlaceIn(s.Conn.rectTransform, 10 * Px + iw * 0.5f, 12 * Px, iw * 0.5f, 20 * Px);

            // Sin unir: "Pulsa ENTER para unirte" + botón
            s.JoinBox = Hud.NewRect(c, "Unirse");
            Hud.PlaceIn(s.JoinBox, 0, 40 * Px, w - 6 * Px, 380 * Px);
            var jt = Hud.MakeText(s.JoinBox, "Texto", "Pulsa " + Hud.Key(scheme.AttackKey) + " para unirte", 16, Fonts.Body, Hud.Ink);
            jt.richText = true;
            Hud.PlaceIn(jt.rectTransform, 0, 110 * Px, w - 6 * Px, 30 * Px);
            ui.MakeButton(s.JoinBox, "Unirse", 150 * Px, () => Join(i));

            // Unido: vista previa, filas, ficha de la habilidad, nota, teclas y salir
            s.Body = Hud.NewRect(c, "Cuerpo");
            Hud.PlaceIn(s.Body, 0, 40 * Px, w - 6 * Px, 400 * Px);
            var pvBg = Hud.MakeImage(s.Body, "FondoVista", MathUtil.Hex(0x24190f));
            Hud.PlaceIn(pvBg.rectTransform, 10 * Px, 0, 150 * Px, 180 * Px);
            s.PreviewImg = new GameObject("Vista", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            s.PreviewImg.transform.SetParent(pvBg.transform, false);
            Hud.Stretch(s.PreviewImg.rectTransform);
            s.PreviewImg.raycastTarget = false;
            float rx = 170 * Px, rw = iw - 160 * Px;
            s.Rows = new RowUi[RowIds.Length];
            for (int k = 0; k < RowIds.Length; k++) s.Rows[k] = BuildRow(s, k, rx, k * 23 * Px, rw);
            // Ficha de la habilidad
            var card = Hud.MakeImage(s.Body, "Ficha", new Color(0, 0, 0, 0.35f));
            Hud.PlaceIn(card.rectTransform, 10 * Px, 190 * Px, iw, 146 * Px);
            var cr = card.rectTransform;
            float cw = iw - 18 * Px;
            s.CardIcon = Hud.MakeText(cr, "Icono", "", 28, Fonts.Body, Color.white);
            Hud.PlaceIn(s.CardIcon.rectTransform, 9 * Px, 4 * Px, 40 * Px, 36 * Px);
            s.CardName = Hud.MakeText(cr, "Nombre", "", 12, Fonts.Body, Hud.Ink);
            s.CardName.richText = true; s.CardName.alignment = TextAlignmentOptions.Left;
            Hud.PlaceIn(s.CardName.rectTransform, 52 * Px, 5 * Px, cw - 44 * Px, 34 * Px);
            s.CardShort = Hud.MakeText(cr, "Resumen", "", 12, Fonts.Body, Hud.Ink);
            s.CardShort.textWrappingMode = TextWrappingModes.Normal; s.CardShort.alignment = TextAlignmentOptions.TopLeft;
            Hud.PlaceIn(s.CardShort.rectTransform, 9 * Px, 40 * Px, cw, 32 * Px);
            s.Pros = Hud.MakeText(cr, "Ventajas", "", 12, Fonts.Body, Hud.Ink);
            s.Cons = Hud.MakeText(cr, "Desventajas", "", 12, Fonts.Body, Hud.Ink);
            foreach (var t in new[] { s.Pros, s.Cons })
            {
                t.richText = true; t.textWrappingMode = TextWrappingModes.Normal; t.alignment = TextAlignmentOptions.TopLeft;
            }
            Hud.PlaceIn(s.Pros.rectTransform, 9 * Px, 72 * Px, cw / 2f - 6 * Px, 50 * Px);
            Hud.PlaceIn(s.Cons.rectTransform, 9 * Px + cw / 2f + 6 * Px, 72 * Px, cw / 2f - 6 * Px, 50 * Px);
            s.Allies = Hud.MakeText(cr, "Companeros", "", 12, Fonts.Body, MathUtil.Hex(0xc9b48d));
            s.Allies.textWrappingMode = TextWrappingModes.Normal; s.Allies.alignment = TextAlignmentOptions.TopLeft;
            Hud.PlaceIn(s.Allies.rectTransform, 9 * Px, 120 * Px, cw, 18 * Px);
            s.NoteText = Hud.MakeText(s.Body, "Nota", "", 12, Fonts.Body, MathUtil.Hex(0xff9a2a));
            s.NoteText.alignment = TextAlignmentOptions.Left;
            Hud.PlaceIn(s.NoteText.rectTransform, 10 * Px, 338 * Px, iw, 16 * Px);
            var keys = Hud.MakeText(s.Body, "Teclas", i == 1 ? "FLECHAS elegir · ENTER listo · SHIFT salir" : "WASD elegir · ESPACIO listo", 11, Fonts.Body, MathUtil.Hex(0x8a7a62));
            keys.alignment = TextAlignmentOptions.Left;
            Hud.PlaceIn(keys.rectTransform, 10 * Px, 352 * Px, iw, 16 * Px);
            if (i == 1)
            {
                var leave = SmallButton(s.Body, "Salir de la sala", 10 * Px, 370 * Px, 130 * Px, 24 * Px, () => Leave(1), 12);
                leave.GetComponent<Image>().color = MathUtil.Hex(0x6a4a2a);
                Hud.ButtonText(leave).color = Hud.Ink;
            }
            s.Preview = new CharacterPreview(g, i, Mathf.RoundToInt(150 * Px * 2), Mathf.RoundToInt(180 * Px * 2));
            s.PreviewImg.texture = s.Preview.Texture;
            return s;
        }

        RowUi BuildRow(Slot s, int k, float x, float y, float w)
        {
            var row = new RowUi();
            row.Sel = Hud.MakeImage(s.Body, "Fila" + k, new Color(1, 1, 1, 0f));
            row.R = row.Sel.rectTransform;
            Hud.PlaceIn(row.R, x, y, w, 22 * Px);
            int i = s.I;
            if (RowIds[k] == "ready")
            {
                s.ReadyBtn = SmallButton(row.R, "Marcar como listo", 2 * Px, 0, w - 4 * Px, 22 * Px, () => { s.Row = k; ToggleReady(i); }, 12);
                s.ReadyText = Hud.ButtonText(s.ReadyBtn);
                s.ReadyFace = s.ReadyBtn.GetComponent<Image>();
                return row;
            }
            var name = Hud.MakeText(row.R, "Nombre", RowNames[k].ToUpperInvariant(), 11, Fonts.Body, MathUtil.Hex(0xb39a72));
            name.fontStyle = FontStyles.Bold; name.alignment = TextAlignmentOptions.Left; name.characterSpacing = 4f;
            Hud.PlaceIn(name.rectTransform, 4 * Px, 0, 74 * Px, 22 * Px);
            SmallButton(row.R, "◀", 80 * Px, 1 * Px, 24 * Px, 20 * Px, () => { s.Row = k; Change(i, -1); }, 12);
            SmallButton(row.R, "▶", w - 26 * Px, 1 * Px, 24 * Px, 20 * Px, () => { s.Row = k; Change(i, 1); }, 12);
            row.Value = Hud.MakeText(row.R, "Valor", "", k == 0 ? 14 : 13, Fonts.Body, MathUtil.Hex(k == 0 ? 0xffcf6a : 0xffe2a0));
            row.Value.fontStyle = FontStyles.Bold;
            row.Value.overflowMode = TextOverflowModes.Ellipsis;
            Hud.PlaceIn(row.Value.rectTransform, 108 * Px, 0, w - 138 * Px, 22 * Px);
            return row;
        }

        // Botoncito dorado de tamaño fijo (flechas de las filas, listo, salir)
        Button SmallButton(RectTransform parent, string label, float x, float y, float w, float h, System.Action onClick, float px)
        {
            var img = Hud.MakeImage(parent, "Boton", Hud.Gold);
            img.raycastTarget = true;
            Hud.PlaceIn(img.rectTransform, x, y, w, h);
            var b = img.gameObject.AddComponent<Button>();
            b.onClick.AddListener(() => onClick());
            var t = Hud.MakeText(img.rectTransform, "Texto", label, px, Fonts.Body, MathUtil.Hex(0x1a120b));
            t.fontStyle = FontStyles.Bold;
            Hud.Stretch(t.rectTransform);
            return b;
        }

        void SettingButton(RectTransform parent, string key, string label, string keyHint, float x, float y, float w)
        {
            var b = SmallButton(parent, "", x, y, w, 30 * Px, () => CycleSetting(key), 13);
            b.GetComponent<Image>().color = MathUtil.Hex(0x3a2a1c);
            var t = Hud.ButtonText(b);
            t.color = Hud.Ink; t.fontStyle = FontStyles.Normal; t.richText = true;
            settingTexts[key] = t;
            t.name = label + "|" + keyHint;
        }

        // ---------- Apertura ----------
        public void Open(int numPlayers)
        {
            IsOpen = true;
            root.gameObject.SetActive(true);
            settings = g.Settings;
            foreach (var s in slots) { s.Joined = false; s.Ready = false; s.Row = 0; s.Note = ""; s.NoteT = 0f; s.NoteText.text = ""; }
            Join(0, true);
            if (numPlayers > 1) Join(1, true);
            Render(1);
            Countdown = null;
            RenderSettings();
            RenderStatus();
        }

        public void Close()
        {
            IsOpen = false;
            root.gameObject.SetActive(false);
            foreach (var s in slots) s.Preview.SetActive(false);
        }

        public void Dispose() { foreach (var s in slots) if (s != null && s.Preview != null) s.Preview.Dispose(); }

        public CharacterPreview PreviewFor(Camera cam)
        {
            foreach (var s in slots) if (s != null && s.Preview != null && s.Preview.Active && s.Preview.IsCamera(cam)) return s.Preview;
            return null;
        }

        PlayerLoadout Loadout(int i) { return g.Loadouts[i]; }
        public bool Joined(int i) { return slots[i].Joined; }
        public bool Ready(int i) { return slots[i].Ready; }
        public int Row(int i) { return slots[i].Row; }
        public string NoteOf(int i) { return slots[i].Note; }
        public CharacterPreview PreviewOf(int i) { return slots[i].Preview; }
        public string StatusText { get { return statusShown; } }
        public bool StartEnabled { get { return startBtn.interactable; } }

        List<Slot> JoinedSlots()
        {
            var l = new List<Slot>();
            foreach (var s in slots) if (s.Joined) l.Add(s);
            return l;
        }

        public bool AllReady()
        {
            var j = JoinedSlots();
            if (j.Count == 0) return false;
            foreach (var s in j) if (!s.Ready) return false;
            return true;
        }

        public void Join(int i, bool quiet = false)
        {
            var s = slots[i];
            if (s.Joined) return;
            s.Joined = true; s.Ready = false; s.Row = 0;
            // Recupera la última elección guardada de ese jugador
            g.Loadouts[i] = PlayerLoadout.Load(i);
            EnforceExclusive(i);
            s.Preview.SetActive(true);
            if (!quiet) { g.Audio.Play("join"); Note(i, "¡" + Player.Looks[i].Name + " se une!"); }
            Render(i);
        }

        // Salir de la sala: el hueco queda libre y su selección deja de contar (no bloquea habilidades exclusivas)
        public void Leave(int i)
        {
            if (i == 0) return;
            var s = slots[i];
            if (!s.Joined) return;
            s.Joined = false; s.Ready = false; s.Row = 0;
            s.Preview.SetActive(false);
            Countdown = null;
            g.Audio.Play("drop");
            Render(i);
            Render(0);
        }

        List<string> TakenAbilities(int except)
        {
            var l = new List<string>();
            foreach (var s in slots) if (s.Joined && s.I != except) l.Add(Loadout(s.I).Ability);
            return l;
        }

        // Modo de habilidades exclusivas: si dos jugadores comparten habilidad, el que no es el anfitrión cambia
        void EnforceExclusive(int i)
        {
            if (settings == null || !settings.ExclusiveAbilities) return;
            var taken = TakenAbilities(i);
            var lo = Loadout(i);
            if (!taken.Contains(lo.Ability)) return;
            foreach (var id in Abilities.Order)
            {
                if (taken.Contains(id)) continue;
                lo.SetAbility(id);
                Note(i, "Habilidad ocupada: cambiada a " + Abilities.Get(id).Name);
                break;
            }
        }

        public void Change(int i, int dir)
        {
            var s = slots[i];
            if (!s.Joined) return;
            if (s.Ready) { Note(i, "Estás listo: pulsa " + ControlSchemes.All[i].AttackKey + " para cambiar algo"); return; }
            string row = RowIds[s.Row];
            var lo = Loadout(i);
            if (row == "ready") return;
            if (row == "ability")
            {
                var ids = Abilities.Order;
                var taken = settings.ExclusiveAbilities ? TakenAbilities(i) : new List<string>();
                int k = ids.IndexOf(lo.Ability);
                for (int n = 0; n < ids.Count; n++)
                {
                    k = (k + dir + ids.Count) % ids.Count;
                    if (!taken.Contains(ids[k])) break;
                }
                lo.SetAbility(ids[k]);
            }
            else
            {
                var list = Cosmetics.List(row);
                int k = list.FindIndex(c => c.Id == lo.Cosmetics[row]);
                var next = list[(k + dir + list.Count) % list.Count];
                var removed = lo.SetCosmetic(row, next.Id);
                foreach (var r in removed) Note(i, r.Item.Name + " no cabe con " + r.BlockedBy.Name + ": se quita");
            }
            lo.Save();
            g.Audio.Play("step");
            Render(i);
        }

        public void ToggleReady(int i)
        {
            var s = slots[i];
            if (!s.Joined) return;
            s.Ready = !s.Ready;
            g.Audio.Play(s.Ready ? "pickup" : "drop");
            if (!s.Ready) Countdown = null;
            Loadout(i).Save();
            Render(i);
        }

        public void CycleSetting(string key)
        {
            var st = settings;
            if (key == "bonusMode") st.BonusMode = st.BonusMode == "normal" ? "always" : st.BonusMode == "always" ? "never" : "normal";
            if (key == "exclusiveAbilities") { st.ExclusiveAbilities = !st.ExclusiveAbilities; EnforceExclusive(1); Render(1); }
            if (key == "swapAllies") st.SwapAllies = !st.SwapAllies;
            st.Save();
            // Cambiar las reglas quita el "listo" (todos deben ver las nuevas condiciones)
            foreach (var s in slots) s.Ready = false;
            Countdown = null;
            g.Audio.Play("step");
            RenderSettings();
            foreach (var s in slots) Render(s.I);
        }

        void Note(int i, string text)
        {
            var s = slots[i];
            s.Note = text; s.NoteT = 3f;
            s.NoteText.text = text;
        }

        void Begin()
        {
            var j = JoinedSlots();
            foreach (var s in j) Loadout(s.I).Save();
            // J1 siempre está (anfitrión)
            Close();
            g.Start(Mathf.Max(1, j.Count));
        }

        // ---------- Bucle (lo llama el frame mientras el estado es la sala) ----------
        public void Update(float dt)
        {
            var input = g.Input;
            if (input.Consume("Escape")) { g.BackToTitle(); return; }
            if (input.Consume("KeyB")) CycleSetting("bonusMode");
            if (input.Consume("KeyX")) CycleSetting("exclusiveAbilities");
            if (input.Consume("KeyC")) CycleSetting("swapAllies");
            foreach (var s in slots)
            {
                var c = new PlayerControls(input, ControlSchemes.All[s.I]);
                if (!s.Joined)
                {
                    if (c.ConsumeAttack()) Join(s.I);
                    continue;
                }
                int n = RowIds.Length;
                if (c.MenuPressed(0)) { s.Row = (s.Row - 1 + n) % n; Render(s.I); }
                if (c.MenuPressed(1)) { s.Row = (s.Row + 1) % n; Render(s.I); }
                if (c.MenuPressed(2)) Change(s.I, -1);
                if (c.MenuPressed(3)) Change(s.I, 1);
                if (c.ConsumeAttack()) ToggleReady(s.I);
                if (s.I == 1 && c.ConsumeInteract()) Leave(1);
                if (s.NoteT > 0f) { s.NoteT -= dt; if (s.NoteT <= 0f) { s.Note = ""; s.NoteText.text = ""; } }
                // Si se conecta o desconecta un mando, se actualiza la etiqueta del dispositivo
                bool padNow = input.Pads[s.I].Connected;
                if (padNow != s.Pad) { s.Pad = padNow; Render(s.I); }
                if (s.Joined)
                {
                    var lo = Loadout(s.I);
                    s.Preview.Set(Player.Looks[s.I], lo.Cosmetics, lo.Ability);
                    s.Preview.Update(dt);
                }
            }
            // Todos los presentes listos → cuenta atrás de 3 s (cancelable)
            if (AllReady())
            {
                if (Countdown == null) Countdown = 3f;
                Countdown -= dt;
                if (Countdown <= 0f) { Begin(); return; }
            }
            else Countdown = null;
            RenderStatus();
        }

        // ---------- Pintado ----------
        void Render(int i)
        {
            var s = slots[i];
            var lo = Loadout(i);
            var pc = MathUtil.Hex(Player.Looks[i].Ring);
            s.Border.color = s.Joined ? (s.Ready ? Color.Lerp(pc, Color.white, 0.35f) : pc) : new Color(pc.r, pc.g, pc.b, 0.45f);
            s.JoinBox.gameObject.SetActive(!s.Joined);
            s.Body.gameObject.SetActive(s.Joined);
            string dev = g.Input.Pads[i].Connected ? "🎮 mando " + (i + 1) : "⌨ teclado";
            s.Conn.text = s.Joined ? dev + " · " + (s.Ready ? "✔ LISTO" : "eligiendo") : "libre";
            s.Conn.color = s.Ready ? MathUtil.Hex(0x7dff8a) : MathUtil.Hex(0xc9b48d);
            s.Conn.fontStyle = s.Ready ? FontStyles.Bold : FontStyles.Normal;
            if (!s.Joined) return;
            for (int k = 0; k < RowIds.Length; k++)
            {
                var row = s.Rows[k];
                bool sel = k == s.Row;
                row.Sel.color = sel ? new Color(pc.r, pc.g, pc.b, 0.22f) : new Color(1, 1, 1, 0f);
                if (RowIds[k] == "ready")
                {
                    s.ReadyText.text = s.Ready ? "✔ ¡LISTO!" : "Marcar como listo";
                    s.ReadyFace.color = s.Ready ? MathUtil.Hex(0x7dd65b) : Hud.Gold;
                    continue;
                }
                string label, icon;
                if (RowIds[k] == "ability") { var a = Abilities.Get(lo.Ability); label = a.Name; icon = a.Icon; }
                else { var it = Cosmetics.Get(lo.Cosmetics[RowIds[k]]); label = it.Name; icon = it.Icon; }
                row.Value.text = icon + " " + label;
            }
            var ab = Abilities.Get(lo.Ability);
            s.CardIcon.text = ab.Icon;
            s.CardName.text = "<b>" + ab.Name + "</b>\n<size=85%><color=#8a7a62>Enfriamiento " + ab.Cooldown + " s · tecla " + ControlSchemes.All[i].AbilityKey + "</color></size>";
            s.CardShort.text = ab.Short;
            s.Pros.text = Lines(ab.Pros, "<color=#7dd65b><b>+ </b></color>");
            s.Cons.text = Lines(ab.Cons, "<color=#ff6a5a><b>− </b></color>");
            s.Allies.text = "👥 Compañeros: " + ab.Allies;
            // La línea de compañeros va justo debajo de la lista más larga
            float listH = Mathf.Max(s.Pros.GetPreferredValues(s.Pros.text, s.Pros.rectTransform.rect.width, 0f).y,
                s.Cons.GetPreferredValues(s.Cons.text, s.Cons.rectTransform.rect.width, 0f).y);
            var ar = s.Allies.rectTransform;
            ar.anchoredPosition = new Vector2(ar.anchoredPosition.x, -(72 * Px + listH + 3 * Px));
        }

        static string Lines(string[] items, string bullet)
        {
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < items.Length; k++) { if (k > 0) sb.Append('\n'); sb.Append(bullet).Append(items[k]); }
            return sb.ToString();
        }

        void RenderSettings()
        {
            foreach (var kv in settingTexts)
            {
                var parts = kv.Value.name.Split('|');
                string v = kv.Key == "bonusMode" ? (settings.BonusMode == "always" ? "Siempre" : settings.BonusMode == "never" ? "Nunca" : "Normal")
                    : kv.Key == "exclusiveAbilities" ? (settings.ExclusiveAbilities ? "Sí" : "No") : (settings.SwapAllies ? "Sí" : "No");
                kv.Value.text = parts[0] + ": <color=#ffcf6a><b>" + v + "</b></color> <size=80%>(" + parts[1] + ")</size>";
            }
        }

        public string SettingText(string key) { return settingTexts[key].text; }

        void RenderStatus()
        {
            var j = JoinedSlots();
            int ready = 0;
            foreach (var s in j) if (s.Ready) ready++;
            string txt = Countdown != null ? "¡Todos listos! Empieza en " + Mathf.CeilToInt(Countdown.Value) + "..."
                : "Listos: " + ready + " / " + j.Count + " — la partida empieza cuando todos estén listos";
            if (txt != statusShown) { statusShown = txt; status.text = txt; }
            startBtn.interactable = AllReady();
        }
    }
}
