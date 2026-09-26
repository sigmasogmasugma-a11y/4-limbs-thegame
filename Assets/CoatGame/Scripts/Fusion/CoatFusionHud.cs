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

        // Frames drawn per second, counted over half a second, so a slow build can
        // be told apart from a smooth one that is only being sent few updates.
        float _fps, _fpsSince;
        int _frames;

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
                // sits at 0, nothing can move whatever the keys do. A client shows
                // how often fresh host states arrive instead.
                string ticks = host
                    ? $"   <color=#9a8fa8>tick {World.HostTicks}   {_fps:0} fps</color>"
                    : $"   <color=#9a8fa8>{_fps:0} fps   host updates {World.HostUpdatesPerSecond:0}/s</color>";
                // The lobby code, for the host to read out to friends.
                string lobby = Runner != null ? $"lobby <b>{Runner.SessionName}</b>   " : "";
                GUI.Label(new Rect(14f, y, 1600f, 24f),
                    $"<color=#8ce87a>ONLINE</color> ({(host ? "host" : "client")})   {lobby}{World.PlayerCount}/4 players   {role}{ticks}", _style);

                if (host)
                    GUI.Label(new Rect(14f, y - 24f, 1800f, 24f), Limbs(), _style);

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
            _frames++;
            float counted = Time.unscaledTime - _fpsSince;
            if (counted >= 0.5f)
            {
                _fps = _frames / counted;
                _frames = 0;
                _fpsSince = Time.unscaledTime;
            }

            if (Runner == null || Keyboard.current == null) return;
            if (Keyboard.current.f6Key.wasPressedThisFrame) _ = Runner.StartHost();
            if (Keyboard.current.f7Key.wasPressedThisFrame) _ = Runner.StartClient();
        }

        /// Host only: who plays each limb, and for a joined player whether their
        /// input is reaching the host. Keys are read from the live bindings so the
        /// two cannot drift apart.
        string Limbs()
        {
            var input = World.LocalInput;
            string Keys(int set) => input != null ? input.Describe(set) : "?";

            string list = "";
            for (int i = 0; i < 4; i++)
            {
                string who;
                if (World.RoleIsLocal(i))
                    who = "you, " + Keys(CoatFusionInputProvider.OwnControls);
                else if (!World.RoleTaken(i))
                    who = i == CoatFusionInputProvider.OwnControls && World.LocalRoleIndex >= 0 && World.LocalRoleIndex != i
                        ? "nobody"
                        : "your " + Keys(i);
                else
                    who = World.RolePlayers.Get(i) + (World.InputArriving(i)
                        ? ", input ok"
                        : ", <color=#ff5b5b>no input</color>");

                list += (list.Length > 0 ? "   |   " : "") + "<color=#9a8fa8>" + CoatFusionWorld.RoleName(i) + ":</color> " + who;
            }
            return list;
        }
    }
}
#endif
