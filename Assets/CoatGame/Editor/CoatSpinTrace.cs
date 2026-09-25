using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Coat;
using Coat.Classic;

/// Why does the body face the opposite way to where the player is going?
///
/// Every earlier test read the heading through Vector3.ProjectOnPlane and compared
/// bearings, which throws away everything that is not yaw -- so a body folded flat on
/// its face still reported a tidy bearing and the test passed. This one prints the
/// whole state, and it walks the body the way a PLAYER does: taps, not a metronome,
/// and often only one leg's keys being pressed at all.
public static class CoatSpinTrace
{
    const float Dt = 1f / 50f;

    static CoatGame _game;
    static CoatVehicle _vehicle;
    static TheCoat _coat;
    static ClassicRagdoll _body;

    static readonly Key[] FwdL = { Key.W }, FwdR = { Key.I };
    static readonly Key[] LeftL = { Key.A }, LeftR = { Key.J };
    static readonly Key[] RightL = { Key.D }, RightR = { Key.L };
    static readonly Key[] BackL = { Key.S }, BackR = { Key.K };

    [MenuItem("Coat/Trace The Spin")]
    public static void FromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (!EditorApplication.isPlaying) return "Needs play mode.";

        _game = Object.FindAnyObjectByType<CoatGame>(FindObjectsInactive.Include);
        if (_game == null || _game.Vehicle == null) return "No CoatGame with a vehicle.";
        _vehicle = _game.Vehicle;
        _coat = _game.Coat;
        _body = _vehicle.Body;

        var log = new StringBuilder();
        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;

        try
        {
            log.AppendLine("Two legs aboard, no arms -- which is what the screenshots show.");
            log.AppendLine("");
            log.AppendLine(Case("BOTH leg players tapping   right, then left", RightL, RightR, LeftL, LeftR, true));
            log.AppendLine(Case("ONLY the left leg player   right, then left", RightL, null, LeftL, null, true));
            log.AppendLine(Case("BOTH leg players tapping   fwd,   then back", FwdL, FwdR, BackL, BackR, true));
            log.AppendLine(Case("ONLY the left leg player   fwd,   then back", FwdL, null, BackL, null, true));
            log.AppendLine(Case("ONLY the left leg player   fwd,   then left", FwdL, null, LeftL, null, true));
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(null);
        }

        return log.ToString();
    }

    static string Case(string label, Key[] aL, Key[] aR, Key[] bL, Key[] bR, bool verbose)
    {
        Board();

        var s = new StringBuilder();
        s.AppendLine("== " + label);

        Walk(aL, aR, 200, null);
        s.AppendLine("   after the first direction:  " + Snapshot());

        Vector3 mark = _body.Pelvis.position;
        var rows = verbose ? new StringBuilder() : null;
        Walk(bL, bR, 300, rows);

        Vector3 travel = _body.Pelvis.position - mark;
        travel.y = 0f;

        s.AppendLine("   after changing direction:   " + Snapshot());
        s.AppendLine($"   travelled {travel.magnitude:0.00} m on bearing {Bearing(travel):0}" +
                     $"   (asked for {Asked(bL ?? bR):0})");

        if (rows != null) s.Append(rows);
        return s.ToString();
    }

    static float Asked(Key[] k)
    {
        if (k == FwdL || k == FwdR) return 0f;
        if (k == BackL || k == BackR) return 180f;
        if (k == LeftL || k == LeftR) return -90f;
        return 90f;
    }

    /// Taps on a 0.4 s rhythm. When only one leg has a player, the other simply never
    /// presses -- which is the ordinary case when one person is testing on a keyboard.
    static void Walk(Key[] kL, Key[] kR, int ticks, StringBuilder rows)
    {
        for (int i = 0; i < ticks; i++)
        {
            int ph = i % 20;
            Key[] keys = ph < 3 ? kL : (ph >= 10 && ph < 13 ? kR : null);
            Tick(keys);
            if (rows != null && i % 25 == 0)
                rows.AppendLine($"      t+{i * Dt,4:0.00}s  " + Snapshot());
        }
    }

    static string Snapshot()
    {
        Vector3 h = _body.Heading;
        Vector3 pf = _body.Pelvis.transform.forward;
        Vector3 tf = _body.Torso.transform.forward;

        // How far off vertical the spine is -- the fold the screenshots show.
        float pelvisTilt = Vector3.Angle(_body.Pelvis.transform.up, Vector3.up);
        float torsoTilt = Vector3.Angle(_body.Torso.transform.up, Vector3.up);

        float yawErr = Mathf.DeltaAngle(Bearing(pf), Bearing(h));

        return $"heading {Bearing(h),4:0} (y {h.y,5:0.00})  pelvis {Bearing(pf),4:0}  " +
               $"yawErr {yawErr,4:0}  tilt p{pelvisTilt,3:0} t{torsoTilt,3:0}  " +
               $"head {_body.Head.position.y,4:0.00}  stab {_body.Stability,4:0.00}  " +
               $"{(_body.Collapsed ? "COLLAPSED " : "")}camYaw {CamYaw(),4:0}";
    }

    static float CamYaw() => _vehicle.Cam != null ? _vehicle.Cam.transform.eulerAngles.y : 0f;

    static float Bearing(Vector3 v)
    {
        Vector3 f = Vector3.ProjectOnPlane(v, Vector3.up);
        return f.sqrMagnitude < 1e-5f ? 0f : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
    }

    /// Two players only: the left leg and the right leg. No arms, matching the screenshots.
    static void Board()
    {
        _coat.Vacate();
        for (int i = 0; i < 4; i++) Tick(null);
        if (_body.gameObject.activeInHierarchy) _body.gameObject.SetActive(false);
        _coat.DropAt(new Vector3(0f, 0.4f, 1.4f), Vector3.forward);
        for (int i = 0; i < 40; i++) Tick(null);

        Key[] coatKeys = { Key.Q, Key.O };
        for (int r = 0; r < 2; r++)
        {
            var c = _game.Characters[r];
            if (!c.gameObject.activeInHierarchy) c.gameObject.SetActive(true);
            float a = r / 4f * Mathf.PI * 2f;
            Vector3 at = _coat.Centre + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.8f;
            at.y = 0f;
            c.PlaceAt(at, _coat.Centre - at);
            for (int i = 0; i < 25; i++) Tick(null);

            for (int k = 0; k < 6; k++) Tick(new[] { coatKeys[r] });
            for (int k = 0; k < 40; k++) Tick(null);
        }

        for (int i = 0; i < 100; i++) Tick(null);
    }

    static void Send(Key[] keys)
    {
        var state = new KeyboardState();
        if (keys != null) foreach (var k in keys) state.Set(k, true);
        InputSystem.QueueStateEvent(Keyboard.current, state);
        InputSystem.Update();
    }

    static void Tick(Key[] keys)
    {
        Send(keys);
        _game.Input.Sample();

        bool worn = _body.gameObject.activeInHierarchy;
        if (worn) { _body.Input.Sample(); _body.Tick(Dt); }

        _game.Tick(Dt);
        Physics.Simulate(Dt);
        if (_vehicle.Cam != null) _vehicle.Cam.Step(Dt);
    }

    // ---- why does the second leg not get in? ------------------------------

    /// The crew boards one at a time, and the moment the FIRST one is in, the body
    /// stands up and the coat's hub jumps from a heap on the floor to chest height on
    /// a standing figure. Everyone still outside is suddenly measured against a point
    /// well above their heads.
    [MenuItem("Coat/Check Boarding")]
    public static void BoardingFromMenu() => Debug.Log(Boarding());

    public static string Boarding()
    {
        if (!EditorApplication.isPlaying) return "Needs play mode.";

        _game = Object.FindAnyObjectByType<CoatGame>(FindObjectsInactive.Include);
        if (_game == null || _game.Vehicle == null) return "No CoatGame with a vehicle.";
        _vehicle = _game.Vehicle;
        _coat = _game.Coat;
        _body = _vehicle.Body;

        var log = new StringBuilder();
        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;

        try
        {
            _coat.Vacate();
            for (int i = 0; i < 4; i++) Tick(null);
            if (_body.gameObject.activeInHierarchy) _body.gameObject.SetActive(false);
            _coat.DropAt(new Vector3(0f, 0.4f, 1.4f), Vector3.forward);
            for (int i = 0; i < 40; i++) Tick(null);

            Key[] keys = { Key.Q, Key.O, Key.B, Key.M };
            string[] who = { "left leg ", "right leg", "left arm ", "right arm" };

            // Everyone walks up first, as they would in play, and only then presses.
            for (int r = 0; r < 4; r++)
            {
                var c = _game.Characters[r];
                if (!c.gameObject.activeInHierarchy) c.gameObject.SetActive(true);
                float a = r / 4f * Mathf.PI * 2f;
                Vector3 at = _coat.Centre + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.8f;
                at.y = 0f;
                c.PlaceAt(at, _coat.Centre - at);
            }
            for (int i = 0; i < 60; i++) Tick(null);

            for (int r = 0; r < 4; r++)
            {
                var c = _game.Characters[r];
                Vector3 gap = c.Body.position - _coat.Centre;
                Vector3 flat = new Vector3(gap.x, 0f, gap.z);

                bool near = flat.magnitude <= _coat.EnterRadius
                            && Mathf.Abs(gap.y) <= _coat.EnterHeight;

                log.AppendLine($"  {who[r]}  standing {flat.magnitude,5:0.00} m away, " +
                               $"{-gap.y,5:0.00} m below the hub   " +
                               $"(straight line would be {gap.magnitude,5:0.00})   " +
                               $"flat limit {_coat.EnterRadius}   " +
                               $"{(near ? "gets in" : "*** TOO FAR ***")}");

                for (int k = 0; k < 6; k++) Tick(new[] { keys[r] });
                for (int k = 0; k < 40; k++) Tick(null);
                log.AppendLine($"              aboard {_coat.Count}   legs {_body.Legs} arms {_body.Arms}");
            }
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(null);
        }

        return log.ToString();
    }

    // ---- hold still for a photograph --------------------------------------

    /// Board two legs, walk, and STOP the clock mid-stride so the pose can actually be
    /// looked at.
    ///
    /// PAUSES play mode rather than just holding physics in script mode. Script mode
    /// stops physics stepping but does NOT stop the body's own FixedUpdate, so it went
    /// on piling AddForce onto bodies that were never being simulated; they all came
    /// out at once on the first step afterwards and fired the rig 170 metres into the
    /// air. Anything measured after that was nonsense.
    [MenuItem("Coat/Freeze Mid Stride")]
    public static void FreezeFromMenu() => Debug.Log(Freeze(215));

    public static string Freeze(int ticks)
    {
        if (!EditorApplication.isPlaying) return "Needs play mode.";

        _game = Object.FindAnyObjectByType<CoatGame>(FindObjectsInactive.Include);
        if (_game == null || _game.Vehicle == null) return "No CoatGame with a vehicle.";
        _vehicle = _game.Vehicle;
        _coat = _game.Coat;
        _body = _vehicle.Body;

        Physics.simulationMode = SimulationMode.Script;
        Board();
        for (int i = 0; i < ticks; i++)
        {
            int ph = i % 20;
            Tick(ph < 3 ? FwdL : ph >= 10 && ph < 13 ? FwdR : null);
        }
        Send(null);

        // Hand physics back and hold the whole game still instead, so nothing keeps
        // running while the pose is being looked at.
        Physics.simulationMode = SimulationMode.FixedUpdate;
        EditorApplication.isPaused = true;

        var s = new StringBuilder();
        s.AppendLine("FROZEN mid stride (play mode paused).");
        s.AppendLine(Legs());
        s.AppendLine(Stance(_body.LegL, "L"));
        s.AppendLine(Stance(_body.LegR, "R"));
        return s.ToString();
    }

    [MenuItem("Coat/Thaw")]
    public static void Thaw()
    {
        Physics.simulationMode = SimulationMode.FixedUpdate;
        EditorApplication.isPaused = false;
        Debug.Log("running again");
    }

    // ---- which way do the knees bend? -------------------------------------

    /// The player drew what a bent leg should look like from the side: thigh down and
    /// FORWARD to the knee, shin back down to the ankle. If the knee is behind the
    /// hip-to-ankle line instead, the leg reads as a bird's, and from the side the whole
    /// body looks like it is facing the other way -- which is exactly the report.
    [MenuItem("Coat/Check Knees")]
    public static void KneesFromMenu() => Debug.Log(Knees());

    public static string Knees()
    {
        if (!EditorApplication.isPlaying) return "Needs play mode.";

        _game = Object.FindAnyObjectByType<CoatGame>(FindObjectsInactive.Include);
        if (_game == null || _game.Vehicle == null) return "No CoatGame with a vehicle.";
        _vehicle = _game.Vehicle;
        _coat = _game.Coat;
        _body = _vehicle.Body;

        var log = new StringBuilder();
        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;

        try
        {
            Board();
            log.AppendLine("Knee angle: 180 is a straight leg. A person stands near 175 and");
            log.AppendLine("walks around 160. Ninety is a deep squat. BACKWARDS means the knee");
            log.AppendLine("is on the wrong side of the hip-to-ankle line -- a bird's leg.");
            log.AppendLine("Toe is the angle between the foot's toe and the way the body faces.");
            log.AppendLine("");

            log.AppendLine("standing still");
            for (int i = 0; i < 100; i++) Tick(null);
            log.AppendLine(Legs());
            log.AppendLine(Stance(_body.LegL, "L"));
            log.AppendLine(Stance(_body.LegR, "R"));

            log.AppendLine("");
            log.AppendLine("walking forward");
            for (int i = 0; i < 200; i++)
            {
                int ph = i % 20;
                Tick(ph < 3 ? FwdL : ph >= 10 && ph < 13 ? FwdR : null);
                if (i % 40 == 0) log.AppendLine(Legs());
            }

            log.AppendLine("");
            log.AppendLine("walking backward");
            for (int i = 0; i < 200; i++)
            {
                int ph = i % 20;
                Tick(ph < 3 ? BackL : ph >= 10 && ph < 13 ? BackR : null);
                if (i % 40 == 0) log.AppendLine(Legs());
            }
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(null);
        }

        return log.ToString();
    }

    static string Legs() => "   " + One(_body.LegL, "L") + "   " + One(_body.LegR, "R") +
                            $"   hips {_body.Pelvis.position.y,4:0.00}" +
                            $"  head {_body.Head.position.y,4:0.00}" +
                            $"  face {Bearing(_body.Pelvis.transform.forward),4:0}" +
                            $"  tilt {Vector3.Angle(_body.Pelvis.transform.up, Vector3.up),3:0}";

    /// The stance solver's own inputs, so a hip height that will not come up can be
    /// read off rather than guessed at.
    static string Stance(ClassicLimb leg, string tag)
    {
        Vector3 hip = _body.Pelvis.transform.TransformPoint(leg.RootAnchorLocal);
        Vector3 flat = hip - leg.StepTarget;
        flat.y = 0f;

        float span = (leg.UpperLen + leg.LowerLen) * _body.StandExtend;
        float drop = Mathf.Sqrt(Mathf.Max(0.0025f, span * span - flat.sqrMagnitude));
        float below = _body.Pelvis.position.y - hip.y;
        float room = leg.StepTarget.y + drop + below;

        Vector3 realFoot = leg.End.position;
        Vector3 stray = realFoot - leg.StepTarget;

        return $"      {tag}: hip {hip.y,5:0.000}  below pelvis {below,5:0.000}  " +
               $"target {leg.StepTarget.ToString("F3")}  flat {flat.magnitude,5:0.000}  " +
               $"span {span,5:0.000}  drop {drop,5:0.000}  ROOM {room,5:0.000}  " +
               $"pelvis {_body.Pelvis.position.y,5:0.000}  foot off target {stray.magnitude,5:0.000}";
    }

    static string One(ClassicLimb leg, string tag)
    {
        Vector3 fwd = Vector3.ProjectOnPlane(_body.Pelvis.transform.forward, Vector3.up).normalized;

        // Each bone is a capsule whose local +Y runs from its parent end to its child
        // end, and whose origin is the midpoint.
        Vector3 hip = leg.Upper.position - leg.Upper.transform.up * (leg.UpperLen * 0.5f);
        Vector3 knee = leg.Upper.position + leg.Upper.transform.up * (leg.UpperLen * 0.5f);
        Vector3 ankle = leg.Lower.position + leg.Lower.transform.up * (leg.LowerLen * 0.5f);

        float bulge = Vector3.Dot(knee - (hip + ankle) * 0.5f, fwd);
        float toeErr = Vector3.Angle(
            Vector3.ProjectOnPlane(leg.End.transform.forward, Vector3.up), fwd);

        // Straight leg is 180. A relaxed human stands near 175 and walks around 160;
        // 95 is a deep squat, which is what the disguise used to do all round.
        float bend = Vector3.Angle(hip - knee, ankle - knee);

        return $"{tag} knee {bend,3:0}deg {(bulge < 0f ? "BACKWARDS" : "fwd")} " +
               $"toe {toeErr,3:0}";
    }
}
