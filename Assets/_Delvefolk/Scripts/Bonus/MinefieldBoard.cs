using System;
using System.Collections.Generic;

namespace Delvefolk
{
    // Lógica pura del Buscaminas (MinefieldBoard.js), sin Unity: se puede probar aislada.
    // Tablero de cols x rows. Todas las acciones pasan por Reveal()/ToggleFlag(), que validan el estado: una
    // casilla solo se revela UNA vez, así dos jugadores que actúan a la vez nunca producen resultados contradictorios.
    public sealed class MinefieldBoard
    {
        public enum CellState { Hidden, Revealed, Flagged }

        public sealed class Cell
        {
            public bool Mine, Hinted;
            public int N;              // minas vecinas
            public CellState State;
            public string Special;     // "hint" | "shield" | "gold" | null
            public object By;          // quién la destapó
        }

        public struct RevealResult
        {
            public bool Ok, Mine;
            public string Reason;
            public List<int> Revealed;
            public List<KeyValuePair<int, string>> Specials;   // (índice, tipo)
        }

        public sealed class Options
        {
            public int Cols, Rows, Mines, MaxTries = 200;
            public uint Seed;
            public int StartC = -1, StartR = -1;
            public int Hint, Shield, Gold;
            public bool RequireSolvable;
        }

        public readonly int Cols, Rows, StartC, StartR;
        public readonly uint Seed;
        public Cell[] Cells;
        public bool Solvable;
        public int MineCount, Attempts, RevealedSafe, TotalSafe, Flags;

        public MinefieldBoard(Options o)
        {
            Cols = o.Cols; Rows = o.Rows;
            StartC = o.StartC >= 0 ? o.StartC : o.Cols / 2;
            StartR = o.StartR >= 0 ? o.StartR : o.Rows - 1;
            Seed = o.Seed;
            Generate(o);
        }

        public int Idx(int c, int r) { return r * Cols + c; }
        public bool InBounds(int c, int r) { return c >= 0 && r >= 0 && c < Cols && r < Rows; }
        public Cell CellAt(int c, int r) { return InBounds(c, r) ? Cells[Idx(c, r)] : null; }

        public List<KeyValuePair<int, int>> Neighbors(int c, int r)
        {
            var outp = new List<KeyValuePair<int, int>>(8);
            for (int dr = -1; dr <= 1; dr++)
                for (int dc = -1; dc <= 1; dc++)
                    if ((dc != 0 || dr != 0) && InBounds(c + dc, r + dr)) outp.Add(new KeyValuePair<int, int>(c + dc, r + dr));
            return outp;
        }

        // ---------- Generación ----------
        void Generate(Options o)
        {
            int total = Cols * Rows;
            // La casilla inicial y sus vecinas nunca tienen mina (apertura garantizada)
            var safe = new HashSet<int> { Idx(StartC, StartR) };
            foreach (var nb in Neighbors(StartC, StartR)) safe.Add(Idx(nb.Key, nb.Value));
            int mines = Math.Max(0, Math.Min(o.Mines, total - safe.Count));
            int tries = o.MaxTries > 0 ? o.MaxTries : 200;
            int attempt = 0;
            for (;;)
            {
                var rnd = new Rng(unchecked((uint)((long)Seed + (long)attempt * 7919L + (long)mines * 104729L)));
                LayMines(mines, safe, rnd);
                Solvable = Solve();
                if (!o.RequireSolvable || Solvable) break;
                attempt++;
                // Si no sale resoluble tras muchos intentos, se quita una mina y se vuelve a intentar: siempre termina
                if (attempt >= tries) { attempt = 0; mines--; }
            }
            MineCount = mines;
            Attempts = attempt;
            PlaceSpecials(o, new Rng(Seed ^ 0x5bd1e995u));
            RevealedSafe = 0;
            TotalSafe = total - mines;
            Flags = 0;
            Reveal(StartC, StartR, null);
        }

        void LayMines(int n, HashSet<int> safe, Rng rnd)
        {
            int total = Cols * Rows;
            Cells = new Cell[total];
            for (int i = 0; i < total; i++) Cells[i] = new Cell();
            var free = new List<int>();
            for (int i = 0; i < total; i++) if (!safe.Contains(i)) free.Add(i);
            rnd.Shuffle(free);
            for (int k = 0; k < n; k++) Cells[free[k]].Mine = true;
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                {
                    int cnt = 0;
                    foreach (var nb in Neighbors(c, r)) if (Cells[Idx(nb.Key, nb.Value)].Mine) cnt++;
                    Cells[Idx(c, r)].N = cnt;
                }
        }

        // Casillas especiales (siempre seguras y fuera de la apertura inicial): pista, escudo, oro
        void PlaceSpecials(Options o, Rng rnd)
        {
            var open = new HashSet<int>(FloodSet(StartC, StartR));
            var cand = new List<int>();
            for (int i = 0; i < Cells.Length; i++) if (!Cells[i].Mine && !open.Contains(i)) cand.Add(i);
            rnd.Shuffle(cand);
            int k = 0;
            foreach (var kind in new[] { "hint", "shield", "gold" })
            {
                int n = kind == "hint" ? o.Hint : kind == "shield" ? o.Shield : o.Gold;
                for (int j = 0; j < n && k < cand.Count; j++) Cells[cand[k++]].Special = kind;
            }
        }

        // Casillas que destapa un clic en (c,r): ella misma y, si es un 0, la expansión en cadena
        public List<int> FloodSet(int c, int r)
        {
            var outp = new List<int>();
            var seen = new HashSet<int>();
            var stack = new Stack<KeyValuePair<int, int>>();
            stack.Push(new KeyValuePair<int, int>(c, r));
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                int i = Idx(p.Key, p.Value);
                if (!seen.Add(i)) continue;
                var cell = Cells[i];
                if (cell.Mine) continue;
                outp.Add(i);
                if (cell.N == 0) foreach (var nb in Neighbors(p.Key, p.Value)) stack.Push(nb);
            }
            return outp;
        }

        // ---------- Acciones (validadas) ----------
        // force: pisar la casilla ignora la bandera
        public RevealResult Reveal(int c, int r, object by, bool force = false)
        {
            var cell = CellAt(c, r);
            if (cell == null) return new RevealResult { Reason = "fuera" };
            if (cell.State == CellState.Revealed) return new RevealResult { Reason = "ya revelada" };
            if (cell.State == CellState.Flagged && !force) return new RevealResult { Reason = "bandera" };
            if (cell.Mine)
            {
                if (cell.State == CellState.Flagged) Flags--;
                cell.State = CellState.Revealed; cell.By = by;
                return new RevealResult { Ok = true, Mine = true, Revealed = new List<int> { Idx(c, r) }, Specials = new List<KeyValuePair<int, string>>() };
            }
            var revealed = new List<int>();
            var specials = new List<KeyValuePair<int, string>>();
            foreach (int i in FloodSet(c, r))
            {
                var k = Cells[i];
                if (k.State == CellState.Revealed) continue;
                if (k.State == CellState.Flagged) Flags--;
                k.State = CellState.Revealed; k.By = by;
                RevealedSafe++;
                revealed.Add(i);
                if (k.Special != null) specials.Add(new KeyValuePair<int, string>(i, k.Special));
            }
            return new RevealResult { Ok = true, Mine = false, Revealed = revealed, Specials = specials };
        }

        // Devuelve (ok, con bandera)
        public bool ToggleFlag(int c, int r, out bool flagged)
        {
            flagged = false;
            var cell = CellAt(c, r);
            if (cell == null || cell.State == CellState.Revealed) return false;
            cell.State = cell.State == CellState.Flagged ? CellState.Hidden : CellState.Flagged;
            flagged = cell.State == CellState.Flagged;
            Flags += flagged ? 1 : -1;
            return true;
        }

        // Pista: marca con bandera una mina aún oculta (la elige de forma estable)
        public int HintMine(Rng rnd)
        {
            var list = new List<int>();
            for (int i = 0; i < Cells.Length; i++) if (Cells[i].Mine && Cells[i].State == CellState.Hidden) list.Add(i);
            if (list.Count == 0) return -1;
            int idx = list[(int)Math.Floor(rnd.NextDouble() * list.Count)];
            Cells[idx].State = CellState.Flagged; Cells[idx].Hinted = true;
            Flags++;
            return idx;
        }

        // Revela n casillas seguras al azar (brújula maldita)
        public List<RevealResult> RevealRandomSafe(int n, Rng rnd)
        {
            var list = new List<int>();
            for (int i = 0; i < Cells.Length; i++) if (!Cells[i].Mine && Cells[i].State != CellState.Revealed) list.Add(i);
            rnd.Shuffle(list);
            var outp = new List<RevealResult>();
            for (int k = 0; k < n && k < list.Count; k++)
            {
                int i = list[k];
                var r = Reveal(i % Cols, i / Cols, null, true);
                if (r.Ok) outp.Add(r);
            }
            return outp;
        }

        public bool Complete { get { return RevealedSafe >= TotalSafe; } }

        // ---------- Resolución sin adivinar ----------
        // Simula a un jugador perfecto que solo destapa lo que puede deducir. true si destapa todas las seguras.
        public bool Solve()
        {
            int n = Cols * Rows;
            var known = new byte[n];   // 0 desconocida, 1 segura revelada, 2 mina deducida
            int revealedSafe = 0, totalSafe = 0;
            foreach (var k in Cells) if (!k.Mine) totalSafe++;
            Action<int> open = i =>
            {
                if (known[i] != 0) return;
                var stack = new Stack<int>();
                stack.Push(i);
                while (stack.Count > 0)
                {
                    int j = stack.Pop();
                    if (known[j] != 0) continue;
                    if (Cells[j].Mine) return;   // no debería pasar: solo se abren deducidas
                    known[j] = 1; revealedSafe++;
                    if (Cells[j].N == 0) foreach (var nb in Neighbors(j % Cols, j / Cols)) stack.Push(Idx(nb.Key, nb.Value));
                }
            };
            open(Idx(StartC, StartR));
            bool progress = true;
            while (progress && revealedSafe < totalSafe)
            {
                progress = false;
                // Restricciones: para cada número, sus vecinas desconocidas y cuántas minas faltan
                var cons = new List<KeyValuePair<List<int>, int>>();
                for (int i = 0; i < n; i++)
                {
                    if (known[i] != 1 || Cells[i].N == 0) continue;
                    var hid = new List<int>();
                    int flagged = 0;
                    foreach (var nb in Neighbors(i % Cols, i / Cols))
                    {
                        int j = Idx(nb.Key, nb.Value);
                        if (known[j] == 0) hid.Add(j); else if (known[j] == 2) flagged++;
                    }
                    if (hid.Count > 0) cons.Add(new KeyValuePair<List<int>, int>(hid, Cells[i].N - flagged));
                }
                Action<List<int>, bool> mark = (list, mine) =>
                {
                    foreach (int j in list)
                    {
                        if (known[j] != 0) continue;
                        if (mine) known[j] = 2; else open(j);
                        progress = true;
                    }
                };
                foreach (var c in cons)
                {
                    if (c.Value == 0) mark(c.Key, false);
                    else if (c.Value == c.Key.Count) mark(c.Key, true);
                }
                if (progress) continue;
                // Regla de subconjuntos: si A ⊆ B, las casillas de B\A tienen (needB - needA) minas
                foreach (var a in cons)
                    foreach (var b in cons)
                    {
                        if (ReferenceEquals(a.Key, b.Key) || a.Key.Count >= b.Key.Count) continue;
                        bool sub = true;
                        foreach (int j in a.Key) if (!b.Key.Contains(j)) { sub = false; break; }
                        if (!sub) continue;
                        var rest = new List<int>();
                        foreach (int j in b.Key) if (!a.Key.Contains(j)) rest.Add(j);
                        int m = b.Value - a.Value;
                        if (m == 0) mark(rest, false);
                        else if (m == rest.Count) mark(rest, true);
                    }
                if (progress) continue;
                // Recuento global de minas
                int unknown = 0, minesLeft = 0;
                foreach (var k in Cells) if (k.Mine) minesLeft++;
                for (int i = 0; i < n; i++) { if (known[i] == 0) unknown++; else if (known[i] == 2) minesLeft--; }
                if (minesLeft == 0 || minesLeft == unknown)
                {
                    var l = new List<int>();
                    for (int i = 0; i < n; i++) if (known[i] == 0) l.Add(i);
                    mark(l, minesLeft != 0);
                }
            }
            return revealedSafe >= totalSafe;
        }
    }
}
