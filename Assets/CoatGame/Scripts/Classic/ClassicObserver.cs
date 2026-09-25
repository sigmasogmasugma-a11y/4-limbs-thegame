using UnityEngine;

namespace Coat.Classic
{
    /// The passer-by watching the original single body, deciding whether that is one
    /// person walking or four people arguing inside a coat.
    ///
    /// Every tell is read off physical state rather than off player input, so it keeps
    /// working when the input arrives over a network and it cannot be cheated by a
    /// client. If it looks wrong, it is wrong.
    public class ClassicObserver : MonoBehaviour
    {
        public ClassicRagdoll Body;
        public Transform Eye;
        public Renderer Skin;

        [Header("Eyesight")]
        public float ViewDistance = 9f;
        [Tooltip("Full width of the vision cone, in degrees.")]
        public float ViewAngle = 110f;
        public float TurnSpeed = 110f;

        [Header("Suspicion")]
        [Range(0f, 1f)] public float Suspicion;
        public float RiseRate = 0.55f;
        public float FallRate = 0.40f;
        public float SuspiciousAt = 0.45f;
        public float ResetAfter = 4f;

        public bool CanSee { get; private set; }
        public string Tell { get; private set; } = "-";
        public float TellStrength { get; private set; }
        public bool Rumbled { get; private set; }

        /// See CoatObserver.EverRumbled -- same signal, same reason. Rumbled
        /// self-clears after ResetAfter so the observer can start doubting you
        /// again; a round result needs to know it happened at all, not whether
        /// it is STILL happening right now.
        public bool EverRumbled { get; private set; }

        /// See CoatObserver.PeakSuspicion.
        public float PeakSuspicion { get; private set; }

        static readonly Color Calm = new Color(0.36f, 0.62f, 0.38f);
        static readonly Color Doubt = new Color(0.90f, 0.70f, 0.22f);
        static readonly Color Certain = new Color(0.85f, 0.22f, 0.20f);

        MaterialPropertyBlock _mpb;
        float _rumbledFor, _best;
        string _bestName;

        void Awake()
        {
            if (Eye == null) Eye = transform;
            _mpb = new MaterialPropertyBlock();
        }

        void FixedUpdate() => Tick(Time.fixedDeltaTime);

        public void Tick(float dt)
        {
            if (Body == null || Body.Pelvis == null) return;

            CanSee = Sees();
            if (CanSee) FaceTarget(dt);

            TellStrength = CanSee ? Evaluate() : 0f;
            Tell = CanSee ? (TellStrength > 0.02f ? _bestName : "nothing odd") : "not looking";

            if (Rumbled)
            {
                _rumbledFor += dt;
                if (_rumbledFor >= ResetAfter) { Rumbled = false; _rumbledFor = 0f; Suspicion = 0f; }
            }
            else
            {
                float delta = CanSee
                    ? TellStrength * RiseRate - (1f - TellStrength) * FallRate * 0.5f
                    : -FallRate;

                Suspicion = Mathf.Clamp01(Suspicion + delta * dt);
                if (Suspicion >= 1f) { Rumbled = true; EverRumbled = true; }
            }

            PeakSuspicion = Mathf.Max(PeakSuspicion, Suspicion);

            Paint();
        }

        /// See CoatObserver.ClearSuspicion -- resets the round-long history
        /// too, not just the live state, so a restarted round cannot inherit
        /// a bust from the attempt before it.
        public void ClearSuspicion()
        {
            Suspicion = 0f;
            Rumbled = false;
            _rumbledFor = 0f;
            Tell = "-";
            TellStrength = 0f;
            EverRumbled = false;
            PeakSuspicion = 0f;
        }

        // ---- the tells ----------------------------------------------------------

        void Consider(string name, float amount)
        {
            if (amount > _best) { _best = amount; _bestName = name; }
        }

        float Evaluate()
        {
            _best = 0f;
            _bestName = "-";

            Consider("ON THE FLOOR", Body.Collapsed ? 1f : 0f);

            // The most obvious thing in the world: that coat is short of a limb.
            Consider("THAT COAT HAS ONE LEG", Body.Legs == 1 ? 0.9f : 0f);
            Consider("THAT COAT HAS NO LEGS", Body.Legs == 0 ? 1f : 0f);
            Consider("AN EMPTY SLEEVE", Body.Arms < 2 ? 0.55f + 0.2f * (2 - Body.Arms) : 0f);

            float tilt = Vector3.Angle(Body.Pelvis.transform.up, Vector3.up);
            Consider("LURCHING", Mathf.InverseLerp(20f, 55f, tilt));
            Consider("TWITCHING", Mathf.InverseLerp(2.5f, 9f, Body.Pelvis.angularVelocity.magnitude));

            Consider("LEGS FIGHTING EACH OTHER", LegDisagreement());
            Consider("STANCE TOO WIDE", StanceWidth());
            Consider("BOTH FEET OFF THE GROUND", Body.Legs > 0 && Body.PlantedFeet == 0 ? 0.8f : 0f);
            Consider("ARMS FLAILING", ArmFlail());

            return Mathf.Clamp01(_best);
        }

        /// Two leg players who recently stepped in opposing directions. Read off the
        /// steps themselves, so a sloppy shuffle does not count but a tug of war does.
        float LegDisagreement()
        {
            var l = Body.LegL;
            var r = Body.LegR;
            if (l == null || r == null || !l.Present || !r.Present) return 0f;
            if (l.StepDirAge > Body.StepMemory || r.StepDirAge > Body.StepMemory) return 0f;

            float agree = Vector3.Dot(l.LastStepDir, r.LastStepDir);
            return Mathf.InverseLerp(0.3f, -0.8f, agree);
        }

        float StanceWidth()
        {
            var l = Body.LegL;
            var r = Body.LegR;
            if (l == null || r == null || !l.Present || !r.Present) return 0f;

            Vector3 gap = l.StepTarget - r.StepTarget;
            gap.y = 0f;
            return Mathf.InverseLerp(0.75f, 1.3f, gap.magnitude);
        }

        /// Hands whipping about at a speed no one idly swings an arm.
        float ArmFlail()
        {
            float worst = 0f;
            worst = Mathf.Max(worst, HandSpeed(Body.ArmL));
            worst = Mathf.Max(worst, HandSpeed(Body.ArmR));
            return worst;
        }

        float HandSpeed(ClassicLimb arm)
        {
            if (arm == null || arm.End == null || !arm.Present) return 0f;
            return Mathf.InverseLerp(2.2f, 6f, arm.End.linearVelocity.magnitude);
        }

        // ---- perception ---------------------------------------------------------

        Vector3 Centre => Body.Torso != null ? Body.Torso.position : Body.Pelvis.position;

        bool Sees() => VisibleFromEye(Centre);

        bool VisibleFromEye(Vector3 point)
        {
            Vector3 to = point - Eye.position;
            float dist = to.magnitude;
            if (dist > ViewDistance || dist < 0.01f) return false;
            if (Vector3.Angle(Eye.forward, to) > ViewAngle * 0.5f) return false;

            // Start clear of our own collider, and treat any world geometry as cover.
            Vector3 from = Eye.position + to / dist * 0.4f;
            return !Physics.Linecast(from, point, CoatLayers.NotRig, QueryTriggerInteraction.Ignore);
        }

        void FaceTarget(float dt)
        {
            Vector3 flat = Centre - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-4f) return;

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(flat.normalized, Vector3.up),
                TurnSpeed * dt);
        }

        void Paint()
        {
            if (Skin == null) return;

            // Created lazily: a script reload during play wipes this without re-running
            // Awake, and GetPropertyBlock throws on a null.
            _mpb ??= new MaterialPropertyBlock();

            Color c = Rumbled ? Certain
                    : Suspicion < SuspiciousAt
                        ? Color.Lerp(Calm, Doubt, Suspicion / Mathf.Max(0.01f, SuspiciousAt))
                        : Color.Lerp(Doubt, Certain, (Suspicion - SuspiciousAt) / Mathf.Max(0.01f, 1f - SuspiciousAt));

            Skin.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            Skin.SetPropertyBlock(_mpb);
        }
    }
}
