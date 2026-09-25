using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Coat;
using Coat.Classic;

/// Does a real round, played end to end, actually pay out or refuse to?
///
/// CoatRoundResultCheck proves the payout MATH. This proves the WIRING: that
/// CoatVan reads the observers at the right moment, that it reads EverRumbled
/// and not the self-clearing Rumbled, that a bust IN DISGUISE counts (the worn
/// observer, not the loose one), and that a restart cannot leak a bust into
/// the next attempt.
///
/// The body is PLACED out of the van and back rather than walked. Walking out
/// was tried first: the body failed to get clear in 700 ticks after a restart
/// (a known walking fault CoatJobTest also notes), the round never closed, and
/// every outcome read "none" -- a failure that had nothing to do with outcomes.
public static class CoatRoundPlaytest
{
    const float Dt = 1f / 50f;

    static CoatGame _game;
    static CoatVan _van;
    static CoatVehicle _vehicle;
    static CoatLoot _loot;
    static CoatObserver _loose;
    static ClassicObserver _worn;

    [MenuItem("Coat/Test Round Result (play mode)")]
    public static void FromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (!EditorApplication.isPlaying) return "Needs play mode.";

        _game = Object.FindAnyObjectByType<CoatGame>(FindObjectsInactive.Include);
        if (_game == null || _game.Van == null || _game.Loot == null)
            return "No CoatGame with a van and a job.";
        _van = _game.Van;
        _vehicle = _game.Vehicle;
        _loot = _game.Loot;
        _loose = _vehicle.LooseObserver ? _vehicle.LooseObserver.GetComponent<CoatObserver>() : null;
        _worn = _vehicle.WornObserver ? _vehicle.WornObserver.GetComponent<ClassicObserver>() : null;

        var log = new StringBuilder();
        log.AppendLine($"loose observer (CoatObserver): {(_loose != null ? _loose.name : "NONE")}   " +
                       $"worn observer (ClassicObserver): {(_worn != null ? _worn.name : "NONE")}");
        log.AppendLine();

        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        try
        {
            log.AppendLine(CleanGetaway());
            log.AppendLine(BustedInDisguiseSurvivesTheCooldown());
            log.AppendLine(RestartDoesNotLeakABust());
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(null);
        }

        return log.ToString();
    }

    // ---- a run that never trips either observer should pay out --------------

    static string CleanGetaway()
    {
        var s = new StringBuilder();
        s.AppendLine("CLEAN GETAWAY");

        int coinsBefore = CoatSave.Current.Coins;
        s.AppendLine("   " + BoardAndLeave());
        ComeHomeAndDeliver();

        var r = _van.LastResult;
        s.AppendLine($"   phase {_van.Now}  outcome {Outcome(r)}  payout {Pay(r)}");
        s.AppendLine($"   coins {coinsBefore} -> {CoatSave.Current.Coins}");
        s.AppendLine($"   at settle: loose ever {_loose != null && _loose.EverRumbled}  " +
                     $"worn ever {_worn != null && _worn.EverRumbled}  peak {(r.HasValue ? r.Value.PeakSuspicion : 0f):0.00}");

        bool ok = _van.Now == CoatVan.Phase.Back &&
                  r.HasValue && r.Value.Outcome == RoundOutcome.GotAway &&
                  r.Value.Payout > 0 &&
                  CoatSave.Current.Coins == coinsBefore + r.Value.Payout;
        s.AppendLine("   " + (ok ? "PASS: pays out, coins landed in the real profile"
                                  : "*** FAIL: did not pay out as expected ***"));
        return s.ToString();
    }

    // ---- the heist case: made while in disguise, then the flag cools ---------

    static string BustedInDisguiseSurvivesTheCooldown()
    {
        var s = new StringBuilder();
        s.AppendLine("BUSTED IN DISGUISE, THEN THE LIVE FLAG COOLS BEFORE YOU GET HOME");

        if (_worn == null) { s.AppendLine("   no worn observer, skipping."); return s.ToString(); }

        int coinsBefore = CoatSave.Current.Coins;
        s.AppendLine("   " + BoardAndLeave());
        s.AppendLine($"   coat worn {_vehicle.Worn}: worn observer active {_worn.isActiveAndEnabled}, " +
                     $"loose observer active {_loose != null && _loose.isActiveAndEnabled}");

        // Past 1 on purpose. [Range] is an inspector hint, not a runtime clamp,
        // so this is the same field the observer's own math writes -- and the
        // next Tick lets the REAL Suspicion >= 1f check set the flags, rather
        // than this test asserting a private flag into being. At exactly 1.0 it
        // would not: that tick's decay runs first and lands it at 0.992.
        _worn.Suspicion = 1.5f;
        Tick(null);
        s.AppendLine($"   forced it: Rumbled {_worn.Rumbled}  EverRumbled {_worn.EverRumbled}");

        int ticksToClear = Mathf.CeilToInt((_worn.ResetAfter + 1f) / Dt);
        for (int i = 0; i < ticksToClear; i++) Tick(null);
        s.AppendLine($"   after waiting out ResetAfter ({_worn.ResetAfter}s): " +
                     $"Rumbled {_worn.Rumbled} (should be false)  EverRumbled {_worn.EverRumbled} (should STILL be true)");

        ComeHomeAndDeliver();

        var r = _van.LastResult;
        s.AppendLine($"   phase {_van.Now}  outcome {Outcome(r)}  payout {Pay(r)}");
        s.AppendLine($"   coins {coinsBefore} -> {CoatSave.Current.Coins} (should be unchanged)");

        bool ok = _van.Now == CoatVan.Phase.Back &&
                  !_worn.Rumbled && _worn.EverRumbled &&
                  r.HasValue && r.Value.Outcome == RoundOutcome.Busted &&
                  r.Value.Payout == 0 &&
                  CoatSave.Current.Coins == coinsBefore;
        s.AppendLine("   " + (ok ? "PASS: busted, even though the live flag had already cleared by delivery"
                                  : "*** FAIL: the cooldown laundered a bust into a clean run ***"));
        return s.ToString();
    }

    // ---- a restart must not carry a bust into the next attempt ---------------

    static string RestartDoesNotLeakABust()
    {
        var s = new StringBuilder();
        s.AppendLine("RESTART AFTER A BUST, THEN A CLEAN ATTEMPT");

        s.AppendLine($"   before restart: worn EverRumbled {_worn != null && _worn.EverRumbled} (left over from the bust)");
        _van.Restart();
        bool cleared = (_worn == null || (!_worn.EverRumbled && _worn.PeakSuspicion == 0f)) &&
                       (_loose == null || (!_loose.EverRumbled && _loose.PeakSuspicion == 0f));
        s.AppendLine($"   right after Restart(): worn ever {_worn != null && _worn.EverRumbled} " +
                     $"peak {(_worn != null ? _worn.PeakSuspicion : 0f):0.00}   " +
                     $"loose ever {_loose != null && _loose.EverRumbled} " +
                     $"peak {(_loose != null ? _loose.PeakSuspicion : 0f):0.00}");

        int coinsBefore = CoatSave.Current.Coins;
        s.AppendLine("   " + BoardAndLeave());
        ComeHomeAndDeliver();

        var r = _van.LastResult;
        s.AppendLine($"   this clean attempt: phase {_van.Now}  outcome {Outcome(r)}  payout {Pay(r)}  " +
                     $"coins {coinsBefore} -> {CoatSave.Current.Coins}");

        bool ok = cleared && _van.Now == CoatVan.Phase.Back &&
                  r.HasValue && r.Value.Outcome == RoundOutcome.GotAway && r.Value.Payout > 0 &&
                  CoatSave.Current.Coins == coinsBefore + r.Value.Payout;
        s.AppendLine("   " + (ok ? "PASS: restart cleared both observers, the next clean run pays normally"
                                  : "*** FAIL: the previous bust leaked into this attempt ***"));
        return s.ToString();
    }

    /// Finish one round as a bust and leave it on screen, for looking at the
    /// verdict banner. Not a check -- Run() is the check.
    public static string LeaveABust()
    {
        if (!EditorApplication.isPlaying) return "Needs play mode.";
        _game = Object.FindAnyObjectByType<CoatGame>(FindObjectsInactive.Include);
        _van = _game.Van; _vehicle = _game.Vehicle; _loot = _game.Loot;
        _loose = _vehicle.LooseObserver ? _vehicle.LooseObserver.GetComponent<CoatObserver>() : null;
        _worn = _vehicle.WornObserver ? _vehicle.WornObserver.GetComponent<ClassicObserver>() : null;

        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        try
        {
            BoardAndLeave();
            _worn.Suspicion = 1.5f;
            Tick(null);
            ComeHomeAndDeliver();
            return $"phase {_van.Now}  outcome {Outcome(_van.LastResult)}  payout {Pay(_van.LastResult)}";
        }
        finally { Physics.simulationMode = mode; Send(null); }
    }

    // ---- plumbing -------------------------------------------------------------

    static string Outcome(CoatRoundResult.Result? r) => r.HasValue ? r.Value.Outcome.ToString() : "none";
    static int Pay(CoatRoundResult.Result? r) => r.HasValue ? r.Value.Payout : 0;

    /// Board everyone, open up, then put the disguise out in the street so
    /// leaving is on the record.
    static string BoardAndLeave()
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

        Vector3 street = _van.transform.TransformPoint(new Vector3(0f, 0f, 6f));
        _vehicle.Body.Place(street, _van.transform.forward);
        for (int i = 0; i < 100 && !_van.HasLeft; i++) Tick(null);

        return $"boarded (worn {_vehicle.Worn}), out in the street: left {_van.HasLeft}, home {_van.AtHome}, phase {_van.Now}";
    }

    /// Body back in the van, loot in a back corner. Not the middle -- the
    /// disguise stands there, and a tray dropped inside a ragdoll is fired
    /// across the level on depenetration (CoatJobTest found that the hard way).
    static void ComeHomeAndDeliver()
    {
        _vehicle.Body.Place(_van.transform.TransformPoint(_van.CoatSeat), _van.transform.forward);
        for (int i = 0; i < 40; i++) Tick(null);

        Vector3 corner = _van.transform.TransformPoint(new Vector3(0.75f, 0.25f, -1.2f));
        _loot.Body.transform.SetPositionAndRotation(corner, Quaternion.identity);
        _loot.Body.position = corner;
        _loot.Body.rotation = Quaternion.identity;
        _loot.Body.linearVelocity = Vector3.zero;
        _loot.Body.angularVelocity = Vector3.zero;

        for (int i = 0; i < 300 && _van.Now == CoatVan.Phase.Away; i++) Tick(null);
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

        // The observers tick themselves from FixedUpdate, which never fires in
        // here -- the whole run happens inside one editor call with no player
        // loop in between. Tick whichever one is switched on, exactly as
        // FixedUpdate would; CoatVehicle decides which that is.
        if (_loose != null && _loose.isActiveAndEnabled) _loose.Tick(Dt);
        if (_worn != null && _worn.isActiveAndEnabled) _worn.Tick(Dt);

        Physics.Simulate(Dt);
        if (_vehicle.Cam != null) _vehicle.Cam.Step(Dt);
    }
}
