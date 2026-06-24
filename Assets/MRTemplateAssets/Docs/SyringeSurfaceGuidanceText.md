# Syringe Surface Guidance Text

Instructional text rendered on the placed injection surface near the syringe (not UI coaching panels elsewhere in the scene). Source: `SyringePlaneAngleOverlay.cs`. Colours follow `InjectionGuidancePalette` and the paper's fixed colour language.

## Colour scheme

The overlay uses a fixed colour language so the learner can read state at a glance:

| Colour | Role |
|--------|------|
| **Amber** | All guidance **geometry** — entry-angle cone and depth zones (same amber throughout) |
| **Green** | Optimal / in-tolerance state in the **overlay geometry** (cone turns green once θ is within band) and in **on-surface text** (e.g. "Maintain angle") |
| **Yellow** | Warning / corrective **text only** — cues that ask the learner to change something (e.g. "insert deeper", "Close thumb faster") |

Correction arrows and warning-state dots use amber geometry; only in-tolerance dots/arrows use green.

## Angle readout

| Text | Colour | When shown |
|------|--------|------------|
| `{angle}°` (e.g. `45.0°`) | Green in band / yellow out of band | Injection Angle step, and during Insertion when thumb is near/at the injection site |

## Injection Angle step

| Text | Colour | When shown |
|------|--------|------------|
| `Maintain angle` | Green | Syringe angle is within the target range |
| `Maintain angle ({seconds}s)` | Green | In range; angle hold countdown active |
| `Lift syringe higher (target {min}-{max} deg)` | Yellow | Angle is too low |
| `Lower syringe (target {min}-{max} deg)` | Yellow | Angle is too high |

## Insertion step

| Text | Colour | When shown |
|------|--------|------------|
| `Move hand closer / insert` | Yellow | Needle has not reached target insertion depth |
| `Steady hand` | Yellow | Lateral hand movement is too unstable |
| `Hold or start dispensing` | Green | Depth and stability are good |

## Flow Rate step

| Text | Colour | When shown |
|------|--------|------------|
| `Keep syringe steady` | Yellow | Lateral hand movement is too unstable |
| `Close thumb faster ({percent}%)` | Yellow | Flow rate is too slow |
| `Ease thumb pressure ({percent}%)` | Yellow | Flow rate is too fast |
| `Maintain flow ({percent}%)` | Green | Flow rate is within target |

## Insertion Speed + Flow Rate step — insertion phase

| Text | Colour | When shown |
|------|--------|------------|
| `Insert toward center` | Yellow | Needle has not reached target insertion depth |
| `Steady hand` | Yellow | Lateral hand movement is too unstable |
| `Hold or dispense` | Green | Depth and stability are good |

## Insertion Speed + Flow Rate step — dispense phase

| Text | Colour | When shown |
|------|--------|------------|
| `Keep syringe steady` | Yellow | Lateral hand movement is too unstable |
| `Close thumb faster ({percent}%)` | Yellow | Flow rate is too slow |
| `Ease thumb pressure ({percent}%)` | Yellow | Flow rate is too fast |
| `Maintain flow ({percent}%)` | Green | Flow rate is within target |

## Notes

- `{percent}` is plunger travel as a whole number (0–100).
- `{seconds}` is the remaining angle-hold time with one decimal place.
- `{min}` and `{max}` come from the active injection type's target angle range (IM / SC / ID / IV).
- Guidance text labels are lifted slightly above the surface to reduce occlusion of the syringe.
