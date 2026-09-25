using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Coat;

/// Board ONE player and check the coat only shows that one limb.
///
/// When the rig was capsules this came free: SetPresent(false) switches a
/// limb's GameObjects off and the renderer goes with them. One skinned mesh is
/// one renderer for the whole body, so it drew all four limbs however many
/// people had boarded -- one player turned up as a whole person, which is the
/// opposite of the joke and kills the ON THE FLOOR / ONE LEG / EMPTY SLEEVE
/// tells the observer grades on.
public static class CoatLimbCheck
{
    const float Dt = 1f / 50f;

    static CoatGame _game;
    static CoatVan _van;
    static CoatVehicle _veh;
    static CoatSkin _skin;

    [MenuItem("Coat/Check One Limb Aboard")]
    public static void FromMenu() => Debug.Log(Run(1));

    /// Drive SetLimbs directly and see what the mesh shows.
    ///
    /// Going through the van and the coat to get one player aboard turned out
    /// to be its own rabbit hole -- the character reports InCoat while the
    /// coat's own Count is still 0 -- and that is the game's boarding logic,
    /// not the skin. This asks the only question the skin is responsible for:
    /// given N limbs present, does the mesh draw N limbs?
    public static string Direct(bool legL, bool legR, bool armL, bool armR)
    {
        if (!EditorApplication.isPlaying) return "Needs play mode.";

        var game = Object.FindAnyObjectByType<CoatGame>(FindObjectsInactive.Include);
        var body = game.Vehicle.Body;
        var skin = body.GetComponent<CoatSkin>();
        if (skin == null) return "No CoatSkin.";

        bool was = body.gameObject.activeSelf;
        body.gameObject.SetActive(true);
        body.SetLimbs(legL, legR, armL, armR);
        skin.Apply();

        var log = new StringBuilder();
        log.AppendLine($"asked for legL {legL} legR {legR} armL {armL} armR {armR}");
        log.AppendLine($"  ragdoll says legs {body.Legs} arms {body.Arms}");
        foreach (var n in new[] { "ThighL", "ThighR", "UpperArmL", "UpperArmR" })
        {
            var t = body.transform.Find(n);
            log.AppendLine($"  {n,-10} activeSelf {(t != null && t.gameObject.activeSelf)}");
        }

        // Print the MATERIAL, never a hardcoded label. A fixed
        // "body, armR, armL, legR, legL" list assumed Blender's slot order
        // survived the FBX export. It does not -- Unity gets armR, legR, body,
        // legL, armL -- so the log read plausibly while the behaviour was
        // scrambled, and that is what hid the bug.
        var mats = skin.Skin.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
        {
            bool hidden = mats[i] != null && mats[i].shader != null &&
                          mats[i].shader.name == "Coat/Hidden";
            string owner = skin.SubmeshOwner != null && i < skin.SubmeshOwner.Length &&
                           !string.IsNullOrEmpty(skin.SubmeshOwner[i])
                         ? " <- " + skin.SubmeshOwner[i] : "";
            log.AppendLine($"  submesh {i} {(mats[i] == null ? "null" : mats[i].name),-14} " +
                           (hidden ? "HIDDEN" : "drawn ") + owner);
        }

        var sv = SceneView.lastActiveSceneView;
        if (sv != null)
        {
            sv.pivot = body.Pelvis.position + Vector3.up * 0.05f;
            sv.rotation = Quaternion.Euler(3f, -145f, 0f);
            sv.size = 1.5f;
            sv.drawGizmos = false;
            sv.Repaint();
        }
        if (!was) { /* leave it on so a capture shows it */ }
        return log.ToString();
    }

    /// howMany: 1 boards only the first player, 4 boards everyone.
    public static string Run(int howMany)
    {
        if (!EditorApplication.isPlaying) return "Needs play mode.";

        _game = Object.FindAnyObjectByType<CoatGame>(FindObjectsInactive.Include);
        if (_game == null || _game.Van == null) return "No CoatGame with a van.";
        _van = _game.Van;
        _veh = _game.Vehicle;

        var body = _veh.Body;
        _skin = body.GetComponent<CoatSkin>();
        if (_skin == null) return "No CoatSkin. Run Coat > Dress The Rig.";

        var log = new StringBuilder();
        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;

        try
        {
            _van.Restart();
            for (int i = 0; i < 60; i++) Tick(null);

            Key[] keys = { Key.Q, Key.O, Key.B, Key.M };
            for (int r = 0; r < Mathf.Clamp(howMany, 1, 4); r++)
            {
                for (int k = 0; k < 6; k++) Tick(new[] { keys[r] });
                for (int k = 0; k < 30; k++) Tick(null);
            }
            while (_van.Now == CoatVan.Phase.Opening) Tick(null);
            for (int k = 0; k < 80; k++) Tick(null);

            log.AppendLine($"boarded {howMany}: legs {body.Legs}  arms {body.Arms}");

            foreach (var n in new[] { "ThighL", "ThighR", "UpperArmL", "UpperArmR" })
            {
                var t = body.transform.Find(n);
                log.AppendLine($"  body {n,-10} active {(t != null && t.gameObject.activeInHierarchy)}");
            }

            var mats = _skin.Skin.sharedMaterials;
            int drawn = 0;
            for (int i = 0; i < mats.Length; i++)
            {
                bool hidden = mats[i] != null && mats[i].shader != null &&
                              mats[i].shader.name == "Coat/Hidden";
                if (!hidden) drawn++;
                log.AppendLine($"  submesh {i} {(mats[i] == null ? "null" : mats[i].name),-14} " +
                               (hidden ? "HIDDEN" : "drawn"));
            }
            log.AppendLine($"{drawn} of {mats.Length} submeshes drawn");
        }
        finally
        {
            Physics.simulationMode = mode;
            Send(null);
        }

        // Leave the camera on it so a capture shows the result.
        var sv = SceneView.lastActiveSceneView;
        if (sv != null)
        {
            sv.pivot = body.Pelvis.position + Vector3.up * 0.05f;
            sv.rotation = Quaternion.Euler(3f, -145f, 0f);
            sv.size = 1.5f;
            sv.drawGizmos = false;
            sv.Repaint();
        }
        return log.ToString();
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
