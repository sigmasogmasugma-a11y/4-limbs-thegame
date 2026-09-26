using UnityEngine;

namespace Coat
{
    /// Somebody who is looking at you, and is starting to have doubts about how many
    /// people they are looking at.
    ///
    /// Every tell is read off physical state rather than off player input, so it keeps
    /// working when the input arrives over a network and it cannot be cheated by a
    /// client. If it looks wrong, it is wrong.
    public class CoatObserver : MonoBehaviour
    {
        public TheCoat Coat;
        public CoatCharacter[] Characters = new CoatCharacter[0];
        public Transform Eye;
        public Renderer Skin;

        /// Fusion evaluates the observer on State Authority only. Proxies receive
        /// the replicated suspicion result instead of running their own perception.
        public bool ExternalSimulation;

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

        /// Whether this HAS EVER rumbled since the last ClearSuspicion(), as
        /// opposed to whether it is rumbled RIGHT NOW.
        ///
        /// Rumbled self-clears after ResetAfter seconds by design, so the
        /// observer can start doubting you again rather than staying tripped
        /// forever. A round result must not read Rumbled directly for exactly
        /// that reason -- gate the round on the live flag and getting caught,
        /// then simply outlasting the clock, reads as a clean getaway. This is
        /// the separate, non-clearing signal a round outcome should read.
        public bool EverRumbled { get; private set; }

        /// The worst it got, even on a run that stayed clean. Two runs that
        /// both got away are not the same run: one the observer never looked
        /// twice at, the other got to 0.97 and cooled off in the last second.
        /// A payout that cannot tell them apart is not measuring the thing the
        /// player was actually doing.
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

        void FixedUpdate()
        {
            if (ExternalSimulation) return;
            Tick(Time.fixedDeltaTime);
        }

        public void Tick(float dt)
        {
            if (Coat == null) return;

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

        /// Apply the authoritative observer result on a client. Same as
        /// ClassicObserver.ApplyNetworkState: the round-long history is kept in
        /// step so the client sees the same run the host judged.
        public void ApplyNetworkState(float suspicion, bool rumbled, string tell, float tellStrength, bool canSee)
        {
            Suspicion = Mathf.Clamp01(suspicion);
            Rumbled = rumbled;
            if (rumbled) EverRumbled = true;
            PeakSuspicion = Mathf.Max(PeakSuspicion, Suspicion);
            Tell = string.IsNullOrEmpty(tell) ? "-" : tell;
            TellStrength = Mathf.Clamp01(tellStrength);
            CanSee = canSee;
            Paint();
        }

        /// Resets the live suspicion state AND the round-long history
        /// (EverRumbled, PeakSuspicion). The two used to only cover the live
        /// state; a round restarted after a bust carried the bust into the
        /// next attempt, since nothing had ever cleared it.
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

            // The whole point of the disguise: anyone walking around loose is a person
            // too many, and that is far worse than a clumsy walk.
            Consider("THAT IS SEVERAL PEOPLE", LoosePersonInView());
            Consider("THE COAT IS EMPTY", Coat.Count == 0 ? 0.35f : 0f);

            Consider("LEGS FIGHTING EACH OTHER", Coat.LegDisagreement);
            Consider("COMING APART", ComingApart());

            if (Coat.Hub != null)
            {
                float tilt = Vector3.Angle(Coat.Hub.transform.up, Vector3.up);
                Consider("LURCHING", Mathf.InverseLerp(20f, 55f, tilt));
                Consider("TWITCHING", Mathf.InverseLerp(2.5f, 9f, Coat.Hub.angularVelocity.magnitude));
            }

            Consider("STANCE TOO WIDE", StanceWidth());

            return Mathf.Clamp01(_best);
        }

        /// Someone visibly outside the coat while the observer is watching.
        float LoosePersonInView()
        {
            float worst = 0f;
            foreach (var c in Characters)
            {
                if (c == null || c.InCoat) continue;
                if (!VisibleFromEye(c.Body.position)) continue;

                // The closer the stray is to the coat, the more obviously they belong to it.
                float near = 1f - Mathf.Clamp01(Vector3.Distance(c.Body.position, Coat.Centre) / 6f);
                worst = Mathf.Max(worst, 0.7f + 0.3f * near);
            }
            return worst;
        }

        /// Wearers straining away from where they ought to be sitting.
        float ComingApart() =>
            Mathf.InverseLerp(Coat.Slack * 1.6f, Coat.Slack * 5f, Coat.Strain);

        float StanceWidth()
        {
            var l = Coat.WearerAt(CoatRole.LeftLeg);
            var r = Coat.WearerAt(CoatRole.RightLeg);
            if (l == null || r == null) return 0f;

            return Mathf.InverseLerp(0.75f, 1.3f, Vector3.Distance(l.Body.position, r.Body.position));
        }

        // ---- perception ---------------------------------------------------------

        bool Sees() => VisibleFromEye(Coat.Centre);

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
            Vector3 flat = Coat.Centre - transform.position;
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
