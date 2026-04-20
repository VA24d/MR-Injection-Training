# MR Template Test

Unity mixed-reality project built on the **MR Template** stack. It implements a **syringe calibration and injection training** flow: marker-based syringe tracking, surface placement, coaching UI, hand overlays, and scored tutorial steps (calibration, fill, bubble check, angle, insertion/flow, removal).

## Requirements

- **Unity Editor** `6000.3.8f1` (Unity 6), matching `ProjectSettings/ProjectVersion.txt`
- **Target devices**: Meta Quest / OpenXR-capable headset (project uses `com.unity.xr.meta-openxr`, Meta XR SDK, XR Interaction Toolkit, XR Hands)
- **Platform**: Configure Android build settings in Unity for device deployment as needed for your lab hardware

## Opening the project

1. Install Unity Hub and add editor version **6000.3.8f1**
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
