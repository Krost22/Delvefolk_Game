using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Delvefolk
{
    // Sala de preparación (LobbyScreen.js): hasta 4 jugadores eligen UNA habilidad especial y su aspecto, y marcan "listo".
    // Unirse: cualquier dispositivo libre pulsa su botón (teclado izquierdo X/ESPACIO, teclado derecho ENTER, mando A o START).
    // En el teclado caben dos jugadores; el resto, con mando. Cada jugador, con SUS controles:
    //   arriba/abajo fila · izquierda/derecha cambiar · ATACAR (o SALTAR) listo · HABILIDAD cambiar de dispositivo ·
    //   INTERACTUAR salir (todos menos el anfitrión). Esc vuelve al título.
    // Ajustes del anfitrión (J1): pisos bonus (B), habilidades exclusivas (V), intercambio con aliados (C).
    // La partida empieza sola cuando todos los presentes están listos (cuenta atrás de 3 s, cancelable).
    public sealed class LobbyScreen
    {
        const float Px = Hud.Px;
        const int N = ControlSchemes.MaxPlayers;
        // Cada hueco enseña solo el personaje: su modelo, su nombre (con ◀ ▶ para cambiarlo) y su habilidad
        const float SlotW = 330f, SlotH = 400f, Gap = 12f, RowH = 20f;
        const float PvW = 220f, PvH = 168f, HeroY = 174f, CardY = 200f, CardH = 60f, ReadyY = 266f;
        static readonly string[] RowIds = { "hero", "ready" };
        static readonly string[] RowNames = { "Personaje", "Listo" };

        sealed class RowUi
        {
            public RectTransform R; public Image Sel; public TextMeshProUGUI Value;
        }

        sealed class Slot
        {
            public int I; public bool Joined, Ready, PadOk = true; public int Row; public string Note = ""; public float NoteT;
            public Image Border, Bg; public TextMeshProUGUI Name, Conn, JoinText, Keys;
            public RectTransform JoinBox, Body;
            public RawImage PreviewImg; public CharacterPreview Preview;
            public RowUi[] Rows; public Button ReadyBtn; public TextMeshProUGUI ReadyText; public Image ReadyFace;
            public TextMeshProUGUI CardIcon, CardName, CardShort, Pros, Cons, NoteText;
            public Button LeaveBtn;
        }

        readonly Game g;
        readonly Hud ui;
        readonly RectTransform root;
        readonly Slot[] slots = new Slot[N];
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
            float W = (SlotW * N + Gap * (N - 1)) * Px;
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 1f);
            box.pivot = new Vector2(0.5f, 1f);
            box.anchoredPosition = new Vector2(0, -2 * Px);
            box.sizeDelta = new Vector2(W, (56 + SlotH + 130) * Px);
            var h2 = ui.Title(box, "Sala de preparación", 28, 0);
            h2.alignment = TextAlignmentOptions.Center;
            for (int i = 0; i < N; i++) slots[i] = BuildSlot(box, i, i * (SlotW + Gap) * Px, 48 * Px, SlotW * Px);

            // Pie: ajustes del anfitrión, estado y botones
            float fy = (48 + SlotH + 8) * Px;
            float sx = W / 2f - 340 * Px;
            var host = Hud.MakeText(box, "Anfitrion", "Anfitrión (J1):", 13, Fonts.Body, MathUtil.Hex(0xb39a72));
            Hud.PlaceIn(host.rectTransform, sx - 120 * Px, fy + 6 * Px, 110 * Px, 20 * Px);
            host.alignment = TextAlignmentOptions.Right;
            SettingButton(box, "bonusMode", "Pisos bonus", "B", sx, fy, 196 * Px);
            SettingButton(box, "exclusiveAbilities", "Habilidades exclusivas", "V", sx + 202 * Px, fy, 226 * Px);
            SettingButton(box, "swapAllies", "Intercambio con aliados", "C", sx + 434 * Px, fy, 230 * Px);
            status = Hud.MakeText(box, "Estado", "", 15, Fonts.Body, MathUtil.Hex(0xffe2a0));
            status.fontStyle = FontStyles.Bold;
            Hud.PlaceIn(status.rectTransform, 0, fy + 36 * Px, W, 22 * Px);
            ui.MakeButton(box, "Volver <size=70%>(Esc)</size>", fy + 62 * Px, () => g.BackToTitle(), -120 * Px);
            startBtn = ui.MakeButton(box, "¡A la mazmorra!", fy + 62 * Px, () => { if (AllReady()) Begin(); }, 120 * Px);
            root.gameObject.SetActive(false);
        }

        // ---------- Construcción de un hueco (columna) ----------
        Slot BuildSlot(RectTransform parent, int i, float x, float y, float w)
        {
            var s = new Slot { I = i };
            var look = Player.Looks[i];
            var pc = MathUtil.Hex(look.Ring);
            s.Border = Hud.MakeImage(parent, "Hueco" + (i + 1), pc);
            var r = s.Border.rectTransform;
            Hud.PlaceIn(r, x, y, w, SlotH * Px);
            s.Bg = Hud.MakeImage(r, "Fondo", new Color(27 / 255f, 19 / 255f, 13 / 255f, 1f));
            Hud.Stretch(s.Bg.rectTransform, 3 * Px);
            var c = s.Bg.rectTransform;
            float iw = w - 6 * Px - 16 * Px;
            s.Name = Hud.MakeText(c, "Nombre", look.Name, 22, Fonts.Body, pc);
            s.Name.fontStyle = FontStyles.Bold; s.Name.characterSpacing = 8f; s.Name.alignment = TextAlignmentOptions.Left;
            Hud.Outline(s.Name, Color.black, 0.2f);
            Hud.PlaceIn(s.Name.rectTransform, 8 * Px, 4 * Px, 60 * Px, 30 * Px);
            s.Conn = Hud.MakeText(c, "Dispositivo", "", 12, Fonts.Body, MathUtil.Hex(0xc9b48d));
            s.Conn.alignment = TextAlignmentOptions.Right;
            s.Conn.richText = true;
            Hud.PlaceIn(s.Conn.rectTransform, 64 * Px, 10 * Px, iw - 56 * Px, 20 * Px);

            // Sin unir: cómo unirse + botón
            s.JoinBox = Hud.NewRect(c, "Unirse");
            Hud.PlaceIn(s.JoinBox, 0, 40 * Px, w - 6 * Px, 400 * Px);
            s.JoinText = Hud.MakeText(s.JoinBox, "Texto", "", 15, Fonts.Body, Hud.Ink);
            s.JoinText.richText = true; s.JoinText.textWrappingMode = TextWrappingModes.Normal;
            Hud.PlaceIn(s.JoinText.rectTransform, 12 * Px, 120 * Px, w - 30 * Px, 60 * Px);
            ui.MakeButton(s.JoinBox, "Unirse", 190 * Px, () => JoinByClick());

            // Unido: vista previa, filas, ficha de la habilidad, nota, teclas y botones
            s.Body = Hud.NewRect(c, "Cuerpo");
            Hud.PlaceIn(s.Body, 0, 36 * Px, w - 6 * Px, (SlotH - 42) * Px);
            var pvBg = Hud.MakeImage(s.Body, "FondoVista", MathUtil.Hex(0x24190f));
            Hud.PlaceIn(pvBg.rectTransform, (w - 6 * Px) / 2f - PvW / 2f * Px, 0, PvW * Px, PvH * Px);
            s.PreviewImg = new GameObject("Vista", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            s.PreviewImg.transform.SetParent(pvBg.transform, false);
            Hud.Stretch(s.PreviewImg.rectTransform);
            s.PreviewImg.raycastTarget = false;
            s.Rows = new RowUi[RowIds.Length];
            for (int k = 0; k < RowIds.Length; k++) s.Rows[k] = BuildRow(s, k, 8 * Px, (RowIds[k] == "ready" ? ReadyY : HeroY) * Px, iw);
            // Habilidad del personaje: icono, nombre y una línea
            float cy = CardY * Px;
            var card = Hud.MakeImage(s.Body, "Ficha", new Color(0, 0, 0, 0.35f));
            Hud.PlaceIn(card.rectTransform, 8 * Px, cy, iw, CardH * Px);
            var cr = card.rectTransform;
            float cw = iw - 14 * Px;
            s.CardIcon = Hud.MakeText(cr, "Icono", "", 24, Fonts.Body, Color.white);
            Hud.PlaceIn(s.CardIcon.rectTransform, 6 * Px, 2 * Px, 34 * Px, 32 * Px);
            s.CardName = Hud.MakeText(cr, "Nombre", "", 12, Fonts.Body, Hud.Ink);
            s.CardName.richText = true; s.CardName.alignment = TextAlignmentOptions.Left;
            Hud.PlaceIn(s.CardName.rectTransform, 42 * Px, 6 * Px, cw - 36 * Px, 24 * Px);
            s.CardShort = Hud.MakeText(cr, "Resumen", "", 11, Fonts.Body, Hud.Ink);
            s.CardShort.textWrappingMode = TextWrappingModes.Normal; s.CardShort.alignment = TextAlignmentOptions.TopLeft;
            Hud.PlaceIn(s.CardShort.rectTransform, 7 * Px, 32 * Px, cw, 26 * Px);
            s.Pros = Hud.MakeText(cr, "Ventajas", "", 11, Fonts.Body, Hud.Ink);
            s.Cons = Hud.MakeText(cr, "Desventajas", "", 11, Fonts.Body, Hud.Ink);
            foreach (var t in new[] { s.Pros, s.Cons })
            {
                t.richText = true; t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis; t.alignment = TextAlignmentOptions.TopLeft;
            }
            Hud.PlaceIn(s.Pros.rectTransform, 7 * Px, 57 * Px, cw, 13 * Px);
            Hud.PlaceIn(s.Cons.rectTransform, 7 * Px, 70 * Px, cw, 13 * Px);
            foreach (var t in new[] { s.Pros, s.Cons }) t.gameObject.SetActive(false);
            float ny = (ReadyY + RowH + 6) * Px;
            s.NoteText = Hud.MakeText(s.Body, "Nota", "", 11, Fonts.Body, MathUtil.Hex(0xff9a2a));
            s.NoteText.alignment = TextAlignmentOptions.Left; s.NoteText.overflowMode = TextOverflowModes.Ellipsis;
            Hud.PlaceIn(s.NoteText.rectTransform, 8 * Px, ny, iw, 15 * Px);
            s.Keys = Hud.MakeText(s.Body, "Teclas", "", 10, Fonts.Body, MathUtil.Hex(0x8a7a62));
            s.Keys.alignment = TextAlignmentOptions.Left; s.Keys.overflowMode = TextOverflowModes.Ellipsis;
            Hud.PlaceIn(s.Keys.rectTransform, 8 * Px, ny + 14 * Px, iw, 14 * Px);
            var swap = SmallButton(s.Body, "🔁 Cambiar control", 8 * Px, ny + 29 * Px, 150 * Px, 22 * Px, () => CycleDevice(i), 12);
            swap.GetComponent<Image>().color = MathUtil.Hex(0x4a3a24);
            Hud.ButtonText(swap).color = Hud.Ink;
            if (i > 0)
            {
                s.LeaveBtn = SmallButton(s.Body, "Salir", iw - 80 * Px, ny + 29 * Px, 88 * Px, 22 * Px, () => Leave(i), 12);
                s.LeaveBtn.GetComponent<Image>().color = MathUtil.Hex(0x6a4a2a);
                Hud.ButtonText(s.LeaveBtn).color = Hud.Ink;
            }
            s.Preview = new CharacterPreview(g, i, Mathf.RoundToInt(PvW * Px * 2), Mathf.RoundToInt(PvH * Px * 2));
            s.PreviewImg.texture = s.Preview.Texture;
            return s;
        }

        RowUi BuildRow(Slot s, int k, float x, float y, float w)
        {
            var row = new RowUi();
            row.Sel = Hud.MakeImage(s.Body, "Fila" + k, new Color(1, 1, 1, 0f));
            row.R = row.Sel.rectTransform;
            Hud.PlaceIn(row.R, x, y, w, (RowH - 1) * Px);
            int i = s.I;
            if (RowIds[k] == "ready")
            {
                s.ReadyBtn = SmallButton(row.R, "Marcar como listo", 2 * Px, 0, w - 4 * Px, (RowH - 1) * Px, () => { s.Row = k; ToggleReady(i); }, 12);
                s.ReadyText = Hud.ButtonText(s.ReadyBtn);
                s.ReadyFace = s.ReadyBtn.GetComponent<Image>();
                return row;
            }
            var name = Hud.MakeText(row.R, "Nombre", RowNames[k].ToUpperInvariant(), 10, Fonts.Body, MathUtil.Hex(0xb39a72));
            name.fontStyle = FontStyles.Bold; name.alignment = TextAlignmentOptions.Left; name.characterSpacing = 3f;
            Hud.PlaceIn(name.rectTransform, 2 * Px, 0, 66 * Px, (RowH - 1) * Px);
            SmallButton(row.R, "◀", 68 * Px, 1 * Px, 22 * Px, 19 * Px, () => { s.Row = k; Change(i, -1); }, 11);
            SmallButton(row.R, "▶", w - 24 * Px, 1 * Px, 22 * Px, 19 * Px, () => { s.Row = k; Change(i, 1); }, 11);
            row.Value = Hud.MakeText(row.R, "Valor", "", k == 0 ? 13 : 12, Fonts.Body, MathUtil.Hex(k == 0 ? 0xffcf6a : 0xffe2a0));
            row.Value.fontStyle = FontStyles.Bold;
            row.Value.overflowMode = TextOverflowModes.Ellipsis;
            Hud.PlaceIn(row.Value.rectTransform, 94 * Px, 0, w - 122 * Px, (RowH - 1) * Px);
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
        // Los dispositivos de los que entran ya están en g.Bindings (el título pone el del J1 y, con 2, el del J2)
        public void Open(int numPlayers)
        {
            IsOpen = true;
            root.gameObject.SetActive(true);
            settings = g.Settings;
            foreach (var s in slots) { s.Joined = false; s.Ready = false; s.Row = 0; s.Note = ""; s.NoteT = 0f; s.NoteText.text = ""; }
            Join(0, true);
            if (numPlayers > 1) Join(1, true);
            for (int i = 0; i < N; i++) Render(i);
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
        public DeviceBinding DeviceOf(int i) { return g.Bindings[i]; }
        public string StatusText { get { return statusShown; } }
        public bool StartEnabled { get { return startBtn.interactable; } }
        public int JoinedCount { get { return JoinedSlots().Count; } }

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

        // ¿Lo usa ya algún jugador de la sala (salvo except)?
        bool DeviceInUse(DeviceBinding b, int except = -1)
        {
            foreach (var s in slots) if (s.Joined && s.I != except && g.Bindings[s.I].Equals(b)) return true;
            return false;
        }

        // Un mando solo está disponible si está conectado (los teclados, siempre)
        bool Available(DeviceBinding b) { return b.IsKeyboard || g.Input.Pads[b.Pad].Connected; }

        public void Join(int i, bool quiet = false)
        {
            var s = slots[i];
            if (s.Joined) return;
            s.Joined = true; s.Ready = false; s.Row = 0;
            // Recupera la última elección guardada de ese jugador
            g.Loadouts[i] = PlayerLoadout.Load(i);
            EnforceExclusive(i);
            s.Preview.SetActive(true);
            if (!quiet) { g.Audio.Play("join"); Note(i, "¡" + Player.Looks[i].Name + " se une! (" + g.Bindings[i].Label + ")"); }
            Render(i);
        }

        // Se une un jugador nuevo con este dispositivo, en el primer hueco libre
        public int JoinWith(DeviceBinding b)
        {
            if (DeviceInUse(b)) return -1;
            for (int i = 0; i < N; i++)
            {
                if (slots[i].Joined) continue;
                g.Bindings[i] = b;
                Join(i);
                Countdown = null;
                RenderAll();
                return i;
            }
            return -1;
        }

        // Botón "Unirse" con el ratón: el primer dispositivo libre (teclados primero, luego mandos conectados)
        void JoinByClick()
        {
            foreach (var b in ControlSchemes.AllDevices())
                if (!DeviceInUse(b) && Available(b)) { JoinWith(b); return; }
            g.Audio.Play("denied");
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
            RenderAll();
        }

        // Cambiar de dispositivo: el siguiente libre (teclado izquierdo → teclado derecho → mandos conectados)
        public void CycleDevice(int i)
        {
            var s = slots[i];
            if (!s.Joined) return;
            var all = ControlSchemes.AllDevices();
            int cur = System.Array.IndexOf(all, g.Bindings[i]);
            for (int k = 1; k < all.Length; k++)
            {
                var b = all[(cur + k + all.Length) % all.Length];
                if (DeviceInUse(b, i) || !Available(b)) continue;
                g.Bindings[i] = b;
                s.Ready = false;
                g.Audio.Play("swap");
                Note(i, "Ahora juegas con: " + b.Label);
                Render(i);
                return;
            }
            Note(i, "No hay otro control libre: conecta un mando");
            g.Audio.Play("denied");
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
            // La habilidad va con el personaje: se cambia al primer personaje con la habilidad libre
            foreach (var id in HeroCatalog.Order())
            {
                var ab = HeroCatalog.AbilityOf(id);
                if (ab == null || taken.Contains(ab)) continue;
                lo.SetHero(id);
                Note(i, "Habilidad ocupada: cambiado a " + HeroCatalog.NameOf(id));
                break;
            }
        }

        public void Change(int i, int dir)
        {
            var s = slots[i];
            if (!s.Joined) return;
            if (s.Ready) { Note(i, "Estás listo: pulsa " + ControlSchemes.For(g.Bindings[i]).AttackKey + " para cambiar algo"); return; }
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
            else if (row == "hero")
            {
                // Solo los personajes personalizados; en modo exclusivo se saltan los de habilidad ocupada
                var ids = HeroCatalog.Order();
                var taken = settings.ExclusiveAbilities ? TakenAbilities(i) : new List<string>();
                int k = ids.IndexOf(lo.Hero);
                if (k < 0) k = dir > 0 ? -1 : 0;
                for (int n = 0; n < ids.Count; n++)
                {
                    k = (k + dir + ids.Count) % ids.Count;
                    if (!taken.Contains(HeroCatalog.AbilityOf(ids[k]))) break;
                }
                lo.SetHero(ids[k]);
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
            if (key == "exclusiveAbilities") { st.ExclusiveAbilities = !st.ExclusiveAbilities; for (int i = 1; i < N; i++) if (slots[i].Joined) EnforceExclusive(i); }
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

        // Empieza la partida: los presentes pasan a ser J1, J2... en orden (sin huecos), cada uno con su dispositivo
        void Begin()
        {
            var j = JoinedSlots();
            foreach (var s in j) Loadout(s.I).Save();
            var binds = new List<DeviceBinding>();
            var loads = new List<PlayerLoadout>();
            foreach (var s in j) { binds.Add(g.Bindings[s.I]); loads.Add(g.Loadouts[s.I]); }
            for (int k = 0; k < j.Count; k++) { g.Bindings[k] = binds[k]; g.Loadouts[k] = loads[k]; }
            Close();
            g.Start(Mathf.Max(1, j.Count));
        }

        // ---------- Bucle (lo llama el frame mientras el estado es la sala) ----------
        public void Update(float dt)
        {
            var input = g.Input;
            if (input.Consume("Escape")) { g.BackToTitle(); return; }
            if (input.Consume("KeyB")) CycleSetting("bonusMode");
            if (input.Consume("KeyV")) CycleSetting("exclusiveAbilities");
            if (input.Consume("KeyC")) CycleSetting("swapAllies");
            foreach (var s in slots)
            {
                if (!s.Joined) continue;
                var c = new PlayerControls(input, g.Bindings[s.I]);
                int n = RowIds.Length;
                if (c.MenuPressed(0)) { s.Row = (s.Row - 1 + n) % n; Render(s.I); }
                if (c.MenuPressed(1)) { s.Row = (s.Row + 1) % n; Render(s.I); }
                if (c.MenuPressed(2)) Change(s.I, -1);
                if (c.MenuPressed(3)) Change(s.I, 1);
                if (c.ConsumeConfirm()) ToggleReady(s.I);
                if (c.ConsumeAbility()) CycleDevice(s.I);
                if (s.I > 0 && s.Joined && c.ConsumeInteract()) { Leave(s.I); continue; }
                if (s.NoteT > 0f) { s.NoteT -= dt; if (s.NoteT <= 0f) { s.Note = ""; s.NoteText.text = ""; } }
                // Si su mando se conecta o desconecta, se actualiza la etiqueta
                bool ok = Available(g.Bindings[s.I]);
                if (ok != s.PadOk) { s.PadOk = ok; Render(s.I); }
                var lo = Loadout(s.I);
                s.Preview.Set(Player.Looks[s.I], lo.Cosmetics, lo.Ability, lo.Hero);
                s.Preview.Update(dt);
            }
            // Unirse: cualquier dispositivo que no use nadie
            foreach (var b in ControlSchemes.AllDevices())
            {
                if (DeviceInUse(b)) continue;
                if (input.ConsumeAny(ControlSchemes.For(b).Join)) JoinWith(b);
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
        void RenderAll() { for (int i = 0; i < N; i++) Render(i); }

        void Render(int i)
        {
            var s = slots[i];
            var lo = Loadout(i);
            var pc = MathUtil.Hex(Player.Looks[i].Ring);
            s.Border.color = s.Joined ? (s.Ready ? Color.Lerp(pc, Color.white, 0.35f) : pc) : new Color(pc.r, pc.g, pc.b, 0.45f);
            s.JoinBox.gameObject.SetActive(!s.Joined);
            s.Body.gameObject.SetActive(s.Joined);
            var b = g.Bindings[i];
            string dev = b.Label + (!Available(b) ? " <color=#ff6a5a>(sin conectar)</color>" : "");
            s.Conn.text = s.Joined ? dev + " · " + (s.Ready ? "✔ LISTO" : "eligiendo") : "libre";
            s.Conn.color = s.Ready ? MathUtil.Hex(0x7dff8a) : MathUtil.Hex(0xc9b48d);
            s.Conn.fontStyle = s.Ready ? FontStyles.Bold : FontStyles.Normal;
            if (!s.Joined)
            {
                bool kbFree = !DeviceInUse(DeviceBinding.KeyA) || !DeviceInUse(DeviceBinding.KeyB);
                s.JoinText.text = kbFree
                    ? "Pulsa " + Hud.Key("A") + " en un mando\no " + Hud.Key(!DeviceInUse(DeviceBinding.KeyB) ? "ENTER" : "X") + " en el teclado para unirte"
                    : "Teclado completo (2 jugadores):\nconecta un mando y pulsa " + Hud.Key("A");
                return;
            }
            var sc = ControlSchemes.For(b);
            s.Keys.text = (sc.Pad >= 0 ? "Cruceta" : sc.MoveKeys) + " elegir · " + sc.AttackKey + " listo · " + sc.AbilityKey + " cambiar control" + (i > 0 ? " · " + sc.InteractKey + " salir" : "");
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
                else if (RowIds[k] == "hero") { label = HeroCatalog.NameOf(lo.Hero); icon = HeroCatalog.IconOf(lo.Hero); }
                else { var it = Cosmetics.Get(lo.Cosmetics[RowIds[k]]); label = it.Name; icon = it.Icon; }
                row.Value.text = icon + " " + label;
            }
            var ab = Abilities.Get(lo.Ability);
            s.CardIcon.text = ab.Icon;
            s.CardName.text = "<b>" + ab.Name + "</b>";
            s.CardShort.text = ab.Short;
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
