#if FUSION2
using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using Coat.Classic;

namespace Coat.Fusion
{
    /// Single State Authority for the shared four-limb body and heist state.
    /// Each player owns one logical CoatRole; the host consumes all four inputs,
    /// advances the existing gameplay/ClassicRagdoll, and replicates the result.
    public sealed class CoatFusionWorld : NetworkBehaviour, INetworkRunnerCallbacks
    {
        public const int MaxBodies = 64;

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
        [Networked] public byte RoundResult { get; private set; } // 0 live, 1 win, 2 lose
        [Networked] public int RoundIndex { get; private set; } = -1;
        [Networked] public byte VanPhase { get; private set; }
        [Networked] public float RoundTime { get; private set; }
        [Networked] public int AtHome { get; private set; }
        [Networked] public NetworkBool HasLeftVan { get; private set; }
        [Networked] public NetworkBool LootDelivered { get; private set; }
        [Networked] public NetworkBool Rumbled { get; private set; }
        [Networked] public float Suspicion { get; private set; }
        [Networked] public NetworkBool ObserverCanSee { get; private set; }
        [Networked, Capacity(64)] public NetworkString<_64> ObserverTell { get; private set; }
        [Networked] public float ObserverTellStrength { get; private set; }

        [Networked] private ulong ActiveMask { get; set; }
        [Networked, Capacity(MaxBodies)] private NetworkArray<CoatFusionBodyState> Bodies => default;
        [Networked] public Vector3 LootPosition { get; private set; }
        [Networked] public Quaternion LootRotation { get; private set; }
        [Networked] public Vector3 LootVelocity { get; private set; }
        [Networked] public Vector3 LootAngularVelocity { get; private set; }

        readonly List<Rigidbody> _bodies = new(MaxBodies);
        readonly CoatInputState[] _states = new CoatInputState[4];
        readonly NetworkButtons[] _previousButtons = new NetworkButtons[4];
        bool _built;
        bool _callbacksAdded;
        int _localRole = -1;
        int _lastAppliedRound = int.MinValue;

        public int LocalRoleIndex => _localRole;
        public bool IsHostAuthority => Object != null && Object.HasStateAuthority;

        void Awake()
        {
            ResolveReferences();
            BuildBodyList();
        }

        void ResolveReferences()
        {
            if (Game == null) Game = FindFirstObjectByType<CoatGame>();
            if (Ragdoll == null) Ragdoll = FindFirstObjectByType<ClassicRagdoll>();
            if (ClassicObserver == null) ClassicObserver = FindFirstObjectByType<ClassicObserver>();
            if (LooseObserver == null) LooseObserver = FindFirstObjectByType<CoatObserver>();
            if (LocalInput == null && Game != null) LocalInput = Game.Input;
            if (PhysicsRoot == null && Ragdoll != null) PhysicsRoot = Ragdoll.transform;
        }

        void BuildBodyList()
        {
            if (_built || PhysicsRoot == null) return;
            _bodies.Clear();
            foreach (var rb in PhysicsRoot.GetComponentsInChildren<Rigidbody>(true))
            {
                if (rb == null || _bodies.Contains(rb)) continue;
                if (_bodies.Count >= MaxBodies) break;
                _bodies.Add(rb);
            }
            _built = true;
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
            }

            RefreshLocalRole();
            ApplyRoundState();
            SetProxyPhysics(!Object.HasStateAuthority);
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
            if (Game != null) Game.ExternalSimulation = false;
            if (Ragdoll != null) Ragdoll.ExternalSimulation = false;
            if (ClassicObserver != null) ClassicObserver.ExternalSimulation = false;
            if (LooseObserver != null) LooseObserver.ExternalSimulation = false;
            if (Game != null && Game.Van != null) Game.Van.ExternalSimulation = false;
        }

        public override void FixedUpdateNetwork()
        {
            ResolveReferences();
            BuildBodyList();
            RefreshLocalRole();
            ApplyRoundState();

            if (Object.HasStateAuthority)
            {
                SimulateAuthority();
                CaptureState();
            }
            else
            {
                ApplyState();
                ApplyLootState();
                ApplyObserverState();
            }
        }

        void SimulateAuthority()
        {
            if (RoundResult != 0) return;

            Array.Clear(_states, 0, _states.Length);
            for (int role = 0; role < 4; role++)
            {
                PlayerRef player = RolePlayers.Get(role);
                if (!player.IsValid) continue;

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

            // CoatGame still owns character/coat/vehicle/loot ordering. We temporarily
            // expose the authoritative network input so those systems and ClassicRagdoll
            // see the exact same four role states.
            if (Game != null && Game.Input != null)
            {
                var oldStates = Game.Input.States;
                Game.Input.States = _states;
                Game.Tick(Runner.DeltaTime);
                Game.Input.States = oldStates;
            }
            else if (Game != null)
            {
                Game.Tick(Runner.DeltaTime);
            }

            // ClassicRagdoll normally reads LocalCoatInput in its own FixedUpdate. In
            // Fusion mode that FixedUpdate is disabled and this explicit call is the
            // authoritative network input path.
            if (Ragdoll != null && Ragdoll.gameObject.activeInHierarchy)
                Ragdoll.Tick(_states, Runner.DeltaTime);

            if (ClassicObserver != null)
                ClassicObserver.Tick(Runner.DeltaTime);

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
            }

            if (Rumbled)
            {
                RoundResult = 2;
            }
            else if (van != null && van.Now == CoatVan.Phase.Back)
            {
                RoundResult = 1;
            }
        }

        void ApplyObserverState()
        {
            if (ClassicObserver == null) return;
            ClassicObserver.ApplyNetworkState(
                Suspicion,
                Rumbled,
                ObserverTell.ToString(),
                ObserverTellStrength,
                ObserverCanSee);
        }

        void CaptureState()
        {
            ulong mask = 0UL;
            for (int i = 0; i < _bodies.Count; i++)
            {
                var rb = _bodies[i];
                if (rb == null) continue;
                if (rb.gameObject.activeInHierarchy) mask |= 1UL << i;
                Bodies.Set(i, new CoatFusionBodyState
                {
                    Position = rb.position,
                    Rotation = rb.rotation,
                    Velocity = rb.linearVelocity,
                    AngularVelocity = rb.angularVelocity
                });
            }
            ActiveMask = mask;
        }

        void ApplyLootState()
        {
            var loot = Game != null ? Game.Loot : null;
            if (loot == null || loot.Body == null) return;
            loot.Body.position = LootPosition;
            loot.Body.rotation = LootRotation;
            loot.Body.linearVelocity = LootVelocity;
            loot.Body.angularVelocity = LootAngularVelocity;
        }

        void ApplyState()
        {
            for (int i = 0; i < _bodies.Count; i++)
            {
                var rb = _bodies[i];
                if (rb == null) continue;
                bool active = (ActiveMask & (1UL << i)) != 0;
                if (rb.gameObject.activeSelf != active) rb.gameObject.SetActive(active);
                if (!active) continue;

                var state = Bodies.Get(i);
                rb.position = state.Position;
                rb.rotation = state.Rotation;
                rb.linearVelocity = state.Velocity;
                rb.angularVelocity = state.AngularVelocity;
            }
        }

        void SetProxyPhysics(bool proxy)
        {
            foreach (var rb in _bodies)
            {
                if (rb == null) continue;
                rb.isKinematic = proxy;
                if (proxy)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }
        }

        void RefreshLocalRole()
        {
            if (Runner == null) return;
            _localRole = -1;
            for (int i = 0; i < 4; i++)
                if (RolePlayers.Get(i) == Runner.LocalPlayer) { _localRole = i; break; }
        }

        void AssignRole(PlayerRef player)
        {
            if (!Object.HasStateAuthority || PlayerCount >= Mathf.Min(PlayerLimit, 4)) return;
            for (int i = 0; i < 4; i++)
            {
                if (RolePlayers.Get(i).IsValid) continue;
                RolePlayers.Set(i, player);
                PlayerCount++;
                return;
            }
        }

        void RemoveRole(PlayerRef player)
        {
            if (!Object.HasStateAuthority) return;
            for (int i = 0; i < 4; i++)
            {
                if (RolePlayers.Get(i) != player) continue;
                RolePlayers.Set(i, PlayerRef.None);
                PlayerCount = Mathf.Max(0, PlayerCount - 1);
                _previousButtons[i] = default;
                return;
            }
        }

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
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    }

    public struct CoatFusionBodyState : INetworkStruct
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public Vector3 AngularVelocity;
    }
}
#endif
