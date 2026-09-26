#if FUSION2
using Fusion;
using UnityEngine;

namespace Coat.Fusion
{
    public enum CoatFusionButton : byte
    {
        Action = 0,
        Coat = 1
    }

    public struct CoatFusionInput : INetworkInput
    {
        public Vector2 Move;
        public NetworkButtons Buttons;

        public bool Action => Buttons.IsSet(CoatFusionButton.Action);
        public bool Coat => Buttons.IsSet(CoatFusionButton.Coat);
    }
}
#endif
