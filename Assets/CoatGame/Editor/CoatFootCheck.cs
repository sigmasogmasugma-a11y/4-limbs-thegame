using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Coat;
using Coat.Classic;

/// Measures the two things that look wrong about the walk: whether a foot lies flat on
/// the floor, and whether the legs stay on their own sides of the body.
/// Menu: Coat > Check Feet.
public static class CoatFootCheck
{
    const float Dt = 1f / 50f;

    static CoatGame _game;
    static ClassicRagdoll _body;

    [MenuItem("Coat/Check Feet")]
    public static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (!EditorApplication.isPlaying) return "Needs play mode.";

        _game = Object.FindAnyObjectByType<CoatGame>(FindObjectsInactive.Include);
        if (_game == null || _game.Vehicle == null) return "No CoatGame with a vehicle.";
        _body = _game.Vehicle.Body;

        var log = new StringBuilder();
        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;

        try
        {
            Board();
            if (!_game.Vehicle.Worn) return "Could not get the crew aboard.";

            for (int i = 0; i < 150; i++) Tick(Key.None);

            log.AppendLine("STANDING STILL");
            log.AppendLine("  " + FootLine("left ", _body.LegL));
            log.AppendLine("  " + FootLine("right", _body.LegR));
            log.AppendLine("  " + SideLine());

            // Walk forward and watch the worst pitch and the closest the feet come to
            // swapping sides.
            float worstPitchL = 0f, worstPitchR = 0f, worstHeel = 0f;
            float worstCross = 999f;
            int crossed = 0, samples = 0;

            for (int i = 0; i < 400; i++)
            {
                int ph = i % 20;
                Tick(ph < 3 ? Key.W : ph >= 10 && ph < 13 ? Key.I : Key.None);

                if (_body.LegL.Planted) worstPitchL = Mathf.Max(worstPitchL, Pitch(_body.LegL));
                if (_body.LegR.Planted) worstPitchR = Mathf.Max(worstPitchR, Pitch(_body.LegR));
                if (_body.LegL.Planted) worstHeel = Mathf.Max(worstHeel, HeelGap(_body.LegL));
                if (_body.LegR.Planted) worstHeel = Mathf.Max(worstHeel, HeelGap(_body.LegR));

                float sep = Separation();
                worstCross = Mathf.Min(worstCross, sep);
                if (sep < 0f) crossed++;
                samples++;
            }

            log.AppendLine("WALKING FORWARD 8s");
            log.AppendLine($"  worst planted pitch  left {worstPitchL:0}deg  right {worstPitchR:0}deg   (0 = flat on the floor)");
            log.AppendLine($"  worst heel gap       {worstHeel * 100f:0.0} cm off the ground");
            log.AppendLine($"  closest to crossing  {worstCross * 100f:0.0} cm   crossed on {crossed} of {samples} ticks");

            // Now push sideways, which is where the crossing was reported.
            worstCross = 999f; crossed = 0; samples = 0;
            for (int i = 0; i < 400; i++)
            {
                int ph = i % 20;
                Tick(ph < 3 ? Key.D : ph >= 10 && ph < 13 ? Key.L : Key.None);

                float sep = Separation();
                worstCross = Mathf.Min(worstCross, sep);
                if (sep < 0f) crossed++;
                samples++;
            }

            log.AppendLine("STRAFING RIGHT 8s");
            log.AppendLine($"  body heading         {_body.Heading}");
            log.AppendLine($"  closest to crossing  {worstCross * 100f:0.0} cm   crossed on {crossed} of {samples} ticks");
            log.AppendLine("  " + SideLine());
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(Key.None);
        }

        return log.ToString();
    }

    // =====================================================================
    //  The three things that look wrong in play: gliding on one leg, feet
    //  scrabbling when you reverse, and feet twisting as the body turns.
    // =====================================================================

    [MenuItem("Coat/Check Movement Bugs")]
    public static void CheckMovesFromMenu() => Debug.Log(CheckMoves());

    public static string CheckMoves()
    {
        if (!EditorApplication.isPlaying) return "Needs play mode.";

        _game = Object.FindAnyObjectByType<CoatGame>(FindObjectsInactive.Include);
        if (_game == null || _game.Vehicle == null) return "No CoatGame with a vehicle.";
        _body = _game.Vehicle.Body;

        var log = new StringBuilder();
        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;

        try
        {
            // ---- A: gliding while hopping -------------------------------
            BoardOnly(1);
            for (int i = 0; i < 150; i++) Tick(Key.None);

            for (int i = 0; i < 250; i++) Tick((i % 20) < 3 ? Key.I : Key.None);

            Vector3 letGo = _body.Pelvis.position;
            Vector3 vAtLetGo = _body.Pelvis.linearVelocity;
            float drift = 0f;
            for (int i = 0; i < 150; i++)
            {
                Tick(Key.None);
                drift = Mathf.Max(drift, Flat(_body.Pelvis.position - letGo));
            }
            Vector3 v = _body.Pelvis.linearVelocity;

            log.AppendLine("A  ONE LEG, let go of the key after hopping");
            log.AppendLine($"     speed when released {Flat(vAtLetGo):0.00} m/s");
            log.AppendLine($"     glided {drift:0.00} m over 3s with no input");
            log.AppendLine($"     still moving at {Flat(v):0.00} m/s, pelvis y {_body.Pelvis.position.y:0.00}");

            // ---- B: forward then reverse --------------------------------
            BoardAll();
            for (int i = 0; i < 150; i++) Tick(Key.None);

            Vector3 h0 = _body.Heading;
            for (int i = 0; i < 250; i++)
            {
                int ph = i % 20;
                Tick(ph < 3 ? Key.W : ph >= 10 && ph < 13 ? Key.I : Key.None);
            }
            Vector3 h1 = _body.Heading;
            Vector3 afterFwd = _body.Pelvis.position;

            int steps = 0, crossed = 0, down = 0;
            for (int i = 0; i < 250; i++)
            {
                int ph = i % 20;
                Tick(ph < 3 ? Key.S : ph >= 10 && ph < 13 ? Key.K : Key.None);
                if (_body.LegL.JustStepped || _body.LegR.JustStepped) steps++;
                if (Separation() < 0f) crossed++;
                if (_body.Collapsed) down++;
            }
            Vector3 h2 = _body.Heading;
            Vector3 back = _body.Pelvis.position - afterFwd;

            log.AppendLine("B  TWO LEGS, walk forward then walk backward");
            log.AppendLine($"     heading turned {Vector3.Angle(h0, h1):0} deg while going forward");
            log.AppendLine($"     heading turned {Vector3.Angle(h1, h2):0} deg while going BACKWARD");
            log.AppendLine($"     moved {back.z:0.00} m on z, {steps} steps, crossed {crossed}/250 ticks, collapsed {down} ticks");

            // ---- C: feet twisting during a turn -------------------------
            BoardAll();
            for (int i = 0; i < 150; i++) Tick(Key.None);

            float worstTwist = 0f, worstPitch = 0f, worstRoll = 0f;
            for (int i = 0; i < 300; i++)
            {
                int ph = i % 20;
                Tick(ph < 3 ? Key.A : ph >= 10 && ph < 13 ? Key.J : Key.None);

                worstTwist = Mathf.Max(worstTwist, Twist(_body.LegL));
                worstTwist = Mathf.Max(worstTwist, Twist(_body.LegR));
                if (_body.LegL.Planted) worstPitch = Mathf.Max(worstPitch, Pitch(_body.LegL));
                if (_body.LegR.Planted) worstPitch = Mathf.Max(worstPitch, Pitch(_body.LegR));
                worstRoll = Mathf.Max(worstRoll, Roll(_body.LegL));
                worstRoll = Mathf.Max(worstRoll, Roll(_body.LegR));
            }

            log.AppendLine("C  TURNING LEFT for 6s");
            log.AppendLine($"     worst foot twist away from the body  {worstTwist:0} deg");
            log.AppendLine($"     worst planted pitch                  {worstPitch:0} deg");
            log.AppendLine($"     worst roll (foot rolled onto an edge) {worstRoll:0} deg");
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(Key.None);
        }

        return log.ToString();
    }

    /// Walk forward, then walk backward, and watch the body's posture the whole way.
    [MenuItem("Coat/Check Reverse")]
    public static void ReverseFromMenu() => Debug.Log(CheckReverse());

    public static string CheckReverse()
    {
        if (!EditorApplication.isPlaying) return "Needs play mode.";

        _game = Object.FindAnyObjectByType<CoatGame>(FindObjectsInactive.Include);
        if (_game == null || _game.Vehicle == null) return "No CoatGame with a vehicle.";
        _body = _game.Vehicle.Body;

        var log = new StringBuilder();
        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;

        try
        {
            BoardAll();
            for (int i = 0; i < 150; i++) Tick(Key.None);
            log.AppendLine($"standing:  torso {Lean(_body.Torso):0} deg, pelvis {Lean(_body.Pelvis):0} deg, " +
                           $"balance {_body.Stability:0.00}, hips {Overhang():0.00} m from the feet");

            log.AppendLine("FORWARD 5s");
            log.AppendLine("  " + Posture(250, Key.W, Key.I));

            log.AppendLine("THEN BACKWARD 5s");
            log.AppendLine("  " + Posture(250, Key.S, Key.K));

            for (int i = 0; i < 150; i++) Tick(Key.None);
            log.AppendLine($"settled:   torso {Lean(_body.Torso):0} deg, pelvis {Lean(_body.Pelvis):0} deg, " +
                           $"balance {_body.Stability:0.00}, hips {Overhang():0.00} m from the feet");
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(Key.None);
        }

        return log.ToString();
    }

    static string Posture(int ticks, Key left, Key right)
    {
        float worstTorso = 0f, worstPelvis = 0f, worstOver = 0f, worstStretch = 0f, lowBal = 1f;
        float worstAccel = 0f;
        Vector3 lastV = _body.Pelvis.linearVelocity;
        int downTicks = 0;

        for (int i = 0; i < ticks; i++)
        {
            int ph = i % 20;
            Tick(ph < 3 ? left : ph >= 10 && ph < 13 ? right : Key.None);

            Vector3 v = _body.Pelvis.linearVelocity;
            Vector3 dv = v - lastV;
            dv.y = 0f;
            worstAccel = Mathf.Max(worstAccel, dv.magnitude / Dt);
            lastV = v;

            worstTorso = Mathf.Max(worstTorso, Lean(_body.Torso));
            worstPelvis = Mathf.Max(worstPelvis, Lean(_body.Pelvis));
            worstOver = Mathf.Max(worstOver, Overhang());
            worstStretch = Mathf.Max(worstStretch, Mathf.Max(Stretch(_body.LegL), Stretch(_body.LegR)));
            lowBal = Mathf.Min(lowBal, _body.Stability);
            if (_body.Collapsed) downTicks++;
        }

        return $"worst torso {worstTorso:0} deg, pelvis {worstPelvis:0} deg, hips {worstOver:0.00} m past the feet, " +
               $"leg {worstStretch * 100f:0} % stretched, lowest balance {lowBal:0.00}, down {downTicks} ticks, " +
               $"peak hip acceleration {worstAccel:0} m/s2 ({worstAccel / 9.81f:0.0} g)";
    }

    static float Lean(Rigidbody rb) => Vector3.Angle(rb.transform.up, Vector3.up);

    /// How far the hips are from the middle of the two foot targets, flat.
    static float Overhang()
    {
        Vector3 support = (_body.LegL.StepTarget + _body.LegR.StepTarget) * 0.5f;
        Vector3 d = _body.Pelvis.position - support;
        d.y = 0f;
        return d.magnitude;
    }

    /// Catches a planted leg folding up under the body instead of standing on the
    /// floor -- the foot ends up level with its own hip while the target is on the ground.
    [MenuItem("Coat/Check Folded Legs")]
    public static void FoldedFromMenu() => Debug.Log(CheckFolded());

    public static string CheckFolded()
    {
        if (!EditorApplication.isPlaying) return "Needs play mode.";

        _game = Object.FindAnyObjectByType<CoatGame>(FindObjectsInactive.Include);
        if (_game == null || _game.Vehicle == null) return "No CoatGame with a vehicle.";
        _body = _game.Vehicle.Body;

        var log = new StringBuilder();
        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;

        try
        {
            BoardAll();
            for (int i = 0; i < 200; i++) Tick(Key.None);
            log.AppendLine($"standing:  left {Extend(_body.LegL) * 100f:0}%, right {Extend(_body.LegR) * 100f:0}%");

            log.AppendLine("WALKING FORWARD 8s");
            log.AppendLine("  " + Folding(400, Key.W, Key.I));

            log.AppendLine("WALKING BACKWARD 8s");
            log.AppendLine("  " + Folding(400, Key.S, Key.K));

            log.AppendLine("TURNING 6s");
            log.AppendLine("  " + Folding(300, Key.A, Key.J));

            for (int i = 0; i < 200; i++) Tick(Key.None);
            log.AppendLine($"settled:   left {Extend(_body.LegL) * 100f:0}%, right {Extend(_body.LegR) * 100f:0}%");
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(Key.None);
        }

        return log.ToString();
    }

    static string Folding(int ticks, Key left, Key right)
    {
        float tightest = 9f, highestFoot = 0f;
        int folded = 0, n = 0;

        for (int i = 0; i < ticks; i++)
        {
            int ph = i % 20;
            Tick(ph < 3 ? left : ph >= 10 && ph < 13 ? right : Key.None);

            foreach (var leg in new[] { _body.LegL, _body.LegR })
            {
                if (!leg.Planted) continue;
                float e = Extend(leg);
                tightest = Mathf.Min(tightest, e);
                highestFoot = Mathf.Max(highestFoot, leg.End.position.y);
                if (e < 0.45f) folded++;
                n++;
            }
        }

        return $"tightest a planted leg folded to {tightest * 100f:0}% of its length, " +
               $"highest a planted foot got {highestFoot:0.00} m, " +
               $"folded under 45% on {folded} of {n} planted samples";
    }

    static float Extend(ClassicLimb leg)
    {
        Vector3 hip = leg.Root.transform.TransformPoint(leg.RootAnchorLocal);
        return Vector3.Distance(hip, leg.End.position) / (leg.UpperLen + leg.LowerLen);
    }

    /// Where the body actually points versus where it is being steered. These are not
    /// the same thing: Heading is the intent, and the upright drive only torques the
    /// pelvis towards it. If the drive is losing, the body faces somewhere else.
    [MenuItem("Coat/Check Heading")]
    public static void HeadingFromMenu() => Debug.Log(CheckHeading());

    public static string CheckHeading()
    {
        if (!EditorApplication.isPlaying) return "Needs play mode.";

        _game = Object.FindAnyObjectByType<CoatGame>(FindObjectsInactive.Include);
        if (_game == null || _game.Vehicle == null) return "No CoatGame with a vehicle.";
        _body = _game.Vehicle.Body;

        var log = new StringBuilder();
        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;

        try
        {
            BoardAll();
            for (int i = 0; i < 200; i++) Tick(Key.None);
            log.AppendLine($"standing:      body is {YawError():0} deg off its own heading");

            log.AppendLine("STANDING STILL 5s");
            log.AppendLine("  " + Heading(250, Key.None, Key.None));

            log.AppendLine("WALKING FORWARD 8s");
            log.AppendLine("  " + Heading(400, Key.W, Key.I));

            log.AppendLine("WALKING BACKWARD 8s");
            log.AppendLine("  " + Heading(400, Key.S, Key.K));

            log.AppendLine("TURNING LEFT 6s");
            log.AppendLine("  " + Heading(300, Key.A, Key.J));

            for (int i = 0; i < 200; i++) Tick(Key.None);
            log.AppendLine($"settled:       body is {YawError():0} deg off its own heading");
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(Key.None);
        }

        return log.ToString();
    }

    static string Heading(int ticks, Key left, Key right)
    {
        float worst = 0f, total = 0f, worstTorso = 0f;
        int n = 0;
        for (int i = 0; i < ticks; i++)
        {
            int ph = i % 20;
            Key k = left == Key.None ? Key.None
                  : ph < 3 ? left : ph >= 10 && ph < 13 ? right : Key.None;
            Tick(k);

            float e = YawError();
            worst = Mathf.Max(worst, e);
            worstTorso = Mathf.Max(worstTorso, TorsoYawError());
            total += e;
            n++;
        }
        return $"pelvis worst {worst:0} deg off heading, average {total / n:0} deg; torso worst {worstTorso:0} deg";
    }

    static float YawError()
    {
        Vector3 facing = Vector3.ProjectOnPlane(_body.Pelvis.transform.forward, Vector3.up);
        Vector3 want = Vector3.ProjectOnPlane(_body.Heading, Vector3.up);
        if (facing.sqrMagnitude < 1e-4f || want.sqrMagnitude < 1e-4f) return 0f;
        return Vector3.Angle(facing, want);
    }

    static float TorsoYawError()
    {
        Vector3 facing = Vector3.ProjectOnPlane(_body.Torso.transform.forward, Vector3.up);
        Vector3 want = Vector3.ProjectOnPlane(_body.Heading, Vector3.up);
        if (facing.sqrMagnitude < 1e-4f || want.sqrMagnitude < 1e-4f) return 0f;
        return Vector3.Angle(facing, want);
    }

    /// Do the feet point the same way the body does?
    [MenuItem("Coat/Check Foot Aim")]
    public static void FootAimFromMenu() => Debug.Log(CheckFootAim());

    public static string CheckFootAim()
    {
        if (!EditorApplication.isPlaying) return "Needs play mode.";

        _game = Object.FindAnyObjectByType<CoatGame>(FindObjectsInactive.Include);
        if (_game == null || _game.Vehicle == null) return "No CoatGame with a vehicle.";
        _body = _game.Vehicle.Body;

        var log = new StringBuilder();
        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;

        try
        {
            BoardAll();
            for (int i = 0; i < 150; i++) Tick(Key.None);
            log.AppendLine($"standing:      left {Twist(_body.LegL):0} deg off the body, right {Twist(_body.LegR):0} deg");

            log.AppendLine("WALKING FORWARD 8s");
            log.AppendLine("  " + AimOver(400, Key.W, Key.I));

            log.AppendLine("SIDE-STEPPING RIGHT 8s");
            log.AppendLine("  " + AimOver(400, Key.D, Key.L));

            for (int i = 0; i < 150; i++) Tick(Key.None);
            log.AppendLine($"settled after: left {Twist(_body.LegL):0} deg off the body, right {Twist(_body.LegR):0} deg");
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(Key.None);
        }

        return log.ToString();
    }

    static string AimOver(int ticks, Key left, Key right)
    {
        float worst = 0f, total = 0f;
        int n = 0;
        for (int i = 0; i < ticks; i++)
        {
            int ph = i % 20;
            Tick(ph < 3 ? left : ph >= 10 && ph < 13 ? right : Key.None);
            float t = Mathf.Max(Twist(_body.LegL), Twist(_body.LegR));
            worst = Mathf.Max(worst, t);
            total += t;
            n++;
        }
        return $"worst {worst:0} deg off the body, average {total / n:0} deg";
    }

    /// Is the leg being asked to reach further than it physically can? A leg stretched
    /// straight cannot hold the hip up, so the body pitches forward over it -- which is
    /// what a chicken walk looks like.
    [MenuItem("Coat/Check Stride")]
    public static void StrideFromMenu() => Debug.Log(CheckStride());

    public static string CheckStride()
    {
        if (!EditorApplication.isPlaying) return "Needs play mode.";

        _game = Object.FindAnyObjectByType<CoatGame>(FindObjectsInactive.Include);
        if (_game == null || _game.Vehicle == null) return "No CoatGame with a vehicle.";
        _body = _game.Vehicle.Body;

        var log = new StringBuilder();
        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;

        try
        {
            BoardAll();
            for (int i = 0; i < 150; i++) Tick(Key.None);

            var leg = _body.LegL;
            float span = leg.UpperLen + leg.LowerLen;
            log.AppendLine($"leg is {span:0.00} m long, MaxReach is set to {leg.MaxReach:0.00} m, StepLength {leg.StepLength:0.00} m");
            log.AppendLine($"standing: hip {HipY(leg):0.00}, ankle target {leg.StepTarget.y:0.00}, " +
                           $"so the most it can reach sideways is {Reachable(leg):0.00} m");

            float worstReach = 0f, worstStretch = 0f, worstLean = 0f, lowest = 9f;
            for (int i = 0; i < 400; i++)
            {
                int ph = i % 20;
                Tick(ph < 3 ? Key.W : ph >= 10 && ph < 13 ? Key.I : Key.None);

                worstReach = Mathf.Max(worstReach, Mathf.Max(FlatReach(_body.LegL), FlatReach(_body.LegR)));
                worstStretch = Mathf.Max(worstStretch, Mathf.Max(Stretch(_body.LegL), Stretch(_body.LegR)));
                worstLean = Mathf.Max(worstLean, Vector3.Angle(_body.Torso.transform.up, Vector3.up));
                lowest = Mathf.Min(lowest, _body.Pelvis.position.y);
            }

            log.AppendLine("WALKING FORWARD 8s");
            log.AppendLine($"  furthest a foot got from its hip, flat:  {worstReach:0.00} m");
            log.AppendLine($"  most the leg was stretched:              {worstStretch * 100f:0} % of its length");
            log.AppendLine($"  worst torso lean from upright:           {worstLean:0} deg");
            log.AppendLine($"  lowest the pelvis dropped:               {lowest:0.00} m (stands at 0.79)");
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(Key.None);
        }

        return log.ToString();
    }

    static float HipY(ClassicLimb leg) => leg.Root.transform.TransformPoint(leg.RootAnchorLocal).y;

    /// Horizontal distance from the hip to the foot target.
    static float FlatReach(ClassicLimb leg)
    {
        Vector3 hip = leg.Root.transform.TransformPoint(leg.RootAnchorLocal);
        Vector3 d = leg.StepTarget - hip;
        d.y = 0f;
        return d.magnitude;
    }

    /// How much of the leg's length the hip-to-target distance is using up.
    static float Stretch(ClassicLimb leg)
    {
        Vector3 hip = leg.Root.transform.TransformPoint(leg.RootAnchorLocal);
        return Vector3.Distance(hip, leg.StepTarget) / (leg.UpperLen + leg.LowerLen);
    }

    /// The furthest sideways a leg of this length can plant from this hip height.
    static float Reachable(ClassicLimb leg)
    {
        float span = leg.UpperLen + leg.LowerLen;
        float drop = HipY(leg) - leg.AnkleHeight;
        float sq = span * span - drop * drop;
        return sq <= 0f ? 0f : Mathf.Sqrt(sq);
    }

    /// A turn like a player actually does: about a quarter turn, then stop. The other
    /// test spins for six seconds straight, which is over two full rotations.
    [MenuItem("Coat/Check Normal Turn")]
    public static void NormalTurnFromMenu() => Debug.Log(NormalTurn());

    public static string NormalTurn()
    {
        if (!EditorApplication.isPlaying) return "Needs play mode.";

        _game = Object.FindAnyObjectByType<CoatGame>(FindObjectsInactive.Include);
        if (_game == null || _game.Vehicle == null) return "No CoatGame with a vehicle.";
        _body = _game.Vehicle.Body;

        var log = new StringBuilder();
        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;

        try
        {
            BoardAll();
            for (int i = 0; i < 150; i++) Tick(Key.None);

            Vector3 h0 = _body.Heading;
            float worst = 0f;

            // roughly a quarter turn: two alternating steps to the left
            for (int i = 0; i < 80; i++)
            {
                int ph = i % 20;
                Tick(ph < 3 ? Key.A : ph >= 10 && ph < 13 ? Key.J : Key.None);
                if (_body.LegL.Planted) worst = Mathf.Max(worst, Pitch(_body.LegL));
                if (_body.LegR.Planted) worst = Mathf.Max(worst, Pitch(_body.LegR));
            }
            log.AppendLine($"during a {Vector3.Angle(h0, _body.Heading):0} deg turn: worst planted pitch {worst:0} deg");

            worst = 0f;
            for (int i = 0; i < 120; i++)
            {
                Tick(Key.None);
                if (_body.LegL.Planted) worst = Mathf.Max(worst, Pitch(_body.LegL));
                if (_body.LegR.Planted) worst = Mathf.Max(worst, Pitch(_body.LegR));
            }
            log.AppendLine($"after it, standing for 2.4s:  worst planted pitch {worst:0} deg");
            log.AppendLine($"settled:  left {Pitch(_body.LegL):0} deg, right {Pitch(_body.LegR):0} deg");

            // then walk off normally and check it recovers
            worst = 0f;
            for (int i = 0; i < 200; i++)
            {
                int ph = i % 20;
                Tick(ph < 3 ? Key.W : ph >= 10 && ph < 13 ? Key.I : Key.None);
                if (_body.LegL.Planted) worst = Mathf.Max(worst, Pitch(_body.LegL));
                if (_body.LegR.Planted) worst = Mathf.Max(worst, Pitch(_body.LegR));
            }
            log.AppendLine($"walking on afterwards:        worst planted pitch {worst:0} deg");
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(Key.None);
        }

        return log.ToString();
    }

    /// Follows one foot through a turn and reports the moment it goes wrong, with
    /// everything around it: was it in the air, where was the shin pointing, how far
    /// was the ankle being asked to bend.
    [MenuItem("Coat/Trace Foot Flip")]
    public static void TraceFromMenu() => Debug.Log(TraceFlip());

    public static string TraceFlip()
    {
        if (!EditorApplication.isPlaying) return "Needs play mode.";

        _game = Object.FindAnyObjectByType<CoatGame>(FindObjectsInactive.Include);
        if (_game == null || _game.Vehicle == null) return "No CoatGame with a vehicle.";
        _body = _game.Vehicle.Body;

        var log = new StringBuilder();
        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;

        try
        {
            BoardAll();
            for (int i = 0; i < 150; i++) Tick(Key.None);

            var leg = _body.LegL;
            log.AppendLine($"before turning: pitch {Pitch(leg):0} shin {ShinTilt(leg):0} from vertical, planted {leg.Planted}");

            float worst = 0f;
            string worstLine = "never went past 45 deg";
            int firstBadTick = -1;

            for (int i = 0; i < 300; i++)
            {
                int ph = i % 20;
                Tick(ph < 3 ? Key.A : ph >= 10 && ph < 13 ? Key.J : Key.None);

                float pitch = Pitch(leg);
                if (pitch > worst)
                {
                    worst = pitch;
                    worstLine = $"tick {i}  pitch {pitch:0}  airborne {leg.Airborne}  planted {leg.Planted}  " +
                                $"shin {ShinTilt(leg):0} off straight-down  ankle bend {AnkleBend(leg):0}  " +
                                $"footY {leg.End.position.y:0.00}  targetY {leg.StepTarget.y:0.00}";
                }
                if (pitch > 45f && firstBadTick < 0) firstBadTick = i;
            }

            log.AppendLine($"worst moment:  {worstLine}");
            log.AppendLine($"first went past 45 deg at tick {firstBadTick} of 300");
            log.AppendLine($"ankle joint limit is {_body.LegL.EndJoint.angularYLimit.limit:0} deg, " +
                           $"spring {_body.LegL.EndJoint.slerpDrive.positionSpring:0}");

            // How often, and does it come back?
            int badPlanted = 0, badAir = 0, samples = 0;
            for (int i = 0; i < 300; i++)
            {
                int ph = i % 20;
                Tick(ph < 3 ? Key.A : ph >= 10 && ph < 13 ? Key.J : Key.None);
                float pitch = Pitch(leg);
                if (pitch > 45f) { if (leg.Airborne) badAir++; else badPlanted++; }
                samples++;
            }
            log.AppendLine($"over another 6s: past 45 deg on {badPlanted} planted ticks and " +
                           $"{badAir} airborne ticks out of {samples}");
            log.AppendLine($"knee hinge axis flipped 180 deg: left leg {_body.LegL.HingeFlips} times, " +
                           $"right leg {_body.LegR.HingeFlips} times");
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(Key.None);
        }

        return log.ToString();
    }

    /// How far the shin leans off vertical.
    static float ShinTilt(ClassicLimb leg) =>
        Vector3.Angle(leg.Lower.rotation * Vector3.up, Vector3.down);

    /// The angle the ankle joint is actually holding between shin and foot.
    static float AnkleBend(ClassicLimb leg) =>
        Quaternion.Angle(leg.Lower.rotation, leg.End.rotation);

    /// Yaw difference between where the foot points and where the body faces.
    static float Twist(ClassicLimb leg)
    {
        Vector3 footFwd = Vector3.ProjectOnPlane(leg.End.transform.forward, Vector3.up);
        Vector3 bodyFwd = Vector3.ProjectOnPlane(_body.Pelvis.transform.forward, Vector3.up);
        if (footFwd.sqrMagnitude < 1e-4f || bodyFwd.sqrMagnitude < 1e-4f) return 0f;
        return Vector3.Angle(footFwd, bodyFwd);
    }

    /// How far the foot has rolled onto its inner or outer edge.
    static float Roll(ClassicLimb leg)
    {
        Vector3 side = leg.End.transform.right;
        return Mathf.Abs(90f - Vector3.Angle(side, Vector3.up));
    }

    static void BoardOnly(int role)
    {
        ClearCoat();
        Approach(role);
        Press(role);
        for (int i = 0; i < 120; i++) Tick(Key.None);
    }

    static void BoardAll()
    {
        ClearCoat();
        for (int i = 0; i < 4; i++) Approach(i);
        for (int i = 0; i < 4; i++) Press(i);
        for (int i = 0; i < 120; i++) Tick(Key.None);
    }

    static void ClearCoat()
    {
        var coat = _game.Coat;
        coat.Vacate();
        for (int i = 0; i < 4; i++) Tick(Key.None);
        if (_body.gameObject.activeInHierarchy) _body.gameObject.SetActive(false);
        coat.DropAt(new Vector3(0f, 0.4f, 1.4f), Vector3.forward);
        for (int i = 0; i < 60; i++) Tick(Key.None);
    }

    static void Approach(int role)
    {
        var c = _game.Characters[role];
        if (!c.gameObject.activeInHierarchy) c.gameObject.SetActive(true);
        Vector3 centre = _game.Coat.Centre;
        float a = role / 4f * Mathf.PI * 2f;
        Vector3 at = centre + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.8f;
        at.y = 0f;
        c.PlaceAt(at, centre - at);
        for (int i = 0; i < 25; i++) Tick(Key.None);
    }

    static void Press(int role)
    {
        Key[] keys = { Key.Q, Key.O, Key.B, Key.M };
        for (int k = 0; k < 6; k++) Tick(keys[role]);
        for (int k = 0; k < 40; k++) Tick(Key.None);
    }

    static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    /// How far the foot's own up axis leans off vertical. A flat foot reads near zero.
    static float Pitch(ClassicLimb leg) =>
        Vector3.Angle(leg.End.transform.up, Vector3.up);

    /// How high the back edge of the foot sits above the ground it is standing on.
    static float HeelGap(ClassicLimb leg)
    {
        Transform f = leg.End.transform;
        Vector3 heel = f.position - f.forward * (f.localScale.z * 0.5f) - f.up * (f.localScale.y * 0.5f);
        float ground = Physics.Raycast(heel + Vector3.up * 0.6f, Vector3.down, out RaycastHit hit, 3f,
                                       CoatLayers.NotRig, QueryTriggerInteraction.Ignore)
            ? hit.point.y : 0f;
        return Mathf.Max(0f, heel.y - ground);
    }

    /// Positive means the feet are on their correct sides. Negative means they crossed.
    static float Separation()
    {
        Vector3 across = Vector3.ProjectOnPlane(_body.Pelvis.transform.right, Vector3.up).normalized;
        float l = Vector3.Dot(_body.LegL.End.position - _body.Pelvis.position, across);
        float r = Vector3.Dot(_body.LegR.End.position - _body.Pelvis.position, across);
        return r - l;
    }

    static string SideLine()
    {
        Vector3 across = Vector3.ProjectOnPlane(_body.Pelvis.transform.right, Vector3.up).normalized;
        float l = Vector3.Dot(_body.LegL.End.position - _body.Pelvis.position, across);
        float r = Vector3.Dot(_body.LegR.End.position - _body.Pelvis.position, across);
        return $"sideways offset from the body: left {l * 100f:0.0} cm, right {r * 100f:0.0} cm, gap {(r - l) * 100f:0.0} cm";
    }

    static string FootLine(string name, ClassicLimb leg)
    {
        Transform f = leg.End.transform;
        return $"{name} foot: pitch {Pitch(leg):0}deg  heel {HeelGap(leg) * 100f:0.0} cm up  " +
               $"toe {ToeGap(leg) * 100f:0.0} cm up  ankleY {f.position.y:0.00}";
    }

    static float ToeGap(ClassicLimb leg)
    {
        Transform f = leg.End.transform;
        Vector3 toe = f.position + f.forward * (f.localScale.z * 0.5f) - f.up * (f.localScale.y * 0.5f);
        float ground = Physics.Raycast(toe + Vector3.up * 0.6f, Vector3.down, out RaycastHit hit, 3f,
                                       CoatLayers.NotRig, QueryTriggerInteraction.Ignore)
            ? hit.point.y : 0f;
        return Mathf.Max(0f, toe.y - ground);
    }

    // ---- plumbing -----------------------------------------------------------

    static void Board()
    {
        var coat = _game.Coat;
        Key[] keys = { Key.Q, Key.O, Key.B, Key.M };

        if (!_game.Vehicle.Worn)
        {
            for (int i = 0; i < 4; i++)
            {
                var c = _game.Characters[i];
                if (!c.gameObject.activeInHierarchy) c.gameObject.SetActive(true);
                float a = i / 4f * Mathf.PI * 2f;
                Vector3 at = coat.Centre + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.8f;
                at.y = 0f;
                c.PlaceAt(at, coat.Centre - at);
            }
            for (int i = 0; i < 60; i++) Tick(Key.None);

            for (int i = 0; i < 4; i++)
            {
                for (int k = 0; k < 6; k++) Tick(keys[i]);
                for (int k = 0; k < 40; k++) Tick(Key.None);
            }
        }
    }

    static void Send(Key key)
    {
        var state = new KeyboardState();
        if (key != Key.None) state.Set(key, true);
        InputSystem.QueueStateEvent(Keyboard.current, state);
        InputSystem.Update();
    }

    static void Tick(Key key)
    {
        Send(key);
        _game.Input.Sample();

        bool worn = _body.gameObject.activeInHierarchy;
        if (worn) { _body.Input.Sample(); _body.Tick(Dt); }

        _game.Tick(Dt);
        Physics.Simulate(Dt);
    }
}
