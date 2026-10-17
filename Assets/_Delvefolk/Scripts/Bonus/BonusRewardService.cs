using System.Collections.Generic;

namespace Delvefolk
{
    // Asignación y validación de recompensas (BonusRewardService.js).
    // Cada recompensa se reclama con una clave única (partida + origen + jugador). Una clave ya usada se
    // rechaza: así una recompensa no se puede cobrar dos veces (dos jugadores a la vez, dos frames seguidos,
    // reintentar el piso, volver a abrir un cofre...).
    public sealed class BonusRewardService
    {
        public struct Claim { public bool Ok, Dup; public string Text; public RewardDef Def; }
        public struct Granted { public string Icon, Text, Category, Note; }
        public sealed class LogEntry { public string Key, Reward, Text, Note; public int Player; }
        public struct PlanItem { public Player Player; public string Reward, Key, Note; }

        readonly Game g;
        readonly HashSet<string> ledger = new HashSet<string>();
        readonly List<LogEntry> log = new List<LogEntry>();

        public BonusRewardService(Game game) { g = game; }

        public void Reset() { ledger.Clear(); log.Clear(); }

        public string Key(string source, Player p) { return g.RunId + "|" + source + "|" + p.Index; }
        public bool Claimed(string source, Player p) { return ledger.Contains(Key(source, p)); }
        public int LedgerCount { get { return ledger.Count; } }
        public IReadOnlyList<LogEntry> Log { get { return log; } }

        public Claim ClaimReward(Player player, string rewardId, string source, string note = "", bool silent = false)
        {
            if (player == null) return new Claim();
            string k = Key(source, player);
            if (ledger.Contains(k)) return new Claim { Dup = true, Text = "" };
            var def = Rewards.Get(rewardId);
            if (def == null || player.Removed) return new Claim();
            ledger.Add(k);
            string text = player.Gear.Apply(rewardId);
            log.Add(new LogEntry { Key = k, Player = player.Index, Reward = rewardId, Text = text, Note = note ?? "" });
            if (def.Kind == "armor") player.Gear.RefreshVisual(player.Parts);
            g.Ui.SetGear(player.Index, player.Gear);
            if (!silent)
            {
                bool curse = def.Category == "Maldición";
                g.Ui.FloatText(player.X, 2.9f, player.Z, def.Icon + " " + text, curse ? TextStyle.Hurt : TextStyle.Pickup);
                g.Audio.Play(curse ? "curse" : "pickup");
            }
            return new Claim { Ok = true, Text = text, Def = def };
        }

        // Todo lo cobrado con claves que empiezan por source (durante y al final de un desafío), por jugador
        public Dictionary<int, List<Granted>> Summary(string source)
        {
            string pre = g.RunId + "|" + source;
            var outp = new Dictionary<int, List<Granted>>();
            foreach (var l in log)
            {
                if (!l.Key.StartsWith(pre, System.StringComparison.Ordinal)) continue;
                var d = Rewards.Get(l.Reward);
                List<Granted> list;
                if (!outp.TryGetValue(l.Player, out list)) outp[l.Player] = list = new List<Granted>();
                list.Add(new Granted { Icon = d.Icon, Text = l.Text, Category = d.Category, Note = l.Note });
            }
            return outp;
        }

        // Reparte un plan completo (fin del desafío). Devuelve por jugador la lista de lo concedido
        public Dictionary<int, List<Granted>> GrantPlan(List<PlanItem> plan)
        {
            var outp = new Dictionary<int, List<Granted>>();
            foreach (var it in plan)
            {
                var r = ClaimReward(it.Player, it.Reward, it.Key, it.Note, true);
                List<Granted> list;
                if (!outp.TryGetValue(it.Player.Index, out list)) outp[it.Player.Index] = list = new List<Granted>();
                if (r.Ok) list.Add(new Granted { Icon = r.Def.Icon, Text = r.Text, Category = r.Def.Category, Note = it.Note ?? "" });
            }
            return outp;
        }
    }
}
