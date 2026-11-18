# AimLab — Offline Aim Assist Research Simulator

AimLab is a self-contained research sandbox. It contains a simulated top-down FPS arena, a simulated
"process memory" that mirrors the arena state, and an aim assist pipeline that reads that memory,
selects targets, and steers the simulated camera. Every tick is recorded as telemetry. You can export
it to JSON or CSV and use it to study the signals an anti-cheat system could use to tell assisted aim
apart from human aim.

**Scope and limits**
- Everything runs inside one process. `SimulatedProcessMemory` is a managed `byte[]`.
  No code opens, reads, or writes another process.
- The only aim sink is `GameWorld.ApplyView`, the simulated camera.
- Input is read only while the AimLab window has focus. Nothing hooks the keyboard or mouse globally.
- The project has no injection, no anti-cheat bypass, no stealth, and no drivers. It does not interact with any real game.

## Requirements

| What | Version |
|---|---|
| .NET SDK | 10.0 |
| OS for the UI | Windows 10/11 (WPF) |
| OS for libraries + tests | Windows, macOS or Linux |

## Build, test, run

```bash
dotnet build AimLab.slnx
```

```bash
dotnet test AimLab.slnx
```

On Windows, run the UI:

```bash
dotnet run --project src/AimLab.UI
```

The UI project sets `EnableWindowsTargeting`, so the whole solution also builds on macOS and Linux.
The WPF app runs only on Windows.

## Using the UI

- **Left panel:** enable or disable the assist, set FOV, smoothing and acquire threshold, choose target priority (crosshair / distance / lowest health), body region (head / body), visibility check, and the activation key.
- **Centre:** the debug radar. Green is you, with a view line and FOV cone. Red is a visible enemy, a red ring is a hidden enemy, blue is a teammate, grey is dead. The current target has a yellow line; other in-FOV candidates have dashed lines.
- **Arrow keys** turn the camera. **Hold the activation key** to engage the assist. You can also turn off "Require activation key".
- **Right panel:** entity counts (valid / invalid / cached), tick rate and per-stage timings, the current target, and live telemetry. **Export JSON / CSV** writes to `Documents/AimLab/telemetry/`.
- Settings are saved to `Documents/AimLab/settings.json`. Logs (Serilog) go to `Documents/AimLab/logs/`.

## Architecture

```
src/
  AimLab.Core           Enums, ViewAngles, IInputState, IAimSink
  AimLab.Mathematics    AngleMath (pitch/yaw, normalization, smoothing), Aabb line-of-sight
  AimLab.Memory         IMemoryReader / IMemoryWriter, SimulatedProcessMemory, MemoryLayout
  AimLab.Entities       PlayerEntity, MemoryEntityParser (validation), EntityCache, LocalPlayerState
  AimLab.Simulation     GameWorld (bots, walls, damage/respawn, visibility), SimulatedGameProcess (publishes to memory)
  AimLab.Targeting      TargetSelector: team / alive / visibility / FOV filters + priority ranking
  AimLab.AimAssist      AimAssistController: smoothed step toward the target, activation gating
  AimLab.Telemetry      TelemetryRecorder (metrics), TelemetryExporter (JSON / CSV)
  AimLab.Configuration  AimSettings (clamped, INotifyPropertyChanged), SettingsStore (JSON)
  AimLab.Runtime        AimLabEngine (one tick of the pipeline), SimulationHost (64 Hz loop), DI registration
  AimLab.UI             WPF dark-theme configuration + radar
tests/
  AimLab.Tests          xUnit: math, memory, parsing, cache, targeting, aim, telemetry, config, end-to-end
```

Each tick (`AimLabEngine.Tick`) does these steps in order:

1. `SimulatedGameProcess.Step` advances the world and writes it into simulated memory.
2. `MemoryEntityParser` reads the memory. Corrupt slots are counted and skipped. A bad header leaves the previous cache in place.
3. `EntityCache` updates entities and records when each one became visible.
4. `TargetSelector` filters and ranks the candidates.
5. `AimAssistController` computes `current + delta / smoothing`.
6. The engine applies the result with `GameWorld.ApplyView`.
7. `TelemetryRecorder` records the tick.

### Memory layout

| Offset | Field | Type |
|---|---|---|
| 0x00 | magic `AIML` | uint |
| 0x04 | version | int |
| 0x08 | entity list pointer (0x100) | long |
| 0x10 | capacity (≤ 64) | int |
| 0x14 | local player slot | int |
| 0x18 | tick | long |
| 0x20 | time | double |

Each entity slot is 0x40 bytes:

| Offset | Field |
|---|---|
| 0x00 | id (0 = empty) |
| 0x04 | team |
| 0x08 | health |
| 0x0C | flags (Alive, Visible) |
| 0x10 | origin |
| 0x1C | head |
| 0x28 | body |
| 0x34 | pitch |
| 0x38 | yaw |

### Conventions

- The world is Z-up.
- Yaw is measured from +X toward +Y and normalized to (-180, 180].
- Pitch is positive when looking down and clamped to ±89°.
- FOV is the full cone angle. A target qualifies when its angular distance from the crosshair is at most FOV / 2.

## Telemetry definitions

| Metric | Definition |
|---|---|
| Angular velocity | Angular distance between consecutive view samples divided by dt (°/s) |
| Angular acceleration | Change in angular velocity divided by dt (°/s²) |
| Target acquisition time | From a target first being selected to the error dropping under the acquire threshold |
| Reaction time | From the target becoming visible (cache visibility onset) to the first applied aim movement |
| Target switching frequency | Selected-target changes between two different entities, per second |
| Tracking accuracy | Share of aim-applied ticks after acquisition whose error is under the threshold |
| Aim trajectory | Per-tick pitch/yaw, target, error and the derived values above (JSON `Trajectory`, CSV rows) |

CSV columns: `time,tick,pitch,yaw,target_id,angular_error,aim_applied,angular_velocity,angular_acceleration`.

## Research ideas

- Compare low smoothing (snaps with very high peak velocity and acceleration) against high smoothing (geometric decay curves).
- Sub-100 ms reaction times combined with a constant-ratio error decay are strong signals of automated aim.
- To get a human baseline, turn off "Enabled" and track targets with the arrow keys. Then compare the two CSVs.
