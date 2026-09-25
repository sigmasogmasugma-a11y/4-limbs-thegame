using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Coat;
using Coat.Classic;

/// "I keep sliding" is two different faults wearing the same word, and they have
/// different cures, so this measures them apart.
///
///   SKATING  -- a foot that is supposed to be planted travels across the floor while
///               it is planted. The body looks like it is on ice even at walking pace.
///   COASTING -- you let go of the keys and the body carries on going.
///
/// It also reports how often BOTH feet are down at once, because the stance friction
/// that is supposed to stop the coasting is only applied on those ticks -- if the legs
/// never overlap, that friction has never once run.
public static class CoatSlideCheck
{
    const float Dt = 1f / 50f;

    static CoatGame _game;
    static CoatVehicle _vehicle;
    static TheCoat _coat;
    static ClassicRagdoll _body;

    static readonly Key[] FwdL = { Key.W }, FwdR = { Key.I };

    static int _crew = 4;

    [MenuItem("Coat/Check Sliding (full crew)")]
    public static void FromMenu() { _crew = 4; Debug.Log(Run()); }

    [MenuItem("Coat/Check Sliding (legs only)")]
    public static void LegsFromMenu() { _crew = 2; Debug.Log(Run()); }

    public static string Run(int crew)
    {
        _crew = crew;
        return Run();
    }

    /// Walking straight forward and drifting sideways.
    ///
    /// One leg in the coat means one support point, and the body is driven to stand
    /// over it -- but the anti-crossing clamp holds that foot several centimetres off
    /// the centre line, where it can never get under the body. So the body is pulled
    /// towards whichever leg is aboard, for as long as it walks.
    ///
    /// Roles: 0 left leg, 1 right leg.
    [MenuItem("Coat/Drift - left leg only")]
    public static void DriftLFromMenu() => Debug.Log(Drift(new[] { 0 }));

    [MenuItem("Coat/Drift - right leg only")]
    public static void DriftRFromMenu() => Debug.Log(Drift(new[] { 1 }));

    [MenuItem("Coat/Drift - both legs")]
    public static void DriftBothFromMenu() => Debug.Log(Drift(new[] { 0, 1 }));

    public static string Drift(int[] roles)
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
            BoardRoles(roles);
            log.AppendLine($"aboard: legs {_body.Legs}  L {_body.LegL.Present}  R {_body.LegR.Present}");

            Vector3 from = _body.Pelvis.position;
            Vector3 face = _body.Heading;
            Vector3 side = Vector3.Cross(Vector3.up, face).normalized;   // +side is the body's right

            float footSideSum = 0f;
            int n = 0;

            for (int i = 0; i < 400; i++)
            {
                int ph = i % 20;
                Key[] keys = ph < 3 ? FwdL : ph >= 10 && ph < 13 ? FwdR : null;
                Tick(keys);

                foreach (var leg in new[] { _body.LegL, _body.LegR })
                {
                    if (!leg.Present) continue;
                    footSideSum += Vector3.Dot(leg.StepTarget - _body.Pelvis.position, side);
                    n++;
                }
            }

            Vector3 went = _body.Pelvis.position - from;
            went.y = 0f;

            float along = Vector3.Dot(went, face);
            float across = Vector3.Dot(went, side);

            log.AppendLine($"  walked {along,5:0.00} m forward and {across,5:0.00} m sideways " +
                           $"({(across > 0f ? "to its RIGHT" : "to its LEFT")})");
            log.AppendLine($"  that is {Mathf.Abs(across) / Mathf.Max(0.01f, Mathf.Abs(along)) * 100f,4:0}% " +
                           $"of the distance travelled, {Vector3.Angle(went, face),3:0} deg off straight");
            if (n > 0)
                log.AppendLine($"  the feet sat {footSideSum / n * 100f,5:0.0} cm to the body's right on average " +
                               $"(negative is to its left)");
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(null);
        }

        return log.ToString();
    }

    /// Being "pulled" sideways while walking, in the BODY's own frame.
    ///
    /// The disguise turns to face where it is going, so a drift that is always to its
    /// own left reads to the player as left when walking forward, right when walking
    /// backward and towards the screen top when walking right -- which is exactly the
    /// set of reports. Measuring in the body frame collapses all of those into one
    /// number, and the two feet's average lateral position says where it comes from.
    [MenuItem("Coat/Check Sideways Pull")]
    public static void PullFromMenu() => Debug.Log(Pull());

    public static string Pull()
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
            log.AppendLine("Positive is to the body's RIGHT, negative to its LEFT.");
            log.AppendLine("Walking straight forward each time; anything sideways is a pull.");
            log.AppendLine("");

            // Somebody has to be in the coat before the body exists to be Placed.
            BoardRoles(new[] { 0, 1 });

            // Each of the four directions, measured twice: over the whole run, and
            // over the last two seconds only. If the tail is straight the veer is just
            // the turn happening; if the tail is still off, it never goes where asked.
            foreach (var way in new[]
            {
                new { name = "W and I (forward) ", l = FwdL,   r = FwdR },
                new { name = "S and K (back)    ", l = BackL,  r = BackR },
                new { name = "D and L (right)   ", l = RightL, r = RightR },
                new { name = "A and J (left)    ", l = LeftL,  r = LeftR },
            })
            {
                log.AppendLine(Heading(way.name, way.l, way.r));
            }

            log.AppendLine("");
            log.AppendLine("and the same four, by crew:");

            System.Func<int, Key[]> walk = i =>
            {
                int ph = i % 20;
                return ph < 3 ? FwdL : ph >= 10 && ph < 13 ? FwdR : null;
            };

            // An arm aboard on one side only hangs its weight on that side, and the
            // balance drive holds the pelvis over the feet rather than under the
            // centre of mass -- so the body should lean, and walk, towards it.
            foreach (var crew in new[]
            {
                new { name = "two legs, no arms      ", roles = new[] { 0, 1 } },
                new { name = "two legs + LEFT arm    ", roles = new[] { 0, 1, 2 } },
                new { name = "two legs + RIGHT arm   ", roles = new[] { 0, 1, 3 } },
                new { name = "full crew              ", roles = new[] { 0, 1, 2, 3 } },
            })
            {
                BoardRoles(crew.roles);
                log.AppendLine(Sideways($"{crew.name} (legs {_body.Legs} arms {_body.Arms})", walk));
            }
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(null);
        }

        return log.ToString();
    }

    static readonly Key[] LeftL = { Key.A }, LeftR = { Key.J };
    static readonly Key[] RightL = { Key.D }, RightR = { Key.L };
    static readonly Key[] BackL = { Key.S }, BackR = { Key.K };

    /// Does the body end up travelling the way the keys asked?
    ///
    /// Measured over the whole run AND over the last two seconds. The whole-run number
    /// includes the turn, which legitimately curves the path; the tail says whether it
    /// ever settles onto the right line.
    static string Heading(string label, Key[] kL, Key[] kR)
    {
        _body.Place(new Vector3(0f, 0f, 1.4f), Vector3.forward);
        for (int i = 0; i < 80; i++) Tick(null);

        // What the player asked for, in the world, is fixed by the camera, not by the
        // body -- pressing D means "right of the screen" however the body ends up.
        float camYaw = _vehicle.Cam != null ? _vehicle.Cam.transform.eulerAngles.y : 0f;
        Vector2 move = kL == FwdL ? Vector2.up
                     : kL == BackL ? Vector2.down
                     : kL == RightL ? Vector2.right
                     : Vector2.left;
        Vector3 want = Quaternion.Euler(0f, camYaw, 0f) * new Vector3(move.x, 0f, move.y);

        Vector3 start = _body.Pelvis.position;
        Vector3 tailFrom = start;

        // Where the feet sit, across the body, averaged over the tail only. If the
        // stance never re-orients after the body turns, the midpoint of the two feet
        // sits off to one side and the body is driven towards it the whole time.
        float sumL = 0f, sumR = 0f;
        int n = 0;

        for (int i = 0; i < 400; i++)
        {
            int ph = i % 20;
            Tick(ph < 3 ? kL : ph >= 10 && ph < 13 ? kR : null);
            if (i == 299) tailFrom = _body.Pelvis.position;

            if (i >= 300)
            {
                Vector3 across = Vector3.ProjectOnPlane(_body.Pelvis.transform.right, Vector3.up).normalized;
                sumL += Vector3.Dot(_body.LegL.StepTarget - _body.Pelvis.position, across);
                sumR += Vector3.Dot(_body.LegR.StepTarget - _body.Pelvis.position, across);
                n++;
            }
        }

        Vector3 all = _body.Pelvis.position - start;
        Vector3 tail = _body.Pelvis.position - tailFrom;
        all.y = tail.y = 0f;

        string allOff = all.magnitude < 0.05f ? "  --" : $"{Vector3.Angle(all, want),4:0}";
        string tailOff = tail.magnitude < 0.05f ? "  --" : $"{Vector3.Angle(tail, want),4:0}";

        // Three bearings: what was asked for, where the body ended up pointing, and
        // where it actually went. Whichever pair disagrees is where the fault is.
        float askB = Bear(want), faceB = Bear(_body.Heading), goB = Bear(tail);
        float bodyB = Bear(_body.Pelvis.transform.forward);

        return $"  {label}  whole run {all.magnitude,4:0.00} m at {allOff} deg off   |   " +
               $"last 2 s {tail.magnitude,4:0.00} m at {tailOff} deg off\n" +
               $"      asked {askB,4:0}   steering wants {faceB,4:0}   body actually points {bodyB,4:0}   " +
               $"travelling {goB,4:0}\n" +
               $"      steering vs ask {Mathf.DeltaAngle(askB, faceB),4:0}   " +
               $"body vs steering {Mathf.DeltaAngle(faceB, bodyB),4:0}   " +
               $"travel vs body {Mathf.DeltaAngle(bodyB, goB),4:0}\n" +
               $"      stance across the body: L {sumL / Mathf.Max(1, n) * 100f,5:0.0} cm  " +
               $"R {sumR / Mathf.Max(1, n) * 100f,5:0.0} cm  " +
               $"midpoint {(sumL + sumR) / (2f * Mathf.Max(1, n)) * 100f,5:0.0} cm";
    }

    static string Sideways(string label, System.Func<int, Key[]> input)
    {
        _body.Place(new Vector3(0f, 0f, 1.4f), Vector3.forward);
        for (int i = 0; i < 80; i++) Tick(null);

        Vector3 from = _body.Pelvis.position;
        Vector3 face = _body.Heading;
        Vector3 side = Vector3.Cross(Vector3.up, face).normalized;

        float sumL = 0f, sumR = 0f;
        int stepsL = 0, stepsR = 0, n = 0;

        for (int i = 0; i < 400; i++)
        {
            Tick(input(i));
            sumL += Vector3.Dot(_body.LegL.StepTarget - _body.Pelvis.position, side);
            sumR += Vector3.Dot(_body.LegR.StepTarget - _body.Pelvis.position, side);
            if (_body.LegL.JustStepped) stepsL++;
            if (_body.LegR.JustStepped) stepsR++;
            n++;
        }

        Vector3 went = _body.Pelvis.position - from;
        went.y = 0f;
        float along = Vector3.Dot(went, face);
        float across = Vector3.Dot(went, side);
        float mid = (sumL + sumR) / (2f * n);

        return $"  {label}\n" +
               $"     went {along,5:0.00} m along and {across,6:0.00} m across " +
               $"({(Mathf.Abs(along) < 0.05f ? 0f : Mathf.Abs(across / along) * 100f),3:0}% sideways)\n" +
               $"     feet sat L {sumL / n * 100f,5:0.0} cm  R {sumR / n * 100f,5:0.0} cm  " +
               $"midpoint {mid * 100f,5:0.0} cm\n" +
               $"     steps taken  L {stepsL}  R {stepsR}";
    }

    static void BoardRoles(int[] roles)
    {
        for (int i = 0; i < 4; i++)
        {
            var w = _coat.WearerAt((CoatRole)i);
            if (w != null) _coat.Leave(w);
        }
        for (int i = 0; i < 20; i++) Tick(null);

        _coat.DropAt(new Vector3(0f, 0.4f, 1.4f), Vector3.forward);
        for (int i = 0; i < 40; i++) Tick(null);

        foreach (int r in roles)
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

    /// How far under the floor should a standing foot be aimed? Swept rather than
    /// guessed: too little and the foot hovers and the body slides, too much and the
    /// leg is jammed into the ground and the walk should start to suffer.
    [MenuItem("Coat/Sweep Plant Depth")]
    public static void SweepFromMenu() => Debug.Log(Sweep());

    public static string Sweep()
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
        float was = _body.LegL.PlantDepth;

        try
        {
            log.AppendLine("full crew, 5 s of walking at each depth");
            foreach (float depth in new[] { 0f, 0.025f, 0.05f, 0.08f })
            {
                _body.LegL.PlantDepth = depth;
                _body.LegR.PlantDepth = depth;

                Board(4);
                log.AppendLine($"  depth {depth * 100f,4:0.0} cm   at rest: {AtRest()}");

                Vector3 from = _body.Pelvis.position;
                string grip = Grip(250);
                Vector3 went = _body.Pelvis.position - from;
                went.y = 0f;

                log.AppendLine($"  depth {depth * 100f,4:0.0} cm   walked {went.magnitude,4:0.00} m   " +
                               grip + $"   lean {Vector3.Angle(_body.Torso.transform.up, Vector3.up),3:0}");
            }
        }
        finally
        {
            _body.LegL.PlantDepth = was;
            _body.LegR.PlantDepth = was;
            Physics.simulationMode = mode;
            Send(null);
        }

        return log.ToString();
    }

    /// One player climbs out of the coat and straight back in, which happens all the
    /// time in play. Measure the grip before and after.
    [MenuItem("Coat/Check Rejoin")]
    public static void RejoinFromMenu() => Debug.Log(Rejoin());

    public static string Rejoin()
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
            log.AppendLine($"before anyone leaves   legs {_body.Legs}");
            log.AppendLine("   " + Grip(250));

            var c = _game.Characters[0];
            _coat.Leave(c);
            for (int i = 0; i < 80; i++) Tick(null);

            Vector3 at = _coat.Centre + Vector3.right * 0.6f;
            at.y = 0f;
            c.PlaceAt(at, _coat.Centre - at);
            for (int i = 0; i < 40; i++) Tick(null);

            bool back = _coat.TryEnter(c);
            for (int i = 0; i < 80; i++) Tick(null);

            log.AppendLine($"after the left leg climbs out and back in   " +
                           $"rejoined={back}  legs {_body.Legs}");
            log.AppendLine("   " + Grip(250));
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(null);
        }

        return log.ToString();
    }

    /// The state the body is in the instant it has finished standing up, before any
    /// walking. If it is already wrong here, the fault is in standing up, not walking.
    static string AtRest()
    {
        string s = "";
        foreach (var pair in new[] { (_body.LegL, "L"), (_body.LegR, "R") })
        {
            var leg = pair.Item1;
            float sole = leg.End.position.y - leg.End.transform.lossyScale.y * 0.5f;
            Vector3 ankle = leg.End.transform.TransformPoint(new Vector3(0f, 1f, -0.25f));
            s += $"{pair.Item2} {(leg.Planted ? "DOWN" : "up")} sole {sole,5:0.00} " +
                 $"aim {leg.StepTarget.y,5:0.00} off {Flat(ankle - leg.StepTarget).magnitude,5:0.00}  ";
        }
        return s + $"hips {_body.Pelvis.position.y,4:0.00}";
    }

    /// Walk forward and report how well the planted feet are gripping.
    static string Grip(int ticks)
    {
        float clearSum = 0f, clearMax = 0f, missSum = 0f;
        int planted = 0, touching = 0;
        float slipL = 0f, slipR = 0f, worst = 0f, dragL = 0f, dragR = 0f;

        Vector3 lastL = _body.LegL.End.position, lastR = _body.LegR.End.position;
        Vector3 aimL = _body.LegL.StepTarget, aimR = _body.LegR.StepTarget;
        bool wasL = _body.LegL.Planted, wasR = _body.LegR.Planted;

        for (int i = 0; i < ticks; i++)
        {
            int ph = i % 20;
            Tick(ph < 3 ? FwdL : ph >= 10 && ph < 13 ? FwdR : null);

            Measure(_body.LegL, ref lastL, ref aimL, ref wasL, ref slipL, ref dragL, ref worst);
            Measure(_body.LegR, ref lastR, ref aimR, ref wasR, ref slipR, ref dragR, ref worst);

            foreach (var leg in new[] { _body.LegL, _body.LegR })
            {
                if (!leg.Present || !leg.Planted) continue;
                planted++;
                float sole = leg.End.position.y - leg.End.transform.lossyScale.y * 0.5f;
                float clear = Mathf.Max(0f, sole - Ground(leg.End.position));
                clearSum += clear;
                clearMax = Mathf.Max(clearMax, clear);
                if (clear < 0.01f) touching++;
                Vector3 ankle = leg.End.transform.TransformPoint(new Vector3(0f, 1f, -0.25f));
                missSum += Flat(ankle - leg.StepTarget).magnitude;
            }
        }

        if (planted == 0) return "no planted ticks at all";
        return $"touching {touching * 100f / planted,3:0}%   clearance mean {clearSum / planted * 100f,5:0.0} cm " +
               $"worst {clearMax * 100f,5:0.0} cm   off spot {missSum / planted * 100f,5:0.0} cm   " +
               $"skate L {slipL * 100f,6:0.0} R {slipR * 100f,5:0.0} cm";
    }

    /// Tick by tick on one leg, because the averages say a planted foot is in the air
    /// and that could be either a leg that will not reach down or a leg that is
    /// swinging with the planted flag stuck on.
    [MenuItem("Coat/Trace One Leg")]
    public static void TraceFromMenu() => Debug.Log(Trace());

    public static string Trace()
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
            log.AppendLine($"full crew: legs {_body.Legs} arms {_body.Arms}");
            log.AppendLine("tick  LEFT plant air  soleY  aimY   offSpot | RIGHT plant air  soleY  offSpot | hips");

            for (int i = 0; i < 120; i++)
            {
                int ph = i % 20;
                Tick(ph < 3 ? FwdL : ph >= 10 && ph < 13 ? FwdR : null);
                if (i % 3 != 0) continue;

                log.AppendLine($"{i,4}  {One(_body.LegL)} | {One(_body.LegR)} | " +
                               $"{_body.Pelvis.position.y,4:0.00}");
            }
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(null);
        }

        return log.ToString();
    }

    static string One(ClassicLimb leg)
    {
        float sole = leg.End.position.y - leg.End.transform.lossyScale.y * 0.5f;
        Vector3 ankle = leg.End.transform.TransformPoint(new Vector3(0f, 1f, -0.25f));
        return $"{(leg.Planted ? "DOWN" : "up  ")} {(leg.Airborne ? "air" : "   ")} " +
               $"{sole,5:0.00} {leg.StepTarget.y,5:0.00} {Flat(ankle - leg.StepTarget).magnitude,5:0.00}";
    }

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
            log.AppendLine(Case(_crew == 4 ? "full crew" : "legs only", _crew));
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(null);
        }

        return log.ToString();
    }

    static string Case(string label, int crew)
    {
        Board(crew);
        var s = new StringBuilder();
        s.AppendLine("== " + label + $"   (legs {_body.Legs} arms {_body.Arms})");

        // ---- skating: how far does a planted foot travel? ----
        float slipL = 0f, slipR = 0f, worstSlipRate = 0f;
        float targetDragL = 0f, targetDragR = 0f;
        int bothDown = 0, ticks = 0;

        // Is a "planted" foot even touching the floor, and is it anywhere near the spot
        // the solver is holding for it?
        float clearSum = 0f, clearMax = 0f, missSum = 0f, missMax = 0f;
        int planted = 0, touching = 0;

        Vector3 lastFootL = _body.LegL.End.position, lastFootR = _body.LegR.End.position;
        Vector3 lastAimL = _body.LegL.StepTarget, lastAimR = _body.LegR.StepTarget;
        bool wasPlantedL = _body.LegL.Planted, wasPlantedR = _body.LegR.Planted;

        for (int i = 0; i < 250; i++)
        {
            int ph = i % 20;
            Tick(ph < 3 ? FwdL : ph >= 10 && ph < 13 ? FwdR : null);

            Measure(_body.LegL, ref lastFootL, ref lastAimL, ref wasPlantedL,
                    ref slipL, ref targetDragL, ref worstSlipRate);
            Measure(_body.LegR, ref lastFootR, ref lastAimR, ref wasPlantedR,
                    ref slipR, ref targetDragR, ref worstSlipRate);

            bool downL = !_body.LegL.Present || _body.LegL.Planted;
            bool downR = !_body.LegR.Present || _body.LegR.Planted;
            if (downL && downR) bothDown++;
            ticks++;

            foreach (var leg in new[] { _body.LegL, _body.LegR })
            {
                if (!leg.Present || !leg.Planted) continue;
                planted++;

                // Lowest point of the foot box, against the floor beneath it.
                float sole = leg.End.position.y - leg.End.transform.lossyScale.y * 0.5f;
                float clear = Mathf.Max(0f, sole - Ground(leg.End.position));
                clearSum += clear;
                clearMax = Mathf.Max(clearMax, clear);
                if (clear < 0.01f) touching++;

                Vector3 ankle = leg.End.transform.TransformPoint(new Vector3(0f, 1f, -0.25f));
                float miss = Flat(ankle - leg.StepTarget).magnitude;
                missSum += miss;
                missMax = Mathf.Max(missMax, miss);
            }
        }

        s.AppendLine($"   SKATING   planted foot travelled  left {slipL * 100f,5:0.0} cm   " +
                     $"right {slipR * 100f,5:0.0} cm   over 5 s of walking");
        s.AppendLine($"             its aim was dragged     left {targetDragL * 100f,5:0.0} cm   " +
                     $"right {targetDragR * 100f,5:0.0} cm   (this is the clamp pulling it in)");
        s.AppendLine($"             fastest skate {worstSlipRate,5:0.00} m/s");
        s.AppendLine($"             both feet down on {bothDown * 100f / ticks,3:0}% of ticks " +
                     $"-- stance friction only runs on these");

        if (planted > 0)
        {
            s.AppendLine($"   GRIP      a planted foot is actually touching the floor on " +
                         $"{touching * 100f / planted,3:0}% of planted ticks");
            s.AppendLine($"             ground clearance  mean {clearSum / planted * 100f,5:0.0} cm " +
                         $"  worst {clearMax * 100f,5:0.0} cm");
            s.AppendLine($"             off its own spot  mean {missSum / planted * 100f,5:0.0} cm " +
                         $"  worst {missMax * 100f,5:0.0} cm");
        }

        // ---- coasting: let go and see how far it carries ----
        Vector3 v0 = Flat(_body.Pelvis.linearVelocity);
        Vector3 from = _body.Pelvis.position;
        float stopped = -1f;
        float at05 = -1f, at10 = -1f;

        for (int i = 0; i < 200; i++)
        {
            Tick(null);
            float sp = Flat(_body.Pelvis.linearVelocity).magnitude;
            if (i == 24) at05 = sp;
            if (i == 49) at10 = sp;
            if (stopped < 0f && sp < 0.05f) stopped = (i + 1) * Dt;
        }

        Vector3 drift = _body.Pelvis.position - from;
        drift.y = 0f;

        s.AppendLine($"   COASTING  let go at {v0.magnitude,4:0.00} m/s   " +
                     $"after 0.5 s {at05,4:0.00}   after 1.0 s {at10,4:0.00}   " +
                     $"{(stopped < 0f ? "NEVER STOPPED in 4 s" : $"stopped after {stopped:0.00} s")}");
        s.AppendLine($"             carried on for {drift.magnitude * 100f,5:0.0} cm after the keys came up");

        return s.ToString();
    }

    /// A foot only counts as skating if it was planted on BOTH this tick and the last
    /// one; the jump as it lands is not slip.
    static void Measure(ClassicLimb leg, ref Vector3 lastFoot, ref Vector3 lastAim,
                        ref bool wasPlanted, ref float slip, ref float drag, ref float worst)
    {
        Vector3 foot = leg.End.position;
        Vector3 aim = leg.StepTarget;

        if (leg.Present && leg.Planted && wasPlanted)
        {
            float d = Flat(foot - lastFoot).magnitude;
            slip += d;
            drag += Flat(aim - lastAim).magnitude;
            worst = Mathf.Max(worst, d / Dt);
        }

        lastFoot = foot;
        lastAim = aim;
        wasPlanted = leg.Present && leg.Planted;
    }

    /// Which force is steering it sideways?
    ///
    /// Every term the balance drive puts on the hips, resolved into the body's own
    /// forward and right. Strafing, the body points the right way but travels about
    /// thirty degrees off it, and none of these terms has any business having a
    /// sideways component once the turn has finished.
    [MenuItem("Coat/Check What Pushes Sideways")]
    public static void LedgerFromMenu() => Debug.Log(Ledger());

    public static string Ledger()
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
            BoardRoles(new[] { 0, 1 });
            log.AppendLine("Averages over the settled last 2 s. fwd/side are the BODY's axes;");
            log.AppendLine("side is positive to its right. Units are m/s^2 on the hips.");
            log.AppendLine("");
            log.AppendLine(Terms("forward (W,I)", FwdL, FwdR));
            log.AppendLine(Terms("right   (D,L)", RightL, RightR));
            log.AppendLine(Terms("left    (A,J)", LeftL, LeftR));
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(null);
        }

        return log.ToString();
    }

    static string Terms(string label, Key[] kL, Key[] kR)
    {
        _body.Place(new Vector3(0f, 0f, 1.4f), Vector3.forward);
        for (int i = 0; i < 80; i++) Tick(null);

        Vector2 chase = Vector2.zero, brake = Vector2.zero, err = Vector2.zero, vel = Vector2.zero;
        Vector2 supp = Vector2.zero, footL = Vector2.zero, footR = Vector2.zero;
        int n = 0;

        for (int i = 0; i < 400; i++)
        {
            int ph = i % 20;
            Tick(ph < 3 ? kL : ph >= 10 && ph < 13 ? kR : null);
            if (i < 300) continue;

            // The transform's own axes. Rolling my own right vector and negating it
            // gave numbers that contradicted the stance measurement above -- a support
            // point 54 cm to one side when neither foot was further out than 30.
            Vector3 f = Vector3.ProjectOnPlane(_body.Pelvis.transform.forward, Vector3.up).normalized;
            Vector3 r = Vector3.ProjectOnPlane(_body.Pelvis.transform.right, Vector3.up).normalized;

            chase += new Vector2(Vector3.Dot(_body.LastChase, f), Vector3.Dot(_body.LastChase, r));
            brake += new Vector2(Vector3.Dot(_body.LastBrake, f), Vector3.Dot(_body.LastBrake, r));
            err += new Vector2(Vector3.Dot(_body.LastWantErr, f), Vector3.Dot(_body.LastWantErr, r));

            // Sanity: the support is a weighted average of the two foot targets, so it
            // can never be further out sideways than the further foot. Printed beside
            // them so a nonsense reading is obvious rather than believed.
            Vector3 s = _body.LastSupport - _body.Pelvis.position;
            s.y = 0f;
            supp += new Vector2(Vector3.Dot(s, f), Vector3.Dot(s, r));

            Vector3 tl = _body.LegL.StepTarget - _body.Pelvis.position;
            Vector3 tr = _body.LegR.StepTarget - _body.Pelvis.position;
            tl.y = tr.y = 0f;
            footL += new Vector2(Vector3.Dot(tl, f), Vector3.Dot(tl, r));
            footR += new Vector2(Vector3.Dot(tr, f), Vector3.Dot(tr, r));

            Vector3 v = Flat(_body.Pelvis.linearVelocity);
            vel += new Vector2(Vector3.Dot(v, f), Vector3.Dot(v, r));
            n++;
        }

        float k = 1f / Mathf.Max(1, n);
        return $"  {label}\n" +
               $"     stance spring  fwd {chase.x * k,7:0.0}  side {chase.y * k,7:0.0}\n" +
               $"     braking        fwd {brake.x * k,7:0.0}  side {brake.y * k,7:0.0}\n" +
               $"     support offset fwd {supp.x * k * 100f,7:0.0}  side {supp.y * k * 100f,7:0.0}  cm\n" +
               $"       left foot    fwd {footL.x * k * 100f,7:0.0}  side {footL.y * k * 100f,7:0.0}  cm\n" +
               $"       right foot   fwd {footR.x * k * 100f,7:0.0}  side {footR.y * k * 100f,7:0.0}  cm\n" +
               $"     stance error   fwd {err.x * k * 100f,7:0.0}  side {err.y * k * 100f,7:0.0}  cm\n" +
               $"     velocity       fwd {vel.x * k,7:0.00}  side {vel.y * k,7:0.00}  m/s";
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    static float Bear(Vector3 v)
    {
        Vector3 f = Flat(v);
        return f.sqrMagnitude < 1e-5f ? 0f : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
    }

    static float Ground(Vector3 p)
    {
        Vector3 from = new Vector3(p.x, p.y + 0.5f, p.z);
        if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 4f,
                            CoatLayers.NotRig, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return 0f;
    }

    /// Everyone out and a new crew in, by exactly the route a coat key takes.
    ///
    /// It used to vacate the coat and switch the body off by hand, and a leg that had
    /// been through that came back flagged planted while hanging a quarter of a metre
    /// off the floor -- so every case after the first measured that instead of what it
    /// was asked to. Leaving and entering through the coat's own calls does not do it;
    /// Check Rejoin confirms a player can climb out and back in with no ill effect.
    static void Board(int crew)
    {
        for (int i = 0; i < 4; i++)
        {
            var w = _coat.WearerAt((CoatRole)i);
            if (w != null) _coat.Leave(w);
        }
        for (int i = 0; i < 20; i++) Tick(null);

        // Put the heap back on the same patch of flat ground every time. The coat folds
        // wherever the body last walked to, and after five seconds of walking that is
        // up on the kerb -- so each successive case was quietly being run on a step
        // instead of on the floor, and I nearly reported that as a re-boarding bug.
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

        for (int i = 0; i < 80; i++) Tick(null);
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
