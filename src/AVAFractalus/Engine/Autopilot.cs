namespace AVAFractalus.Engine;

/// <summary>
/// The computer pilot, for the demonstration and the tests. It flies to the nearest downed pilot's beacon, keeping
/// well above the ground ahead, slows and lands beside him, and opens the airlock when he knocks - unless the
/// face at the canopy is a Jaggi's, when it lifts off. It shoots the guns and saucers in front of it, and turns
/// on a saucer that comes close. With every pilot aboard it climbs to the mother ship.
/// </summary>
public sealed class Autopilot
{
    private bool _landPrev, _airPrev, _firePrev;
    private double _wait;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _landPrev = _airPrev = _firePrev = false;
        _wait = 0;
        Doing = "";
    }

    public Controls Decide(GameEngine g) => g.Phase != Phase.Play ? default : Decide(g.World);

    public Controls Decide(World w)
    {
        var s = w.Ship;
        var p = w.Planet;
        if (s.Dead || s.Settling > 0) return default;
        if (s.Landed)
        {
            var at = w.Pilots.FirstOrDefault(pl => pl.State == PilotState.Knocking);
            var coming = w.Pilots.Any(pl => pl.State == PilotState.Running);
            if (at != null)
            {
                // Has the face at the canopy shown itself a Jaggi? Wait long enough to see.
                if (w.Face >= 0) { Doing = "a Jaggi! lifting off"; return Press(land: true); }
                if (at.T > Sim.FaceAfter + 0.3) { Doing = "letting him in"; return Press(airlock: true); }
                Doing = "who's knocking?";
                return default;
            }
            if (coming) { Doing = "waiting for him"; return default; }
            _wait += Field.Step;
            if (_wait < 0.6) return default;
            _wait = 0;
            Doing = "lifting off";
            return Press(land: true);
        }
        _wait = 0;
        var near = w.Saucers.OrderBy(sc => PlanetDef.Distance(sc.X, sc.Y, s.X, s.Y)).FirstOrDefault();
        bool threat = near != null && PlanetDef.Distance(near.X, near.Y, s.X, s.Y) < 700;
        // All aboard: up to the mother ship (seeing off any saucer on the way).
        if (w.AllAboard && !(threat && s.Alt < Field.DockHeight - 300))
        {
            Doing = "climbing to the mother ship";
            return new Controls(0, -1, Fire: Shoot(w, s), Faster: s.Speed < 90);
        }
        // A saucer close by: round to face it.
        var saucer = w.Saucers.OrderBy(sc => PlanetDef.Distance(sc.X, sc.Y, s.X, s.Y)).FirstOrDefault();
        double tx, ty;
        bool landing = false;
        if (saucer != null && PlanetDef.Distance(saucer.X, saucer.Y, s.X, s.Y) < 700)
        {
            Doing = "fighting a saucer";
            (tx, ty) = (saucer.X, saucer.Y);
        }
        else
        {
            var pl = w.Pilots.Where(x => x.State == PilotState.Waiting).OrderBy(x => PlanetDef.Distance(x.X, x.Y, s.X, s.Y)).FirstOrDefault();
            if (pl == null) { Doing = "circling"; return new Controls(1, s.Alt < p.Ground(s.X, s.Y) + 200 ? -1 : 0); }
            (tx, ty) = (pl.X, pl.Y);
            landing = true;
            Doing = "to a pilot";
        }
        double dist = PlanetDef.Distance(s.X, s.Y, tx, ty);
        double err = Sim.Wrap(Sim.Bearing(s.X, s.Y, tx, ty) - s.Heading);
        int dx = Math.Abs(err) < 0.04 ? 0 : Math.Sign(err);
        // Height: clear of everything on the way, with room; down near the pilot.
        // (Ahead, and to either side of it in case of a turn.)
        double ahead = 0;
        double reach = Math.Min(600, Math.Max(100, dist));
        foreach (double fan in new[] { 0, -0.35, 0.35, err * 0.5 })
        {
            var (ax, ay) = Sim.Ahead(s.Heading + fan);
            for (double d = 0; d <= reach; d += 25)
                ahead = Math.Max(ahead, p.Ground(s.X + ax * d, s.Y + ay * d));
        }
        double here = p.Ground(s.X, s.Y);
        double want = Math.Max(ahead, here) + (landing && dist < 260 ? 25 : 55);
        int dy = s.Alt < want - 6 ? -1 : s.Alt > want + 25 ? 1 : 0;
        bool faster = false, slower = false;
        double speed = landing ? Math.Clamp(dist * 0.35, 20, Ship.MaxSpeed) : 110;
        if (Math.Abs(err) > 0.8) speed = Math.Min(speed, 50);
        // A wall ahead: slow while climbing over it.
        if (want - s.Alt > 60) speed = Math.Min(speed, 45);
        if (s.Speed < speed - 4) faster = true;
        else if (s.Speed > speed + 4) slower = true;
        bool land = landing && dist < 150 && s.Speed < Sim.LandSpeed && s.Alt - here < Sim.LandHeight && p.Roughness(s.X, s.Y) < Sim.LandRough;
        if (land) { Doing = "landing"; return Press(land: true); }
        return new Controls(dx, dy, Shoot(w, s), faster, slower);
    }

    /// <summary>Fire at whatever's close to the line ahead.</summary>
    private bool Shoot(World w, Ship s)
    {
        foreach (var (x, y, z) in Sim.Targets(w))
        {
            double d = PlanetDef.Distance(s.X, s.Y, x, y);
            if (d > 1300) continue;
            if (Math.Abs(Sim.Wrap(Sim.Bearing(s.X, s.Y, x, y) - s.Heading)) < 0.1 && Sim.Clear(w.Planet, s.X, s.Y, s.Alt, x, y, z + 4))
            {
                _firePrev = !_firePrev;
                return true;
            }
        }
        return false;
    }

    private Controls Press(bool land = false, bool airlock = false)
    {
        // Each press is let go of before the next.
        if (land && _landPrev || airlock && _airPrev) { _landPrev = _airPrev = false; return default; }
        _landPrev = land;
        _airPrev = airlock;
        return new Controls(Land: land, Airlock: airlock);
    }
}
