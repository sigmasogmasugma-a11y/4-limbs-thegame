using UnityEngine;
using UnityEngine.InputSystem;

namespace Coat
{
    [System.Serializable]
    public struct KeyCluster
    {
        public Key Up, Down, Left, Right, Action, Coat;

        public string Describe() =>
            $"{Short(Up)}{Short(Left)}{Short(Down)}{Short(Right)} + {Short(Action)} / {Short(Coat)}";

        static string Short(Key k)
        {
            switch (k)
            {
                case Key.Semicolon: return ";";
                case Key.Quote: return "'";
                case Key.Slash: return "/";
                case Key.Backslash: return "\\";
                case Key.Comma: return ",";
                case Key.Period: return ".";
                case Key.LeftBracket: return "[";
                case Key.RightBracket: return "]";
                case Key.LeftShift: return "LShift";
                case Key.RightShift: return "RShift";
                case Key.Space: return "Space";
            }

            string s = k.ToString();
            if (s.StartsWith("Numpad")) return "#" + s.Substring(6);
            if (s.StartsWith("Digit")) return s.Substring(5);
            return s;
        }
    }

    /// Local-only input for four players on one keyboard, for feel testing before any
    /// netcode exists. Gamepad N drives player N when plugged in, otherwise the keyboard
    /// cluster for that player does.
    ///
    /// This file is named after the class on purpose: Unity will not attach a
    /// MonoBehaviour whose class name does not match its file name, and it fails
    /// silently when you try.
    public class LocalCoatInput : MonoBehaviour
    {
        public CoatInputState[] States = new CoatInputState[4];
        public bool[] UsingGamepad = new bool[4];

        [Tooltip("Keyboard bindings per player, in CoatRole order: LeftLeg, RightLeg, " +
                 "LeftArm, RightArm. Edit these to match your own keyboard.")]
        public KeyCluster[] Clusters = Defaults();

        /// No arrow keys and no numpad, so this works on a laptop. The last key in each
        /// cluster climbs into or out of the coat.
        public static KeyCluster[] Defaults() => new[]
        {
            new KeyCluster { Up = Key.W, Left = Key.A, Down = Key.S, Right = Key.D, Action = Key.LeftShift, Coat = Key.Q },
            new KeyCluster { Up = Key.I, Left = Key.J, Down = Key.K, Right = Key.L, Action = Key.U, Coat = Key.O },
            new KeyCluster { Up = Key.T, Left = Key.F, Down = Key.G, Right = Key.H, Action = Key.R, Coat = Key.B },
            new KeyCluster { Up = Key.P, Left = Key.Semicolon, Down = Key.Slash, Right = Key.Quote, Action = Key.RightShift, Coat = Key.M },
        };

        [Header("Testing")]
        [Tooltip("Solo testing aid: the right leg copies the left leg. Toggle in game " +
                 "with the key below. No place in a real four player session.")]
        public bool MirrorLegs;
        public Key MirrorToggle = Key.Backslash;

        readonly bool[] _prevAction = new bool[4];
        readonly bool[] _prevCoat = new bool[4];

        void Awake()
        {
            if (States == null || States.Length != 4) States = new CoatInputState[4];
            if (UsingGamepad == null || UsingGamepad.Length != 4) UsingGamepad = new bool[4];
            if (Clusters == null || Clusters.Length != 4) Clusters = Defaults();
        }

        void Update()
        {
            Sample();
        }

        /// Reads the devices into States. Public so a test harness can exercise the real
        /// key mapping instead of writing into States directly and never finding out
        /// whether the bindings themselves are wrong.
        public void Sample()
        {
            var pads = Gamepad.all;
            var kb = Keyboard.current;

            for (int i = 0; i < 4; i++)
            {
                Vector2 move = Vector2.zero;
                bool action = false, coat = false;

                if (i < pads.Count)
                {
                    var pad = pads[i];
                    move = pad.leftStick.ReadValue();
                    action = pad.buttonSouth.isPressed || pad.rightTrigger.ReadValue() > 0.4f;
                    coat = pad.buttonWest.isPressed;
                    UsingGamepad[i] = true;
                }
                else if (kb != null)
                {
                    var c = Clusters[i];
                    if (kb[c.Up].isPressed) move.y += 1f;
                    if (kb[c.Down].isPressed) move.y -= 1f;
                    if (kb[c.Left].isPressed) move.x -= 1f;
                    if (kb[c.Right].isPressed) move.x += 1f;
                    move = Vector2.ClampMagnitude(move, 1f);
                    action = kb[c.Action].isPressed;
                    coat = kb[c.Coat].isPressed;
                    UsingGamepad[i] = false;
                }

                States[i].Move = move;
                States[i].ActionDown = action && !_prevAction[i];
                States[i].Action = action;
                States[i].CoatDown = coat && !_prevCoat[i];
                States[i].Coat = coat;
                _prevAction[i] = action;
                _prevCoat[i] = coat;
            }

            if (kb != null && kb[MirrorToggle].wasPressedThisFrame) MirrorLegs = !MirrorLegs;

            if (MirrorLegs)
            {
                States[(int)CoatRole.RightLeg] = States[(int)CoatRole.LeftLeg];
                UsingGamepad[(int)CoatRole.RightLeg] = UsingGamepad[(int)CoatRole.LeftLeg];
            }
        }

        public CoatInputState Get(CoatRole role) => States[(int)role];

        /// What the HUD prints, read from the live bindings so the two cannot drift apart.
        public string Describe(int role)
        {
            if (UsingGamepad[role]) return "pad " + role;
            if (Clusters == null || role >= Clusters.Length) return "unbound";
            return Clusters[role].Describe();
        }
    }
}
