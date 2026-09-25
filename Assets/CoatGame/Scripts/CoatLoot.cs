using UnityEngine;

namespace Coat
{
    /// The thing the round is actually about: something the crew has to pick up out
    /// there and carry home to the van.
    ///
    /// It is deliberately long and deliberately not very heavy. Nothing here forbids
    /// carrying it one handed -- a single hand grabs one end, the other end drops, and
    /// it drags along the floor looking exactly as wrong as it is. Two hands hold it
    /// level. The rule is the shape of the object, not an if statement, which means
    /// the players work it out by looking rather than by being told.
    ///
    /// It also knows nothing about being carried. It asks the hands what they are
    /// holding, so an arm has no idea a prop exists and a prop has no idea what an arm
    /// is. Falling over drops it for free: Collapse already calls DropEverything.
    public class CoatLoot : MonoBehaviour
    {
        public CoatGame Game;
        public CoatVan Van;
        public Rigidbody Body;

        [Tooltip("What the readout calls it. The wedding's tray, the bank's cash box " +
                 "and the takeaway's order are all this same prop wearing a hat.")]
        public string Called = "the cake";

        [Tooltip("How far it may be from where it started before it counts as having " +
                 "been picked up at all. Stops a nudge reading as a heist.")]
        public float Moved = 0.4f;

        /// How many hands are on it right now, counting the disguise's two and any
        /// loose player who has grabbed it.
        public int Hands { get; private set; }
        public bool Carried => Hands > 0;

        /// In the van, which is the only place that counts as delivered.
        public bool Home { get; private set; }

        /// It has been home once. Kept latched so setting it down inside and stepping
        /// away does not un-deliver it.
        public bool Delivered { get; private set; }

        /// Times it has been dropped after being properly carried, which is the
        /// number the players will argue about afterwards.
        public int Fumbles { get; private set; }

        public Vector3 Started { get; private set; }
        public bool Disturbed => (Body.position - Started).sqrMagnitude > Moved * Moved;

        int _wasHands;
        bool _everHeld;

        void Awake()
        {
            if (Body == null) Body = GetComponent<Rigidbody>();
            Started = Body != null ? Body.position : transform.position;
        }

        /// Driven by CoatGame, like everything else, so there is still one owner.
        public void Tick(float dt)
        {
            if (Body == null) return;

            Hands = CountHands();
            if (Hands >= 1) _everHeld = true;

            // Losing every hand after having had at least two is a fumble. One hand
            // letting go of a two handed carry is just the other player taking the
            // weight, and should not count.
            if (_wasHands >= 2 && Hands == 0 && _everHeld && !Delivered) Fumbles++;
            _wasHands = Hands;

            Home = Van != null && Van.Inside(Body.position);
            if (Home) Delivered = true;
        }

        int CountHands()
        {
            int n = 0;

            var v = Game != null ? Game.Vehicle : null;
            if (v != null && v.Worn && v.Body != null)
            {
                if (v.Body.ArmL != null && v.Body.ArmL.Held == Body) n++;
                if (v.Body.ArmR != null && v.Body.ArmR.Held == Body) n++;
            }

            if (Game != null && Game.Characters != null)
                foreach (var c in Game.Characters)
                    if (c != null && c.gameObject.activeInHierarchy)
                        n += c.HandsOn(Body);

            return n;
        }

        /// Put it back where it started, for running the round again.
        public void Reset()
        {
            if (Body == null) return;

            Body.transform.SetPositionAndRotation(Started, Quaternion.identity);
            Body.position = Started;
            Body.rotation = Quaternion.identity;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;

            Hands = 0;
            _wasHands = 0;
            _everHeld = false;
            Home = false;
            Delivered = false;
            Fumbles = 0;
        }

        public string Readout()
        {
            if (Delivered) return $"<color=#8ce87a>{Called} is in the van</color>";
            if (Hands >= 2) return $"<color=#8ce87a>carrying {Called}</color>   both hands";
            if (Hands == 1) return $"<color=#ffd24a>{Called}, one handed</color>   <i>it will drag</i>";
            if (Disturbed) return $"<color=#ff5b5b>{Called} is on the floor</color>";
            return $"{Called} is still on the table";
        }
    }
}
