# StS2 Orb Layout

A **Slay the Spire 2** mod for the Defect that lets you reshape the orb slot layout into any curve you like — straight line, gentle arc, bold S-curve, whatever. Slots redistribute along the curve automatically as orbs are added or evoked.

> Tired of the default fan? Just hold **Ctrl**, drag the dots, drop, done.

| Editing the curve (Ctrl held) | Result in normal play |
|---|---|
| ![curve editor with waypoint markers](docs/screenshots/editing.png) | ![orbs distributed along the saved curve](docs/screenshots/result.png) |

[한국어 README](README.ko.md)

**Nexus Mods:** https://www.nexusmods.com/slaythespire2/mods/808

---

## Features

- **Free-form Catmull-Rom curve** through user-placed waypoints — straight, arc, S-curve, anything
- **Arc-length distribution** — orbs spread evenly along the curve regardless of how many you have
- **Click-to-add** waypoint UX — click directly on the curve to add a control point right where you want it
- **Auto-resize** when capacity grows or shrinks — the curve stays the same, slots just redistribute
- **Persistent across game restarts** — your curve is saved to disk and reapplied on the next combat
- **Affects only the local Defect player** — purely visual, doesn't touch combat logic
- The manifest declares `"affects_gameplay": false` — safe to leave installed during multiplayer

## Controls

All shortcuts work in combat while you're playing the Defect (or any character with orb slots).

| Input | Action |
|---|---|
| **Hold `Ctrl`** | Show the curve, waypoint markers, and slot numbers |
| **`Ctrl + LMB` on a waypoint marker** | Drag the waypoint |
| **`Ctrl + LMB` on the curve** (≤18 px) | Add a new waypoint at that spot and start dragging it |
| **`Ctrl + Shift + LMB`** anywhere | Force-add a waypoint at the click position (power-user fallback) |
| **`Ctrl + RMB`** on a waypoint marker | Remove that waypoint (endpoints can't be removed) |

First time you hold `Ctrl` in combat, the mod captures the current orb positions as the initial waypoint set — one waypoint per slot — so you immediately have control points to grab.

## How it works

The mod patches `MegaCrit.Sts2.Core.Nodes.Orbs.NOrbManager.TweenLayout()` with Harmony.

When a saved curve exists:
1. The patch runs a Catmull-Rom spline through your waypoints.
2. It computes orb positions by **arc-length parameterization** — slot *i* of *N* is placed at fractional arc length `i/(N-1)` along the curve.
3. The original tween call is skipped; the orbs are tweened to the curve points instead.

When no curve is saved (or capacity is 0), the patch returns control to the original game logic and you get the default fan layout.

## Where the data lives

Your curve is saved as JSON in:

```
%APPDATA%/Godot/app_userdata/Slay the Spire 2/Sts2OrbLayout/orb_curve.json   (Windows)
~/.local/share/godot/app_userdata/Slay the Spire 2/Sts2OrbLayout/orb_curve.json   (Linux)
```

Delete the file to fall back to the default fan layout. The mod will recreate it the next time you hold `Ctrl` in combat.

## Installation

1. Download the latest `Sts2OrbLayout-vX.Y.Z.zip` from [Nexus Mods](https://www.nexusmods.com/slaythespire2/mods/808) or [GitHub Releases](../../releases).
2. Extract `Sts2OrbLayout.dll` and `Sts2OrbLayout.json` into:
   ```
   <Slay the Spire 2 install>/mods/Sts2OrbLayout/
   ```
3. Launch the game.

## Building from source

Requirements:
- .NET SDK 9.0
- Godot.NET.Sdk 4.5.1 (resolved automatically)
- A local Slay the Spire 2 install (auto-detected via Steam registry / standard paths by `Sts2PathDiscovery.props`)

```sh
dotnet build Sts2OrbLayout.csproj -c Release
```

The build automatically copies `Sts2OrbLayout.dll` and `Sts2OrbLayout.json` into `<sts2>/mods/Sts2OrbLayout/`.

## Notes & limits

- Catmull-Rom is uniform-parameterized. With many tightly-clustered waypoints the curve can wobble — add fewer, more evenly spaced ones for a smoother result.
- Endpoints (waypoint 0 and N-1) cannot be removed; you always need at least 2 to define a curve.
- The mod looks for the **local player's** `NOrbManager` only. The remote player's orb area is ignored.

## Credits

- **MegaCrit** — for Slay the Spire 2.
- **HarmonyX** — runtime patching library used by this mod (bundled with the game; not redistributed here).

## License

[MIT](LICENSE).
