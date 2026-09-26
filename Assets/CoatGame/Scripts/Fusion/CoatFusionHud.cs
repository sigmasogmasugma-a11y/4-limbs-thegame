#if FUSION2
using UnityEngine;
using UnityEngine.InputSystem;

namespace Coat.Fusion
{
    /// Connection status only. The round's verdict is CoatVan's banner, which
    /// clients now show from the host's result, so it is not repeated here.
    /// Drawn just above the bottom line: the top-left is the van's line and the
    /// HUD panels, and the very bottom is CoatVehicle's coat line.
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
            // One line up from the bottom: CoatVehicle's coat line ("nobody in the
            // coat", "a whole person") already sits at the very bottom, and
            // drawing on the same line printed the two over each other.
            float y = Screen.height - 60f;

            if (World != null && World.Runner != null && World.Runner.IsRunning)
            {
                bool host = World.IsHostAuthority;
                string role = World.LocalRoleIndex < 0
                    ? "waiting for a limb"
                    : $"you are the <b>{CoatFusionWorld.RoleName(World.LocalRoleIndex)}</b>   (W A S D, Left Shift, Q)";

                // The tick count says the host's game is running at all; if it
                // sits at 0, nothing can move whatever the keys do.
                string ticks = host ? $"   <color=#9a8fa8>tick {World.HostTicks}</color>" : "";
                GUI.Label(new Rect(14f, y, 1600f, 24f),
                    $"<color=#8ce87a>ONLINE</color> ({(host ? "host" : "client")})   {World.PlayerCount}/4 players   {role}{ticks}", _style);

                if (host)
                {
                    string spare = SpareLimbs();
                    if (spare.Length > 0)
                        GUI.Label(new Rect(14f, y - 24f, 1600f, 24f),
                            "<color=#9a8fa8>limbs nobody has joined for are yours too:</color>   " + spare, _style);
                }

                if (!string.IsNullOrEmpty(World.LastError))
                    GUI.Label(new Rect(14f, y - 48f, 1600f, 24f),
                        "<color=#ff5b5b>error: " + World.LastError + "</color>", _style);
                return;
            }

            // Connected, but the game's network object never joined the session:
            // the scene was not set up for Fusion, or not saved after it was.
            if (Runner != null && Runner.Running)
            {
                GUI.Label(new Rect(14f, y, 1400f, 24f),
                    "<color=#ffd24a>Connected, but the game did not join the session.</color>   " +
                    "Stop Play, run Coat > Fusion > Setup Current Scene, save (Ctrl+S), try again.", _style);
                return;
            }

            if (Runner != null && Runner.Starting)
            {
                GUI.Label(new Rect(14f, y, 900f, 24f),
                    "<color=#ffd24a>Connecting to Photon...</color>   this takes a few seconds, keep Play running", _style);
                return;
            }

            if (Runner != null && !Runner.Running)
            {
                string failed = Runner.LastError != null
                    ? $"   <color=#ff5b5b>last try failed: {Runner.LastError}</color>" : "";
                GUI.Label(new Rect(14f, y, 1400f, 24f), "F6 Host   |   F7 Join   |   Session: " + Runner.SessionName + failed, _style);
            }
        }

        void Update()
        {
            if (Runner == null || Keyboard.current == null) return;
            if (Keyboard.current.f6Key.wasPressedThisFrame) _ = Runner.StartHost();
            if (Keyboard.current.f7Key.wasPressedThisFrame) _ = Runner.StartClient();
        }

        /// The limbs the host's own keyboard is playing, with their offline keys,
        /// read from the live bindings so the two cannot drift apart.
        string SpareLimbs()
        {
            var input = World.LocalInput;
            string list = "";
            for (int i = 0; i < 4; i++)
            {
                if (World.RoleTaken(i)) continue;
                string keys = input != null ? input.Describe(i) : "its usual keys";
                list += (list.Length > 0 ? "   |   " : "") + CoatFusionWorld.RoleName(i) + " " + keys;
            }
            return list;
        }
    }
}
#endif
