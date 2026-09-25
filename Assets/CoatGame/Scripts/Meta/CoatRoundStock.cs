using System.Collections.Generic;
using UnityEngine;

namespace Coat
{
    /// One round the game can draw.
    ///
    /// EMPTY BY DESIGN right now. A round is currently a name and a weight and
    /// nothing else -- picking one does not change what you play. The point of
    /// landing this before any round exists is that the drawing, the rarity and
    /// the history are settled and tested first, so adding "Parkour" later is
    /// one entry in a list rather than a design argument.
    [System.Serializable]
    public class RoundDef
    {
        [Tooltip("Stable and never reused -- it is what gets written into the " +
                 "play history and what the host would replicate.")]
        public string Id;

        public string Name;

        [Tooltip("Scene to load for this round. Empty stays in the current " +
                 "scene, which is what every placeholder does.")]
        public string Scene;

        [Tooltip("Designer weight, applied BEFORE the repeat penalty. 1 is " +
                 "normal. 0 takes a round out of the draw without deleting it, " +
                 "which is how you park a half-finished one.")]
        public float Weight = 1f;

        [Tooltip("Pickable, but does nothing when picked. Clear this when the " +
                 "round is actually built.")]
        public bool Placeholder = true;
    }

    /// Every round the game can draw, as an asset so rounds can be added in the
    /// inspector without touching code.
    ///
    /// Lives at Assets/CoatGame/Resources/CoatRoundStock.asset so CoatRounds
    /// finds it by name with no scene reference to wire up.
    [CreateAssetMenu(fileName = "CoatRoundStock", menuName = "Coat/Round Stock")]
    public class CoatRoundStock : ScriptableObject
    {
        public List<RoundDef> Rounds = new List<RoundDef>();
    }
}
