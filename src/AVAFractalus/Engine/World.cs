namespace AVAFractalus.Engine;

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
}

/// <summary>The fighter: where it is, how high, which way it heads (0 is north, turning clockwise), how fast.</summary>
public sealed class Ship
{
    public const double MaxSpeed = 150, MinSpeed = 0, Accel = 60, Turn = 1.1, Climb = 70;
    public double X, Y, Alt, Heading, Speed = 60, Vz;
    /// <summary>Shields and power, out of 100: at nothing the fighter is lost.</summary>
    public double Energy = 100;
    public bool Landed, Dead, LandPrev, AirlockPrev;
    /// <summary>Settling onto the ground (counts down to landed), or lifting off.</summary>
    public double Settling;
    public double FireTimer, Bank;
    public Ship Copy() => (Ship)MemberwiseClone();
}

public enum PilotState
{
    /// <summary>By his wreck, waiting to be picked up.</summary>
    Waiting,
    /// <summary>Running to the fighter that has landed near him.</summary>
    Running,
    /// <summary>At the airlock, knocking.</summary>
    Knocking,
    Aboard,
    /// <summary>A Jaggi shut out (it gives up and slinks off), or one let in.</summary>
    Gone,
}

public sealed class Pilot
{
    public PilotDef Def;
    public PilotState State;
    public double X, Y, T;
    public Pilot Copy() => (Pilot)MemberwiseClone();
}

public sealed class Gun
{
    public GunDef Def;
    public double Z, Cool;
    public bool Down;
    public Gun Copy() => (Gun)MemberwiseClone();
}

public sealed class Saucer
{
    public double X, Y, Z, Heading, Cool;
    public Saucer Copy() => (Saucer)MemberwiseClone();
}

/// <summary>A bolt of energy: the fighter's (Mine), a gun's, or a saucer's.</summary>
public sealed class Bolt
{
    public double X, Y, Z, Vx, Vy, Vz, Life;
    public bool Mine;
    public Bolt Copy() => (Bolt)MemberwiseClone();
}

public enum Blast { Small, Big, Hit }

public sealed record Boom(double X, double Y, double Z, Blast Kind)
{
    public double T;
}

/// <summary>Everything on a planet that moves or changes, kept together so it can be copied whole.</summary>
public sealed class World
{
    public PlanetDef Planet = null!;
    public Ship Ship = new();
    public List<Pilot> Pilots = [];
    public List<Gun> Guns = [];
    public List<Saucer> Saucers = [];
    public List<Bolt> Bolts = [];
    public List<Boom> Booms = [];
    public List<Sfx> Heard = [];
    public Dice Dice = new(1);
    public double Clock, NextSaucer;
    public int Score, Rescued;
    public string Cause = "";
    /// <summary>A Jaggi's face pressed to the canopy (how long it's been there).</summary>
    public double Face = -1;
    /// <summary>Docked with the mother ship: the planet done.</summary>
    public bool Docked;

    public bool AllAboard => Rescued >= Planet.Humans;

    public World Copy() => new()
    {
        Planet = Planet, Ship = Ship.Copy(), Pilots = Pilots.Select(p => p.Copy()).ToList(), Guns = Guns.Select(g => g.Copy()).ToList(),
        Saucers = Saucers.Select(s => s.Copy()).ToList(), Bolts = Bolts.Select(b => b.Copy()).ToList(), Dice = Dice, Clock = Clock,
        NextSaucer = NextSaucer, Score = Score, Rescued = Rescued, Cause = Cause, Face = Face, Docked = Docked,
    };
}
