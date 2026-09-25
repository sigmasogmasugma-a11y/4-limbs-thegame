using UnityEngine;

namespace Coat.Classic
{
    /// A two-bone limb of the original single-body rig, driven by one player. Arms and
    /// legs share the solver: input moves an end-effector target, two-bone IK turns that
    /// into bone directions, and the joint drives push the real rigidbodies there.
    public class ClassicLimb : MonoBehaviour
    {
        public CoatRole Role;
        public bool IsLeg;
        [Tooltip("-1 for a left limb, +1 for a right one.")]
        public float Side = -1f;

        [Header("Bodies")]
        public Rigidbody Root;   // pelvis for legs, torso for arms
        public Rigidbody Upper;
        public Rigidbody Lower;
        public Rigidbody End;

        [Header("Joints")]
        public ConfigurableJoint UpperJoint;
        public ConfigurableJoint LowerJoint;
        [Tooltip("Ankle or wrist. Legs drive this to keep the foot flat on the floor.")]
        public ConfigurableJoint EndJoint;

        [Header("Geometry, measured at build")]
        public Vector3 RootAnchorLocal;
        public float UpperLen = 0.39f;
        public float LowerLen = 0.38f;
        [Tooltip("Which side of the straight hip-to-ankle line the middle joint bulges " +
                 "out on. Rotating the hip-to-foot direction by a POSITIVE angle about " +
                 "the body's right axis tips it backwards, so -1 is the forward-pointing " +
                 "knee of a person and +1 is the backward one of a bird. It was +1, and " +
                 "the knee stood 29cm out the back.")]
        public float BendSign = -1f;

        [Header("Legs")]
        [Tooltip("The other leg. A step cannot carry this foot further than MaxStride " +
                 "from it, which stops one leg walking the whole body off alone.")]
        public ClassicLimb Partner;
        public float MaxStride = 0.7f;
        [Tooltip("How high a foot may climb in one step, and how far above the foot the " +
                 "ground check starts.")]
        public float StepUp = 0.4f;
        [Tooltip("Step only on a fresh PRESS rather than while the key is held. " +
                 "Off, because on it meant exactly one step per press: holding W " +
                 "walked 0.21 m in five seconds and took a single step, and the " +
                 "only way to keep moving was to tap. It was there to make the two " +
                 "leg players tap in rhythm with the turn taking; held stepping is " +
                 "paced by StepTime instead, and the partner-airborne check still " +
                 "keeps one foot on the floor.")]
        public bool StepOnPressOnly = false;
        [Tooltip("Kept inside what the leg can reach at StandHeight, so an ordinary " +
                 "stride is not clamped short every single step.")]
        public float StepLength = 0.4f;
        public float StepTime = 0.34f;
        [Tooltip("Dead time after a step lands. Zero: the cadence is StepTime plus " +
                 "however long the partner is off the ground.")]
        public float StepCooldown = 0f;
        [Tooltip("Commanded arc height on flat ground; stepping up adds the climb on " +
                 "top, so this does not have to be tall enough for a kerb. The leg " +
                 "overshoots it by roughly a third. " +
                 "At 0.24 on a 0.77 m leg the knee came up near horizontal on every " +
                 "stride, which reads as a high march rather than a walk.")]
        public float LiftHeight = 0.15f;
        public float MaxReach = 0.72f;
        [Tooltip("Ankle height above the ground with the foot flat. The chain ends at the " +
                 "ankle, not at the foot's centre.")]
        public float AnkleHeight = 0.15f;
        [Tooltip("How far BELOW the floor a standing foot is aimed, so that it leans on " +
                 "the ground instead of hovering over it.\n" +
                 "Aimed exactly at floor level, a planted foot was touching down on only " +
                 "15% of the ticks it was supposed to be standing on, floating about " +
                 "two and a half centimetres up the rest of the time. A foot that is not " +
                 "touching cannot push back on anything, so the body slid around as if " +
                 "on ice. Aiming a little under the floor is what a real leg does: it " +
                 "puts weight through the foot.")]
        public float PlantDepth = 0.025f;
        [Tooltip("Closest a foot may plant to the body's centre line. Without a floor " +
                 "here the legs walk straight through each other when you strafe, which " +
                 "reads instantly as two people rather than one.")]
        public float MinSideOffset = 0.07f;
        [Tooltip("Furthest out a foot may plant. Keeps the stance from splaying.")]
        public float MaxSideOffset = 0.32f;
        [Tooltip("How fast a planted foot shuffles back under the body when the stance " +
                 "has been left too wide by the body turning.")]
        public float ShuffleSpeed = 0.55f;
        [Tooltip("The most of the leg's length a foot target may use up. This had to be " +
                 "small while the hips were pinned at one height, because anything more " +
                 "straightened the leg, stopped it holding the hip up and pitched the " +
                 "body over onto it. The hips now drop to meet a wide stride instead, " +
                 "so the leg is never asked for more than it has.")]
        [Range(0.6f, 1f)] public float MaxExtend = 0.97f;
        [Tooltip("How fast the foot is allowed to pivot to follow the body, in degrees " +
                 "per second. Commanding it straight to the body's heading wrenched a " +
                 "planted foot right over whenever you turned.")]
        public float FootTurnRate = 110f;
        [Tooltip("How hard the foot is held flat. This is a torque on the foot itself, " +
                 "not the ankle joint's drive: routed through the joint it kept losing " +
                 "to the leg IK as the shin swung, and the foot ended up on its back.")]
        public float FootTorque = 2400f;
        public float FootDamp = 120f;

        [Header("Arms")]
        public float AimSpeed = 2f;
        public float ArmSpan = 0.42f;
        [Tooltip("How far the hand sits from the shoulder. Just under the arm's own " +
                 "0.60 so it stays slightly bent rather than locked straight.")]
        public float Reach = 0.56f;
        [Tooltip("Where the hand sits with nobody touching the controls, in degrees " +
                 "from straight forward. -88 is hanging at the side.\n" +
                 "It used to rest 0.12 below the shoulder and 0.40 in front -- only " +
                 "0.435 away on a 0.60 arm, so the elbow had to fold to take up the " +
                 "slack: upper arm straight out, forearm straight down, a crane rather " +
                 "than a person.")]
        public float HangAngle = -88f;
        [Tooltip("Where the hand gets to with the stick held fully up.")]
        public float RaisedAngle = 40f;
        public float Rest = 0.12f;
        public float GrabRadius = 0.22f;

        /// Whether this limb's player is in the coat. An absent limb has its bones
        /// switched off entirely, so the disguise really is missing an arm or a leg
        /// rather than dragging a dead one around.
        public bool Present { get; private set; } = true;

        public bool Planted { get; private set; } = true;
        /// The player is holding Action to keep this foot nailed down.
        public bool Bracing { get; private set; }
        public bool Airborne => _stepT > 0f;
        public bool JustStepped { get; private set; }
        public bool Holding => _grab != null;
        /// What this hand has hold of, if anything. Needed so a prop can tell
        /// how many hands are on it without the hands knowing what a prop is.
        public Rigidbody Held => _grab == null ? null : _grab.connectedBody;
        public Vector3 Target => _target;
        public Vector3 StepTarget => _stepT > 0f ? _stepTo : _target;
        public Vector3 LastStepDir { get; private set; }
        public float StepDirAge { get; private set; } = 999f;

        Vector2 _aim;
        Vector3 _target;
        Quaternion _upperStart, _lowerStart, _endStart;
        FixedJoint _grab;

        float _stepT, _cooldown;

        [Header("Splits")]
        [Tooltip("How fast a foot is dragged along the floor when the two leg " +
                 "players are hauling in opposite directions.")]
        public float SlideSpeed = 1.2f;
        Vector3 _slide;
        Vector3 _stepFrom, _stepTo;
        bool _wasPushing;

        Pose _upperRest, _lowerRest, _endRest;

        /// How many times the knee's hinge axis has jumped 180 degrees.
        public int HingeFlips { get; private set; }
        Vector3 _lastHinge;
        bool _hingeSeen;

        /// The way the foot is being told to point. Carried as state so it survives the
        /// foot itself ending up in a pose we cannot read a heading out of.
        Vector3 _footYaw;

        void Awake()
        {
            _upperStart = Quaternion.Inverse(Root.rotation) * Upper.rotation;
            _lowerStart = Quaternion.Inverse(Upper.rotation) * Lower.rotation;
            if (End != null) _endStart = Quaternion.Inverse(Lower.rotation) * End.rotation;

            // Where the bones hang off the pelvis or torso when nothing has moved. A limb
            // that gets switched back on has been sitting wherever it was abandoned, so
            // it has to be put back on the body before anyone sees it.
            // Arms start hanging, not held out. aim.y of -1 is the bottom of its range.
            if (!IsLeg) _aim = new Vector2(0f, -1f);

            _upperRest = RestOf(Upper);
            _lowerRest = RestOf(Lower);
            _endRest = RestOf(End);

            // The solvable chain ends at the ankle/wrist, not at the foot or hand centre.
            _target = Lower.position + Lower.rotation * Vector3.up * (LowerLen * 0.5f);
            _stepTo = _target;
        }

        Pose RestOf(Rigidbody rb) => rb == null
            ? default
            : new Pose(Root.transform.InverseTransformPoint(rb.position),
                       Quaternion.Inverse(Root.rotation) * rb.rotation);

        /// Switch this limb on or off. Turning one back on puts its bones back where they
        /// belong on the body first, or the arm reappears across the room.
        public void SetPresent(bool on)
        {
            if (on && !Present) Reattach();

            Present = on;
            Show(Upper, on);
            Show(Lower, on);
            Show(End, on);
        }

        static void Show(Rigidbody rb, bool on)
        {
            if (rb != null && rb.gameObject.activeSelf != on) rb.gameObject.SetActive(on);
        }

        /// Put this limb back exactly as it was built, carrying none of the last
        /// shift's bookkeeping.
        ///
        /// A limb that stays switched on across a change of crew never went through
        /// Reattach, which only runs for one coming back from being off, so it kept
        /// its swing timer, its cooldown, its remembered step direction and the yaw it
        /// was holding its foot at. Place already promised a clean stance and only
        /// delivered clean BONES; this makes the promise true.
        ///
        /// It is NOT the cure for the folded leg on a second boarding. That was the
        /// reason this was written and it did not fix it -- the left leg still stands
        /// up with its foot at hip height the second time a coat is emptied and worn
        /// again. Kept because resetting the state is right regardless, but the real
        /// fault is still somewhere else.
        public void Restore() => Reattach();

        void Reattach()
        {
            Put(Upper, _upperRest);
            Put(Lower, _lowerRest);
            Put(End, _endRest);

            _stepT = 0f;
            _cooldown = 0f;
            _wasPushing = false;
            _aim = IsLeg ? Vector2.zero : new Vector2(0f, -1f);
            StepDirAge = 999f;
            LastStepDir = Vector3.zero;
            Bracing = false;
            JustStepped = false;
            HingeFlips = 0;
            _hingeSeen = false;
            LowestHipY = float.PositiveInfinity;
            PlanHipY = float.PositiveInfinity;

            _target = Root.transform.TransformPoint(_lowerRest.position)
                    + (Root.rotation * _lowerRest.rotation) * Vector3.up * (LowerLen * 0.5f);
            _stepFrom = _stepTo = _target;
            Planted = true;
            _footYaw = Vector3.zero;
        }

        void Put(Rigidbody rb, Pose rest)
        {
            if (rb == null) return;

            Vector3 p = Root.transform.TransformPoint(rest.position);
            Quaternion r = Root.rotation * rest.rotation;

            rb.transform.SetPositionAndRotation(p, r);
            rb.position = p;
            rb.rotation = r;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        public void Tick(Vector2 move, bool action, float camYaw, float dt,
                         bool partnerAirborne = false, bool mayStep = true)
        {
            if (!Present) return;

            if (IsLeg) TickLeg(move, action, camYaw, dt, partnerAirborne, mayStep);
            else TickArm(move, action, dt);

            Solve(_target, dt);
        }

        // ---- legs -------------------------------------------------------------

        void TickLeg(Vector2 move, bool brace, float camYaw, float dt, bool partnerAirborne, bool mayStep)
        {
            Bracing = brace;
            JustStepped = false;
            if (_cooldown > 0f) _cooldown -= dt;
            StepDirAge += dt;

            // Every tick, in the air as well as on the ground. A step is aimed once and
            // then takes a third of a second to land, and the body covers half a metre
            // in that time -- so a destination that was reachable when it was chosen is
            // well out of reach by the time the foot gets there.
            // Being pulled apart. This runs BEFORE UncrossFrom and PullInReach,
            // and that is the whole point: PullInReach drags a foot target back
            // to what the leg can actually span, so with it in front the stance
            // settled at 0.91 m and simply stayed there -- the body stood in the
            // splits for eight seconds and never fell. Skipping the clamps lets
            // the feet outrun the legs, which hauls the hips down, and the
            // ordinary low-pelvis check then puts it on the floor by itself.
            //
            // It also cancels any swing: a foot cannot be mid-stride and doing
            // the splits at the same time.
            if (_slide.sqrMagnitude > 1e-4f)
            {
                _stepT = 0f;
                _stepTo += _slide.normalized * (SlideSpeed * dt);
                _stepTo.y = Standing(_stepTo);
                _target = _stepTo;
                Planted = true;
                _slide = Vector3.zero;
                return;
            }

            _stepTo = UncrossFrom(_stepTo, dt);
            _stepTo = PullInReach(_stepTo, dt);

            bool pushing = move.sqrMagnitude > 0.04f;
            bool fresh = pushing && (!StepOnPressOnly || !_wasPushing);
            _wasPushing = pushing;

            if (_stepT > 0f)
            {
                _stepT -= dt;
                float u = 1f - Mathf.Clamp01(_stepT / Mathf.Max(0.01f, StepTime));

                // Lift higher when stepping onto something. A flat arc of 0.20 left
                // about a centimetre over the 0.14 kerb once the foot's own thickness
                // is counted, so the foot clipped it and the body pitched over.
                float climb = Mathf.Max(0f, _stepTo.y - _stepFrom.y);
                Vector3 p = Vector3.Lerp(_stepFrom, _stepTo, u);
                p.y += Mathf.Sin(u * Mathf.PI) * (LiftHeight + climb);
                _target = p;
                Planted = false;

                if (_stepT <= 0f)
                {
                    _target = _stepTo;
                    Planted = true;
                    _cooldown = StepCooldown;
                }
                return;
            }

            Planted = true;
            _target = _stepTo;

            if (fresh && mayStep && !brace && !partnerAirborne && _cooldown <= 0f)
                BeginStep(move, camYaw);
        }

        /// Drag this foot along the floor this tick instead of stepping.
        ///
        /// Set every tick while it lasts: it clears itself once used, so letting
        /// go of the keys stops the slide on the next tick with no timer to
        /// unwind.
        public void SlideThisTick(Vector3 dir)
        {
            _slide = dir;
        }

        void BeginStep(Vector2 move, float camYaw)
        {
            Vector3 dir = Quaternion.Euler(0f, camYaw, 0f) * new Vector3(move.x, 0f, move.y);
            if (dir.sqrMagnitude < 1e-4f) return;
            dir.Normalize();

            LastStepDir = dir;
            StepDirAge = 0f;

            Vector3 from = _stepTo;
            from.y = Standing(from);
            Vector3 to = from + dir * StepLength;
            to.y = Standing(to);

            // Aim it somewhere the leg can get to. MaxReach on its own is a flat
            // distance and takes no account of the length spent dropping to the floor,
            // so it let the step be aimed well past anything the leg could reach --
            // which is how walking backwards ended up asking for nearly double.
            Vector3 hip = Root.transform.TransformPoint(RootAnchorLocal);
            Vector3 flat = to - hip;
            flat.y = 0f;
            float allowed = Mathf.Min(MaxReach, AllowedToAim(hip.y, to.y));
            if (flat.magnitude > allowed) flat = flat.normalized * allowed;
            to = hip + flat;

            if (Partner != null && Partner.Present)
            {
                Vector3 stride = to - Partner.StepTarget;
                stride.y = 0f;
                if (stride.magnitude > MaxStride)
                    to = Partner.StepTarget + stride.normalized * MaxStride;
            }

            to = OwnSide(to);
            to.y = Standing(to);

            _stepFrom = from;
            _stepTo = to;
            _stepT = StepTime;
            Planted = false;
            JustStepped = true;
        }

        /// Is this leg the only one in the coat? Then there is no other leg to cross,
        /// and the rules that keep the two of them apart do the opposite of good.
        bool Alone => Partner == null || !Partner.Present;

        /// Push a step back onto this leg's own side of the body. Measured from the
        /// body's centre rather than from the hip, so the two feet cannot end up closer
        /// together than a real stance, nor swap over.
        ///
        /// On one leg the foot is aimed at the centre line instead. Anyone hopping puts
        /// their foot UNDER themselves; holding it out to one side, as the anti-crossing
        /// rule did, means the body is forever being driven to stand over a support
        /// point that is off to one side. Measured: walking straight forward on the left
        /// leg drifted 1.24 m to the left over 6.54 m, about a fifth of the distance.
        Vector3 OwnSide(Vector3 to)
        {
            Vector3 across = Vector3.ProjectOnPlane(Root.transform.right, Vector3.up);
            if (across.sqrMagnitude < 1e-4f) return to;
            across.Normalize();

            float lateral = Vector3.Dot(to - Root.position, across);
            float want = Alone
                ? 0f
                : Side < 0f
                    ? Mathf.Clamp(lateral, -MaxSideOffset, -MinSideOffset)
                    : Mathf.Clamp(lateral, MinSideOffset, MaxSideOffset);

            return to + across * (want - lateral);
        }

        /// Crossing the centre line is snapped out instantly, because two legs through
        /// each other is the worst thing the disguise can do. Being too far OUT is only
        /// shuffled back at walking pace: a planted foot that slid home the moment the
        /// body turned would skate across the floor.
        Vector3 UncrossFrom(Vector3 to, float dt)
        {
            Vector3 across = Vector3.ProjectOnPlane(Root.transform.right, Vector3.up);
            if (across.sqrMagnitude < 1e-4f) return to;
            across.Normalize();

            float lateral = Vector3.Dot(to - Root.position, across);

            // Alone, the foot shuffles in under the body rather than being held out on
            // its own side. Nothing can be crossed, so it is always a shuffle and never
            // a snap.
            if (Alone)
                return to + across * Mathf.Clamp(-lateral, -ShuffleSpeed * dt, ShuffleSpeed * dt);

            // Only keeps the foot inside the legal band; it does NOT drag it back to a
            // nominal stance width.
            //
            // Easing it home was tried, because strafing leaves one foot out at 30 cm
            // and the other at 8 and the midpoint of the two sits 11 cm off to one
            // side, which looked like the cause of the sideways pull. It is not. With
            // the stance forced tidy (midpoint under 1 cm) the deflection got WORSE,
            // 28 degrees to 46, and walking backwards went from 1 degree off to 44 --
            // dragging a planted foot sideways every tick costs more than the skewed
            // stance ever did. The pull is still unexplained; it is not this.
            float want = Side < 0f
                ? Mathf.Clamp(lateral, -MaxSideOffset, -MinSideOffset)
                : Mathf.Clamp(lateral, MinSideOffset, MaxSideOffset);

            float move = want - lateral;
            bool crossed = Side < 0f ? lateral > -MinSideOffset : lateral < MinSideOffset;
            if (!crossed) move = Mathf.Clamp(move, -ShuffleSpeed * dt, ShuffleSpeed * dt);

            return to + across * move;
        }

        /// Where this leg's hip would be at the very bottom of a stride, in world Y.
        /// Written by the body every tick; until it is, a step is judged from the hip
        /// as it currently stands.
        [System.NonSerialized] public float LowestHipY = float.PositiveInfinity;

        /// Where this leg's hip is on its way to, in world Y -- the height the body has
        /// committed to standing at for the feet it currently has on the floor.
        [System.NonSerialized] public float PlanHipY = float.PositiveInfinity;

        /// How far out from a hip at this height the foot may be, given how much of
        /// the leg is already used up dropping to the floor.
        float AllowedFlat(float hipY, float footY)
        {
            float longest = (UpperLen + LowerLen) * MaxExtend;
            float drop = hipY - footY;
            float sq = longest * longest - drop * drop;
            return sq <= 0f ? 0.05f : Mathf.Sqrt(sq);
        }

        /// Where a NEW step may be aimed: against the lowest the hips are willing to
        /// sink to, because the body comes DOWN to meet a wide stride. Judged from a
        /// tall stance it would refuse every stride the leg can comfortably take, and
        /// refusing them is what forced the stance into a permanent squat.
        float AllowedToAim(float hipY, float footY) =>
            AllowedFlat(Mathf.Min(hipY, LowestHipY), footY);

        /// How far out a PLANTED foot may stay. Judged against the height the body has
        /// actually committed to standing at, not the optimistic one a step was aimed
        /// with -- that optimism belongs to the moment of aiming only. Left in here it
        /// let a foot sit half a metre out while the hips were still up at their full
        /// height, and the leg was asked for a fifth more than its own length.
        float AllowedToKeep(float hipY, float footY) =>
            AllowedFlat(Mathf.Min(hipY, PlanHipY), footY);

        /// Keep the planted foot inside what the leg can actually reach. Checked every
        /// tick rather than only when the step is taken: the clamp in BeginStep is
        /// measured against where the hip was at the time, and the body then walks on.
        /// Going backwards it walked on so far that the leg was asked for nearly double
        /// its own length, went straight, and the body pitched over on top of it.
        Vector3 PullInReach(Vector3 to, float dt)
        {
            Vector3 hip = Root.transform.TransformPoint(RootAnchorLocal);
            float allowed = AllowedToKeep(hip.y, to.y);

            Vector3 flat = to - hip;
            flat.y = 0f;
            float d = flat.magnitude;
            if (d <= allowed || d < 1e-4f) return to;

            // A couple of centimetres over and the foot shuffles home, which reads as a
            // little adjusting step. Anything more is snapped straight back: rate
            // limiting it meant the correction moved at about 1.2 m/s while the body
            // walked away at 1.6, so going backwards the clamp simply lost the race.
            float over = d - allowed;
            float move = over > 0.06f ? over : Mathf.Min(over, ShuffleSpeed * dt);
            return to - flat / d * move;
        }

        public void ForcePlant(Vector3 worldFoot)
        {
            Vector3 p = worldFoot;
            p.y = Standing(p);
            _target = p;
            _stepTo = p;
            _stepFrom = p;
            Planted = true;
            _stepT = 0f;
            _cooldown = 0f;
        }

        // ---- arms -------------------------------------------------------------

        void TickArm(Vector2 move, bool grab, float dt)
        {
            _aim.x = Mathf.Clamp(_aim.x + move.x * AimSpeed * dt, -1f, 1f);
            _aim.y = Mathf.Clamp(_aim.y + move.y * AimSpeed * dt, -1f, 1f);

            // Body relative, not camera relative: orbiting the view must not drag the
            // hands around the body on its own.
            Quaternion frame = BodyFrame();

            // The hand swings on an ARC about the shoulder, which is what a shoulder
            // does: hanging at the side at the bottom, straight out in front through
            // the middle, up and forward at the top. aim.y starts at -1, so a player
            // touching nothing has an arm that hangs rather than one held out like a
            // sleepwalker.
            //
            // The arc matters more than it looks. Driving height and forward reach as
            // two independent numbers gave an arm that could be DOWN at its side or
            // FORWARD at chest height and nothing in between -- so there was no way to
            // reach down and forward, which is the whole of picking something up off a
            // table. Hands sat two centimetres from the tray and could not touch it.
            float lift = (_aim.y + 1f) * 0.5f;
            float swing = Mathf.Lerp(HangAngle, RaisedAngle, lift) * Mathf.Deg2Rad;

            Vector3 shoulder = Root.transform.TransformPoint(RootAnchorLocal);
            Vector3 local = new Vector3(
                Side * Rest + _aim.x * ArmSpan * Mathf.Lerp(0.3f, 1f, lift),
                Mathf.Sin(swing) * Reach,
                Mathf.Cos(swing) * Reach);
            _target = shoulder + frame * local;

            if (grab) TryGrab();
            else Release();
        }

        Quaternion BodyFrame()
        {
            Vector3 fwd = Vector3.ProjectOnPlane(Root.transform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(Root.transform.up, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) return Quaternion.identity;
            return Quaternion.LookRotation(fwd.normalized, Vector3.up);
        }

        void TryGrab()
        {
            if (_grab != null) return;

            var hits = Physics.OverlapSphere(End.position, GrabRadius, CoatLayers.NotRig, QueryTriggerInteraction.Ignore);
            Rigidbody best = null;
            float bestDist = float.MaxValue;

            foreach (var h in hits)
            {
                var rb = h.attachedRigidbody;
                if (rb == null || rb.isKinematic) continue;
                float d = (h.ClosestPoint(End.position) - End.position).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = rb; }
            }
            if (best == null) return;

            _grab = End.gameObject.AddComponent<FixedJoint>();
            _grab.connectedBody = best;
            _grab.breakForce = 3200f;
            _grab.breakTorque = 3200f;
        }

        void Release()
        {
            if (_grab != null) { Destroy(_grab); _grab = null; }
        }

        public void DropEverything() => Release();

        // ---- two-bone IK ------------------------------------------------------

        void Solve(Vector3 target, float dt)
        {
            Vector3 origin = Root.transform.TransformPoint(RootAnchorLocal);
            Vector3 to = target - origin;

            float span = UpperLen + LowerLen;
            float d = Mathf.Clamp(to.magnitude, 0.08f, span - 0.015f);
            if (to.sqrMagnitude < 1e-6f) return;
            Vector3 dir = to.normalized;

            Vector3 hinge = Vector3.ProjectOnPlane(Root.transform.right, dir);
            if (hinge.sqrMagnitude < 1e-6f) hinge = Vector3.ProjectOnPlane(Root.transform.forward, dir);
            hinge.Normalize();

            // Instrumentation: how often does this axis jump to the opposite side?
            if (_hingeSeen && Vector3.Dot(hinge, _lastHinge) < 0f) HingeFlips++;
            _lastHinge = hinge;
            _hingeSeen = true;

            float cos = Mathf.Clamp((UpperLen * UpperLen + d * d - LowerLen * LowerLen) / (2f * UpperLen * d), -1f, 1f);
            float lift = Mathf.Acos(cos) * Mathf.Rad2Deg;

            Vector3 upperDir = Quaternion.AngleAxis(lift * BendSign, hinge) * dir;
            Vector3 knee = origin + upperDir * UpperLen;
            Vector3 lowerDir = (target - knee).normalized;
            if (lowerDir.sqrMagnitude < 1e-6f) return;

            Quaternion upperWorld = Quaternion.LookRotation(hinge, upperDir);
            Quaternion lowerWorld = Quaternion.LookRotation(hinge, lowerDir);

            UpperJoint.SetTargetRotationLocal(Quaternion.Inverse(Root.rotation) * upperWorld, _upperStart);
            LowerJoint.SetTargetRotationLocal(Quaternion.Inverse(upperWorld) * lowerWorld, _lowerStart);

            LevelFoot(dt);
        }

        /// Hold the sole parallel to the ground. Nothing used to drive the ankle at all,
        /// so the foot kept whatever angle it had to the shin when the rig was built --
        /// and standing bends the knee by nearly ninety degrees, which tipped the whole
        /// foot onto its edge with the heel in the air.
        ///
        /// The heading it is levelled around starts from where the foot ALREADY points
        /// and eases toward the body, rather than snapping to the body outright. Pointing
        /// it straight at the body's heading meant that every time you turned, a foot
        /// stood on the floor was ordered to twist on the spot -- and against the ankle's
        /// limit it wrenched the whole foot over onto its back.
        void LevelFoot(float dt)
        {
            if (!IsLeg || End == null) return;

            // The foot points where the body points, in the air and on the ground alike.
            // It used to be aimed along the step instead, which meant a sideways step
            // turned the foot sideways -- and side-stepping repeatedly left both feet
            // permanently splayed off the heading.
            Vector3 want = Vector3.ProjectOnPlane(Root.transform.forward, Vector3.up);
            if (want.sqrMagnitude < 1e-4f) want = _footYaw;
            if (want.sqrMagnitude < 1e-4f) return;
            want.Normalize();

            // The heading is carried here rather than read back off the foot. Reading it
            // off the foot was self-locking: once the foot tipped past about eighty
            // degrees its own forward pointed at the sky, the flattened copy of it went
            // to zero, the whole routine bailed out, and the foot stayed over on its
            // back for good.
            if (_footYaw.sqrMagnitude < 1e-4f) _footYaw = want;
            _footYaw = Vector3.RotateTowards(_footYaw, want, FootTurnRate * Mathf.Deg2Rad * dt, 0f);

            Quaternion flat = Quaternion.LookRotation(_footYaw, Vector3.up);

            // Both at once, at the same target: the joint drive holds the everyday pose
            // cheaply, and the torque has the authority to haul the foot back when the
            // shin swings hard enough to beat the drive.
            if (EndJoint != null)
                EndJoint.SetTargetRotationLocal(Quaternion.Inverse(Lower.rotation) * flat, _endStart);

            Quaternion delta = flat * Quaternion.Inverse(End.rotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (axis.sqrMagnitude < 1e-6f || float.IsNaN(axis.x) || float.IsInfinity(axis.x)) return;

            End.AddTorque(axis.normalized * (angle * Mathf.Deg2Rad * FootTorque)
                          - End.angularVelocity * FootDamp, ForceMode.Acceleration);
        }

        /// Where the ankle is aimed for a foot standing on the floor under this point.
        float Standing(Vector3 p) => Ground(p) + AnkleHeight - PlantDepth;

        float Ground(Vector3 p)
        {
            Vector3 from = new Vector3(p.x, p.y + StepUp, p.z);
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, StepUp + 2f,
                                CoatLayers.NotRig, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return 0f;
        }
    }
}
