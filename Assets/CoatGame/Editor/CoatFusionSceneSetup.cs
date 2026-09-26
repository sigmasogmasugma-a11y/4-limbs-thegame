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

            // Explicit null checks, not ??: in the editor GetComponent hands back a
            // "fake null" for a missing component, which ?? treats as present, so
            // nothing would ever be added.
            if (root.GetComponent<NetworkObject>() == null) Undo.AddComponent<NetworkObject>(root);
            var world = root.GetComponent<CoatFusionWorld>();
            if (world == null) world = Undo.AddComponent<CoatFusionWorld>(root);
            var runner = root.GetComponent<CoatFusionRunner>();
            if (runner == null) runner = Undo.AddComponent<CoatFusionRunner>(root);
            var hud = root.GetComponent<CoatFusionHud>();
            if (hud == null) hud = Undo.AddComponent<CoatFusionHud>(root);

            // From the vehicle first: the disguise and both observers are switched
            // off whenever the coat is not worn, and a plain Find skips them.
            var vehicle = game.Vehicle;
            world.Game = game;
            world.Ragdoll = vehicle != null && vehicle.Body != null
                ? vehicle.Body
                : Object.FindFirstObjectByType<ClassicRagdoll>(FindObjectsInactive.Include);
            world.ClassicObserver = vehicle != null && vehicle.WornObserver != null
                ? vehicle.WornObserver.GetComponent<ClassicObserver>()
                : Object.FindFirstObjectByType<ClassicObserver>(FindObjectsInactive.Include);
            world.LooseObserver = vehicle != null && vehicle.LooseObserver != null
                ? vehicle.LooseObserver.GetComponent<CoatObserver>()
                : Object.FindFirstObjectByType<CoatObserver>(FindObjectsInactive.Include);
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
