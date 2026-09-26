using UnityEngine;
using UnityEngine.InputSystem;

namespace Coat
{
    /// The van is the round.
    ///
    /// Four people are shut in the back of it with a coat on the floor. Nobody goes
    /// anywhere until all four are inside the coat -- the door is a real collider and
    /// it is down. When they are all aboard the shutter goes up, and that is the start
    /// whistle: the clock runs from the moment the disguise can leave.
    ///
    /// Getting back inside is the extraction. Everyone counts, whether they are riding
    /// in the coat or have climbed out and are running back on their own legs, so a
    /// crew that falls apart in the street has to gather itself up before it can go
    /// home.
    ///
    /// Deliberately the only thing in the project that knows what a "round" is. The
    /// coat does not, the body does not, and the observers do not; they all just carry
    /// on, and the van decides when that stopped mattering.
    public class CoatVan : MonoBehaviour
    {
        public CoatGame Game;
        public CoatVehicle Vehicle;
        public TheCoat Coat;
        [Tooltip("What has to come home with them. Leave it empty and the round is " +
                 "just a there-and-back.")]
        public CoatLoot Loot;

        /// Fusion owns the simulation tick while this is true.
        public bool ExternalSimulation;

        [Header("Door")]
        [Tooltip("The shutter. Slid straight up out of the way; its collider goes with " +
                 "it, which is what actually lets anybody out.")]
        public Transform Door;
        public float DoorLift = 2.35f;
        public float DoorSeconds = 1.3f;

        [Header("What counts as being in the van")]
        [Tooltip("A box in the VAN's own space, so the whole thing can be moved or " +
                 "turned and the test moves with it.")]
        public Vector3 InsideCentre = new Vector3(0f, 1.05f, 0f);
        public Vector3 InsideSize = new Vector3(2.3f, 2.2f, 3.3f);

        [Header("Where everyone starts")]
        public Vector3 CoatSeat = new Vector3(0f, 0.4f, 0.5f);
        public Vector3[] Seats =
        {
            new Vector3(-0.6f, 0f,  1.2f),
            new Vector3( 0.6f, 0f,  1.2f),
            new Vector3(-0.6f, 0f, -0.4f),
            new Vector3( 0.6f, 0f, -0.4f),
        };

        [Header("Running it again")]
        public Key ResetKey = Key.F5;

        /// Loading: shut in, getting into the coat. Opening: the shutter is going up.
        /// Away: the round is live. Back: everyone is home and the clock has stopped.
        public enum Phase { Loading, Opening, Away, Back }

        public Phase Now { get; private set; } = Phase.Loading;
        public float Elapsed { get; private set; }
        public int AtHome { get; private set; }
        public float BestTime { get; private set; } = -1f;
        /// Whether they have got clear of the van at all this round. Coming back
        /// only counts once you have been away.
        public bool HasLeft => _left;

        /// 0 shut, 1 fully up.
        public float DoorOpenness => _door;

        float _door;
        Vector3 _doorDown;
        bool _knowDoor;
        bool _resetHeld;
        bool _left;

        void Start() => Shut();

        /// Called by CoatGame so the whole simulation still has one owner.
        public void Tick(float dt)
        {
            if (Game == null || Coat == null) return;

            if (!ExternalSimulation) ReadResetKey();

            switch (Now)
            {
                case Phase.Loading:
                    // Everybody in the coat, and only then. A crew that cannot get
                    // itself dressed does not get to leave.
                    if (Coat.Count >= 4) Now = Phase.Opening;
                    break;

                case Phase.Opening:
                    _door = Mathf.MoveTowards(_door, 1f, dt / Mathf.Max(0.01f, DoorSeconds));
                    if (_door >= 1f) Now = Phase.Away;
                    break;

                case Phase.Away:
                    Elapsed += dt;
                    AtHome = CountHome();

                    // You have to actually go out before coming back means anything.
                    // Without this the round ended on the tick the door finished
                    // opening, because everybody was of course still in the van.
                    if (AtHome == 0) _left = true;

                    // Delivered, not Home: once it has been inside it stays
                    // brought back. Gating on where it is this instant meant a
                    // tray that settled and rolled a few centimetres un-delivered
                    // itself and the round would not close.
                    if (_left && AtHome >= 4 && (Loot == null || Loot.Delivered))
                    {
                        Now = Phase.Back;
                        if (BestTime < 0f || Elapsed < BestTime) BestTime = Elapsed;
                    }
                    break;

                case Phase.Back:
                    _door = Mathf.MoveTowards(_door, 0f, dt / Mathf.Max(0.01f, DoorSeconds));
                    break;
            }

            PlaceDoor();
        }

        /// Where a player is, for the purpose of being home: riding in the coat means
        /// the disguise's position, otherwise their own.
        public bool IsHome(int role)
        {
            var c = Game.Characters[role];
            if (c == null) return false;

            Vector3 where = c.InCoat && Vehicle != null && Vehicle.Worn
                ? Vehicle.Body.Pelvis.position
                : c.Body.position;

            return Inside(where);
        }

        public bool Inside(Vector3 world)
        {
            Vector3 p = transform.InverseTransformPoint(world);
            return new Bounds(InsideCentre, InsideSize).Contains(p);
        }

        int CountHome()
        {
            int n = 0;
            for (int i = 0; i < 4; i++) if (IsHome(i)) n++;
            return n;
        }

        // ---- door -------------------------------------------------------------

        void PlaceDoor()
        {
            if (Door == null) return;
            if (!_knowDoor) { _doorDown = Door.localPosition; _knowDoor = true; }
            Door.localPosition = _doorDown + Vector3.up * (DoorLift * _door);
        }

        void Shut()
        {
            _door = 0f;
            PlaceDoor();
        }

        // ---- running it again --------------------------------------------------

        void ReadResetKey()
        {
            var kb = Keyboard.current;
            bool down = kb != null && kb[ResetKey].isPressed;
            if (down && !_resetHeld) Restart();
            _resetHeld = down;
        }

        /// Everyone out of the coat, back in their seats, shutter down, clock zeroed.
        public void Restart()
        {
            Coat.Vacate();

            // Put the body away HERE, rather than leaving the vehicle to notice an
            // empty coat next tick and fold it. Its Fold() drops the coat wherever the
            // disguise happened to be standing at the end of the last round, which
            // silently undoes the seating below -- the coat ended up out in front of
            // the van and the two players in the rear seats could no longer reach it.
            // Switching the body off first means the vehicle simply believes the coat
            // is unworn and leaves it where we put it.
            if (Vehicle != null && Vehicle.Body != null)
                Vehicle.Body.gameObject.SetActive(false);

            for (int i = 0; i < 4 && i < Seats.Length; i++)
            {
                var c = Game.Characters[i];
                if (c == null) continue;
                if (!c.gameObject.activeInHierarchy) c.gameObject.SetActive(true);

                Vector3 at = transform.TransformPoint(Seats[i]);
                c.PlaceAt(at, transform.TransformPoint(CoatSeat) - at);
            }

            Coat.DropAt(transform.TransformPoint(CoatSeat), transform.forward);
            if (Loot != null) Loot.Reset();

            Now = Phase.Loading;
            Elapsed = 0f;
            AtHome = 0;
            _left = false;
            Shut();
        }

        // ---- readout ------------------------------------------------------------

        void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };

            string line;
            switch (Now)
            {
                case Phase.Loading:
                    line = $"<b>IN THE VAN</b>   {Coat.Count} of 4 in the coat   " +
                           "<i>all four and the door goes up</i>";
                    break;
                case Phase.Opening:
                    line = "<color=#ffd24a><b>DOOR GOING UP</b></color>";
                    break;
                case Phase.Away:
                    string job = Loot != null ? "   " + Loot.Readout() : "";
                    line = _left
                        ? $"<color=#8ce87a><b>OUT</b></color>   {Elapsed:0.0}s   " +
                          $"{AtHome} of 4 back{job}"
                        : $"<color=#8ce87a><b>OUT</b></color>   {Elapsed:0.0}s   " +
                          $"<i>get clear of the van</i>{job}";
                    break;
                default:
                    line = $"<color=#8ce87a><b>HOME</b></color>   {Elapsed:0.0}s" +
                           (Loot != null && Loot.Fumbles > 0
                               ? $"   <color=#ffd24a>dropped it {Loot.Fumbles}x</color>" : "") +
                           (BestTime >= 0f ? $"   best {BestTime:0.0}s" : "") +
                           $"   <i>{ResetKey} to run it again</i>";
                    break;
            }

            GUI.Label(new Rect(14f, 12f, 900f, 24f), line, style);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 0.5f, 0.35f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(InsideCentre, InsideSize);
        }
    }
}
