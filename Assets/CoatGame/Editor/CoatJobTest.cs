using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Coat;

/// Can four people actually carry something?
///
/// The interesting questions are not whether the code compiles but whether the arms
/// are strong enough to hold a tray at walking pace, whether one hand really does
/// make it drag, and whether falling over drops it. All three are measurements.
public static class CoatJobTest
{
    const float Dt = 1f / 50f;

    static CoatGame _game;
    static CoatVan _van;
    static CoatVehicle _vehicle;
    static CoatLoot _loot;

    // Legs walk with W/I. Arms aim with G (left) and Slash (right), and grab with
    // R (left) and Right Shift (right).
    static readonly Key[] FwdL = { Key.W }, FwdR = { Key.I };
    static readonly Key[] AimUp = { Key.T, Key.P };
    static readonly Key[] AimAndGrab = { Key.T, Key.P, Key.R, Key.RightShift };
    static readonly Key[] GrabBoth = { Key.R, Key.RightShift };
    static readonly Key[] WalkAndHold = { Key.W, Key.R, Key.RightShift };
    static readonly Key[] WalkAndHoldR = { Key.I, Key.R, Key.RightShift };
    static readonly Key[] BackAndHold = { Key.S, Key.R, Key.RightShift };
    static readonly Key[] BackAndHoldR = { Key.K, Key.R, Key.RightShift };

    [MenuItem("Coat/Test The Job")]
    public static void FromMenu() => Debug.Log(Run());

    /// Just the round, on a clean session. The full test restarts three times before
    /// it gets here, and the body walks worse after every restart -- a known fault
    /// that has nothing to do with the job, but which stops the round finishing and
    /// makes this look broken when it is not.
    [MenuItem("Coat/Test The Job - delivery only")]
    public static void DeliveryFromMenu() => Debug.Log(DeliveryOnly());

    public static string DeliveryOnly()
    {
        if (!EditorApplication.isPlaying) return "Needs play mode.";

        _game = Object.FindAnyObjectByType<CoatGame>(FindObjectsInactive.Include);
        if (_game == null || _game.Van == null || _game.Loot == null)
            return "No CoatGame with a van and a job.";
        _van = _game.Van;
        _vehicle = _game.Vehicle;
        _loot = _game.Loot;

        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        try { return Delivery(); }
        finally { Physics.simulationMode = mode; Send(null); }
    }

    public static string Run()
    {
        if (!EditorApplication.isPlaying) return "Needs play mode.";

        _game = Object.FindAnyObjectByType<CoatGame>(FindObjectsInactive.Include);
        if (_game == null || _game.Van == null || _game.Loot == null)
            return "No CoatGame with a van and a job.";
        _van = _game.Van;
        _vehicle = _game.Vehicle;
        _loot = _game.Loot;

        var log = new StringBuilder();
        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;

        try
        {
            Dress();
            log.AppendLine($"crew aboard: legs {_vehicle.Body.Legs} arms {_vehicle.Body.Arms}");
            log.AppendLine("");

            log.AppendLine(TwoHands());
            log.AppendLine(OneHand());
            log.AppendLine(FallOver());
            log.AppendLine(Delivery());
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(null);
        }

        return log.ToString();
    }

    // ---- can two hands pick it up and keep it? ------------------------------

    static string TwoHands()
    {
        AtTheTable();

        var s = new StringBuilder();
        s.AppendLine("TWO HANDS");

        for (int i = 0; i < 14; i++) Tick(AimUp);
        for (int i = 0; i < 30; i++) Tick(null);
        s.AppendLine($"   arms reaching: hands at y {_vehicle.Body.ArmL.End.position.y:0.00} " +
                     $"and {_vehicle.Body.ArmR.End.position.y:0.00}, tray at {_loot.Body.position.y:0.00}, " +
                     $"hand {Vector3.Distance(_vehicle.Body.ArmL.End.position, _loot.Body.position):0.00} m from it");

        int got = 0;
        for (int i = 0; i < 120; i++) { Tick(AimAndGrab); got = Mathf.Max(got, _loot.Hands); }
        s.AppendLine($"   after grabbing: {_loot.Hands} hands on it (best {got})");

        if (_loot.Hands == 0)
        {
            s.AppendLine("   *** COULD NOT PICK IT UP ***");
            return s.ToString();
        }

        // Walk AWAY from the table, not into it. The body is stood facing the table
        // to reach the tray, so "forward" is straight into the thing it just picked
        // up -- the first run of this managed 0.41 m and I blamed the arms.
        Vector3 from = _vehicle.Body.Pelvis.position;
        int lost = 0, ticks = 0;
        float worstTilt = 0f, lowest = 9f;

        for (int i = 0; i < 300; i++)
        {
            int ph = i % 20;
            Tick(ph < 3 ? BackAndHold : ph >= 10 && ph < 13 ? BackAndHoldR : GrabBoth);

            if (_loot.Hands < 2) lost++;
            worstTilt = Mathf.Max(worstTilt, Vector3.Angle(_loot.Body.transform.up, Vector3.up));
            lowest = Mathf.Min(lowest, _loot.Body.position.y);
            ticks++;
        }

        Vector3 went = _vehicle.Body.Pelvis.position - from;
        went.y = 0f;

        s.AppendLine($"   walked {went.magnitude:0.00} m carrying it");
        s.AppendLine($"   held by both hands on {(ticks - lost) * 100f / ticks:0}% of ticks, " +
                     $"{_loot.Hands} still on at the end");
        s.AppendLine($"   worst tilt {worstTilt:0} deg, lowest it got {lowest:0.00} " +
                     $"(table height was 0.78, floor is 0.03)");
        s.AppendLine($"   fumbles so far: {_loot.Fumbles}");
        return s.ToString();
    }

    // ---- does one hand really make it drag? --------------------------------

    static string OneHand()
    {
        AtTheTable();

        var s = new StringBuilder();
        s.AppendLine("ONE HAND, which should look wrong rather than be forbidden");

        for (int i = 0; i < 14; i++) Tick(AimUp);
        for (int i = 0; i < 30; i++) Tick(null);
        for (int i = 0; i < 120; i++) Tick(new[] { Key.R });
        s.AppendLine($"   hands on it: {_loot.Hands}");

        if (_loot.Hands == 0) { s.AppendLine("   (could not get hold of it one handed)"); return s.ToString(); }

        float worstTilt = 0f, lowest = 9f;
        for (int i = 0; i < 250; i++)
        {
            int ph = i % 20;
            Tick(ph < 3 ? new[] { Key.S, Key.R } : ph >= 10 && ph < 13 ? new[] { Key.K, Key.R } : new[] { Key.R });
            worstTilt = Mathf.Max(worstTilt, Vector3.Angle(_loot.Body.transform.up, Vector3.up));
            lowest = Mathf.Min(lowest, _loot.Body.position.y);
        }

        s.AppendLine($"   worst tilt {worstTilt:0} deg, dropped to y {lowest:0.00}");
        s.AppendLine($"   {(worstTilt > 35f || lowest < 0.35f ? "it drags, as intended" : "it stays level -- one hand is too easy")}");
        return s.ToString();
    }

    // ---- going over should drop it -----------------------------------------

    static string FallOver()
    {
        AtTheTable();
        for (int i = 0; i < 14; i++) Tick(AimUp);
        for (int i = 0; i < 30; i++) Tick(null);
        for (int i = 0; i < 120; i++) Tick(AimAndGrab);

        int before = _loot.Hands;
        int fumblesBefore = _loot.Fumbles;

        // Let go of the grab keys as well, or the arms simply pick it straight back
        // up off the floor and the drop never shows.
        _vehicle.Body.Collapse();
        for (int i = 0; i < 80; i++) Tick(null);

        return $"FALLING OVER\n" +
               $"   had {before} hands on it, collapsed, now {_loot.Hands}\n" +
               $"   fumbles {fumblesBefore} -> {_loot.Fumbles}   " +
               $"{(before >= 2 && _loot.Hands == 0 ? "dropped it, as it should" : "*** KEPT HOLD OF IT ***")}";
    }

    // ---- does the round refuse to end without it? --------------------------

    static string Delivery()
    {
        var s = new StringBuilder();
        s.AppendLine("DELIVERY");

        _van.Restart();
        for (int i = 0; i < 60; i++) Tick(null);
        Key[] keys = { Key.Q, Key.O, Key.B, Key.M };
        for (int r = 0; r < 4; r++)
        {
            for (int k = 0; k < 6; k++) Tick(new[] { keys[r] });
            for (int k = 0; k < 30; k++) Tick(null);
        }
        while (_van.Now == CoatVan.Phase.Opening) Tick(null);

        s.AppendLine($"   standing at z {_vehicle.Body.Pelvis.position.z:0.00}, " +
                     $"facing {_vehicle.Body.Heading.ToString("F2")}, " +
                     $"worn {_vehicle.Worn}, legs {_vehicle.Body.Legs}, " +
                     $"shutter {_van.DoorOpenness:0.00}");

        // Out into the street, so leaving is on the record. Given plenty of time on
        // purpose: this runs after three restarts and the body walks noticeably
        // slower each time, which is its own problem and not the one being tested.
        for (int i = 0; i < 700 && !_van.HasLeft; i++)
        {
            int ph = i % 20;
            Tick(ph < 3 ? FwdL : ph >= 10 && ph < 13 ? FwdR : null);
            if (i % 100 == 0)
                s.AppendLine($"      t+{i * Dt,4:0.0}s  z {_vehicle.Body.Pelvis.position.z,6:0.00}  " +
                             $"home {_van.AtHome}  left {_van.HasLeft}");
        }
        s.AppendLine($"   out in the street: phase {_van.Now}, got clear: {_van.HasLeft}, " +
                     $"{_van.AtHome} of 4 home");

        // Walk home WITHOUT it and make sure the van will not let them finish.
        for (int i = 0; i < 700 && _van.Now == CoatVan.Phase.Away; i++)
        {
            int ph = i % 20;
            Tick(ph < 3 ? new[] { Key.S } : ph >= 10 && ph < 13 ? new[] { Key.K } : null);
        }
        s.AppendLine($"   crew back without it: {_van.AtHome} of 4 home, phase {_van.Now}  " +
                     $"{(_van.Now == CoatVan.Phase.Away ? "round correctly still running" : "*** ENDED WITHOUT THE JOB ***")}");

        // Now put it in the van and the round should close. Into a back corner, NOT
        // the middle -- the disguise is standing on the middle, and dropping a tray
        // inside a ragdoll fires it across the level on depenetration. The first run
        // of this launched it to (-8, 1.8, 16).
        Vector3 corner = _van.transform.TransformPoint(new Vector3(0.75f, 0.25f, -1.2f));
        _loot.Body.transform.SetPositionAndRotation(corner, Quaternion.identity);
        _loot.Body.position = corner;
        _loot.Body.rotation = Quaternion.identity;
        _loot.Body.linearVelocity = Vector3.zero;
        _loot.Body.angularVelocity = Vector3.zero;
        for (int i = 0; i < 60; i++) Tick(null);

        s.AppendLine($"   with it in the van: home {_loot.Home}, delivered {_loot.Delivered}, " +
                     $"tray at {_loot.Body.position.ToString("F2")}");
        s.AppendLine($"   van says: {_van.AtHome} of 4 home, got clear earlier: {_van.HasLeft}, " +
                     $"phase {_van.Now}");
        for (int i = 0; i < 4; i++)
            s.AppendLine($"      role {i} home: {_van.IsHome(i)}");
        s.AppendLine($"   {(_van.Now == CoatVan.Phase.Back ? "round complete" : "*** STILL WILL NOT END ***")}");
        return s.ToString();
    }

    // ---- plumbing -----------------------------------------------------------

    /// Everyone in the coat, standing in the van.
    static void Dress()
    {
        _van.Restart();
        for (int i = 0; i < 60; i++) Tick(null);

        Key[] keys = { Key.Q, Key.O, Key.B, Key.M };
        for (int r = 0; r < 4; r++)
        {
            for (int k = 0; k < 6; k++) Tick(new[] { keys[r] });
            for (int k = 0; k < 30; k++) Tick(null);
        }
        while (_van.Now == CoatVan.Phase.Opening) Tick(null);
    }

    /// Stand the disguise in front of the table, facing it, with the tray back on top.
    /// Walking there is a separate problem from picking it up.
    static void AtTheTable()
    {
        _loot.Reset();
        Vector3 table = _loot.Started;
        _vehicle.Body.Place(new Vector3(table.x, 0f, table.z - 0.50f), Vector3.forward);
        for (int i = 0; i < 60; i++) Tick(null);
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

        var body = _vehicle.Body;
        if (body.gameObject.activeInHierarchy) { body.Input.Sample(); body.Tick(Dt); }

        _game.Tick(Dt);
        Physics.Simulate(Dt);
        if (_vehicle.Cam != null) _vehicle.Cam.Step(Dt);
    }
}
