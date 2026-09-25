using UnityEngine;

namespace Coat
{
    public enum CoatRole { LeftLeg = 0, RightLeg = 1, LeftArm = 2, RightArm = 3 }

    [System.Serializable]
    public struct CoatInputState
    {
        public Vector2 Move;
        /// Grab, for an arm. Brace, for a leg.
        public bool Action;
        public bool ActionDown;
        /// Climb into the coat, or climb back out of it.
        public bool Coat;
        public bool CoatDown;
    }

    public static class CoatLayers
    {
        /// Every part of the rig lives here so the limbs pass through each other
        /// and collide only with the world.
        public const int Rig = 8;

        public static int NotRig => ~(1 << Rig);
    }
}
