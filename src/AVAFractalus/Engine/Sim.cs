namespace AVAFractalus.Engine;

/// <summary>
/// A planet, step by step: the fighter flying over the mountains, landing by a downed pilot and lifting off
/// again; the pilots running to the airlock and knocking (and the Jaggi among them); the guns on the peaks and
/// the saucers; and, with every pilot aboard, the climb to the mother ship.
/// </summary>
public static class Sim
{
    private const double Dt = Field.Step;
    public const double BoltSpeed = 700, GunBolt = 260, GunRange = 1400, GunReachUp = 600, SaucerSpeed = 110, PilotSpeed = 18;
    /// <summary>A pilot runs to a fighter that lands this close.</summary>
    public const double CallRange = 320;
    public const double LandHeight = 70, LandSpeed = 45, LandRough = 14;
    public const double FaceAfter = 1.4, BreakAfter = 3.5, Recharge = 0.3;
    public const int RescuePoints = 1000, GunPoints = 250, SaucerPoints = 400;

    public static void Start(World w, PlanetDef p, int seed)
    {
        w.Planet = p;
        w.Dice = new Dice(seed);
        w.Clock = 0;
        w.NextSaucer = p.SaucerEvery;
        w.Pilots = p.Pilots.Select(d => new Pilot { Def = d, X = d.X, Y = d.Y }).ToList();
        w.Guns = p.Guns.Select(g => new Gun { Def = g, Z = p.Ground(g.X, g.Y), Cool = 2 + w.Dice.Next() * 3 }).ToList();
        w.Saucers.Clear();
        w.Bolts.Clear();
        w.Booms.Clear();
        w.Rescued = 0;
        w.Face = -1;
        w.Docked = false;
        w.Cause = "";
        Launch(w);
    }

    /// <summary>A fresh fighter, out of the mother ship's bay over the start.</summary>
    public static void Launch(World w)
    {
        var p = w.Planet;
        w.Ship = new Ship { X = p.StartX, Y = p.StartY, Alt = p.Ground(p.StartX, p.StartY) + 320, Heading = 0, Speed = 60 };
        w.Saucers.Clear();
        w.Bolts.Clear();
        w.Face = -1;
        foreach (var pl in w.Pilots)
            if (pl.State is PilotState.Running or PilotState.Knocking) Abandon(pl);
    }

    public static (double X, double Y) Ahead(double heading) => (Math.Sin(heading), -Math.Cos(heading));

    public static double Wrap(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    public static double Bearing(double fx, double fy, double tx, double ty)
    {
        var (dx, dy) = PlanetDef.Toward(fx, fy, tx, ty);
        return Math.Atan2(dx, -dy);
    }

    private static double Mod(double v) => (v % Field.Size + Field.Size) % Field.Size;

    public static void Step(World w, Controls c)
    {
        w.Clock += Dt;
        foreach (var b in w.Booms) b.T += Dt;
        w.Booms.RemoveAll(b => b.T > 1.2);
        var s = w.Ship;
        // The shields build back up, slowly.
        if (!s.Dead && s.Energy > 0 && s.Energy < 100) s.Energy = Math.Min(100, s.Energy + Recharge * Dt);
        if (!s.Dead && !w.Docked) Fly(w, s, c);
        s.LandPrev = c.Land;
        s.AirlockPrev = c.Airlock;
        Pilots(w);
        Guns(w);
        Saucers(w);
        Bolts(w);
        if (!s.Dead && s.Energy <= 0)
        {
            s.Energy = 0;
            s.Dead = true;
            if (w.Cause == "") w.Cause = "SHOT DOWN";
            w.Booms.Add(new Boom(s.X, s.Y, s.Alt, Blast.Big));
            w.Heard.Add(Sfx.Explode);
        }
    }

    private static void Fly(World w, Ship s, Controls c)
    {
        var p = w.Planet;
        bool land = c.Land && !s.LandPrev, airlock = c.Airlock && !s.AirlockPrev;
        s.FireTimer -= Dt;
        double ground = p.Ground(s.X, s.Y);
        if (s.Landed)
        {
            if (land || c.Dy < 0)
            {
                // Lift off: any pilot on the way is left behind, any Jaggi at the canopy falls away.
                // Straight up, clear of the ground, before it moves off.
                s.Landed = false;
                s.Vz = 50;
                s.Speed = 0;
                s.Alt = ground + 8;
                w.Face = -1;
                foreach (var pl in w.Pilots)
                    if (pl.State is PilotState.Running or PilotState.Knocking) Abandon(pl);
                w.Heard.Add(Sfx.LiftOff);
                return;
            }
            if (airlock)
            {
                var at = w.Pilots.FirstOrDefault(pl => pl.State == PilotState.Knocking);
                if (at == null) { w.Heard.Add(Sfx.Airlock); return; }
                if (!at.Def.Jaggi)
                {
                    at.State = PilotState.Aboard;
                    w.Rescued++;
                    w.Score += RescuePoints;
                    w.Heard.Add(Sfx.Aboard);
                }
                else
                {
                    // Let in a Jaggi.
                    at.State = PilotState.Gone;
                    w.Face = -1;
                    s.Energy -= 35;
                    w.Cause = "A JAGGI ABOARD";
                    w.Booms.Add(new Boom(s.X, s.Y, s.Alt, Blast.Hit));
                    w.Heard.Add(Sfx.JaggiIn);
                }
            }
            return;
        }
        if (s.Settling > 0)
        {
            // Coming down onto the ground.
            s.Speed = Math.Max(0, s.Speed - 60 * Dt);
            s.Alt = Math.Max(ground, s.Alt - 30 * Dt);
            Move(s);
            if (s.Alt - p.Ground(s.X, s.Y) < 0.5)
            {
                s.Alt = p.Ground(s.X, s.Y);
                s.Settling = 0;
                s.Speed = 0;
                s.Landed = true;
                w.Heard.Add(Sfx.Landed);
            }
            return;
        }
        if (land)
        {
            if (s.Alt - ground < LandHeight && s.Speed < LandSpeed && p.Roughness(s.X, s.Y) < LandRough) { s.Settling = 1; w.Heard.Add(Sfx.Landing); }
            else w.Heard.Add(Sfx.Deny);
        }
        s.Heading = Wrap(s.Heading + c.Dx * Ship.Turn * Dt);
        s.Bank += (c.Dx * 0.35 - s.Bank) * Math.Min(1, Dt * 6);
        if (c.Faster) s.Speed = Math.Min(Ship.MaxSpeed, s.Speed + Ship.Accel * Dt);
        if (c.Slower) s.Speed = Math.Max(Ship.MinSpeed, s.Speed - Ship.Accel * Dt);
        double want = c.Dy < 0 ? Ship.Climb : c.Dy > 0 ? -Ship.Climb : 0;
        s.Vz += (want - s.Vz) * Math.Min(1, Dt * 4);
        s.Alt = Math.Min(Field.DockHeight + 300, s.Alt + s.Vz * Dt);
        Move(s);
        ground = p.Ground(s.X, s.Y);
        if (s.Alt < ground + 2)
        {
            // Into the mountainside.
            s.Energy -= 20;
            s.Alt = ground + 30;
            s.Speed *= 0.4;
            s.Vz = 20;
            w.Cause = "FLEW INTO A MOUNTAIN";
            w.Booms.Add(new Boom(s.X, s.Y, ground, Blast.Hit));
            w.Heard.Add(Sfx.Crash);
        }
        if (c.Fire && s.FireTimer <= 0)
        {
            s.FireTimer = 0.25;
            Shoot(w, s);
        }
        if (w.AllAboard && s.Alt >= Field.DockHeight)
        {
            w.Docked = true;
            w.Heard.Add(Sfx.Dock);
        }
    }

    private static void Move(Ship s)
    {
        var (ax, ay) = Ahead(s.Heading);
        s.X = Mod(s.X + ax * s.Speed * Dt);
        s.Y = Mod(s.Y + ay * s.Speed * Dt);
    }

    /// <summary>
    /// A bolt straight ahead; or, with a target close to the line of fire, at where that target will be when the bolt
    /// gets there (the fighter's sight leads it).
    /// </summary>
    private static void Shoot(World w, Ship s)
    {
        var (ax, ay) = Ahead(s.Heading);
        var bolt = new Bolt { X = s.X, Y = s.Y, Z = s.Alt - 2, Vx = ax * BoltSpeed, Vy = ay * BoltSpeed, Life = 2.4, Mine = true };
        double best = 0.13;
        foreach (var (x, y, z, vx, vy) in Moving(w))
        {
            double d = PlanetDef.Distance(s.X, s.Y, x, y);
            if (d > 1600 || d < 20) continue;
            double off = Math.Abs(Wrap(Bearing(s.X, s.Y, x, y) - s.Heading));
            if (off >= best) continue;
            best = off;
            var (dx, dy) = PlanetDef.Toward(s.X, s.Y, x, y);
            double t = Math.Sqrt(dx * dx + dy * dy + (z - s.Alt) * (z - s.Alt)) / BoltSpeed;
            for (int i = 0; i < 3; i++)
            {
                double px = dx + vx * t, py = dy + vy * t;
                t = Math.Sqrt(px * px + py * py + (z - s.Alt) * (z - s.Alt)) / BoltSpeed;
            }
            double tx = dx + vx * t, ty = dy + vy * t, tz = z - bolt.Z;
            double len = Math.Sqrt(tx * tx + ty * ty + tz * tz);
            (bolt.Vx, bolt.Vy, bolt.Vz) = (tx / len * BoltSpeed, ty / len * BoltSpeed, tz / len * BoltSpeed);
        }
        w.Bolts.Add(bolt);
        w.Heard.Add(Sfx.Fire);
    }

    /// <summary>The targets, and how fast each is going which way.</summary>
    private static IEnumerable<(double X, double Y, double Z, double Vx, double Vy)> Moving(World w)
    {
        foreach (var g in w.Guns) if (!g.Down) yield return (g.Def.X, g.Def.Y, g.Z + 6, 0, 0);
        foreach (var sc in w.Saucers)
        {
            var (ax, ay) = Ahead(sc.Heading);
            yield return (sc.X, sc.Y, sc.Z, ax * SaucerSpeed, ay * SaucerSpeed);
        }
    }

    public static IEnumerable<(double X, double Y, double Z)> Targets(World w)
    {
        foreach (var g in w.Guns) if (!g.Down) yield return (g.Def.X, g.Def.Y, g.Z + 6);
        foreach (var sc in w.Saucers) yield return (sc.X, sc.Y, sc.Z);
    }

    private static void Abandon(Pilot pl)
    {
        if (pl.Def.Jaggi && pl.State == PilotState.Knocking) { pl.State = PilotState.Gone; return; }
        pl.State = PilotState.Waiting;
        pl.X = pl.Def.X;
        pl.Y = pl.Def.Y;
        pl.T = 0;
    }

    private static void Pilots(World w)
    {
        var s = w.Ship;
        bool down = s.Landed && !s.Dead;
        foreach (var pl in w.Pilots)
        {
            switch (pl.State)
            {
                case PilotState.Waiting:
                    if (down && PlanetDef.Distance(pl.X, pl.Y, s.X, s.Y) < CallRange && !w.Pilots.Any(o => o.State is PilotState.Running or PilotState.Knocking))
                    {
                        pl.State = PilotState.Running;
                        pl.T = 0;
                        w.Heard.Add(Sfx.Running);
                    }
                    break;
                case PilotState.Running:
                {
                    // Round to the front of the fighter, where he can be seen, then to the airlock.
                    var (ax, ay) = Ahead(s.Heading);
                    var (fx, fy) = PlanetDef.Toward(pl.X, pl.Y, s.X + ax * 30, s.Y + ay * 30);
                    if (pl.T == 0 && fx * fx + fy * fy < 64) pl.T = 1;
                    var (dx, dy) = pl.T == 0 ? (fx, fy) : PlanetDef.Toward(pl.X, pl.Y, s.X, s.Y);
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < 10 && pl.T > 0)
                    {
                        pl.State = PilotState.Knocking;
                        pl.T = 0;
                        w.Heard.Add(Sfx.Knock);
                        break;
                    }
                    pl.X = Mod(pl.X + dx / d * PilotSpeed * Field.Step);
                    pl.Y = Mod(pl.Y + dy / d * PilotSpeed * Field.Step);
                    break;
                }
                case PilotState.Knocking:
                    pl.T += Field.Step;
                    if (!pl.Def.Jaggi)
                    {
                        if ((int)(pl.T / 1.2) != (int)((pl.T - Field.Step) / 1.2)) w.Heard.Add(Sfx.Knock);
                        break;
                    }
                    // A Jaggi: its face at the canopy, then it starts breaking in.
                    if (pl.T >= FaceAfter && w.Face < 0) { w.Face = 0; w.Heard.Add(Sfx.Jaggi); }
                    if (w.Face >= 0) w.Face += Field.Step;
                    if (pl.T > BreakAfter)
                    {
                        s.Energy -= 5 * Field.Step;
                        w.Cause = "THE JAGGI BROKE IN";
                        if ((int)(pl.T / 0.5) != (int)((pl.T - Field.Step) / 0.5)) w.Heard.Add(Sfx.Bang);
                    }
                    break;
            }
        }
    }

    /// <summary>Whether anything solid lies between two points.</summary>
    public static bool Clear(PlanetDef p, double ax, double ay, double az, double bx, double by, double bz)
    {
        var (dx, dy) = PlanetDef.Toward(ax, ay, bx, by);
        for (int i = 1; i < 16; i++)
        {
            double t = i / 16.0;
            if (p.Ground(Mod(ax + dx * t), Mod(ay + dy * t)) > az + (bz - az) * t + 2) return false;
        }
        return true;
    }

    private static void Guns(World w)
    {
        var s = w.Ship;
        var p = w.Planet;
        foreach (var g in w.Guns)
        {
            if (g.Down) continue;
            g.Cool -= Dt;
            if (s.Dead || g.Cool > 0) continue;
            double d = PlanetDef.Distance(g.Def.X, g.Def.Y, s.X, s.Y);
            // (They can't lift their barrels to anything far overhead.)
            if (d > GunRange || s.Alt - g.Z > GunReachUp || !Clear(p, g.Def.X, g.Def.Y, g.Z + 10, s.X, s.Y, s.Alt)) continue;
            g.Cool = p.GunEvery * (0.8 + w.Dice.Next() * 0.5);
            Fire(w, g.Def.X, g.Def.Y, g.Z + 10, GunBolt);
            w.Heard.Add(Sfx.GunFire);
        }
    }

    /// <summary>An enemy bolt at where the fighter will be.</summary>
    private static void Fire(World w, double x, double y, double z, double speed)
    {
        var s = w.Ship;
        var (ax, ay) = Ahead(s.Heading);
        var (dx, dy) = PlanetDef.Toward(x, y, s.X, s.Y);
        double t = Math.Sqrt(dx * dx + dy * dy + (s.Alt - z) * (s.Alt - z)) / speed;
        // Not quite dead on.
        double tx = dx + ax * s.Speed * t + (w.Dice.Next() - 0.5) * 30, ty = dy + ay * s.Speed * t + (w.Dice.Next() - 0.5) * 30, tz = s.Alt + s.Vz * t - z;
        w.Bolts.Add(new Bolt { X = x, Y = y, Z = z, Vx = tx / t, Vy = ty / t, Vz = tz / t, Life = t + 0.6 });
    }

    private static void Saucers(World w)
    {
        var s = w.Ship;
        var p = w.Planet;
        if (!s.Dead && w.Clock >= w.NextSaucer)
        {
            w.NextSaucer = w.Clock + p.SaucerEvery;
            if (w.Saucers.Count < 1 + p.Level / 2)
            {
                double a = w.Dice.Next() * Math.PI * 2;
                double x = Mod(s.X + Math.Sin(a) * 1600), y = Mod(s.Y - Math.Cos(a) * 1600);
                w.Saucers.Add(new Saucer { X = x, Y = y, Z = Math.Max(s.Alt + 60, p.Ground(x, y) + 80), Heading = a + Math.PI, Cool = 3 });
                w.Heard.Add(Sfx.Saucer);
            }
        }
        foreach (var sc in w.Saucers)
        {
            sc.Cool -= Dt;
            double want = Bearing(sc.X, sc.Y, s.X, s.Y);
            double d = PlanetDef.Distance(sc.X, sc.Y, s.X, s.Y);
            // It closes in, then circles.
            if (d < 350) want += Math.PI / 2;
            sc.Heading = Wrap(sc.Heading + Math.Clamp(Wrap(want - sc.Heading), -1.2 * Dt, 1.2 * Dt));
            var (ax, ay) = Ahead(sc.Heading);
            sc.X = Mod(sc.X + ax * SaucerSpeed * Dt);
            sc.Y = Mod(sc.Y + ay * SaucerSpeed * Dt);
            double floor = p.Ground(sc.X, sc.Y) + 40;
            sc.Z += (Math.Max(floor, s.Alt + 25) - sc.Z) * Math.Min(1, Dt * 1.5);
            if (sc.Z < floor) sc.Z = floor;
            if (!s.Dead && sc.Cool <= 0 && d < 850)
            {
                sc.Cool = 2.4;
                Fire(w, sc.X, sc.Y, sc.Z, GunBolt * 1.2);
                w.Heard.Add(Sfx.SaucerFire);
            }
        }
    }

    private static void Bolts(World w)
    {
        var s = w.Ship;
        var p = w.Planet;
        foreach (var b in w.Bolts)
        {
            b.X = Mod(b.X + b.Vx * Dt);
            b.Y = Mod(b.Y + b.Vy * Dt);
            b.Z += b.Vz * Dt;
            b.Life -= Dt;
            if (b.Life <= 0) continue;
            if (p.Ground(b.X, b.Y) > b.Z)
            {
                b.Life = 0;
                if (b.Mine) w.Booms.Add(new Boom(b.X, b.Y, b.Z, Blast.Small));
                if (!b.Mine) continue;
            }
            if (b.Mine)
            {
                foreach (var g in w.Guns)
                    if (!g.Down && Near(b, g.Def.X, g.Def.Y, g.Z + 6, 34))
                    {
                        g.Down = true;
                        b.Life = 0;
                        w.Score += GunPoints;
                        w.Booms.Add(new Boom(g.Def.X, g.Def.Y, g.Z + 6, Blast.Big));
                        w.Heard.Add(Sfx.GunDown);
                    }
                foreach (var sc in w.Saucers)
                    if (Near(b, sc.X, sc.Y, sc.Z, 30))
                    {
                        sc.Cool = -99;
                        b.Life = 0;
                        w.Score += SaucerPoints;
                        w.Booms.Add(new Boom(sc.X, sc.Y, sc.Z, Blast.Big));
                        w.Heard.Add(Sfx.SaucerDown);
                    }
                w.Saucers.RemoveAll(sc => sc.Cool == -99);
            }
            else if (!s.Dead && Near(b, s.X, s.Y, s.Alt, 16))
            {
                b.Life = 0;
                s.Energy -= 6;
                w.Cause = "SHOT DOWN";
                w.Booms.Add(new Boom(s.X, s.Y, s.Alt, Blast.Hit));
                w.Heard.Add(Sfx.Hurt);
            }
        }
        w.Bolts.RemoveAll(b => b.Life <= 0);
    }

    private static bool Near(Bolt b, double x, double y, double z, double r)
    {
        var (dx, dy) = PlanetDef.Toward(b.X, b.Y, x, y);
        return dx * dx + dy * dy + (b.Z - z) * (b.Z - z) < r * r;
    }
}
