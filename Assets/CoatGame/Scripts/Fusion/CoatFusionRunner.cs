#if FUSION2
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Coat.Fusion
{
    /// Runtime launcher for the 4 Limbs host/client session. It deliberately does not
    /// own gameplay; CoatFusionWorld owns the authoritative simulation.
    public sealed class CoatFusionRunner : MonoBehaviour
    {
        public string SessionName = "4Limbs";
        public GameMode HostMode = GameMode.Host;
        public bool AutoStartHost;
        public bool AutoStartClient;

        public NetworkRunner Runner { get; private set; }
        public bool Running => Runner != null && Runner.IsRunning;

        async void Start()
        {
            if (AutoStartHost) await StartHost();
            else if (AutoStartClient) await StartClient();
        }

        public Task<StartGameResult> StartHost() => StartGame(GameMode.Host);
        public Task<StartGameResult> StartClient() => StartGame(GameMode.Client);

        async Task<StartGameResult> StartGame(GameMode mode)
        {
            if (Running)
                return default;

            var go = new GameObject("4Limbs Network Runner");
            DontDestroyOnLoad(go);

            Runner = go.AddComponent<NetworkRunner>();
            Runner.ProvideInput = mode != GameMode.Server;

            var sceneManager = go.AddComponent<NetworkSceneManagerDefault>();
            int buildIndex = SceneManager.GetActiveScene().buildIndex;
            var sceneRef = SceneRef.FromIndex(buildIndex);
            var sceneInfo = new NetworkSceneInfo();
            sceneInfo.AddSceneRef(sceneRef, LoadSceneMode.Single);

            var result = await Runner.StartGame(new StartGameArgs
            {
                GameMode = mode,
                SessionName = SessionName,
                Scene = sceneInfo,
                SceneManager = sceneManager
            });

            if (!result.Ok)
                Debug.LogError($"[4 Limbs] Fusion start failed: {result.ShutdownReason} {result.ErrorMessage}");
            else
                Debug.Log($"[4 Limbs] Fusion session '{SessionName}' started as {mode}.");

            return result;
        }
    }
}
#endif
