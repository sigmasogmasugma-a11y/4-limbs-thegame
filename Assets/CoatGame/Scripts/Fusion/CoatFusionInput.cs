#if FUSION2
using Fusion;
using UnityEngine;

namespace Coat.Fusion
{
    /// Must stay int-backed. NetworkButtons' generic Set/IsSet/WasPressed assert
    /// the enum's underlying type is int; a byte enum threw on every tick on both
    /// sides, so a client's input was never sent and the host never read it.
    public enum CoatFusionButton
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
