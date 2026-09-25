using UnityEngine;

namespace Coat
{
    /// One of the four. A small person on one articulated leg, with a spine that bends,
    /// balance that can run out, and the ability to go down and haul themselves back up.
    ///
    /// The precariousness is the point. An earlier version held everyone upright with an
    /// absolute torque and no way to fall, and it read as a stiff pole: technically
    /// walking, but never struggling, never catching itself, never going over.
    public class CoatCharacter : MonoBehaviour
    {
        public CoatRole Role;
        [Tooltip("The hips. This is what gets stood up over the foot.")]
        public Rigidbody Body;
        [Tooltip("Jointed above the hips on a springy drive, so it bends and lags.")]
        public Rigidbody Torso;
        public Rigidbody Head;
        public Rigidbody HandL;
        public Rigidbody HandR;
        public CoatLeg Leg;
        [Tooltip("The other leg. Null falls back to the old one-legged rig, where " +
                 "two crew side by side under the coat were the disguise's pair.")]
        public CoatLeg LegR;

        [Header("Stance")]
        public float StandHeight = 0.46f;
        public float StandSpring = 700f;
        public float StandDamp = 50f;
        public float MaxStandError = 0.3f;
        [Range(0f, 1f)] public float MinTrack = 0.5f;
        [Range(0f, 1f)] public float HipAssist = 0.7f;
        public float MaxRiseSpeed = 2.5f;

        [Header("Balance")]
        [Tooltip("Has to out-argue the spine drive and the weight of everything above the " +
                 "hips, so it wants to be large. At a few hundred they simply fold over " +
                 "and lie on the floor.")]
        public float UprightTorque = 1600f;
        public float UprightDamp = 110f;
        [Tooltip("How far the hips may drift off the foot before balance is gone entirely.")]
        public float ToppleRadius = 0.42f;
        [Tooltip("Floor on self-righting, so a stumble can still be saved.")]
        [Range(0f, 1f)] public float MinUpright = 0.32f;

        [Header("Going down and getting up")]
        public float HeadMinHeight = 0.58f;
        [Tooltip("Head must stay low this long before giving up, so one stumble does not " +
                 "dump you on the floor.")]
        public float CollapseGrace = 0.35f;
        public float CollapseSeconds = 1.2f;
        public float RecoverSeconds = 1.3f;
        public float RecoverBoost = 1.4f;

        [Header("Stepping")]
        [Tooltip("In the coat, step only on a fresh push. Alone, hold to keep stepping.")]
        public bool PressOnlyInCoat = true;
        public float StepLength = 0.3f;
        public float StepTime = 0.32f;
        [Tooltip("Dead time after a step lands. Zero, to match the disguise: the " +
                 "cadence is StepTime alone. Outside the coat a child already " +
                 "steps on a HELD key -- pressOnly is InCoat && PressOnlyInCoat " +
                 "-- so this was the only thing left making the walk stutter.")]
        public float StepCooldown = 0f;
        public float StepLift = 0.18f;
        public float MaxReach = 0.3f;
        public float MaxStride = 0.62f;
        public float TurnRate = 300f;

        [Header("Riding")]
        public float SeatSpring = 240f;
        public float SeatDamp = 26f;
        public float MaxSeatError = 0.5f;

        [Header("Arms")]
        public float ArmForward = 0.3f;
        public float ArmSpread = 0.18f;
        public float ArmSpan = 0.3f;
        public float AimSpeed = 2.2f;
        public float ArmSpring = 320f;
        public float ArmDamp = 28f;
        public float GrabRadius = 0.2f;
        [Tooltip("How far the hands trail behind the body as it moves. Dead arms were " +
                 "most of what made the walk look lifeless.")]
        public float SwingLag = 0.12f;
        [Tooltip("How far the arms counter-swing through a step.")]
        public float SwingThrough = 0.16f;

        public TheCoat Coat { get; private set; }
        public bool InCoat => Coat != null;
        public bool IsLegRole => Role == CoatRole.LeftLeg || Role == CoatRole.RightLeg;
        /// Aboard a vehicle coat, this one is switched off entirely and the shared body
        /// wears their limb instead, so there is nothing here left to drive.
        public bool Waiting => Coat != null && !Coat.Assemble;
        public bool Holding => _grabL != null || _grabR != null;
        /// How many of this person's own two hands are on a given body.
        public int HandsOn(Rigidbody rb)
        {
            if (rb == null) return 0;
            int n = 0;
            if (_grabL != null && _grabL.connectedBody == rb) n++;
            if (_grabR != null && _grabR.connectedBody == rb) n++;
            return n;
        }
        public Vector3 Facing => _facing;

        public bool Airborne
        {
            get
            {
                foreach (var f in Feet) if (f.Airborne) return true;
                return false;
            }
        }
        public bool JustStepped { get; private set; }
        public Vector3 Stance => Support;
        public Vector3 FootTarget => Support;

        /// Set by the coat once both legs are in. The pair then shares ONE balance
        /// instead of each person keeping themselves up, which is the whole difference
        /// between two tied-together people and one body that can teeter.
        public bool SharedBalance { get; set; }

        /// 1 is balanced, 0 is about to go over. Visible so the HUD can show the struggle.
        public float Stability { get; private set; }
        public bool Collapsed => _collapse > 0f;
        public bool Recovering => _recover > 0f;

        Vector2 _aim;
        Vector3 _facing = Vector3.forward;
        FixedJoint _grabL, _grabR;

        /// One leg's worth of stepping. There are two of these now, and the
        /// body stands over the point between them.
        class Foot
        {
            public CoatLeg Leg;
            public float StepT, Cool;
            public Vector3 From, To;
            public bool Stepped;
            public bool Airborne => StepT > 0f;
        }

        Foot[] _feet;
        bool _rightNext;
        bool _wasPushing;

        Foot[] Feet
        {
            get
            {
                if (_feet == null)
                    _feet = LegR != null
                          ? new[] { new Foot { Leg = Leg }, new Foot { Leg = LegR } }
                          : new[] { new Foot { Leg = Leg } };
                return _feet;
            }
        }

        /// The point the body is standing over: between the feet with two legs,
        /// on the one foot with one.
        Vector3 Support
        {
            get
            {
                Vector3 sum = Vector3.zero;
                foreach (var f in Feet) sum += f.To;
                return sum / Feet.Length;
            }
        }

        float _collapse, _recover, _lowTimer;

        void Awake()
        {
            Vector3 fwd = Vector3.ProjectOnPlane(Body.transform.forward, Vector3.up);
            _facing = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            foreach (var foot in Feet)
                foot.To = foot.From = foot.Leg != null ? foot.Leg.AnkleNow : Body.position;
            Stability = 1f;
        }

        public void SetCoat(TheCoat coat)
        {
            Coat = coat;
            foreach (var f in Feet)
            {
                f.StepT = 0f;
                f.Cool = 0f;
                if (f.Leg != null) f.To = f.From = f.Leg.AnkleNow;
            }
            _wasPushing = false;
            SetHeadVisible(true);
        }

        public void Tick(CoatInputState input, float camYaw, float dt,
                         bool mayStep = true, CoatCharacter partner = null)
        {
            // Down. Nothing holds you up; you are a ragdoll until the timer runs out.
            if (_collapse > 0f)
            {
                _collapse -= dt;
                Stability = 0f;
                if (_collapse <= 0f) BeginRecover();
                foreach (var f in Feet) if (f.Leg != null) f.Leg.Solve(f.To);
                Arms(Vector2.zero, false, camYaw, dt);
                return;
            }

            // Balance fades back in over the recovery, so getting up is a heave rather
            // than the body snapping upright like a cardboard cutout.
            float ramp = 1f;
            if (_recover > 0f)
            {
                _recover -= dt;
                ramp = Mathf.Clamp01(1f - _recover / Mathf.Max(0.01f, RecoverSeconds));
            }

            bool riding = InCoat && !IsLegRole;

            if (riding)
            {
                RideShoulders();
                Dangle();
                Stability = 1f;
            }
            else
            {
                Step(Waiting ? Vector2.zero : input.Move, camYaw, dt, mayStep, partner);
                // When the coat is driving a shared balance it does the standing and the
                // righting for the pair; this character only places its own foot.
                if (!SharedBalance) Stand(dt, ramp);
            }

            Arms(riding ? input.Move : Vector2.zero, input.Action, camYaw, dt);

            // Only give up after being down a sustained moment, and never mid-recovery,
            // or the head dipping during the heave re-collapses you forever.
            if (!riding && _recover <= 0f && Head.position.y < HeadMinHeight) _lowTimer += dt;
            else _lowTimer = 0f;

            if (_lowTimer > CollapseGrace) Collapse();
        }

        public void Collapse()
        {
            if (_collapse > 0f) return;
            _collapse = CollapseSeconds;
            _recover = 0f;
            _lowTimer = 0f;
            Stability = 0f;
        }

        void BeginRecover()
        {
            _recover = RecoverSeconds;
            _lowTimer = 0f;

            // Put the foot back underneath wherever you ended up, so the stand
            // controller has something to push against on the way up.
            if (Leg == null) return;
            foreach (var f in Feet)
            {
                if (f.Leg == null) continue;
                Vector3 p = Body.position + Vector3.right * 0f;
                p.y = f.Leg.Ground(p) + f.Leg.AnkleHeight;
                f.To = f.From = p;
                f.StepT = 0f;
            }
        }

        // ---- stepping --------------------------------------------------------

        void Step(Vector2 move, float camYaw, float dt, bool mayStep, CoatCharacter partner)
        {
            JustStepped = false;

            bool pushing = move.sqrMagnitude > 0.04f;
            bool pressOnly = InCoat && PressOnlyInCoat;
            bool fresh = pushing && (!pressOnly || !_wasPushing);
            _wasPushing = pushing;

            if (pushing)
            {
                Vector3 dir = Quaternion.Euler(0f, camYaw, 0f) * new Vector3(move.x, 0f, move.y);
                if (dir.sqrMagnitude > 1e-4f)
                    _facing = Vector3.RotateTowards(_facing, dir.normalized, TurnRate * Mathf.Deg2Rad * dt, 0f);
            }

            var feet = Feet;

            // Carry every foot through whatever it is already doing first, so
            // the decision below sees this tick's truth.
            bool anyAirborne = false;
            foreach (var f in feet)
            {
                if (f.Cool > 0f) f.Cool -= dt;
                if (f.StepT <= 0f) continue;

                f.StepT -= dt;
                float u = 1f - Mathf.Clamp01(f.StepT / Mathf.Max(0.01f, StepTime));
                Vector3 p = Vector3.Lerp(f.From, f.To, u);
                p.y += Mathf.Sin(u * Mathf.PI) * StepLift;
                if (f.Leg != null) f.Leg.Solve(p);

                if (f.StepT <= 0f) f.Cool = StepCooldown;
                else anyAirborne = true;
            }

            // Anything still on the ground holds its mark.
            foreach (var f in feet)
                if (!f.Airborne && f.Leg != null) f.Leg.Solve(f.To);

            // ONE foot at a time, and they take turns. Without the turn taking
            // both legs step together on a held key and it hops; without the
            // one-at-a-time rule there is a moment with nothing on the floor.
            if (!fresh || !mayStep || anyAirborne) return;

            int want = feet.Length > 1 && _rightNext ? 1 : 0;
            var foot = feet[want];
            if (foot.Cool > 0f)
            {
                if (feet.Length < 2) return;
                foot = feet[1 - want];              // the other one is ready
                if (foot.Cool > 0f) return;
                want = 1 - want;
            }

            if (BeginStep(foot, move, camYaw, partner))
            {
                JustStepped = true;
                if (feet.Length > 1) _rightNext = want == 0;
            }
        }

        bool BeginStep(Foot foot, Vector2 move, float camYaw, CoatCharacter partner)
        {
            if (foot.Leg == null) return false;

            Vector3 dir = Quaternion.Euler(0f, camYaw, 0f) * new Vector3(move.x, 0f, move.y);
            if (dir.sqrMagnitude < 1e-4f) return false;
            dir.Normalize();

            Vector3 from = foot.To;
            Vector3 to = from + dir * StepLength;

            // Reach is measured from THIS leg's own hip, which is what keeps the
            // two feet on their own sides instead of both drifting to the middle.
            Vector3 hip = foot.Leg.HipWorld;
            Vector3 flat = to - hip;
            flat.y = 0f;
            if (flat.magnitude > MaxReach) flat = flat.normalized * MaxReach;
            to = hip + flat;

            if (InCoat && partner != null)
            {
                Vector3 stride = to - partner.Stance;
                stride.y = 0f;
                if (stride.magnitude > MaxStride)
                    to = partner.Stance + stride.normalized * MaxStride;
            }

            to.y = foot.Leg.Ground(to) + foot.Leg.AnkleHeight;

            foot.From = from;
            foot.To = to;
            foot.StepT = StepTime;
            return true;
        }

        // ---- standing --------------------------------------------------------

        void Stand(float dt, float ramp)
        {
            // How far the hips have drifted off the foot. This is the whole feel: lean a
            // little and you wobble, lean past the topple radius and you are going over.
            Vector3 lean = Body.position - Support;
            lean.y = 0f;
            Stability = Mathf.Clamp01(1f - lean.magnitude / Mathf.Max(0.01f, ToppleRadius));

            float authority = Stability * ramp;
            float lift = _recover > 0f ? RecoverBoost : 1f;
            float track = Mathf.Max(authority, MinTrack * ramp);

            Vector3 over = Support;
            Vector3 want = new Vector3(over.x, GroundUnderBody() + StandHeight, over.z);
            Vector3 err = Vector3.ClampMagnitude(want - Body.position, MaxStandError);
            err.x *= track;
            err.z *= track;

            Body.AddForce(err * (StandSpring * lift) - Body.linearVelocity * StandDamp, ForceMode.Acceleration);
            Body.AddForce(Vector3.up * (9.81f * HipAssist * Mathf.Max(authority, 0.4f) * lift), ForceMode.Acceleration);

            if (Body.linearVelocity.y > MaxRiseSpeed)
                Body.AddForce(Vector3.down * ((Body.linearVelocity.y - MaxRiseSpeed) * 14f), ForceMode.Acceleration);

            float rise = Mathf.Max(authority, MinUpright * ramp);
            Quaternion desired = Quaternion.LookRotation(_facing, Vector3.up);
            Upright(Body, desired, rise);
            if (Torso != null) Upright(Torso, desired, rise * 0.6f);
        }

        /// Stand this character up, but on numbers the coat worked out for the pair.
        public void DriveStand(Vector3 want, float track, float assist, float lift)
        {
            Vector3 err = Vector3.ClampMagnitude(want - Body.position, MaxStandError);
            err.x *= track;
            err.z *= track;

            Body.AddForce(err * (StandSpring * lift) - Body.linearVelocity * StandDamp, ForceMode.Acceleration);
            Body.AddForce(Vector3.up * (9.81f * HipAssist * assist * lift), ForceMode.Acceleration);

            if (Body.linearVelocity.y > MaxRiseSpeed)
                Body.AddForce(Vector3.down * ((Body.linearVelocity.y - MaxRiseSpeed) * 14f), ForceMode.Acceleration);
        }

        /// Right this character towards a heading the coat chose for everyone.
        public void DriveUpright(Quaternion desired, float strength)
        {
            Upright(Body, desired, strength);
            if (Torso != null) Upright(Torso, desired, strength * 0.6f);
        }

        public void SetStability(float s) => Stability = s;

        public float GroundUnderBody()
        {
            if (Physics.Raycast(Body.position, Vector3.down, out RaycastHit hit, 6f,
                                CoatLayers.NotRig, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return 0f;
        }

        void Upright(Rigidbody rb, Quaternion desired, float strength)
        {
            Quaternion delta = desired * Quaternion.Inverse(rb.rotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (axis.sqrMagnitude < 1e-6f || float.IsNaN(axis.x) || float.IsInfinity(axis.x)) return;

            Vector3 torque = axis.normalized * (angle * Mathf.Deg2Rad * UprightTorque * strength)
                           - rb.angularVelocity * UprightDamp;
            rb.AddTorque(torque, ForceMode.Acceleration);
        }

        void Dangle()
        {
            foreach (var f in Feet)
            {
                if (f.Leg == null) continue;
                f.To = Body.position + Vector3.down * (StandHeight * 0.9f);
                f.Leg.Solve(f.To);
            }
        }

        void RideShoulders()
        {
            if (Coat == null) return;

            Vector3 seat = Coat.SeatFor(Role);
            if (seat == Vector3.zero) return;

            Vector3 err = Vector3.ClampMagnitude(seat - Body.position, MaxSeatError);
            Body.AddForce(err * SeatSpring - Body.linearVelocity * SeatDamp, ForceMode.Acceleration);
        }

        // ---- arms ------------------------------------------------------------

        void Arms(Vector2 move, bool grab, float camYaw, float dt)
        {
            _aim.x = Mathf.Clamp(_aim.x + move.x * AimSpeed * dt, -1f, 1f);
            _aim.y = Mathf.Clamp(_aim.y + move.y * AimSpeed * dt, -1f, 1f);

            var anchor = Torso != null ? Torso : Body;
            Quaternion frame = Quaternion.LookRotation(_facing, Vector3.up);
            Vector3 origin = anchor.position + Vector3.up * 0.04f;
            Vector3 aim = frame * new Vector3(_aim.x * ArmSpan, _aim.y * ArmSpan, ArmForward);

            // The hands trail the body and counter-swing through a step, so the arms are
            // reacting to what the legs are doing instead of hanging there dead.
            Vector3 vel = anchor.linearVelocity;
            vel.y = 0f;
            Vector3 lag = -vel * SwingLag;

            // Whichever foot is in the air drives the counter-swing, and the
            // sign flips with which one it is -- that is what makes the arms
            // alternate with the legs instead of both pumping together.
            float stepT = 0f, sign = 1f;
            var feet = Feet;
            for (int i = 0; i < feet.Length; i++)
                if (feet[i].StepT > stepT) { stepT = feet[i].StepT; sign = i == 0 ? 1f : -1f; }

            float phase = stepT > 0f ? 1f - Mathf.Clamp01(stepT / Mathf.Max(0.01f, StepTime)) : 0f;
            float swing = stepT > 0f ? Mathf.Sin(phase * Mathf.PI * 2f) * SwingThrough * sign : 0f;
            Vector3 through = frame * new Vector3(0f, 0f, swing);

            Drive(HandL, origin + aim + lag + through + frame * new Vector3(-ArmSpread, 0f, 0f));
            Drive(HandR, origin + aim + lag - through + frame * new Vector3(ArmSpread, 0f, 0f));

            if (grab)
            {
                _grabL = TryGrab(HandL, _grabL);
                _grabR = TryGrab(HandR, _grabR);
            }
            else
            {
                Release(ref _grabL);
                Release(ref _grabR);
            }
        }

        void Drive(Rigidbody hand, Vector3 target)
        {
            if (hand == null) return;
            hand.AddForce((target - hand.position) * ArmSpring - hand.linearVelocity * ArmDamp,
                          ForceMode.Acceleration);
        }

        FixedJoint TryGrab(Rigidbody hand, FixedJoint existing)
        {
            if (existing != null || hand == null) return existing;

            var hits = Physics.OverlapSphere(hand.position, GrabRadius, CoatLayers.NotRig, QueryTriggerInteraction.Ignore);
            Rigidbody best = null;
            float bestDist = float.MaxValue;

            foreach (var h in hits)
            {
                var rb = h.attachedRigidbody;
                if (rb == null || rb.isKinematic) continue;
                float d = (h.ClosestPoint(hand.position) - hand.position).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = rb; }
            }
            if (best == null) return null;

            var j = hand.gameObject.AddComponent<FixedJoint>();
            j.connectedBody = best;
            j.breakForce = 3200f;
            j.breakTorque = 3200f;
            return j;
        }

        void Release(ref FixedJoint j)
        {
            if (j != null) { Destroy(j); j = null; }
        }

        public void DropEverything()
        {
            Release(ref _grabL);
            Release(ref _grabR);
        }

        /// Put this one on the floor at a spot, upright, foot underneath them, at rest.
        /// Used when the crew climbs back out of the body and has to exist again.
        public void PlaceAt(Vector3 groundPoint, Vector3 facing)
        {
            facing.y = 0f;
            _facing = facing.sqrMagnitude > 1e-4f ? facing.normalized : Vector3.forward;

            Vector3 delta = (groundPoint + Vector3.up * StandHeight) - Body.position;
            foreach (var rb in GetComponentsInChildren<Rigidbody>(true))
            {
                rb.position += delta;
                rb.transform.position += delta;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            foreach (var f in Feet)
            {
                f.To = groundPoint + Vector3.up * (f.Leg != null ? f.Leg.AnkleHeight : 0f);
                f.From = f.To;
                f.StepT = 0f;
                f.Cool = 0f;
            }
            _wasPushing = false;
            _collapse = 0f;
            _recover = 0f;
            _lowTimer = 0f;
            Stability = 1f;
        }

        public void SetHeadVisible(bool visible)
        {
            if (Head == null) return;
            foreach (var r in Head.GetComponentsInChildren<Renderer>()) r.enabled = visible;
        }
    }
}
