using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AVAFractalus.Engine;

namespace AVAFractalus.Views;

/// <summary>
/// The view from the cockpit, drawn a pixel at a time the way the 8-bit machines drew it: each column of the
/// screen is a ray marched out over the height map, the ground drawn up the column wherever it rises above what's
/// already there, shaded by its slope and fading into the haze. Pilots, wrecks, guns, saucers and bolts go in
/// after, each pixel kept only where it's nearer than the ground.
/// </summary>
public sealed class Renderer
{
    public const int W = 320, H = 190;
    public const double HalfFov = 0.62, Far = 2600;
    private readonly uint[] _px = new uint[W * H];
    private readonly float[] _z = new float[W * H];
    private readonly double _focal = W / 2.0 / Math.Tan(HalfFov);
    private WriteableBitmap? _bitmap;

    private double _x, _y, _alt, _heading, _horizon, _night;
    private double _fx, _fy, _rx, _ry;

    public double Horizon => _horizon;

    public Bitmap Render(World w, double clock)
    {
        var s = w.Ship;
        var p = w.Planet;
        _x = s.X;
        _y = s.Y;
        // The cockpit sits up on the landing legs when it's down.
        _alt = s.Alt + (s.Landed || s.Settling > 0 ? 20 : 2);
        _heading = s.Heading;
        _night = p.Night;
        // Climbing tips the nose (and so the horizon) a little.
        _horizon = H * 0.42 + Math.Clamp(s.Vz * 0.25, -22, 22);
        (_fx, _fy) = Sim.Ahead(_heading);
        (_rx, _ry) = (-_fy, _fx);
        Sky(clock);
        Ground(p);
        Things(w, clock);
        return Blit();
    }

    // ───────────────────────────── the sky and the ground ─────────────────────────────

    private uint SkyAt(int y)
    {
        double t = Math.Clamp(y / _horizon, 0, 1);
        // Day: a dusty violet over an orange haze; night: near black over a dim red glow.
        double r = Lerp(Lerp(70, 8, _night), Lerp(230, 90, _night), t * t);
        double g = Lerp(Lerp(40, 6, _night), Lerp(140, 40, _night), t * t);
        double b = Lerp(Lerp(110, 28, _night), Lerp(110, 50, _night), t * t);
        return Rgb(r, g, b);
    }

    private void Sky(double clock)
    {
        for (int y = 0; y < H; y++)
        {
            uint c = SkyAt(y);
            for (int x = 0; x < W; x++)
            {
                _px[y * W + x] = c;
                _z[y * W + x] = float.MaxValue;
            }
        }
        if (_night > 0.2)
        {
            // Stars, fixed to the heading.
            for (int i = 0; i < 90; i++)
            {
                uint h = (uint)(i * 2654435761u);
                double a = (h % 6283) / 1000.0;
                double off = Sim.Wrap(a - _heading);
                if (Math.Abs(off) > HalfFov) continue;
                int sx = (int)(W / 2 + Math.Tan(off) * _focal), sy = (int)((h >> 13) % (uint)Math.Max(1, _horizon - 10));
                if (sx >= 0 && sx < W && sy >= 0 && sy < H) _px[sy * W + sx] = Rgb(200 * _night, 200 * _night, 220 * _night);
            }
        }
    }

    private void Ground(PlanetDef p)
    {
        uint haze = SkyAt((int)_horizon);
        double hr = (haze >> 16) & 0xFF, hg = (haze >> 8) & 0xFF, hb = haze & 0xFF;
        for (int col = 0; col < W; col++)
        {
            double off = Math.Atan((col - W / 2.0) / _focal);
            double a = _heading + off;
            double dx = Math.Sin(a), dy = -Math.Cos(a), corr = Math.Cos(off);
            int ybuf = H;
            double z = 3, dz = 1.2;
            while (z < Far && ybuf > 0)
            {
                double x = _x + dx * z, y = _y + dy * z;
                double h = p.Ground(x, y);
                double zc = z * corr;
                int sy = (int)(_horizon + (_alt - h) / zc * _focal);
                if (sy < ybuf)
                {
                    // Lit from the north-west: slopes facing it bright, facing away dark.
                    double slope = (p.Ground(x - 6, y - 6) - h) * 0.05;
                    double light = Math.Clamp(1.0 + slope, 0.45, 1.45);
                    var (r, g, b) = Rock(h);
                    double fog = Math.Pow(Math.Min(1, z / Far), 1.4);
                    double dark = 1 - _night * 0.55;
                    uint c = Rgb(Lerp(r * light * dark, hr, fog), Lerp(g * light * dark, hg, fog), Lerp(b * light * dark, hb, fog));
                    int top = Math.Max(0, sy);
                    for (int yy = top; yy < ybuf; yy++)
                    {
                        _px[yy * W + col] = c;
                        _z[yy * W + col] = (float)zc;
                    }
                    ybuf = top;
                }
                z += dz;
                dz *= 1.016;
            }
        }
    }

    /// <summary>The rock's colour by height: dark rust in the valleys, ochre slopes, pale peaks.</summary>
    private static (double R, double G, double B) Rock(double h)
    {
        double t = Math.Clamp(h / 620, 0, 1);
        if (t < 0.35) return (Lerp(70, 140, t / 0.35), Lerp(34, 70, t / 0.35), Lerp(30, 38, t / 0.35));
        if (t < 0.7) return (Lerp(140, 200, (t - 0.35) / 0.35), Lerp(70, 130, (t - 0.35) / 0.35), Lerp(38, 70, (t - 0.35) / 0.35));
        return (Lerp(200, 236, (t - 0.7) / 0.3), Lerp(130, 210, (t - 0.7) / 0.3), Lerp(70, 170, (t - 0.7) / 0.3));
    }

    // ───────────────────────────── things on and over the ground ─────────────────────────────

    /// <summary>Where a point is on the screen, how far ahead it is, and how many pixels a metre is there.</summary>
    private bool Project(double ox, double oy, double oz, out double sx, out double sy, out double f, out double k)
    {
        var (dx, dy) = PlanetDef.Toward(_x, _y, ox, oy);
        f = dx * _fx + dy * _fy;
        double r = dx * _rx + dy * _ry;
        sx = sy = k = 0;
        if (f < 4 || f > Far) return false;
        k = _focal / f;
        sx = W / 2.0 + r * k;
        sy = _horizon + (_alt - oz) * k;
        return sx > -60 && sx < W + 60;
    }

    private void Things(World w, double clock)
    {
        var p = w.Planet;
        foreach (var pl in w.Pilots)
        {
            // The wreck stays where he came down.
            if (Project(pl.Def.X + 14, pl.Def.Y, p.Ground(pl.Def.X + 14, pl.Def.Y), out var wx, out var wy, out var wf, out var wk))
                Wreck(wx, wy, wf, wk);
            // (One at the airlock is beside the cockpit, not in front of it.)
            if (pl.State is PilotState.Aboard or PilotState.Gone or PilotState.Knocking) continue;
            double gz = p.Ground(pl.X, pl.Y);
            if (!Project(pl.X, pl.Y, gz, out var sx, out var sy, out var f, out var k)) continue;
            Figure(sx, sy, f, Math.Max(k, 0.5), pl.State == PilotState.Running ? clock : 0);
            // His beacon, blinking.
            if (pl.State == PilotState.Waiting && (int)(clock * 3) % 2 == 0)
                Box(sx - 1, sy - Math.Max(4, 6 * k) - 4, 2, 2, f - 1, Rgb(255, 255, 120));
        }
        foreach (var g in w.Guns)
            if (Project(g.Def.X, g.Def.Y, g.Z, out var sx, out var sy, out var f, out var k))
                Gun(sx, sy, f, k, g.Down, clock);
        foreach (var sc in w.Saucers)
            if (Project(sc.X, sc.Y, sc.Z, out var sx, out var sy, out var f, out var k))
                Saucer(sx, sy, f, k, clock);
        if (w.AllAboard && Project(p.StartX, p.StartY, Field.DockHeight + 200, out var mx, out var my, out var mf, out var mk))
            MotherShip(mx, my, Math.Max(mk, 0.08), clock);
        foreach (var b in w.Bolts)
            if (Project(b.X, b.Y, b.Z, out var sx, out var sy, out var f, out var k))
            {
                double r = Math.Max(1.2, 1.5 * k);
                Disc(sx, sy, r, f, b.Mine ? Rgb(140, 255, 255) : Rgb(255, 90, 60));
            }
        foreach (var bm in w.Booms)
            if (Project(bm.X, bm.Y, bm.Z, out var sx, out var sy, out var f, out var k))
            {
                double r = (bm.Kind == Blast.Big ? 26 : 10) * Math.Min(1, bm.T * 4) * k + 1;
                uint c = bm.T < 0.2 ? Rgb(255, 255, 220) : bm.T < 0.5 ? Rgb(255, 180, 60) : Rgb(150, 70, 40);
                Disc(sx, sy, r, f - 5, c);
            }
    }

    private void Wreck(double sx, double sy, double f, double k)
    {
        k = Math.Max(k, 0.25);
        uint hull = Shade(Rgb(150, 160, 170), f), dark = Shade(Rgb(70, 74, 80), f);
        Box(sx - 7 * k, sy - 2.5 * k, 14 * k, 2.5 * k, f, hull);
        Box(sx - 2 * k, sy - 5 * k, 6 * k, 2.5 * k, f, dark);
        Box(sx + 5 * k, sy - 4 * k, 1.5 * k, 2 * k, f, hull);
    }

    /// <summary>A pilot in his flight suit and helmet (or something wearing one), running if <paramref name="run"/> moves.</summary>
    private void Figure(double sx, double sy, double f, double k, double run)
    {
        k *= 2.6;
        uint suit = Shade(Rgb(230, 120, 40), f), helmet = Shade(Rgb(235, 235, 240), f), visor = Shade(Rgb(40, 60, 90), f);
        double step = run > 0 ? Math.Sin(run * 12) * 0.5 * k : 0;
        Box(sx - 0.5 * k + step, sy - 0.9 * k, 0.4 * k, 0.9 * k, f, suit);
        Box(sx + 0.1 * k - step, sy - 0.9 * k, 0.4 * k, 0.9 * k, f, suit);
        Box(sx - 0.55 * k, sy - 1.8 * k, 1.1 * k, 1.0 * k, f, suit);
        Box(sx - 0.45 * k, sy - 2.35 * k, 0.9 * k, 0.6 * k, f, helmet);
        Box(sx - 0.3 * k, sy - 2.2 * k, 0.6 * k, 0.25 * k, f, visor);
    }

    private void Gun(double sx, double sy, double f, double k, bool down, double clock)
    {
        k = Math.Max(k, 0.2);
        if (down)
        {
            Disc(sx, sy - 2 * k, 5 * k, f, Shade(Rgb(40, 30, 28), f));
            return;
        }
        uint body = Shade(Rgb(90, 100, 80), f);
        Disc(sx, sy - 4 * k, 6 * k, f, body);
        Box(sx - 6 * k, sy - 4 * k, 12 * k, 4 * k, f, body);
        Box(sx - 1 * k, sy - 14 * k, 2 * k, 8 * k, f, Shade(Rgb(60, 66, 54), f));
        if ((int)(clock * 2 + sx) % 2 == 0) Box(sx - 0.8 * k, sy - 8 * k, 1.6 * k, 1.6 * k, f - 1, Rgb(255, 60, 40));
    }

    private void Saucer(double sx, double sy, double f, double k, double clock)
    {
        k = Math.Max(k, 0.15);
        uint body = Rgb(170, 175, 190), dome = Rgb(120, 220, 160);
        for (int i = -6; i <= 6; i++)
        {
            double half = Math.Sqrt(1 - i * i / 49.0) * 12 * k;
            Box(sx - half, sy + i * 0.5 * k, half * 2, 0.5 * k + 1, f, i < 0 ? body : Shade(Rgb(110, 115, 130), f));
        }
        Disc(sx, sy - 3 * k, 4.5 * k, f - 1, dome);
        for (int i = 0; i < 4; i++)
        {
            double a = clock * 4 + i * Math.PI / 2;
            Box(sx + Math.Cos(a) * 9 * k - 0.7 * k, sy + 0.5 * k, 1.4 * k + 1, 1.2 * k + 1, f - 1, i % 2 == 0 ? Rgb(255, 230, 80) : Rgb(255, 80, 80));
        }
    }

    private void MotherShip(double sx, double sy, double k, double clock)
    {
        uint hull = Rgb(60, 64, 80);
        Box(sx - 120 * k, sy - 12 * k, 240 * k, 24 * k, 1e9f, hull);
        Box(sx - 60 * k, sy - 30 * k, 120 * k, 20 * k, 1e9f, Rgb(80, 84, 100));
        for (int i = -5; i <= 5; i++)
            if ((int)(clock * 3 + i) % 3 != 0) Box(sx + i * 20 * k - 2, sy + 10 * k, 3, 2, 1e9f, Rgb(255, 220, 120));
    }

    // ───────────────────────────── pixels ─────────────────────────────

    private uint Shade(uint c, double f)
    {
        double fog = Math.Pow(Math.Min(1, f / Far), 1.4), dark = 1 - _night * 0.4;
        uint haze = SkyAt((int)_horizon);
        return Rgb(Lerp(((c >> 16) & 0xFF) * dark, (haze >> 16) & 0xFF, fog), Lerp(((c >> 8) & 0xFF) * dark, (haze >> 8) & 0xFF, fog), Lerp((c & 0xFF) * dark, haze & 0xFF, fog));
    }

    private void Box(double x, double y, double w, double h, double depth, uint c)
    {
        int x0 = (int)Math.Max(0, Math.Floor(x)), x1 = (int)Math.Min(W, Math.Ceiling(x + Math.Max(1, w)));
        int y0 = (int)Math.Max(0, Math.Floor(y)), y1 = (int)Math.Min(H, Math.Ceiling(y + Math.Max(1, h)));
        float d = (float)depth;
        for (int yy = y0; yy < y1; yy++)
        for (int xx = x0; xx < x1; xx++)
        {
            int i = yy * W + xx;
            if (d < _z[i]) { _px[i] = c; _z[i] = d; }
        }
    }

    private void Disc(double cx, double cy, double r, double depth, uint c)
    {
        int x0 = (int)Math.Max(0, cx - r), x1 = (int)Math.Min(W - 1, cx + r), y0 = (int)Math.Max(0, cy - r), y1 = (int)Math.Min(H - 1, cy + r);
        float d = (float)depth;
        for (int yy = y0; yy <= y1; yy++)
        for (int xx = x0; xx <= x1; xx++)
        {
            if ((xx - cx) * (xx - cx) + (yy - cy) * (yy - cy) > r * r) continue;
            int i = yy * W + xx;
            if (d < _z[i]) { _px[i] = c; _z[i] = d; }
        }
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static uint Rgb(double r, double g, double b) =>
        0xFF000000u | (uint)Math.Clamp(r, 0, 255) << 16 | (uint)Math.Clamp(g, 0, 255) << 8 | (uint)Math.Clamp(b, 0, 255);

    private Bitmap Blit()
    {
        _bitmap ??= new WriteableBitmap(new PixelSize(W, H), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var fb = _bitmap.Lock())
        {
            unsafe
            {
                for (int y = 0; y < H; y++)
                    _px.AsSpan(y * W, W).CopyTo(new Span<uint>((void*)(fb.Address + y * fb.RowBytes), W));
            }
        }
        return _bitmap;
    }
}
