using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AVAFractalus.Engine;

namespace AVAFractalus.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAFractalus/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused;

    private const double ViewH = 608, Scale = Field.Width / Renderer.W;
    private readonly Renderer _renderer = new();

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, Field.Height));
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            case GameState.GameOver: DrawGameOver(dc); break;
            default:
                DrawView(dc, g.World);
                DrawCockpit(dc, g.World);
                DrawPanel(dc, g.World);
                if (g.Message is { } msg)
                {
                    double wdt = Measure(msg, 38) + 40;
                    dc.FillRectangle(Brush(0xB0000000), new Rect(512 - wdt / 2, 180, wdt, 56), 6);
                    Text(dc, msg, new Point(512, 208), 38, g.Phase == Phase.Dying ? Red : Yellow, center: true);
                }
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, 40), 22, Yellow, center: true);
    }

    private static double Measure(string text, double size) =>
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(White)).Width;

    // ───────────────────────────── through the canopy ─────────────────────────────

    private void DrawView(DrawingContext dc, World w)
    {
        var bmp = _renderer.Render(w, _clock);
        using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
            dc.DrawImage(bmp, new Rect(0, 0, Renderer.W, Renderer.H), new Rect(0, 0, Field.Width, ViewH));
    }

    private void DrawCockpit(DrawingContext dc, World w)
    {
        var s = w.Ship;
        const uint frame = 0xFF1C1E26, edge = 0xFF3A3E4A;
        // The canopy's frame: struts up from the panel, and the bar across the top.
        Poly(dc, frame, edge, 2, new Point(0, 0), new Point(150, 0), new Point(70, ViewH), new Point(0, ViewH));
        Poly(dc, frame, edge, 2, new Point(Field.Width, 0), new Point(Field.Width - 150, 0), new Point(Field.Width - 70, ViewH), new Point(Field.Width, ViewH));
        Poly(dc, frame, edge, 2, new Point(150, 0), new Point(Field.Width - 150, 0), new Point(Field.Width - 160, 22), new Point(160, 22));
        // Rivets.
        for (int i = 0; i < 6; i++)
        {
            dc.FillEllipse(30 + i * 8, 80 + i * 90, 3, edge);
            dc.FillEllipse(Field.Width - 30 - i * 8, 80 + i * 90, 3, edge);
        }
        // The sight: where the bolts go.
        double cy = _renderer.Horizon * Scale;
        uint sight = s.Landed ? 0x6040FF40 : 0xC040FF40;
        dc.DrawLine(Pen(sight, 2), new Point(512 - 30, cy), new Point(512 - 10, cy));
        dc.DrawLine(Pen(sight, 2), new Point(512 + 10, cy), new Point(512 + 30, cy));
        dc.DrawLine(Pen(sight, 2), new Point(512, cy - 24), new Point(512, cy - 8));
        dc.DrawLine(Pen(sight, 2), new Point(512, cy + 8), new Point(512, cy + 24));
        // Whoever is at the airlock: a pilot's helmet at the side window, or a Jaggi's face on the canopy.
        var at = w.Pilots.FirstOrDefault(p => p.State == PilotState.Knocking);
        if (w.Face >= 0) Jaggi(dc, Math.Min(1, w.Face * 3), w.Face);
        else if (at != null) Helmet(dc, Math.Min(1, at.T * 2));
        if (s.Energy < 25 && !s.Dead && (int)(_clock * 4) % 2 == 0)
            dc.DrawRectangle(null, Pen(0xA0FF2020, 8), new Rect(4, 4, Field.Width - 8, ViewH - 8));
    }

    /// <summary>A rescued pilot's helmet rising at the left-hand window, knocking.</summary>
    private void Helmet(DrawingContext dc, double rise)
    {
        double x = 190, y = ViewH - 140 * rise + 40;
        dc.FillEllipse(x, y, 62, 0xFFE8E8EC);
        dc.FillEllipse(x, y + 6, 46, 0xFF203048);
        dc.FillEllipse(x - 12, y - 6, 12, 0x60FFFFFF);
        dc.FillRectangle(Brush(0xFFE0782A), new Rect(x - 70, y + 52, 140, 60), 10);
        double knock = Math.Abs(Math.Sin(_clock * 8)) * 16;
        dc.FillEllipse(x + 74, y - 10 - knock, 16, 0xFFE0782A);
        Text(dc, "KNOCK KNOCK", new Point(x, y - 86), 26, White, center: true);
    }

    /// <summary>The Jaggi: its face pressed to the glass, growing, eyes glaring.</summary>
    private void Jaggi(DrawingContext dc, double grow, double t)
    {
        double cx = 512, cy = 300, k = 0.5 + grow * 0.9;
        dc.DrawEllipse(Brush(0xFF3AA040), Pen(0xFF1A5020, 6), new Point(cx, cy), 210 * k, 250 * k);
        // Brow ridges, eyes, the mouth.
        Poly(dc, 0xFF2A7A30, null, 0, new Point(cx - 180 * k, cy - 90 * k), new Point(cx - 20 * k, cy - 40 * k), new Point(cx - 40 * k, cy - 120 * k));
        Poly(dc, 0xFF2A7A30, null, 0, new Point(cx + 180 * k, cy - 90 * k), new Point(cx + 20 * k, cy - 40 * k), new Point(cx + 40 * k, cy - 120 * k));
        foreach (int side in new[] { -1, 1 })
        {
            dc.DrawEllipse(Brush(0xFFFFF060), null, new Point(cx + side * 90 * k, cy - 40 * k), 48 * k, 30 * k);
            dc.DrawEllipse(Brush(0xFF100000), null, new Point(cx + side * 90 * k + Math.Sin(t * 5) * 8 * k, cy - 40 * k), 12 * k, 26 * k);
        }
        dc.DrawEllipse(Brush(0xFF200808), null, new Point(cx, cy + 120 * k), 120 * k, 50 * k * (0.7 + 0.3 * Math.Abs(Math.Sin(t * 7))));
        for (int i = -4; i <= 4; i++)
            Poly(dc, 0xFFF0F0D8, null, 0, new Point(cx + i * 24 * k - 9 * k, cy + 82 * k), new Point(cx + i * 24 * k + 9 * k, cy + 82 * k), new Point(cx + i * 24 * k, cy + 108 * k));
        // Hands on the glass.
        foreach (int side in new[] { -1, 1 })
            dc.DrawEllipse(Brush(0xFF3AA040), Pen(0xFF1A5020, 4), new Point(cx + side * 300 * k, cy + 40 * k + Math.Sin(t * 9 + side) * 20), 60 * k, 70 * k);
        Text(dc, "JAGGI!", new Point(cx, 110), 60, (int)(_clock * 6) % 2 == 0 ? Red : Yellow, center: true);
    }

    // ───────────────────────────── the instrument panel ─────────────────────────────

    private void DrawPanel(DrawingContext dc, World w)
    {
        var g = Engine;
        var s = w.Ship;
        var p = w.Planet;
        double y0 = ViewH;
        dc.FillRectangle(Brush(0xFF22242E), new Rect(0, y0, Field.Width, Field.Height - y0));
        dc.FillRectangle(Brush(0xFF3A3E4A), new Rect(0, y0, Field.Width, 4));
        // The long-range scanner: the pilots' beacons, the way ahead up.
        double cx = 512, cy = y0 + 82, r = 70;
        dc.DrawEllipse(Brush(0xFF061A0C), Pen(0xFF2A6A3A, 3), new Point(cx, cy), r, r);
        dc.DrawEllipse(null, Pen(0xFF1A4A28, 1), new Point(cx, cy), r / 2, r / 2);
        dc.DrawLine(Pen(0xFF1A4A28, 1), new Point(cx - r, cy), new Point(cx + r, cy));
        dc.DrawLine(Pen(0xFF1A4A28, 1), new Point(cx, cy - r), new Point(cx, cy + r));
        const double range = 2400;
        var (fx, fy) = Sim.Ahead(s.Heading);
        void Blip(double x, double y, double size, uint c)
        {
            var (dx, dy) = PlanetDef.Toward(s.X, s.Y, x, y);
            double f = dx * fx + dy * fy, side = dx * -fy + dy * fx;
            double px = side / range * r, py = -f / range * r;
            double len = Math.Sqrt(px * px + py * py);
            if (len > r - 4) { px *= (r - 4) / len; py *= (r - 4) / len; }
            dc.FillEllipse(cx + px, cy + py, size, c);
        }
        foreach (var pl in w.Pilots)
            if (pl.State is PilotState.Waiting or PilotState.Running or PilotState.Knocking)
                Blip(pl.X, pl.Y, 4, (int)(_clock * 3) % 2 == 0 ? 0xFFFFF060 : 0xFFB0A040);
        foreach (var sc in w.Saucers) Blip(sc.X, sc.Y, 3.5, 0xFFFF5050);
        if (w.AllAboard) Blip(p.StartX, p.StartY, 5, 0xFF60C0FF);
        Poly(dc, 0xFF80FF90, null, 0, new Point(cx, cy - 7), new Point(cx + 5, cy + 5), new Point(cx - 5, cy + 5));
        Text(dc, "SCANNER", new Point(cx, y0 + 152), 16, Dim, center: true);
        // Left: altitude, speed, heading.
        double above = s.Alt - p.Ground(s.X, s.Y);
        Gauge(dc, 30, y0 + 20, "ALTITUDE", $"{(int)above,4} M", above < 40 && !s.Landed ? Red : Cyan);
        Gauge(dc, 30, y0 + 70, "SPEED", $"{(int)s.Speed,4}", Cyan);
        int deg = ((int)Math.Round(s.Heading * 180 / Math.PI) + 360) % 360;
        Gauge(dc, 30, y0 + 120, "HEADING", $"{deg:000}", Cyan);
        // Energy.
        Text(dc, "ENERGY", new Point(200, y0 + 30), 20, Dim);
        dc.FillRectangle(Brush(0xFF101218), new Rect(280, y0 + 20, 150, 20), 3);
        dc.FillRectangle(Brush(s.Energy > 50 ? 0xFF40D060 : s.Energy > 25 ? Orange : Red), new Rect(280, y0 + 20, 150 * Math.Max(0, s.Energy) / 100, 20), 3);
        // Status lights.
        Light(dc, 200, y0 + 72, "LANDED", s.Landed);
        Light(dc, 300, y0 + 72, "LANDING", s.Settling > 0);
        bool knock = w.Pilots.Any(pl => pl.State == PilotState.Knocking);
        Light(dc, 200, y0 + 108, "AIRLOCK", knock && (int)(_clock * 3) % 2 == 0);
        Light(dc, 300, y0 + 108, "WARNING", w.Face >= 0 || s.Energy < 25);
        // Right: pilots, score, fighters left.
        Text(dc, $"PILOTS {w.Rescued}/{p.Humans}", new Point(620, y0 + 30), 30, w.AllAboard ? 0xFF60FF80 : White);
        Text(dc, $"SCORE {g.Score:000000}", new Point(620, y0 + 72), 28, Yellow);
        Text(dc, $"HIGH  {Math.Max(g.HighScores.TopScore, g.Score):000000}", new Point(620, y0 + 104), 22, Dim);
        Text(dc, p.Name, new Point(620, y0 + 136), 22, Dim);
        for (int i = 0; i < Math.Min(5, g.Lives - 1); i++)
        {
            double lx = 900 + i * 22, ly = y0 + 40;
            Poly(dc, 0xFFB0B8C8, null, 0, new Point(lx, ly - 12), new Point(lx + 9, ly + 8), new Point(lx, ly + 3), new Point(lx - 9, ly + 8));
        }
    }

    private static void Gauge(DrawingContext dc, double x, double y, string label, string value, uint c)
    {
        Text(dc, label, new Point(x, y + 10), 18, Dim);
        dc.FillRectangle(Brush(0xFF101218), new Rect(x + 86, y - 2, 64, 26), 3);
        Text(dc, value, new Point(x + 146, y + 11), 22, c, right: true, shadow: false);
    }

    private static void Light(DrawingContext dc, double x, double y, string label, bool on)
    {
        dc.FillRectangle(Brush(on ? 0xFFFF5030 : 0xFF3A2020), new Rect(x, y, 88, 24), 4);
        Text(dc, label, new Point(x + 44, y + 12), 16, on ? White : 0xFF805050, center: true, shadow: false);
    }

    // ───────────────────────────── the title and the end ─────────────────────────────

    private World? _titleWorld;

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        if (_titleWorld == null)
        {
            _titleWorld = new World();
            Sim.Start(_titleWorld, PlanetBook.Build(0), 1);
        }
        // A slow flight over the mountains behind the title.
        var s = _titleWorld.Ship;
        s.Heading = _clock * 0.08;
        s.Alt = _titleWorld.Planet.Ground(s.X, s.Y) + 260;
        var bmp = _renderer.Render(_titleWorld, _clock);
        using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
            dc.DrawImage(bmp, new Rect(0, 0, Renderer.W, Renderer.H), new Rect(0, 0, Field.Width, Field.Height));
        dc.FillRectangle(Brush(0xA0000000), new Rect(0, 30, Field.Width, 140));
        Text(dc, "AVA FRACTALUS", new Point(cx, 88), 100, Yellow, center: true);
        Text(dc, "AFTER LUCASFILM GAMES' 1985 RESCUE ON FRACTALUS!", new Point(cx, 148), 22, Cyan, center: true);
        dc.FillRectangle(Brush(0xB0000000), new Rect(0, 470, Field.Width, 298));
        Text(dc, "PILOTS ARE DOWN ON A HOSTILE PLANET. FLY YOUR FIGHTER OVER ITS FRACTAL MOUNTAINS, LAND BESIDE", new Point(cx, 492), 21, White, center: true);
        Text(dc, "EACH ONE, AND OPEN THE AIRLOCK WHEN HE KNOCKS. BUT LOOK BEFORE YOU OPEN: SOME OF THEM ARE JAGGI.", new Point(cx, 518), 21, White, center: true);
        Text(dc, "WITH EVERY PILOT ABOARD, CLIMB TO THE MOTHER SHIP.", new Point(cx, 544), 21, White, center: true);
        Text(dc, "PRESS ENTER TO START", new Point(cx, 600), 40, (int)(_clock * 2) % 2 == 0 ? Yellow : Orange, center: true);
        Text(dc, "ARROWS: FLY   W/S: FASTER/SLOWER   SPACE: FIRE   L: LAND / LIFT OFF   A: AIRLOCK   P: PAUSE", new Point(cx, 660), 20, Dim, center: true);
        Text(dc, $"HIGH SCORE {Engine.HighScores.TopScore:000000}", new Point(cx, 710), 24, Cyan, center: true);
    }

    private void DrawGameOver(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        Text(dc, "GAME OVER", new Point(cx, 240), 110, Red, center: true);
        Text(dc, "NO FIGHTERS LEFT IN THE MOTHER SHIP", new Point(cx, 330), 34, White, center: true);
        Text(dc, $"SCORE {Engine.Score}", new Point(cx, 420), 44, Yellow, center: true);
        Text(dc, $"PLANETS {Engine.Cleared}", new Point(cx, 476), 30, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "TOP RESCUERS", new Point(cx, 120), 64, 0xFFFFE040, center: true);
        Text(dc, "RANK NAME    SCORE  PLANETS", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,5}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
