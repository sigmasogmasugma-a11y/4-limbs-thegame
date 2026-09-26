#if FUSION2
using UnityEngine;
using UnityEngine.InputSystem;

namespace Coat.Fusion
{
    public sealed class CoatFusionHud : MonoBehaviour
    {
        public CoatFusionWorld World;
        public CoatFusionRunner Runner;
        public bool Show = true;

        void OnGUI()
        {
            if (!Show) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };
            float y = 46f;

            if (World != null && World.Runner != null && World.Runner.IsRunning)
            {
                string status = World.RoundResult != 0
                    ? (World.RoundResult == 1 ? "<color=#8ce87a><b>HEIST COMPLETE</b></color>" : "<color=#ff5b5b><b>CAUGHT</b></color>")
                    : $"<color=#8ce87a>ONLINE</color>   {World.PlayerCount}/4 players   role: {RoleName(World.LocalRoleIndex)}";
                GUI.Label(new Rect(14f, y, 900f, 24f), status, style);
                GUI.Label(new Rect(14f, y + 24f, 900f, 24f),
                    $"Suspicion {World.Suspicion:0.00}   Home {World.AtHome}/4   Loot {(World.LootDelivered ? "delivered" : "not delivered")}", style);
                return;
            }

            if (Runner != null && !Runner.Running)
                GUI.Label(new Rect(14f, y, 900f, 24f), "F6 Host   |   F7 Client   |   Session: " + Runner.SessionName, style);
        }

        void Update()
        {
            if (Runner == null || Keyboard.current == null) return;
            if (Keyboard.current.f6Key.wasPressedThisFrame) _ = Runner.StartHost();
            if (Keyboard.current.f7Key.wasPressedThisFrame) _ = Runner.StartClient();
        }

        static string RoleName(int role) => role < 0 ? "waiting" : ((CoatRole)role).ToString();
    }
}
#endif
