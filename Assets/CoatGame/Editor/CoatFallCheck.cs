using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Coat;
using Coat.Classic;

/// "Walk too fast or turn too fast and the torso lies down while the feet keep trying
/// to walk."
///
/// Two separate questions:
///   1. Does the spine really go over that far, and does it come back?
///   2. If it does not come back, why is that not counted as falling over?
///
/// Collapse is detected purely by head height, and a body lying flat still has its
/// head up at about hip height -- so the detector never fires and the disguise keeps
/// walking along horizontal for ever.
public static class CoatFallCheck
{
    const float Dt = 1f / 50f;

    static CoatGame _game;
    static CoatVehicle _vehicle;
    static TheCoat _coat;
    static ClassicRagdoll _body;

    static readonly Key[] FwdL = { Key.W }, FwdR = { Key.I };
    static readonly Key[] BackL = { Key.S }, BackR = { Key.K };
    static readonly Key[] BothFwd = { Key.W, Key.I };
    static readonly Key[] BothBack = { Key.S, Key.K };

    [MenuItem("Coat/Check Lying Down")]
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
            log.AppendLine("Pitch is how far the spine is off vertical. 90 is lying flat.");
            log.AppendLine("");

            Board(4);
            log.AppendLine(Phase("full crew, thirty seconds of random mashing", 1500, Chaos));
            log.AppendLine(Phase("full crew, flipping direction every 8 ticks", 600, Flip));

            Board(1);
            log.AppendLine(Phase("ONE LEG, thirty seconds of random mashing", 1500, Chaos));
            log.AppendLine(Phase("ONE LEG, flipping direction every 8 ticks", 600, Flip));

            Board(4);
            Shove();
            log.AppendLine(Phase("full crew, driven into the kerb and turning", 600, Flip));
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(null);
        }

        return log.ToString();
    }

    /// Pressing S and K walks backwards, and the thigh comes up horizontal with the
    /// knee at hip height, like a high march.
    ///
    /// A two-bone IK folds like that when the foot target is too CLOSE to the hip:
    /// there is more leg than there is distance, so the knee has to go somewhere and
    /// it goes out sideways. So the thing to watch is how much of the leg's length the
    /// target is actually asking for.
    [MenuItem("Coat/Check Backward Leg")]
    public static void BackFromMenu() => Debug.Log(Backward());

    public static string Backward()
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
            Board(2);
            log.AppendLine("Thigh angle: 0 is hanging straight down, 90 is sticking out level.");
            log.AppendLine("Reach is how much of the leg's length the foot target asks for;");
            log.AppendLine("a small number means the leg has to fold up to fit.");
            log.AppendLine("");
            log.AppendLine(Legs("holding W and I  (forward)", FwdL, FwdR));
            log.AppendLine(Legs("holding S and K  (backward)", BackL, BackR));
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(null);
        }

        return log.ToString();
    }

    /// How low the hips are allowed to sink decides how far out a planted foot may be
    /// left, and that is what splays the leg going backwards. Swept with a Place()
    /// between runs rather than a re-boarding, because emptying and re-wearing the
    /// coat has its own fault that would poison every case after the first.
    [MenuItem("Coat/Sweep Stand Floor")]
    public static void FloorFromMenu() => Debug.Log(Floor());

    public static string Floor()
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
        float was = _body.MinStandHeight;

        try
        {
            Board(2);
            log.AppendLine("thigh angle off vertical, and how far a planted foot is left from its hip");
            foreach (float floor in new[] { 0.72f, 0.78f, 0.84f, 0.90f })
            {
                _body.MinStandHeight = floor;

                _body.Place(new Vector3(0f, 0f, 1.4f), Vector3.forward);
                for (int i = 0; i < 80; i++) Tick(null);
                var fwd = Measure(300, FwdL, FwdR);

                _body.Place(new Vector3(0f, 0f, 1.4f), Vector3.forward);
                for (int i = 0; i < 80; i++) Tick(null);
                var back = Measure(300, BackL, BackR);

                log.AppendLine($"  floor {floor,4:0.00}   forward: {fwd}");
                log.AppendLine($"                back:    {back}");
            }
        }
        finally
        {
            _body.MinStandHeight = was;
            Physics.simulationMode = mode;
            Send(null);
        }

        return log.ToString();
    }

    static string Measure(int ticks, Key[] kL, Key[] kR)
    {
        float worstThigh = 0f, worstOut = 0f, lowestHip = 9f;
        Vector3 from = _body.Pelvis.position;

        for (int i = 0; i < ticks; i++)
        {
            int ph = i % 20;
            Tick(ph < 3 ? kL : ph >= 10 && ph < 13 ? kR : null);

            foreach (var leg in new[] { _body.LegL, _body.LegR })
            {
                if (!leg.Present) continue;
                worstThigh = Mathf.Max(worstThigh,
                    Vector3.Angle(leg.Upper.transform.up, Vector3.down));

                if (!leg.Planted) continue;
                Vector3 hip = leg.Root.transform.TransformPoint(leg.RootAnchorLocal);
                Vector3 flat = leg.Target - hip;
                flat.y = 0f;
                worstOut = Mathf.Max(worstOut, flat.magnitude);
                lowestHip = Mathf.Min(lowestHip, hip.y);
            }
        }

        Vector3 went = _body.Pelvis.position - from;
        went.y = 0f;
        return $"travelled {went.magnitude,4:0.00} m   worst thigh {worstThigh,3:0} deg   " +
               $"foot up to {worstOut * 100f,4:0} cm from its hip   lowest hip {lowestHip,4:0.00}";
    }

    static string Legs(string label, Key[] kL, Key[] kR)
    {
        float worstThigh = 0f, leastReach = 9f, worstTargetY = 0f;
        var rows = new StringBuilder();

        for (int i = 0; i < 300; i++)
        {
            int ph = i % 20;
            Tick(ph < 3 ? kL : ph >= 10 && ph < 13 ? kR : null);

            foreach (var leg in new[] { _body.LegL, _body.LegR })
            {
                if (!leg.Present) continue;

                Vector3 hip = leg.Root.transform.TransformPoint(leg.RootAnchorLocal);
                // The bone's +Y runs from its parent end to its child end, so hip->knee
                // is +up, not -up. Measured the wrong way round first time and got 174
                // degrees for a perfectly normal forward stride.
                float thigh = Vector3.Angle(leg.Upper.transform.up, Vector3.down);
                float reach = Vector3.Distance(hip, leg.Target) / (leg.UpperLen + leg.LowerLen);

                worstThigh = Mathf.Max(worstThigh, thigh);
                leastReach = Mathf.Min(leastReach, reach);
                worstTargetY = Mathf.Max(worstTargetY, leg.Target.y);
            }

            if (i % 50 == 0)
            {
                var l = _body.LegL;
                Vector3 hipL = l.Root.transform.TransformPoint(l.RootAnchorLocal);
                rows.AppendLine($"      t+{i * Dt,4:0.0}s  thighL " +
                    $"{Vector3.Angle(l.Upper.transform.up, Vector3.down),3:0} deg  " +
                    $"reach {Vector3.Distance(hipL, l.Target) / (l.UpperLen + l.LowerLen),4:0.00}  " +
                    $"targetY {l.Target.y,5:0.00}  hipY {hipL.y,5:0.00}  " +
                    $"{(l.Airborne ? "swinging" : "planted ")}");
            }
        }

        return $"  {label}\n" +
               $"     worst thigh angle {worstThigh,3:0} deg   " +
               $"least reach asked for {leastReach,4:0.00} of the leg   " +
               $"highest target {worstTargetY,4:0.00}\n" + rows;
    }

    /// Tip the disguise onto its face and see whether it notices.
    ///
    /// Before MaxLean it did not: falling was judged on head HEIGHT, and a body lying
    /// flat holds its head at about hip height, so it never counted as down, never got
    /// up, and walked along horizontal with its feet still going.
    [MenuItem("Coat/Check Lying Down Recovers")]
    public static void TipFromMenu() => Debug.Log(Tip());

    public static string Tip()
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
            Board(4);

            // Lay the spine over, leaving the legs where they are, which is the pose
            // that was drawn.
            Quaternion tip = Quaternion.AngleAxis(85f, _body.Pelvis.transform.right);
            foreach (var rb in new[] { _body.Pelvis, _body.Torso, _body.Head })
            {
                // Both the transform and the body. Writing only Rigidbody.rotation
                // leaves transform.rotation stale until the next step, and the first
                // attempt at this measured the stale one and reported 3 degrees.
                Quaternion r = tip * rb.rotation;
                rb.transform.rotation = r;
                rb.rotation = r;
                rb.angularVelocity = Vector3.zero;
                rb.linearVelocity = Vector3.zero;
            }

            log.AppendLine($"tipped over: spine {Vector3.Angle(_body.Torso.transform.up, Vector3.up),3:0} deg, " +
                           $"head {_body.Head.position.y:0.00}  (MaxLean {_body.MaxLean}, " +
                           $"HeadMinHeight {_body.HeadMinHeight})");

            // Held there for a second. A body that is merely knocked over rights itself
            // in well under the grace period -- measured, it came back from 88 degrees
            // before the timer could even start. What the player is describing stays
            // down, so something is holding it, and that is what this imitates.
            int down = 0, gettingUp = 0;
            float worst = 0f;
            for (int i = 0; i < 500; i++)
            {
                if (i < 50)
                {
                    foreach (var rb in new[] { _body.Pelvis, _body.Torso })
                    {
                        Quaternion r = tip * Quaternion.LookRotation(_body.Heading, Vector3.up);
                        rb.transform.rotation = r;
                        rb.rotation = r;
                        rb.angularVelocity = Vector3.zero;
                    }
                }

                int ph = i % 20;
                Tick(ph < 3 ? FwdL : ph >= 10 && ph < 13 ? FwdR : null);
                if (_body.Collapsed) down++;
                if (_body.Recovering) gettingUp++;
                worst = Mathf.Max(worst, Vector3.Angle(_body.Torso.transform.up, Vector3.up));
            }

            log.AppendLine($"  held over for 1 s, then let go");
            log.AppendLine($"  counted as a fall on {down} ticks, getting up on {gettingUp}");
            log.AppendLine($"  ten seconds later: spine {Vector3.Angle(_body.Torso.transform.up, Vector3.up),3:0} deg, " +
                           $"head {_body.Head.position.y:0.00}, hips {_body.Pelvis.position.y:0.00}");
            log.AppendLine($"  {(down > 0 ? "it noticed" : "*** IT NEVER NOTICED ***")}");
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(null);
        }

        return log.ToString();
    }

    /// Every key either player has, thrown at it in no order at all. A fixed seed so
    /// a run that finds the fault can be repeated.
    static Key[] Chaos(int i)
    {
        var rng = new System.Random(i * 2654435761u.GetHashCode() ^ 17);
        int roll = rng.Next(0, 10);
        if (roll < 3) return null;

        Key[] pool = { Key.W, Key.A, Key.S, Key.D, Key.I, Key.J, Key.K, Key.L };
        int n = rng.Next(1, 4);
        var keys = new Key[n];
        for (int k = 0; k < n; k++) keys[k] = pool[rng.Next(0, pool.Length)];
        return keys;
    }

    /// Reversing as fast as the legs can be told to, which is what "turning around too
    /// fast" means in practice.
    static Key[] Flip(int i)
    {
        bool back = (i / 8) % 2 == 1;
        int ph = i % 8;
        if (ph < 2) return back ? BackL : FwdL;
        if (ph >= 4 && ph < 6) return back ? BackR : FwdR;
        return null;
    }

    /// Put the body up against the kerb so the legs have something to trip on.
    static void Shove()
    {
        var kerb = GameObject.Find("Kerb");
        Vector3 at = kerb != null
            ? kerb.transform.position - Vector3.forward * 0.6f
            : new Vector3(0f, 0f, 3.2f);
        at.y = 0f;
        _body.Place(at, Vector3.forward);
        for (int i = 0; i < 60; i++) Tick(null);
    }

    static string Phase(string label, int ticks, System.Func<int, Key[]> input)
    {
        float worstTorso = 0f, worstPelvis = 0f, lowestHead = 99f;
        int overSixty = 0, collapsed = 0, recovered = 0;
        bool wasDown = false;
        float longestBout = 0f, bout = 0f;

        for (int i = 0; i < ticks; i++)
        {
            Tick(input(i));

            float torso = Vector3.Angle(_body.Torso.transform.up, Vector3.up);
            float pelvis = Vector3.Angle(_body.Pelvis.transform.up, Vector3.up);

            worstTorso = Mathf.Max(worstTorso, torso);
            worstPelvis = Mathf.Max(worstPelvis, pelvis);
            lowestHead = Mathf.Min(lowestHead, _body.Head.position.y);

            bool down = torso > 60f;
            if (down) { overSixty++; bout += Dt; longestBout = Mathf.Max(longestBout, bout); }
            else bout = 0f;

            if (_body.Collapsed) collapsed++;
            if (_body.Recovering) recovered++;
            if (down && !wasDown) { }
            wasDown = down;
        }

        return $"  {label}\n" +
               $"     worst pitch  torso {worstTorso,3:0} deg   pelvis {worstPelvis,3:0} deg\n" +
               $"     over 60 deg on {overSixty * 100f / ticks,3:0}% of ticks, " +
               $"longest bout {longestBout,4:0.0} s\n" +
               $"     lowest head {lowestHead,4:0.00} (collapse fires under {_body.HeadMinHeight})\n" +
               $"     counted as a fall on {collapsed} ticks, getting up on {recovered}";
    }

    static void Board(int crew)
    {
        for (int i = 0; i < 4; i++)
        {
            var w = _coat.WearerAt((CoatRole)i);
            if (w != null) _coat.Leave(w);
        }
        for (int i = 0; i < 20; i++) Tick(null);

        _coat.DropAt(new Vector3(0f, 0.4f, 1.4f), Vector3.forward);
        for (int i = 0; i < 40; i++) Tick(null);

        for (int r = 0; r < crew; r++)
        {
            var c = _game.Characters[r];
            if (!c.gameObject.activeInHierarchy) c.gameObject.SetActive(true);
            float a = r / 4f * Mathf.PI * 2f;
            Vector3 at = _coat.Centre + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.7f;
            at.y = 0f;
            c.PlaceAt(at, _coat.Centre - at);
            for (int i = 0; i < 30; i++) Tick(null);

            _coat.TryEnter(c);
            for (int i = 0; i < 40; i++) Tick(null);
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
        if (_body.gameObject.activeInHierarchy) { _body.Input.Sample(); _body.Tick(Dt); }
        _game.Tick(Dt);
        Physics.Simulate(Dt);
        if (_vehicle.Cam != null) _vehicle.Cam.Step(Dt);
    }
}
