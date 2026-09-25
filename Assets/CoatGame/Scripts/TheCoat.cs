using UnityEngine;

namespace Coat
{
    /// The coat. A real object lying in the world with four places inside it. Walk up,
    /// press your coat key, and you are joined in; press it again to climb out. Two at
    /// the bottom become the legs, two above them sit on their shoulders and become the
    /// arms.
    ///
    /// The load path matters: the ones on top are joined to the ones underneath, who are
    /// standing on the ground. An earlier version hung everybody off a floating hub in
    /// the middle, and since the legs pushed it up exactly as hard as the arms pulled it
    /// down, the whole thing settled into a flat pile instead of a stack.
    ///
    /// The joints are slack on purpose. Four people crammed into one garment should
    /// shift and wobble against each other, not move like one welded prop.
    public class TheCoat : MonoBehaviour
    {
        [Tooltip("Follows the wearers and carries the cloth. Physical only while the coat " +
                 "is lying empty on the floor.")]
        public Rigidbody Hub;
        public Transform Collar;
        public Transform Hem;

        [Header("Fit")]
        [Tooltip("How high the ones on top ride above the ones underneath. Commanded, not " +
                 "achieved: the slack seat sags, so they end up roughly three quarters of this.")]
        public float ShoulderHeight = 0.68f;
        [Tooltip("How far the two legs may drift apart before the coat pulls them back.")]
        public float HipSpread = 0.44f;
        [Tooltip("Slack on a rider's seat. Larger is baggier and funnier, and less stable.")]
        public float Slack = 0.1f;
        public float SeatSpring = 16000f;
        public float SeatDamper = 240f;
        public float HipSpring = 2200f;
        public float HipDamper = 90f;
        public float Twist = 30f;

        [Header("Mode")]
        [Tooltip("Off means the coat is a VEHICLE: it tracks who is aboard and nothing " +
                 "else, and CoatVehicle stands the real body up once all four are in. " +
                 "On is the old behaviour, where the wearers are joined to each other " +
                 "with springs and the coat balances them as a pair. Kept behind a flag " +
                 "because that whole system is one bool away if it is ever wanted back.")]
        public bool Assemble;

        [Header("Walking")]
        [Tooltip("The two legs must take turns: neither can step twice in a row. This is " +
                 "what makes them sync up instead of one player dragging the coat about.")]
        public bool AlternatingSteps = true;
        [Tooltip("If whoever's turn it is does not step within this long, the turn frees " +
                 "up, so a partner who is busy or has climbed out cannot lock you solid.")]
        public float TurnTimeout = 1.5f;

        [Header("Shared balance")]
        [Tooltip("Hip height above the planted feet. Once both legs are in, the pair is " +
                 "balanced as ONE body over the average of their two feet, instead of two " +
                 "people each keeping themselves up. That shared teetering is the feel.")]
        public float SharedStandHeight = 0.46f;
        [Tooltip("How far the hips may drift off the feet before balance is gone.")]
        public float ToppleRadius = 0.5f;
        [Range(0f, 1f)] public float MinTrack = 0.5f;
        [Range(0f, 1f)] public float MinUpright = 0.32f;
        [Tooltip("How much the riders lean along with the whole thing.")]
        [Range(0f, 1f)] public float RiderLean = 0.4f;

        /// 1 is balanced, 0 is about to go over. Shared by everyone in the coat.
        public float Stability { get; private set; } = 1f;

        [Header("Looking like one person")]
        [Tooltip("The head the disguise shows the world. The riders hide their own.")]
        public GameObject DisguiseHead;

        [Header("Getting in")]
        [Tooltip("How close you must stand to climb in, measured ALONG THE GROUND.\n" +
                 "It used to be a straight line to the hub, and the hub rides at chest " +
                 "height on a standing body -- so the moment the first player was in " +
                 "and the disguise stood up, most of the allowance was spent climbing " +
                 "and the rest of the crew could not reach it from one step away. " +
                 "Standing five centimetres taller was enough to lock a player out.")]
        public float EnterRadius = 1.3f;
        [Tooltip("How far above or below the hub you may be and still climb in. Only " +
                 "there to stop someone boarding from a balcony.")]
        public float EnterHeight = 1.8f;
        public float ReentryDelay = 0.6f;
        [Tooltip("Where the hub rides above the legs, for the cloth and for what the " +
                 "observer thinks it is looking at.")]
        public float HubHeight = 0.34f;

        CoatCloth _cloth;
        Collider _hubCollider;

        void Awake()
        {
            _cloth = GetComponentInChildren<CoatCloth>(true);
            _hubCollider = Hub != null ? Hub.GetComponent<Collider>() : null;
        }

        /// Once anybody is wearing it, the coat IS the body: the heap's own cloth goes
        /// away, and the hub rides along so that "walk over to the coat and climb in"
        /// still has somewhere to mean.
        public void RideOn(Rigidbody carrier, float height)
        {
            if (Hub == null || carrier == null) return;

            if (!Hub.isKinematic) Hub.isKinematic = true;
            if (_hubCollider != null && _hubCollider.enabled) _hubCollider.enabled = false;
            if (_cloth != null && _cloth.gameObject.activeSelf) _cloth.gameObject.SetActive(false);

            Vector3 p = carrier.position + Vector3.up * height;
            Hub.position = p;
            Hub.rotation = carrier.rotation;
            Hub.transform.SetPositionAndRotation(p, carrier.rotation);
        }

        /// Back on the floor as a heap somebody can climb into.
        public void BecomeHeap()
        {
            if (_cloth != null && !_cloth.gameObject.activeSelf) _cloth.gameObject.SetActive(true);
            if (_hubCollider != null) _hubCollider.enabled = true;
        }

        readonly CoatCharacter[] _wearers = new CoatCharacter[4];
        readonly float[] _cooldown = new float[4];
        readonly System.Collections.Generic.List<ConfigurableJoint> _joints =
            new System.Collections.Generic.List<ConfigurableJoint>();

        public CoatCharacter WearerAt(CoatRole role) => _wearers[(int)role];
        public bool Occupied(CoatRole role) => _wearers[(int)role] != null;

        public int Count
        {
            get { int n = 0; foreach (var w in _wearers) if (w != null) n++; return n; }
        }

        public bool FullyWorn => Count == 4;

        /// Where the disguise appears to be standing.
        public Vector3 Centre => Hub != null ? Hub.position : transform.position;

        public Vector3 Facing
        {
            get
            {
                Vector3 f = Vector3.zero;
                var l = _wearers[(int)CoatRole.LeftLeg];
                var r = _wearers[(int)CoatRole.RightLeg];
                if (l != null) f += l.Facing;
                if (r != null) f += r.Facing;
                if (f.sqrMagnitude < 1e-4f) f = Hub != null ? Hub.transform.forward : Vector3.forward;
                f.y = 0f;
                return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            }
        }

        /// How badly the legs are pulling against each other, 0 to 1. The signature tell.
        public float LegDisagreement
        {
            get
            {
                var l = _wearers[(int)CoatRole.LeftLeg];
                var r = _wearers[(int)CoatRole.RightLeg];
                if (l == null || r == null) return 0f;

                Vector3 a = l.Body.linearVelocity; a.y = 0f;
                Vector3 b = r.Body.linearVelocity; b.y = 0f;
                float sa = a.magnitude, sb = b.magnitude;
                if (sa < 0.25f || sb < 0.25f) return 0f;

                float opposed = Mathf.Clamp01(-Vector3.Dot(a / sa, b / sb));
                return opposed * Mathf.Clamp01(Mathf.Min(sa, sb) / 1.2f);
            }
        }

        /// How far the worst wearer has strained from where they should be sitting.
        public float Strain
        {
            get
            {
                float worst = 0f;
                for (int i = 0; i < 4; i++)
                {
                    var w = _wearers[i];
                    if (w == null) continue;
                    Vector3 want = SeatFor((CoatRole)i);
                    if (want == Vector3.zero) continue;
                    worst = Mathf.Max(worst, Vector3.Distance(w.Body.position, want));
                }
                return worst;
            }
        }

        /// Where a given role ought to be right now, in world space.
        public Vector3 SeatFor(CoatRole role)
        {
            switch (role)
            {
                case CoatRole.LeftArm:
                case CoatRole.RightArm:
                    var under = Carrier(role);
                    return under != null ? under.Body.position + Vector3.up * ShoulderHeight : Vector3.zero;
                default:
                    var other = _wearers[role == CoatRole.LeftLeg ? (int)CoatRole.RightLeg : (int)CoatRole.LeftLeg];
                    return other != null ? other.Body.position : Vector3.zero;
            }
        }

        /// Whoever an arm is riding on: their own side by preference, the other leg if not.
        CoatCharacter Carrier(CoatRole arm)
        {
            var same = _wearers[arm == CoatRole.LeftArm ? (int)CoatRole.LeftLeg : (int)CoatRole.RightLeg];
            if (same != null) return same;
            return _wearers[arm == CoatRole.LeftArm ? (int)CoatRole.RightLeg : (int)CoatRole.LeftLeg];
        }

        /// Driven by CoatGame rather than by its own FixedUpdate, so the whole simulation
        /// has one owner and a test harness (or Fusion) can step it deterministically.
        public void Tick(float dt)
        {
            for (int i = 0; i < 4; i++)
                if (_cooldown[i] > 0f) _cooldown[i] -= dt;

            if (_nextLeg != Leg.Either)
            {
                _turnWait += dt;
                if (_turnWait > TurnTimeout) { _nextLeg = Leg.Either; _turnWait = 0f; }
            }

            if (!Assemble) return;   // a vehicle: occupancy only, no linkage of our own

            BalanceTogether();
            RideHub();
        }

        /// One balance for the pair. Each hip still stands over its own foot, but the
        /// authority behind it comes from how the PAIR is leaning, so either player can
        /// destabilise the whole disguise and both have to save it.
        void BalanceTogether()
        {
            var l = _wearers[(int)CoatRole.LeftLeg];
            var r = _wearers[(int)CoatRole.RightLeg];

            if (l == null || r == null) { Stability = 1f; return; }

            Vector3 support = (l.FootTarget + r.FootTarget) * 0.5f;
            Vector3 hips = (l.Body.position + r.Body.position) * 0.5f;

            Vector3 lean = hips - support;
            lean.y = 0f;
            Stability = Mathf.Clamp01(1f - lean.magnitude / Mathf.Max(0.01f, ToppleRadius));

            l.SetStability(Stability);
            r.SetStability(Stability);

            float track = Mathf.Max(Stability, MinTrack);
            float rise = Mathf.Max(Stability, MinUpright);
            float assist = Mathf.Max(Stability, 0.4f);
            float groundY = l.GroundUnderBody();
            Quaternion desired = Quaternion.LookRotation(Facing, Vector3.up);

            Hold(l, groundY, track, rise, assist, desired);
            Hold(r, groundY, track, rise, assist, desired);

            // The riders lean along with it rather than holding themselves plumb.
            var la = _wearers[(int)CoatRole.LeftArm];
            var ra = _wearers[(int)CoatRole.RightArm];
            if (la != null) la.DriveUpright(desired, rise * RiderLean);
            if (ra != null) ra.DriveUpright(desired, rise * RiderLean);

            // One body, so it goes down as one.
            if (l.Collapsed || r.Collapsed)
                foreach (var w in _wearers) if (w != null) w.Collapse();
        }

        void Hold(CoatCharacter leg, float groundY, float track, float rise, float assist, Quaternion desired)
        {
            Vector3 want = new Vector3(leg.FootTarget.x, groundY + SharedStandHeight, leg.FootTarget.z);
            leg.DriveStand(want, track, assist, 1f);
            leg.DriveUpright(desired, rise);
        }

        public enum Leg { Either, Left, Right }
        /// Whose turn it is to step. Either means whoever moves first.
        public Leg NextLeg => _nextLeg;

        Leg _nextLeg = Leg.Either;
        float _turnWait;

        public bool MayStep(CoatRole role)
        {
            if (!AlternatingSteps) return true;
            if (role == CoatRole.LeftLeg) return _nextLeg != Leg.Right;
            if (role == CoatRole.RightLeg) return _nextLeg != Leg.Left;
            return true;
        }

        public void RegisterStep(CoatRole role)
        {
            if (role == CoatRole.LeftLeg) _nextLeg = Leg.Right;
            else if (role == CoatRole.RightLeg) _nextLeg = Leg.Left;
            _turnWait = 0f;
        }

        /// While anyone is wearing it the hub is carried, not simulated: it exists to
        /// hang the cloth on and to give the observer something to look at.
        void RideHub()
        {
            if (Hub == null) return;

            if (Count == 0)
            {
                if (Hub.isKinematic) Hub.isKinematic = false;
                return;
            }

            if (!Hub.isKinematic) Hub.isKinematic = true;

            Vector3 basePos = Vector3.zero;
            int n = 0;
            var l = _wearers[(int)CoatRole.LeftLeg];
            var r = _wearers[(int)CoatRole.RightLeg];
            if (l != null) { basePos += l.Body.position; n++; }
            if (r != null) { basePos += r.Body.position; n++; }

            if (n == 0)
            {
                foreach (var w in _wearers)
                    if (w != null) { basePos += w.Body.position; n++; }
            }
            if (n == 0) return;

            Hub.position = basePos / n + Vector3.up * HubHeight;
            Hub.rotation = Quaternion.LookRotation(Facing, Vector3.up);
        }

        public void Toggle(CoatCharacter who)
        {
            if (who == null) return;
            if (who.InCoat) Leave(who);
            else TryEnter(who);
        }

        public bool TryEnter(CoatCharacter who)
        {
            int i = (int)who.Role;
            if (_wearers[i] != null || _cooldown[i] > 0f) return false;
            Vector3 gap = who.Body.position - Centre;
            float climb = Mathf.Abs(gap.y);
            gap.y = 0f;
            if (gap.magnitude > EnterRadius || climb > EnterHeight) return false;

            _wearers[i] = who;
            who.SetCoat(this);
            if (Assemble) { Rewire(); Dress(); }
            return true;
        }

        public void Leave(CoatCharacter who)
        {
            int i = (int)who.Role;
            if (_wearers[i] != who) return;

            _wearers[i] = null;
            _cooldown[i] = ReentryDelay;
            who.SetCoat(null);
            who.SharedBalance = false;
            who.DropEverything();
            who.SetHeadVisible(true);
            if (_nextLeg == (who.Role == CoatRole.LeftLeg ? Leg.Left : Leg.Right)) _nextLeg = Leg.Either;
            if (Assemble) { Rewire(); Dress(); }
        }

        /// One head, not four. Whoever is riding on top tucks theirs inside the collar
        /// and the coat shows its own instead; the legs are under the hem anyway.
        void Dress()
        {
            bool anyRider = Occupied(CoatRole.LeftArm) || Occupied(CoatRole.RightArm);

            for (int i = 0; i < 4; i++)
            {
                var w = _wearers[i];
                if (w == null) continue;
                w.SetHeadVisible(false);
            }

            if (DisguiseHead != null) DisguiseHead.SetActive(anyRider);
        }

        /// Occupancy changed, so rebuild the whole linkage. Cheap, and much easier to
        /// reason about than patching individual joints as people come and go.
        void Rewire()
        {
            foreach (var j in _joints) if (j != null) Destroy(j);
            _joints.Clear();

            var ll = _wearers[(int)CoatRole.LeftLeg];
            var rl = _wearers[(int)CoatRole.RightLeg];

            // The two legs are tied at the hip, loosely enough to take a stride.
            if (ll != null && rl != null)
                _joints.Add(Tie(ll.Body, rl.Body, Vector3.zero, HipSpread, HipSpring, HipDamper));

            // Both legs present means the coat balances them as one body from now on.
            bool pair = ll != null && rl != null;
            for (int i = 0; i < 4; i++)
                if (_wearers[i] != null)
                    _wearers[i].SharedBalance = pair && _wearers[i].IsLegRole;

            Seat(CoatRole.LeftArm);
            Seat(CoatRole.RightArm);
        }

        void Seat(CoatRole arm)
        {
            var rider = _wearers[(int)arm];
            if (rider == null) return;

            var carrier = Carrier(arm);
            if (carrier == null) return;   // nobody underneath; they just stand in it

            // connectedAnchor lives in the carrier's LOCAL space, which is scaled. The
            // carrier's capsule has a y scale of about 0.31, so passing the shoulder
            // height straight in shrank it to a third and seated the rider inside them.
            Vector3 seat = carrier.Body.transform.InverseTransformPoint(
                carrier.Body.position + Vector3.up * ShoulderHeight);

            _joints.Add(Tie(rider.Body, carrier.Body, seat, Slack, SeatSpring, SeatDamper));
        }

        /// Joins two bodies with slack. connectedAnchor is where on the carrier the
        /// rider should sit, in the carrier's local space.
        ConfigurableJoint Tie(Rigidbody body, Rigidbody to, Vector3 seat, float slack, float spring, float damper)
        {
            var j = body.gameObject.AddComponent<ConfigurableJoint>();
            j.connectedBody = to;
            j.autoConfigureConnectedAnchor = false;
            j.anchor = Vector3.zero;
            j.connectedAnchor = seat;

            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Limited;
            j.linearLimit = new SoftJointLimit { limit = slack };
            j.linearLimitSpring = new SoftJointLimitSpring { spring = spring, damper = damper };

            j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Limited;
            j.lowAngularXLimit = new SoftJointLimit { limit = -Twist };
            j.highAngularXLimit = new SoftJointLimit { limit = Twist };
            j.angularYLimit = new SoftJointLimit { limit = Twist };
            j.angularZLimit = new SoftJointLimit { limit = Twist };

            j.enablePreprocessing = false;
            j.projectionMode = JointProjectionMode.PositionAndRotation;
            j.projectionDistance = 0.1f;
            j.projectionAngle = 25f;
            return j;
        }

        /// Everybody out, with no assembly to unpick. Used when the crew hands over to
        /// the real body, and again when they climb back out of it.
        public void Vacate()
        {
            foreach (var j in _joints) if (j != null) Destroy(j);
            _joints.Clear();

            for (int i = 0; i < 4; i++)
            {
                var w = _wearers[i];
                _wearers[i] = null;
                _cooldown[i] = ReentryDelay;
                if (w == null) continue;

                w.SetCoat(null);
                w.SharedBalance = false;
                w.DropEverything();
                w.SetHeadVisible(true);
            }

            _nextLeg = Leg.Either;
            _turnWait = 0f;
            if (DisguiseHead != null) DisguiseHead.SetActive(false);
        }

        /// Dump the empty coat on the floor at a spot, as a heap to be climbed back into.
        public void DropAt(Vector3 where, Vector3 facing)
        {
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-4f) facing = Vector3.forward;

            Quaternion rot = Quaternion.LookRotation(facing.normalized, Vector3.up);
            transform.SetPositionAndRotation(where, rot);

            BecomeHeap();

            if (Hub == null) return;
            Hub.isKinematic = false;
            Hub.position = where;
            Hub.rotation = rot;
            Hub.transform.SetPositionAndRotation(where, rot);
            Hub.linearVelocity = Vector3.zero;
            Hub.angularVelocity = Vector3.zero;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.35f);
            Gizmos.DrawWireSphere(Centre, EnterRadius);   // roughly; the real test is flat
        }
    }
}
