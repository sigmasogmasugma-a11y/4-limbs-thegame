using UnityEngine;

namespace Coat
{
    /// The coat is a vehicle, not a pile of people tied together.
    ///
    /// Four separate little people walk around the level on their own. The moment any
    /// one of them climbs in, the real body stands up wearing the coat — but only the
    /// limbs whose players are actually aboard. One leg and it hops. Two legs and it
    /// walks, badly, because there are no arms out to catch it. No legs at all and it
    /// is a torso on the floor, waving, going nowhere.
    ///
    /// That body is the original single ragdoll, untouched. Four sprung torsos can be
    /// tuned for a long time and will still not move like one skeleton, because the feel
    /// comes from everything hanging off a single rigid pelvis. So rather than imitate
    /// it, the crew hands over to it.
    public class CoatVehicle : MonoBehaviour
    {
        public CoatGame Game;
        public TheCoat Coat;
        public Classic.ClassicRagdoll Body;

        [Header("Who is watching")]
        [Tooltip("Sees people wandering about loose outside the coat.")]
        public GameObject LooseObserver;
        [Tooltip("Sees the disguise, and judges how it walks.")]
        public GameObject WornObserver;

        [Header("Readouts")]
        public CoatHud LooseHud;
        public Classic.ClassicHud WornHud;
        public CoatCamera Cam;

        [Header("Fit")]
        [Tooltip("Where the coat marker rides on the body, so that walking up to the " +
                 "disguise and pressing your key is what gets you in once it is standing.")]
        public float HubHeight = 0.42f;
        [Tooltip("How far from the coat a player pops out when they climb back out.")]
        public float ExitRadius = 0.85f;

        [Header("View")]
        [Tooltip("Untick to hide the cloth on both coats -- the heap on the floor and " +
                 "the one the body wears -- so you can watch the skeleton underneath. " +
                 "Purely what gets drawn; the simulation is untouched, so suspicion, " +
                 "collisions and the disguise all carry on exactly as before.")]
        public bool ShowCoat = true;

        /// True while the body is up, whether it has one limb in it or all four.
        public bool Worn => _worn;
        public int Crew => Coat != null ? Coat.Count : 0;

        [System.NonSerialized] bool _worn;
        [System.NonSerialized] int _mask = -1;
        readonly bool[] _armed = new bool[4];

        Renderer _heapCloth, _wornCloth;

        void Start() => Believe();

        /// Recompiling while the game is running reloads the scripting domain, which
        /// empties the coat's list of who is aboard while leaving the objects switched
        /// on exactly as they were. Start does not run again afterwards, so the one
        /// invariant that matters -- the body is up if and only if somebody is in it --
        /// gets re-checked every tick. Believe the bodies, not the bookkeeping.
        void Believe()
        {
            _worn = Body != null && Body.gameObject.activeInHierarchy;
            _mask = -1;
            Apply();
        }

        /// Driven by CoatGame so the whole simulation still has one owner.
        public void Tick(float dt)
        {
            if (Body == null || Coat == null) return;
            if (_worn != Body.gameObject.activeInHierarchy) Believe();

            Leaving();

            int crew = Coat.Count;
            if (crew > 0 && !_worn) Stand();
            else if (crew == 0 && _worn) Fold();

            if (_worn)
            {
                Fit();
                Coat.RideOn(Body.Pelvis, HubHeight);
            }

            Stow();
            Drape();
        }

        /// Show or hide the cloth on both coats. Renderers rather than GameObjects,
        /// because TheCoat switches the heap's cloth object on and off by itself as
        /// people climb in and out -- toggling the same object from here would mean the
        /// two of us fighting over it every tick.
        void Drape()
        {
            if (_heapCloth == null && Coat != null)
            {
                var cloth = Coat.GetComponentInChildren<CoatCloth>(true);
                if (cloth != null) _heapCloth = cloth.GetComponent<Renderer>();
            }

            if (_wornCloth == null && Body != null)
            {
                var cloth = Body.GetComponentInChildren<Classic.ClassicCloth>(true);
                if (cloth != null) _wornCloth = cloth.GetComponent<Renderer>();
            }

            if (_heapCloth != null && _heapCloth.enabled != ShowCoat) _heapCloth.enabled = ShowCoat;
            if (_wornCloth != null && _wornCloth.enabled != ShowCoat) _wornCloth.enabled = ShowCoat;
        }

        void OnValidate()
        {
            if (Application.isPlaying) Drape();
        }

        // ---- getting out ------------------------------------------------------

        /// A player aboard presses their own coat key to climb out. Read as a level
        /// rather than an edge, and only once the key that got them IN has been let go,
        /// so the press that seats you cannot immediately unseat you.
        ///
        /// Read from the game's input, never the body's: the body only starts sampling
        /// the keyboard on the tick it wakes up, so on the way in its states are a frame
        /// stale. Arming off that stale frame threw every player straight back out.
        void Leaving()
        {
            var input = Game != null ? Game.Input : null;
            if (input == null || input.States == null) return;

            for (int i = 0; i < 4; i++)
            {
                var who = Coat.WearerAt((CoatRole)i);
                if (who == null) { _armed[i] = false; continue; }

                if (!input.States[i].Coat) { _armed[i] = true; continue; }
                if (!_armed[i]) continue;

                _armed[i] = false;
                Coat.Leave(who);
            }
        }

        // ---- standing up and folding back down --------------------------------

        void Stand()
        {
            Vector3 spot = Coat.Centre;
            spot.y = GroundAt(spot);

            Vector3 facing = Coat.Hub != null
                ? Vector3.ProjectOnPlane(Coat.Hub.transform.forward, Vector3.up)
                : Vector3.forward;

            Body.gameObject.SetActive(true);
            Body.Place(spot, facing);

            _worn = true;
            _mask = -1;      // whatever limbs it had last time, re-decide them now
            Fit();
            Apply();
        }

        void Fold()
        {
            Vector3 spot = Body.Pelvis.position;
            Vector3 facing = Body.Heading;
            spot.y = GroundAt(spot);

            Body.gameObject.SetActive(false);
            Coat.DropAt(spot + Vector3.up * 0.25f, facing);

            _worn = false;
            _mask = -1;
            Apply();
        }

        /// Switch the body's limbs to match who is actually aboard.
        void Fit()
        {
            bool ll = Coat.Occupied(CoatRole.LeftLeg);
            bool rl = Coat.Occupied(CoatRole.RightLeg);
            bool la = Coat.Occupied(CoatRole.LeftArm);
            bool ra = Coat.Occupied(CoatRole.RightArm);

            int mask = (ll ? 1 : 0) | (rl ? 2 : 0) | (la ? 4 : 0) | (ra ? 8 : 0);
            if (mask == _mask) return;

            _mask = mask;
            Body.SetLimbs(ll, rl, la, ra);
            Apply();
        }

        // ---- who exists in the world ------------------------------------------

        /// Anyone aboard is inside the body and switched off; anyone who is not is a
        /// person again. Reconciled rather than driven by events, so one player leaving
        /// and a whole crew folding both land in the world the same way.
        void Stow()
        {
            for (int i = 0; i < Game.Characters.Length; i++)
            {
                var c = Game.Characters[i];
                if (c == null) continue;

                bool aboard = c.InCoat;
                bool up = c.gameObject.activeInHierarchy;
                if (aboard != up) continue;   // already correct

                if (aboard)
                {
                    c.DropEverything();
                    c.gameObject.SetActive(false);
                }
                else
                {
                    Vector3 from = Coat.Centre;
                    float a = i / (float)Game.Characters.Length * Mathf.PI * 2f;
                    Vector3 at = from + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * ExitRadius;
                    at.y = GroundAt(at);

                    c.gameObject.SetActive(true);
                    c.PlaceAt(at, from - at);   // turned to face what they just left
                }
            }
        }

        // ---- presentation ------------------------------------------------------

        /// Both passers-by can be up at once: with a partial crew there is a disguise to
        /// squint at AND spare people stood next to it, and each is its own problem.
        public void Apply()
        {
            if (WornObserver != null) WornObserver.SetActive(_worn);
            if (LooseObserver != null) LooseObserver.SetActive(!_worn || AnyoneLoose());

            if (LooseHud != null) LooseHud.enabled = !_worn;
            if (WornHud != null) WornHud.enabled = _worn;

            if (Cam == null) return;
            Cam.Game = _worn ? null : Game;
            Cam.Fallback = _worn && Body != null ? Body.Torso.transform
                         : Coat != null ? Coat.transform
                         : Cam.Fallback;
        }

        bool AnyoneLoose()
        {
            foreach (var c in Game.Characters)
                if (c != null && !c.InCoat) return true;
            return false;
        }

        /// The floor under a point.
        ///
        /// Starts the ray only just above the point, NOT two metres up. Two metres
        /// above anybody standing in the van is above its roof, so the ray came back
        /// down onto the roof and reported that as the ground: folding the coat inside
        /// the van dropped it at y 2.39, on the roof, a clear two metres over the heads
        /// of the crew sitting under it. Nobody could reach it and the round could
        /// never be restarted.
        ///
        /// Half a metre is enough to cope with a body that has sunk slightly into the
        /// floor, and low enough that it cannot see over anything a person fits inside.
        static float GroundAt(Vector3 p)
        {
            Vector3 from = p + Vector3.up * 0.5f;
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 8f,
                                CoatLayers.NotRig, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return 0f;
        }

        void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };

            string line;
            if (!_worn)
            {
                line = "<color=#ffd24a>nobody in the coat</color>" +
                       "   <i>walk to it and press your coat key</i>";
            }
            else
            {
                string shape =
                    Body.Legs == 0 ? "<color=#ff5b5b>no legs, going nowhere</color>"
                  : Body.Legs == 1 ? "<color=#ffd24a>hopping on one leg</color>"
                  : Body.Arms == 2 ? "<color=#8ce87a>a whole person</color>"
                                   : "<color=#ffd24a>walking, nothing to catch itself with</color>";

                line = $"<b>{Crew} of 4 aboard</b>   {Body.Legs} legs, {Body.Arms} arms   {shape}" +
                       "   <i>your coat key climbs out</i>";
            }

            if (!ShowCoat) line += "   <color=#8a8a8a>[coat hidden]</color>";

            GUI.Label(new Rect(14f, Screen.height - 34f, 860f, 24f), line, style);
        }
    }
}
