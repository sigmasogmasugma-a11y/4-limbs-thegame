#if FUSION2 && UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Coat;
using Coat.Classic;
using Coat.Fusion;
using Fusion;

namespace Coat.Editor
{
    public static class CoatFusionSceneSetup
    {
        [MenuItem("Coat/Fusion/Setup Current Scene")]
        public static void Setup()
        {
            var game = Object.FindFirstObjectByType<CoatGame>();
            if (game == null) { EditorUtility.DisplayDialog("4 Limbs Fusion", "No CoatGame was found in the active scene.", "OK"); return; }

            var root = game.gameObject;
            var networkObject = root.GetComponent<NetworkObject>() ?? Undo.AddComponent<NetworkObject>(root);
            var world = root.GetComponent<CoatFusionWorld>() ?? Undo.AddComponent<CoatFusionWorld>(root);
            var runner = root.GetComponent<CoatFusionRunner>() ?? Undo.AddComponent<CoatFusionRunner>(root);
            var hud = root.GetComponent<CoatFusionHud>() ?? Undo.AddComponent<CoatFusionHud>(root);

            world.Game = game;
            world.Ragdoll = Object.FindFirstObjectByType<ClassicRagdoll>();
            world.ClassicObserver = Object.FindFirstObjectByType<ClassicObserver>();
            world.LooseObserver = Object.FindFirstObjectByType<CoatObserver>();
            world.LocalInput = game.Input;
            world.PhysicsRoot = world.Ragdoll != null ? world.Ragdoll.transform : root.transform;
            hud.World = world;
            hud.Runner = runner;

            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log("[4 Limbs] Fusion scene setup complete. Save the scene and configure Fusion 2/App ID before running.");
        }
    }
}
#endif
