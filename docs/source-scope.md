# Public source scope — V1

This repository publishes the **DesktopChao application layer** and the **desktop-specific simulation** for transparency and inspection. The full ChaoWorldHD game-engine source is deliberately not included.

## Included

| Location | What you can inspect |
| --- | --- |
| `src/ChaoGarden.DesktopChao` | Startup, transparent windows, context menus, input, settings, roster management, persistence and resource packaging |
| `src/ChaoGarden.DesktopChao/Migration` | The two SA2 process-reading/import adapters used by the desktop app |
| `reference/DesktopSimulation` | Desktop behavior selection, native animation timing, walking/flying/climbing, food seeking, learned pastimes and saved state |
| `docs/desktop-chao.md` | Controls and simulation behavior, including V1 limits |

The process adapters were copied into the application directory for this snapshot; their original shared-engine namespaces remain unchanged. Desktop simulation files are reference source and are not a separate buildable project in this repository.

## Shared dependencies

The executable also uses ChaoWorldHD's shared Core, Simulation, Content, ModelBuilder, Rendering, Infrastructure and ChaoPresentation libraries, plus FNA and its native graphics libraries. Those implementations are outside this public snapshot. The project file therefore retains unresolved shared-project references here: **this repository cannot build the complete application by itself**.

The rendering pipeline, common game rules, general engine types and full game history have not been published. No runtime model/texture archive, published executable, personal Chao saves, build outputs or credentials are included in this source snapshot. Packaged distributions carry their own `DesktopChao.dat` and third-party software notices.

## Useful starting points

- `Program.cs`: startup, command-line options, single-instance handling and resource/save locations.
- `PetApplicationContext.cs`, `PetForm.cs`: roster, menu actions, input and simulation updates.
- `DesktopWindowSurfaces.cs`, `DesktopSurfaceController.cs`: reading exposed window geometry, support, movement and falling.
- `LayeredPetWindow.cs`: the application's transparent desktop window.
- `PetSaveStore.cs`, `PetRosterStore.cs`, `DesktopSaveMigration.cs`: local data, backups and legacy migration.
- `DesktopResourcePackage.cs`: resource archive selection, validation and cache extraction.
- `DesktopChaoImport.cs`, `Sa2DesktopSync.cs`, `Migration/SA2ProcessReader.cs`: file/process import and optional read-only SA2 synchronization.
- `reference/DesktopSimulation/DesktopChaoSimulation.cs` and `DesktopPastimeSystem.cs`: ordinary behavior and learned activities.

These application filenames are relative to `src/ChaoGarden.DesktopChao`, unless a full path is shown. Original game assets and names are covered by [the disclaimer](../disclaimer.md); this publication does not grant rights to them.
