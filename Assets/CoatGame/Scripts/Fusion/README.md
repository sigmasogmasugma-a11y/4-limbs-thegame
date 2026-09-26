# 4 Limbs: Fusion multiplayer

Online play for the heist with Photon Fusion 2. Host-authoritative, as planned in
`CLAUDE.md`: every player sends input for one limb, the host runs the whole game
(kids, coat, disguise, observers, van, loot) and everyone else shows the host's state.

Written by Jae; merged onto the current `master` code (which keeps the round
outcome system) and extended. Targets **Fusion 2.1**: 2.0 differs in
`OnReliableDataReceived` (`ArraySegment<byte>` instead of `ReadOnlySpan<byte>`).
Compiles against Fusion 2.1 and hosts in the owner's editor; **not yet played with a
second peer.**

## Setup

1. Import the Photon Fusion 2 SDK (2.1 or newer). It is not committed: `Assets/Photon/` is in
   `.gitignore`, so each person installs it themselves. It also holds the Photon
   App ID, and this repository is public.
2. Enter the Photon App ID in Fusion's settings. Get it from the project owner in a
   private message. Never commit it.
3. All network code is inside `#if FUSION2`. The symbol is now committed in
   `ProjectSettings.asset` (Scripting Define Symbols, with Fusion's own `FUSION_*`
   ones), so **a fresh clone does not compile until the Fusion SDK is imported**.
   To play offline without Fusion, remove `FUSION2` there and Apply. (No
   **Coat > Fusion** menu with Fusion imported means the symbol is missing.)
4. In the Network Project Config, set **Tick Rate to 50**. The ragdoll is tuned at
   the project's fixed timestep of 0.02 s; the host logs a warning if they differ.
5. Open `SampleScene`, run **Coat > Fusion > Setup Current Scene**, and save.

## Playing

- **From the main menu:** Play > **Create Lobby** reveals the round, loads the game
  and hosts a session named after the 4-character lobby code, shown on the status
  line (`lobby ABCD`). Friends type that code under Play and press **Join Lobby**.
  A code with no game behind it sends the joiner back to the menu with the reason.
  If the host cannot reach Photon, the game plays offline as before.
- Esc back to the menu ends the session (`CoatLobby.Leaving` shuts the runner down
  before the menu loads). A client whose host leaves is sent back to the menu and
  told why.
- Host and friends must be in the same Photon region. Fusion picks the best region
  for each machine, so friends far apart can miss each other ("GameNotFound"): set
  one **Fixed Region** in the Photon App Settings (Fusion's Realtime settings)
  before building. It is per machine, since `Assets/Photon/` is not committed.
- **In the game scene:** **F6** hosts, **F7** joins, with the session name set on
  `CoatFusionRunner` (default `4Limbs`), for testing without the menu. Starting loads the open scene additively, as in Photon's
  Host Mode tutorial, so Fusion takes it over instead of reloading it. The status line at the bottom of the screen shows
  the session, the player count and your limb.
- Limbs are dealt in join order: left leg, right leg, left arm, right arm.
- A limb nobody has joined for is played from the host's keyboard with its offline
  keys (I J K L, T F G H, P ; / '), so one person can test online alone.
- The host's status line counts network ticks. If it stays at 0, the game is not
  being run at all. The line above it lists who plays each limb and, for a joined
  player, whether their input is reaching the host ("input ok" / "no input"). An
  exception in the host's tick is shown in red above that.
- Everyone uses the same controls on their own machine: **W A S D** to move,
  **Left Shift** for grab/brace, **Q** to climb in or out of the coat (or the first
  gamepad).

## How it works

- `CoatFusionInputProvider` sends this player's input. `CoatFusionWorld` (on the
  host) files each player's input under their limb and runs `CoatGame.Tick`,
  `ClassicRagdoll.Tick` and whichever observers are up, then steps physics itself
  (`Physics.simulationMode = Script`), keeping the offline order: input, logic,
  one physics step.
- The host's own limb is read straight off the host's controls, not through
  Fusion's input: sent through it and read back with `TryGetInputForPlayer`, the
  host's limb never moved (cause not found). Remote players go through Fusion; a
  late or lost tick of theirs holds their last input for up to 0.2 s.
- The host sends every body of the disguise, the kids and the coat (pose and
  whether it is switched on), who is aboard, both observers (suspicion, tell,
  where they are looking), the van (phase, clock, door, who is home), the loot and
  the round result.
- Clients simulate nothing. `CoatFusionWorld.Render` copies the host's state every
  frame: bodies are kinematic and placed, the vehicle swaps observers, HUDs and
  camera from the host's "worn" state, and the van shows the host's clock and door.
- Host states arrive at the Server Send Rate (25 a second here), so a client moves
  each body, the loot and the coat from where it is drawn to the newest host pose
  over the time between the two (`StateStamp` says which tick a pose is from).
  Placed straight onto each pose, the client looked like it ran at 25 fps. A jump
  of more than 1.5 m, or anyone climbing in or out, is drawn as a jump. The status
  line shows fps, and on a client, host updates per second.
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
- A joining client sees no round reveal; it plays the host's round when it arrives.
- Cosmetics are not synced. The coat/head lock at round start is not written.
- No host migration: if the host leaves, the session ends.
