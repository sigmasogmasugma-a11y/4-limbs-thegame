using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Coat
{
    /// Applies the saved settings wherever the game starts, and takes Escape
    /// back to the menu.
    ///
    /// It spawns itself rather than living in a scene. Pressing Play on
    /// SampleScene directly is how this project is actually tested, so needing
    /// an object in the scene would mean the settings only took effect when you
    /// came in through the front door -- and SampleScene would have had to be
    /// edited to get one, which is the one file worth not touching.
    public class CoatSession : MonoBehaviour
    {
        public const string MenuScene = "MainMenu";

        static CoatSession _it;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (_it != null) return;
            // No hideFlags: DontSave would keep it out of FindObjectsByType,
            // which is exactly where anyone debugging this would look first.
            var go = new GameObject("[CoatSession]");
            DontDestroyOnLoad(go);
            _it = go.AddComponent<CoatSession>();
        }

        void OnEnable()  => SceneManager.sceneLoaded += OnScene;
        void OnDisable() => SceneManager.sceneLoaded -= OnScene;

        void Start() => Enter(SceneManager.GetActiveScene());
        void OnScene(Scene s, LoadSceneMode m) => Enter(s);

        /// Settle a scene: push the settings, and clear the round on the way
        /// back to the menu.
        ///
        /// The round is NOT drawn here. Drawing it belongs to something that
        /// is certainly present in the game scene, which is CoatGame.Awake --
        /// this object spawns itself before the first scene and is a worse
        /// place to depend on for anything that must happen exactly once.
        void Enter(Scene s)
        {
            Apply();
            if (s.name == MenuScene) CoatRounds.Clear();
        }

        /// Push the saved settings at the engine and at whatever is in the
        /// scene that answers to them.
        public static void Apply()
        {
            var s = CoatSave.Current.Settings;
            s.Apply();

            foreach (var hud in FindObjectsByType<CoatHud>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                hud.Draw = s.ShowHud;
                // Orbit runs off the sign, so inverting is the same control
                // turned around rather than a second branch in CoatHud.
                hud.OrbitSpeed = s.CameraSpeed * (s.InvertCamera ? -1f : 1f);
            }
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;

            // In the menu, Escape is the menu's own back button.
            if (SceneManager.GetActiveScene().name == MenuScene) return;
            ToMenu();
        }

        public static void ToMenu()
        {
            CoatLobby.Leave();
            if (Application.CanStreamedLevelBeLoaded(MenuScene))
                SceneManager.LoadScene(MenuScene);
            else
                Debug.LogWarning("[coat] no MainMenu scene in the build settings. " +
                                 "Run Coat > Build Main Menu.");
        }
    }
}
