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

        /// True from the moment F6/F7 is pressed until Photon answers: a few
        /// seconds of reaching the cloud with nothing else on screen, which is
        /// long enough to look like nothing happened.
        public bool Starting { get; private set; }

        /// Why the last attempt failed, for the status line. Null if it didn't.
        public string LastError { get; private set; }

        async void Start()
        {
            if (AutoStartHost) await StartHost();
            else if (AutoStartClient) await StartClient();
        }

        public Task<StartGameResult> StartHost() => StartGame(GameMode.Host);
        public Task<StartGameResult> StartClient() => StartGame(GameMode.Client);

        async Task<StartGameResult> StartGame(GameMode mode)
        {
            // Once at a time: pressing F6 again while the first attempt was still
            // connecting started a second runner alongside it.
            if (Running || Starting)
                return default;
            Starting = true;
            LastError = null;

            var go = new GameObject("4Limbs Network Runner");
            DontDestroyOnLoad(go);

            Runner = go.AddComponent<NetworkRunner>();
            Runner.ProvideInput = mode != GameMode.Server;

            var sceneManager = go.AddComponent<NetworkSceneManagerDefault>();
            int buildIndex = SceneManager.GetActiveScene().buildIndex;
            var sceneRef = SceneRef.FromIndex(buildIndex);
            var sceneInfo = new NetworkSceneInfo();
            sceneInfo.AddSceneRef(sceneRef, LoadSceneMode.Single);

            StartGameResult result;
            try
            {
                result = await Runner.StartGame(new StartGameArgs
                {
                    GameMode = mode,
                    SessionName = SessionName,
                    Scene = sceneInfo,
                    SceneManager = sceneManager
                });
            }
            finally
            {
                Starting = false;
            }

            // Fusion can report Ok with an error attached (seen: Ok:True with
            // "DisconnectException: ApplicationQuit" when Play stopped mid-connect),
            // so the message counts as a failure too.
            if (!result.Ok || !string.IsNullOrEmpty(result.ErrorMessage))
            {
                LastError = $"{result.ShutdownReason} {result.ErrorMessage}";
                Debug.LogError($"[4 Limbs] Fusion start failed: {LastError}");
                // Throw the dead runner away so F6/F7 can simply be pressed again.
                if (go != null) Destroy(go);
                Runner = null;
            }
            else
            {
                Debug.Log($"[4 Limbs] Fusion session '{SessionName}' started as {mode}.");
            }

            return result;
        }
    }
}
#endif
