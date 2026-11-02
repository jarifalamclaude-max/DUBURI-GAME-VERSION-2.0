# Duburi.Core — API guide for the Unity layer

Pure C# port of the game logic in `src/` (physics, bots, course, judging, CPU
pilot). Namespace `Duburi.Core`, assembly `Duburi.Core.asmdef`
(`noEngineReferences: true`). No UnityEngine, no threads, no IO.

It runs **bit-identical** to the browser game: `tools/check.sh core` replays the
JS (`tools/golden/make-golden.mjs`) and compares about 440 000 values: math,
water, thrusters, environment, allocation, 900-step vehicle traces for every
bot, courses, stats, and whole autopilot matches up to 440 s with 4 vehicles.
All of them are equal to the last bit. It also runs the assertions of
`tests/game.test.mjs` and checks that `Game.Step` allocates nothing.

## Conventions

* **Names.** Fields keep the JS names in lower case (`veh.p`, `run.scores`,
  `course.gate`). Methods are PascalCase (`Step`, `Action`, `ToWorld`).
  Module-level JS constants keep their JS names (`BOTS`, `POINTS`, `DT`).
  The table below gives the JS name for everything.
* **Units.** SI throughout: m, s, kg, N, N·m, rad, m/s, A, V, Pa. PWM is in µs.
  Stats rates are in deg/s, as in the JS.
* **Frames.** The world is **NED** (x north, y east, z down; origin at the still
  surface, pool centre; depth = +z). The body is **FRD** (x forward, y right,
  z down, origin at the CG). Quaternions are `Quat(w, x, y, z)`, body → NED
  (index `q[0]` = w, like the JS array). Course positions on the floor plan are
  `V2(north, east)`. To convert to Unity, see PORTING.md "Coordinates": NED → Unity world
  `(y, -z, x)`, `Quaternion(-qy, qz, -qx, qw)`.
* **Value types.** `V2`, `V3` and `Quat` are structs with public fields
  (`x, y, z` / `w, x, y, z`) and a JS-style indexer (`v[0]`). Fields of these
  types on classes can be changed in place: `veh.p.z = 1;`.
* **JS semantics.** `OrderedMap<K,V>` is a JS object used as a map (keys keep
  insertion order). `Js.*` has JS number formatting (`Js.Str`, `Js.ToFixed`,
  `Js.ToLocaleString`, `Js.PadStart`), `Js.Number(string)` and `Js.StableSort`.
  `JsMath.*` is V8's `Math`. Use `JsMath` / `MathUtil` in any code that changes
  game state, so it stays deterministic.
* **Name clashes.** `Duburi.Core.Environment` clashes with `System.Environment`, and
  inside `namespace Duburi.Game` the simple name `Game` means the namespace. Write
  `Duburi.Core.Game`, or add `using CoreGame = Duburi.Core.Game;` and
  `using Env = Duburi.Core.Environment;`.
* **Randomness.** Matches are seeded (`GameOptions.seed`). Only defaults (a missing
  seed, item ids) use `CoreRandom` (the stand-in for `Math.random`). Call
  `CoreRandom.Seed(...)` to make those reproducible too.

## Starting a match

```csharp
using Duburi.Core;
using CoreGame = Duburi.Core.Game;

MatchSetup S = MenuSetup.DefaultSetup();          // or the saved one (PlayerPrefs "life-as-duburi-setup-v2")
var opts = new GameOptions { scenario = S.scenario, runMinutes = S.minutes, coinFlip = S.coinFlip, depth = S.depth, players = new List<PlayerConfig>() };
foreach (var s in S.slots) if (s.type != "off") opts.players.Add(new PlayerConfig { name = s.name, bot = s.bot, color = s.color, device = s.type });
var game = new CoreGame(opts);                     // = menu.js start() + main.js start(cfg)
var pilots = new Autopilot[game.players.Count];   // a CPU player: device == "cpu"
for (int i = 0; i < pilots.Length; i++) if (opts.players[i].device == "cpu") pilots[i] = new Autopilot(game, i);

// fixed 120 Hz loop (main.js): per physics step
foreach (var ap in pilots) ap?.Update(Vehicle.DT);
// humans: write game.players[i].veh.input.surge/sway/heave/yaw/roll/pitch (-1..1) before the step
game.Step(Vehicle.DT);
// actions from buttons: game.Action(i, "marker" | "torpedo" | "grip" | "depthHold" | "skip" | "endRun" | "restart" | "forfeit" | "leave")
// events for HUD / sounds: read game.events, then game.events.Clear()   (main.js: g.events.splice(0))
```

## API by JS module

### `src/ui/menu.js` (pure parts) → `Sim/Setup.cs`
| C# | JS | notes |
|---|---|---|
| `MenuSetup.GAME_NAME` | `GAME_NAME` | "Life as Duburi" |
| `MenuSetup.DefaultSetup()` → `MatchSetup` | `defaultSetup()` | `{scenario "pool", minutes 15, coinFlip true, depth 0, slots[4]}`; same JSON field names as the saved setup |
| `MatchSetup.scenario, minutes, coinFlip, depth, slots`, `Clone()` | setup object | `minutes` is the match clock (min). `depth` is one of `DEPTH_CHOICES`, 0 = the water's own |
| `SlotSetup.type, name, bot, color`, `Clone()` | slot | `type`: "off" or a device id ("kbA", "kbB", "pad0".., "cpu"). `bot`: BOTS key. `color`: "#rrggbb" |

### `src/sim/game.js` → `Sim/Game.cs`
| C# | JS | notes |
|---|---|---|
| `new Game(GameOptions o)` | `new Game({...})` | `GameOptions`: `scenario` ("pool"), `players` (`List<PlayerConfig>`; null = one Duburi), `runMinutes` (15), `coinFlip` (true), `depth` (0 = scenario depth, m), `seed` (`double?`, null = random) |
| `PlayerConfig.name, bot, color, device` | players[i] | an empty name becomes "Player n", an empty colour becomes the bot colour. `device` is not used by Core |
| `game.Step(dt = Vehicle.DT)` | `step(dt)` | advances the environment, the vehicles (pilot loop + plant), contacts, items and judging. Ends the match when the clock runs out, or after 2 s / 10 s when nobody has a run going. Allocates nothing |
| `game.Action(int i, string what)` | `action(i, what)` | `restart`/`forfeit` (new run, points lost), `leave`, `skip`, `depthHold` (toggle), `endRun`, `marker`, `torpedo`, `grip` (grab / release) |
| `game.StartRun(p, first)`, `EndRun(p, reason)`, `SkipTask(p)`, `Leave(p)` | same | normally reached through `Action` |
| `game.Total(p)` | `total(p)` | points of the current run |
| `game.CurrentTask(p)` | `currentTask(p)` | "gate", "slalom", "bin", "torpedo", "octagon" or "home" |
| `game.GetTaskStart(task)` → `TaskStart {pos V3, hdg}` | `taskStart(task)` | NED, rad |
| `game.GetPingerBearing(p, id)` → `PingerBearing {rel, dist, pinger}` | `pingerBearing(p, id)` | `rel`: rad relative to the heading (wrapped to ±π). `dist`: m. `id`: "octagon" or "torpedo" |
| `game.Remaining()` | `remaining()` | s left on the clock |
| `game.BinLocal(V3 p)` → `V2` | `binLocal(p)` | (along the bin heading, to its right) |
| fields `scenario, seed, depth` (m), `arena`, `env`, `course`, `duration` (s), `coinFlip`, `time` (s), `over`, `idle` | same | |
| `game.players` `List<Player>` | `players` | |
| `game.items` `List<Item>` | `items` | the trash (cup, spoon) plus dropped markers and fired torpedoes |
| `game.events` `List<GameEvent>` | `events` | `{player` (index, -1 = match), `text, pts, kind` ("info", "warn", "score", "bad"), `t` (s)`}`. Core only appends to it |
| `game.rand` `Func<double>` | `rand` | the match's mulberry32 (`Mulberry32`) |
| `POINTS.coinFlip … POINTS.timePerMinute` (const double) | `POINTS` | all point values |
| `Game.TASK_KEYS` `string[]` | `TASK_KEYS` | coin, gate, style, slalom, bin, torpedo, octagon, home, time (display order) |
| `Game.TASK_LABEL` `OrderedMap<string,string>` | `TASK_LABEL` | e.g. "bin" → "Bin (BRUVS)" |
| `Game.ITEM_PHYS` | `ITEM_PHYS` | `ItemPhys {mass kg, vol m³, cq, cl, r m}` |

**Player** (`game.players[i]`): `index`, `name`, `color` ("#rrggbb"), `bot` (id), `design` (`BotDesign`),
`veh` (`Vehicle`), `run` (`Run`, the current attempt), `runs` (`List<RunResult {total, scores, reason}>`, the ended runs),
`best` (best total), `left` (has left the match), `touched` (`HashSet<string>`, obstacle tags touched
in the last step: gateLeg, gateBar, gateDivider, slalomRed, slalomWhite, bin, board, boardPole, table, octagon, bot).

**Run** (`p.run`): `state` ("running" | "ended"), `reason`, `startTime` (s), `coin` ("heads" | "tails" | null),
`markers`, `torpedoes` (left), `held` (`Item` in the gripper or null), `scores` (`Scores`), `leftStart`,
`gatePassed`, `gateSkipped`, `chosen` ("shark" | "sawfish" | null), `gateSide` (−1 left, +1 right), `finished`
(completed: returned home or skipped home), `total` (set when the run ends), `slalomBest` `double[3]`,
`torpedoHits` (`List<TorpedoHit {through, animal, far, t}>`), `octSurface`, `octItems`, `faceBest`, `rotBest`,
`basketPts` (`OrderedMap` kind → pts), `binBest`, `skipped` (task names). Judging state: `gateS, homeS, inGateBuf, styleAcc` (V3, rad),
`gateTouched, gateTouchedThis, slalomS`, `faceTimer`, `rotAcc`. `gateAttempt` is the time of the
gate pass as `double?` (JS `gateAttempt.time`).

**Scores**: fields `coin, gate, style, slalom, bin, torpedo, octagon, home, time`, plus the indexer
`scores["bin"]` with the `TASK_KEYS` names, and `Clone()`.

**Item**: `id`, `kind` ("marker" | "torpedo" | "cup" | "spoon"), `owner` (`Player`, null for the trash),
`pos`, `vel` (V3 NED, m and m/s), `state` ("rest" | "fall" | "held"), `on` (what it rests on: "table" |
"bin" | "basket" | "floor" | null), `holder` (`Player` holding it), `inBasket` ("cup" | "spoon" | null),
`releasedBy`, `home` / `hasHome` (start position of the trash), `t` (s falling), `ph`, `meff`; torpedo:
`dir` (V3 firing direction), `fireDist`, `prevN`.

### `src/sim/vehicle.js` → `Sim/Vehicle.cs`
| C# | JS | notes |
|---|---|---|
| `Vehicle.DT` = 1/120 | `DT` | physics step (s) |
| `new Vehicle(BotDesign, Environment)` | `new Vehicle(design, env)` | |
| `veh.Step(dt, armed = true)` | `step(dt, armed)` | pilot loop + 6-DOF plant. Allocates nothing |
| `veh.Control(dt)` | `control(dt)` | pilot loop only (called by `Step`) |
| `veh.Reset(V3 pos, double heading)` | `reset(pos, heading)` | at rest, level, depth hold on, inputs cleared |
| `veh.input` (`VehicleInput`) | `input` | `surge, sway, heave, yaw, roll, pitch`, all −1..1. Roll / pitch are rate commands. Heave > 0 goes **up** (JS: Fz = −heave · wmax). `Clear()` sets all to 0 |
| `p` V3 | `p` | position, NED m |
| `v`, `w` V3 | `v`, `w` | body velocity (m/s, FRD) and body rates (rad/s) |
| `q`, `targetQ` Quat | `q`, `targetQ` | attitude body → NED, and the attitude the stabiliser steers to |
| `R` `double[9]` | `R` | rotation body → NED, row-major (updated in place) |
| `hdg`, `depthTarget`, `depthHold`, `depthI`, `acro` | same | rad, m |
| `depth` (property) | `get depth` | m below the local wavy surface |
| `Euler()` → V3 (roll, pitch, yaw) | `euler()` | rad, ArduPilot 3-2-1. Yaw = heading from north |
| `ToWorld(V3 body)`, `BodyToWorldDir(V3)`, `WorldVel()`, `Speed()` | same | NED |
| `ProjectedHeading(Quat)`, `PwmFor(i, N)` | same | |
| `th` `ThrusterState[]` | `th` | per thruster (same order as `design.thrusters`) |
| `u` `double[]` | `u` | thruster commands −1..1 |
| `diag` (`VehicleDiag`) | `diag` | `submerged` 0..1, `buoyancy`, `weight`, `net` (N), `rho` (kg/m³), `thrust` (N, Σ\|T\|), `current` (A), `flow` (V3 NED m/s). Updated in place |
| `lastImpact` | `lastImpact` | m/s of the last knock (NaN until the first. main.js reads it and sets it to 0) |
| `mass`, `volume` (m³), `Mt`, `It` (`double[3]`), `wmax` (`double[6]` N / N·m), `nom`, `P`, `voltage`, `d` (design), `env` | same | |

### `src/physics/thruster.js` → `Physics/Thruster.cs`
| C# | JS | notes |
|---|---|---|
| `ThrusterState`: `n` (signed rotor speed, ±1 = full), `T` (property, static thrust N), `Tcmd`, `Teff` (N applied last step), `vent` (0..1, ventilation), `pwm` (µs), `vbus` (V), `current` (A), `dwell`, `def`, `curve`, `preset` | `ThrusterState` | render: prop spin ∝ `n`, bubbles ∝ `Teff`. Audio: Σ\|n\| |
| `Update(pwm, dt[, vbus])`, `Effective(inflow, depth)`, `ReactionTorque()`, `VoltageScale(V, sign)`, `MaxCurrent(V)`, `Side(sign)` | same | |
| `ThrusterDef` | thruster object | `name, pos, axis` (V3 FRD), `preset`, `hand` (±1), `table`. Optional (`null` = JS undefined): `fwdMax, revMax, enabled, voltage, tableVoltage, torqueRatio, reverseDwell, J0, J0Rev, escMinV, idleCurrent, propDiameter, maxRpm, tauUp, tauDown, polarity, health, deduction, deductionRev, inflowModel, tableOnReversedUnit, elecSet/elec/elecData`. `Clone()` |
| `ThrustCurve {pwm[], T[], fwdMax, revMax}`, `Thrust(pwm)`, `Set(table)` | `ThrustCurve` | |
| `Thrusters.ParametricCurve(CurveParams)` → `double[][]` rows [pwm, N] | `parametricCurve` | |
| `Thrusters.THRUSTER_PRESETS` (`OrderedMap<string, ThrusterPreset>`), `Thrusters.Preset(key)` | `THRUSTER_PRESETS` | "t200-16v", "t200-12v", "t500-20v", "generic-small". `propDiameter` (m) gives the mesh size |
| `Thrusters.ELECTRICAL`, `Thrusters.ParseCurveText(text, unit)` | `ELECTRICAL`, `parseCurveText` | |

### `src/sim/bots.js` → `Sim/Bots.cs`
| C# | JS | notes |
|---|---|---|
| `Bots.BOTS` (`OrderedMap<string, BotDesign>`), `Bots.Get(id, fallback)` | `BOTS` | duburi, barracuda, kraken, manta, dubomini |
| `Bots.BOT_ORDER` `string[]` | `BOT_ORDER` | lobby / garage order |
| `Bots.PLAYER_COLORS` `string[8]` | `PLAYER_COLORS` | "#rrggbb" |
| `BotDesign`: `id, name, kind, blurb, color`, `mass` (kg), `Bnet` (N), `dims` (V3 m: length, width, height), `zB` (m), `inertia` (V3 kg·m²), `addedMass`, `dampLin`, `dampQuad` (`double[6]`), `thrusters` (`ThrusterDef[]`, body FRD), `spheres` (`Sphere[]`: `c` V3 + `r`, collision), `gripper`, `marker`, `torpedo` (V3 body mount points) | design | |

### `src/sim/scenarios.js` → `Sim/Scenarios.cs`
| C# | JS | notes |
|---|---|---|
| `Scenarios.SCENARIOS` (`OrderedMap<string, Scenario>`), `Scenarios.Get(id)` (falls back to pool) | `SCENARIOS` | pool, pacific, arctic |
| `Scenarios.SCENARIO_ORDER` | `SCENARIO_ORDER` | |
| `Scenarios.DEPTH_CHOICES` `double[]` | `DEPTH_CHOICES` | {0, 5, 8, 12, 16, 20} m, 0 = scenario depth |
| `Scenarios.WaterDepth(id, override)`, `Scenarios.EnvironmentConfig(id, depth)` → `EnvConfig` | `waterDepth`, `environmentConfig` | |
| `Scenario`: `id, name, place, blurb, env` (`EnvConfig`: temperature °C, salinity g/kg, current V3 NED m/s, turbulence, waves, visibility m), `depth` (m), `pool {length, width, style}` | scenario | |
| `ScenarioLook`: `water, fog, sky, sun, surface` (int 0xRRGGBB → `Colors.Rgb`), `fogDensity` (FogExp2), `floor` ("tiles" \| "sand" \| "gravel"), `light`, `particles` (0..1), `life` ("none" \| "reef" \| "arctic"), `ice` | `look` | |

### `src/sim/course.js` → `Sim/Course.cs`
| C# | JS | notes |
|---|---|---|
| `Course.ARENA` (`Arena {length 50, width 25, depth 7}`) | `ARENA` | m. `game.arena` is a copy with the match depth |
| `Course.Build(Func<double> rng, depth)` | `buildCourse(rng, depth)` | the Game builds its own |
| `Course.BoardLocal(Board, V3)` → `BoardCoords {u, v, n}` | `boardLocal` | u right as seen from the front, v up, n toward the viewer (m) |
| `course.floor` (m), `random {sharkSide, boardSharkTop, binSharkNear}`, `dock {pos, radius}` | same | |
| `gate {pos, heading, width, legDepth, topDepth, barH, dividerDepth, sharkSide, dir, right}` | `gate` | V2 dir/right (north, east). legDepth / dividerDepth are V2 [top, bottom] depths |
| `paths` (`{pos, heading}`), `slalom {pos, sets[{pos, pipes[{p, color}]}], heading, dir, right, band}` | same | band = [top, bottom] depth of the pipes |
| `bin {pos, size, plate, height, sharkNear, heading, dir, right, halves[2]}` | `bin` | halves = [near, far] animal |
| `board {pos, depth, size, sharkTop, heading, dir, right, openings[{u, v, r, shape, animal, label}], farDistance}` | `board` | |
| `octagon {pos, radius, tableTop, tableHalf, heading, images[{animal, angle}], baskets[{item, c, h, depthTop}], trash[{kind, pos}]}` | `octagon` | |
| `obstacles` (`Obstacle {type, tag, p, r, z0, z1, c, h, yaw, board, R, z}`), `pingers` (`{id, pos, freq kHz, period s}`), `tasks` (`{id, name, pos}`) | same | |

### `src/sim/collide.js` → `Sim/Collide.cs`
`Collide.SphereVs(o, c, r, out ContactHit)` (`sphereVs`), `Collide.CollideVehicle(veh, course, arena, others[, touched])` (`collideVehicle`). The Game calls them.

### `src/sim/autopilot.js` → `Sim/Autopilot.cs`
`new Autopilot(game, playerIndex, skill = 1)`. Call `Update(dt)` before every `game.Step`. It writes the player's
`veh.input` and calls `game.Action`. Fields: `plan` (`List<PlanStep>`), `k` (current step), `t`, `side`.
`Steer(target, hdg, dt, precise)` → `SteerResult {d, speed, eh}`.

### `src/sim/stats.js` → `Sim/Stats.cs`
`Stats.BotStats(BotDesign)` → `BotStatsResult` (`botStats`, cached per id): `surge, sway, heave` (m/s top speed),
`yawRate, rollRate` (deg/s), `righting` (N·m/rad), `thrust` (N), `mass` (kg), `thrusters`, `dof`
(`List<string>`: "Surge", "Sway", "Heave", "Roll", "Pitch", "Yaw"), `buoyancy` (`OrderedMap` scenario id → net N in that water).

### `src/physics/environment.js` → `Physics/Environment.cs`, `src/hydro/water.js` → `Hydro/Water.cs`
| C# | JS | notes |
|---|---|---|
| `Environment(EnvConfig, RNG)`, `Configure(cfg)`, `Step(dt)` | same | the Game steps it |
| `env.rho` (kg/m³ at the surface), `nu`, `g`, `pAtm` (Pa), `time`, `cfg`, `floor` (`FloorProfile`), `gust` | same | |
| `SurfaceElevation(x, y[, t])` (m, **up**), `FluidVelocity(x, y, z[, t])`, `FluidAccel(...)` (V3 NED), `PressureAt`, `AbsolutePressureAt` (Pa), `PressureGauge(depth)`, `DensityAtDepth(depth)`, `FloorDepth(x)`, `FloorSlope(x)`, `WaveProfile`, `WaveNumber` | same | render: water surface height = `SurfaceElevation`. Particles drift with `FluidVelocity` |
| `EnvConfig` (+ `PoolConfig`, `TurbulenceConfig`, `WavesConfig`, `ContactConfig`), `Environment.DEFAULT_ENVIRONMENT`, `DEFAULT_POOL` | same | a new config holds the JS defaults. `Clone()` |
| `Water.WaterDensity(tempC, salinity)` | `waterDensity` | kg/m³ (garage "buoyancy in each water") |
| `Water.WaterViscosity`, `KinematicViscosity`, `Eos80Density*`, `AirPressureAtAltitude`, `EllipsoidAddedMass`, `DragFromProjections` | same | |

### `src/core/*` → `Math/*`, `src/alloc/*` → `Alloc/*`
`MathUtil` (math.js: `DEG`, `RAD`, `Clamp`, `WrapPI`, `QFromEuler`, `QToEuler`, `QRotate`, `QToMat`, `Svd`, `Pinv`, …),
`RNG` / `GaussMarkov` (rng.js), `JsMath` (V8 `Math`), `Js` (JS semantics helpers), `CoreRandom` (`Math.random`),
`Allocation` (allocation.js: `AXES`, `BuildB`, `AnalyzeLayout`, `EffectiveWrench`, `TableQuality`, …),
`Frames` (frames.js: `FACTOR_AXES`, `FRAMES`, `HeavyLayout`, `VectoredLayout`, `FrameLayout`, …).
