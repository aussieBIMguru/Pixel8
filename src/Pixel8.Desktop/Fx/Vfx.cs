using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pixel8.Desktop.Rendering;

namespace Pixel8.Desktop.Fx;

/// <summary>
/// Chunky 8-bit hit and spell effects (session 7), drawn procedurally with single pixels in the PICO-8 palette:
/// slash, smash, stab, bite, arrow, bolt (fire bolt), fire, missile, radiant, heal, shield, poof, crit.
/// Effects are named by the "fx" keys in the data (see ContentDb.KnownFx). Projectiles (arrow, bolt, missile)
/// fly from the source to the target first; <see cref="Lead"/> says how long before they land.
/// Uses System.Random for particle scatter: the rules RNG is never touched.
/// </summary>
public sealed class VfxPlayer
{
    private sealed class Effect
    {
        public string Kind = "";
        public Vector2 From;
        public Vector2 To;
        public float Age;
        public float Duration;
        public int Seed;
    }

    private readonly List<Effect> _effects = new();
    private readonly Random _random = new();

    public bool Active => _effects.Count > 0;

    /// <summary>Time from spawn until a projectile reaches its target (0 for effects that start on the target).</summary>
    public static float Lead(string kind) => kind switch
    {
        "arrow" => 0.16f,
        "bolt" => 0.18f,
        "missile" => 0.26f,
        _ => 0f
    };

    private static float DurationOf(string kind) => kind switch
    {
        "slash" => 0.24f,
        "smash" => 0.3f,
        "stab" => 0.2f,
        "bite" => 0.26f,
        "arrow" => 0.26f,
        "bolt" => 0.5f,
        "fire" => 0.5f,
        "missile" => 0.42f,
        "radiant" => 0.45f,
        "heal" => 0.55f,
        "shield" => 0.35f,
        "poof" => 0.5f,
        "crit" => 0.28f,
        "poison" => 0.45f,
        "frost" => 0.5f,
        "necrotic" => 0.5f,
        "song" => 0.55f,
        "vine" => 0.45f,
        _ => 0.25f
    };

    /// <param name="from">Centre of the source sprite (projectiles start here).</param>
    /// <param name="to">Centre of the target sprite.</param>
    public void Spawn(string kind, Point from, Point to)
    {
        _effects.Add(new Effect
        {
            Kind = kind.ToLowerInvariant(),
            From = from.ToVector2(),
            To = to.ToVector2(),
            Duration = DurationOf(kind.ToLowerInvariant()),
            Seed = _random.Next()
        });
    }

    public void Update(float dt)
    {
        for (int i = _effects.Count - 1; i >= 0; i--)
        {
            _effects[i].Age += dt;
            if (_effects[i].Age >= _effects[i].Duration) _effects.RemoveAt(i);
        }
    }

    public void Draw(SpriteBatch b, PixelCanvas c)
    {
        foreach (var e in _effects)
        {
            float t = Math.Clamp(e.Age / e.Duration, 0f, 1f);
            var rnd = new Random(e.Seed);   // the same scatter every frame for this effect
            switch (e.Kind)
            {
                case "slash": Slash(b, c, e.To, t); break;
                case "smash": Smash(b, c, e.To, t, rnd); break;
                case "stab": Stab(b, c, e.From, e.To, t); break;
                case "bite": Bite(b, c, e.To, t); break;
                case "arrow": Projectile(b, c, e, Lead("arrow"), Palette.Brown, Palette.White, 3); break;
                case "bolt":
                    if (!Projectile(b, c, e, Lead("bolt"), Palette.Orange, Palette.Yellow, 2))
                        Fire(b, c, e.To, (e.Age - Lead("bolt")) / (e.Duration - Lead("bolt")), rnd, 8);
                    break;
                case "fire": Fire(b, c, e.To, t, rnd, 12); break;
                case "missile": Missiles(b, c, e); break;
                case "radiant": Radiant(b, c, e.To, t); break;
                case "heal": Heal(b, c, e.To, t, rnd); break;
                case "shield": Shield(b, c, e.To, t); break;
                case "poof": Poof(b, c, e.To, t, rnd); break;
                case "crit": Crit(b, c, e.To, t); break;
                case "poison": Poison(b, c, e.To, t, rnd); break;
                case "frost": Frost(b, c, e.To, t, rnd); break;
                case "necrotic": Necrotic(b, c, e.To, t, rnd); break;
                case "song": Song(b, c, e.From, e.To, t); break;
                case "vine": Vine(b, c, e.To, t); break;
            }
        }
    }

    // ------------------------------------------------------------------ effects

    private static void Px(SpriteBatch b, PixelCanvas c, float x, float y, Color color) =>
        c.Fill(b, (int)MathF.Round(x), (int)MathF.Round(y), 1, 1, color);

    /// <summary>A white diagonal cut sweeping across the target, its tail fading to grey.</summary>
    private static void Slash(SpriteBatch b, PixelCanvas c, Vector2 at, float t)
    {
        var start = at + new Vector2(5, -6);
        var end = at + new Vector2(-5, 6);
        float head = Math.Min(1, t * 1.6f);
        float tail = Math.Max(0, (t - 0.35f) * 1.6f);
        for (int i = 0; i <= 12; i++)
        {
            float f = i / 12f;
            if (f > head || f < tail) continue;
            var p = Vector2.Lerp(start, end, f);
            var col = head - f < 0.2f ? Palette.White : Palette.LightGrey;
            Px(b, c, p.X, p.Y, col);
            Px(b, c, p.X + 1, p.Y, col);
        }
    }

    /// <summary>An expanding square shockwave with debris (clubs, morningstars, staves).</summary>
    private static void Smash(SpriteBatch b, PixelCanvas c, Vector2 at, float t, Random rnd)
    {
        int r = 2 + (int)(t * 7);
        var col = t < 0.3f ? Palette.White : t < 0.6f ? Palette.Orange : Palette.Brown;
        c.Outline(b, new Rectangle((int)at.X - r, (int)at.Y - r, r * 2 + 1, r * 2 + 1), col);
        for (int i = 0; i < 4; i++)
        {
            float a = (float)(rnd.NextDouble() * Math.PI * 2);
            float d = 3 + t * 9;
            Px(b, c, at.X + MathF.Cos(a) * d, at.Y + MathF.Sin(a) * d + t * t * 6, Palette.Brown);
        }
    }

    /// <summary>A quick thrust line from the attacker's side with a bright tip.</summary>
    private static void Stab(SpriteBatch b, PixelCanvas c, Vector2 from, Vector2 at, float t)
    {
        int dir = from.X <= at.X ? 1 : -1;
        float reach = t < 0.5f ? t * 2 : (1 - t) * 2;
        float tipX = at.X - dir * 9 + dir * reach * 11;
        for (int i = 0; i < 5; i++)
            Px(b, c, tipX - dir * i, at.Y, i == 0 ? Palette.Yellow : Palette.LightGrey);
        if (t > 0.4f && t < 0.7f)
        {
            Px(b, c, at.X, at.Y - 2, Palette.White);
            Px(b, c, at.X, at.Y + 2, Palette.White);
        }
    }

    /// <summary>Two rows of teeth closing on the target.</summary>
    private static void Bite(SpriteBatch b, PixelCanvas c, Vector2 at, float t)
    {
        float close = Math.Min(1, t * 2);
        int top = (int)(at.Y - 7 + close * 5);
        int bottom = (int)(at.Y + 7 - close * 5);
        for (int dx = -3; dx <= 3; dx++)
        {
            Px(b, c, at.X + dx, top, Palette.White);
            Px(b, c, at.X + dx, bottom, Palette.White);
            if ((dx & 1) == 0)
            {
                Px(b, c, at.X + dx, top + 1, Palette.White);
                Px(b, c, at.X + dx, bottom - 1, Palette.White);
            }
        }
        if (t > 0.5f)
            for (int i = 0; i < 3; i++)
                Px(b, c, at.X - 2 + i * 2, at.Y + (t - 0.5f) * 10, Palette.Red);
    }

    /// <summary>
    /// A short streak flying from source to target, then a spark. Returns false once it has landed
    /// (so the bolt can follow up with a fire burst).
    /// </summary>
    private static bool Projectile(SpriteBatch b, PixelCanvas c, Effect e, float lead, Color body, Color tip, int length)
    {
        if (e.Age < lead)
        {
            float f = e.Age / lead;
            var pos = Vector2.Lerp(e.From, e.To, f);
            var dir = e.To - e.From;
            if (dir != Vector2.Zero) dir.Normalize();
            for (int i = 0; i < length; i++)
                Px(b, c, pos.X - dir.X * i, pos.Y - dir.Y * i, i == 0 ? tip : body);
            return true;
        }
        float k = (e.Age - lead) / Math.Max(0.01f, e.Duration - lead);
        if (e.Kind == "arrow")
        {
            int r = 1 + (int)(k * 3);
            Px(b, c, e.To.X - r, e.To.Y, Palette.White);
            Px(b, c, e.To.X + r, e.To.Y, Palette.White);
            Px(b, c, e.To.X, e.To.Y - r, Palette.White);
            Px(b, c, e.To.X, e.To.Y + r, Palette.White);
        }
        return false;
    }

    /// <summary>A burst of rising embers: yellow → orange → red → smoke.</summary>
    private static void Fire(SpriteBatch b, PixelCanvas c, Vector2 at, float t, Random rnd, int count)
    {
        t = Math.Clamp(t, 0, 1);
        if (t < 0.15f) c.Fill(b, (int)at.X - 2, (int)at.Y - 2, 5, 5, Palette.Yellow);
        for (int i = 0; i < count; i++)
        {
            float vx = (float)(rnd.NextDouble() * 2 - 1) * 9;
            float vy = -(float)(rnd.NextDouble() * 10 + 3);
            float life = 0.6f + (float)rnd.NextDouble() * 0.4f;
            float k = t / life;
            if (k > 1) continue;
            var col = k < 0.25f ? Palette.Yellow : k < 0.55f ? Palette.Orange : k < 0.8f ? Palette.Red : Palette.DarkGrey;
            Px(b, c, at.X + vx * k, at.Y + vy * k, col);
        }
    }

    /// <summary>Three force darts in a staggered volley, each popping on arrival.</summary>
    private static void Missiles(SpriteBatch b, PixelCanvas c, Effect e)
    {
        const float flight = 0.16f, gap = 0.05f, pop = 0.1f;
        for (int i = 0; i < 3; i++)
        {
            float age = e.Age - i * gap;
            if (age < 0) continue;
            var offset = new Vector2(0, (i - 1) * 3);
            if (age < flight)
            {
                // a slight arc on the way in
                float f = age / flight;
                var pos = Vector2.Lerp(e.From, e.To + offset, f) + new Vector2(0, -MathF.Sin(f * MathF.PI) * 4 * (i - 1));
                Px(b, c, pos.X, pos.Y, Palette.White);
                Px(b, c, pos.X - Math.Sign(e.To.X - e.From.X), pos.Y, Palette.Lavender);
            }
            else if (age < flight + pop)
            {
                var p = e.To + offset;
                Px(b, c, p.X - 1, p.Y, Palette.Lavender);
                Px(b, c, p.X + 1, p.Y, Palette.Lavender);
                Px(b, c, p.X, p.Y - 1, Palette.Lavender);
                Px(b, c, p.X, p.Y + 1, Palette.Lavender);
            }
        }
    }

    /// <summary>A beam of light from above, then a cross-shaped flare.</summary>
    private static void Radiant(SpriteBatch b, PixelCanvas c, Vector2 at, float t)
    {
        if (t < 0.55f)
        {
            int top = Math.Max(0, (int)at.Y - 30);
            c.Fill(b, (int)at.X - 1, top, 3, (int)at.Y - top, Palette.Yellow);
            c.Fill(b, (int)at.X, top, 1, (int)at.Y - top, Palette.White);
        }
        else
        {
            int r = 2 + (int)((t - 0.55f) * 14);
            for (int i = -r; i <= r; i++)
            {
                Px(b, c, at.X + i, at.Y, Palette.Yellow);
                Px(b, c, at.X, at.Y + i, Palette.Yellow);
            }
            Px(b, c, at.X, at.Y, Palette.White);
        }
    }

    /// <summary>Little green plus signs drifting up around the target.</summary>
    /// <summary>Round 11: ice shards bursting out from the target, white → blue → lavender.</summary>
    private static void Frost(SpriteBatch b, PixelCanvas c, Vector2 at, float t, Random rnd)
    {
        t = Math.Clamp(t, 0, 1);
        if (t < 0.15f) c.Fill(b, (int)at.X - 1, (int)at.Y - 1, 3, 3, Palette.White);
        for (int i = 0; i < 8; i++)
        {
            double angle = i * Math.PI / 4 + rnd.NextDouble() * 0.3;
            float dist = 2 + t * 8;
            var col = t < 0.3f ? Palette.White : t < 0.7f ? Palette.Blue : Palette.Lavender;
            Px(b, c, at.X + (float)Math.Cos(angle) * dist, at.Y + (float)Math.Sin(angle) * dist, col);
            if (t < 0.5f) Px(b, c, at.X + (float)Math.Cos(angle) * (dist - 1), at.Y + (float)Math.Sin(angle) * (dist - 1), Palette.White);
        }
    }

    /// <summary>Round 11: dark motes sucked into the target (life drain, gazes).</summary>
    private static void Necrotic(SpriteBatch b, PixelCanvas c, Vector2 at, float t, Random rnd)
    {
        t = Math.Clamp(t, 0, 1);
        for (int i = 0; i < 9; i++)
        {
            double angle = rnd.NextDouble() * Math.PI * 2;
            float dist = (1 - t) * (6 + (float)rnd.NextDouble() * 5);
            var col = i % 3 == 0 ? Palette.DarkGreen : i % 3 == 1 ? Palette.DarkPurple : Palette.Black;
            Px(b, c, at.X + (float)Math.Cos(angle) * dist, at.Y + (float)Math.Sin(angle) * dist, col);
        }
        if (t > 0.8f) c.Fill(b, (int)at.X - 1, (int)at.Y - 1, 3, 3, Palette.DarkPurple);
    }

    /// <summary>Round 11: two little notes bobbing from the caster to the target (bardic magic).</summary>
    private static void Song(SpriteBatch b, PixelCanvas c, Vector2 from, Vector2 at, float t)
    {
        t = Math.Clamp(t, 0, 1);
        for (int i = 0; i < 2; i++)
        {
            float k = Math.Clamp(t * 1.3f - i * 0.25f, 0, 1);
            var pos = Vector2.Lerp(from, at, k) + new Vector2(0, -4 - MathF.Sin(k * MathF.PI * 3) * 3);
            var col = i == 0 ? Palette.Pink : Palette.Yellow;
            // a note: a head and a stem
            c.Fill(b, (int)pos.X, (int)pos.Y + 2, 2, 2, col);
            c.Fill(b, (int)pos.X + 1, (int)pos.Y - 1, 1, 3, col);
        }
    }

    /// <summary>Round 11: vines curling up around the target from the ground.</summary>
    private static void Vine(SpriteBatch b, PixelCanvas c, Vector2 at, float t)
    {
        t = Math.Clamp(t, 0, 1);
        int h = (int)(t * 10);
        for (int i = 0; i < h; i++)
        {
            int sway = (int)Math.Round(Math.Sin(i * 0.9) * 2);
            Px(b, c, at.X - 4 + sway, at.Y + 5 - i, Palette.Green);
            Px(b, c, at.X + 4 - sway, at.Y + 5 - i, Palette.DarkGreen);
            if (i % 3 == 2) Px(b, c, at.X - 3 + sway, at.Y + 5 - i, Palette.Green);
        }
    }

    /// <summary>Single-pixel green and purple bubbles that rise and pop (poison ticks, poisoned blades).</summary>
    private static void Poison(SpriteBatch b, PixelCanvas c, Vector2 at, float t, Random rnd)
    {
        for (int i = 0; i < 7; i++)
        {
            float x = at.X - 5 + (float)rnd.NextDouble() * 10;
            float delay = (float)rnd.NextDouble() * 0.4f;
            float k = (t - delay) / (1 - delay);
            if (k < 0 || k > 1) continue;
            float y = at.Y + 3 - k * 9;
            var col = k > 0.8f ? Palette.DarkGreen : i % 3 == 0 ? Palette.DarkPurple : Palette.Green;
            Px(b, c, x, y, col);
            if (k < 0.5f) Px(b, c, x + 1, y, col);
        }
    }

    private static void Heal(SpriteBatch b, PixelCanvas c, Vector2 at, float t, Random rnd)
    {
        for (int i = 0; i < 5; i++)
        {
            float x = at.X - 6 + (float)rnd.NextDouble() * 12;
            float delay = (float)rnd.NextDouble() * 0.3f;
            float k = (t - delay) / (1 - delay);
            if (k < 0 || k > 1) continue;
            float y = at.Y + 4 - k * 12;
            var col = k < 0.7f ? Palette.Green : Palette.DarkGreen;
            Px(b, c, x, y, col);
            Px(b, c, x - 1, y, col);
            Px(b, c, x + 1, y, col);
            Px(b, c, x, y - 1, col);
            Px(b, c, x, y + 1, col);
        }
    }

    /// <summary>Blue brackets snapping outward with a few sparks (Block, Shield).</summary>
    private static void Shield(SpriteBatch b, PixelCanvas c, Vector2 at, float t)
    {
        int r = 5 + (int)(t * 4);
        var col = t < 0.5f ? Palette.White : Palette.Blue;
        for (int dy = -4; dy <= 4; dy++)
        {
            Px(b, c, at.X - r, at.Y + dy, col);
            Px(b, c, at.X + r, at.Y + dy, col);
        }
        Px(b, c, at.X - r + 1, at.Y - 4, col);
        Px(b, c, at.X - r + 1, at.Y + 4, col);
        Px(b, c, at.X + r - 1, at.Y - 4, col);
        Px(b, c, at.X + r - 1, at.Y + 4, col);
        if (t > 0.3f)
        {
            float d = 6 + t * 6;
            Px(b, c, at.X - d, at.Y - d * 0.5f, Palette.Blue);
            Px(b, c, at.X + d, at.Y - d * 0.5f, Palette.Blue);
        }
    }

    /// <summary>Grey smoke puffing out and settling when something dies.</summary>
    private static void Poof(SpriteBatch b, PixelCanvas c, Vector2 at, float t, Random rnd)
    {
        float ease = 1 - (1 - t) * (1 - t);
        for (int i = 0; i < 10; i++)
        {
            float a = (float)(rnd.NextDouble() * Math.PI * 2);
            float d = (2 + (float)rnd.NextDouble() * 7) * ease;
            var col = t < 0.5f ? Palette.LightGrey : Palette.DarkGrey;
            Px(b, c, at.X + MathF.Cos(a) * d, at.Y + MathF.Sin(a) * d - t * 3, col);
        }
    }

    /// <summary>A white star burst on critical hits.</summary>
    private static void Crit(SpriteBatch b, PixelCanvas c, Vector2 at, float t)
    {
        int len = 2 + (int)(t * 7);
        int start = (int)(t * 4);
        for (int i = start; i < len; i++)
        {
            var col = i < len - 2 ? Palette.Yellow : Palette.White;
            Px(b, c, at.X + i, at.Y, col);
            Px(b, c, at.X - i, at.Y, col);
            Px(b, c, at.X, at.Y + i, col);
            Px(b, c, at.X, at.Y - i, col);
            if (i < len - 2)
            {
                Px(b, c, at.X + i * 0.7f, at.Y + i * 0.7f, col);
                Px(b, c, at.X - i * 0.7f, at.Y - i * 0.7f, col);
                Px(b, c, at.X + i * 0.7f, at.Y - i * 0.7f, col);
                Px(b, c, at.X - i * 0.7f, at.Y + i * 0.7f, col);
            }
        }
    }
}
