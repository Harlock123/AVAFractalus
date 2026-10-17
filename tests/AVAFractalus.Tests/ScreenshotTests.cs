using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAFractalus.Engine;
using AVAFractalus.Views;
using Controls = AVAFractalus.Engine.Controls;

namespace AVAFractalus.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set FRACTALUS_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avafractalus-ui-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("FRACTALUS_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAFractalus-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 48210, 3);
        g.HighScores.Insert("AVA", 21500, 1);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 300 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>A planet flown by the computer pilot until something worth showing.</summary>
    private static void Play(int level, string name, Func<GameEngine, bool> moment, int seconds = 300, Action<GameEngine>? setup = null)
    {
        var (w, c, g) = Open();
        g.Go(level);
        g.SetLives(20);
        g.Pilot.Reset();
        setup?.Invoke(g);
        Assert.True(PlayUntil(g, c, moment, seconds), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    private static double Nearest(GameEngine g, Func<World, IEnumerable<(double X, double Y)>> things)
    {
        var s = g.World.Ship;
        return things(g.World).Select(p => PlanetDef.Distance(p.X, p.Y, s.X, s.Y)).DefaultIfEmpty(1e9).Min();
    }

    [AvaloniaFact]
    public void Over_the_mountains() => Play(0, "mountains", g => g.World.Clock > 6 && g.World.Ship.Alt - g.World.Planet.Ground(g.World.Ship.X, g.World.Ship.Y) < 120);

    [AvaloniaFact]
    public void A_pilot_running_to_the_fighter() => Play(0, "pilot", g => g.World.Pilots.Any(p => p.State == PilotState.Running && PlanetDef.Distance(p.X, p.Y, g.World.Ship.X, g.World.Ship.Y) is > 40 and < 140
        && Math.Abs(Sim.Wrap(Sim.Bearing(g.World.Ship.X, g.World.Ship.Y, p.X, p.Y) - g.World.Ship.Heading)) < 0.35
        && Sim.Clear(g.World.Planet, g.World.Ship.X, g.World.Ship.Y, g.World.Ship.Alt + 20, p.X, p.Y, g.World.Planet.Ground(p.X, p.Y) + 2)));

    [AvaloniaFact]
    public void Knock_knock() => Play(0, "knock", g => g.World.Pilots.Any(p => p.State == PilotState.Knocking && !p.Def.Jaggi && p.T > 1));

    [AvaloniaFact]
    public void A_Jaggi()
    {
        var (w, c, g) = Open();
        g.Go(1);
        g.SetLives(20);
        Assert.True(PlayUntil(g, c, g => g.World.Face >= 0, 400), "never saw a Jaggi");
        // Sitting still for a look at it.
        for (int i = 0; i < 45; i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Shot(w, c, "jaggi");
    }

    [AvaloniaFact]
    public void A_gun_on_a_peak() => Play(1, "gun", g => g.World.Bolts.Count(b => b.Mine) >= 1 && Nearest(g, w => w.Guns.Where(x => !x.Down).Select(x => (x.Def.X, x.Def.Y))) < 700);

    [AvaloniaFact]
    public void A_saucer() => Play(2, "saucer", g => Nearest(g, w => w.Saucers.Select(s => (s.X, s.Y))) < 420 && g.World.Bolts.Count >= 1);

    [AvaloniaFact]
    public void Night() => Play(4, "night", g => g.World.Clock > 8 && g.World.Pilots.Any(p => p.State == PilotState.Waiting && PlanetDef.Distance(p.X, p.Y, g.World.Ship.X, g.World.Ship.Y) < 600));

    [AvaloniaFact]
    public void Docked() => Play(0, "docked", g => g.Phase == Phase.Docked && g.PhaseTime > 0.4, 400);

    [AvaloniaFact]
    public void Game_over()
    {
        var (w, c, g) = Open();
        g.Go(0);
        g.SetLives(1);
        g.World.Score = 3420;
        g.World.Ship.Energy = 0;
        for (int i = 0; i < 600 && !(g.State == GameState.GameOver && g.StateTime > 1); i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Shot(w, c, "game-over");
    }
}
