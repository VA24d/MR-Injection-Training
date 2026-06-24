# MR Injection Training

Unity mixed-reality injection-technique trainer for standalone headsets (Meta Quest 3). It runs **markerless**, with no haptic device, instrumented prop, or external tracker: the syringe is reconstructed from **articulated hand tracking**, and the app overlays **real-time 3D geometric guidance** on a learner-placed injection site (a per-injection-type valid-angle cone plus colour-coded depth zones), then scores each attempt on angle, depth, speed, flow, and removal stability.

To keep hand-tracked angle/depth usable while the gripping hand occludes the needle, the trainer uses a **center-snap** pose model that anchors the needle entry to the placed target and derives angle from a long, stable lever. Earlier marker-based detectors (ArUco and a custom checkerboard/band tracker) remain in the source but are **dormant** — see the tracking notes below.

## How it works (pipeline)

`Hand tracking → markerless syringe pose (sign anchor + center-snap) → per-type profile + angle/depth/speed metrics → 3D guidance overlay (valid-angle cone + depth zones) → scored, step-by-step coaching workflow`.

## Requirements

- **Unity Editor** `6000.3.9f1` (Unity 6), matching `ProjectSettings/ProjectVersion.txt`
- **Target devices**: Meta Quest / OpenXR-capable headset (project uses `com.unity.xr.meta-openxr`, Meta XR SDK, XR Interaction Toolkit, XR Hands)
- **Platform**: Configure Android build settings in Unity for device deployment as needed for your lab hardware

## Opening the project

1. Install Unity Hub and add editor version **6000.3.9f1**
2. Open this folder as a Unity project
3. Main scene: `Assets/Scenes/SampleScene.unity`

## Notable packages

| Area | Package |
|------|---------|
| Rendering | Universal Render Pipeline (`com.unity.render-pipelines.universal`) |
| XR / MR | OpenXR, Meta OpenXR, AR Foundation, XR Management |
| Interaction | XR Interaction Toolkit, XR Hands |
| Meta | Meta XR SDK (`com.meta.xr.sdk.all`) |

Full dependency list: `Packages/manifest.json`.

## Core scripts (custom logic)

Located under `Assets/MRTemplateAssets/Scripts/`:

- `GoalManager.cs` — orchestrates goals, coaching panel, and related systems
- `SyringeCalibrationButtonBridge.cs` — tutorial steps, injection type, scoring (`ScoreBreakdown`)
- `SyringeOverlayTracker.cs`, `SurfaceSelectionTool.cs`, `FloatingCoachingUIGrab.cs`, and related UI/overlay helpers

## Documentation and coursework

- **Phase 3 report** (requirements reflection, UML): `phase3_development/5_report.md`
- **System class diagram (PlantUML)**: `phase3_development/system_class_diagram.puml`
- **Exported diagram**: `phase3_development/system_class_diagram.png`

## Unity project settings

- **Company**: Divijh  
- **Product name** (Player settings): `notsuspiciousapp`

## License and third-party assets

Sample content from Unity (XR Interaction Toolkit, XR Hands, TextMesh Pro, etc.) and NuGet packages under `Assets/Packages/` follow their respective licenses. See package folders and Unity Package Manager for attribution.
