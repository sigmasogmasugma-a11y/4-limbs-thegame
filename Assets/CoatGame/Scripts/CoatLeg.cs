using UnityEngine;

namespace Coat
{
    /// One articulated leg: thigh, shin, foot, driven by two-bone IK into joint targets.
    /// This is what makes the walk read as stepping rather than a capsule being shoved
    /// around. Each of the four carries one, and the two at the bottom of the coat are
    /// the disguise's two legs.
    public class CoatLeg : MonoBehaviour
    {
        [Header("Bodies")]
        public Rigidbody Root;    // the character's torso, which the hip hangs off
        public Rigidbody Thigh;
        public Rigidbody Shin;
        public Rigidbody Foot;

        [Header("Joints")]
        public ConfigurableJoint Hip;
        public ConfigurableJoint Knee;

        [Header("Geometry, measured at build")]
        [Tooltip("Hip anchor in the torso's own local space, which is SCALED.")]
        public Vector3 HipLocal;
        public float ThighLen = 0.20f;
        public float ShinLen = 0.18f;
        [Tooltip("Which way the knee folds. Unity's AngleAxis is left handed, so this " +
                 "is +1 for a knee that bends forwards.")]
        public float BendSign = -1f;

        [Tooltip("Ankle height above the ground with the foot flat. The chain ends at " +
                 "the ankle, not at the foot's centre.")]
        public float AnkleHeight = 0.1f;
        [Tooltip("How high a foot may climb in one step. Also stops the ground check " +
                 "finding table tops above the foot and planting in mid air.")]
        public float StepUp = 0.35f;

        public float Span => ThighLen + ShinLen;
        public Vector3 HipWorld => Root.transform.TransformPoint(HipLocal);

        Quaternion _thighStart, _shinStart;

        void Awake()
        {
            _thighStart = Quaternion.Inverse(Root.rotation) * Thigh.rotation;
            _shinStart = Quaternion.Inverse(Thigh.rotation) * Shin.rotation;
        }

        /// The far tip of the shin: bones run along their own local +Y.
        public Vector3 AnkleNow => Shin.position + Shin.rotation * Vector3.up * (ShinLen * 0.5f);

        public void Solve(Vector3 target)
        {
            Vector3 origin = HipWorld;
            Vector3 to = target - origin;
            if (to.sqrMagnitude < 1e-6f) return;

            float d = Mathf.Clamp(to.magnitude, 0.06f, Span - 0.012f);
            Vector3 dir = to.normalized;

            // Knees only fold one way, across the body.
            Vector3 hinge = Vector3.ProjectOnPlane(Root.transform.right, dir);
            if (hinge.sqrMagnitude < 1e-6f) hinge = Vector3.ProjectOnPlane(Root.transform.forward, dir);
            hinge.Normalize();

            float cos = Mathf.Clamp((ThighLen * ThighLen + d * d - ShinLen * ShinLen) / (2f * ThighLen * d), -1f, 1f);
            float lift = Mathf.Acos(cos) * Mathf.Rad2Deg;

            Vector3 thighDir = Quaternion.AngleAxis(lift * BendSign, hinge) * dir;
            Vector3 knee = origin + thighDir * ThighLen;
            Vector3 shinDir = (target - knee).normalized;
            if (shinDir.sqrMagnitude < 1e-6f) return;

            // Bones are built with their local +Y running down the bone.
            Quaternion thighWorld = Quaternion.LookRotation(hinge, thighDir);
            Quaternion shinWorld = Quaternion.LookRotation(hinge, shinDir);

            Hip.SetTargetRotationLocal(Quaternion.Inverse(Root.rotation) * thighWorld, _thighStart);
            Knee.SetTargetRotationLocal(Quaternion.Inverse(thighWorld) * shinWorld, _shinStart);
        }

        /// Starts just above the foot rather than high overhead, so a foot passing under
        /// a table does not find the TABLE as its ground and plant in mid air.
        public float Ground(Vector3 p)
        {
            Vector3 from = new Vector3(p.x, p.y + StepUp, p.z);
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, StepUp + 2f,
                                CoatLayers.NotRig, QueryTriggerInteraction.Ignore))
                return hit.point.y;

            return 0f;
        }
    }
}
