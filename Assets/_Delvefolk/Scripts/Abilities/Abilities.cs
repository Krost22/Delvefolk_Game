using System;
using System.Collections.Generic;

namespace Delvefolk
{
    // Ficha de una habilidad especial (Abilities.js): datos, textos de la sala de preparación y su
    // demostración en la vista previa. El comportamiento (activar, durar, terminar) llega en la fase 5.
    public sealed class AbilityDef
    {
        public string Id, Name, Icon, Short, Allies;
        public int Color;
        public float Cooldown, Duration;
        public bool LocksMovement;
        public string[] Pros, Cons;
        // Parámetros de juego (p) del original
        public Dictionary<string, float> P = new Dictionary<string, float>();
        // Efecto de demostración para la sala de preparación (fx, tiempo)
        public Action<Effects, float> Preview;

        public float this[string key] { get { return P[key]; } }
    }

    public static class Abilities
    {
        public static readonly Dictionary<string, AbilityDef> Defs = new Dictionary<string, AbilityDef>();
        public static readonly List<string> Order = new List<string>();

        public static AbilityDef Get(string id) { AbilityDef d; return id != null && Defs.TryGetValue(id, out d) ? d : null; }
        public static string DefaultFor(int index) { return index == 0 ? "quake" : "shout"; }

        static void Register(AbilityDef d) { Defs[d.Id] = d; Order.Add(d.Id); }

        static Dictionary<string, float> P(params object[] kv)
        {
            var d = new Dictionary<string, float>();
            for (int i = 0; i < kv.Length; i += 2) d[(string)kv[i]] = Convert.ToSingle(kv[i + 1]);
            return d;
        }

        static Abilities()
        {
            // ======================= A: TERREMOTO BRUTAL =======================
            Register(new AbilityDef
            {
                Id = "quake", Name = "Terremoto Brutal", Icon = "🌋", Color = 0xd09a50, Cooldown = 14, Duration = 0.35f, LocksMovement = true,
                Short = "Golpeas el suelo: todo lo cercano salta por los aires y queda aturdido.",
                Pros = new[] { "Aturde a todos los enemigos cercanos", "Interrumpe ataques", "Desplaza barriles y objetos" },
                Cons = new[] { "También aturde a tus compañeros", "Enfriamiento largo", "Te quedas clavado un instante" },
                Allies = "Sí: los aturde y los levanta (sin daño)",
                P = P("radius", 4.2f, "damage", 8, "stun", 1.1f, "allyStun", 0.8f, "knock", 6),
                Preview = (fx, t) =>
                {
                    fx.Ring(0, 0, RingOpts.Of(0xd09a50, 3f, 0.45f));
                    fx.Burst(0, 0.2f, 0, BurstOpts.Of(18, 0x8a7b68, 5f, 5f, 0.15f));
                },
            });
            // ======================= B: EXPLOSIÓN DESESPERADA =======================
            Register(new AbilityDef
            {
                Id = "blast", Name = "Explosión Desesperada", Icon = "💥", Color = 0xff7a30, Cooldown = 20, Duration = 0,
                Short = "Explotas. Literalmente. Mucho daño a todo lo de alrededor... incluido tú.",
                Pros = new[] { "Daño enorme en área", "Rompe barriles y detona la pólvora", "Empuja muy fuerte" },
                Cons = new[] { "Te quita vida a ti (¡puede derribarte!)", "Hiere a los compañeros cercanos", "Enfriamiento muy largo" },
                Allies = "Sí: reciben daño y salen despedidos",
                P = P("radius", 3.6f, "damage", 55, "knock", 22, "stun", 0.9f, "selfDamage", 18, "allyDamage", 22),
                Preview = (fx, t) =>
                {
                    fx.Ring(0, 0, RingOpts.Of(0xffb040, 3f, 0.4f));
                    fx.Burst(0, 0.8f, 0, BurstOpts.Of(30, 0xff8a20, 8f, 6f, 0.2f).WithColorVar(0.15f));
                    fx.Flash(0, 2, 0, 0xff8a30, 4f, 0.3f);
                },
            });
            // ======================= C: CARGA DEL BUEY =======================
            Register(new AbilityDef
            {
                Id = "charge", Name = "Carga del Buey", Icon = "🐂", Color = 0xc84a3a, Cooldown = 8, Duration = 0.55f, LocksMovement = true,
                Short = "Embistes en línea recta arrollando lo que pilles. Girar cuesta.",
                Pros = new[] { "Arrolla enemigos, barriles y compañeros", "Perfecta para mandar monstruos a púas y fosos", "Enfriamiento corto" },
                Cons = new[] { "Apenas puedes girar durante la carga", "Si chocas con una pared te quedas aturdido", "Puedes arrollar a tu compañero" },
                Allies = "Sí: los arrolla (los empuja y aturde un poco, sin daño)",
                P = P("speed", 15, "turnRate", 2.4f, "damage", 12, "knock", 17, "stun", 0.7f, "allyKnock", 12, "allyStun", 0.45f, "wallStun", 0.9f, "wallDamage", 6),
                // Coordenadas de la vista previa del web (x, z) → Unity (x, −z)
                Preview = (fx, t) =>
                {
                    for (int i = 0; i < 6; i++) fx.Burst(-1.5f + i * 0.5f, 0.1f, -(-1.5f + i * 0.5f), BurstOpts.Of(2, 0x8a7b68, 1f, 2f, 0.14f));
                    fx.Ring(1.5f, -1.5f, RingOpts.Of(0xc84a3a, 1.3f, 0.3f));
                },
            });
            // ======================= D: GRITO DE GUERRA =======================
            Register(new AbilityDef
            {
                Id = "shout", Name = "Grito de Guerra", Icon = "📣", Color = 0xff6040, Cooldown = 12, Duration = 0,
                Short = "Un berrido mágico: los monstruos se cortan y tu equipo corre más.",
                Pros = new[] { "Interrumpe a los enemigos cercanos", "Tú y tus aliados corréis más unos segundos", "Aparta barriles y objetos ligeros" },
                Cons = new[] { "Casi no hace daño", "La interrupción es breve" },
                Allies = "Sí: les da velocidad (beneficioso)",
                P = P("radius", 6.5f, "allyRadius", 8, "damage", 3, "stun", 0.55f, "haste", 1.35f, "hasteTime", 4, "push", 8),
                Preview = (fx, t) =>
                {
                    for (int i = 0; i < 3; i++) fx.Ring(0, 0, RingOpts.Of(i == 1 ? 0xffd040 : 0xff6040, 1.8f + i * 0.8f, 0.35f + i * 0.12f, 1.0f));
                },
            });
            // ======================= E: INTERCAMBIO CAÓTICO =======================
            Register(new AbilityDef
            {
                Id = "swap", Name = "Intercambio Caótico", Icon = "🔀", Color = 0xb060ff, Cooldown = 15, Duration = 0,
                Short = "Cambias tu posición con el personaje al que miras (o el más cercano).",
                Pros = new[] { "Escapa de un rodeo en un instante", "Mete a un monstruo donde estabas tú (¿junto a las púas?)", "Desorienta al objetivo" },
                Cons = new[] { "Alcance limitado", "Enfriamiento alto", "Si no hay nadie a la vista, no funciona" },
                Allies = "Configurable: con \"intercambio con aliados\" activado, puede cambiarte por tu compañero (se marea un momento)",
                P = P("range", 8, "cone", 0.9f, "enemyStun", 0.7f, "confuse", 1.0f),
                Preview = (fx, t) =>
                {
                    fx.Burst(-1.5f, 1, 0, BurstOpts.Of(12, 0xb060ff, 3f, 3f, 0.14f));
                    fx.Burst(1.5f, 1, 0, BurstOpts.Of(12, 0xb060ff, 3f, 3f, 0.14f));
                    fx.Ring(0, 0, RingOpts.Of(0xb060ff, 1.4f, 0.3f));
                },
            });
            // ======================= F: IMÁN DE CHATARRA =======================
            Register(new AbilityDef
            {
                Id = "magnet", Name = "Imán de Chatarra", Icon = "🧲", Color = 0x60b0ff, Cooldown = 10, Duration = 0.9f,
                Short = "Atraes armas, comida y barriles cercanos. Lo que vuela golpea a quien pille.",
                Pros = new[] { "Reúne armas improvisadas", "Los objetos en vuelo golpean a los enemigos", "Los barriles atraídos arrollan" },
                Cons = new[] { "Los objetos también golpean a tus compañeros", "Lo pesado tarda en llegar", "No mueve nada fijo (columnas, cajas, trampas)" },
                Allies = "Sí: los objetos que vuelan les golpean",
                P = P("radius", 8, "itemSpeed", 11, "barrelPull", 26, "hitDamage", 8, "hitKnock", 6),
                Preview = (fx, t) =>
                {
                    var r = RingOpts.Of(0x60b0ff, 3f, 0.4f); r.R0 = 3f;
                    fx.Ring(0, 0, r);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * 1.57f;
                        fx.Burst((float)Math.Cos(a) * 2.5f, 0.8f, -(float)Math.Sin(a) * 2.5f, BurstOpts.Of(3, 0x60b0ff, 1f, 1f, 0.1f));
                    }
                },
            });
            // ======================= G: ÚLTIMO RECURSO =======================
            Register(new AbilityDef
            {
                Id = "guard", Name = "Último Recurso", Icon = "🐢", Color = 0xffd040, Cooldown = 16, Duration = 3.0f,
                Short = "Te haces una bola: recibes mucho menos daño y nada te empuja. Luego vas lento.",
                Pros = new[] { "-65 % de daño recibido durante 3 s", "Inmune a empujones y aturdimientos", "Los golpes no interrumpen lo que haces (comer, atacar)" },
                Cons = new[] { "No es invulnerabilidad", "Al acabar quedas ralentizado", "Enfriamiento alto" },
                Allies = "No afecta a los compañeros",
                P = P("damageTaken", 0.35f, "slow", 0.55f, "slowTime", 1.6f),
                Preview = (fx, t) => fx.Ring(0, 0, RingOpts.Of(0xffd040, 1.5f, 0.4f, 1f)),
            });
        }
    }
}
