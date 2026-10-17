using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    public struct IncomingResult
    {
        public bool Blocked, ArmorHit, ArmorBroke;
        public float Amount;
    }

    // Lo que trae un jugador al cambiar de piso (y lo que se restaura al reintentar)
    [System.Serializable]
    public sealed class GearState
    {
        public int gold;
        public string armorId;
        public int armorDurability;
        public List<string> itemIds = new List<string>();
        public List<int> itemCharges = new List<int>();
        public List<BuffState> buffs = new List<BuffState>();
    }

    [System.Serializable]
    public sealed class BuffState
    {
        public string id;
        public int stacks;
        public float t;
    }

    // Equipo de JUEGO de cada jugador (PlayerGear.js): recompensas de los pisos bonus. Categorías:
    //  - armadura persistente (reduce daño, se gasta golpe a golpe, se ve en el muñeco)
    //  - objeto especial persistente (escudo, botas, amuleto, guantes, brújula, ídolo)
    //  - mejora temporal en tiempo de juego, con pilas y número de mejoras activas limitados
    //  - instantánea (curación, oro, maldiciones)
    // Los cosméticos NO están aquí: viven en PlayerLoadout y nunca tocan estas cifras.
    public sealed class PlayerGear
    {
        public sealed class Buff { public string Id; public int Stacks; public float T; }
        sealed class ItemSlot { public string Id; public int Charges; }

        public readonly Player Owner;
        public string ArmorId;            // null: sin armadura
        public int ArmorDurability;
        readonly List<ItemSlot> items = new List<ItemSlot>();   // en orden de llegada (fichas del panel)
        public readonly List<Buff> Buffs = new List<Buff>();
        public int Gold;

        public PlayerGear(Player owner) { Owner = owner; }

        // ---------- Estado entre pisos ----------
        public GearState GetState()
        {
            var s = new GearState { gold = Gold, armorId = ArmorId, armorDurability = ArmorDurability };
            foreach (var it in items) { s.itemIds.Add(it.Id); s.itemCharges.Add(it.Charges); }
            foreach (var b in Buffs) s.buffs.Add(new BuffState { id = b.Id, stacks = b.Stacks, t = b.T });
            return s;
        }

        public void SetState(GearState s)
        {
            if (s == null) return;
            ArmorId = s.armorId != null && Rewards.Get(s.armorId) != null ? s.armorId : null;
            ArmorDurability = ArmorId != null ? s.armorDurability : 0;
            items.Clear();
            for (int i = 0; i < s.itemIds.Count; i++)
                if (Rewards.Get(s.itemIds[i]) != null) items.Add(new ItemSlot { Id = s.itemIds[i], Charges = s.itemCharges[i] });
            Buffs.Clear();
            foreach (var b in s.buffs) if (Rewards.Get(b.id) != null) Buffs.Add(new Buff { Id = b.id, Stacks = b.stacks, T = b.t });
            Gold = s.gold;
        }

        // ---------- Consultas ----------
        ItemSlot FindItem(string id) { foreach (var it in items) if (it.Id == id) return it; return null; }
        public bool Has(string id) { var it = FindItem(id); return it != null && it.Charges > 0; }
        public int Charges(string id) { var it = FindItem(id); return it != null ? it.Charges : 0; }
        public int ItemCount { get { return items.Count; } }
        public IEnumerable<string> ItemIds { get { foreach (var it in items) yield return it.Id; } }
        public int BuffStacks(string id) { foreach (var b in Buffs) if (b.Id == id) return b.Stacks; return 0; }
        public float BuffValue(string id) { return BuffStacks(id) * Rewards.Get(id).Per; }
        public RewardDef ArmorDef() { return ArmorId != null ? Rewards.Get(ArmorId) : null; }
        public float ArmorFraction() { var a = ArmorDef(); return a != null ? (float)ArmorDurability / a.Durability : 0f; }

        // ---------- Modificadores que consultan los sistemas existentes ----------
        public float SpeedMult()
        {
            return Mathf.Max(0.5f, 1f + BuffValue("buff_speed") + BuffValue("curse_slow") + (Has("item_boots") ? 0.12f : 0f));
        }

        public float KnockTakenMult() { return Mathf.Max(0.3f, 1f - BuffValue("buff_anchor")); }

        public float StunMult()
        {
            var a = ArmorDef();
            return Mathf.Max(0.25f, (1f - BuffValue("buff_headstrong")) * (1f - (a != null ? a.StunResist : 0f)));
        }

        public float DamageMult() { return (1f + BuffValue("buff_cook")) * (Has("item_idol") ? 1.35f : 1f); }
        public float KnockDealtMult() { return Has("item_gloves") ? 1.4f : 1f; }
        public float Luck() { return Has("item_luck") ? 1f : 0f; }

        // Daño entrante. kind: "trap" | "hit" | ...
        public IncomingResult ModifyIncoming(float amount, string kind)
        {
            var sh = FindItem("item_shield");
            if (sh != null && sh.Charges > 0)
            {
                sh.Charges--;
                if (sh.Charges <= 0) items.Remove(sh);
                return new IncomingResult { Amount = 0f, Blocked = true };
            }
            float a = amount;
            if (Has("item_idol")) a *= 1.3f;
            if (kind == "trap")
            {
                a *= Mathf.Max(0.1f, 1f - BuffValue("buff_soles"));
                var td = ArmorDef();
                if (td != null && td.TrapResist > 0f) a *= 1f - td.TrapResist;
            }
            bool armorHit = false, armorBroke = false;
            var ad = ArmorDef();
            if (ad != null && a > 0f)
            {
                a *= 1f - ad.Reduction;
                ArmorDurability--;
                armorHit = true;
                if (ArmorDurability <= 0) { ArmorId = null; ArmorDurability = 0; armorBroke = true; }
            }
            return new IncomingResult { Amount = Mathf.Max(1f, MathUtil.RoundJs(a)), ArmorHit = armorHit, ArmorBroke = armorBroke };
        }

        // Devuelve true si caducó alguna mejora (refrescar el HUD)
        public bool Update(float dt)
        {
            for (int i = 0; i < Buffs.Count; i++) Buffs[i].T -= dt;
            int before = Buffs.Count;
            Buffs.RemoveAll(b => b.T <= 0f);
            return before != Buffs.Count;
        }

        public bool HasTimedBuffs { get { return Buffs.Count > 0; } }

        // Aplica una recompensa. Devuelve el texto para mostrar al jugador (null si no existe)
        public string Apply(string id)
        {
            var d = Rewards.Get(id);
            var p = Owner;
            if (d == null) return null;
            if (d.Kind == "armor")
            {
                // Una sola armadura: se queda la que más protección total ofrece
                var cur = ArmorDef();
                if (cur != null && cur.Reduction * ArmorDurability > d.Reduction * d.Durability)
                {
                    Gold += 20;
                    return d.Name + " (peor que la tuya) → +20 monedas";
                }
                ArmorId = d.Id; ArmorDurability = d.Durability;
                return d.Name;
            }
            if (d.Kind == "item")
            {
                if (id == "item_shield")
                {
                    var sh = FindItem(id);
                    int ch = sh != null ? sh.Charges : 0;
                    if (ch >= GearConfig.MaxShieldCharges) { Gold += 15; return "Escudo de sobra → +15 monedas"; }
                    if (sh == null) items.Add(new ItemSlot { Id = id, Charges = 1 }); else sh.Charges++;
                    return d.Name;
                }
                if (FindItem(id) != null || items.Count >= GearConfig.MaxItems) { Gold += 25; return d.Name + " repetido → +25 monedas"; }
                items.Add(new ItemSlot { Id = id, Charges = 1 });
                return d.Name;
            }
            if (d.Kind == "buff")
            {
                foreach (var b in Buffs)
                {
                    if (b.Id != id) continue;
                    b.Stacks = Mathf.Min(GearConfig.MaxBuffStacks, b.Stacks + 1);
                    b.T = GearConfig.BuffDuration;
                    return d.Name + " x" + b.Stacks;
                }
                if (Buffs.Count >= GearConfig.MaxActiveBuffs)
                {
                    // Sin hueco: sustituye a la mejora a la que menos tiempo le queda
                    Buff least = Buffs[0];
                    foreach (var b in Buffs) if (b.T < least.T) least = b;
                    Buffs.Remove(least);
                }
                Buffs.Add(new Buff { Id = id, Stacks = 1, T = d.Curse ? 60f : GearConfig.BuffDuration });
                return d.Name;
            }
            // Instantáneas
            // (sin dueño: equipo suelto de las pruebas de lógica)
            if (d.Heal > 0f && p != null) p.Heal(d.Heal);
            if (d.Gold > 0f) Gold += (int)d.Gold;
            if (d.Hurt > 0f && p != null && !p.Dead) { p.Hp = Mathf.Max(1f, p.Hp - d.Hurt); p.G.Ui.SetHealth(p.Index, p.Hp, p.MaxHp); }
            var w = p != null ? p.Weapon : null;
            if (d.Brittle && w != null && w.HasDurability && !w.Broken) w.Durability = Mathf.Max(1f, Mathf.Ceil(w.Durability / 2f));
            return d.Name;
        }

        // Lo que enseña el panel de EQUIPO: armadura (icono y fracción que le queda) y fichas de objetos y
        // mejoras (texto enriquecido). Sin armadura, armorIcon queda a null.
        public void HudInfo(out string armorIcon, out float armorK, out string chips)
        {
            var ad = ArmorDef();
            armorIcon = ad != null ? ad.Icon : null;
            armorK = ArmorFraction();
            var sb = new System.Text.StringBuilder();
            foreach (var it in items)
            {
                var d = Rewards.Get(it.Id);
                sb.Append(d.Icon);
                if (it.Charges > 1) sb.Append("<size=60%><color=#ffe2a0><b>").Append(it.Charges).Append("</b></color></size>");
                sb.Append(' ');
            }
            foreach (var b in Buffs)
            {
                var d = Rewards.Get(b.Id);
                sb.Append(d.Icon);
                sb.Append("<size=60%>");
                if (b.Stacks > 1) sb.Append("<color=#ffe2a0><b>x").Append(b.Stacks).Append("</b></color>");
                sb.Append(d.Curse ? "<color=#ff6a5a>" : "<color=#9fd36a>").Append(Mathf.CeilToInt(b.T)).Append("</color></size> ");
            }
            chips = sb.ToString().TrimEnd();
        }

        // Piezas de armadura sobre el muñeco: color y forma propios, distintas de cualquier cosmético
        public void RefreshVisual(CharacterParts parts)
        {
            if (parts == null || !parts.Armor) return;
            var g = parts.Armor;
            for (int i = g.childCount - 1; i >= 0; i--) Object.Destroy(g.GetChild(i).gameObject);
            var ad = ArmorDef();
            if (ad == null) return;
            var col = MathUtil.Hex(ad.Color);
            var body = new MeshBuilder();
            var rim = new MeshBuilder();
            for (int s = -1; s <= 1; s += 2)
            {
                body.Add(ProcMesh.Box(0.26f, 0.12f, 0.34f), Matrix4x4.TRS(new Vector3(s * 0.38f, 1.12f, 0), ProcMesh.EulerXYZ(0, 0, s * -0.35f), Vector3.one), col);
                rim.Add(ProcMesh.Box(0.27f, 0.025f, 0.35f), Matrix4x4.TRS(new Vector3(s * 0.38f, 1.05f, 0), ProcMesh.EulerXYZ(0, 0, s * -0.35f), Vector3.one), MathUtil.Hex(0x7dd6ff));
            }
            body.Add(ProcMesh.Cylinder(0.4f, 0.43f, 0.18f, 8), new Vector3(0, 0.42f, 0), col);
            var a = CharacterModel.MeshObject(g, "Piezas", body.ToMesh("armadura"), Mats.Lit, Vector3.zero);
            var b = CharacterModel.MeshObject(g, "Filo", rim.ToMesh("filoArmadura"), Mats.Unlit, Vector3.zero);
            a.layer = b.layer = g.gameObject.layer;
        }
    }
}
