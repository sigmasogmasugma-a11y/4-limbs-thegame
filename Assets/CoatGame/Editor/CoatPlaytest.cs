using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Coat;
using Coat.Classic;

/// Drives the game the way a player does — real key events through the real bindings —
/// and steps physics by hand so every run measures the same thing. Menu: Coat > Playtest.
///
/// Input goes in as keyboard events rather than straight into LocalCoatInput.States,
/// because writing the states directly tests everything except whether the bindings are
/// right, which is exactly where the bugs have been.
public static class CoatPlaytest
{
    const float Dt = 1f / 50f;

    static readonly Key[] CoatKeys = { Key.Q, Key.O, Key.B, Key.M };
    static readonly Key[] Forward = { Key.W, Key.I, Key.T, Key.P };
    static readonly string[] RoleName = { "left leg", "right leg", "left arm", "right arm" };

    static CoatGame _game;
    static CoatVehicle _vehicle;
    static TheCoat _coat;
    static ClassicRagdoll _body;

    [MenuItem("Coat/Playtest")]
    public static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (!EditorApplication.isPlaying) return "Playtest needs play mode.";

        _game = Object.FindAnyObjectByType<CoatGame>(FindObjectsInactive.Include);
        if (_game == null || _game.Vehicle == null) return "No CoatGame with a vehicle in the scene.";

        _vehicle = _game.Vehicle;
        _coat = _game.Coat;
        _body = _vehicle.Body;

        var log = new StringBuilder();
        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;

        try
        {
            Reset();
            log.AppendLine($"START            worn={_vehicle.Worn} aboard={_coat.Count} bodyUp={_body.gameObject.activeInHierarchy}");

            LooseWalk(log);

            Crew(log, "RIGHT LEG ONLY", 1);
            Crew(log, "BOTH LEGS", 0, 1);
            Crew(log, "BOTH LEGS + BOTH ARMS", 0, 1, 2, 3);
            Crew(log, "ARMS ONLY", 2, 3);
            Crew(log, "LEFT ARM ONLY", 2);

            log.AppendLine("CLUMSY PLAY      same sloppy input, both keys mashed together");
            Clumsy(log, "both legs, no arms", 0, 1);
            Clumsy(log, "both legs + arms  ", 0, 1, 2, 3);

            JoinMidShift(log);
            LeaveOneByOne(log);
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(Key.None);
        }

        return log.ToString();
    }

    /// Confirms the ShowCoat tickbox hides both coats and changes nothing else.
    [MenuItem("Coat/Check Coat Toggle")]
    public static void CheckCoatToggleFromMenu() => Debug.Log(CheckCoatToggle());

    public static string CheckCoatToggle()
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
        bool was = _vehicle.ShowCoat;

        try
        {
            Reset();
            log.AppendLine("HEAP ON FLOOR");
            log.AppendLine("  shown   " + Drawn());
            _vehicle.ShowCoat = false;
            for (int i = 0; i < 4; i++) Tick();
            log.AppendLine("  hidden  " + Drawn());
            _vehicle.ShowCoat = true;
            for (int i = 0; i < 4; i++) Tick();
            log.AppendLine("  shown   " + Drawn());

            Gather();
            for (int i = 0; i < 4; i++) Board(i);
            for (int i = 0; i < 140; i++) Tick();
            log.AppendLine("WORN");
            log.AppendLine("  shown   " + Drawn());

            _vehicle.ShowCoat = false;
            for (int i = 0; i < 4; i++) Tick();
            log.AppendLine("  hidden  " + Drawn());

            Vector3 from = _body.Pelvis.position;
            int steps = 0;
            float low = 1f;
            for (int i = 0; i < 400; i++)
            {
                int ph = i % 20;
                Tick(ph < 3 ? Key.W : ph >= 10 && ph < 13 ? Key.I : Key.None);
                if (_body.LegL.JustStepped || _body.LegR.JustStepped) steps++;
                low = Mathf.Min(low, _body.Stability);
            }
            log.AppendLine($"  walk with it hidden: {(_body.Pelvis.position - from).z:0.00}m, {steps} steps, lowest balance {low:0.00}");

            var obs = Object.FindAnyObjectByType<ClassicObserver>();
            log.AppendLine($"  observer still watching: canSee={obs != null && obs.CanSee} tell={(obs != null ? obs.Tell : "-")}");

            _vehicle.ShowCoat = true;
            for (int i = 0; i < 4; i++) Tick();
            log.AppendLine("  shown   " + Drawn());
        }
        finally
        {
            _vehicle.ShowCoat = was;
            Physics.simulationMode = mode;
            Send(Key.None);
        }

        return log.ToString();
    }

    static string Drawn()
    {
        var heap = _coat.GetComponentInChildren<CoatCloth>(true);
        var worn = _body.GetComponentInChildren<ClassicCloth>(true);
        return $"showCoat={_vehicle.ShowCoat}  heapClothDrawn={heap != null && heap.GetComponent<Renderer>().enabled}  " +
               $"bodyClothDrawn={worn != null && worn.GetComponent<Renderer>().enabled}  worn={_vehicle.Worn}";
    }

    // ---- scenarios ----------------------------------------------------------

    static void LooseWalk(StringBuilder log)
    {
        var p = _game.Characters[0];
        Vector3 a = p.Body.position;
        for (int i = 0; i < 200; i++) Tick(Key.W);
        log.AppendLine($"LOOSE WALK 4s    left leg moved {Flat(p.Body.position - a):0.00}m, upright {p.Stability:0.00}");
    }

    /// Put exactly these roles in the coat, then try to walk with whatever turned up.
    static void Crew(StringBuilder log, string title, params int[] roles)
    {
        Reset();
        Gather();
        foreach (int r in roles) Board(r);

        for (int i = 0; i < 140; i++) Tick();

        if (!_vehicle.Worn)
        {
            log.AppendLine($"{title}: body never stood up (aboard={_coat.Count})");
            return;
        }

        log.AppendLine($"{title}");
        log.AppendLine($"  shape          legs={_body.Legs} arms={_body.Arms} canWalk={_body.CanWalk}  " +
                       $"pelvis {_body.Pelvis.position.y:0.00}  head {_body.Head.position.y:0.00}  " +
                       $"balance {_body.Stability:0.00}");
        log.AppendLine($"  bones up       {BoneReport()}");

        Vector3 from = _body.Pelvis.position;
        int steps = 0, falls = 0;
        float low = 1f;

        for (int i = 0; i < 500; i++)
        {
            int ph = i % 20;
            Key k = ph < 3 ? Key.W : ph >= 10 && ph < 13 ? Key.I : Key.None;
            Tick(k);
            if (_body.LegL.JustStepped || _body.LegR.JustStepped) steps++;
            if (_body.Collapsed) falls++;
            low = Mathf.Min(low, _body.Stability);
        }

        Vector3 moved = _body.Pelvis.position - from;
        log.AppendLine($"  walk 10s       travelled {Flat(moved):0.00}m  steps {steps}  " +
                       $"lowest balance {low:0.00}  ticks collapsed {falls}");
    }

    /// The same deliberately bad input for every crew, so the collapse counts can be
    /// compared. Both leg keys mashed on a rhythm that does not line up with the step
    /// timing, which is what a real pair of players arguing actually produces.
    static void Clumsy(StringBuilder log, string title, params int[] roles)
    {
        Reset();
        Gather();
        foreach (int r in roles) Board(r);
        for (int i = 0; i < 140; i++) Tick();

        Vector3 from = _body.Pelvis.position;
        int falls = 0, collapses = 0;
        bool wasDown = false;
        float low = 1f;

        // Left leg drives forward, right leg drives backward: the two players actively
        // fighting each other. Mashing both keys the SAME way is not clumsy at all, it
        // just walks faster, which is why the first version of this test proved nothing.
        for (int i = 0; i < 900; i++)
        {
            int ph = i % 13;
            if (ph < 3) Tick(Key.W);
            else if (ph < 6) Tick(Key.K);
            else if (ph < 8) { Send(Key.W); Step(); Send(Key.K); Step(); }
            else Tick();

            if (_body.Collapsed) falls++;
            if (_body.Collapsed && !wasDown) collapses++;
            wasDown = _body.Collapsed;
            low = Mathf.Min(low, _body.Stability);
        }

        log.AppendLine($"  {title}  went over {collapses} times ({falls} ticks down), " +
                       $"travelled {Flat(_body.Pelvis.position - from):0.00}m, lowest balance {low:0.00}");
    }

    /// Start on one leg, then let the other three climb in while it is already standing.
    static void JoinMidShift(StringBuilder log)
    {
        Reset();
        Gather();
        Board(1);
        for (int i = 0; i < 120; i++) Tick();
        log.AppendLine($"JOIN MID SHIFT   start legs={_body.Legs} arms={_body.Arms} balance {_body.Stability:0.00}");

        foreach (int r in new[] { 0, 2, 3 })
        {
            Approach(r);
            Board(r);
            for (int i = 0; i < 120; i++) Tick();
            log.AppendLine($"  +{RoleName[r],-10} legs={_body.Legs} arms={_body.Arms} " +
                           $"pelvis {_body.Pelvis.position.y:0.00} balance {_body.Stability:0.00} " +
                           $"bones {BoneReport()}");
        }

        Vector3 from = _body.Pelvis.position;
        int steps = 0;
        for (int i = 0; i < 400; i++)
        {
            int ph = i % 20;
            Tick(ph < 3 ? Key.W : ph >= 10 && ph < 13 ? Key.I : Key.None);
            if (_body.LegL.JustStepped || _body.LegR.JustStepped) steps++;
        }
        log.AppendLine($"  full crew walk travelled {Flat(_body.Pelvis.position - from):0.00}m  steps {steps}");
    }

    static void LeaveOneByOne(StringBuilder log)
    {
        log.AppendLine("LEAVE ONE BY ONE");
        foreach (int r in new[] { 3, 2, 0, 1 })
        {
            Bail(r);
            for (int i = 0; i < 90; i++) Tick();

            var who = _game.Characters[r];
            log.AppendLine($"  -{RoleName[r],-10} aboard={_coat.Count} worn={_vehicle.Worn} " +
                           $"legs={(_vehicle.Worn ? _body.Legs : 0)} " +
                           $"theyAreUp={who.gameObject.activeInHierarchy} " +
                           $"theyAreStanding={who.gameObject.activeInHierarchy && !who.Collapsed && who.Head.position.y > 0.70f}");
        }
        log.AppendLine($"  heap back      heapUp={_coat.gameObject.activeInHierarchy} " +
                       $"bodyUp={_body.gameObject.activeInHierarchy} heapY={_coat.Centre.y:0.00}");
    }

    // ---- plumbing -----------------------------------------------------------

    static string BoneReport() =>
        $"L.leg {On(_body.LegL)} R.leg {On(_body.LegR)} L.arm {On(_body.ArmL)} R.arm {On(_body.ArmR)}";

    static string On(ClassicLimb limb) =>
        limb.Present == limb.Upper.gameObject.activeInHierarchy
            ? (limb.Present ? "on" : "off")
            : "DESYNC";

    static void Reset()
    {
        for (int i = 0; i < 4; i++) Bail(i);
        for (int i = 0; i < 120; i++) Tick();

        _coat.Vacate();
        for (int i = 0; i < 4; i++) Tick();

        if (_body.gameObject.activeInHierarchy) _body.gameObject.SetActive(false);
        _coat.DropAt(new Vector3(0f, 0.4f, 1.4f), Vector3.forward);

        float[] x = { -1.5f, -0.5f, 0.5f, 1.5f };
        for (int i = 0; i < 4; i++)
        {
            if (!_game.Characters[i].gameObject.activeInHierarchy)
                _game.Characters[i].gameObject.SetActive(true);
            _game.Characters[i].PlaceAt(new Vector3(x[i], 0f, -0.6f), Vector3.forward);
        }
        for (int i = 0; i < 120; i++) Tick();
    }

    /// Stand everyone within reach of the coat.
    static void Gather()
    {
        for (int i = 0; i < 4; i++) Approach(i);
        for (int i = 0; i < 80; i++) Tick();
    }

    static void Approach(int role)
    {
        var c = _game.Characters[role];
        if (c.InCoat) return;
        if (!c.gameObject.activeInHierarchy) c.gameObject.SetActive(true);

        Vector3 centre = _coat.Centre;
        float a = role / 4f * Mathf.PI * 2f;
        Vector3 at = centre + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.8f;
        at.y = 0f;
        c.PlaceAt(at, centre - at);
        for (int i = 0; i < 30; i++) Tick();
    }

    static void Board(int role)
    {
        for (int k = 0; k < 6; k++) Tick(CoatKeys[role]);
        for (int k = 0; k < 40; k++) Tick();
    }

    static void Bail(int role)
    {
        if (_coat.WearerAt((CoatRole)role) == null) return;
        for (int k = 0; k < 6; k++) Tick(CoatKeys[role]);
        for (int k = 0; k < 20; k++) Tick();
    }

    static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    static void Send(Key key)
    {
        var state = new KeyboardState();
        if (key != Key.None) state.Set(key, true);
        InputSystem.QueueStateEvent(Keyboard.current, state);
        InputSystem.Update();
    }

    static void Tick(Key key = Key.None)
    {
        Send(key);
        Step();
    }

    /// One simulation step against whatever keys were last sent.
    static void Step()
    {
        _game.Input.Sample();

        bool worn = _body.gameObject.activeInHierarchy;
        if (worn) { _body.Input.Sample(); _body.Tick(Dt); }

        _game.Tick(Dt);
        Physics.Simulate(Dt);

        if (!worn) return;
        var cloth = _body.GetComponentInChildren<ClassicCloth>();
        if (cloth != null) cloth.Rebuild(Dt);
    }
}
