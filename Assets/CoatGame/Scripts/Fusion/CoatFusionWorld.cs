#if FUSION2
using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;
using Coat.Classic;

namespace Coat.Fusion
{
    /// Single State Authority for the shared four-limb body and heist state.
    /// Each player owns one logical CoatRole; the host consumes all four inputs,
    /// advances the existing gameplay/ClassicRagdoll, and replicates the result.
    ///
    /// Clients simulate nothing. Everything they show is copied from the host in
    /// Render(): proxies do not run FixedUpdateNetwork in Fusion 2, so applying
    /// state there would never happen on a client.
    public sealed class CoatFusionWorld : NetworkBehaviour, INetworkRunnerCallbacks
    {
        /// Disguise (15) + four kids and the coat (61) in the test scene, with room
        /// to spare.
        public const int MaxBodies = 96;

        [Header("Existing game")]
        public CoatGame Game;
        public ClassicRagdoll Ragdoll;
        public ClassicObserver ClassicObserver;
        public CoatObserver LooseObserver;
        public LocalCoatInput LocalInput;
        public Transform PhysicsRoot;

        [Header("Session")]
        public bool ProvideInput = true;
        [Range(1, 4)] public int PlayerLimit = 4;

        [Header("Networked heist state")]
        [Networked, Capacity(4)] public NetworkArray<PlayerRef> RolePlayers => default;
        [Networked] public int PlayerCount { get; private set; }
        [Networked] public int RoundIndex { get; private set; } = -1;

        // The van. The host's van ticks for real; clients copy these so the door,
        // the clock and the banner match on every screen.
        [Networked] public byte VanPhase { get; private set; }
        [Networked] public float RoundTime { get; private set; }
        [Networked] public int AtHome { get; private set; }
        [Networked] public NetworkBool HasLeftVan { get; private set; }
        [Networked] public float DoorOpen { get; private set; }
        [Networked] public NetworkBool LootDelivered { get; private set; }

        // The round result, as settled by CoatVan/CoatRoundResult on the host.
        // 0 still running, 1 got away, 2 busted. Clients settle the same numbers
        // locally so each player is paid into their own save.
        [Networked] public byte RoundResult { get; private set; }
        [Networked] public int ResultFumbles { get; private set; }
        [Networked] public float ResultPeak { get; private set; }
        [Networked] public float ResultSeconds { get; private set; }

        // Both observers. They can be up at the same time (a partial crew means a
        // disguise AND loose kids to look at), so each gets its own copy.
        [Networked] public float Suspicion { get; private set; }
        [Networked] public NetworkBool Rumbled { get; private set; }
        [Networked] public NetworkBool ObserverCanSee { get; private set; }
        [Networked] public NetworkString<_64> ObserverTell { get; private set; }
        [Networked] public float ObserverTellStrength { get; private set; }
        [Networked] public float ObserverYaw { get; private set; }
        [Networked] public float LooseSuspicion { get; private set; }
        [Networked] public NetworkBool LooseRumbled { get; private set; }
        [Networked] public NetworkBool LooseCanSee { get; private set; }
        [Networked] public NetworkString<_64> LooseTell { get; private set; }
        [Networked] public float LooseTellStrength { get; private set; }
        [Networked] public float LooseYaw { get; private set; }

        // Who is where: the disguise up or folded, which limbs it wears, which kids
        // are still loose, and where the coat is.
        [Networked] public NetworkBool BodyUp { get; private set; }
        [Networked] public byte LimbMask { get; private set; }
        [Networked] public byte KidMask { get; private set; }
        [Networked] public Vector3 CoatPosition { get; private set; }
        [Networked] public Quaternion CoatRotation { get; private set; }

        [Networked, Capacity(MaxBodies)] private NetworkArray<CoatFusionBodyState> Bodies => default;
        [Networked] public Vector3 LootPosition { get; private set; }
        [Networked] public Quaternion LootRotation { get; private set; }
        [Networked] public Vector3 LootVelocity { get; private set; }
        [Networked] public Vector3 LootAngularVelocity { get; private set; }

        readonly List<Rigidbody> _bodies = new(MaxBodies);
        readonly CoatInputState[] _states = new CoatInputState[4];
        readonly NetworkButtons[] _previousButtons = new NetworkButtons[4];
        readonly bool[] _localActionWas = new bool[4];
        readonly bool[] _localCoatWas = new bool[4];
        bool _built;
        bool _callbacksAdded;
        bool _proxyPhysics;
        bool _stepsPhysics;
        UnityEngine.SimulationMode _oldSimulationMode;
        int _localRole = -1;
        int _lastAppliedRound = int.MinValue;
        int _appliedLimbs = -1;
        int _appliedPresence = -1;

        public int LocalRoleIndex => _localRole;
        public bool IsHostAuthority => Object != null && Object.HasStateAuthority;

        /// Network ticks the host has run, and the last thing that went wrong in
        /// one. Both go on the status line: the game is tested from screenshots of
        /// the Game view, where the Console is not.
        public int HostTicks { get; private set; }
        public string LastError { get; private set; }

        void Awake()
        {
            ResolveReferences();
            BuildBodyList();
        }

        /// The disguise and both observers are switched off whenever the coat is
        /// not worn, and FindFirstObjectByType skips switched-off objects by default.
        /// So they are taken from the vehicle that owns them first, as CoatVan does.
        void ResolveReferences()
        {
            if (Game == null) Game = FindFirstObjectByType<CoatGame>(FindObjectsInactive.Include);
            var vehicle = Game != null ? Game.Vehicle : null;

            if (Ragdoll == null && vehicle != null) Ragdoll = vehicle.Body;
            if (Ragdoll == null) Ragdoll = FindFirstObjectByType<ClassicRagdoll>(FindObjectsInactive.Include);

            if (ClassicObserver == null && vehicle != null && vehicle.WornObserver != null)
                ClassicObserver = vehicle.WornObserver.GetComponent<ClassicObserver>();
            if (ClassicObserver == null) ClassicObserver = FindFirstObjectByType<ClassicObserver>(FindObjectsInactive.Include);

            if (LooseObserver == null && vehicle != null && vehicle.LooseObserver != null)
                LooseObserver = vehicle.LooseObserver.GetComponent<CoatObserver>();
            if (LooseObserver == null) LooseObserver = FindFirstObjectByType<CoatObserver>(FindObjectsInactive.Include);

            if (LocalInput == null && Game != null) LocalInput = Game.Input;
            if (PhysicsRoot == null && Ragdoll != null) PhysicsRoot = Ragdoll.transform;
        }

        /// The disguise, the four kids and the coat. The same list, in the same
        /// order, on every peer: it comes from the scene, not from anything that
        /// happens at runtime.
        void BuildBodyList()
        {
            if (_built || PhysicsRoot == null) return;
            _bodies.Clear();
            AddBodies(PhysicsRoot);
            if (Game != null)
            {
                foreach (var c in Game.Characters)
                    if (c != null) AddBodies(c.transform);
                if (Game.Coat != null) AddBodies(Game.Coat.transform);
            }
            if (_bodies.Count >= MaxBodies)
                Debug.LogWarning($"[4 Limbs] More than {MaxBodies} bodies to sync; the rest are ignored. Raise CoatFusionWorld.MaxBodies.");
            _built = true;
        }

        void AddBodies(Transform root)
        {
            foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
            {
                if (rb == null || _bodies.Contains(rb)) continue;
                if (_bodies.Count >= MaxBodies) return;
                _bodies.Add(rb);
            }
        }

        public override void Spawned()
        {
            ResolveReferences();
            BuildBodyList();

            Runner.AddCallbacks(this);
            var provider = GetComponent<CoatFusionInputProvider>();
            if (provider == null) provider = gameObject.AddComponent<CoatFusionInputProvider>();
            provider.World = this;
            provider.LocalInput = LocalInput;
            Runner.AddCallbacks(provider);
            Runner.ProvideInput = ProvideInput;
            _callbacksAdded = true;

            if (Game != null) Game.ExternalSimulation = true;
            if (Ragdoll != null) Ragdoll.ExternalSimulation = true;
            if (ClassicObserver != null) ClassicObserver.ExternalSimulation = true;
            if (LooseObserver != null) LooseObserver.ExternalSimulation = true;
            if (Game != null && Game.Van != null) Game.Van.ExternalSimulation = true;

            if (Object.HasStateAuthority)
            {
                PlayerCount = 0;
                RoundResult = 0;
                RoundTime = 0f;
                InitialiseAuthoritativeRound();

                foreach (var player in Runner.ActivePlayers)
                    AssignRole(player);

                // Offline, every script ticks in FixedUpdate and Unity steps the
                // physics straight after. Online the game ticks here instead, so the
                // host steps the physics itself after each tick to keep that order:
                // input, then logic, then one physics step. The editor harnesses
                // drive the game the same way.
                _oldSimulationMode = UnityEngine.Physics.simulationMode;
                UnityEngine.Physics.simulationMode = UnityEngine.SimulationMode.Script;
                _stepsPhysics = true;

                // The ragdoll's joints and step timings were tuned at the project's
                // fixed timestep (0.02 s, 50 Hz). A different network tick changes
                // how the body moves, so say so rather than let it feel quietly off.
                if (Mathf.Abs(Runner.DeltaTime - Time.fixedDeltaTime) > 0.0001f)
                    Debug.LogWarning($"[4 Limbs] Fusion ticks every {Runner.DeltaTime:0.####} s but physics is tuned for " +
                                     $"{Time.fixedDeltaTime:0.####} s. Set the Tick Rate in the Network Project Config to " +
                                     $"{Mathf.RoundToInt(1f / Time.fixedDeltaTime)} so the body moves the same as offline.");

                // Fill in everything now, so a client never starts from defaults.
                SyncGameplayState();
                CaptureState();
            }
            else
            {
                MakeProxyPhysics();
            }

            RefreshLocalRole();
            ApplyRoundState();
        }

        void InitialiseAuthoritativeRound()
        {
            var current = CoatRounds.Current;
            if (current == null)
                current = CoatRounds.Begin(CoatSave.Current);

            RoundIndex = FindRoundIndex(current);
            _lastAppliedRound = RoundIndex;
        }

        static int FindRoundIndex(RoundDef round)
        {
            if (round == null) return -1;
            var all = CoatRounds.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] == round || (all[i] != null && all[i].Id == round.Id)) return i;
            return -1;
        }

        void ApplyRoundState()
        {
            if (RoundIndex == _lastAppliedRound) return;
            _lastAppliedRound = RoundIndex;
            var all = CoatRounds.All;
            if (RoundIndex >= 0 && RoundIndex < all.Count && all[RoundIndex] != null)
                CoatRounds.SetCurrent(all[RoundIndex].Id);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (_callbacksAdded)
            {
                runner.RemoveCallbacks(this);
                var provider = GetComponent<CoatFusionInputProvider>();
                if (provider != null) runner.RemoveCallbacks(provider);
                _callbacksAdded = false;
            }
            if (_stepsPhysics)
            {
                UnityEngine.Physics.simulationMode = _oldSimulationMode;
                _stepsPhysics = false;
            }
            if (Game != null) Game.ExternalSimulation = false;
            if (Ragdoll != null) Ragdoll.ExternalSimulation = false;
            if (ClassicObserver != null) ClassicObserver.ExternalSimulation = false;
            if (LooseObserver != null) LooseObserver.ExternalSimulation = false;
            if (Game != null && Game.Van != null) Game.Van.ExternalSimulation = false;
        }

        // ---- host --------------------------------------------------------------

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;
            HostTicks++;

            try
            {
                ResolveReferences();
                BuildBodyList();
                ReconcileRoles();
                RefreshLocalRole();

                SimulateAuthority();
                CaptureState();
            }
            catch (Exception e)
            {
                // Logged once per kind rather than fifty times a second.
                string what = e.GetType().Name + ": " + e.Message;
                if (what != LastError)
                {
                    LastError = what;
                    Debug.LogException(e);
                }
            }
        }

        void SimulateAuthority()
        {
            float dt = Runner.DeltaTime;

            Array.Clear(_states, 0, _states.Length);
            for (int role = 0; role < 4; role++)
            {
                PlayerRef player = RolePlayers.Get(role);
                if (!Connected(player))
                {
                    FromHostKeyboard(role);
                    continue;
                }
                _localActionWas[role] = _localCoatWas[role] = false;

                if (Runner.TryGetInputForPlayer<CoatFusionInput>(player, out var input))
                {
                    _states[role].Move = Vector2.ClampMagnitude(input.Move, 1f);
                    _states[role].Action = input.Action;
                    _states[role].ActionDown = input.Buttons.WasPressed(_previousButtons[role], CoatFusionButton.Action);
                    _states[role].Coat = input.Coat;
                    _states[role].CoatDown = input.Buttons.WasPressed(_previousButtons[role], CoatFusionButton.Coat);
                    _previousButtons[role] = input.Buttons;
                }
            }

            // CoatGame still owns character/coat/vehicle/loot/van ordering. We
            // temporarily expose the authoritative network input so those systems
            // see the exact same four role states as ClassicRagdoll.
            if (Game != null && Game.Input != null)
            {
                var oldStates = Game.Input.States;
                Game.Input.States = _states;
                try { Game.Tick(dt); }
                finally { Game.Input.States = oldStates; }
            }
            else if (Game != null)
            {
                Game.Tick(dt);
            }

            // ClassicRagdoll normally reads LocalCoatInput in its own FixedUpdate. In
            // Fusion mode that FixedUpdate is off and this is the input path. Only
            // while it is switched on, exactly as FixedUpdate would have been.
            if (Ragdoll != null && Ragdoll.isActiveAndEnabled)
                Ragdoll.Tick(_states, dt);

            // Observers tick by hand only while they are up: CoatVehicle switches
            // them on and off, and offline an inactive observer's FixedUpdate never
            // runs. Ticking a switched-off one would grade a body nobody can see.
            if (ClassicObserver != null && ClassicObserver.isActiveAndEnabled)
                ClassicObserver.Tick(dt);
            if (LooseObserver != null && LooseObserver.isActiveAndEnabled)
                LooseObserver.Tick(dt);

            if (_stepsPhysics) UnityEngine.Physics.Simulate(dt);

            SyncGameplayState();
        }

        void SyncGameplayState()
        {
            var van = Game != null ? Game.Van : null;
            var loot = Game != null ? Game.Loot : null;

            if (van != null)
            {
                VanPhase = (byte)van.Now;
                RoundTime = van.Elapsed;
                AtHome = van.AtHome;
                HasLeftVan = van.HasLeft;
                DoorOpen = van.DoorOpenness;

                // The round result is the van's, settled by CoatRoundResult at the
                // one moment the round closes (EverRumbled, not Rumbled). Nothing
                // here re-decides it; this only reports it.
                if (van.Now == CoatVan.Phase.Back && van.LastResult.HasValue)
                {
                    var r = van.LastResult.Value;
                    RoundResult = (byte)(r.Outcome == RoundOutcome.GotAway ? 1 : 2);
                    ResultFumbles = r.Fumbles;
                    ResultPeak = r.PeakSuspicion;
                    ResultSeconds = r.Seconds;
                }
                else
                {
                    RoundResult = 0;
                }
            }
            else
            {
                RoundTime += Runner.DeltaTime;
            }

            LootDelivered = loot != null && loot.Delivered;
            if (loot != null && loot.Body != null)
            {
                LootPosition = loot.Body.position;
                LootRotation = loot.Body.rotation;
                LootVelocity = loot.Body.linearVelocity;
                LootAngularVelocity = loot.Body.angularVelocity;
            }

            if (ClassicObserver != null)
            {
                Suspicion = ClassicObserver.Suspicion;
                Rumbled = ClassicObserver.Rumbled;
                ObserverCanSee = ClassicObserver.CanSee;
                ObserverTell = ClassicObserver.Tell ?? string.Empty;
                ObserverTellStrength = ClassicObserver.TellStrength;
                ObserverYaw = ClassicObserver.transform.eulerAngles.y;
            }
            if (LooseObserver != null)
            {
                LooseSuspicion = LooseObserver.Suspicion;
                LooseRumbled = LooseObserver.Rumbled;
                LooseCanSee = LooseObserver.CanSee;
                LooseTell = LooseObserver.Tell ?? string.Empty;
                LooseTellStrength = LooseObserver.TellStrength;
                LooseYaw = LooseObserver.transform.eulerAngles.y;
            }

            BodyUp = Ragdoll != null && Ragdoll.gameObject.activeSelf;

            int limbs = 0;
            if (Game != null && Game.Coat != null)
            {
                if (Game.Coat.Occupied(CoatRole.LeftLeg)) limbs |= 1;
                if (Game.Coat.Occupied(CoatRole.RightLeg)) limbs |= 2;
                if (Game.Coat.Occupied(CoatRole.LeftArm)) limbs |= 4;
                if (Game.Coat.Occupied(CoatRole.RightArm)) limbs |= 8;
                CoatPosition = Game.Coat.transform.position;
                CoatRotation = Game.Coat.transform.rotation;
            }
            LimbMask = (byte)limbs;

            int kids = 0;
            if (Game != null)
                for (int i = 0; i < Game.Characters.Length && i < 8; i++)
                    if (Game.Characters[i] != null && Game.Characters[i].gameObject.activeSelf) kids |= 1 << i;
            KidMask = (byte)kids;
        }

        void CaptureState()
        {
            for (int i = 0; i < _bodies.Count; i++)
            {
                var rb = _bodies[i];
                if (rb == null) continue;
                Bodies.Set(i, new CoatFusionBodyState
                {
                    Active = rb.gameObject.activeSelf,
                    Position = rb.position,
                    Rotation = rb.rotation,
                    Velocity = rb.linearVelocity,
                    AngularVelocity = rb.angularVelocity
                });
            }
        }

        // ---- clients -----------------------------------------------------------

        /// Runs every frame on every peer. Clients copy the host's latest state;
        /// the host has nothing to copy.
        public override void Render()
        {
            if (Object == null || Object.HasStateAuthority) return;

            ResolveReferences();
            BuildBodyList();
            RefreshLocalRole();
            ApplyRoundState();

            ApplyPresence();
            ApplyState();
            ApplyLootState();
            ApplyObserverState();
            ApplyVanState();
        }

        /// Who is switched on, as the host has it: the disguise, its limbs, the
        /// kids, and which observers and HUDs are up. Only touched when it changes.
        void ApplyPresence()
        {
            if (Game != null && Game.Coat != null && Valid(CoatRotation))
                Game.Coat.transform.SetPositionAndRotation(CoatPosition, CoatRotation);

            int presence = (BodyUp ? 1 : 0) | (KidMask << 1);
            if (presence != _appliedPresence)
            {
                _appliedPresence = presence;

                if (Ragdoll != null && Ragdoll.gameObject.activeSelf != BodyUp)
                    Ragdoll.gameObject.SetActive(BodyUp);

                if (Game != null)
                    for (int i = 0; i < Game.Characters.Length && i < 8; i++)
                    {
                        var c = Game.Characters[i];
                        if (c == null) continue;
                        bool up = (KidMask & (1 << i)) != 0;
                        if (c.gameObject.activeSelf != up) c.gameObject.SetActive(up);
                    }

                if (Game != null && Game.Vehicle != null)
                    Game.Vehicle.ApplyNetworkState(BodyUp, KidMask != 0);

                _appliedLimbs = -1;   // a body that just stood up needs its limbs again
            }

            if (BodyUp && Ragdoll != null && LimbMask != _appliedLimbs)
            {
                _appliedLimbs = LimbMask;
                Ragdoll.SetLimbs((LimbMask & 1) != 0, (LimbMask & 2) != 0,
                                 (LimbMask & 4) != 0, (LimbMask & 8) != 0);
            }
        }

        void ApplyObserverState()
        {
            if (ClassicObserver != null)
            {
                ClassicObserver.ApplyNetworkState(
                    Suspicion, Rumbled, ObserverTell.ToString(), ObserverTellStrength, ObserverCanSee);
                Face(ClassicObserver.transform, ObserverYaw);
            }
            if (LooseObserver != null)
            {
                LooseObserver.ApplyNetworkState(
                    LooseSuspicion, LooseRumbled, LooseTell.ToString(), LooseTellStrength, LooseCanSee);
                Face(LooseObserver.transform, LooseYaw);
            }
        }

        /// The observer's head turn is replicated state: which way they look is
        /// gameplay, and only the host decides it.
        static void Face(Transform t, float yaw)
        {
            var e = t.eulerAngles;
            t.rotation = Quaternion.Euler(e.x, yaw, e.z);
        }

        void ApplyVanState()
        {
            var van = Game != null ? Game.Van : null;
            if (van == null) return;

            van.ApplyNetworkState((CoatVan.Phase)VanPhase, RoundTime, AtHome, HasLeftVan, DoorOpen);
            if (RoundResult != 0)
                van.ApplyNetworkResult(RoundResult == 2, ResultFumbles, ResultPeak, ResultSeconds);
        }

        void ApplyLootState()
        {
            var loot = Game != null ? Game.Loot : null;
            if (loot == null || loot.Body == null || !Valid(LootRotation)) return;
            loot.Body.position = LootPosition;
            loot.Body.rotation = LootRotation;
            if (!loot.Body.isKinematic)
            {
                loot.Body.linearVelocity = LootVelocity;
                loot.Body.angularVelocity = LootAngularVelocity;
            }
        }

        void ApplyState()
        {
            for (int i = 0; i < _bodies.Count; i++)
            {
                var rb = _bodies[i];
                if (rb == null) continue;

                var state = Bodies.Get(i);
                bool active = state.Active;
                if (rb.gameObject.activeSelf != active) rb.gameObject.SetActive(active);
                if (!active) continue;

                // Proxy bodies are kinematic, so they are placed rather than pushed.
                // Setting a velocity on a kinematic body only earns a warning.
                if (!Valid(state.Rotation)) continue;
                rb.position = state.Position;
                rb.rotation = state.Rotation;
            }
        }

        /// A networked Quaternion that was never written is all zeros, which is not
        /// a rotation, and Unity complains loudly if handed one.
        static bool Valid(Quaternion q) => q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w > 0.5f;

        /// Clients never simulate: every synced body, and the loot, is placed from
        /// the host's state. Velocities are cleared BEFORE going kinematic, since
        /// Unity refuses velocity on a kinematic body.
        void MakeProxyPhysics()
        {
            if (_proxyPhysics) return;
            _proxyPhysics = true;

            foreach (var rb in _bodies)
                MakeKinematic(rb);

            var loot = Game != null ? Game.Loot : null;
            if (loot != null) MakeKinematic(loot.Body);
        }

        static void MakeKinematic(Rigidbody rb)
        {
            if (rb == null || rb.isKinematic) return;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        // ---- players -----------------------------------------------------------

        void RefreshLocalRole()
        {
            if (Runner == null) return;
            _localRole = -1;
            for (int i = 0; i < 4; i++)
                if (RolePlayers.Get(i) == Runner.LocalPlayer) { _localRole = i; break; }
        }

        /// Whether this player is in the session right now, and the only test for
        /// a taken limb. Fusion 2 retired PlayerRef.IsValid (IsRealPlayer replaced
        /// it), so it is not trusted to say whether a slot is empty: an empty slot
        /// read as taken deals nobody a limb and sends nobody's input anywhere.
        bool Connected(PlayerRef player)
        {
            if (player == PlayerRef.None || Runner == null) return false;
            foreach (var active in Runner.ActivePlayers)
                if (active == player) return true;
            return false;
        }

        /// Whether a limb is being played by someone in the session. False means
        /// the host's own keyboard plays it.
        public bool RoleTaken(int role) => role >= 0 && role < 4 && Connected(RolePlayers.Get(role));

        int RoleOf(PlayerRef player)
        {
            for (int i = 0; i < 4; i++)
                if (RolePlayers.Get(i) == player) return i;
            return -1;
        }

        /// Every connected player has a limb and every limb's player is still
        /// connected, checked every tick rather than trusted to join/leave events.
        /// On the host its own player joins before this object spawns, so neither
        /// Spawned's sweep nor OnPlayerJoined ever saw it.
        void ReconcileRoles()
        {
            for (int i = 0; i < 4; i++)
            {
                var p = RolePlayers.Get(i);
                if (p != PlayerRef.None && !Connected(p)) ClearRole(i);
            }

            foreach (var a in Runner.ActivePlayers)
                if (RoleOf(a) < 0) AssignRole(a);

            CountPlayers();
        }

        /// Counted from the limbs every time, never added to or taken from, so it
        /// cannot drift from who is actually playing.
        void CountPlayers()
        {
            int count = 0;
            for (int i = 0; i < 4; i++)
                if (Connected(RolePlayers.Get(i))) count++;
            if (PlayerCount != count) PlayerCount = count;
        }

        /// A limb nobody has joined for is played from the host's own keyboard
        /// with its offline keys (I J K L, T F G H, P ; / '), so one person can
        /// test online alone exactly as offline, and a crew short of four can
        /// still get everyone into the coat.
        ///
        /// Press edges are worked out here, once per network tick, from the held
        /// keys: LocalCoatInput's own edges are per rendered frame, and a tick can
        /// run zero or two times in a frame, which would drop or double a climb-in.
        void FromHostKeyboard(int role)
        {
            var local = LocalInput != null ? LocalInput.States : null;
            if (local == null || role >= local.Length) return;

            var k = local[role];
            _states[role].Move = Vector2.ClampMagnitude(k.Move, 1f);
            _states[role].Action = k.Action;
            _states[role].ActionDown = k.Action && !_localActionWas[role];
            _states[role].Coat = k.Coat;
            _states[role].CoatDown = k.Coat && !_localCoatWas[role];
            _localActionWas[role] = k.Action;
            _localCoatWas[role] = k.Coat;
        }

        void AssignRole(PlayerRef player)
        {
            if (!Object.HasStateAuthority || !Connected(player) || RoleOf(player) >= 0) return;

            int taken = 0;
            for (int i = 0; i < 4; i++)
                if (RoleTaken(i)) taken++;
            if (taken >= Mathf.Clamp(PlayerLimit, 1, 4)) return;

            for (int i = 0; i < 4; i++)
            {
                if (RoleTaken(i)) continue;
                RolePlayers.Set(i, player);
                _previousButtons[i] = default;
                CountPlayers();
                Debug.Log($"[4 Limbs] {player} plays the {RoleName(i)}.");
                return;
            }
        }

        void RemoveRole(PlayerRef player)
        {
            if (!Object.HasStateAuthority) return;
            int role = RoleOf(player);
            if (role >= 0) ClearRole(role);
            CountPlayers();
        }

        void ClearRole(int role)
        {
            RolePlayers.Set(role, PlayerRef.None);
            _previousButtons[role] = default;
        }

        public static string RoleName(int role) => role switch
        {
            (int)CoatRole.LeftLeg => "left leg",
            (int)CoatRole.RightLeg => "right leg",
            (int)CoatRole.LeftArm => "left arm",
            (int)CoatRole.RightArm => "right arm",
            _ => "nothing"
        };

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (Object.HasStateAuthority) AssignRole(player);
            RefreshLocalRole();
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            if (Object.HasStateAuthority) RemoveRole(player);
            RefreshLocalRole();
        }

        public void OnInput(NetworkRunner runner, NetworkInput input) { }
        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    }

    public struct CoatFusionBodyState : INetworkStruct
    {
        public NetworkBool Active;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public Vector3 AngularVelocity;
    }
}
#endif
