using UnityEngine;

namespace Coat
{
    /// Drives the four of them. Input gathering stays outside this class so swapping
    /// LocalCoatInput for a networked source later is a one line change.
    public class CoatGame : MonoBehaviour
    {
        public LocalCoatInput Input;
        public TheCoat Coat;
        public CoatCharacter[] Characters = new CoatCharacter[4];
        public CoatVehicle Vehicle;
        public CoatVan Van;
        public CoatLoot Loot;
        public Transform CameraRef;

        /// Set by the Fusion world to prevent the local FixedUpdate simulation from
        /// running alongside FixedUpdateNetwork.
        public bool ExternalSimulation;

        void Awake()
        {
            if (Input == null) Input = GetComponent<LocalCoatInput>();
            if (CameraRef == null && Camera.main != null) CameraRef = Camera.main.transform;

            // Every bone of every character shares one layer and must pass through the
            // others, colliding only with the world. This used to live on the old single
            // ragdoll; without it the bones shove each other apart, the joints lose, and
            // the whole upper body is crushed down into the hips.
            Physics.IgnoreLayerCollision(CoatLayers.Rig, CoatLayers.Rig, true);

            // Coming through the menu, the lobby has already drawn the round
            // and this leaves it alone. Pressing Play straight onto this scene
            // skips all that, so draw one here.
            //
            // It lives on this component because this one is certainly in the
            // game scene. CoatSession spawns itself before the first scene
            // loads, which makes it the wrong place to hang something that has
            // to happen exactly once per game.
            if (CoatRounds.Current == null) CoatRounds.Begin(CoatSave.Current);
            CoatSession.Apply();
        }

        void FixedUpdate()
        {
            if (ExternalSimulation) return;
            Tick(Time.fixedDeltaTime);
        }

        /// One simulation step. Public so a test harness, or Fusion's FixedUpdateNetwork,
        /// can drive it without this class caring which.
        public void Tick(float dt)
        {
            float camYaw = CameraRef != null ? CameraRef.eulerAngles.y : 0f;

            for (int i = 0; i < Characters.Length; i++)
            {
                var c = Characters[i];
                // Switched off means they are inside the body, which drives itself.
                if (c == null || !c.gameObject.activeInHierarchy) continue;

                var state = Input.States[i];

                if (state.CoatDown && Coat != null) Coat.Toggle(c);

                // The coat's leg turn-taking applies only to whoever is WEARING
                // it. Under the coat the two leg players are one pair of legs
                // and have to alternate; out in the van each child has two legs
                // of its own and alternates between them internally.
                //
                // Applied to everyone, it locked each leg-role child out waiting
                // for the OTHER PLAYER to take a turn that was never coming:
                // 4 steps in 5 seconds and 0.26 m against 16 steps and 1.85 m
                // for an arm-role child, who skips this test entirely. That is
                // the stutter that felt like a per-step cooldown.
                bool mayStep = Coat == null || !c.InCoat || Coat.MayStep(c.Role);
                var partner = c.InCoat ? PartnerLeg(c.Role) : null;

                c.Tick(state, camYaw, dt, mayStep, partner);

                if (c.JustStepped && c.InCoat && Coat != null) Coat.RegisterStep(c.Role);
            }

            // After the wearers have moved, so the coat settles onto where they now are.
            if (Coat != null && Coat.gameObject.activeInHierarchy) Coat.Tick(dt);

            // Last: it may swap the four of them for the body, or the body back for them.
            if (Vehicle != null) Vehicle.Tick(dt);

            // The prop works out whether anybody has hold of it, before the van asks
            // whether it is home.
            if (Loot != null) Loot.Tick(dt);

            // And then the van decides whether any of that still counts.
            if (Van != null) Van.Tick(dt);
        }

        public CoatCharacter Get(CoatRole role) => Characters[(int)role];

        /// The other leg, so a step can be capped against where it is standing.
        CoatCharacter PartnerLeg(CoatRole role)
        {
            if (role == CoatRole.LeftLeg) return Get(CoatRole.RightLeg);
            if (role == CoatRole.RightLeg) return Get(CoatRole.LeftLeg);
            return null;
        }
    }
}
