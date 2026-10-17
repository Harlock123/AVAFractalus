using Avalonia.Input;
using AVAFractalus.Engine;

namespace AVAFractalus.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avafractalus-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>The first planet, quiet: no guns, no saucers for a long while.</summary>
    private static World Planet()
    {
        var w = new World();
        Sim.Start(w, PlanetBook.Build(0), 1);
        w.Guns.Clear();
        w.NextSaucer = 1e9;
        return w;
    }

    private static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    /// <summary>The fighter set down beside the first pilot.</summary>
    private static (World W, Pilot P) Landed(bool jaggi = false)
    {
        var w = Planet();
        var p = w.Pilots[0];
        if (jaggi) p.Def = p.Def with { Jaggi = true };
        var s = w.Ship;
        s.X = p.X + 60;
        s.Y = p.Y;
        s.Alt = w.Planet.Ground(s.X, s.Y);
        s.Speed = 0;
        s.Landed = true;
        return (w, p);
    }

    // ───────────────────────────── flying ─────────────────────────────

    [Fact]
    public void It_flies_the_way_it_heads_and_turns_and_climbs_and_dives()
    {
        var w = Planet();
        var s = w.Ship;
        double y = s.Y, alt = s.Alt;
        Run(w, 60);
        Assert.True(s.Y < y - 50, "north, the way it heads");
        Run(w, 60, new Controls(0, -1));
        Assert.True(s.Alt > alt + 40, "up climbs");
        alt = s.Alt;
        Run(w, 30, new Controls(0, 1));
        Assert.True(s.Alt < alt, "down dives");
        Run(w, 30, new Controls(1, 0));
        Assert.True(s.Heading > 0.4, "right turns clockwise");
    }

    [Fact]
    public void Faster_and_slower()
    {
        var w = Planet();
        Run(w, 180, new Controls(Faster: true));
        Assert.Equal(Ship.MaxSpeed, w.Ship.Speed, 3);
        Run(w, 300, new Controls(Slower: true));
        Assert.Equal(Ship.MinSpeed, w.Ship.Speed, 3);
    }

    [Fact]
    public void The_surface_wraps_round()
    {
        var w = Planet();
        w.Ship.Y = 10;
        Run(w, 60);
        Assert.InRange(w.Ship.Y, Field.Size - 60, Field.Size);
        Assert.InRange(PlanetDef.Distance(10, 10, Field.Size - 10, Field.Size - 10), 28, 29);
    }

    [Fact]
    public void Flying_into_a_mountain_costs_energy_and_throws_it_up()
    {
        var w = Planet();
        var s = w.Ship;
        s.Alt = w.Planet.Ground(s.X, s.Y) + 3;
        Run(w, 5, new Controls(0, 1));
        Assert.Equal(100 - 20, s.Energy, 0);
        Assert.True(s.Alt > w.Planet.Ground(s.X, s.Y) + 10);
        Assert.Equal("FLEW INTO A MOUNTAIN", w.Cause);
    }

    [Fact]
    public void Shields_build_back_up_slowly()
    {
        var w = Planet();
        w.Ship.Energy = 50;
        Run(w, 600);
        Assert.Equal(50 + Sim.Recharge * 10, w.Ship.Energy, 1);
    }

    // ───────────────────────────── landing and the airlock ─────────────────────────────

    [Fact]
    public void It_lands_only_low_slow_and_on_level_ground_and_lifts_off_again()
    {
        var w = Planet();
        var s = w.Ship;
        var p = w.Pilots[0];
        s.X = p.X;
        s.Y = p.Y;
        Sim.Step(w, new Controls(Land: true));
        Assert.Equal(0, s.Settling);
        Assert.Contains(Sfx.Deny, w.Heard);
        s.Alt = w.Planet.Ground(s.X, s.Y) + 40;
        s.Speed = 20;
        Sim.Step(w, default);
        Sim.Step(w, new Controls(Land: true));
        Assert.True(s.Settling > 0);
        Run(w, 180);
        Assert.True(s.Landed);
        Assert.Equal(w.Planet.Ground(s.X, s.Y), s.Alt, 1);
        Sim.Step(w, new Controls(Land: true));
        Assert.False(s.Landed);
        Run(w, 60, new Controls(0, -1));
        Assert.True(s.Alt > w.Planet.Ground(s.X, s.Y) + 20);
        Assert.False(s.Dead);
    }

    [Fact]
    public void A_pilot_runs_to_the_fighter_knocks_and_is_let_in()
    {
        var (w, p) = Landed();
        Run(w, 2);
        Assert.Equal(PilotState.Running, p.State);
        Run(w, 60 * 12);
        Assert.Equal(PilotState.Knocking, p.State);
        Assert.Equal(-1, w.Face);
        Sim.Step(w, new Controls(Airlock: true));
        Assert.Equal(PilotState.Aboard, p.State);
        Assert.Equal(1, w.Rescued);
        Assert.Equal(Sim.RescuePoints, w.Score);
    }

    [Fact]
    public void A_Jaggi_shows_its_face_and_breaks_in_if_left_and_hurts_if_let_in()
    {
        var (w, p) = Landed(jaggi: true);
        Run(w, 60 * 12);
        Assert.Equal(PilotState.Knocking, p.State);
        Run(w, (int)(60 * Sim.FaceAfter) + 2);
        Assert.True(w.Face >= 0);
        double e = w.Ship.Energy;
        Run(w, 60 * 4);
        Assert.True(w.Ship.Energy < e - 5, "it breaks in");
        Sim.Step(w, new Controls(Airlock: true));
        Assert.Equal(PilotState.Gone, p.State);
        Assert.Equal(0, w.Rescued);
        Assert.Equal("A JAGGI ABOARD", w.Cause);
    }

    [Fact]
    public void Lifting_off_shakes_off_a_Jaggi_and_leaves_a_pilot_by_his_wreck()
    {
        var (w, p) = Landed(jaggi: true);
        Run(w, 60 * 12 + (int)(60 * Sim.FaceAfter) + 5);
        Sim.Step(w, new Controls(Land: true));
        Assert.Equal(PilotState.Gone, p.State);
        Assert.Equal(-1, w.Face);

        var (w2, p2) = Landed();
        Run(w2, 60 * 2);
        Assert.Equal(PilotState.Running, p2.State);
        Sim.Step(w2, new Controls(Land: true));
        Assert.Equal(PilotState.Waiting, p2.State);
        Assert.Equal((p2.Def.X, p2.Def.Y), (p2.X, p2.Y));
    }

    [Fact]
    public void Every_pilot_aboard_it_docks_up_at_the_mother_ship()
    {
        var w = Planet();
        foreach (var p in w.Pilots) if (!p.Def.Jaggi) p.State = PilotState.Aboard;
        w.Rescued = w.Planet.Humans;
        w.Ship.Alt = Field.DockHeight - 50;
        Run(w, 120, new Controls(0, -1));
        Assert.True(w.Docked);
    }

    [Fact]
    public void It_does_not_dock_with_pilots_still_out()
    {
        var w = Planet();
        w.Ship.Alt = Field.DockHeight + 10;
        Run(w, 30, new Controls(0, -1));
        Assert.False(w.Docked);
    }

    // ───────────────────────────── the Jaggi's guns and saucers ─────────────────────────────

    [Fact]
    public void A_gun_with_a_clear_shot_fires_and_a_bolt_takes_it_out()
    {
        var w = new World();
        Sim.Start(w, PlanetBook.Build(0), 1);
        w.NextSaucer = 1e9;
        var g = w.Guns[0];
        var s = w.Ship;
        // Hovering high over it, a little way off, facing it.
        s.X = g.Def.X;
        s.Y = g.Def.Y + 500;
        s.Alt = g.Z + 250;
        s.Speed = 0;
        g.Cool = 0;
        Run(w, 2);
        Assert.Contains(w.Bolts, b => !b.Mine);
        s.Heading = 0;
        w.Bolts.Clear();
        Run(w, 60, new Controls(Fire: true));
        Assert.True(g.Down);
        Assert.True(w.Score >= Sim.GunPoints);
    }

    [Fact]
    public void A_gun_cannot_reach_far_overhead()
    {
        var w = new World();
        Sim.Start(w, PlanetBook.Build(0), 1);
        var g = w.Guns[0];
        w.Ship.X = g.Def.X + 100;
        w.Ship.Y = g.Def.Y;
        w.Ship.Alt = g.Z + Sim.GunReachUp + 50;
        w.Ship.Speed = 0;
        g.Cool = 0;
        Run(w, 2);
        Assert.DoesNotContain(w.Bolts, b => !b.Mine);
    }

    [Fact]
    public void Saucers_come_after_the_fighter_and_are_shot_down_with_a_leading_sight()
    {
        var w = Planet();
        w.NextSaucer = 0;
        Run(w, 2);
        var sc = Assert.Single(w.Saucers);
        Assert.InRange(PlanetDef.Distance(sc.X, sc.Y, w.Ship.X, w.Ship.Y), 1500, 1700);
        // Put it crossing in front of the fighter.
        sc.X = w.Ship.X + 50;
        sc.Y = w.Ship.Y - 500;
        sc.Z = w.Ship.Alt;
        sc.Heading = Math.PI / 2;
        Run(w, 50, new Controls(Fire: true));
        Assert.Empty(w.Saucers);
        Assert.Equal(Sim.SaucerPoints, w.Score);
    }

    // ───────────────────────────── the game ─────────────────────────────

    private static GameEngine Engine() => new(HighScoreTable.Load(TempFolder())) { Silent = true };

    [Fact]
    public void Docking_scores_the_energy_left_and_goes_on_to_the_next_planet()
    {
        var g = Engine();
        g.Go(0);
        g.World.Docked = true;
        g.World.Ship.Energy = 50;
        g.Run(1);
        Assert.Equal(Phase.Docked, g.Phase);
        Assert.Equal(GameEngine.DockBonus + 50 * GameEngine.EnergyPoints, g.Score);
        g.Run((int)(60 * GameEngine.DockedSeconds) + 2);
        Assert.Equal(1, g.Level);
        Assert.Equal(GameEngine.Lives0 + 1, g.Lives);
        Assert.Equal(100, g.World.Ship.Energy);
    }

    [Fact]
    public void A_fighter_lost_and_the_next_comes_out_of_the_mother_ship()
    {
        var g = Engine();
        g.Go(0);
        g.World.Ship.Energy = 0;
        g.Run(2);
        Assert.Equal(Phase.Dying, g.Phase);
        g.Run((int)(60 * GameEngine.DyingSeconds) + 2);
        Assert.Equal(GameEngine.Lives0 - 1, g.Lives);
        Assert.False(g.World.Ship.Dead);
        Assert.Equal(100, g.World.Ship.Energy, 0);
    }

    [Fact]
    public void The_last_fighter_lost_is_game_over_then_the_high_scores()
    {
        var g = Engine();
        g.Go(0);
        g.SetLives(1);
        g.World.Score = 25000;
        g.World.Ship.Energy = 0;
        g.Run((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.Equal(GameState.GameOver, g.State);
        g.Run(60 * 5);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "ACE") g.Initials!.Type(ch);
        g.Run(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal("ACE", g.HighScores.Entries[g.NewEntryIndex].Initials);
    }

    [Fact]
    public void The_title_gives_way_to_the_demonstration_and_enter_starts_a_game()
    {
        var g = Engine();
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        double x = g.World.Ship.X, y = g.World.Ship.Y;
        g.Idle(60 * 6);
        Assert.NotEqual((x, y), (g.World.Ship.X, g.World.Ship.Y));
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(GameEngine.Lives0, g.Lives);
    }

    [Fact]
    public void Paused_nothing_moves()
    {
        var g = Engine();
        g.Go(0);
        g.Run(30);
        g.Pause();
        double x = g.World.Ship.X, y = g.World.Ship.Y;
        g.Run(120);
        Assert.Equal((x, y), (g.World.Ship.X, g.World.Ship.Y));
        g.Resume();
        g.Run(10);
        Assert.NotEqual((x, y), (g.World.Ship.X, g.World.Ship.Y));
    }

    [Fact]
    public void The_keys_fly_and_land_and_open_the_airlock()
    {
        using var input = new InputHandler();
        input.OnKeyDown(Key.Up);
        Assert.Equal(-1, input.Player.Dy);
        input.OnKeyUp(Key.Up);
        input.OnKeyDown(Key.Down);
        Assert.Equal(1, input.Player.Dy);
        input.OnKeyUp(Key.Down);
        input.OnKeyDown(Key.Left);
        Assert.Equal(-1, input.Player.Dx);
        input.OnKeyUp(Key.Left);
        foreach (var (key, check) in new (Key, Func<Controls, bool>)[] { (Key.Space, c => c.Fire), (Key.W, c => c.Faster), (Key.S, c => c.Slower), (Key.L, c => c.Land), (Key.A, c => c.Airlock) })
        {
            input.OnKeyDown(key);
            Assert.True(check(input.Player), key.ToString());
            input.OnKeyUp(key);
        }

        // Up through to the fighter: it climbs.
        var w = Planet();
        double alt = w.Ship.Alt;
        input.OnKeyDown(Key.Up);
        Run(w, 60, input.Player);
        Assert.True(w.Ship.Alt > alt + 30);
    }

    [Fact]
    public void Every_pilot_aboard_is_announced()
    {
        var g = Engine();
        var heard = new List<Sfx>();
        g.Silent = false;
        g.Sound += heard.Add;
        g.Go(0);
        g.World.Rescued = g.World.Planet.Humans;
        g.Run(2);
        Assert.Contains(Sfx.SayAllAboard, heard);
        Assert.Equal("ALL PILOTS ABOARD - CLIMB TO THE MOTHER SHIP", g.Message);
    }
}
