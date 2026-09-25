using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Coat;

/// How far does it get on ONE leg key versus both?
///
/// The complaint is having to press W and I together to move at all. Two things
/// could cause that:
///
///   StepCooldown     0.06 s after a step, per leg. Tiny.
///   AlternatingSteps neither leg may step twice in a row, and the lock only
///                    releases after TurnTimeout = 1.5 s.
///
/// With one key held, alternation means one step then a 1.5 s wait, over and
/// over. That is the one worth measuring: distance on W alone against W+I.
public static class CoatStepCheck
{
    const float Dt = 1f / 50f;

    static CoatGame _game;
    static CoatVan _van;
    static CoatVehicle _veh;
    static CoatSkin _skin;

    [MenuItem("Coat/Check Stepping")]
    public static void FromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (!EditorApplication.isPlaying) return "Needs play mode.";

        _game = Object.FindAnyObjectByType<CoatGame>(FindObjectsInactive.Include);
        if (_game == null || _game.Van == null) return "No CoatGame with a van.";
        _van = _game.Van;
        _veh = _game.Vehicle;
        _skin = _veh.Body.GetComponent<CoatSkin>();

        var body = _veh.Body;
        var log = new StringBuilder();
        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;

        try
        {
            log.AppendLine($"alternating {body.AlternatingSteps}  " +
                           $"turnTimeout {body.TurnTimeout:0.00}  " +
                           $"stepCooldown {body.LegL.StepCooldown:0.000}");

            log.AppendLine(Leg("W only      ", new[] { Key.W }));
            log.AppendLine(Leg("I only      ", new[] { Key.I }));
            log.AppendLine(Leg("W and I     ", new[] { Key.W, Key.I }));
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(null);
        }
        return log.ToString();
    }

    /// Does a child walk on TWO legs, out in the van?
    ///
    /// They used to have one leg apiece -- two of them side by side under the
    /// coat being the disguise's pair -- which is fine inside the coat, where
    /// the character is switched off entirely and the shared body walks, but
    /// out in the lobby it left them hopping.
    public static string CrewWalk(string who, Key[] keys)
    {
        if (!EditorApplication.isPlaying) return "Needs play mode.";

        _game = Object.FindAnyObjectByType<CoatGame>(FindObjectsInactive.Include);
        _van = _game.Van;
        _veh = _game.Vehicle;
        _skin = null;

        CoatCharacter c = null;
        foreach (var x in Object.FindObjectsByType<CoatCharacter>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (x.name == who) { c = x; break; }
        if (c == null) return who + " not found";

        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        try
        {
            _van.Restart();
            for (int i = 0; i < 60; i++) Tick(null);

            Vector3 start = c.Body.position;
            int steps = 0;
            float gapMin = 99f, gapMax = 0f;
            for (int i = 0; i < 250; i++)
            {
                Tick(keys);
                if (c.JustStepped) steps++;
                if (c.Leg != null && c.LegR != null)
                {
                    Vector3 a = c.Leg.Foot.position, b = c.LegR.Foot.position;
                    a.y = 0f; b.y = 0f;
                    float g = Vector3.Distance(a, b);
                    gapMin = Mathf.Min(gapMin, g);
                    gapMax = Mathf.Max(gapMax, g);
                }
            }
            Vector3 end = c.Body.position;
            float d = Vector3.Distance(new Vector3(start.x, 0f, start.z),
                                       new Vector3(end.x, 0f, end.z));

            // InCoat matters: pressOnly is InCoat && PressOnlyInCoat, so a
            // child wrongly flagged as aboard goes back to one step per press.
            return $"  {who}  moved {d:0.00} m in 5 s   steps {steps}" +
                   $"   legs {(c.LegR != null ? 2 : 1)}" +
                   $"   foot gap {gapMin:0.00}-{gapMax:0.00} m" +
                   $"   inCoat {c.InCoat}" +
                   $"   {start.x:0.0},{start.z:0.0} -> {end.x:0.0},{end.z:0.0}";
        }
        finally { Physics.simulationMode = mode; Send(null); }
    }

    /// Do opposing leg inputs pull the feet apart and put it on the floor?
    ///
    /// A is player one pushing left and L is player two pushing right, so the
    /// two leg players are hauling against each other; W against K is the same
    /// thing fore and aft. Measured: how far the feet separate, and how long
    /// until the body gives way.
    public static string Splits(string label, Key[] keys)
    {
        if (!EditorApplication.isPlaying) return "Needs play mode.";

        _game = Object.FindAnyObjectByType<CoatGame>(FindObjectsInactive.Include);
        _van = _game.Van;
        _veh = _game.Vehicle;
        _skin = _veh.Body.GetComponent<CoatSkin>();

        var body = _veh.Body;
        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        try
        {
            Board();
            float before = Gap(body);

            float widest = before, tCollapse = -1f, gapAtFall = 0f;
            float lowestHead = 99f, mostLean = 0f;
            for (int i = 0; i < 400; i++)
            {
                Tick(keys);
                widest = Mathf.Max(widest, Gap(body));
                lowestHead = Mathf.Min(lowestHead, body.Head.position.y);
                mostLean = Mathf.Max(mostLean,
                    Vector3.Angle(body.Torso.transform.up, Vector3.up));
                if (body.Collapsed && tCollapse < 0f)
                {
                    tCollapse = i * Dt;
                    gapAtFall = Gap(body);
                }
            }
            float after = Gap(body);
            bool down = body.Collapsed;

            // Falling needs head below HeadMinHeight or lean past MaxLean --
            // a wide stance on its own never triggers it.
            return $"  {label}  feet {before:0.00} -> {widest:0.00} m" +
                   $"   head low {lowestHead:0.00} (needs <{body.HeadMinHeight:0.00})" +
                   $"   lean {mostLean:0} deg (needs >{body.MaxLean:0})" +
                   $"   fell {(tCollapse >= 0f ? tCollapse.ToString("0.00") + " s" : "NO")}";
        }
        finally { Physics.simulationMode = mode; Send(null); }
    }

    static float Gap(Coat.Classic.ClassicRagdoll body)
    {
        if (body.LegL == null || body.LegR == null) return 0f;
        Vector3 a = body.LegL.End.position, b = body.LegR.End.position;
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    /// Board fresh, hold the keys for 5 s, report distance and steps taken.
    static string Leg(string label, Key[] keys)
    {
        Board();
        var body = _veh.Body;

        int stepsL = 0, stepsR = 0;
        Vector3 start = body.Pelvis.position;
        for (int i = 0; i < 250; i++)
        {
            Tick(keys);
            if (body.LegL.JustStepped) stepsL++;
            if (body.LegR.JustStepped) stepsR++;
        }
        Vector3 end = body.Pelvis.position;
        float d = Vector3.Distance(new Vector3(start.x, 0f, start.z),
                                   new Vector3(end.x, 0f, end.z));
        return $"  {label} {d:0.00} m in 5 s   steps L {stepsL} R {stepsR}";
    }

    static void Board()
    {
        _van.Restart();
        for (int i = 0; i < 60; i++) Tick(null);

        Key[] seat = { Key.Q, Key.O, Key.B, Key.M };
        for (int r = 0; r < 4; r++)
        {
            for (int k = 0; k < 6; k++) Tick(new[] { seat[r] });
            for (int k = 0; k < 30; k++) Tick(null);
        }
        while (_van.Now == CoatVan.Phase.Opening) Tick(null);
        for (int k = 0; k < 40; k++) Tick(null);
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

        var body = _veh.Body;
        if (body.gameObject.activeInHierarchy) { body.Input.Sample(); body.Tick(Dt); }

        _game.Tick(Dt);
        Physics.Simulate(Dt);
        if (_skin != null) _skin.Apply();
        if (_veh.Cam != null) _veh.Cam.Step(Dt);
    }
}
