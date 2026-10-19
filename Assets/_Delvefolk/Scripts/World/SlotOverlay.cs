using UnityEngine;

namespace Delvefolk
{
    // La tirada de la tragaperras en pantalla. Mientras está abierta, la simulación se congela (como las
    // presentaciones de los pisos bonus) y solo avanza la animación, también con el juego en pausa.
    //  1. Los resultados ya vienen elegidos (SlotMachine.Spin) y no se vuelven a sortear.
    //  2. Cada rodillo gira y frena (ease-out) hasta caer EXACTAMENTE en su resultado, uno tras otro.
    //  3. Al pararse el último se aplican los resultados (una vez) y se explica cada uno.
    //  4. Se cierra sola tras la pausa de revelado o antes si el jugador confirma, y el juego sigue.
    // Si algo falla o tarda demasiado, se cierra igualmente (y se aplica si aún no se había hecho).
    public sealed class SlotOverlay
    {
        readonly Game g;
        SlotMachine machine;
        Player player;
        float t, total;
        readonly float[] stopAt = new float[3];
        readonly float[] endPos = new float[3];
        readonly string[][] icons = new string[3][];
        bool stopped, failed;

        public bool Active { get; private set; }
        public bool Revealed { get { return stopped; } }
        public float Elapsed { get { return t; } }
        public int Opened { get; private set; }

        public SlotOverlay(Game game) { g = game; }

        public void Open(SlotMachine m, Player p, SlotMachineConfig cfg)
        {
            machine = m; player = p;
            t = 0f; stopped = false; failed = false;
            var lists = new[] { cfg.Enemies, cfg.Abilities, cfg.Affinities };
            var res = new[] { m.EnemyResult, m.AbilityResult, m.AffinityResult };
            for (int r = 0; r < 3; r++)
            {
                var l = lists[r];
                icons[r] = new string[l.Count];
                for (int i = 0; i < l.Count; i++) icons[r][i] = l[i] != null ? l[i].Icon : "?";
                stopAt[r] = Mathf.Max(0.2f, cfg.ReelSpin) + Mathf.Max(0f, cfg.ReelStagger) * r;
                int target = Mathf.Max(0, l.IndexOf(res[r]));
                endPos[r] = target + l.Count * (3 + r);      // unas vueltas completas y cae en su casilla
            }
            total = stopAt[2] + Mathf.Max(0.3f, cfg.RevealPause);
            Active = true;
            Opened++;
            g.Ui.ShowSlot(true);
            g.Ui.SetSlotFooter("Girando...");
            for (int r = 0; r < 3; r++) g.Ui.SetSlotReel(r, icons[r], 0f, false);
        }

        // Un frame (dt del bucle del juego; en pausa también avanza)
        public void Update(float dt)
        {
            if (!Active) return;
            try
            {
                t += dt;
                for (int r = 0; r < 3; r++)
                {
                    float k = Mathf.Clamp01(t / stopAt[r]);
                    float e = 1f - (1f - k) * (1f - k) * (1f - k);   // frena hasta parar
                    g.Ui.SetSlotReel(r, icons[r], endPos[r] * e, k >= 1f);
                }
                if (!stopped && t >= stopAt[2]) Reveal();
                // Confirmar cierra antes (solo cuando ya se ve el resultado)
                if (stopped && player != null && t >= stopAt[2] + 0.25f &&
                    (player.Controls.ConsumeAttack() | player.Controls.ConsumeInteract() | player.Controls.ConsumeJump())) Close();
                else if (t >= total) Close();
            }
            catch (System.Exception ex)
            {
                if (!failed) Debug.LogError("[Tragaperras] Error en la animación: " + ex.Message + "\n" + ex.StackTrace);
                failed = true;
                Close();
            }
        }

        void Reveal()
        {
            stopped = true;
            machine.Finish();   // aplica los tres resultados (una sola vez)
            g.Ui.SetSlotResult(0, machine.EnemyResult.Explain);
            g.Ui.SetSlotResult(1, machine.AbilityResult.Label);
            var th = FloorThemeCatalog.Load().Get(g.Affinity);
            g.Ui.SetSlotResult(2, machine.AffinityResult.Label + (th != null && !string.IsNullOrEmpty(th.WeaponId) ? " · ¡arma nueva!" : ""));
            string key = player != null ? player.Controls.S.AttackKey : "X";
            g.Ui.SetSlotFooter("Pulsa " + Hud.Key(key) + " para seguir");
            g.Audio.Play("gate");
        }

        // Cierra siempre (aunque haya fallado algo): aplica si no se aplicó, oculta el panel y devuelve el control
        public void Close()
        {
            if (!Active) return;
            Active = false;
            try { if (machine != null && !machine.Applied) machine.Finish(); }
            catch (System.Exception ex) { Debug.LogError("[Tragaperras] No se pudieron aplicar los resultados: " + ex.Message); }
            finally
            {
                g.Ui.ShowSlot(false);
                g.Input.Clear();   // la pulsación de confirmar no se convierte en un ataque al volver
                machine = null; player = null;
            }
        }

        // Al cambiar de piso o reiniciar: se cierra sin aplicar nada del piso anterior
        public void Abort()
        {
            machine = null;
            if (Active) { Active = false; g.Ui.ShowSlot(false); }
        }
    }
}
