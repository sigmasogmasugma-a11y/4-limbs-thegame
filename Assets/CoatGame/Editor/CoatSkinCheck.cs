using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Coat;

/// Does the mesh actually follow the ragdoll, and does wearing it change how
/// the ragdoll walks?
///
/// Two things worth measuring and one worth proving:
///   * every bone should sit at a FIXED offset from its body, forever. If that
///     drifts, the bind is wrong and the mesh will slowly slide off.
///   * the walk should be unchanged. The mesh is drawn, not simulated -- the
///     colliders are untouched -- so the distance covered should match what
///     the rig did bare.
///   * the renderer's bounds should travel with the body, not sit at the
///     origin. A skinned mesh whose bones are moved by script keeps its
///     authored bounds unless updateWhenOffscreen is on, and then vanishes the
///     moment the camera looks away from where it used to be.
public static class CoatSkinCheck
{
    const float Dt = 1f / 50f;

    static CoatGame _game;
    static CoatVan _van;
    static CoatVehicle _vehicle;
    static CoatSkin _skin;

    [MenuItem("Coat/Check The Skin")]
    public static void FromMenu() => Debug.Log(Run());

    /// wearing=false strips the mesh off first and walks the bare rig, so the
    /// two distances can be compared. Claiming "the mesh is drawn, not
    /// simulated, so the walk is unchanged" is an argument; this is a number.
    public static string Run(bool wearing = true)
    {
        if (!EditorApplication.isPlaying) return "Needs play mode.";

        _game = Object.FindAnyObjectByType<CoatGame>(FindObjectsInactive.Include);
        if (_game == null || _game.Van == null) return "No CoatGame with a van.";
        _van = _game.Van;
        _vehicle = _game.Vehicle;

        var body = _vehicle.Body;
        var skin = body.GetComponent<CoatSkin>();
        if (skin == null) return "No CoatSkin on the body. Run Coat > Dress The Rig.";

        if (!wearing)
        {
            skin.ShowPrimitives();
            var worn = body.transform.Find("Skin");
            if (worn != null) Object.DestroyImmediate(worn.gameObject);
            Object.DestroyImmediate(skin);
            skin = null;
        }
        _skin = skin;

        var log = new StringBuilder();
        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;

        try
        {
            Dress();
            log.AppendLine(wearing ? skin.LastReport : "BARE RIG -- mesh removed");

            float[] rest = wearing ? Offsets(skin) : null;
            Vector3 start = body.Pelvis.position;

            for (int i = 0; i < 260; i++) Tick(new[] { Key.W, Key.I });

            Vector3 end = body.Pelvis.position;
            float travelled = Vector3.Distance(
                new Vector3(start.x, 0f, start.z), new Vector3(end.x, 0f, end.z));

            log.AppendLine($"walked {travelled:0.00} m in 5.2 s  " +
                           $"(ended {end.x:0.00}, {end.z:0.00})");

            if (wearing)
            {
                float[] now = Offsets(skin);
                float drift = 0f;
                for (int i = 0; i < rest.Length; i++)
                    drift = Mathf.Max(drift, Mathf.Abs(now[i] - rest[i]));
                log.AppendLine($"worst bone-to-body drift {drift * 1000f:0.00} mm " +
                               (drift < 0.001f ? "(rigid, as it should be)"
                                               : "(BIND IS SLIPPING)"));

                Bounds b = skin.Skin.bounds;
                float off = Vector3.Distance(
                    new Vector3(b.center.x, 0f, b.center.z),
                    new Vector3(end.x, 0f, end.z));
                log.AppendLine($"renderer bounds size {b.size}, {off:0.00} m from the pelvis " +
                               (off < 0.6f ? "(travelling with it)" : "(LEFT BEHIND)"));
            }

            int drawn = 0;
            foreach (var rb in body.GetComponentsInChildren<Rigidbody>(true))
            {
                var r = rb.GetComponent<MeshRenderer>();
                if (r != null && r.enabled) drawn++;
            }
            log.AppendLine($"primitive renderers still drawn: {drawn} " +
                           (drawn == 0 ? "(all hidden)" : "(should be 0)"));
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(null);
        }

        return log.ToString();
    }

    static float[] Offsets(CoatSkin skin)
    {
        var bones = skin.Skin.bones;
        var rig = skin.Rig;
        var by = new System.Collections.Generic.Dictionary<string, Transform>();
        foreach (var rb in rig.GetComponentsInChildren<Rigidbody>(true))
            by[rb.name] = rb.transform;

        var d = new float[bones.Length];
        for (int i = 0; i < bones.Length; i++)
            d[i] = bones[i] != null && by.ContainsKey(bones[i].name)
                 ? Vector3.Distance(bones[i].position, by[bones[i].name].position)
                 : 0f;
        return d;
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
        if (_skin != null) _skin.Apply();
        if (_vehicle.Cam != null) _vehicle.Cam.Step(Dt);
    }
}
