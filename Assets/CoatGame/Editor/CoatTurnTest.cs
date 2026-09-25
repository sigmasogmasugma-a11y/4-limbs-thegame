using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Coat;
using Coat.Classic;

/// Changing direction mid-walk, which is what a player actually does and which none of
/// the other tests covered -- they all start from a fresh mount, standing still.
///
/// Facing is blended from BOTH legs' last step directions over a memory window, so what
/// the body does when you switch direction depends on what the other leg did last.
public static class CoatTurnTest
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

    [MenuItem("Coat/Test Direction Changes")]
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
            log.AppendLine("Walk forward first, then change direction. Bearing: 0 = start, -90 = left, +90 = right.");
            log.AppendLine("");

            log.AppendLine("BOTH LEGS SWITCH TO THE NEW DIRECTION");
            log.AppendLine(Swap("then left ", LeftL, LeftR, -90f));
            log.AppendLine(Swap("then right", RightL, RightR, 90f));
            log.AppendLine(Swap("then back ", BackL, BackR, 0f));

            log.AppendLine("");
            log.AppendLine("ONLY THE LEFT LEG SWITCHES, the right leg keeps walking forward");
            log.AppendLine(Swap("then left ", LeftL, FwdR, -90f));
            log.AppendLine(Swap("then right", RightL, FwdR, 90f));

            log.AppendLine("");
            log.AppendLine("ONLY THE LEFT LEG SWITCHES, the right leg stops pressing");
            log.AppendLine(Swap("then left ", LeftL, null, -90f));
            log.AppendLine(Swap("then right", RightL, null, 90f));

            log.AppendLine("");
            log.AppendLine("ONLY THE RIGHT LEG SWITCHES, the left leg keeps walking forward");
            log.AppendLine(Swap("then left ", FwdL, LeftR, -90f));
            log.AppendLine(Swap("then right", FwdL, RightR, 90f));

            log.AppendLine("");
            log.AppendLine("CHANGING YOUR MIND: walk one way, then walk the OPPOSITE way");
            log.AppendLine(Reverse("right then left", RightL, RightR, LeftL, LeftR));
            log.AppendLine(Reverse("left then right", LeftL, LeftR, RightL, RightR));
            log.AppendLine(Reverse("fwd then back   ", FwdL, FwdR, BackL, BackR));
            log.AppendLine(Reverse("back then fwd   ", BackL, BackR, FwdL, FwdR));
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(null);
        }

        return log.ToString();
    }

    static string Swap(string label, Key[] newL, Key[] newR, float wanted)
    {
        Board();
        for (int i = 0; i < 150; i++) Tick(null);

        // Three seconds of walking straight forward first.
        for (int i = 0; i < 150; i++)
        {
            int ph = i % 20;
            Tick(ph < 3 ? FwdL : ph >= 10 && ph < 13 ? FwdR : null);
        }
        float afterForward = Bearing(_body.Heading);

        // Then the new direction, for four seconds.
        for (int i = 0; i < 200; i++)
        {
            int ph = i % 20;
            Tick(ph < 3 ? newL : ph >= 10 && ph < 13 ? newR : null);
        }

        float ended = Bearing(_body.Heading);
        float turned = Delta(afterForward, ended);
        bool wrongWay = Mathf.Abs(wanted) > 1f && Mathf.Sign(turned) != Mathf.Sign(wanted) && Mathf.Abs(turned) > 10f;

        return $"  {label}  after forward {afterForward,4:0} deg  ->  turned {turned,5:0} deg  " +
               $"(wanted {wanted,4:0})  {(wrongWay ? "*** WRONG WAY ***" : "")}";
    }

    /// Walk one way for a while, then the opposite way. The body should end up facing
    /// roughly where it is now going, not still facing where it started.
    static string Reverse(string label, Key[] firstL, Key[] firstR, Key[] thenL, Key[] thenR)
    {
        Board();
        for (int i = 0; i < 150; i++) Tick(null);

        for (int i = 0; i < 200; i++)
        {
            int ph = i % 20;
            Tick(ph < 3 ? firstL : ph >= 10 && ph < 13 ? firstR : null);
        }
        float wentFirst = Bearing(_body.Heading);

        // Let the turn happen and settle, THEN measure. Measuring across the turn
        // itself mixes the old direction of travel in and says nothing useful.
        for (int i = 0; i < 200; i++)
        {
            int ph = i % 20;
            Tick(ph < 3 ? thenL : ph >= 10 && ph < 13 ? thenR : null);
        }

        Vector3 markA = _body.Pelvis.position;
        for (int i = 0; i < 150; i++)
        {
            int ph = i % 20;
            Tick(ph < 3 ? thenL : ph >= 10 && ph < 13 ? thenR : null);
        }
        float wentThen = Bearing(_body.Heading);
        Vector3 travel = _body.Pelvis.position - markA;
        travel.y = 0f;

        // Does the body face roughly the way it is now travelling?
        float mismatch = travel.magnitude < 0.15f ? -1f
            : Vector3.Angle(_body.Heading, travel.normalized);

        string verdict = mismatch < 0f ? "(barely moved)"
                       : mismatch > 90f ? "*** FACING THE WRONG WAY ***"
                       : mismatch > 45f ? "(facing well off its travel)"
                       : "ok";

        return $"  {label}  faced {wentFirst,4:0} -> {wentThen,4:0} deg   " +
               $"travelled {travel.magnitude,4:0.00} m   facing is {mismatch,4:0} deg off travel   {verdict}";
    }

    static float Bearing(Vector3 v)
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
}
