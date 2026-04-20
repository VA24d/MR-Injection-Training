# Phase 3 Development Report — MR Syringe Calibration Prototype
## Team 5
### Members: Divijh and Vijay

**Project:** Mixed-reality syringe calibration and injection training (Unity MR Template, Meta Quest / OpenXR).

---

## a. Requirements, prototype, and final build — what evolved

**Initial requirements** focused on a coherent injection-training flow: choose injection type, calibrate a tracked syringe, place a target surface, run guided steps (fill, bubble check, angle, insertion/flow, removal), and surface a score. The intent was to align coaching, hand interaction, and marker-based pose estimation in one MR experience.

**The prototype** validated core loops early: marker calibration taps, surface placement via pinch, floating coaching UI that could follow the head or pin to world space, and step-to-step progression through `SyringeCalibrationButtonBridge` orchestrated by `GoalManager`. Some scope was deliberately narrow so that tracking and UI could be debugged before polishing visuals or secondary feedback.

**The final build** tightened integration between systems: `GoalManager` coordinates the tutorial bridge, `SyringeOverlayTracker`, `SurfaceSelectionTool`, angle overlay, and hand skeleton visibility. Scoring (`ScoreBreakdown`) is wired across steps rather than treated as a late add-on. Compared to the first sketches, the **behavioral contract** between “what the user must do” and “what the software measures” became more explicit; several steps gained clearer preconditions (e.g. valid surface and calibration before angle guidance).

<< Images >>
---

## b. What we learned from prototyping

**Behavioral logic needed iteration** Step order and gating (when advance is allowed, when calibration resets matter) changed once real hand tracking and marker jitter were in the loop. For example, treating “calibration complete” as a hard gate for later steps reduced false positives in angle and insertion scoring.

**MR-specific assumptions broke in practice.** Distance to UI, panel follow versus world-locked layout, and one-handed versus two-handed use all influenced defaults that were not obvious from paper requirements.

---

## c. VerQDV and the design process (vs. VReqDV tooling)

As our system was built using MR, we couldn't use vreqdv to its full potiential. The VerQDV was used to **prototype the injection UI and tutorial surface**: step labels, injection-type selection, coaching copy, scoring presentation, and grab/pin behavior for the floating panel. 
But we feel if MR is integrated into VReqDV, it would have significantly improved our intial protyping speed.

---

## d. Challenges encountered

1. **UI** — Panel placement without blocking the workspace, and consistent affordances across steps. Balancing head-follow comfort against world-pinned precision for fine tasks was non-trivial.

2. **Stable hand tracking** — Jitter, occlusion, and brief loss of tracking affected pinch-based surface placement and any logic that assumed continuous joint streams. Calibration and scoring had to tolerate noisy inputs or short dropouts.

3. **Very selective positioning** — Injection angle and placement relative to the user-placed surface demanded tight tolerances in world space. Small errors in surface normal or hand pose amplified into frustrating false negatives; tuning thresholds and feedback copy became a significant part of the work.

---

## e. Ideas not implemented (future work)

These would improve realism and feedback but were **not** built in this phase:

- **Skin texture for the surface** — The placed injection plane could use a procedural or photographic skin-like material (normal/specular variation) to improve depth cues and training fidelity without changing the core mesh logic.

- **Audio feedback** — Short cues for step success, calibration lock-in, threshold crossings (angle in range), and errors would reduce reliance on visual scan of the coaching panel and help users who look at the physical syringe more than the UI.

---

## f. Demo video

Record a walkthrough of the final VR scene and upload it as an **unlisted** YouTube video. Paste the link below.

**Video:** [Add your YouTube URL here](https://www.youtube.com/watch?v=REPLACE_ME)

---