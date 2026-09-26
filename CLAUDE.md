# 4 Limbs — notes for the next session

Unity 6 co-op physics party game. Read this before touching anything: most of the
traps below were hit for real, and several look like the obvious thing to do.

## The game

Four players, one body, one coat. Each player controls ONE limb of a single
active-ragdoll body — left leg, right leg, left arm, right arm — and together they
are four kids stacked in a trenchcoat trying to pass as one normal person.

- **The loop (the heist, the only playable round today):** the four start as their
  own small characters inside a van. Each walks to the coat and climbs in. When all
  four are aboard the shutter goes up and the clock starts. They walk out as one
  person, steal the loot (a cake on a tray, on a table outside), and bring it back.
  The round ends when everyone is home and the loot has been delivered.
- **Walking:** each leg player steps their own leg. Opposing inputs (left leg
  pushing A while right leg pushes L, or W against K) slide the feet apart into the
  splits; the body only collapses once it genuinely loses balance, not instantly.
- **Arms:** reach and grab. Two hands carry the tray level; one hand drags it;
  falling over drops it (counted as a fumble).
- **Suspicion:** an observer NPC reads the body's physical pose for "tells" (limbs
  fighting each other, a missing leg, an empty sleeve...). Tells fill a suspicion
  meter; full means **rumbled**. Getting rumbled at any point busts the round.
- **Result:** Got away pays coins (fewer for fumbles and for how close you came to
  being caught); Busted pays nothing.
- **Around it:** main menu (Play / Settings / Shop / Quit), a random round picked at
  game start with a reveal reel, a cosmetics shop (empty), saved coins/settings.

Online 4-player (Photon Fusion 2.1) is written and compiling, not yet run — see
`Scripts/Fusion/README.md`. Without Fusion imported it is four players on one
keyboard (or gamepads).

### Controls

| Role | Move | Action (grab / brace) | Coat (climb in/out) |
|---|---|---|---|
| Left leg | W A S D | Left Shift | Q |
| Right leg | I J K L | U | O |
| Left arm | T F G H | R | B |
| Right arm | P ; / ' | Right Shift | M |

Gamepads, if connected, take a player slot each: left stick / south button or right
trigger / west button. `F5` restarts the round, `[` `]` orbit the camera, `Esc` goes
back to the menu, `\` mirrors the left leg onto the right (solo testing aid only).

## Running it

- Unity **6000.5.1f1**, URP 17.5, Input System 1.19. No other packages of ours.
- `Assets/Scenes/MainMenu.unity` is build index 0 (full flow: menu → Create Lobby →
  round reveal → game). `SampleScene.unity` is the game; pressing Play on it directly
  also works and is how most testing is done (a round is still drawn, no reveal).
- Save file (coins, owned/equipped cosmetics, round history, settings) is JSON at
  `%USERPROFILE%\AppData\LocalLow\DefaultCompany\MultiPlayer\coat-profile.json`.
- Everything is in namespace `Coat` (`Coat.Classic` for the disguise body).

## How the code is organised

All game code is under `Assets/CoatGame/`.

**`Scripts/` — the game**
- `CoatGame` — the tick orchestrator. Nothing drives itself except the observers;
  `CoatGame.Tick(dt)` ticks the characters, the coat, the vehicle, the loot, then the
  van, in that order. Also draws the round for direct-Play sessions (in `Awake`).
- `CoatCharacter` + `CoatLeg` — one kid on their own legs (two legs, real arms).
- `TheCoat` — the wearing system: `_wearers[]` indexed by `CoatRole`.
- `CoatVehicle` — the handover. When anyone climbs in, the kid is switched off
  entirely and the shared disguise body stands up wearing only the limbs that are
  aboard. It also **swaps the observers and HUDs** by whether the coat is worn.
- `Classic/ClassicRagdoll`, `ClassicLimb` — the disguise body itself (the original
  single-body active ragdoll, kept because four sprung torsos never moved like one
  skeleton). `ClassicObserver`, `ClassicHud` — the observer and HUD used while worn.
- `CoatObserver`, `CoatHud` — the observer and HUD used while the crew are loose.
- `CoatVan` — the round: phases `Loading → Opening → Away → Back`, home detection,
  and the round outcome. The only thing that knows what a "round" is.
- `CoatLoot` — the prop: hands on it, delivered, fumbles.
- `CoatSkin` — drapes the skinned mesh over the flat rigidbody rig.
- `LocalCoatInput` — keyboard/gamepad → `CoatInputState` per role. Swap point for
  networked input.
- `CoatTypes` — `CoatRole` (`LeftLeg=0, RightLeg=1, LeftArm=2, RightArm=3`); most
  per-player arrays are indexed by `(int)role`.
- `CoatPalette` — every colour in the game (Frog Sqwad's palette), with each
  character colour's toon shadow tone. The builders, the HUD and the menu read it.

**`Scripts/Fusion/` — online play** (all inside `#if FUSION2`; written by Jae, the
networking collaborator, merged onto current `master`). `CoatFusionWorld` is the
host's tick and the clients' copy of it; `CoatFusionInputProvider` sends input;
`CoatFusionRunner` starts/joins (F6/F7); `CoatFusionHud` is the status line. Offline
loops are switched off through an `ExternalSimulation` flag on `CoatGame`, `CoatVan`,
`ClassicRagdoll` and both observers. Its README has setup and what's missing.

**`Scripts/Meta/` — everything around the game**
- `CoatMenu` (IMGUI front end), `CoatSession` (self-spawning, applies settings, Esc →
  menu), `CoatLobby` (Create/Join seam; `Open` draws the round, `StartGame` loads).
- `CoatProfile` / `CoatSave` (the save file), `CoatCosmetics` / `CoatShopStock`
  (catalogue), `CoatLoadout` (who wears what), `CoatCosmeticFitter` (hangs a
  cosmetic prefab on a bone).
- `CoatRounds` / `CoatRoundStock` (the draw), `CoatRoundReel` (the reveal),
  `CoatRoundResult` (outcome + payout).

**`Editor/`** — one harness per system, all under the `Coat/` menu. Pure-logic
checks run without Play mode (`Check Shop Rules`, `Check Round Draw`, `Check Round
Result`); physics checks need Play mode (`Test Round Result (play mode)`, `Test The
Job`, `Check Stepping`, ...). Builders: `Build Main Menu`, `Build Round Stock`,
`Dress The Rig`.

**`Art/`** (inside Assets) — `Disguise.fbx`, `RedChild.fbx`, per-limb materials,
`CoatHidden.shader`, `CoatToon.shader` (the characters), `CoatSky.shader` (the
gradient sky), `CoatHazard.png` (kerb stripes). **`Resources/`** — `CoatShopStock.asset`, `CoatRoundStock.asset`.

**`/Art` at the repo root** — the Blender side, run headless
(`blender.exe --background --python <script>`): `build_characters.py` (procedural
builder, superseded), `clean_scan.py` (turns generated scans into usable characters;
its header lists the dead ends), `rig_for_unity.py` (armature with bone names matching
the rigidbody names, exports FBX into `Assets/CoatGame/Art/`), `FourLimbs.blend`.

## Design decisions, and why

**Physics**
- Active ragdoll = `ConfigurableJoint` slerp drives chasing target rotations. The rig
  is **flat**: every rigidbody is a direct child of the rig root, held together only
  by joints. Don't reason about it as a transform hierarchy.
- Every rig part is on layer `CoatLayers.Rig` (8), which ignores itself, so limbs
  pass through each other and only hit the world.
- Tells are read off **physical state, never input** — it keeps working when input
  arrives over a network, and a client can't cheat it.
- Steps have no cooldown and no forced alternation on the disguise body
  (`ClassicRagdoll.AlternatingSteps = false`, `StepCooldown = 0`) — asked for
  explicitly; the stutter felt like a cooldown.

**Shop / cosmetics** (settled with the owner — don't reopen)
- Bought per **pair** (Legs, Arms), never per limb, because the limb isn't known
  until the match. Worn on whichever limb of that pair you draw. Draw the other pair
  and you wear **nothing**, and it does not transfer to whoever drew a leg. A limb is
  dressed only by the player in it.
- Coat and Head are shared: a **vote** — most picked wins; a tie goes to the lobby
  leader; if the leader owns none, lowest seat among the tied. Owning nothing is not
  a vote.

**Rounds**
- Recently played rounds are rarer by **recency, not lifetime plays** (a lifetime
  count would permanently bury a round you like). Penalty 0.05× just after playing,
  back to 1× after 5 rounds.
- The roll is a hand-written **SplitMix32** hash, not `System.Random`: seeding
  `System.Random` from a counter/`TickCount` and taking its first value is badly
  correlated (skewed a 6-round spread to 8%–27%), and its algorithm isn't stable
  across runtimes, which matters once host and clients must agree.
- The reveal reel **displays** the already-drawn round; it never picks one.

**Round outcome**
- Read `EverRumbled`, **never `Rumbled`**. `Rumbled` self-clears after `ResetAfter`
  (4 s) by design; gating on it would let a bust cool off before you get home.
  `EverRumbled`/`PeakSuspicion` only reset in `ClearSuspicion()`, called by
  `CoatVan.Restart()` on both observers.
- Busted pays exactly 0 (stakes are the joke). Getaway pays
  `100 − 15×fumbles − round(40×peakSuspicion)`, floored at 0. A bust is not a best time.
- The verdict banner is drawn by `CoatVan`, not a HUD — the HUDs swap and a round
  always ends with the coat worn.

**UI** — everything is IMGUI, to match the existing HUDs: no canvas, no prefabs, no
scene wiring, drivable from a harness. Rules live outside the UI classes.

**The look** (Frog Sqwad's, asked for by the owner; art direction pages "The Frog
Sqwad Look" / "The 4 Limbs Look")
- **Colour gives you away.** The world, the coat and the observer are muted; the
  four limbs are the only loud colour on the body, so every tell shows up in colour.
  Only goals and hazards (violet van, orchid cake, striped kerb) are also strong.
- Limbs: coral red `#FE564D`, cyan `#57D0D9`, orange `#FEAF32`, lime `#93DE5A` —
  each kept in its old colour family. All values live in `CoatPalette`; change them
  there, then re-run Dress The Rig / Build Test Scene (both overwrite the materials).
- **Characters only** are toon shaded (`Coat/Toon`): lit and shadow as two flat
  colours, a small hard highlight, a plum outline (`#2F1643`, never black). The lit
  colour is the hex value itself, not scaled by the sun, so swatches match the
  screen. The world stays on URP Lit with no outlines. `CoatFabric` (the coat tube)
  is double-sided (`_Cull` 0) with no outline, or its inside would draw plum.
- Scene: gradient ambient light, a warm sun (`#FFF0D8`), linear fog 35–110 m, and
  the `Coat/Sky` gradient sky instead of the default skybox.

**Networking** (designed with rickleo, the networking collaborator — keep to it)
- Photon **Fusion**, host-authoritative. One peer simulates the whole body; everyone
  else sends input only. **Never split state authority across jointed limbs** — the
  body tears itself apart.
- On the wire: root + the **live** IK targets (~19 floats vs 195 for all bodies).
  Remotes pose the body kinematically. `Solve()` writes joint drive targets, which do
  nothing on a machine with no physics, so remotes need a second apply path.
- The observer is **host-only and one-way.** Host joints sag under load, remotes see
  clean IK, and the observer grades bone poses — clients would compute a different
  suspicion. Guard in **both** observers: `if (!IsHost) { Paint(); return; }` before
  `Sees()`/`Evaluate()`. That makes the observer's head turn replicated state.
  `ClearSuspicion` is host-only. Clients predict nothing.
- Observer payload: 3 bytes — suspicion, tell strength, then a 5-bit tell id (16 tells
  exist, so 4 bits is full) + canSee + rumbled + a spare bit. Tell is an enum; names
  stay client-side.
- Round: host draws and replicates the round **id**, never the seed (the weighting
  reads per-player history). Limb cosmetics resolve locally from replicated equipped
  ids. Coat/head: local for the lobby preview only; the host resolves and **locks**
  them at round start. The round outcome is host-settled too (not yet reviewed by
  rickleo).
- As built: the host ticks the game in `FixedUpdateNetwork` and then steps physics
  itself (`simulationMode = Script`) to keep offline's input → logic → physics order;
  Fusion's Tick Rate must be 50 to match the 0.02 s the ragdoll is tuned at. The host
  reads its own limb straight off its keyboard (through Fusion's input it never
  arrived; cause unknown, so watch remote players' "input ok" on the status line).
  The host sends every synced body's full state (~76 bodies), not the root + IK targets above —
  a first version; move to the plan if bandwidth hurts. Clients settle the host's
  result numbers locally (`CoatVan.ApplyNetworkResult`) so each is paid into their
  own save; `CoatSave` can only write the local file. Online, everyone plays on
  control set 0 (W A S D / first gamepad); the role only says where the host files it.

## Traps (read these)

- **Two observers, two HUDs, swapped by worn state.** `WornObserver` =
  `ClassicObserver`, `LooseObserver` = `CoatObserver`, same for `ClassicHud` /
  `CoatHud`. Edit mode has the coat off, so the loose ones look like the only live
  ones. During the heist it's the opposite. Read both; get them from `CoatVehicle`.
- **Joint projection stays off.** It teleports bones into impossible poses.
- **Bind the skin at rest in the editor, never at runtime.** `CoatSkin._links` is
  serialized; re-binding in `Start` baked a mid-spawn pose in (mesh 0.53 m adrift).
- **Blender's FBX export permutes material slots** (`body, armR, armL, legR, legL` →
  `armR, legR, body, legL, armL`) and mirrors X (Blender −X = Unity +X = right).
  Classify submeshes by geometry (`CoatDressUp.Classify`), never by slot order, and
  never log labels you assigned yourself — that's what hid this for three rounds.
- **Hiding a limb:** check `activeSelf`, not `activeInHierarchy`.
- **`CoatVertexColor.shader` is a dead end** — renders in the Scene view, draws
  nothing in the Game view (borrowed a depth pass that rejects it). The world uses
  stock URP Lit; the characters use `Coat/Toon`, which writes its own DepthOnly and
  DepthNormals (the renderer is Forward+ with SSAO, so both run). Any shader change:
  check the Game view, not just the Scene view.
- **`CoatSave.Save` writes one fixed path whatever profile you pass.** Tests must
  never call anything that saves with a scratch profile — use
  `CoatRoundResult.Evaluate`, not `Settle`; `CoatRounds.Record`, not `Begin`.
- **Harnesses:** set `Physics.simulationMode = Script`, step with `Physics.Simulate`,
  feed keys with `InputSystem.QueueStateEvent`. Observers tick from `FixedUpdate`,
  which never fires inside a synchronous harness loop — tick them by hand, only if
  `isActiveAndEnabled`. To force a bust, set `Suspicion` **above** 1 (e.g. 1.5);
  `[Range]` is inspector-only, and at exactly 1.0 that tick's decay lands at 0.992.
- **Unity MCP (`Unity_RunCommand`):** compiling a command during Play mode reloads
  the domain and wipes statics without re-running `Awake` — a working system reads
  null. Stopping Play mode restores the pre-play scene, so check
  `GetActiveScene().name` every cycle (a whole debugging session was lost testing the
  wrong scene). Reflection is blocked. Big snippets can drop the command DLL — keep
  harnesses as editor scripts. "Could not find type RunCommandMacroEvaluatorEntryPoint"
  is transient: retry. For persisted state, the save file on disk is the reliable
  witness.
- **Measure the defect before fixing it.** A hand "membrane" was once cut away that
  didn't exist and wrecked the model. Only touch what was flagged.
- **Don't change the characters' appearance** (the disguise, the kids) unless asked.
  (Colours and toon shading were asked for; the models are unchanged.)
- **Fusion 2 proxies don't run `FixedUpdateNetwork`.** Anything a client must show
  from the host's state goes in `Render()`.
- **`FindFirstObjectByType` skips switched-off objects.** The disguise and both
  observers are off whenever the coat isn't worn — get them from `CoatVehicle`.
- **Never upload whole files through GitHub's website** on top of newer code. That
  once put back old copies of `CoatVan` and both observers and deleted the round
  outcome. Pull `master` first, change it, push with git.

## Status

**Done and tested**
- Active ragdoll body, walking, splits/collapse, grabbing, carrying, falling.
- Crew kids with two legs and real arms, coloured by limb; boarding/leaving the coat.
- Skinned characters from the Blender pipeline, limbs hidden per aboard player.
- Observer/suspicion with tells, the van heist loop, loot/fumbles.
- Round outcome: got away / busted, payout into the save, verdict banner, busts don't
  set best time (24 logic checks + 3 play-mode scenarios).
- Main menu, settings (all wired to something real), Esc → menu.
- Shop rules and cosmetic resolution (48 checks), round draw + reveal reel (30 checks).

**Half-done**
- Frog Sqwad palette + toon shading (`CoatPalette`, `Coat/Toon`, `Coat/Sky`): in the
  owner's editor, not yet judged against Frog Sqwad side by side.
- Online (`Scripts/Fusion/`): compiles against Fusion 2.1.3 in the owner's editor, and
  F6 hosts. The host is dealt a limb, and limbs nobody joined for move on the host's
  offline keys. The host's own limb reading straight off its keyboard is the latest
  fix, not yet confirmed. The status line counts host ticks, shows tick errors in
  red, and each limb's player and input. A second peer (F7) has never joined.
  Fusion is never committed (`Assets/Photon/` is gitignored; the repo is public and
  the SDK holds the owner's App ID). Not wired to the menu:
  `CoatLobby.Online` is still false. F5 restart is off online.
- Coat/head host lock: agreed, not written. (The host-only observer is done, via
  `ExternalSimulation`, in both observers.)
- Roles are pinned to seat index — nobody is ever dealt a different limb, so the
  per-limb cosmetic rule is inert. `CoatLoadout.Leader` is hard-coded to seat 0;
  whatever deals roles must set it.
- Shop catalogue is empty on purpose. The "Add" tab was read as "top up coins" (a
  placeholder with an editor-only grant); unconfirmed with the owner.
- Round stock is six empty placeholders. The reveal plays only via the menu path.
- The play-mode round test settles real rounds, so it adds real coins to the save.

**Broken / known issues**
- **The body walks worse after every `Restart()`** — noted in `CoatJobTest`, and it
  couldn't get out of the van in 14 s after a restart during testing. Unmeasured,
  cause unknown. Hurts the core replay loop.
- Leg-role kids take a normal number of steps but barely move (0.26 m vs 1.84 m for
  arm-role kids in 5 s). Undiagnosed; they spawn at the van's mouth, collision is the
  suspect. The owner was asked to walk one around to confirm.
- `Person_LeftLeg.InCoat` can read true while the coat's `Count` is 0.
- `ClassicRagdoll.WatchFalls` is on and spams `[coat] spine … off vertical` warnings;
  turn it off before recording.
- The disguise mesh is ~44k faces; decimation was offered, not done.
- No Git LFS: `.fbx` and `.blend` files are committed as plain binaries.

## Next

1. **Measure, then fix, the walking-after-restart degradation.** Distance walked in
   5 s on a fresh session vs after 1, 2, 3 restarts; suspect state `Restart()` resets
   for the coat and crew but not the body's limbs (step timers, drive targets,
   planted feet). Unverified guess.
2. **Milestone 1:** the current heist fully playable online with 4 players, plus a
   proper win/lose (win/lose now done). The code is written; next is the first
   compile with Fusion 2 imported, then a real 2-4 player test. No new rounds until
   online feels right.
3. The coat/head lock at round start, and F5 restart online.
4. Role dealing — matters once each player has their own profile online; must set
   `CoatLoadout.Leader`.
5. Rounds, one at a time after online: parkour, horror house escape, fast food
   restaurant, goalkeeper, sword fight, driving (hands on the wheel, feet on the
   pedals), carrying furniture through stairs/elevators/obstacles, airport security,
   wedding waiter. Adding one = an entry in `CoatRoundStock` + a scene.
6. Cosmetics art and coin packs; cleanups from the issues list.
