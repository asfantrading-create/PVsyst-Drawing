# Architecture

```
PVDesigner.Core (Assets/PVDesigner/Scripts/Core)        no UnityEngine reference, C# 9, unit tested
 ├─ Math          Vec2/Vec3 (X = East, Y = Up, Z = North), polygons, triangulation
 ├─ Solar         NOAA sun position, Perez / Hay-Davies / isotropic transposition, IAM, single axis tracker + backtracking
 ├─ Meteo         MeteoDataset (8760 h), PVGIS TMY parser, synthetic year
 ├─ Components    module / inverter specs, built-in library, PAN / OND reader
 ├─ Model         PVProject (site, fields, tables, obstacles, design, losses ...), UndoStack
 ├─ Layout        table geometry, automatic layout in polygons with setback and keep-out zones
 ├─ Shading       ray traced scene (quads, boxes, cylinders, ellipsoids + uniform grid), shading factor tables
 ├─ Electrical    string / inverter sizing, voltage & current checks, string assignment
 ├─ Simulation    hourly energy model and PVsyst style loss diagram
 ├─ Economics     LCOE, NPV, IRR, payback
 ├─ Reports       HTML report, hourly CSV, bill of materials
 ├─ Updates       semantic versions, GitHub release parsing, checksum helpers
 └─ Serialization JSON project files (*.pvdproj)

PVDesigner.Runtime (Assets/PVDesigner/Scripts/Runtime)  Unity layer
 ├─ App           PVDesignerApp (bootstraps itself on scene load), JobRunner (worker threads), paths & settings
 ├─ SceneView     SceneBuilder (model -> GameObjects, colour modes), SunController (light, sky, sun path)
 ├─ Rendering     procedural meshes (tables, trackers), materials & textures
 ├─ CameraControl orbit / pan / zoom camera
 ├─ Tools         select / move / rotate / delete, boundary drawing, obstacle placement, measuring
 ├─ UI            code-built uGUI (works as screen overlay and as a world-space panel in VR), panels per tab
 ├─ VR            VRManager (OpenXR start/stop at runtime, rig, teleport, grab/scale, time control), laser UI input module
 └─ Updates       AutoUpdater (GitHub releases, SHA-256 verified download, self install on Windows)

PVDesigner.Editor (Assets/PVDesigner/Scripts/Editor)
 ├─ ProjectSetup  scene, player settings, always-included shaders, OpenXR loaders + interaction profiles
 └─ BuildScript   menu and CI build entry point (-buildTarget, -customBuildPath, -buildVersion)
```

## Data flow

* Every edit goes through `PVDesignerApp.Edit(kind, mutate)`: an undo snapshot is recorded, the change is applied,
  the dependent results are invalidated (layout/obstacle changes invalidate the shading table, everything else only
  the simulation) and the scene / UI are refreshed.
* Shading and simulation run on a worker thread on a snapshot of the project, results come back on the main thread.
* The 3D scene is a pure view of the model; the model is the only thing saved to disk.

## Shading model

For each field, sample points are distributed on the tables (rows of points per module row). For every sun direction
of the table (azimuth step 10°, 13 heights) a ray is traced from each point; tables, buildings, walls, poles, tanks and
tree crowns (partially transparent) block it. The result is:

* linear loss = mean shaded fraction,
* electrical loss = fraction of module rows (strings) touched by a shadow, blended with the linear loss by the
  "electrical effect" factor,
* diffuse factor = sky visibility weighted by cos(incidence) (isotropic sky),
* albedo factor = visibility of the ground in front of the modules (ground under tables counts as shaded).

Trackers are re-oriented for each sun direction using the same backtracking law as the simulation.

## Coordinate conventions

* World: X = East, Y = Up, Z = North, metres (identical in Core and Unity).
* Plane azimuth in the UI follows PVsyst: 0° = South, −90° = East, +90° = West.
* Sun azimuth internally is a compass azimuth (0° = North, clockwise).
