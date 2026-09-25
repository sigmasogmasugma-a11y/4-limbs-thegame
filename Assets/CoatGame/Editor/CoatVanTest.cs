using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Coat;

/// A whole round, start to finish: shut in the van, get dressed, door up, walk out,
/// walk home. Checks the things that would make the van a nuisance rather than a
/// frame -- a shutter that does not clear the opening, a crew that cannot reach the
/// coat from where they are sitting, an extraction that never triggers.
public static class CoatVanTest
{
    const float Dt = 1f / 50f;

    static CoatGame _game;
    static CoatVan _van;
    static CoatVehicle _vehicle;

    static readonly Key[] FwdL = { Key.W }, FwdR = { Key.I };
    static readonly Key[] BackL = { Key.S }, BackR = { Key.K };

    [MenuItem("Coat/Test The Van")]
    public static void FromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (!EditorApplication.isPlaying) return "Needs play mode.";

        _game = Object.FindAnyObjectByType<CoatGame>(FindObjectsInactive.Include);
        if (_game == null || _game.Van == null) return "No CoatGame with a van.";
        _van = _game.Van;
        _vehicle = _game.Vehicle;

        var log = new StringBuilder();
        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;

        try
        {
            _van.Restart();

            // Restart vacates the coat, and vacating puts a ReentryDelay on everybody.
            // Pressing inside that window is silently refused -- the first test of this
            // pressed after a fifth of a second and reported the left leg as unable to
            // board at all.
            for (int i = 0; i < 60; i++) Tick(null);

            log.AppendLine($"1. shut in      phase {_van.Now}   aboard {_game.Coat.Count}   " +
                           $"shutter {_van.DoorOpenness:0.00}   " +
                           $"everyone inside: {AllInside()}");

            // Each player presses their own coat key from where they are sitting.
            Key[] keys = { Key.Q, Key.O, Key.B, Key.M };
            for (int r = 0; r < 4; r++)
            {
                for (int k = 0; k < 6; k++) Tick(new[] { keys[r] });
                for (int k = 0; k < 25; k++) Tick(null);
                log.AppendLine($"   {keys[r]} pressed -> aboard {_game.Coat.Count}   phase {_van.Now}");
            }

            log.AppendLine($"2. dressed      phase {_van.Now}   worn {_vehicle.Worn}   " +
                           $"legs {_vehicle.Body.Legs} arms {_vehicle.Body.Arms}");

            // The shutter should take about DoorSeconds and then be clear of the hole.
            int lift = 0;
            while (_van.DoorOpenness < 1f && lift < 300) { Tick(null); lift++; }
            log.AppendLine($"3. door up      after {lift * Dt:0.00}s   phase {_van.Now}   " +
                           $"shutter bottom at y {DoorBottom():0.00} " +
                           $"(needs to clear {_vehicle.Body.Head.position.y:0.00}, the head)");

            // Out of the back and up the street.
            Vector3 from = _vehicle.Body.Pelvis.position;
            var walk = new StringBuilder();
            for (int i = 0; i < 400; i++)
            {
                int ph = i % 20;
                Tick(ph < 3 ? FwdL : ph >= 10 && ph < 13 ? FwdR : null);

                if (i % 50 != 0) continue;
                Vector3 p = _vehicle.Body.Pelvis.position;
                Vector3 local = _van.transform.InverseTransformPoint(p);
                walk.AppendLine($"      t+{i * Dt,4:0.0}s  z {p.z,6:0.00}  " +
                                $"van-local z {local.z,5:0.00} (out past 1.65)  " +
                                $"hips {p.y,4:0.00}  facing {Bear(_vehicle.Body.Heading),4:0}  " +
                                $"{(_van.Inside(p) ? "inside" : "OUT")}");
            }
            Vector3 outside = _vehicle.Body.Pelvis.position;
            log.Append(walk);
            log.AppendLine($"4. walked out   {(outside - from).magnitude:0.00} m   " +
                           $"still in the van: {_van.Inside(outside)}   " +
                           $"clock {_van.Elapsed:0.0}s   home {_van.AtHome} of 4");

            // And back in again.
            for (int i = 0; i < 700 && _van.Now == CoatVan.Phase.Away; i++)
            {
                int ph = i % 20;
                Tick(ph < 3 ? BackL : ph >= 10 && ph < 13 ? BackR : null);
            }

            log.AppendLine($"5. came home    phase {_van.Now}   home {_van.AtHome} of 4   " +
                           $"clock {_van.Elapsed:0.0}s");

            for (int i = 0; i < 120; i++) Tick(null);
            log.AppendLine($"6. shutter down after the round: {_van.DoorOpenness:0.00}");

            log.AppendLine("");
            log.AppendLine(_van.Now == CoatVan.Phase.Back
                ? "the round ran start to finish"
                : "*** NEVER GOT HOME ***");
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(null);
        }

        return log.ToString();
    }

    static float Bear(Vector3 v)
    {
        Vector3 f = Vector3.ProjectOnPlane(v, Vector3.up);
        return f.sqrMagnitude < 1e-5f ? 0f : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
    }

    static bool AllInside()
    {
        for (int i = 0; i < 4; i++)
            if (!_van.Inside(_game.Characters[i].Body.position)) return false;
        return true;
    }

    /// The bottom edge of the shutter in world Y, which is what anyone walking out
    /// has to get under.
    static float DoorBottom() =>
        _van.Door.position.y - _van.Door.lossyScale.y * 0.5f;

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
