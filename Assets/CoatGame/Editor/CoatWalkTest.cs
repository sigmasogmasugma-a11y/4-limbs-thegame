using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Coat;
using Coat.Classic;

/// Walks the disguise in every direction and reports its posture in each. The earlier
/// checks only ever drove it forwards and backwards, which is why sideways and diagonal
/// walking went unnoticed for so long.
///
/// Both leg players press the same direction, alternating, the way a real pair would.
public static class CoatWalkTest
{
    const float Dt = 1f / 50f;

    static CoatGame _game;
    static CoatVehicle _vehicle;
    static TheCoat _coat;
    static ClassicRagdoll _body;

    struct Way
    {
        public string Name;
        public Key[] Left;      // left leg player's keys
        public Key[] Right;     // right leg player's keys
        public Vector3 World;   // where it should end up going
    }

    static readonly Way[] Ways =
    {
        new Way { Name = "forward",    Left = new[]{Key.W},          Right = new[]{Key.I},          World = new Vector3( 0, 0,  1) },
        new Way { Name = "backward",   Left = new[]{Key.S},          Right = new[]{Key.K},          World = new Vector3( 0, 0, -1) },
        new Way { Name = "left",       Left = new[]{Key.A},          Right = new[]{Key.J},          World = new Vector3(-1, 0,  0) },
        new Way { Name = "right",      Left = new[]{Key.D},          Right = new[]{Key.L},          World = new Vector3( 1, 0,  0) },
        new Way { Name = "fwd-left",   Left = new[]{Key.W, Key.A},   Right = new[]{Key.I, Key.J},   World = new Vector3(-1, 0,  1) },
        new Way { Name = "fwd-right",  Left = new[]{Key.W, Key.D},   Right = new[]{Key.I, Key.L},   World = new Vector3( 1, 0,  1) },
        new Way { Name = "back-left",  Left = new[]{Key.S, Key.A},   Right = new[]{Key.K, Key.J},   World = new Vector3(-1, 0, -1) },
        new Way { Name = "back-right", Left = new[]{Key.S, Key.D},   Right = new[]{Key.K, Key.L},   World = new Vector3( 1, 0, -1) },
    };

    enum Crew { All, LegsOnly }
    enum Style { Taking, OneSided, Mashing }

    [MenuItem("Coat/Walk Test All Directions")]
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
            log.AppendLine("FULL CREW, clean alternating rhythm, flat ground");
            log.AppendLine("direction    travelled  mean t/p  peak t/p   legStretch  peakAccel  offCourse  falls");
            foreach (var way in Ways) log.AppendLine(Walk(way, Crew.All, Style.Taking, false));

            log.AppendLine("");
            log.AppendLine("LEGS ONLY, no arms to balance with");
            foreach (var way in Ways) log.AppendLine(Walk(way, Crew.LegsOnly, Style.Taking, false));

            log.AppendLine("");
            log.AppendLine("ONE LEG PRESSING, the other player asleep");
            foreach (var way in Ways) log.AppendLine(Walk(way, Crew.All, Style.OneSided, false));

            log.AppendLine("");
            log.AppendLine("MASHING, no rhythm at all");
            foreach (var way in Ways) log.AppendLine(Walk(way, Crew.All, Style.Mashing, false));

            log.AppendLine("");
            log.AppendLine("WALKING INTO THE KERB (0.14 m step up)");
            foreach (var way in Ways) log.AppendLine(Walk(way, Crew.All, Style.Taking, true));
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(null);
        }

        return log.ToString();
    }

    /// Which way does it actually turn? Signed: negative is left, positive is right.
    [MenuItem("Coat/Check Turn Direction")]
    public static void TurnDirFromMenu() => Debug.Log(TurnDirection());

    public static string TurnDirection()
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
            float[] orbits = { 0f, 90f, 180f };
            foreach (float orbit in orbits)
            foreach (var way in new[] { Ways[2] })            // just LEFT, at each orbit
            {
                Board(Crew.LegsOnly);
                if (_vehicle.Cam != null) _vehicle.Cam.Orbit = orbit;
                log.AppendLine($"--- camera orbit {orbit:0} deg ---");
                for (int i = 0; i < 150; i++) Tick(null);

                float startHeading = Compass(_body.Heading);
                float startBody = Compass(_body.Pelvis.transform.forward);
                log.AppendLine($"pressing {way.Name.ToUpper()}  (negative = turning left, positive = turning right)");
                log.AppendLine($"  start        heading {startHeading,6:0} deg   body {startBody,6:0} deg");

                for (int block = 0; block < 6; block++)
                {
                    for (int i = 0; i < 50; i++)
                    {
                        int ph = i % 20;
                        Tick(ph < 3 ? way.Left : ph >= 10 && ph < 13 ? way.Right : null);
                    }
                    log.AppendLine($"  after {(block + 1) * 1.0f:0.0}s   heading {Delta(startHeading, Compass(_body.Heading)),6:0} deg   " +
                                   $"body {Delta(startBody, Compass(_body.Pelvis.transform.forward)),6:0} deg   " +
                                   $"cameraYaw {Camera.main.transform.eulerAngles.y,6:0} deg   " +
                                   $"stepDir L {Flat(_body.LegL.LastStepDir)}  R {Flat(_body.LegR.LastStepDir)}");
                }
                log.AppendLine("");
            }
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(null);
        }

        return log.ToString();
    }

    /// Compass bearing: 0 is +z, +90 is +x (right), -90 is -x (left).
    static float Compass(Vector3 v)
    {
        Vector3 f = Vector3.ProjectOnPlane(v, Vector3.up);
        return f.sqrMagnitude < 1e-5f ? 0f : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
    }

    static float Delta(float from, float to)
    {
        float d = to - from;
        while (d > 180f) d -= 360f;
        while (d < -180f) d += 360f;
        return d;
    }

    static string Flat(Vector3 v) => $"({v.x:0.0},{v.z:0.0})";

    static string Walk(Way way, Crew crew, Style style, bool atKerb)
    {
        Board(crew);

        if (atKerb) PlaceNearKerb(way);
        for (int i = 0; i < 150; i++) Tick(null);

        Vector3 from = _body.Pelvis.position;
        Vector3 want = way.World.normalized;

        float worstTorso = 0f, worstPelvis = 0f, worstStretch = 0f, peakAccel = 0f;
        // Peaks alone are one bad tick out of three hundred, and chasing them sent me
        // after ghosts more than once. The typical value is what the player sees.
        float sumTorso = 0f, sumPelvis = 0f;
        int falls = 0;
        Vector3 lastV = _body.Pelvis.linearVelocity;

        for (int i = 0; i < 300; i++)
        {
            int ph = i % 20;
            Key[] keys;
            switch (style)
            {
                case Style.OneSided:
                    keys = ph < 3 ? way.Left : null;
                    break;
                case Style.Mashing:
                    // No rhythm: both players jabbing at once, on a beat that does not
                    // line up with the step timing.
                    keys = (i % 7) < 3 ? Both(way) : (i % 11) < 2 ? way.Right : null;
                    break;
                default:
                    keys = ph < 3 ? way.Left : ph >= 10 && ph < 13 ? way.Right : null;
                    break;
            }
            Tick(keys);

            float lt = Lean(_body.Torso), lp = Lean(_body.Pelvis);
            worstTorso = Mathf.Max(worstTorso, lt);
            worstPelvis = Mathf.Max(worstPelvis, lp);
            sumTorso += lt;
            sumPelvis += lp;
            worstStretch = Mathf.Max(worstStretch, Mathf.Max(Stretch(_body.LegL), Stretch(_body.LegR)));

            Vector3 v = _body.Pelvis.linearVelocity;
            Vector3 dv = v - lastV; dv.y = 0f;
            peakAccel = Mathf.Max(peakAccel, dv.magnitude / Dt);
            lastV = v;

            if (_body.Collapsed) falls++;
        }

        Vector3 moved = _body.Pelvis.position - from;
        moved.y = 0f;
        float along = Vector3.Dot(moved, want);
        float offCourse = moved.magnitude < 0.05f ? 0f : Vector3.Angle(moved, want);

        return $"{way.Name,-11}  {along,6:0.00} m   " +
               $"{sumTorso / 300f,3:0}/{sumPelvis / 300f,-3:0}  {worstTorso,3:0}/{worstPelvis,-3:0}    " +
               $"{worstStretch * 100f,3:0} %    {peakAccel / 9.81f,4:0.0} g   {offCourse,3:0} deg   {falls}";
    }

    static Key[] Both(Way way)
    {
        var all = new Key[way.Left.Length + way.Right.Length];
        way.Left.CopyTo(all, 0);
        way.Right.CopyTo(all, way.Left.Length);
        return all;
    }

    /// Stand the body just short of the kerb, facing it, so the walk runs into it.
    static void PlaceNearKerb(Way way)
    {
        Vector3 dir = way.World.normalized;
        // The kerb runs along x at z = 5.5, 0.14 high. Start 1.2 m short of it on the
        // side the walk is coming from.
        Vector3 spot = new Vector3(0f, 0f, 5.5f) - dir * 1.2f;
        spot.y = 0f;
        _body.Place(spot, dir);
        _coat.RideOn(_body.Pelvis, _vehicle.HubHeight);
    }

    static float Lean(Rigidbody rb) => Vector3.Angle(rb.transform.up, Vector3.up);

    /// How much of the leg the IK is actually asking for, right now.
    ///
    /// This used to measure StepTarget, which mid-step is the far end of the stride --
    /// a place the foot is deliberately not at yet and the hip has not arrived at
    /// either. It read over 100% on a perfectly healthy walk and meant nothing. Target
    /// is the live goal the solver chases, and a planted leg is the one that has to
    /// hold the body up.
    static float Stretch(ClassicLimb leg)
    {
        if (!leg.Present || !leg.Planted) return 0f;
        Vector3 hip = leg.Root.transform.TransformPoint(leg.RootAnchorLocal);
        return Vector3.Distance(hip, leg.Target) / (leg.UpperLen + leg.LowerLen);
    }

    // ---- plumbing -----------------------------------------------------------

    /// Everyone out, coat back on the floor, crew aboard again, from scratch.
    static void Board(Crew crew)
    {
        _coat.Vacate();
        for (int i = 0; i < 4; i++) Tick(null);
        if (_body.gameObject.activeInHierarchy) _body.gameObject.SetActive(false);
        _coat.DropAt(new Vector3(0f, 0.4f, 1.4f), Vector3.forward);
        for (int i = 0; i < 40; i++) Tick(null);

        Key[] coatKeys = { Key.Q, Key.O, Key.B, Key.M };
        int seats = crew == Crew.LegsOnly ? 2 : 4;
        for (int r = 0; r < seats; r++)
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

        // Drive the camera too. The controls are camera relative, so leaving it frozen
        // would test a game nobody plays.
        if (_vehicle.Cam != null) _vehicle.Cam.Step(Dt);
    }
}
