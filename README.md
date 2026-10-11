# ChaoGardenDesktop — V1

Bring a Chao Garden companion to your Windows desktop.

**ChaoGardenDesktop (DesktopChao) V1** is a free, non-commercial fan project. It renders live 3D Chao using the models, textures, palettes and animations from *Sonic Adventure 2 / Battle*, with a smaller simulation designed for life alongside your everyday applications.

Your Chao can wander along the taskbar, rest, sleep, eat fruit and play. Pick it up, place it on a window, give it animal parts or accessories, and keep several independently simulated Chao on screen at once.

> **V1 — first release.** This is a desktop companion, with a selection of the game's mechanics. See [V1 scope](#v1-scope) for what is currently supported.

## Screenshots

<img width="2013" height="1507" alt="image" src="https://github.com/user-attachments/assets/cea97f5b-fd5e-4427-ae5d-bf9ebd35be84" />

<img width="522" height="300" alt="image" src="https://github.com/user-attachments/assets/37d188ab-0172-430a-b80c-e3602e047794" />

<img width="807" height="267" alt="image" src="https://github.com/user-attachments/assets/e3164e84-55d2-4f2c-8dfa-9f97f624bc17" />

## Features

- **Live 3D rendering:** Direct3D 11 rendering, anti-aliasing and a 60 fps target. Appearance changes update immediately, without regenerating sprite sheets.
- **A living desktop companion:** walking, child crawling, running, sitting, looking around, scratching, petting, eating and multi-stage sleeping/waking sequences use native game animations.
- **Stat-based movement:** Run progress affects movement style; sufficiently developed Fly and Power stats unlock flight and screen-edge climbing.
- **Window platforms:** drag a Chao onto an exposed window top and watch it walk along the ledge or jump off. Moving the window sideways or upward carries the Chao. Resizing preserves its horizontal screen position; shrinking its support away or moving the window downward lets it fall and land again.
- **Fruit and feeding:** spawn different fruit, drag it around the desktop or onto a window, and let hungry Chao approach and eat it. Portions shrink with each bite, and feeding affects needs and stats.
- **Learned activities:** singing, dancing, exercise, sit-ups, drawing, eight instruments and six toy routines occur naturally when a Chao has the corresponding saved skill or toy. Quiet resting and wandering remain the main activities.
- **Custom appearance:** change Chao type, animal parts, hats and other supported accessories from the context menu. Cosmetic parts do not automatically teach animal behaviors or classroom skills.
- **Multiple Chao:** each has its own identity, saved state and random simulation seed. Add, rename, hide, show or delete individuals; display size applies to the whole roster.
- **Type-correct emotes:** hero adults and Hero Chaos use a halo; dark adults and Dark Chaos use a spike ball. Neutral adults and Neutral Chaos retain a round ball, with the game palette applied.
- **Cursor following:** idle Chao can turn their heads toward the mouse cursor.
- **Import and sync:** import a `.chao` file, import one Chao from a running SA2 garden, or optionally mirror the game's saved garden roster.
- **Persistent saves:** the roster, appearance, needs, stats, activity and loose fruit are saved locally and restored when the application restarts.

## Getting started

### Requirements

- A Windows x64 PC with a Direct3D 11-capable graphics device.
- Requires the **.NET 10 Desktop Runtime for Windows x64** available here: https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-10.0.12-windows-x64-installer.

### Run a packaged build

1. Extract the release into a folder.
2. Keep `ChaoGarden.DesktopChao.exe` and `DesktopChao.dat` together.
3. Run `ChaoGarden.DesktopChao.exe`.
4. Right-click a Chao or its tray icon to open the controls.

On first run, a basic neutral child Chao appears. Later runs load your existing roster. If the Chao was hidden previously, use its tray menu or double-click the tray icon to show it again.
Note: It may take up to 30 seconds or so to start up and load necessary components.

The app runs independently of SA2. SA2 only needs to be running when using process import or synchronization.
Chao data is stored separate from SA2 - this app does not edit your existing SA2 chao data in any way, shape or form. Only reads it.

## Controls

| Action | What it does |
| --- | --- |
| Left-click a Chao | Pet it |
| Drag a Chao | Pick it up and move it; release to drop it |
| Right-click a Chao or the tray icon | Open the context menu |
| **Chao → Add chao** | Add a new neutral child |
| **This Chao** / roster controls | Rename, show, hide or delete an individual |
| **Appearance** | Change type, animal parts and supported accessories |
| **Spawn fruit** | Place food on the desktop for Chao to find |
| Drag fruit | Move it around the desktop, across monitors or onto a window |
| Right-click fruit | Remove it |
| **Settings** | Change cursor following, always-on-top, monitor and size for all Chao |
| **Minimize to taskbar** | Put the Chao out of the way; restore it from its taskbar icon |
| Double-click the tray icon | Show the roster |
| **Open Save Folder** | Open the application's local data folder |
| **Quit** | Save the roster and exit |

Transparent parts of a Chao's window let clicks pass through. Always-on-top is optional. Closing an individual Chao window hides it; the tray application stays running. Hidden Chao continue simulating, while the **Pause while minimized** setting controls minimized ones.

## How the simulation works

Each Chao updates independently at **60 simulation steps per second**. Long standing, wandering and seated sessions are mixed with occasional gestures, sleep and learned activities. The engine's need counters, stat rules, animation profiles and deterministic random algorithm provide the foundation, with desktop-specific scheduling and window physics.

Hungry Chao seek fruit on the same reachable surface. Sleeping Chao use separate lying-down, back/belly sleep, rollover and waking animations. Feeding uses pickup, sitting, slow/fast eating and looking-around phases. Imported classroom skills and owned-toy flags make additional activities eligible; a newly created child has no learned skills or toys.

Every Chao saves its simulation state and random stream, so restarting resumes that individual. There is no progression while the application is closed. This is a desktop adaptation, not the complete SA2 garden AI or navigation system.

For detailed timings, stat thresholds and behavior rules, see the [application and simulation guide](https://github.com/ChaosHillZone/ChaoGardenDesktop/blob/main/docs/desktop-chao.md).

## Importing Chao from SA2

- **Import from .chao file…** reads one Chao from a standard 2048-byte SA2 file or an ECW 2112-byte file. The ECW extension is not interpreted. Importing creates an independent desktop copy.
- **Import one from SA2…** lets you select one occupied garden slot from the running Windows/Steam `sonic2app.exe` process.
- **Mirror Chao from SA2 (read only)** periodically mirrors the game's saved garden slots, including appearance, stats and learned skills. Desktop identity, random state, position, activity and needs remain local.

For process import or synchronization, run DesktopChao **as administrator** and enter a Chao Garden in SA2 first. File import and ordinary desktop use do not need administrator permissions. The application does not write to SA2 and does not mirror live in-game movement or animation.

## Saves and updates

Your data is stored in:

```text
%LOCALAPPDATA%\ChaoWorldHD\DesktopChao
```

- `roster.json` stores the roster, visibility, shared size and loose fruit.
- `chao.json` stores the primary Chao; additional Chao live under `chao/<id>/chao.json`.
- `.bak` files retain previous atomic saves. Deleted Chao retain a recovery copy under `deleted/`.
- `desktop.log` records application errors.
- `assets/` is a validated cache extracted from `DesktopChao.dat` and shared by the roster.

To update, quit the application and replace the release files. Your save folder is separate and remains intact. Back up that folder if you want to keep a manual copy. Legacy DesktopPet saves are migrated automatically when applicable.

## V1 scope

V1 focuses on desktop interaction, live appearance changes and a persistent lightweight simulation. It currently has no audio, offline aging, automatic evolution, breeding, death/reincarnation, races, karate or Chao-to-Chao interactions. Special breeding/evolution fruit effects are not implemented. Chao seek food on their current surface; they do not navigate between windows to fetch it.

Skills and stats can be imported, but there is no classroom, automatic skill teaching or dedicated stat editor. Window tops must be exposed and wide enough to support the body; hidden, minimized and covered windows cannot provide a platform. Rendering targets 60 fps, but several visible Chao or a heavily loaded system may reduce performance.

## Source code and transparency

This repository contains the DesktopChao application source, its desktop-specific simulation code and the SA2 import adapters, so the desktop behavior and interactions can be inspected. **The full ChaoWorldHD game-engine source is not published here.**

This is a **partial source snapshot**, not a standalone buildable checkout. Shared rendering, model loading, engine data types and common simulation libraries remain external dependencies. Runtime game assets, executables and personal saves are not included in the source repository.

See [Public source scope](https://github.com/ChaosHillZone/ChaoGardenDesktop/blob/main/docs/source-scope.md) for the included files, useful review starting points and dependency boundaries.

## Credits and disclaimer

Created by **ChaosHillZone** as a non-commercial fan project. No financial gain is made from this project.

Chao and the original models, textures, animations and associated intellectual property belong to **SEGA CORPORATION / Sonic Team** and their respective rights holders. ChaosHillZone and ChaoGardenDesktop are not affiliated with, endorsed by or sponsored by SEGA CORPORATION or Sonic Team.

Read [disclaimer.md](disclaimer.md) for the full notice. Third-party software retains its own licenses; see `THIRD-PARTY-NOTICES.txt` in packaged releases and the respective upstream library licenses.
