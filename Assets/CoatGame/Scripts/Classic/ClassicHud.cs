using UnityEngine;
using UnityEngine.InputSystem;

namespace Coat.Classic
{
    /// Prototype readout for the original rig: which player owns which limb, how close
    /// the body is to going over, and how convinced the passer-by is.
    public class ClassicHud : MonoBehaviour
    {
        public ClassicRagdoll Body;
        public LocalCoatInput Input;
        public CoatCamera Cam;
        public ClassicObserver Observer;
        public float OrbitSpeed = 90f;

        static readonly string[] Names = { "LEFT LEG ", "RIGHT LEG", "LEFT ARM ", "RIGHT ARM" };

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

            // Put the camera back square behind the body, for when it has been nudged.
            if (kb[Key.Backspace].wasPressedThisFrame) Cam.Orbit = 0f;

            if (kb[Key.H].wasPressedThisFrame && Body != null)
            {
                var arrow = Body.GetComponent<ClassicHeadingArrow>();
                if (arrow != null) arrow.Show = !arrow.Show;
            }
        }

        void OnGUI()
        {
            if (Body == null || Input == null) return;

            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };

            GUILayout.BeginArea(new Rect(14, 14, 520, 190), GUI.skin.box);

            for (int i = 0; i < 4; i++)
            {
                var limb = Limb(i);
                string state = limb == null ? "<color=#ff5b5b>unwired</color>"
                             : !limb.Present ? "<color=#8a8a8a>nobody in this sleeve</color>"
                             : Describe(limb);
                GUILayout.Label($"<b>{Names[i]}</b>  ({Input.Describe(i)})   {state}", _style);
            }

            GUILayout.Space(4);

            string stance = Body.Legs == 0 ? "<color=#ff5b5b>NO LEGS, GOING NOWHERE</color>"
                          : Body.Collapsed ? "<color=#ff5b5b>ON THE FLOOR</color>"
                          : Body.Recovering ? "<color=#ffd24a>getting up</color>"
                          : Body.PlantedFeet == 0 ? "<color=#ff5b5b>both feet off the ground</color>"
                          : Body.PlantedFeet == 1 ? "<color=#ffd24a>on one foot</color>"
                          : "<color=#8ce87a>both feet down</color>";

            string turn = Body.NextLeg == ClassicRagdoll.Leg.Left ? "left leg's turn"
                        : Body.NextLeg == ClassicRagdoll.Leg.Right ? "right leg's turn"
                        : "either leg may step";

            string crew = Body.Legs == 2 && Body.Arms == 2
                ? "<color=#8ce87a>full crew</color>"
                : $"<color=#ffd24a>{Body.Legs} legs, {Body.Arms} arms</color>";

            GUILayout.Label($"balance <b>{Body.Stability:0.00}</b>   {stance}   {crew}", _style);
            GUILayout.Label($"<i>{turn}</i>", _style);

            // Which way the body thinks it is pointing, and whether the camera has been
            // swung round. The controls are camera relative, so an orbited camera turns
            // "left" into some other direction entirely without ever saying so.
            float bearing = Bearing(Body.Heading);
            float orbit = Cam != null ? Mathf.Repeat(Cam.Orbit, 360f) : 0f;
            string orbitNote = Mathf.Abs(Mathf.DeltaAngle(orbit, 0f)) < 1f
                ? "<color=#8ce87a>camera straight on</color>"
                : $"<color=#ff5b5b>camera orbited {orbit:0} deg -- your left is not the world's left</color>";

            GUILayout.Label($"facing <b>{bearing:0}</b> deg   {orbitNote}", _style);
            GUILayout.Label("<i>green arrow = where it is steering, orange = where it points  " +
                            "·  H hides them  ·  Backspace re-centres the camera</i>", _style);
            GUILayout.Label("<i>legs step on a fresh key press, and must take turns  ·  action key braces</i>", _style);
            GUILayout.Label("<i>arms steer with their keys and grab with action  ·  [ ] orbit camera</i>", _style);

            GUILayout.EndArea();

            DrawObserver();
        }

        /// 0 is along +z, +90 is +x. Just a compass reading for the readout.
        static float Bearing(Vector3 v)
        {
            Vector3 f = Vector3.ProjectOnPlane(v, Vector3.up);
            return f.sqrMagnitude < 1e-5f ? 0f : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }

        string Describe(ClassicLimb limb)
        {
            if (!limb.IsLeg)
                return limb.Holding ? "<color=#8ce87a>HOLDING</color>" : "<color=#ffd24a>empty handed</color>";

            if (limb.Airborne) return "<color=#ffd24a>mid step</color>";
            if (limb.Bracing) return "<color=#8ce87a>braced</color>";
            return "<color=#8ce87a>planted</color>";
        }

        ClassicLimb Limb(int i) => i switch
        {
            0 => Body.LegL,
            1 => Body.LegR,
            2 => Body.ArmL,
            _ => Body.ArmR,
        };

        void DrawObserver()
        {
            if (Observer == null) return;

            const float w = 520f;
            var box = new Rect(14f, 212f, w, 86f);
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
