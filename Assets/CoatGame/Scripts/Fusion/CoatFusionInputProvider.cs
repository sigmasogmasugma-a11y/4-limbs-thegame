#if FUSION2
using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace Coat.Fusion
{
    /// Collects the local player's assigned limb input. The host assembles the four
    /// role inputs in CoatFusionWorld. Only one CoatFusionInput is submitted per tick.
    public sealed class CoatFusionInputProvider : MonoBehaviour, INetworkRunnerCallbacks
    {
        public CoatFusionWorld World;
        public LocalCoatInput LocalInput;

        CoatInputState _last;

        /// Online, everybody is on their own machine, so everybody plays on the
        /// first control set -- W A S D, Left Shift, Q, or the first gamepad --
        /// whichever limb they were dealt. The role only decides where the host
        /// files this input; it no longer decides which keys this player presses.
        const int OwnControls = 0;

        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
            var data = default(CoatFusionInput);
            int role = World != null ? World.LocalRoleIndex : -1;

            if (LocalInput != null && LocalInput.States != null && role >= 0 && role < 4)
            {
                var state = LocalInput.States[OwnControls];
                data.Move = Vector2.ClampMagnitude(state.Move, 1f);
                data.Buttons.Set(CoatFusionButton.Action, state.Action);
                data.Buttons.Set(CoatFusionButton.Coat, state.Coat);
                _last = state;
            }

            input.Set(data);
        }

        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
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
}
#endif
