# DesktopChao application and simulation guide

DesktopChao V1 is a Windows desktop companion originally developed in the ChaoWorldHD solution. It renders the original Sonic Adventure 2 / Battle Chao models, palettes, animations, animal parts and accessories in transparent windows. Multiple Chao share one graphics device, but each has its own saved identity, needs, animation state and random stream.

This guide describes the implemented application. It reuses ChaoWorldHD game systems where practical; desktop navigation and quiet idle behavior are adaptations, not a complete running Chao Garden.

## Launching

For a packaged build, run `ChaoGarden.DesktopChao.exe` with `DesktopChao.dat` beside it. Portable builds include .NET; framework-dependent builds require the .NET 10 Desktop Runtime for Windows x64. Use `--data-dir <folder>` for a separate roster and `--resources <folder-or-dat>` to select another resource set. Only one instance may own a data directory.

This repository is a partial source snapshot. Shared engine libraries and build tooling are outside its scope; see [Public source scope](source-scope.md).

The first run creates a visible neutral child with zero Run progress, a default 240-pixel rendering surface and a 90-second initial standing session. Cursor following and pausing while minimized start enabled; always-on-top starts disabled. Subsequent launches restore the saved roster and resume its simulation.

## Controls and context menu

Left-click a Chao to pet it. Drag it to pick it up, move it between monitors or place it above a window; releasing the mouse drops it. Transparent pixels pass clicks through to applications beneath it.

Right-click a Chao for these sections:

| Section | Actions |
| --- | --- |
| This Chao | Rename, hide, minimize to the taskbar, delete |
| Chao | Manage individual visibility and names; add a neutral child; import a Chao; configure SA2 synchronization |
| Appearance | Choose Chao type, animal parts and accessories |
| Spawn fruit | Spawn available foods by catalog category; clear uneaten fruit |
| Settings | Always on top, follow cursor, pause while minimized, size for all Chao, monitor |
| Bottom actions | Open this Chao's save folder; quit the application |

The shared tray menu provides roster management, fruit spawning, global size, the roster save folder and quit. Double-click the tray icon to show all Chao. Closing an individual Chao window hides it; it does not quit the roster. A hidden Chao keeps simulating but is not rendered and cannot acquire loose food. Minimizing can pause that Chao according to its setting. Quit saves and closes the entire application.

Size choices apply to every Chao, including hidden ones and future additions/imports; loose fruit scales with that shared size. The size is the transparent rendering surface width, not the exact visible body width. Monitor and topmost preferences are individual. Fruit is topmost when a visible Chao uses always-on-top.

Type changes preserve identity, stats and attachments while selecting the chosen body's base alignment/morph. Adding animal parts does not award animal abilities or change alignment. Delete removes a Chao from the active roster and keeps a recovery copy. Deleting the last Chao leaves the tray available so another can be added or imported.

## How the simulation runs

### Fixed time and independent Chao

Each Chao advances in fixed 1/60-second steps. The UI host accumulates elapsed time, performs whole simulation ticks, then renders. Rendering targets 60 fps separately. A delayed callback contributes at most 0.25 seconds to the accumulator, so a long stall does not create unlimited catch-up work. There is no offline progression while the application is closed.

Every Chao saves its own random seed and generation count. The engine's MSVC-style random generator supplies reproducible decisions after a save/reload. Adding or hiding another Chao does not consume that Chao's random stream, although shared fruit availability and changing desktop surfaces can affect behavior.

The desktop invokes the engine's byte-emotion and need-drive service functions every 96 simulation ticks (1.6 seconds). Those functions retain their own saved counters and rules; every service does not imply every need changes. Behavioral-state refresh occurs every 7,680 ticks (128 seconds). This preserves the per-Chao service cadence without reproducing SA2's shared 32-slot scheduler.

### Quiet activity selection

Standing, strolling and sitting dominate. Typical sessions are approximately 45–120 seconds standing, 35–80 seconds strolling and 60–180 seconds seated. Sitting mainly uses the rocking idle. Its occasional look-around or head-scratch breaks occur after roughly 25–50 seconds, followed by seated idle. Eligible ambient behaviors can replace some of those breaks.

After checking for an eligible learned/toy activity, an ordinary standing decision boundary rolls a value from 0 through 99:

| Roll | Choice |
| --- | --- |
| 0–3 | Sleep |
| 4–39 | Sit |
| 40–47 | Fly if eligible; otherwise stroll |
| 48–67 | Stroll |
| 68–74 | Try an eligible ambient behavior, otherwise continue standing |
| 75–99 | Continue standing |

Sleep pressure above 8,000 overrides that choice when the selector runs. It does not instantly cancel every ongoing activity. Interactions, transitions, food seeking and movement have their own completion rules.

Ambient selection uses engine mood, health, personality and animal-behavior gates. Learned activities and owned toys have the separate scheduler below. Merely equipping a cosmetic animal part does not teach its behavior. The catalog contains 279 clips and 622 native motion profiles; context-dependent water, partner and competition clips are not played arbitrarily on the desktop.

### Learned activities and toys

At the end of a standing idle or seated rocking interval, a rested, non-hungry Chao has a 35% chance to start an eligible pastime. This check requires sleep pressure and hunger below 8,000, no meal in progress, and ground/window support. Completing or interrupting one adds a saved 90�180 second cooldown; consecutive repeats are avoided when another eligible activity exists. Ordinary standing, wandering, sitting and sleep remain the majority of the simulation. These timings and selection probabilities are desktop policy, while the motion ranges, speeds and chains come from the native animation table.

| Activity | Required saved skill or toy |
| --- | --- |
| Sing, with animated mouth and alternating phrases | Any Song1�Song5 classroom skill |
| Go-go, shake, step and spin dancing | Matching GoGo, Shake, Step or Spin classroom skill |
| Standing exercise and floor sit-ups | Exercise classroom skill |
| Draw with crayon and paper | Any Drawing1�Drawing5 classroom skill |
| Bell, castanets, cymbals, drum, flute, maracas, trumpet, tambourine | Matching classroom instrument skill |
| Rattle, toy car, picture book, bubbles, sweeping, watering can | Rattle, Car, PictureBook, BubbleWand, Broomstick or WateringCan toy flag |

These 22 activities play for approximately 15�25 seconds (drawing 20�30), finishing at a native phrase boundary. They sit or stand before performing, use successive phrases/gestures without continually replaying setup, and recover from floor activities through the game's get-up sequence. Toys and instruments appear during their relevant motion. Petting, feeding, pickup and falling interrupt appropriately; high hunger/sleep pressure or loss of an imported skill ends the activity at the next phrase boundary. Singing and instruments are visual only: no audio dependency is added. Watering and sweeping are toy performances, without garden terrain effects.

Classroom and toy flags come from the saved Chao, including `.chao` and SA2 imports/sync. A fresh neutral child has none; the desktop does not automatically teach skills or grant toys. Cosmetic animal parts do not unlock them. No animation picker is added. Animal behavior animations continue to use their existing learned-animal gates. Unsupported toy flags (such as shovel/pogo/doll) and contextual partner/water/race activities do not start without their game behavior/prop support.

The `pastime` object in each `chao.json` saves its ID, posture phase, duration, phrase step, cooldown and previous choice alongside the native motion clock and independent RNG. Old saves without that field start inactive; restart resumes an active activity deterministically. Each Chao's selection remains independent of all others.

### Default emote shape

The desktop resolves the persistent icon from the Chao type on every draw. Hero adults and Hero Chaos use a halo; dark adults and Dark Chaos use a spike ball; children, neutral adults, Neutral Chaos and the character Chao use a round ball. Palette tint remains independent from the shape. Petting temporarily shows a heart and then restores the type's default, including after appearance changes or switching between Chao. A saved hidden orb is respected without hiding an explicit care reaction.

### Walking, crawling and turning

Movement uses the engine's acceleration, floor response and animation playback-rate calculations. Gaits use the saved **Run progress-point counter**, not the visible stat level:

| Run progress points | Child | Adult, Chaos or character Chao |
| --- | --- | --- |
| 0–49 | Crawl | Walk |
| 50–666 | Walk | Walk |
| 667–1,333 | Run | Run |
| 1,334 and above, in ordinary stat ranges | Fast run | Fast run |

Crawling is child-only. An adult restored from an old crawling save, changed from a child, or synchronized from SA2 switches out of the child gait on its next simulation tick. Existing queued interactions retain their completion path.

At a screen edge, a Chao stops moving while turning on the spot, then resumes in the opposite direction. Crawlers stay in their crawling posture; turning does not replay sit-down or wake-up motions. Upright Chao use the native walking turn. Qualified Chao may choose climbing instead. Native gait selection retains unsigned arithmetic for unusually large imported stat values.

### Flight, climbing, drops and window ledges

Flight becomes eligible at 134 Fly progress points. Ordinary idle selection can choose flight. When leaving a window ledge, an eligible Chao has the native approximately 40% flight choice; otherwise it falls. The desktop uses the engine's wing-assisted falling and flight updates, with screen boundaries replacing garden geometry.

Screen-edge climbing requires 50 Power progress points. This is a desktop policy; there is no separate Climb stat in this tool. A qualified moving Chao has a 35% climb choice at a screen edge. Climbing uses the native climbing motion/update, then flight or falling on completion. Window side walls are not climbable.

Dragging uses the native held pose. A mouse drop starts with zero velocity. A falling Chao can land on an exposed window-top segment wide enough to support its body. It can walk along that segment, follow a moved window and occasionally leave the edge. A 35% departure choice applies when its movement reaches a supported ledge edge. Ordinary landing returns to standing or a queued interaction, without forcing the unrelated trip/get-up animation.

Window translation carries a supported Chao sideways and upward by the window�s pixel displacement. Resizing preserves its horizontal screen position rather than scaling it with the window width; shrinking an edge out from under its body causes a fall. Moving the top downward leaves it in the air so gravity can land it on the window again. An upward-moving top pushes it upward. Full window bounds are tracked separately from exposed segments, so another window uncovering or covering an edge cannot drag the Chao sideways.

Hidden, minimized, cloaked and covered windows are excluded as supports; the app's own windows are excluded too. Geometry is refreshed roughly every 100 ms. Closing or losing a support causes a fall. Window handles are not persisted: a Chao saved on a ledge resumes at its desktop position and resolves available support again.

### Sleeping and petting

Sleep follows native motion links: lie down, settle onto the back, sleep on the back, roll onto the belly, sleep on the belly, roll back, then eventually wake into a seated pose. Back/belly sleep phases typically last 35–60 seconds; the normal sequence completes two roll cycles before waking. The desktop adapter reduces sleep pressure/depth and fatigue during sleep, and fatigue during sitting.

Accepted petting gives one unit of raising-quality care and plays the happy head-shake/heart response for about three seconds. That clip loops forwards. Petting is ignored during eating, sleep or an already pending petting response. A seated/crawling or airborne Chao uses the appropriate recovery before the response.

### Food and eating

Spawn fruit places a full four-portion food on the desktop floor near the menu's opening position. Drag fruit horizontally or vertically, including to another monitor. Release it above an exposed window top to land there, or directly on the edge to place it. Fruit follows its supporting window and falls onto the next available surface if that window is closed, hidden, minimized or covered. Right-click fruit to remove it. Up to 40 loose foods can exist. The menu includes the 22 food types with available models; it omits unsupported Grapes and MushroomAlt entries.

A visible, available Chao on the same desktop floor or exposed window-top segment can seek food once hunger exceeds 3,000. It checks for the nearest food within 50 game units once per second, with the check phase derived from its seed. Sitting Chao stand first; movement uses the same age/stat gait rules as ordinary roaming. A pursuit ends if the target disappears or after 1,800 ticks (30 seconds). A Chao on a window only seeks fruit on that same exposed ledge; floor Chao only seek floor fruit. Held or falling fruit is unavailable. Sleeping, held, airborne, hidden or otherwise occupied Chao wait until available.

The first Chao to arrive acquires the item. Standing pickup uses the native pickup chain, followed by sitting and the eating introduction; a seated or recovering crawler enters through its appropriate transition. Eating cycles through slow chewing, fast chewing, looking around with the default mouth, and returning to chewing. A portion is consumed after 182 eligible chewing ticks (about 3.03 seconds); introductions, look-arounds and motion links pause that counter. Four portions therefore take longer than 12 seconds in wall time.

Each portion applies the selected food's engine stat contribution, including the final portion's remainder, and the desktop hunger reduction of 2,400. The fruit shrinks as portions are eaten. Finishing produces a satisfied response and seated rest. Self-acquisition is not treated as a direct player handoff for raising-quality care. Feeding does not automatically alter alignment, body palette or evolution.

Chao Fruit uses the complete model exported as `HeartFruit2.glb`; `FruitCore.glb` is the eaten core. Square Fruit uses `SquareFruit.glb`, not the seed. Its desktop prop scale compensates for the extra native 0.8 square-fruit factor so its full-size silhouette remains comparable to other foods. That desktop adjustment applies to loose and held renders and retains normal portion shrinking; garden simulation/display scaling remains unchanged.

## Rendering and appearance

The app uses live 3D by default and exposes no 2D rendering switch. A shared Direct3D 11 device renders Chao through the photobooth's model/material pipeline with 4x MSAA. Appearance changes update the live model without generating sprite sheets. Loose fruit is rendered when created or resized, then presented as a static transparent window.

Horizontal placement is anchored to the model origin at the frame centre, with a fixed native collision radius. Changing head direction or body yaw does not recenter the window from its silhouette. Vertical floor contact comes from base-body geometry; wings, tails and accessories may clip into the taskbar or window surface without lifting the Chao.

Cursor following uses continuously smoothed angles, up to ±30° head yaw and ±20° pitch, for eligible motions. Scripted actions retain their authored head motion. Automatic blinking applies to children, neutral adults, Tails, Amy and Knuckles. Hero adults, dark adults and all Chaos types preserve their special eyes. Explicit expressions remain separate from automatic blinking. Hats and masks use alpha transparency.

The app does not crossfade finished images. Native skeletal motion links interpolate poses where the motion system calls for it. Animation evaluation, skinning and Windows layered-window pixel transfer still require CPU work; GPU rendering does not remove that cost. Heavy system load or many visible Chao can reduce the displayed frame rate.

Manual sprite export remains available through `tools/desktop-chao.ps1 -BakeOnly`, optionally with `-CenterOnly`. This exports the primary saved Chao using the desktop photobooth recipe; it is not a requirement for running the live desktop app. Custom photobooth scenes/props use the separate [photobooth project](../src/ChaoGarden.Photobooth/README.md).

## Persistence and import

Default storage is `%LOCALAPPDATA%\ChaoWorldHD\DesktopChao`:

| Path | Contents |
| --- | --- |
| `roster.json` | Active identities, visibility, global size, SA2 sync preferences/excluded slots and loose fruit |
| `chao.json` | Primary Chao, appearance, stats, settings, activity, motion/physics, food portions, position and RNG |
| `chao/<guid>/chao.json` | Each additional Chao |
| `deleted/<guid>/chao.json` | Recovery copy of a deleted Chao |
| `*.bak` | Previous atomic save |
| `desktop.log` | Startup/render/save errors |

Chao save periodically every 15 seconds, after interactions/settings changes and on quit. Spawning, removal and release save immediately; moving/falling fruit positions are also saved periodically. Fruit height and position survive restart, and support is resolved again against current windows without persisting window handles. The pickup handoff saves the Chao's ownership receipt before removing the loose item from the roster; loading reconciles those receipts to prevent a duplicated fruit after an interrupted save. Failed roster saves are retried. Save validation either recovers a valid backup or reports the problem without silently replacing the Chao.

Old `DesktopPet` rosters migrate to `DesktopChao`, and legacy `pet.json` becomes `chao.json`. Existing new-format saves take priority; original files remain recovery copies. Quit the previous application before migration. To edit or back up a complete roster manually, quit first and copy the entire data folder. Names are display labels, not filenames.

**Import from .chao file** supports standard 2,048-byte and ECW 2,112-byte files through the engine parser. The ECW extension is not interpreted. Invalid files, eggs and empty slots are rejected. Each import creates a new desktop identity and random stream without modifying the source file.

**Import one from SA2** reads one selected occupied garden slot from the running Windows/Steam `sonic2app.exe`. Run DesktopChao as administrator and enter a Chao Garden in SA2. Importing is a copy; it does not enable synchronization.

**SA2 synchronization** is an optional, admin-only, read-only snapshot mirror. It polls 24 save slots every two seconds on a worker. Appearance, morph, stats, skills and personality follow the source; desktop identity, RNG, activity, needs, position and visibility remain local. Local rename/type overrides persist, while other cosmetic edits can be replaced by the next snapshot. Bindings follow slots, including a replacement Chao in a slot. Disconnecting never deletes Chao. Deleted synchronized slots are excluded from automatic re-import. There are no writes to SA2 and no mirroring of its live animation or position.

## Code map

See [Public source scope](source-scope.md) for the application entry points, included simulation files and external dependency boundaries. The complete project has automated simulation/save tests and GPU validation, but its shared engine and validation projects are not included in this snapshot.

## Current limits

The desktop does not implement offline aging, automatic evolution, breeding, death/reincarnation, audio, garden terrain/water, races/karate or interactions between Chao. Feeding's special breeding/evolution effects are not implemented. Stats and learned skills can be imported; there is no dedicated stat editor in the menu. Chao seek fruit on their current surface; they do not navigate between windows to fetch food. Accessory changes are visual and do not teach skills. Window support and per-Chao random streams are desktop adaptations; the full game scheduler and AI are not running behind each pet.

## Packaged releases

Keep the executable and `DesktopChao.dat` together. Packaged builds include `README.md`, `disclaimer.md` and `THIRD-PARTY-NOTICES.txt`. They do not need a full ChaoWorldHD installation. Replacing these files after quitting preserves the separate save folder.
