namespace AVAFractalus.Engine;

/// <summary>
/// The fighter's controls: left and right to turn, up to climb and down to dive (up the screen is -1); faster and
/// slower; fire; land (or lift off); and open the airlock.
/// </summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false, bool Faster = false, bool Slower = false, bool Land = false, bool Airlock = false);

/// <summary>The screen, and the planet's surface.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    /// <summary>The height map: this many points on a side, this many metres apart. The surface wraps round.</summary>
    public const int N = 256;
    public const double Cell = 16;
    public const double Size = N * Cell;
    /// <summary>The mother ship waits above this height for the fighter to come up and dock.</summary>
    public const double DockHeight = 1500;
}

/// <summary>A downed pilot by his wrecked fighter, or a Jaggi waiting in a pilot's suit.</summary>
public readonly record struct PilotDef(double X, double Y, bool Jaggi);

/// <summary>A Jaggi gun on a peak.</summary>
public readonly record struct GunDef(double X, double Y);

/// <summary>
/// A planet's surface (a fractal height map), where the downed pilots are, which of them are Jaggi in disguise,
/// the guns on the peaks, and how hard the Jaggi fight.
/// </summary>
public sealed class PlanetDef
{
    public int Level;
    public string Name = "";
    public float[] Height = new float[Field.N * Field.N];
    public List<PilotDef> Pilots = [];
    public List<GunDef> Guns = [];
    public double StartX = Field.Size / 2, StartY = Field.Size / 2;
    /// <summary>How dark it is: 0 (day) to 1 (night).</summary>
    public double Night;
    public double SaucerEvery = 30, GunEvery = 3.0;

    public int Humans => Pilots.Count(p => !p.Jaggi);

    private float At(int x, int y) => Height[(y & (Field.N - 1)) * Field.N + (x & (Field.N - 1))];

    /// <summary>The ground's height at a point (smoothly between the map's points; the surface wraps).</summary>
    public double Ground(double x, double y)
    {
        double gx = x / Field.Cell, gy = y / Field.Cell;
        int ix = (int)Math.Floor(gx), iy = (int)Math.Floor(gy);
        double fx = gx - ix, fy = gy - iy;
        double a = At(ix, iy), b = At(ix + 1, iy), c = At(ix, iy + 1), d = At(ix + 1, iy + 1);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    /// <summary>How rough the ground is round a point: the most it rises or falls within a few metres.</summary>
    public double Roughness(double x, double y)
    {
        double h = Ground(x, y), most = 0;
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            most = Math.Max(most, Math.Abs(Ground(x + Math.Cos(a) * 24, y + Math.Sin(a) * 24) - h));
        }
        return most;
    }

    /// <summary>The shortest way from one point to another across a surface that wraps.</summary>
    public static (double Dx, double Dy) Toward(double fx, double fy, double tx, double ty)
    {
        double dx = tx - fx, dy = ty - fy;
        dx -= Math.Round(dx / Field.Size) * Field.Size;
        dy -= Math.Round(dy / Field.Size) * Field.Size;
        return (dx, dy);
    }

    public static double Distance(double fx, double fy, double tx, double ty)
    {
        var (dx, dy) = Toward(fx, fy, tx, ty);
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>The planets, one after another: more pilots to find, more Jaggi among them, and darker.</summary>
public static class PlanetBook
{
    private static readonly string[] Names = ["FRACTALUS", "FRACTALUS II", "FRACTALUS III", "FRACTALUS IV", "FRACTALUS V", "FRACTALUS VI"];

    public static PlanetDef Build(int level, int seed = 0)
    {
        var d = new Dice(7000 + level * 389 + seed * 17);
        var p = new PlanetDef
        {
            Level = level,
            Name = level < Names.Length ? Names[level] : $"FRACTALUS {level + 1}",
            Night = Math.Min(0.85, level * 0.17),
            SaucerEvery = Math.Max(10, 32 - level * 4),
            GunEvery = Math.Max(1.6, 3.8 - level * 0.35),
        };
        Fractal(p.Height, ref d, 0.58);
        // Lifted so the valleys are well above zero, and stretched to mountains.
        float lo = p.Height.Min(), hi = p.Height.Max();
        for (int i = 0; i < p.Height.Length; i++) p.Height[i] = (p.Height[i] - lo) / (hi - lo) * 620f;
        // Downed pilots: on lowish, flattish ground, spread out, away from the start.
        int pilots = 5 + Math.Min(level, 4), jaggi = 1 + level / 2;
        for (int tries = 0; p.Pilots.Count < pilots && tries < 20000; tries++)
        {
            double x = d.Next() * Field.Size, y = d.Next() * Field.Size;
            if (p.Ground(x, y) > 300 || p.Roughness(x, y) > 9) continue;
            if (PlanetDef.Distance(x, y, p.StartX, p.StartY) < 600) continue;
            if (p.Pilots.Any(o => PlanetDef.Distance(o.X, o.Y, x, y) < 700)) continue;
            p.Pilots.Add(new PilotDef(x, y, false));
        }
        // Some of them are Jaggi.
        for (int k = 0; k < jaggi && k < p.Pilots.Count - 2; k++)
        {
            int i = 1 + d.Next(p.Pilots.Count - 1);
            if (p.Pilots[i].Jaggi) { k--; continue; }
            p.Pilots[i] = p.Pilots[i] with { Jaggi = true };
        }
        // Guns on the highest peaks.
        int guns = 8 + level * 3;
        var peaks = new List<(double X, double Y, double H)>();
        for (int y = 0; y < Field.N; y += 2)
        for (int x = 0; x < Field.N; x += 2)
        {
            double h = p.Height[y * Field.N + x];
            bool top = true;
            for (int oy = -3; oy <= 3 && top; oy++)
            for (int ox = -3; ox <= 3 && top; ox++)
                if ((ox != 0 || oy != 0) && p.Height[((y + oy) & (Field.N - 1)) * Field.N + ((x + ox) & (Field.N - 1))] > h) top = false;
            if (top && h > 330) peaks.Add((x * Field.Cell, y * Field.Cell, h));
        }
        foreach (var pk in peaks.OrderBy(_ => d.Next()))
        {
            if (p.Guns.Count >= guns) break;
            if (PlanetDef.Distance(pk.X, pk.Y, p.StartX, p.StartY) < 900) continue;
            if (p.Guns.Any(g => PlanetDef.Distance(g.X, g.Y, pk.X, pk.Y) < 500)) continue;
            p.Guns.Add(new GunDef(pk.X, pk.Y));
        }
        return p;
    }

    /// <summary>Diamond-square: a fractal height map that wraps at its edges.</summary>
    private static void Fractal(float[] h, ref Dice d, double rough)
    {
        int n = Field.N;
        float Get(int x, int y) => h[(y & (n - 1)) * n + (x & (n - 1))];
        void Set(int x, int y, float v) => h[(y & (n - 1)) * n + (x & (n - 1))] = v;
        double scale = 1;
        for (int step = n; step > 1; step /= 2)
        {
            int half = step / 2;
            for (int y = 0; y < n; y += step)
            for (int x = 0; x < n; x += step)
            {
                float avg = (Get(x, y) + Get(x + step, y) + Get(x, y + step) + Get(x + step, y + step)) / 4;
                Set(x + half, y + half, avg + (float)((d.Next() * 2 - 1) * scale));
            }
            for (int y = 0; y < n; y += half)
            for (int x = (y / half % 2 == 0) ? half : 0; x < n; x += step)
            {
                float avg = (Get(x - half, y) + Get(x + half, y) + Get(x, y - half) + Get(x, y + half)) / 4;
                Set(x, y, avg + (float)((d.Next() * 2 - 1) * scale));
            }
            scale *= rough;
        }
    }
}
