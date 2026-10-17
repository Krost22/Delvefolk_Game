using System.Collections.Generic;
using UnityEngine;

namespace Delvefolk
{
    // Suelo de peligros de los minijuegos (HazardFloor.js): cada casilla puede estar segura, en aviso
    // (parpadeo naranja) o peligrosa. Las peligrosas hacen daño periódico a los jugadores (como trampa: la
    // armadura de corcho y las suelas gruesas ayudan) y a los enemigos (daño del entorno).
    // Lo usan la Arena de los Ecos, el Último Aliento y el Chef.
    public sealed class HazardFloor
    {
        public enum CellState { Safe, Warn, Danger }

        sealed class Cell
        {
            public int Tx, Tz;
            public CellState State;
            public MeshRenderer R;
            public float T;
        }

        readonly Game g;
        readonly Dictionary<long, Cell> cells = new Dictionary<long, Cell>();
        readonly List<Cell> order = new List<Cell>();
        readonly Transform group;
        public float Dps, Tick = 0.5f, EnemyMult = 0.8f;
        public int Color = 0xd0302a, WarnColor = 0xffa030;
        float tickT, time;
        List<Cell> dangerList;
        bool dangerDirty = true;
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        static Mesh quad;

        public HazardFloor(Game game, Transform parent, IEnumerable<Vector2Int> tiles, float dps = 12f, int color = 0xd0302a, int warnColor = 0xffa030, float enemyMult = 0.8f)
        {
            g = game;
            Dps = dps; Color = color; WarnColor = warnColor; EnemyMult = enemyMult;
            group = new GameObject("SueloPeligroso").transform;
            group.SetParent(parent, false);
            if (!quad) quad = new MeshBuilder().Add(ProcMesh.Plane(Cfg.Tile * 0.98f, Cfg.Tile * 0.98f).RotateX(-Mathf.PI / 2f), Vector3.zero, UnityEngine.Color.white).ToMesh("casillaPeligro");
            foreach (var t in tiles)
            {
                var go = CharacterModel.MeshObject(group, "Casilla", quad, Mats.Transparent, new Vector3(Grid.X(t.x), 0.03f, Grid.Z(t.y)));
                go.SetActive(false);
                var c = new Cell { Tx = t.x, Tz = t.y, State = CellState.Safe, R = go.GetComponent<MeshRenderer>() };
                cells[Key(t.x, t.y)] = c;
                order.Add(c);
            }
        }

        static long Key(int tx, int tz) { return ((long)tx << 32) ^ (uint)tz; }

        public CellState StateAt(float x, float z)
        {
            Cell c;
            return cells.TryGetValue(Key(Grid.TileX(x), Grid.TileZ(z)), out c) ? c.State : CellState.Safe;
        }

        public CellState StateOf(int tx, int tz) { Cell c; return cells.TryGetValue(Key(tx, tz), out c) ? c.State : CellState.Safe; }

        public void Set(int tx, int tz, CellState state)
        {
            Cell c;
            if (!cells.TryGetValue(Key(tx, tz), out c) || c.State == state) return;
            c.State = state; c.T = 0f;
            dangerDirty = true;
            c.R.gameObject.SetActive(state != CellState.Safe);
        }

        public int Count(CellState state)
        {
            int n = 0;
            foreach (var c in order) if (c.State == state) n++;
            return n;
        }

        void Paint(Cell c, float opacity)
        {
            var col = MathUtil.Hex(c.State == CellState.Warn ? WarnColor : Color).linear;
            mpb.SetVector(Mats.BaseColorId, new Vector4(col.r, col.g, col.b, opacity));
            c.R.SetPropertyBlock(mpb);
        }

        public void Update(float dt)
        {
            time += dt;
            foreach (var c in order)
            {
                c.T += dt;
                if (c.State == CellState.Warn) Paint(c, 0.18f + (Mathf.Sin(time * 14f) * 0.5f + 0.5f) * 0.35f);
                else if (c.State == CellState.Danger) Paint(c, 0.45f + Mathf.Sin(time * 3f + c.Tx + c.Tz) * 0.1f);
            }
            // Burbujas de vez en cuando sobre las casillas peligrosas
            if (g.FxRand.Next() < dt * 10f)
            {
                if (dangerList == null || dangerDirty)
                {
                    dangerList = new List<Cell>();
                    foreach (var c in order) if (c.State == CellState.Danger) dangerList.Add(c);
                    dangerDirty = false;
                }
                if (dangerList.Count > 0)
                {
                    var c = dangerList[(int)(g.FxRand.Next() * dangerList.Count)];
                    g.Effects.Burst(Grid.X(c.Tx) + (g.FxRand.Next() - 0.5f) * 1.6f, 0.1f, Grid.Z(c.Tz) + (g.FxRand.Next() - 0.5f) * 1.6f,
                        BurstOpts.Of(1, Color, 0.4f, 2.5f, 0.14f).WithGravity(1f).WithLife(0.8f));
                }
            }
            tickT -= dt;
            if (tickT > 0f) return;
            tickT = Tick;
            float dmg = MathUtil.RoundJs(Dps * Tick);
            foreach (var p in g.Players)
            {
                if (!p.Targetable || p.IsThrown || p.JumpY > 0.4f) continue;
                if (StateAt(p.X, p.Z) == CellState.Danger)
                {
                    if (p.Hurt(dmg, p.X, p.Z, 0f, new HurtOpts { Kind = "trap", Pierce = true, Dot = true })) p.Iframes = 0f;
                }
            }
            foreach (var e in g.Enemies)
            {
                if (e.Dead || e.State == EnemyState.Falling || !e.Body.Enabled) continue;
                if (StateAt(e.X, e.Z) == CellState.Danger) e.EnvDamage(dmg * EnemyMult, null);
            }
        }
    }
}
