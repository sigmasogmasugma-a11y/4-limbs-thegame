# 4 Limbs: Fusion multiplayer

Online play for the heist with Photon Fusion 2. Host-authoritative, as planned in
`CLAUDE.md`: every player sends input for one limb, the host runs the whole game
(kids, coat, disguise, observers, van, loot) and everyone else shows the host's state.

Written by Jae; merged onto the current `master` code (which keeps the round
outcome system) and extended. Targets **Fusion 2.1**: 2.0 differs in
`OnReliableDataReceived` (`ArraySegment<byte>` instead of `ReadOnlySpan<byte>`).
Compiling against Fusion 2.1 in the owner's editor; **not run online yet.**

## Setup

1. Import the Photon Fusion 2 SDK (2.1 or newer). It is not committed: `Assets/Photon/` is in
   `.gitignore`, so each person installs it themselves. It also holds the Photon
   App ID, and this repository is public.
2. Enter the Photon App ID in Fusion's settings. Get it from the project owner in a
   private message. Never commit it.
3. All network code is inside `#if FUSION2`. Fusion 2.1 did not add that symbol by
   itself here: add `FUSION2` under Project Settings > Player > Other Settings >
   Scripting Define Symbols, then Apply. (No **Coat > Fusion** menu means it is
   missing.) Without Fusion the project compiles and plays offline as before.
4. In the Network Project Config, set **Tick Rate to 50**. The ragdoll is tuned at
   the project's fixed timestep of 0.02 s; the host logs a warning if they differ.
5. Open `SampleScene`, run **Coat > Fusion > Setup Current Scene**, and save.

## Playing

- **F6** hosts, **F7** joins. Starting loads the open scene additively, as in Photon's
  Host Mode tutorial, so Fusion takes it over instead of reloading it. The status line at the bottom of the screen shows
  the session, the player count and your limb.
- Limbs are dealt in join order: left leg, right leg, left arm, right arm.
- Everyone uses the same controls on their own machine: **W A S D** to move,
  **Left Shift** for grab/brace, **Q** to climb in or out of the coat (or the first
  gamepad).

## How it works

- `CoatFusionInputProvider` sends this player's input. `CoatFusionWorld` (on the
  host) files each player's input under their limb and runs `CoatGame.Tick`,
  `ClassicRagdoll.Tick` and whichever observers are up, then steps physics itself
  (`Physics.simulationMode = Script`), keeping the offline order: input, logic,
  one physics step.
- The host sends every body of the disguise, the kids and the coat (pose and
  whether it is switched on), who is aboard, both observers (suspicion, tell,
  where they are looking), the van (phase, clock, door, who is home), the loot and
  the round result.
- Clients simulate nothing. `CoatFusionWorld.Render` copies the host's state every
  frame: bodies are kinematic and placed, the vehicle swaps observers, HUDs and
  camera from the host's "worn" state, and the van shows the host's clock and door.
- The round result is `CoatVan`'s, settled by `CoatRoundResult` on the host as
  offline (EverRumbled, never Rumbled). Each client settles the same numbers
  locally with `CoatVan.ApplyNetworkResult`, so every player sees the same GOT
  AWAY / BUSTED banner and is paid into their own save.
- Offline `FixedUpdate` loops are switched off with `ExternalSimulation` while
  Fusion runs the game.

## Not done yet

- **Restart (F5) is off online.** A new round means restarting the session.
- **Bandwidth.** This sends full body state (position, rotation, velocities) for
  about 76 bodies. The plan in `CLAUDE.md` was the body's root plus the live limb
  targets, about 19 numbers. Fine to start with; revisit if it lags.
- The observer's tell is sent as text, not the small id in the plan.
- Moves are read relative to the host's camera. Cameras all face the same way
  unless someone orbits theirs with `[` `]`; then that player's directions drift.
- The player rows in the in-coat HUD still list each limb's local keys, not the
  shared online controls.
- The three grabbable props in the test level are not synced.
- The main menu's Create/Join (`CoatLobby`) is not wired to Fusion yet; use F6/F7
  in the game scene.
- Cosmetics are not synced. The coat/head lock at round start is not written.
- No host migration: if the host leaves, the session ends.
