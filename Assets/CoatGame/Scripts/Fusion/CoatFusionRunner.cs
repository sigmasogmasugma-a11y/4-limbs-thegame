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

        // Static, not per component: this component lives in the game scene, and
        // anything that reloads the scene makes a fresh one that would otherwise
        // forget the session the old one started -- the status line then went
        // back to "F6 Host" while the runner was still running.
        static NetworkRunner s_runner;
        static bool s_starting;
        static string s_lastError;

        /// Every Play starts clean, even with domain reload switched off in the
        /// Enter Play Mode options, where statics survive from the last Play.
        /// A session stopped mid-connect would otherwise leave "starting" stuck
        /// on and F6 would never work again.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_runner = null;
            s_starting = false;
            s_lastError = null;
        }

        public NetworkRunner Runner => s_runner;
        public bool Running => s_runner != null && s_runner.IsRunning;

        /// True from the moment F6/F7 is pressed until Photon answers: a few
        /// seconds of reaching the cloud with nothing else on screen, which is
        /// long enough to look like nothing happened.
        public bool Starting => s_starting;

        /// Why the last attempt failed, for the status line. Null if it didn't.
        public string LastError => s_lastError;

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
            s_starting = true;
            s_lastError = null;

            var go = new GameObject("4Limbs Network Runner");
            DontDestroyOnLoad(go);

            s_runner = go.AddComponent<NetworkRunner>();
            s_runner.ProvideInput = mode != GameMode.Server;

            var sceneManager = go.AddComponent<NetworkSceneManagerDefault>();
            int buildIndex = SceneManager.GetActiveScene().buildIndex;
            var sceneRef = SceneRef.FromIndex(buildIndex);
            var sceneInfo = new NetworkSceneInfo();
            // Additive, as in Photon's own Host Mode tutorial: the scene is already
            // open, and Fusion takes it over as it is. Single threw it away and
            // loaded a fresh copy, which reset everything in it mid-connect.
            sceneInfo.AddSceneRef(sceneRef, LoadSceneMode.Additive);

            StartGameResult result;
            try
            {
                result = await s_runner.StartGame(new StartGameArgs
                {
                    GameMode = mode,
                    SessionName = SessionName,
                    Scene = sceneInfo,
                    SceneManager = sceneManager
                });
            }
            finally
            {
                s_starting = false;
            }

            // Fusion can report Ok with an error attached (seen: Ok:True with
            // "DisconnectException: ApplicationQuit" when Play stopped mid-connect),
            // so the message counts as a failure too.
            if (!result.Ok || !string.IsNullOrEmpty(result.ErrorMessage))
            {
                s_lastError = $"{result.ShutdownReason} {result.ErrorMessage}";
                Debug.LogError($"[4 Limbs] Fusion start failed: {s_lastError}");
                // Throw the dead runner away so F6/F7 can simply be pressed again.
                if (go != null) Destroy(go);
                s_runner = null;
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
