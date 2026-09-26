using UnityEngine;

namespace Coat.Classic
{
    /// The original rig: ONE body, four limbs, four players. Two leg players own a leg
    /// each of the same pelvis, so they are always pushing the same centre of mass
    /// around. Every value here is what we arrived at by testing the first time.
    public class ClassicRagdoll : MonoBehaviour
    {
        public LocalCoatInput Input;

        /// Fusion drives this ragdoll explicitly from host-authoritative input.
        /// When false, the original FixedUpdate/local-input path is unchanged.
        public bool ExternalSimulation;
        public Transform CameraRef;

        [Header("Parts")]
        public Rigidbody Pelvis;
        public Rigidbody Torso;
        public Rigidbody Head;
        public ClassicLimb LegL;
        public ClassicLimb LegR;
        public ClassicLimb ArmL;
        public ClassicLimb ArmR;

        [Header("Stance")]
        [Tooltip("How tall the body stands with its feet together. A CAP, not a fixed " +
                 "height: the hips drop by themselves as the stride opens, so nothing " +
                 "has to be traded away to buy stride length. Held at one value it " +
                 "forced an unhappy choice -- stand tall and the leg was straightened " +
                 "and the body pitched over it, or crouch and the disguise spent the " +
                 "whole round walking in a ninety degree squat.")]
        public float StandHeight = 0.95f;
        [Tooltip("How far the hips may sink at the bottom of a long stride. " +
                 "This also decides how far a planted foot may be abandoned from its " +
                 "own hip, because the lower the hips are allowed to go the further " +
                 "away the leg can still reach. At 0.72 a foot could be left 57 cm " +
                 "behind its hip and the thigh came up past horizontal to reach it, " +
                 "so walking backwards looked like a high march. " +
                 "Raising it straightens that out AND walks slightly further forwards, " +
                 "at the cost of backing up, because a foot that cannot be left far " +
                 "behind cannot push the body backwards either. 0.86 straightened the " +
                 "leg nicely and then could not walk backwards at all -- 1.50 m became " +
                 "-0.07. This is the middle: most of the posture, most of the reverse.")]
        public float MinStandHeight = 0.78f;
        [Tooltip("Vertical only. Stiff, because it is what holds the body up.")]
        public float StandSpring = 600f;
        public float StandDamp = 45f;
        [Tooltip("Horizontal chase towards the feet. Deliberately far softer than the " +
                 "vertical hold: at the same gain the hips were accelerating at seven g, " +
                 "and the torso, hanging off a spring above them, could not keep up. It " +
                 "whipped backwards and the body walked along leaning away from wherever " +
                 "it was going. A person walking peaks at about half a g.")]
        public float TrackSpring = 170f;
        public float TrackDamp = 16f;
        [Tooltip("Largest stance error acted on. Without it, getting up off the floor " +
                 "fires the body into the air.")]
        public float MaxStandError = 0.35f;

        [Header("Balance")]
        [Tooltip("Has to out-argue the spine and hip drives, so it wants to be large.")]
        public float UprightTorque = 1600f;
        public float UprightDamp = 110f;
        public float BraceBonus = 1.3f;
        public float TurnRate = 140f;
        [Tooltip("How long a leg's last step direction still counts towards facing.")]
        public float StepMemory = 0.9f;
        [Tooltip("The legs must take turns: neither can step twice in a row. " +
                 "Off. With it on, one leg player holding their key got a single " +
                 "step and then waited out TurnTimeout -- 1.5 s -- because the " +
                 "other leg was never going to take its turn. That is what made " +
                 "walking feel like it needed both W and I pressed together. " +
                 "One foot still stays down: ClassicLimb refuses to step while its " +
                 "partner is airborne, which is the check that actually stops it " +
                 "hopping.")]
        public bool AlternatingSteps = false;
        public float TurnTimeout = 1.5f;
        [Tooltip("How much of the weight the hips are carrying they hold up directly, " +
                 "as a fraction. Left a little under 1 on purpose, so the stance spring " +
                 "still has something to do and the body settles rather than floats.")]
        [Range(0f, 1f)] public float HipAssist = 0.9f;
        [Tooltip("Floor on that support. Same reasoning as MinUpright: a crew short of " +
                 "arms never gets its balance reading above 0.3, and holding itself off " +
                 "the floor should not be a privilege of a full crew.")]
        [Range(0f, 1f)] public float MinHipAssist = 0.8f;
        public float ToppleRadius = 0.75f;
        [Range(0f, 1f)] public float OneFootPenalty = 0.85f;
        [Tooltip("How much the foot mid-step counts towards where the body should be. " +
                 "This is what carries you forward.")]
        [Range(0f, 1f)] public float SwingWeight = 0.45f;
        public float MaxWalkSpeed = 1.6f;
        [Tooltip("How hard the body is held back once it is over its top speed. NOT " +
                 "currently an effective cap -- hopping on one leg runs at 0.72 m/s " +
                 "against a 0.64 ceiling and mashing both keys averages 2.3 against " +
                 "1.6. Raising this to 45 barely moved either, so the speed is not " +
                 "coming from the pelvis outrunning the damper and the real cause is " +
                 "still unfound. Left at the tested value rather than guessed at.")]
        public float OverspeedDamp = 12f;
        public float MaxRiseSpeed = 2.5f;
        [Tooltip("Floor on how hard the hips chase the feet, so leaning does not weaken " +
                 "the very correction that fixes the lean.")]
        [Range(0f, 1f)] public float MinTrack = 0.45f;
        [Tooltip("Floor on self-righting, so a stumble can still be saved -- and so the " +
                 "body keeps itself vertical while walking instead of hunching forward " +
                 "over its own legs. This does NOT touch the Stability reading, so the " +
                 "teetering is still there to see and to punish; the body just carries " +
                 "itself while it teeters.")]
        [Range(0f, 1f)] public float MinUpright = 0.55f;
        [Tooltip("How much of the leg the body will stand on. Kept a little under the " +
                 "limb's own MaxExtend so standing does not sit exactly on the limit " +
                 "with nothing left over to absorb a stumble.")]
        [Range(0.6f, 1f)] public float StandExtend = 0.95f;

        [Header("Short handed")]
        [Tooltip("Balance lost per missing arm. You steady yourself with your arms, and " +
                 "taking their weight off the torso would otherwise make an armless coat " +
                 "MORE stable, which is the wrong way round.")]
        [Range(0f, 0.5f)] public float ArmlessPenalty = 0.35f;
        [Tooltip("Extra wobble when hopping on one leg, on top of OneFootPenalty.")]
        [Range(0f, 1f)] public float OneLeggedPenalty = 0.65f;

        [Header("Standing still")]
        [Tooltip("How hard a planted foot drags on the floor. Without it nothing opposes " +
                 "a slide below the top speed, so letting go of the keys left the body " +
                 "coasting off in whatever direction it had last walked.")]
        public float StanceFriction = 7f;
        [Tooltip("A step this far opposed to the way the body faces is treated as backing " +
                 "up rather than as a change of heading, so a step or two backwards does " +
                 "not spin the disguise round on the spot.")]
        [Range(0f, 1f)] public float BackstepDot = 0.35f;
        [Tooltip("How many steps in a row you may take against your own facing before " +
                 "the body accepts you have changed your mind and comes round. One step " +
                 "back is backing up; two in a row is walking that way. " +
                 "Counted in STEPS and not in seconds on purpose: one player tapping on " +
                 "their own steps about once every second and a half and a full crew " +
                 "three times as often, so any timer is either a spin on the spot for " +
                 "one of them or, as it was, a refusal that never expires -- the body " +
                 "walked off sideways still facing where it started and never came " +
                 "round. Set it to 1 to turn towards travel immediately.")]
        [Range(1, 6)] public int BackstepAllowance = 2;
        [Tooltip("Top speed while hopping, as a fraction of MaxWalkSpeed. Without this " +
                 "one leg is the FASTEST way to travel, because the body chases its one " +
                 "foot through the whole stride instead of averaging over two.")]
        [Range(0.1f, 1f)] public float HopSpeed = 0.4f;

        [Header("Diagnostics")]
        [Tooltip("Print a line to the console the moment the spine goes past MaxLean, " +
                 "with everything worth knowing at that instant. Left on while the " +
                 "lying-down fault is still being chased -- turn it off for recording.")]
        public bool WatchFalls = true;

        [Header("Collapse and recovery")]
        [Tooltip("Follows StandHeight down: the head now rides about 0.10 lower, so the " +
                 "same threshold would have counted standing up as falling over.")]
        public float HeadMinHeight = 0.78f;
        [Tooltip("How far the spine may lean off vertical before the disguise counts as " +
                 "having gone over, in degrees.\n" +
                 "Falling used to be judged on the head's HEIGHT alone, and a body lying " +
                 "flat still holds its head up at about hip height -- well clear of the " +
                 "floor. So going over on your face was never noticed: the disguise did " +
                 "not fall, did not get up, and walked on horizontally with its feet " +
                 "still going. Ordinary walking peaks around 27 degrees and the worst " +
                 "the kerb produced was 41, so this has room to spare.")]
        public float MaxLean = 65f;
        public float CollapseSeconds = 1.4f;
        [Tooltip("Two leg players hauling in opposite directions drag the feet " +
                 "apart and put the body on the floor. A and L, or W and K.")]
        public bool SplitsCollapse = true;
        [Tooltip("How opposed the two leg players have to be. -1 is dead against " +
                 "each other; -0.6 is about 127 degrees apart, so a sloppy " +
                 "disagreement still counts and merely turning does not.")]
        public float SplitDot = -0.6f;
        [Tooltip("How long they have to fight before the feet actually start " +
                 "sliding apart, so brushing opposite directions in passing is " +
                 "survivable. NOTHING here forces a fall: once the feet are " +
                 "sliding, the body goes down when it can no longer hold itself " +
                 "up over a stance that wide, through the same low-pelvis and " +
                 "lean checks that catch any other stumble. Collapsing on a " +
                 "timer put it on the floor at a fixed 0.44 s every time, which " +
                 "read as a scripted animation rather than losing your footing.")]
        public float SplitSeconds = 0.18f;
        public float CollapseGrace = 0.4f;
        public float RecoverSeconds = 1.4f;
        public float RecoverBoost = 1.4f;
        public float StanceWidth = 0.13f;

        public float Stability { get; private set; }
        public int PlantedFeet { get; private set; }
        /// How many of the two legs have a player in them. Zero means the coat is a
        /// torso with nothing underneath it, and it is going nowhere.
        public int Legs { get; private set; } = 2;
        public int Arms { get; private set; } = 2;
        public bool CanWalk => Legs > 0;
        public bool Collapsed => _collapse > 0f;
        /// The legs are being hauled apart right now. Read by the HUD and by
        /// anything that wants to react before the body actually goes down.
        public bool Splitting { get; private set; }
        public bool Recovering => _recover > 0f;
        public Vector3 Heading => _facing;

        /// What the balance drive pushed the hips with last tick, so a harness can
        /// resolve each term into the body's own axes and see which one is steering it
        /// sideways. Diagnostics only; nothing reads these in the game.
        [System.NonSerialized] public Vector3 LastChase;      // horizontal stance spring
        [System.NonSerialized] public Vector3 LastBrake;      // overspeed + stance friction
        [System.NonSerialized] public Vector3 LastSupport;    // the point it is standing over
        [System.NonSerialized] public Vector3 LastWantErr;    // support minus hips, flat

        public enum Leg { Either, Left, Right }
        public Leg NextLeg => _nextLeg;

        float _collapse, _recover, _lowTimer, _turnWait, _bout, _splitT;
        int _backSteps;
        Vector3 _facing = Vector3.forward;
        Leg _nextLeg = Leg.Either;

        Rigidbody[] _bones;
        Pose[] _buildPose;
        ClassicCloth _cloth;
        float _carry = -1f;
        float _standWanted;

        void Awake()
        {
            if (Input == null) Input = GetComponent<LocalCoatInput>();
            if (CameraRef == null && Camera.main != null) CameraRef = Camera.main.transform;

            Physics.IgnoreLayerCollision(CoatLayers.Rig, CoatLayers.Rig, true);

            Vector3 f = Vector3.ProjectOnPlane(Pelvis.transform.forward, Vector3.up);
            _facing = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;

            // Remember the stance we were built in. Awake runs the first time this rig
            // is switched on, before any physics has touched it, so this is the clean
            // pose and every later Place() can snap straight back to it.
            _bones = GetComponentsInChildren<Rigidbody>(true);
            _buildPose = new Pose[_bones.Length];
            for (int i = 0; i < _bones.Length; i++)
                _buildPose[i] = new Pose(
                    transform.InverseTransformPoint(_bones[i].position),
                    Quaternion.Inverse(transform.rotation) * _bones[i].rotation);

            _cloth = GetComponentInChildren<ClassicCloth>(true);
        }

        /// Stand the body up on a spot, in the pose it was built in, at rest. Called
        /// when the crew climbs in: every shift starts from the same clean stance rather
        /// than from whatever heap the last one ended in.
        public void Place(Vector3 groundPoint, Vector3 facing)
        {
            facing.y = 0f;
            facing = facing.sqrMagnitude > 1e-4f ? facing.normalized : Vector3.forward;
            Quaternion rot = Quaternion.LookRotation(facing, Vector3.up);

            transform.SetPositionAndRotation(groundPoint, rot);

            for (int i = 0; i < _bones.Length; i++)
            {
                var rb = _bones[i];
                // A switched off limb is placed by SetLimbs when its player turns up.
                if (rb == null || !rb.gameObject.activeInHierarchy) continue;

                Vector3 p = transform.TransformPoint(_buildPose[i].position);
                Quaternion r = rot * _buildPose[i].rotation;

                rb.transform.SetPositionAndRotation(p, r);
                rb.position = p;
                rb.rotation = r;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            // Every limb, not just the bones. A limb that was already switched on
            // across a change of crew otherwise keeps the last shift's step state.
            if (LegL.Present) LegL.Restore();
            if (LegR.Present) LegR.Restore();
            if (ArmL.Present) ArmL.Restore();
            if (ArmR.Present) ArmR.Restore();

            _facing = facing;
            _collapse = _recover = _lowTimer = _turnWait = _splitT = 0f;
            _backSteps = 0;
            _nextLeg = Leg.Either;
            Stability = 1f;

            Vector3 side = rot * Vector3.right * StanceWidth;
            if (LegL.Present) LegL.ForcePlant(Pelvis.position - side);
            if (LegR.Present) LegR.ForcePlant(Pelvis.position + side);

            if (_cloth != null) _cloth.Reseed();
        }

        /// Say which limbs have a player in them. The rest are switched off, so the
        /// disguise is visibly short of an arm or a leg rather than dragging a dead one.
        public void SetLimbs(bool legL, bool legR, bool armL, bool armR)
        {
            bool hadL = LegL.Present, hadR = LegR.Present;

            LegL.SetPresent(legL);
            LegR.SetPresent(legR);
            ArmL.SetPresent(armL);
            ArmR.SetPresent(armR);

            Legs = (legL ? 1 : 0) + (legR ? 1 : 0);
            Arms = (armL ? 1 : 0) + (armR ? 1 : 0);
            _carry = -1f;   // a limb just came or went, so re-add the load

            // Taking turns needs two of them. On one leg you have to be allowed to hop.
            _nextLeg = Leg.Either;
            _turnWait = 0f;

            // Only a leg that has just turned up needs putting on the floor. Doing it
            // to one that is already walking would yank it out from under a stride.
            if (legL && !hadL) LegL.ForcePlant(FootSpot(-1f));
            if (legR && !hadR) LegR.ForcePlant(FootSpot(1f));
        }

        Vector3 FootSpot(float side)
        {
            Vector3 across = Vector3.Cross(Vector3.up, _facing).normalized;
            return Pelvis.position + across * (side * StanceWidth);
        }

        void FixedUpdate()
        {
            if (ExternalSimulation) return;
            Tick(Time.fixedDeltaTime);
        }

        public void Tick(float dt)
        {
            Tick(Input != null ? Input.States : null, dt);
        }

        /// Network-safe entry point. The simulation consumes the supplied four-role
        /// input instead of reaching into LocalCoatInput, so Fusion can feed the exact
        /// host-authoritative inputs received from the four players.
        public void Tick(CoatInputState[] states, float dt)
        {
            float camYaw = CameraRef != null ? CameraRef.eulerAngles.y : 0f;

            if (_collapse > 0f)
            {
                _collapse -= dt;
                if (_collapse <= 0f) BeginRecover();
                return;
            }

            float ramp = 1f;
            if (_recover > 0f)
            {
                _recover -= dt;
                ramp = Mathf.Clamp01(1f - _recover / Mathf.Max(0.01f, RecoverSeconds));
            }

            if (_nextLeg != Leg.Either)
            {
                _turnWait += dt;
                if (_turnWait > TurnTimeout) { _nextLeg = Leg.Either; _turnWait = 0f; }
            }

            // A step is planned against the lowest the hips will sink to, not against
            // where they happen to be: the body comes DOWN to meet a wide stride, so
            // judging the step from a tall stance vetoes strides it can easily take.
            float lowPelvis = GroundUnderBody() + MinStandHeight;
            LegL.LowestHipY = lowPelvis - HipBelowPelvis(LegL);
            LegR.LowestHipY = lowPelvis - HipBelowPelvis(LegR);

            // And the height the body has actually committed to, from last tick, which
            // is what decides whether a foot already on the floor is still in reach.
            if (_standWanted > 0f)
            {
                LegL.PlanHipY = _standWanted - HipBelowPelvis(LegL);
                LegR.PlanHipY = _standWanted - HipBelowPelvis(LegR);
            }

            // Taking turns is only a rule when there are two legs to take them.
            bool takeTurns = AlternatingSteps && Legs == 2;

            var l = GetInput(states, CoatRole.LeftLeg);
            var r = GetInput(states, CoatRole.RightLeg);

            // The splits. Two leg players pulling opposite ways drag their own
            // feet apart, and after a moment the legs stop holding the body up.
            // Checked on LIVE INPUT, not on last step directions the way the
            // observer's LEGS FIGHTING EACH OTHER tell is -- that one only
            // notices after two steps have already landed, which is far too
            // late to be something you can do on purpose.
            Vector3 lDir = Quaternion.Euler(0f, camYaw, 0f) * new Vector3(l.Move.x, 0f, l.Move.y);
            Vector3 rDir = Quaternion.Euler(0f, camYaw, 0f) * new Vector3(r.Move.x, 0f, r.Move.y);

            bool opposed = SplitsCollapse && Legs == 2
                        && LegL.Present && LegR.Present
                        && _collapse <= 0f
                        && lDir.sqrMagnitude > 0.04f && rDir.sqrMagnitude > 0.04f
                        && Vector3.Dot(lDir.normalized, rDir.normalized) < SplitDot;

            if (opposed)
            {
                _splitT += dt;
                if (_splitT > SplitSeconds)
                {
                    LegL.SlideThisTick(lDir);
                    LegR.SlideThisTick(rDir);
                }
            }
            else _splitT = 0f;

            Splitting = opposed;

            bool leftMay = !takeTurns || _nextLeg != Leg.Right;
            LegL.Tick(l.Move, l.Action, camYaw, dt, LegR.Present && LegR.Airborne, leftMay);
            if (LegL.JustStepped) { _nextLeg = Leg.Right; _turnWait = 0f; NoteStep(LegL.LastStepDir); }

            bool rightMay = !takeTurns || _nextLeg != Leg.Left;
            LegR.Tick(r.Move, r.Action, camYaw, dt, LegL.Present && LegL.Airborne, rightMay);
            if (LegR.JustStepped) { _nextLeg = Leg.Left; _turnWait = 0f; NoteStep(LegR.LastStepDir); }

            var la = GetInput(states, CoatRole.LeftArm);
            var ra = GetInput(states, CoatRole.RightArm);
            ArmL.Tick(la.Move, la.Action, camYaw, dt);
            ArmR.Tick(ra.Move, ra.Action, camYaw, dt);

            UpdateBalance(ramp, dt);

            // With no legs there is nothing to stand on and nothing to get up with, so
            // the collapse timer would only churn. It is already as down as it gets.
            if (Legs == 0) { _lowTimer = 0f; return; }

            // Down means either low or lying over. Either way it has to hold for
            // CollapseGrace, so a stumble is still just a stumble.
            float lean = Vector3.Angle(Torso.transform.up, Vector3.up);

            // Three ways to be down, and the third is the one the splits needs.
            // Slid all the way out, the body ends up propped between two
            // stretched legs like an A-frame -- head at 0.86 against a 0.78
            // threshold and the torso only 4 degrees off vertical, so neither
            // of the first two ever fires and it sits in the splits for ever.
            // No footing IS not being able to balance, so it counts.
            bool noFooting = Footing() <= 0.02f;
            bool over = Head.position.y < HeadMinHeight || lean > MaxLean || noFooting;

            if (_recover <= 0f && over) _lowTimer += dt;
            else _lowTimer = 0f;

            if (WatchFalls && lean > MaxLean && _bout <= 0f)
                Debug.LogWarning($"[coat] spine {lean:0} deg off vertical, head {Head.position.y:0.00}, " +
                                 $"hips {Pelvis.position.y:0.00}, legs {Legs}, arms {Arms}, " +
                                 $"balance {Stability:0.00}, planted {PlantedFeet}, " +
                                 $"speed {Pelvis.linearVelocity.magnitude:0.00}", this);
            _bout = lean > MaxLean ? _bout + dt : 0f;

            if (_lowTimer > CollapseGrace) Collapse();
        }

        static CoatInputState GetInput(CoatInputState[] states, CoatRole role)
        {
            int i = (int)role;
            return states != null && i >= 0 && i < states.Length ? states[i] : default;
        }

        void UpdateBalance(float ramp, float dt)
        {
            // Foot TARGETS, not where the feet physically are: a dragged foot would move
            // the support point, which moves the body, which drags the foot further.
            Vector3 support = Vector3.zero;
            float weight = 0f;
            PlantedFeet = 0;

            // Each foot's POSITION carries its own weight. It used to add the position
            // unweighted and divide by the total weight, which is only a midpoint when
            // both feet happen to weigh the same.
            //
            // With one foot in the air the weights are 1 and 0.45, so it computed
            // (a + b) / 1.45 -- the true midpoint multiplied by 1.38. Multiplying a
            // WORLD position scales it away from the world origin, so the body spent
            // most of every stride chasing a point flung off towards wherever it
            // happened to be standing relative to (0,0,0). Walking out at z = 3 that
            // is about 65 cm of phantom pull, and because it points at a fixed spot in
            // the level rather than anywhere on the body, it reads as an invisible
            // force dragging you off course -- worst when strafing, because then it is
            // across your path rather than along it.
            if (LegL.Present)
            {
                float w = LegL.Planted ? 1f : SwingWeight;
                support += LegL.StepTarget * w;
                weight += w;
                if (LegL.Planted) PlantedFeet++;
            }
            if (LegR.Present)
            {
                float w = LegR.Planted ? 1f : SwingWeight;
                support += LegR.StepTarget * w;
                weight += w;
                if (LegR.Planted) PlantedFeet++;
            }

            // Nothing to stand on. Let it lie there; the arms still work.
            if (Legs == 0 || weight <= 0.01f) { Stability = 0f; return; }
            support /= weight;

            Vector3 lean = Pelvis.position - support;
            lean.y = 0f;
            Stability = Mathf.Clamp01(1f - lean.magnitude / ToppleRadius);
            if (PlantedFeet == 1) Stability *= OneFootPenalty;

            // Short handed: hopping is precarious, and arms are how you catch yourself.
            // This has to reach the FLOORS below as well as the stability number. The
            // floors are what hold the thing up no matter how badly it is leaning, so
            // leaving them alone gives a one legged coat 0.2 balance and a rock solid
            // stance, which is the opposite of the point.
            // Deliberately NOT multiplied together. Anyone hopping on one leg is short
            // of arms too, so stacking the two double counts and drops the body below
            // the point where it can stand at all: it stops hopping and just faceplants
            // over and over. One leg gets its own number instead.
            float handicap = Legs == 1
                ? 1f - OneLeggedPenalty
                : 1f - ArmlessPenalty * (2 - Arms);

            // The handicap belongs on the BALANCE READING, which is what decides how
            // close to going over you are. It must not also gut the body's ability to
            // hold itself up: with no arms the handicap is 0.3, and applying that to
            // the self-righting floor left the body with a sixth of its usual torque.
            // It could not stand, so it folded to seventy-odd degrees and walked along
            // nearly horizontal.
            Stability *= handicap;

            float authority = Stability * ramp;
            if (LegL.Present && LegL.Planted && GetInput(states, CoatRole.LeftLeg).Action) authority = Mathf.Min(1f, authority * BraceBonus);
            if (LegR.Present && LegR.Planted && GetInput(states, CoatRole.RightLeg).Action) authority = Mathf.Min(1f, authority * BraceBonus);

            float lift = _recover > 0f ? RecoverBoost : 1f;

            // Height from the ground under the body, never from the feet.
            float ground = GroundUnderBody();
            _standWanted = ground + StandFor(ground);
            Vector3 want = new Vector3(support.x, _standWanted, support.z);
            Vector3 err = Vector3.ClampMagnitude(want - Pelvis.position, MaxStandError);
            // A short crew tracks its feet a little less eagerly, but never so little
            // that it cannot follow them at all.
            float track = Mathf.Max(authority, MinTrack * Mathf.Max(handicap, 0.65f));
            Vector3 vel = Pelvis.linearVelocity;

            // Split on purpose: hold hard upwards, chase gently sideways.
            Vector3 push = new Vector3(
                err.x * track * TrackSpring - vel.x * TrackDamp,
                err.y * StandSpring - vel.y * StandDamp,
                err.z * track * TrackSpring - vel.z * TrackDamp);

            LastChase = new Vector3(push.x, 0f, push.z) * lift;
            LastSupport = support;
            LastWantErr = new Vector3(err.x, 0f, err.z);

            Pelvis.AddForce(push * lift, ForceMode.Acceleration);

            // Gravity compensation, scaled by everything the hips are actually holding
            // up. The force below goes on with ForceMode.Acceleration, which knows only
            // the pelvis's own 12 kg, while the body above it weighs more than fifty --
            // so a "one g" assist lifted a fifth of what it had to, and the stance
            // spring was left carrying the whole body on its error term. It sat seven
            // centimetres low for ever and the knees bent to match, which is the squat.
            float hold = Mathf.Max(authority, MinHipAssist * ramp) * Footing();
            float carry = Carrying() / Mathf.Max(0.01f, Pelvis.mass);
            Pelvis.AddForce(Vector3.up * (9.81f * HipAssist * hold * carry * lift),
                            ForceMode.Acceleration);

            Vector3 v = Pelvis.linearVelocity;
            Vector3 hv = new Vector3(v.x, 0f, v.z);
            float speed = hv.magnitude;
            float topSpeed = MaxWalkSpeed * (Legs == 1 ? HopSpeed : 1f);
            LastBrake = Vector3.zero;
            if (speed > topSpeed)
            {
                Vector3 slow = -hv / speed * ((speed - topSpeed) * OverspeedDamp);
                LastBrake += slow;
                Pelvis.AddForce(slow, ForceMode.Acceleration);
            }

            // Standing still should actually stand still. Only once nothing is mid-step,
            // so the swinging foot can still carry the body forward the way it always has.
            bool settled = !(LegL.Present && LegL.Airborne) && !(LegR.Present && LegR.Airborne);
            if (settled && PlantedFeet > 0)
            {
                Vector3 drag = -hv * (StanceFriction * PlantedFeet * 0.5f);
                LastBrake += drag;
                Pelvis.AddForce(drag, ForceMode.Acceleration);
            }
            if (v.y > MaxRiseSpeed)
                Pelvis.AddForce(Vector3.down * ((v.y - MaxRiseSpeed) * 14f), ForceMode.Acceleration);

            // No handicap here. Standing upright is not a privilege of a full crew.
            float rise = Mathf.Max(authority, MinUpright * ramp);
            Quaternion desired = Quaternion.LookRotation(Facing(dt), Vector3.up);
            Upright(Pelvis, desired, rise);
            Upright(Torso, desired, rise * 0.7f);
        }

        /// The body faces where its legs are stepping. Taking it from the line between
        /// the feet had a stable wrong answer about 45 degrees off, because the body
        /// would rotate until that fore-and-aft biased line looked square on.
        Vector3 Facing(float dt)
        {
            Vector3 intent = Vector3.zero;
            if (LegL.Present && LegL.StepDirAge < StepMemory) intent += LegL.LastStepDir;
            if (LegR.Present && LegR.StepDirAge < StepMemory) intent += LegR.LastStepDir;

            // Deliberately leaves the backstep count alone. Zeroing it whenever the
            // legs went quiet for a moment is precisely what made the old timer reset
            // for ever on any cadence slower than the step memory.
            if (intent.sqrMagnitude <= 0.04f) return _facing;

            Vector3 dir = intent.normalized;
            bool opposed = Vector3.Dot(dir, _facing) < -BackstepDot;

            // Only the wedge directly behind the body is refused, and only until enough
            // steps have gone into it. Everything else steers freely, so once the turn
            // is under way it runs to completion instead of stalling half way round.
            if (!opposed || _backSteps >= BackstepAllowance)
                _facing = Vector3.RotateTowards(_facing, dir, TurnRate * Mathf.Deg2Rad * dt, 0f);

            if (!opposed) _backSteps = 0;

            return _facing;
        }

        /// Called once per step, not once per tick: how long you have been pressing
        /// against your own facing says nothing, because the cadence depends on how
        /// many players are aboard, but how many steps you have taken says exactly
        /// what it should.
        void NoteStep(Vector3 dir)
        {
            if (Vector3.Dot(dir, _facing) < -BackstepDot) _backSteps++;
            else _backSteps = 0;
        }

        /// How tall the body may stand right now, given how far apart its feet are.
        ///
        /// A person standing still has nearly straight legs and bends them as the
        /// stride opens; the hips rise and fall through every step. One fixed height
        /// had to be low enough for the widest stride the legs would ever take, which
        /// is why the disguise squatted its way through the entire round -- and simply
        /// raising it only left the legs unable to reach the ground in front of them.
        float StandFor(float ground)
        {
            float tall = StandHeight;
            if (LegL.Present) tall = Mathf.Min(tall, Room(LegL, ground));
            if (LegR.Present) tall = Mathf.Min(tall, Room(LegR, ground));
            // The floor under the crouch fades with the footing. Held at a flat
            // MinStandHeight it kept the hips up no matter how splayed the legs
            // were.
            return Mathf.Max(tall, MinStandHeight * Footing());
        }

        /// The tallest the pelvis can be without asking this leg for more than it has.
        /// How much the legs can still hold up, 1 down to 0.
        ///
        /// 1 while a foot is under its hip; 0 once it has slid a full leg span
        /// away and the leg is pointing flat sideways with nothing left to push
        /// with. Everything that props the body up is scaled by this, because
        /// both props assume the legs can get under you: MinStandHeight puts a
        /// floor under the crouch and MinHipAssist keeps lifting whatever
        /// happens. With those unconditional, the body stood in a 1.66 m
        /// stance -- wider than the 1.54 m its two legs can even span -- for
        /// eight seconds, head barely dropping and torso 4 degrees off
        /// vertical. Nothing could ever make it fall.
        float Footing()
        {
            float worst = 1f;
            if (LegL.Present) worst = Mathf.Min(worst, Footing(LegL));
            if (LegR.Present) worst = Mathf.Min(worst, Footing(LegR));
            return worst;
        }

        float Footing(ClassicLimb leg)
        {
            Vector3 hip = Pelvis.transform.TransformPoint(leg.RootAnchorLocal);
            Vector3 flat = hip - leg.StepTarget;
            flat.y = 0f;

            float span = (leg.UpperLen + leg.LowerLen) * StandExtend;
            return Mathf.Clamp01(1f - flat.magnitude / Mathf.Max(0.01f, span));
        }

        float Room(ClassicLimb leg, float ground)
        {
            Vector3 hip = Pelvis.transform.TransformPoint(leg.RootAnchorLocal);
            Vector3 flat = hip - leg.StepTarget;
            flat.y = 0f;

            float span = (leg.UpperLen + leg.LowerLen) * StandExtend;
            float drop = Mathf.Sqrt(Mathf.Max(0.0025f, span * span - flat.sqrMagnitude));

            return leg.StepTarget.y + drop + HipBelowPelvis(leg) - ground;
        }

        /// How far the hip sits below the pelvis, in metres.
        ///
        /// Not RootAnchorLocal.y. That is a point in the pelvis's own LOCAL space, and
        /// the pelvis is a cube scaled to 0.20 tall, so its -0.08 metre hip offset is
        /// stored there as -0.40. Read raw it put the hip four times too far down, the
        /// legs were told they could plant a foot most of a metre away, and the body
        /// then had to crouch to reach the feet it had flung out.
        float HipBelowPelvis(ClassicLimb leg) =>
            Pelvis.position.y - Pelvis.transform.TransformPoint(leg.RootAnchorLocal).y;

        /// Total mass of the bones that are switched on, less the feet, which the
        /// floor is holding up rather than the hips. Recomputed only when the crew
        /// changes, because it cannot change at any other time.
        float Carrying()
        {
            if (_carry >= 0f) return _carry;

            _carry = 0f;
            foreach (var rb in _bones)
            {
                if (rb == null || !rb.gameObject.activeInHierarchy) continue;
                if (rb == LegL.End || rb == LegR.End) continue;
                _carry += rb.mass;
            }
            return _carry;
        }

        float GroundUnderBody()
        {
            if (Physics.Raycast(Pelvis.position, Vector3.down, out RaycastHit hit, 6f,
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

        public void Collapse()
        {
            if (_collapse > 0f) return;
            _collapse = CollapseSeconds;
            _lowTimer = 0f;
            Stability = 0f;
            LegL.DropEverything(); LegR.DropEverything();
            ArmL.DropEverything(); ArmR.DropEverything();
        }

        void BeginRecover()
        {
            _recover = RecoverSeconds;
            _lowTimer = 0f;
            _nextLeg = Leg.Either;

            Vector3 p = Pelvis.position;
            Vector3 fwd = Vector3.ProjectOnPlane(Pelvis.transform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            Quaternion frame = Quaternion.LookRotation(fwd.normalized, Vector3.up);

            if (LegL.Present) LegL.ForcePlant(p + frame * new Vector3(-StanceWidth, 0f, 0f));
            if (LegR.Present) LegR.ForcePlant(p + frame * new Vector3(StanceWidth, 0f, 0f));
        }
    }
}
