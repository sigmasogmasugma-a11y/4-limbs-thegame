# 4 Limbs Fusion multiplayer integration

Branch: `feature/fusion-multiplayer`

This is the Fusion integration layer for the existing 4 Limbs project. It is host/state-authoritative: each player sends input for one CoatRole, the host runs the shared ClassicRagdoll and heist simulation, and clients receive the authoritative body/game state.

## Covered
- ClassicRagdoll receives the network input directly through `ClassicRagdoll.Tick(CoatInputState[], dt)`; it no longer depends on LocalCoatInput during Fusion simulation.
- ClassicObserver runs only on State Authority and its suspicion/result is replicated to proxies.
- Van phase, elapsed time, home count, left-van flag, loot transform/delivery and win/lose state are replicated.
- Round selection is host-authoritative and the selected round ID is represented by a replicated round index.
- Four roles are assigned in join order: LeftLeg, RightLeg, LeftArm, RightArm.
- Offline FixedUpdate loops are disabled while Fusion owns the simulation.

## Required external dependency
Photon Fusion 2 is intentionally not vendored into this repository. Import the Fusion 2 SDK and configure the project's Fusion App ID before compiling/running the network layer.

## Scene setup
After Fusion is imported, open the gameplay scene and use `Coat > Fusion > Setup Current Scene`, then save. Register the gameplay scene with the Fusion Network Project Config.

## Important testing note
This source has been statically reviewed against the supplied project structure, but it has not been runtime-compiled against the buyer's exact Fusion SDK because that SDK/App ID was not present in the supplied repository. The first Unity/Fusion compile is still required to validate SDK-version-specific API details and scene configuration.
