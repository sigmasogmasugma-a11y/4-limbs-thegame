#if FUSION2
using UnityEngine;
using UnityEngine.InputSystem;

namespace Coat.Fusion
{
    /// Connection status only. The round's verdict is CoatVan's banner, which
    /// clients now show from the host's result, so it is not repeated here.
    /// Drawn along the bottom: the top-left is the van's line and the HUD panels.
    public sealed class CoatFusionHud : MonoBehaviour
    {
        public CoatFusionWorld World;
        public CoatFusionRunner Runner;
        public bool Show = true;

        GUIStyle _style;

        void OnGUI()
        {
            if (!Show) return;
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };
            float y = Screen.height - 34f;

            if (World != null && World.Runner != null && World.Runner.IsRunning)
            {
                string who = World.IsHostAuthority ? "host" : "client";
                string role = World.LocalRoleIndex < 0
                    ? "waiting for a limb"
                    : $"you are the <b>{RoleName(World.LocalRoleIndex)}</b>   (W A S D, Left Shift, Q)";
                GUI.Label(new Rect(14f, y, 900f, 24f),
                    $"<color=#8ce87a>ONLINE</color> ({who})   {World.PlayerCount}/4 players   {role}", _style);
                return;
            }

            if (Runner != null && !Runner.Running)
                GUI.Label(new Rect(14f, y, 900f, 24f), "F6 Host   |   F7 Join   |   Session: " + Runner.SessionName, _style);
        }

        void Update()
        {
            if (Runner == null || Keyboard.current == null) return;
            if (Keyboard.current.f6Key.wasPressedThisFrame) _ = Runner.StartHost();
            if (Keyboard.current.f7Key.wasPressedThisFrame) _ = Runner.StartClient();
        }

        static string RoleName(int role) => role switch
        {
            (int)CoatRole.LeftLeg => "left leg",
            (int)CoatRole.RightLeg => "right leg",
            (int)CoatRole.LeftArm => "left arm",
            (int)CoatRole.RightArm => "right arm",
            _ => "waiting"
        };
    }
}
#endif
