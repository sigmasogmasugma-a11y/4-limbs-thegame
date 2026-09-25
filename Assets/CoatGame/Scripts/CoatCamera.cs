using UnityEngine;

namespace Coat
{
    /// Frames all four of them, whether they are in the coat or wandering off. Pulls
    /// back as they spread out so a straggler never goes off screen unnoticed.
    ///
    /// Deliberately world-fixed rather than bolted to anyone's facing: every player
    /// needs "stick up" to mean the same direction at the same time.
    public class CoatCamera : MonoBehaviour
    {
        public CoatGame Game;
        public Transform Fallback;

        public Vector3 Offset = new Vector3(0f, 3.0f, -6.5f);
        public float Smooth = 4f;
        public float Orbit = 0f;
        [Tooltip("Extra distance per metre the group is spread out.")]
        public float SpreadPullback = 0.9f;
        public float MaxPullback = 6f;

        public Vector3 Focus { get; private set; }
        public float Spread { get; private set; }

        void LateUpdate() => Step(Time.deltaTime);

        /// Public so a test harness can drive the camera in step with the physics.
        /// The controls are camera relative, so a camera that is not being simulated
        /// makes a walk test lie about which way the body turns.
        public void Step(float dt)
        {
            Measure();

            Quaternion spin = Quaternion.Euler(0f, Orbit, 0f);
            Vector3 back = spin * Offset.normalized * (Offset.magnitude + Mathf.Min(Spread * SpreadPullback, MaxPullback));
            Vector3 want = Focus + back;

            transform.position = Vector3.Lerp(transform.position, want, 1f - Mathf.Exp(-Smooth * dt));
            transform.rotation = Quaternion.LookRotation((Focus + Vector3.up * 0.3f) - transform.position, Vector3.up);
        }

        void Measure()
        {
            if (Game == null || Game.Characters == null || Game.Characters.Length == 0)
            {
                Focus = Fallback != null ? Fallback.position : Focus;
                Spread = 0f;
                return;
            }

            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var c in Game.Characters)
            {
                if (c == null || c.Body == null) continue;
                sum += c.Body.position;
                n++;
            }
            if (n == 0) return;

            Focus = sum / n;

            float worst = 0f;
            foreach (var c in Game.Characters)
            {
                if (c == null || c.Body == null) continue;
                worst = Mathf.Max(worst, Vector3.Distance(c.Body.position, Focus));
            }
            Spread = worst;
        }
    }
}
