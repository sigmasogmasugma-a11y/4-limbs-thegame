using UnityEngine;
using UnityEngine.InputSystem;

namespace Coat
{
    /// Throwaway prototype readout: who is who, who is in the coat, and how convinced
    /// the observer is. Delete once it stops being useful.
    public class CoatHud : MonoBehaviour
    {
        public CoatGame Game;
        public LocalCoatInput Input;
        public TheCoat Coat;
        public CoatCamera Cam;
        public CoatObserver Observer;
        public float OrbitSpeed = 90f;
        [Tooltip("Off hides the readout but leaves the orbit keys working, " +
                 "which is why this is not simply the component being disabled.")]
        public bool Draw = true;

        static readonly string[] Names = { "LEFT LEG ", "RIGHT LEG", "LEFT ARM ", "RIGHT ARM" };
        static readonly string[] Where = { "bottom left", "bottom right", "top left", "top right" };

        GUIStyle _style;
        Texture2D _px;

        Texture2D Px
        {
            get
            {
                if (_px == null)
                {
                    _px = new Texture2D(1, 1);
                    _px.SetPixel(0, 0, Color.white);
                    _px.Apply();
                }
                return _px;
            }
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || Cam == null) return;
            if (kb[Key.LeftBracket].isPressed) Cam.Orbit -= OrbitSpeed * Time.deltaTime;
            if (kb[Key.RightBracket].isPressed) Cam.Orbit += OrbitSpeed * Time.deltaTime;
        }

        void OnGUI()
        {
            if (!Draw) return;
            if (Game == null || Input == null || Coat == null) return;

            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };

            // Starts at 40, not 14: CoatVan draws its own banner across the top
            // at y 12, and this box used to run underneath it.
            GUILayout.BeginArea(new Rect(14, 40, 520, 238), GUI.skin.box);

            // Which round was drawn. Nothing acts on it yet -- every round in
            // the stock is a placeholder -- but having it on screen is how you
            // can see the draw and its repeat rarity actually working.
            var round = CoatRounds.Current;
            GUILayout.Label(round == null
                ? "round: <color=#ff5b5b>none drawn</color>"
                : $"round: <b>{round.Name}</b>"
                  + (round.Placeholder ? "  <color=#ffd24a>(placeholder, does nothing)</color>" : ""),
                _style);
            GUILayout.Space(4);

            for (int i = 0; i < 4; i++)
            {
                var c = Game.Characters[i];
                string state = c == null ? "<color=#ff5b5b>missing</color>"
                             : c.InCoat ? $"<color=#8ce87a>in the coat, {Where[i]}</color>"
                             : "<color=#ffd24a>out of the coat</color>";
                if (c != null && c.Holding) state += "  <color=#8ce87a>HOLDING</color>";

                GUILayout.Label($"<b>{Names[i]}</b>  ({Input.Describe(i)})   {state}", _style);
            }

            GUILayout.Space(4);
            GUILayout.Label($"in the coat: <b>{Coat.Count} of 4</b>"
                          + (Coat.FullyWorn ? "   <color=#8ce87a>passes for one person</color>"
                                            : "   <color=#ff5b5b>that is not one person</color>"), _style);
            GUILayout.Label("<i>walk with your direction keys  ·  last key climbs in and out of the coat</i>", _style);
            GUILayout.Label("<i>arms grab with their action key  ·  [ ] orbit camera</i>", _style);

            GUILayout.EndArea();

            DrawObserver();
        }

        void DrawObserver()
        {
            if (Observer == null) return;

            const float w = 520f;
            // Tracks the panel above: 40 + 238 + a gap.
            var box = new Rect(14f, 286f, w, 86f);
            GUI.Box(box, GUIContent.none);

            string watching = Observer.CanSee
                ? "<color=#ffd24a>WATCHING YOU</color>"
                : "<color=#8ce87a>looking elsewhere</color>";
            GUI.Label(new Rect(box.x + 10f, box.y + 6f, w - 20f, 22f), $"<b>OBSERVER</b>   {watching}", _style);

            var bar = new Rect(box.x + 10f, box.y + 34f, w - 20f, 16f);
            var old = GUI.color;

            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(bar, Px);

            GUI.color = Observer.Rumbled
                ? new Color(0.85f, 0.22f, 0.20f)
                : Color.Lerp(new Color(0.36f, 0.72f, 0.38f), new Color(0.88f, 0.28f, 0.22f), Observer.Suspicion);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(Observer.Suspicion), bar.height), Px);

            GUI.color = old;

            string tell = Observer.Rumbled ? "<color=#ff5b5b>RUMBLED — it knows</color>" : Observer.Tell;
            GUI.Label(new Rect(box.x + 10f, box.y + 54f, w - 20f, 22f),
                      $"suspicion {Observer.Suspicion:0.00}   ·   {tell}", _style);
        }
    }
}
