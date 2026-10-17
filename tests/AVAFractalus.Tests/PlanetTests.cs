using AVAFractalus.Engine;

namespace AVAFractalus.Tests;

/// <summary>The planets: built to be flown over, and done by the computer pilot.</summary>
public class PlanetTests(ITestOutputHelper output)
{
    public static TheoryData<int> Planets() => [.. Enumerable.Range(0, 6)];

    [Theory]
    [MemberData(nameof(Planets))]
    public void Pilots_down_on_level_ground_some_of_them_Jaggi_and_guns_on_the_peaks(int level)
    {
        var p = PlanetBook.Build(level);
        Assert.Equal(5 + Math.Min(level, 4), p.Pilots.Count);
        Assert.Equal(1 + level / 2, p.Pilots.Count(x => x.Jaggi));
        Assert.All(p.Pilots, x => Assert.True(p.Roughness(x.X, x.Y) < 9));
        Assert.All(p.Guns, g => Assert.True(p.Ground(g.X, g.Y) > 330));
        Assert.True(p.Guns.Count >= 6);
        Assert.InRange(p.Height.Min(), 0, 1);
        Assert.InRange(p.Height.Max(), 619, 621);
    }

    [Fact]
    public void The_surface_wraps_without_a_seam()
    {
        var p = PlanetBook.Build(0);
        for (double y = 0; y < Field.Size; y += 300)
            Assert.Equal(p.Ground(0.001, y), p.Ground(Field.Size + 0.001, y), 3);
    }

    [Fact]
    public void Each_planet_is_darker_with_more_Jaggi_than_the_last()
    {
        for (int i = 1; i < 6; i++)
        {
            var a = PlanetBook.Build(i - 1);
            var b = PlanetBook.Build(i);
            Assert.True(b.Night >= a.Night);
            Assert.True(b.Pilots.Count(x => x.Jaggi) >= a.Pilots.Count(x => x.Jaggi));
            Assert.True(b.GunEvery <= a.GunEvery);
        }
    }

    [Theory]
    [MemberData(nameof(Planets))]
    public void Every_planet_is_done_by_the_computer_pilot_without_letting_a_Jaggi_in(int level)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avafractalus-tests-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        g.Go(level);
        g.SetLives(10);
        int t = 0, lost = 0;
        var last = g.Phase;
        for (; t < 60 * 600 && g.State == GameState.Playing && g.Phase != Phase.Docked; t++)
        {
            g.Tick(g.Pilot.Decide(g));
            if (g.Phase == Phase.Dying && last != Phase.Dying) lost++;
            last = g.Phase;
        }
        output.WriteLine($"{g.World.Planet.Name}: {g.Phase} after {t / 60}s, {lost} fighters lost, {g.World.Rescued} rescued");
        Assert.Equal(Phase.Docked, g.Phase);
        Assert.True(lost <= 1, $"{lost} fighters lost");
        Assert.Equal(g.World.Planet.Humans, g.World.Rescued);
        Assert.DoesNotContain("JAGGI", g.World.Cause);
    }

    [Fact]
    public void Doing_nothing_but_shooting_no_pilot_is_rescued()
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avafractalus-tests-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        g.Go(0);
        for (int t = 0; t < 60 * 600 && g.State == GameState.Playing; t++) g.Tick(new Controls(Fire: true));
        Assert.Equal(0, g.World.Rescued);
        Assert.NotEqual(Phase.Docked, g.Phase);
    }
}
