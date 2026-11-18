# AimLab — Offline CS2-style Aim Trainer and Analysis Simulator

AimLab is a standalone, offline aim trainer with a built-in analysis lab. The drills are modeled on
Counter-Strike 2 practice: flick, tracking and target switch. Every tick and every shot is recorded,
and you can export the data to compare manual sessions with assisted ones.

## Scope

- The app runs entirely inside its own process. It has no network access and no connection to CS2 or any other process.
- `SimulatedProcessMemory` is a managed `byte[]` that mirrors the simulated world. No code opens, reads or writes another process.
- The app never injects code, hooks input globally, moves the OS mouse or bypasses anti-cheat.
- Mouse input comes from Windows Raw Input registered for the AimLab window only, and only while that window is in the foreground. During mouse look the cursor is confined to the view with `ClipCursor` and released on Esc or focus loss. The app never moves the cursor itself.
- The research aim assist moves only the **simulated** camera (`GameWorld.ApplyView`).

## Simulation, not CS2

The following mechanics are simulation choices. They are not CS2 behavior. All numbers are configurable and none was verified against CS2.

| Mechanic | Simulation behaviour |
|---|---|
| Hit volumes | One head sphere and one body sphere per target (`HeadRadius`, `BodyRadius`, in simulation units) |
| Damage | Any hit eliminates the target in Flick / Target switch / Arena. Tracking targets are never eliminated. |
| Weapon | Hitscan along the view direction. No recoil, spread, movement inaccuracy or first-shot accuracy model. `FireIntervalSeconds` is a plain rate limit. |
| Shot timing | A shot uses the view at the start of the tick: after manual input, before the assist step. |
| Units | Simulation units. Eye height 64 and body height 38 are simulation defaults. |
| Sensitivity | `MouseSensitivity × YawDegreesPerCount` per raw count. The default 0.022°/count is a Source-style value. Check it against your own setup before comparing with CS2. |
| Camera | Horizontal FOV, configurable (default 90°). The renderer is a simple WPF 3D scene, not CS2 rendering. |
| Tick | Fixed 64 Hz simulation step, with no sub-tick or interpolation model |

The UI repeats this notice at the bottom of the window.

## Requirements

| What | Version |
|---|---|
| .NET SDK | 10.0 |
| Run the UI | Windows 10/11 (WPF, Raw Input) |
| Build everything and run the tests | Windows, macOS or Linux |

## Build, test, run

```bash
dotnet build AimLab.slnx
```

```bash
dotnet test AimLab.slnx
```

On Windows:

```bash
dotnet run --project src/AimLab.UI
```

## Training flow

1. **Pick a scenario** in the left panel.
   - **Mode:** Flick, Tracking, Target switch, or Arena (the original wandering-bot arena).
   - **Visibility:** AlwaysVisible (no walls), WallOcclusion (arena walls block sight and shots), or Peek (targets alternate between exposed and hidden on a timer).
   - **Seed:** the same seed with the same scenario values gives the same target spawn sequence and target motion. Spawns do not depend on how well you shoot.
   - **Target speed, head and body radius, min and max distance, spawn yaw and pitch spread, switch target count, peek timings, fire interval, respawn delay.**
2. **Mouse:** set sensitivity, yaw and pitch degrees per count, invert Y and camera FOV.
3. Click **▶ Start timed session**. The world, cache and telemetry reset, and the timer starts. The default length is 60 s.
4. Click the view to capture the mouse. **Left click shoots.** **Esc** releases the mouse. Arrow keys also turn the camera.
5. When the timer ends, the world freezes and a **result summary** appears with these figures:
   - shots, hits, misses, accuracy and headshots
   - when the first hit landed, and the mean time to first hit per target
   - mean hit-to-hit switch time
   - acquisition and reaction times
   - tracking accuracy
   - how often the assist was active

   From the summary you can export JSON or CSV, or click **Run again**.

**Free play** runs the same scenario without a timer. The app opens in free play.

**Radar (debug / analysis)** switches the center view to the top-down radar. It shows walls, all entities, hidden enemies, the analysis target and other candidates, and the FOV drawn as a circular sector. The sector is correct for any FOV from 1° to 180°.

### Comparing manual and assisted sessions

Target analysis always runs. **Assist enabled** decides only whether the camera is moved. To compare:

1. Turn **Assist enabled** off and run a session.
2. Turn it on and run the same seed and scenario again.
3. Export both sessions.

The JSON `Metadata` records the mode, seed, visibility, planned duration, whether the assist was on, and every scenario parameter. `Summary.AssistActiveShare` shows the share of target ticks in which the assist actually moved the view. Widen **Analysis / assist FOV** for manual sessions so the analysis can pick targets you have not turned toward yet.

## Telemetry definitions

Each sample stores two errors, measured against the analysis target's aim point:

- `ErrorBefore`: the error of the view after manual input this tick, before the assist step.
- `ErrorAfter`: the error of the view that is recorded in the sample (`Pitch` / `Yaw`), at the end of the tick.

All target metrics below use `ErrorAfter`. This makes manual and assisted ticks directly comparable. With smoothing 1, the snap is measured as acquired in the same tick (acquisition time 0).

| Metric | Definition |
|---|---|
| Angular velocity | Angular distance between consecutive samples / dt. Null on the first sample. |
| Angular acceleration | Change in velocity / dt. Null until two velocities exist. |
| Acquisition time | From a target being selected to the first tick with `ErrorAfter` ≤ the acquire threshold |
| Reaction time | From the target becoming visible (or selected, if unknown) to the first tick in which the camera moved and `ErrorAfter` dropped below the previous error for that target |
| Tracking accuracy | Share of ticks after acquisition (same target) with `ErrorAfter` ≤ the threshold. It does **not** depend on the assist being active. |
| Target switches | Selected target changes between two different entities, per second of session |
| Shots, hits, misses, headshots | From resolved shot events |
| First hit at | Session time of the first hit |
| Time to first hit | Per target instance: from its first visible moment to its first hit |
| Switch time | From a hit on one target instance to the next hit on a different instance (a respawned target counts as a new instance) |

**Session vs window.** The recorder keeps at most 200,000 samples in memory, about 52 minutes at 64 Hz, and drops the oldest 10% when it is full.

- `Summary.Session` is exact for every sample recorded since the reset.
- `Summary.Window` describes only the retained samples, which are the ones exported.

In each block, the sample count, duration and means come from the same set of samples. Target and shot metrics are always session-wide.

**Atomic export.** JSON export takes a single `TelemetrySnapshot` (metadata, summary, shots and trajectory) under one lock, so the summary always matches the exported samples.

CSV columns: `time,tick,pitch,yaw,target_id,error_before,error_after,aim_applied,angular_velocity,angular_acceleration`. Shot events are only in the JSON export (`Shots`).

Files are saved under `Documents/AimLab/`:

| Path | Content |
|---|---|
| `telemetry/` | Exported JSON and CSV |
| `settings.json`, `training.json` | Settings saved with **Save settings** |
| `logs/` | Serilog logs |

## Architecture

```
src/
  AimLab.Core           Enums (TrainingMode, VisibilityScenario…), ViewAngles, ShotEvent, IInputState, IAimSink
  AimLab.Mathematics    AngleMath, Aabb, GeometryMath.RaySphere, FovSector, MouseLook
  AimLab.Memory         IMemoryReader / IMemoryWriter, SimulatedProcessMemory, MemoryLayout
  AimLab.Entities       PlayerEntity, MemoryEntityParser (validation), EntityCache (+Reset), LocalPlayerState
  AimLab.Simulation     GameWorld (arena + training modes, peek, hitscan, seeded spawns), SimulatedGameProcess (+Reset)
  AimLab.Targeting      TargetSelector: team / alive / visibility / FOV filters + priority ranking
  AimLab.AimAssist      AimAssistController: target analysis always; smoothed step only when enabled + activated
  AimLab.Telemetry      TelemetryRecorder (session/window stats, shots, snapshot), TelemetryExporter, SessionReport
  AimLab.Configuration  AimSettings, TrainingSettings (clamped, INotifyPropertyChanged), SettingsStore
  AimLab.Runtime        AimLabEngine (tick + sessions), ScenarioFactory, SimulationHost (64 Hz), DI registration
  AimLab.UI             WPF: first-person view (WPF 3D), radar, Raw Input mouse look, session panel
tests/
  AimLab.Tests          xUnit
```

Each tick of `AimLabEngine.Tick` runs these steps in order:

1. **Step the world.** The world applies queued manual turns, moves targets, updates visibility and resolves a queued shot.
2. **Publish to simulated memory.**
3. **Parse and cache.** The engine parses the memory and updates the entity cache.
4. **Select a target.**
5. **Run the assist step.** Errors are always computed; the view moves only when the assist is active.
6. **Apply the view to the simulated world.**
7. **Record telemetry.** Shots are recorded first, then the sample.

A timed session freezes once its duration is reached.

The simulated memory layout is unchanged from the previous version: a header at 0x00 and 64 entity slots of 0x40 bytes from 0x100. See `MemoryLayout.cs`.

## Platform limitations

- The WPF UI compiles on macOS and Linux (`EnableWindowsTargeting`) but runs only on Windows.
- The following have not been exercised by automated tests:
  - Raw Input mouse look, cursor clipping and Esc release
  - the WPF 3D first-person view and the radar sector rendering
  - in-app mouse clicks and the result panel
- The sector geometry itself (`FovSector`) and the mouse count conversion (`MouseLook`) are covered by unit tests.
- If Raw Input is unavailable (`RegisterRawInputDevices` fails), mouse look does not engage. Arrow keys still work. Absolute-position devices, such as some remote-desktop or tablet sessions, are ignored by mouse look.
