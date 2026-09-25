using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Coat;

/// Which way do the elbows bend?
///
/// The legs had their BendSign inverted: +1 put the knee 29 cm BEHIND the straight
/// hip-to-ankle line, a bird's leg, and from the side it read as a body facing the
/// wrong way. The arms were left on -1 with a comment claiming that folds an elbow
/// backwards -- but -1 is exactly the value that proved to bend a knee FORWARD.
///
/// A person's elbow points back and out. If this reports the elbow in front of the
/// shoulder-to-wrist line, the arms have the same fault the legs had.
public static class CoatArmCheck
{
    const float Dt = 1f / 50f;

    static CoatGame _game;
    static CoatVan _van;
    static CoatVehicle _vehicle;

    [MenuItem("Coat/Check Elbows")]
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
            Dress();
            var b = _vehicle.Body;
            log.AppendLine($"aboard: legs {b.Legs} arms {b.Arms}   " +
                           $"bendSign  arms {b.ArmL.BendSign}  legs {b.LegL.BendSign}");
            log.AppendLine("Elbow offset is measured along the way the body faces, from the");
            log.AppendLine("straight line between shoulder and wrist. A person's elbow points");
            log.AppendLine("BACK, so negative is right and positive is the wrong way round.");
            log.AppendLine("");

            log.AppendLine("hands at rest");
            for (int i = 0; i < 80; i++) Tick(null);
            log.AppendLine(Arms());

            log.AppendLine("");
            log.AppendLine("reaching down for something on a table");
            for (int i = 0; i < 90; i++) Tick(new[] { Key.G, Key.Slash });
            log.AppendLine(Arms());

            log.AppendLine("");
            log.AppendLine("hands raised");
            for (int i = 0; i < 140; i++) Tick(new[] { Key.T, Key.P });
            log.AppendLine(Arms());
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(null);
        }

        return log.ToString();
    }

    static string Arms()
    {
        var b = _vehicle.Body;
        return "   " + One(b.ArmL, "left ") + "\n   " + One(b.ArmR, "right");
    }

    static string One(Coat.Classic.ClassicLimb arm, string tag)
    {
        Vector3 fwd = Vector3.ProjectOnPlane(_vehicle.Body.Torso.transform.forward, Vector3.up).normalized;

        // Each bone is a capsule whose local +Y runs from its parent end to its child
        // end, with its origin at the midpoint.
        Vector3 shoulder = arm.Upper.position - arm.Upper.transform.up * (arm.UpperLen * 0.5f);
        Vector3 elbow = arm.Upper.position + arm.Upper.transform.up * (arm.UpperLen * 0.5f);
        Vector3 wrist = arm.Lower.position + arm.Lower.transform.up * (arm.LowerLen * 0.5f);

        float bulge = Vector3.Dot(elbow - (shoulder + wrist) * 0.5f, fwd);
        float bend = Vector3.Angle(shoulder - elbow, wrist - elbow);

        return $"{tag} elbow {bulge * 100f,6:0.0} cm {(bulge > 0.02f ? "FORWARD (wrong)" : bulge < -0.02f ? "back (right)  " : "in line       ")}" +
               $"  bend {bend,3:0} deg   hand y {arm.End.position.y,4:0.00}";
    }

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
