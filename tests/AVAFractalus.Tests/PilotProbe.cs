using AVAFractalus.Engine;

namespace AVAFractalus.Tests;

/// <summary>How the computer pilot gets on on each planet (set FRACTALUS_PROBE to run it, for tuning).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_pilot_does()
    {
        if (Environment.GetEnvironmentVariable("FRACTALUS_PROBE") is not { } mode) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 6; i++)
        {
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "frac-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.Go(i);
            g.SetLives(30);
            int t = 0, lost = 0, jaggiIn = 0;
            var last = g.Phase;
            var doing = new Dictionary<string, int>();
            var causes = new Dictionary<string, int>();
            var hurt = new Dictionary<int, int>();
            double e0 = 100;
            var crashes = new List<string>();
            for (; t < 60 * 900 && g.State == GameState.Playing && g.Cleared == 0; t++)
            {
                var c = mode == "lazy" ? new Controls(0, 0, true) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Dying && last != Phase.Dying) { lost++; causes[g.World.Cause] = causes.GetValueOrDefault(g.World.Cause) + 1; }
                last = g.Phase;
                doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                double e1 = g.World.Ship.Energy;
                if (e1 < e0 - 1) { int k = (int)Math.Round(e0 - e1); hurt[k] = hurt.GetValueOrDefault(k) + 1; if (k == 6) crashes.Add($"{g.Pilot.Doing}@{g.World.Ship.Speed:0}/{g.World.Ship.Vz:0}"); }
                e0 = e1;
            }
            var w = g.World;
            jaggiIn = w.Pilots.Count(p => p.Def.Jaggi && p.State == PilotState.Gone);
            output.WriteLine($"{i + 1} {PlanetBook.Build(i).Name}: {(g.Cleared > 0 ? "DOCKED" : "not docked")} after {t / 60}s, {lost} lost ({string.Join(", ", causes.Select(c => $"{c.Key} {c.Value}"))}), rescued {w.Rescued}, score {w.Score}, hurt {string.Join(" ", hurt.OrderBy(h => h.Key).Select(h => $"{h.Key}:{h.Value}"))}; crashes: {string.Join("; ", crashes)}");
        }
        output.WriteLine($"total {sw.ElapsedMilliseconds} ms");
    }
}
