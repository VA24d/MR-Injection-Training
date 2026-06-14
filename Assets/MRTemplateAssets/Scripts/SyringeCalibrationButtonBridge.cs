namespace UnityEngine.XR.Templates.MR
{
    /// <summary>
    /// Per-category scores that sum to <see cref="ScoreBreakdown.total"/> (before display rounding).
    /// </summary>
    [System.Serializable]
    public struct ScoreBreakdown
    {
        public float calibration;
        public float injectionType;
        public float fill;
        public float bubble;
        public float surfaceClean;
        public float angle;
        public float insertion;
        public float flow;
        public float removal;
        public float total;
    }

    /// <summary>
    /// Disables the MR template wrist menu and runs a stubbed injection tutorial flow.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class SyringeCalibrationButtonBridge : MonoBehaviour
    {
        public enum TutorialStep
        {
            Start = 0,
            Calibration = 1,
            InjectionType = 2,
            FillSyringe = 3,
            BubbleCheckManual = 4,
            InjectionAngle = 5,
            InsertionSpeedFlowRate = 6,
            RemoveSpeed = 7,
            FinalScore = 8,
            /// <summary>Appended at end to preserve serialized enum values for prior steps.</summary>
            CleanSurfaceAlcohol = 9,
            /// <summary>Thumb-to-center insertion at the site (after clean surface + Next).</summary>
            Insertion = 10,
            /// <summary>Thumb-closing flow / dispense rate.</summary>
            FlowRate = 11,
        }

        public static bool IsSiteSnapStep(TutorialStep step) =>
            step == TutorialStep.InjectionAngle ||
            step == TutorialStep.Insertion ||
            step == TutorialStep.FlowRate ||
            step == TutorialStep.RemoveSpeed ||
            step == TutorialStep.InsertionSpeedFlowRate;

        public static bool IsInsertionPipelineStep(TutorialStep step) =>
            step == TutorialStep.Insertion ||
            step == TutorialStep.FlowRate ||
            step == TutorialStep.RemoveSpeed;

        public enum InjectionType
        {
            None,
            Intramuscular,
            Subcutaneous,
            Intradermal,
            Intravenous,
        }

        /// <summary>
        /// How the syringe-vs-surface angle is derived. The needle is assumed to enter at the
        /// target-spot center, which lets us pin one end of the lever to a stable point.
        /// </summary>
        public enum AngleEstimationMode
        {
            /// <summary>Angle of (plunger - snapped spot). Longest, most occlusion-stable lever.</summary>
            LongLeverFromSpot,
            /// <summary>Blend of the long-lever direction with the live needleTip-plunger axis.</summary>
            FuseLeverAndAxis,
            /// <summary>Live needleTip-plunger axis with extra temporal smoothing on the angle.</summary>
            AxisWithSmoothing,
        }

        [Header("Wrist menu removal")]
        [SerializeField]
        [Tooltip("When true, the MR template wrist menu root is disabled at startup (default matches previous always-on behavior).")]
        bool m_DisableWristMenu = true;

        [SerializeField]
        string m_WristMenuRootPath = "UI/Hand Menu Setup MR Template Variant";

        [Header("Tutorial references")]
        [SerializeField]
        SyringeOverlayTracker m_Tracker;

        [SerializeField]
        SurfaceSelectionTool m_SurfaceSelectionTool;

        [SerializeField]
        SyringePlaneAngleOverlay m_PlaneAngleOverlay;

        [SerializeField]
        HandOverlaySkeletonToggleBridge m_HandOverlayBridge;

        [SerializeField]
        [Tooltip("3D injection target guide. Auto-created at runtime if left empty.")]
        InjectionTargetGuide m_TargetGuide;

        [Header("Stub behavior")]
        [SerializeField]
        bool m_AutoAdvanceStub;

        [SerializeField, Min(0f)]
        float m_IntroDelaySeconds = 0.6f;

        [SerializeField, Min(0f)]
        float m_TypeSelectionDelaySeconds = 1.25f;

        [SerializeField]
        InjectionType m_DefaultInjectionType = InjectionType.Intramuscular;

        [SerializeField, Min(0.01f)]
        float m_FillRateNormalizedPerSecond = 0.35f;

        [SerializeField, Range(0f, 1f)]
        float m_TargetFillAmount = 1f;

        [SerializeField, Min(0f)]
        float m_BubbleManualFallbackDelay = 2.5f;

        [SerializeField, Min(0f)]
        float m_CleanSurfaceManualFallbackDelay = 2.5f;

        [SerializeField]
        Vector2 m_DefaultInjectionAngleRange = new Vector2(25f, 55f);

        [SerializeField]
        Vector2 m_TargetInjectionAngleRange = new Vector2(25f, 55f);

        [SerializeField]
        Vector2 m_IntramuscularAngleRange = new Vector2(80f, 95f);

        [SerializeField]
        Vector2 m_SubcutaneousAngleRange = new Vector2(35f, 50f);

        [SerializeField]
        Vector2 m_IntradermalAngleRange = new Vector2(8f, 18f);

        [SerializeField]
        Vector2 m_IntravenousAngleRange = new Vector2(20f, 35f);

        [Header("Per-type insertion depth (cm) — x = green/accurate, y = orange/max")]
        [SerializeField]
        [Tooltip("Intradermal correct (x) and max (y) needle depth in cm. Clamped to needle length 4.7 cm.")]
        Vector2 m_IntradermalDepthCm = new Vector2(0.12f, 0.25f);

        [SerializeField]
        [Tooltip("Subcutaneous correct (x) and max (y) needle depth in cm.")]
        Vector2 m_SubcutaneousDepthCm = new Vector2(0.35f, 0.6f);

        [SerializeField]
        [Tooltip("Intramuscular correct (x) and max (y) needle depth in cm.")]
        Vector2 m_IntramuscularDepthCm = new Vector2(0.7f, 1.1f);

        [SerializeField]
        [Tooltip("Intravenous correct (x) and max (y) needle depth in cm.")]
        Vector2 m_IntravenousDepthCm = new Vector2(0.45f, 0.75f);

        [SerializeField]
        [Tooltip("Fallback correct (x) and max (y) needle depth in cm when no type is selected.")]
        Vector2 m_DefaultDepthCm = new Vector2(0.35f, 0.6f);

        [Header("Angle estimation")]
        [SerializeField]
        [Tooltip("Source signal for the syringe-vs-surface angle. AxisWithSmoothing uses the syringe's own stabilized axis (correct even when the tip is not yet at the spot).")]
        AngleEstimationMode m_AngleEstimationMode = AngleEstimationMode.AxisWithSmoothing;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("FuseLeverAndAxis: weight of the live needleTip-plunger axis vs the long lever (0 = all lever).")]
        float m_AngleFuseAxisWeight = 0.35f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("AxisWithSmoothing: temporal low-pass factor applied to the measured angle (higher = smoother/laggier).")]
        float m_AngleSmoothing = 0.9f;

        [SerializeField, Min(0.001f)]
        [Tooltip("Needle tip must be within this distance (m) of the surface to lock the snapped contact point on first contact.")]
        float m_ContactSnapDistanceMeters = 0.02f;

        [SerializeField, Min(0f)]
        [Tooltip("Lateral radius (m) within which the rendered needle entry hard-locks to the site center. Passed to the tracker each frame.")]
        float m_CenterLockRadius = 0.05f;

        [SerializeField]
        [Tooltip("Absolute lock mode: the needle entry is ALWAYS pinned to the site center (zero lateral offset, no radius). Only insertion distance varies; angle is still measured from the hand. Toggled live from the coaching UI.")]
        bool m_AbsoluteCenterLock = false;

        [SerializeField, Min(0.1f)]
        float m_AngleHoldSeconds = 3f;

        [SerializeField, Min(0.1f)]
        float m_TargetInsertionSpeedCmPerSec = 3.5f;

        [SerializeField, Min(0.1f)]
        float m_TargetFlowRateMlPerSec = 0.6f;

        [SerializeField, Min(0.1f)]
        float m_MinInsertionDepthCm = 0.25f;

        [SerializeField, Min(0.1f)]
        float m_MaxInsertionDepthCm = 1f;

        [SerializeField, Min(0.1f)]
        float m_MaxLateralStabilityCmPerSec = 6f;

        [SerializeField, Min(0.1f)]
        float m_InsertedStabilityHoldSeconds = 0.45f;

        [SerializeField, Min(0.1f)]
        [Tooltip("During insertion, hold hand steady with needle inserted for this long to advance to flow (even if target depth not reached).")]
        float m_InsertionStabilityHoldSeconds = 2f;

        [SerializeField, Min(0.1f)]
        float m_TargetDispensePlungerRateCmPerSec = 1.6f;

        [SerializeField, Min(0.01f)]
        float m_MinPlungerTravelToCompleteCm = 0.8f;

        [SerializeField, Min(0.01f)]
        float m_DispenseStartPlungerRateCmPerSec = 0.12f;

        [SerializeField, Min(0.01f)]
        float m_DispenseStopPlungerRateCmPerSec = 0.08f;

        [SerializeField, Min(0.05f)]
        [Tooltip("Thumb closing toward knuckles must be sustained this long before leaving the insertion step.")]
        float m_ThumbDispenseSustainSeconds = 0.28f;

        [SerializeField, Min(0.05f)]
        [Tooltip("Minimum insertion depth (cm) at the site before dispensing can start the flow step.")]
        float m_MinDepthBeforeDispenseCm = 0.08f;

        [SerializeField, Min(1f)]
        [Tooltip("Thumb must be within this plane distance (cm) from the site before depth tracking starts.")]
        float m_MaxThumbPlaneDistForDepthBaselineCm = 3.5f;

        [SerializeField, Min(0.1f)]
        [Tooltip("Minimum time spent actively dispensing before stopping can advance to removal.")]
        float m_MinDispenseActiveSeconds = 0.35f;

        [SerializeField, Min(0.05f)]
        [Tooltip("Stopped administering must be held this long before advancing to removal.")]
        float m_DispenseStopHoldSeconds = 0.2f;

        [SerializeField, Min(0.01f)]
        [Tooltip("Thumb closing toward knuckles (cm/s) to start the flow/dispense sub-phase.")]
        float m_ThumbTowardKnucklesStartCmPerSec = 0.05f;

        [SerializeField, Min(0.01f)]
        [Tooltip("Thumb closing rate below this (cm/s) counts as stopped administering.")]
        float m_ThumbTowardKnucklesStopCmPerSec = 0.05f;

        [SerializeField, Min(0.1f)]
        [Tooltip("After insertion depth is met, hold at the site this long before auto-advancing to flow.")]
        float m_InsertionAtDepthHoldSeconds = 0.6f;

        [SerializeField, Min(0.05f)]
        [Tooltip("Sustained thumb movement while inserted also advances to flow.")]
        float m_InsertionThumbActivitySeconds = 0.15f;

        [SerializeField, Min(0.1f)]
        [Tooltip("After correct insertion depth is reached, hold at depth with hand tracking for this long to auto-advance to removal.")]
        float m_PostInjectionHoldSeconds = 5f;

        [SerializeField, Min(0.02f)]
        [Tooltip("Depth decrease (cm) from peak after insertion to count as starting withdrawal.")]
        float m_WithdrawalDepthDeltaCm = 0.06f;

        [SerializeField, Min(0.1f)]
        [Tooltip("Axial needle speed outward (cm/s) along the ideal axis to count as withdrawal.")]
        float m_MinWithdrawalAxialSpeedCmPerSec = 0.35f;

        [SerializeField, Min(0.1f)]
        [Tooltip("Horizontal syringe/hand speed away from the injection-site center (cm/s) to count as withdrawal.")]
        float m_MinHandAwayFromCenterCmPerSec = 1.2f;

        [SerializeField, Min(0.1f)]
        float m_TargetRemovalSpeedCmPerSec = 4.5f;

        [SerializeField, Min(0.1f)]
        float m_SpeedHoldSeconds = 0.8f;

        [SerializeField, Min(0.05f)]
        [Tooltip("Max smoothed lateral needle speed (cm/s) during withdrawal to count as stable for the removal checkpoint.")]
        float m_MaxRemovalLateralSpeedCmPerSec = 2.2f;

        [SerializeField, Min(0.1f)]
        [Tooltip("Lateral speed at or above this (cm/s) drives the stability portion of removal score toward zero.")]
        float m_RemovalLateralScoreRefCmPerSec = 5f;

        [SerializeField, Min(0.5f)]
        [Tooltip("How quickly displayed lateral speed follows measured lateral speed (higher = snappier).")]
        float m_RemovalLateralSmoothing = 8f;

        [SerializeField, Min(0.5f)]
        [Tooltip("Low-pass rate for the displayed insertion speed and flow rate (higher = snappier, lower = smoother).")]
        float m_SpeedDisplaySmoothing = 6f;

        [SerializeField, Min(0f)]
        [Tooltip("Depth threshold in cm considered fully removed from the surface during removal step.")]
        float m_RemovalCompleteDepthCm = 0.02f;

        [Header("Runtime state")]
        [SerializeField]
        TutorialStep m_CurrentStep;

        [SerializeField]
        bool m_IsTutorialRunning;

        [SerializeField]
        bool m_IsFinished;

        [SerializeField]
        InjectionType m_SelectedInjectionType = InjectionType.None;

        [SerializeField, Range(0f, 1f)]
        float m_FillAmountNormalized;

        [SerializeField]
        bool m_BubbleCheckCompleted;

        [SerializeField]
        bool m_SurfaceCleanCompleted;

        [SerializeField]
        float m_InjectionAngleDegrees;

        [SerializeField]
        float m_InsertionSpeedCmPerSec;

        [SerializeField]
        float m_FlowRateMlPerSec;

        [SerializeField]
        float m_RemovalSpeedCmPerSec;

        [SerializeField]
        float m_RemovalLateralSpeedCmPerSec;

        [SerializeField]
        float m_RemovalLateralSmoothedCmPerSec;

        [SerializeField, Range(0f, 100f)]
        float m_FinalScore;

        [SerializeField]
        ScoreBreakdown m_LastScoreBreakdown;

        [SerializeField]
        float m_StepElapsedSeconds;

        [SerializeField]
        float m_CurrentInsertionDepthCm;

        [SerializeField]
        float m_EffectiveInsertionDepthTargetCm;

        [SerializeField]
        float m_CurrentLateralStabilityCmPerSec;

        [SerializeField]
        float m_CurrentPlungerPushRateCmPerSec;

        [SerializeField]
        float m_ThumbTowardKnucklesRateCmPerSec;

        [SerializeField]
        float m_PlungerTravelNormalized;

        [SerializeField]
        bool m_HasCompletedInsertionDepth;

        [SerializeField]
        bool m_HasBeenInsertedDuringRemoval;

        [SerializeField]
        bool m_HasStartedDispensePress;

        [SerializeField]
        float m_AngleGuidanceErrorDegrees;

        float m_AngleHoldProgress;
        float m_InsertionHoldProgress;
        float m_RemovalHoldProgress;
        float m_DispenseStopProgress;
        float m_DispenseElapsedSeconds;
        Vector3 m_PreviousNeedleTip;
        Vector3 m_PlungerWorldPrev;
        bool m_HasPreviousNeedleTip;
        float m_PreviousInsertionDepthCm;
        bool m_HasPreviousInsertionDepth;
        float m_InsertionStableHoldProgress;
        float m_InitialPlungerToWingsDistanceCm;
        bool m_HasInitialPlungerDistance;
        float m_InsertionDepthBaselineThumbPlaneCm;
        bool m_HasInsertionDepthBaseline;
        bool m_HasLatchedNearSiteEngagement;
        float m_ThumbDispenseSustainProgress;
        float m_InsertionThumbActivityProgress;
        float m_InsertionAtDepthHoldProgress;
        float m_PostInjectionDepthHoldProgress;
        float m_PeakInsertionDepthCm;
        float m_HandDistanceFromCenterCm;
        float m_PreviousHandDistanceFromCenterCm;
        float m_CurrentAxialNeedleSpeedCmPerSec;
        bool m_HasPreviousHandDistance;

        // Snapped injection geometry (needle assumed to enter at the target-spot center).
        Vector3 m_InjectionContactPoint;
        Vector3 m_LockedContactPoint;
        Vector3 m_IdealInjectionAxis = Vector3.down;
        bool m_HasContactPoint;
        bool m_HasLockedContactPoint;
        bool m_HasSmoothedAngle;
        float m_SmoothedAngleDegrees;

        public TutorialStep currentStep => m_CurrentStep;
        public bool isTutorialRunning => m_IsTutorialRunning;
        public bool isFinished => m_IsFinished;
        public InjectionType selectedInjectionType => m_SelectedInjectionType;
        public float fillAmountNormalized => m_FillAmountNormalized;
        public float finalScore => m_FinalScore;
        public ScoreBreakdown lastScoreBreakdown => m_LastScoreBreakdown;

        /// <summary>When true the needle entry is always pinned to the site center (distance-only mode).</summary>
        public bool absoluteCenterLock => m_AbsoluteCenterLock;

        /// <summary>Flips the lock mode (radius soft-lock &lt;-&gt; absolute center lock). Driven by the coaching UI button.</summary>
        public void ToggleAbsoluteCenterLock() => m_AbsoluteCenterLock = !m_AbsoluteCenterLock;

        public float injectionAngleDegrees => m_InjectionAngleDegrees;
        public float insertionSpeedCmPerSec => m_InsertionSpeedCmPerSec;
        public float flowRateMlPerSec => m_FlowRateMlPerSec;
        public float removalSpeedCmPerSec => m_RemovalSpeedCmPerSec;
        /// <summary>Instantaneous lateral needle speed during withdrawal (cm/s), perpendicular to syringe forward.</summary>
        public float removalLateralSpeedCmPerSec => m_RemovalLateralSpeedCmPerSec;
        /// <summary>Smoothed lateral speed used for stability gate and scoring.</summary>
        public float removalLateralSmoothedCmPerSec => m_RemovalLateralSmoothedCmPerSec;
        public float maxRemovalLateralSpeedCmPerSec => m_MaxRemovalLateralSpeedCmPerSec;
        /// <summary>When false, the coaching Next button is disabled on the removal step.</summary>
        public bool removalCheckpointMet =>
            m_CurrentStep != TutorialStep.RemoveSpeed ||
            m_RemovalHoldProgress >= m_SpeedHoldSeconds ||
            m_IsFinished;
        public bool bubbleCheckCompleted => m_BubbleCheckCompleted;
        public bool surfaceCleanCompleted => m_SurfaceCleanCompleted;

        public float angleHoldProgressNormalized =>
            m_AngleHoldSeconds > 0.01f ? Mathf.Clamp01(m_AngleHoldProgress / m_AngleHoldSeconds) : 0f;

        public float insertionFlowHoldProgressNormalized =>
            m_SpeedHoldSeconds > 0.01f ? Mathf.Clamp01(m_InsertionHoldProgress / m_SpeedHoldSeconds) : 0f;

        public float insertionStabilityHoldProgressNormalized =>
            m_InsertionStabilityHoldSeconds > 0.01f ? Mathf.Clamp01(m_InsertionStableHoldProgress / m_InsertionStabilityHoldSeconds) : 0f;

        public float removalHoldProgressNormalized =>
            m_SpeedHoldSeconds > 0.01f ? Mathf.Clamp01(m_RemovalHoldProgress / m_SpeedHoldSeconds) : 0f;

        /// <summary>Rounded checkpoint progress 0–100 on <see cref="TutorialStep.RemoveSpeed"/>; -1 on other steps (for UI diffing).</summary>
        public int removalCheckpointProgressPercent =>
            m_CurrentStep == TutorialStep.RemoveSpeed
                ? Mathf.Clamp(Mathf.RoundToInt(removalHoldProgressNormalized * 100f), 0, 100)
                : -1;

        public Vector2 targetInjectionAngleRange => GetTargetInjectionAngleRangeForSelectedType();
        public float targetInsertionSpeedCmPerSec => m_TargetInsertionSpeedCmPerSec;
        public float targetFlowRateMlPerSec => m_TargetFlowRateMlPerSec;
        public float targetRemovalSpeedCmPerSec => m_TargetRemovalSpeedCmPerSec;
        public float targetFillAmountNormalized => m_TargetFillAmount;
        public float insertionStabilityHoldSeconds => m_InsertionStabilityHoldSeconds;

        public bool isDispensePhase => m_CurrentStep == TutorialStep.FlowRate;
        public bool hasLatchedNearSiteEngagement => m_HasLatchedNearSiteEngagement;
        public float currentInsertionDepthCm => m_CurrentInsertionDepthCm;
        public float effectiveInsertionDepthTargetCm => m_EffectiveInsertionDepthTargetCm;
        public float currentLateralStabilityCmPerSec => m_CurrentLateralStabilityCmPerSec;
        public float currentPlungerPushRateCmPerSec => m_CurrentPlungerPushRateCmPerSec;
        public float thumbTowardKnucklesRateCmPerSec => m_ThumbTowardKnucklesRateCmPerSec;
        public float plungerTravelNormalized => m_PlungerTravelNormalized;
        public float angleGuidanceErrorDegrees => m_AngleGuidanceErrorDegrees;
        public float angleHoldSecondsRemaining => Mathf.Max(0f, m_AngleHoldSeconds - m_AngleHoldProgress);
        public float minInsertionDepthCm => m_MinInsertionDepthCm;
        public float maxInsertionDepthCm => m_MaxInsertionDepthCm;

        /// <summary>Accurate (green) needle depth in cm for the selected injection type.</summary>
        public float currentInjectionGreenDepthCm => GetDepthRangeForSelectedType().x;
        /// <summary>Max (orange) needle depth in cm for the selected injection type, capped at needle length.</summary>
        public float currentInjectionMaxDepthCm => GetDepthRangeForSelectedType().y;
        /// <summary>Midpoint of the selected type's valid angle range, in degrees from the surface.</summary>
        public float idealInjectionAngleDegrees
        {
            get
            {
                var r = GetTargetInjectionAngleRangeForSelectedType();
                return 0.5f * (r.x + r.y);
            }
        }
        /// <summary>World-space point where the needle is assumed to enter (snapped to the target-spot center).</summary>
        public Vector3 injectionContactPoint => m_InjectionContactPoint;
        /// <summary>Whether a snapped contact point is currently available this frame.</summary>
        public bool hasInjectionContactPoint => m_HasContactPoint;
        /// <summary>Unit direction the needle ideally travels below the skin (nominal type angle).</summary>
        public Vector3 idealInjectionAxis => m_IdealInjectionAxis;
        public bool hasCompletedInsertionDepth => m_HasCompletedInsertionDepth;
        public float maxLateralStabilityCmPerSec => m_MaxLateralStabilityCmPerSec;
        public float targetDispensePlungerRateCmPerSec => m_TargetDispensePlungerRateCmPerSec;

        /// <summary>
        /// Multi-line summary for the final score coaching panel (category points + total).
        /// </summary>
        public string GetScoreBreakdownDisplayString()
        {
            var s = m_LastScoreBreakdown;
            return
                "Calibration: " + s.calibration.ToString("F1") + "\n" +
                "Injection type: " + s.injectionType.ToString("F1") + "\n" +
                "Fill: " + s.fill.ToString("F1") + "\n" +
                "Bubble check: " + s.bubble.ToString("F1") + "\n" +
                "Clean surface: " + s.surfaceClean.ToString("F1") + "\n" +
                "Angle: " + s.angle.ToString("F1") + "\n" +
                "Insertion: " + s.insertion.ToString("F1") + "\n" +
                "Flow rate: " + s.flow.ToString("F1") + "\n" +
                "Removal: " + s.removal.ToString("F1") + "\n" +
                "---\n" +
                "Total: " + s.total.ToString("F1") + " / 100";
        }

        void Start()
        {
            ResolveReferences();

            // Wrist UI is optionally hidden while using the floating coaching UI.
            if (m_DisableWristMenu)
                DisableWristMenu();

            BeginTutorial();
        }

        void Update()
        {
            if (!m_IsTutorialRunning || m_IsFinished)
                return;

            m_StepElapsedSeconds += Time.deltaTime;

#if UNITY_EDITOR
            HandleEditorDebugInput();
#endif
        }

        void LateUpdate()
        {
            if (!m_IsTutorialRunning || m_IsFinished)
                return;

            TickStep();
        }

        public void BeginTutorial()
        {
            // Force requested behavior regardless of legacy serialized scene values.
            m_AngleHoldSeconds = 3f;

            m_IsTutorialRunning = true;
            m_IsFinished = false;
            m_FinalScore = 0f;
            m_SelectedInjectionType = InjectionType.None;
            m_FillAmountNormalized = 0f;
            m_BubbleCheckCompleted = false;
            m_SurfaceCleanCompleted = false;
            m_InjectionAngleDegrees = 0f;
            m_InsertionSpeedCmPerSec = 0f;
            m_FlowRateMlPerSec = 0f;
            m_RemovalSpeedCmPerSec = 0f;
            m_RemovalLateralSpeedCmPerSec = 0f;
            m_RemovalLateralSmoothedCmPerSec = 0f;
            m_HasPreviousNeedleTip = false;
            m_LastScoreBreakdown = default;
            m_CurrentInsertionDepthCm = 0f;
            m_EffectiveInsertionDepthTargetCm = m_MinInsertionDepthCm;
            m_CurrentLateralStabilityCmPerSec = 0f;
            m_CurrentPlungerPushRateCmPerSec = 0f;
            m_ThumbTowardKnucklesRateCmPerSec = 0f;
            m_PlungerTravelNormalized = 0f;
            m_HasCompletedInsertionDepth = false;
            m_HasStartedDispensePress = false;
            m_DispenseStopProgress = 0f;
            m_HasBeenInsertedDuringRemoval = false;
            m_AngleGuidanceErrorDegrees = 0f;
            m_InsertionStableHoldProgress = 0f;
            m_HasInitialPlungerDistance = false;
            m_DispenseElapsedSeconds = 0f;
            m_PostInjectionDepthHoldProgress = 0f;
            m_PeakInsertionDepthCm = 0f;
            m_HandDistanceFromCenterCm = 0f;
            m_PreviousHandDistanceFromCenterCm = 0f;
            m_CurrentAxialNeedleSpeedCmPerSec = 0f;
            m_HasPreviousHandDistance = false;
            m_HasInsertionDepthBaseline = false;
            m_InsertionDepthBaselineThumbPlaneCm = 0f;
            m_HasLatchedNearSiteEngagement = false;
            m_ThumbDispenseSustainProgress = 0f;
            m_HasContactPoint = false;
            m_HasLockedContactPoint = false;
            m_HasSmoothedAngle = false;
            m_SmoothedAngleDegrees = 0f;
            m_IdealInjectionAxis = Vector3.down;

            GoToStep(TutorialStep.Start);
        }

        public void SetInjectionType(InjectionType type)
        {
            m_SelectedInjectionType = type;
        }

        public void CycleInjectionType()
        {
            var nextType = m_SelectedInjectionType switch
            {
                InjectionType.None => InjectionType.Intramuscular,
                InjectionType.Intramuscular => InjectionType.Subcutaneous,
                InjectionType.Subcutaneous => InjectionType.Intradermal,
                InjectionType.Intradermal => InjectionType.Intravenous,
                InjectionType.Intravenous => InjectionType.Intramuscular,
                _ => InjectionType.Intramuscular,
            };

            SetInjectionType(nextType);
        }

        public void SetFillAmount(float normalizedAmount)
        {
            m_FillAmountNormalized = Mathf.Clamp01(normalizedAmount);
        }

        public void MarkBubbleCheckCompleted()
        {
            m_BubbleCheckCompleted = true;
        }

        public void MarkSurfaceCleanCompleted()
        {
            m_SurfaceCleanCompleted = true;
        }

        public void AdvanceStep()
        {
            if (m_IsFinished)
                return;

            switch (m_CurrentStep)
            {
                case TutorialStep.Start:
                    GoToStep(TutorialStep.InjectionType);
                    break;
                case TutorialStep.InjectionType:
                    GoToStep(TutorialStep.Calibration);
                    break;
                case TutorialStep.Calibration:
                    GoToStep(TutorialStep.FillSyringe);
                    break;
                case TutorialStep.FillSyringe:
                    GoToStep(TutorialStep.BubbleCheckManual);
                    break;
                case TutorialStep.BubbleCheckManual:
                    GoToStep(TutorialStep.CleanSurfaceAlcohol);
                    break;
                case TutorialStep.CleanSurfaceAlcohol:
                    GoToStep(TutorialStep.InjectionAngle);
                    break;
                case TutorialStep.InjectionAngle:
                    GoToStep(TutorialStep.Insertion);
                    break;
                case TutorialStep.Insertion:
                    m_HasLatchedNearSiteEngagement = true;
                    m_HasCompletedInsertionDepth = true;
                    GoToStep(TutorialStep.FlowRate);
                    break;
                case TutorialStep.FlowRate:
                case TutorialStep.InsertionSpeedFlowRate:
                    // Auto-advance from the dispense detection, or manually via the Next button.
                    GoToStep(TutorialStep.RemoveSpeed);
                    break;
                case TutorialStep.RemoveSpeed:
                    // Auto-advance from withdrawal, or manually via the Next button.
                    GoToStep(TutorialStep.FinalScore);
                    break;
                case TutorialStep.FinalScore:
                    break;
            }
        }

        public void PreviousStep()
        {
            if (m_CurrentStep == TutorialStep.Start)
                return;

            if (m_IsFinished)
            {
                m_IsFinished = false;
                m_IsTutorialRunning = true;
            }

            switch (m_CurrentStep)
            {
                case TutorialStep.InjectionType:
                    GoToStep(TutorialStep.Start);
                    break;
                case TutorialStep.Calibration:
                    GoToStep(TutorialStep.InjectionType);
                    break;
                case TutorialStep.FillSyringe:
                    GoToStep(TutorialStep.Calibration);
                    break;
                case TutorialStep.BubbleCheckManual:
                    GoToStep(TutorialStep.FillSyringe);
                    break;
                case TutorialStep.CleanSurfaceAlcohol:
                    GoToStep(TutorialStep.BubbleCheckManual);
                    break;
                case TutorialStep.InjectionAngle:
                    GoToStep(TutorialStep.CleanSurfaceAlcohol);
                    break;
                case TutorialStep.Insertion:
                    GoToStep(TutorialStep.InjectionAngle);
                    break;
                case TutorialStep.FlowRate:
                    GoToStep(TutorialStep.Insertion);
                    break;
                case TutorialStep.InsertionSpeedFlowRate:
                    GoToStep(TutorialStep.InjectionAngle);
                    break;
                case TutorialStep.RemoveSpeed:
                    GoToStep(TutorialStep.FlowRate);
                    break;
                case TutorialStep.FinalScore:
                    GoToStep(TutorialStep.RemoveSpeed);
                    break;
            }

            m_HasPreviousNeedleTip = false;
        }

        void ResolveReferences()
        {
            if (m_Tracker == null)
                m_Tracker = GetComponent<SyringeOverlayTracker>() ?? FindAnyObjectByType<SyringeOverlayTracker>();

            if (m_SurfaceSelectionTool == null)
                m_SurfaceSelectionTool = GetComponent<SurfaceSelectionTool>() ?? FindAnyObjectByType<SurfaceSelectionTool>();

            if (m_PlaneAngleOverlay == null)
                m_PlaneAngleOverlay = GetComponent<SyringePlaneAngleOverlay>() ?? FindAnyObjectByType<SyringePlaneAngleOverlay>();

            if (m_HandOverlayBridge == null)
                m_HandOverlayBridge = GetComponent<HandOverlaySkeletonToggleBridge>() ?? FindAnyObjectByType<HandOverlaySkeletonToggleBridge>();

            if (m_TargetGuide == null)
            {
                m_TargetGuide = GetComponent<InjectionTargetGuide>() ?? FindAnyObjectByType<InjectionTargetGuide>();
                // Runtime-create the guide if it isn't in the scene (avoids hand-editing the scene YAML).
                if (m_TargetGuide == null)
                    m_TargetGuide = gameObject.AddComponent<InjectionTargetGuide>();
            }
        }

        void DisableWristMenu()
        {
            var wristMenuRoot = GameObject.Find(m_WristMenuRootPath);
            if (wristMenuRoot != null)
                wristMenuRoot.SetActive(false);
        }

        void EnableWristMenu()
        {
            var wristMenuRoot = GameObject.Find(m_WristMenuRootPath);
            if (wristMenuRoot != null)
                wristMenuRoot.SetActive(true);
        }

        void TickStep()
        {
            switch (m_CurrentStep)
            {
                case TutorialStep.Start:
                    TickStartStep();
                    break;
                case TutorialStep.Calibration:
                    TickCalibrationStep();
                    break;
                case TutorialStep.InjectionType:
                    TickInjectionTypeStep();
                    break;
                case TutorialStep.FillSyringe:
                    TickFillSyringeStep();
                    break;
                case TutorialStep.BubbleCheckManual:
                    TickBubbleCheckStep();
                    break;
                case TutorialStep.CleanSurfaceAlcohol:
                    TickCleanSurfaceStep();
                    break;
                case TutorialStep.InjectionAngle:
                    TickInjectionAngleStep();
                    break;
                case TutorialStep.Insertion:
                    TickInsertionStep();
                    break;
                case TutorialStep.FlowRate:
                    TickFlowRateStep();
                    break;
                case TutorialStep.InsertionSpeedFlowRate:
                    TickInsertionStep();
                    break;
                case TutorialStep.RemoveSpeed:
                    TickRemovalSpeedStep();
                    break;
                case TutorialStep.FinalScore:
                    // FinalizeScore runs once from GoToStep(FinalScore); avoid re-running every frame.
                    break;
            }
        }

        void TickStartStep()
        {
            if (m_AutoAdvanceStub && m_StepElapsedSeconds >= m_IntroDelaySeconds)
                GoToStep(TutorialStep.InjectionType);
        }

        void TickCalibrationStep()
        {
            if (m_Tracker != null &&
                m_AutoAdvanceStub &&
                !m_Tracker.isMarkerCalibrated &&
                !m_Tracker.isCalibratingMarker)
            {
                m_Tracker.StartMarkerCalibration();
            }

            // Stay on Calibration until the user taps Next — surface + syringe are both available from the action bar.
        }

        void TickInjectionTypeStep()
        {
            if (m_SelectedInjectionType == InjectionType.None &&
                m_AutoAdvanceStub &&
                m_StepElapsedSeconds >= m_TypeSelectionDelaySeconds)
            {
                m_SelectedInjectionType = m_DefaultInjectionType;
            }

            // Do not auto-advance when a type is chosen — use Next / AdvanceStep so the user stays on this screen.
        }

        void TickFillSyringeStep()
        {
            if (m_AutoAdvanceStub)
            {
                m_FillAmountNormalized = Mathf.Clamp01(
                    m_FillAmountNormalized + m_FillRateNormalizedPerSecond * Time.deltaTime);
            }

            if (m_FillAmountNormalized >= m_TargetFillAmount)
                GoToStep(TutorialStep.BubbleCheckManual);
        }

        void TickBubbleCheckStep()
        {
            if (!m_BubbleCheckCompleted && m_AutoAdvanceStub && m_StepElapsedSeconds >= m_BubbleManualFallbackDelay)
                m_BubbleCheckCompleted = true;

            if (m_BubbleCheckCompleted)
                GoToStep(TutorialStep.CleanSurfaceAlcohol);
        }

        void TickCleanSurfaceStep()
        {
            if (!m_SurfaceCleanCompleted && m_AutoAdvanceStub && m_StepElapsedSeconds >= m_CleanSurfaceManualFallbackDelay)
                m_SurfaceCleanCompleted = true;
        }

        void TickInjectionAngleStep()
        {
            var angleRange = GetTargetInjectionAngleRangeForSelectedType();
            var hasTracking = TryUpdatePoseMetrics();
            if (!hasTracking && m_AutoAdvanceStub)
            {
                var target = 0.5f * (angleRange.x + angleRange.y);
                m_InjectionAngleDegrees = Mathf.Lerp(m_InjectionAngleDegrees, target, 4f * Time.deltaTime);
            }

            var isWithinRange =
                m_InjectionAngleDegrees >= angleRange.x &&
                m_InjectionAngleDegrees <= angleRange.y;

            if (m_InjectionAngleDegrees < angleRange.x)
                m_AngleGuidanceErrorDegrees = angleRange.x - m_InjectionAngleDegrees;
            else if (m_InjectionAngleDegrees > angleRange.y)
                m_AngleGuidanceErrorDegrees = angleRange.y - m_InjectionAngleDegrees;
            else
                m_AngleGuidanceErrorDegrees = 0f;

            if (isWithinRange)
                m_AngleHoldProgress += Time.deltaTime;
            else
                m_AngleHoldProgress = 0f;

            if (m_AngleHoldProgress >= m_AngleHoldSeconds)
                GoToStep(TutorialStep.Insertion);
        }

        void TickInsertionStep()
        {
            var hasTracking = TryUpdatePoseMetrics();

            if (!IsThumbEngagedAtSite())
            {
                m_ThumbDispenseSustainProgress = 0f;
                m_InsertionThumbActivityProgress = 0f;
                m_InsertionAtDepthHoldProgress = 0f;
                return;
            }

            if (!hasTracking)
                return;

            var inserted = m_CurrentInsertionDepthCm >= m_MinDepthBeforeDispenseCm ||
                           m_PeakInsertionDepthCm >= m_MinDepthBeforeDispenseCm;
            var thumbClosing = m_ThumbTowardKnucklesRateCmPerSec >= m_ThumbTowardKnucklesStartCmPerSec;
            var thumbActive = Mathf.Abs(m_ThumbTowardKnucklesRateCmPerSec) >= m_ThumbTowardKnucklesStartCmPerSec * 0.45f;

            if (thumbClosing && inserted)
                m_ThumbDispenseSustainProgress += Time.deltaTime;
            else
                m_ThumbDispenseSustainProgress = 0f;

            if (inserted && thumbActive)
                m_InsertionThumbActivityProgress += Time.deltaTime;
            else
                m_InsertionThumbActivityProgress = 0f;

            if (inserted && !thumbActive)
                m_InsertionAtDepthHoldProgress += Time.deltaTime;
            else
                m_InsertionAtDepthHoldProgress = 0f;

            var readyForFlow =
                m_ThumbDispenseSustainProgress >= m_ThumbDispenseSustainSeconds ||
                (inserted && m_InsertionThumbActivityProgress >= m_InsertionThumbActivitySeconds) ||
                (inserted && m_InsertionAtDepthHoldProgress >= m_InsertionAtDepthHoldSeconds);

            if (readyForFlow)
            {
                m_HasCompletedInsertionDepth = inserted;
                m_HasLatchedNearSiteEngagement = inserted;
                GoToStep(TutorialStep.FlowRate);
            }
        }

        void TickFlowRateStep()
        {
            if (!HasMeaningfulInsertionAtSite())
                return;

            var hasTracking = TryUpdatePoseMetrics();
            var atSite = m_Tracker != null &&
                         (m_Tracker.isThumbToCenterMode || m_Tracker.isNearInjectionSite);
            if (!hasTracking || !atSite)
                return;

            if (m_ThumbTowardKnucklesRateCmPerSec >= m_ThumbTowardKnucklesStartCmPerSec)
                m_HasStartedDispensePress = true;

            if (!m_HasStartedDispensePress)
                return;

            m_DispenseElapsedSeconds += Time.deltaTime;

            if (m_ThumbTowardKnucklesRateCmPerSec <= m_ThumbTowardKnucklesStopCmPerSec)
                m_DispenseStopProgress += Time.deltaTime;
            else
                m_DispenseStopProgress = 0f;

            if (m_DispenseElapsedSeconds >= m_MinDispenseActiveSeconds &&
                m_DispenseStopProgress >= m_DispenseStopHoldSeconds)
                GoToStep(TutorialStep.RemoveSpeed);
        }

        bool IsThumbEngagedAtSite() => m_Tracker != null && m_Tracker.isThumbToCenterMode;

        bool HasMeaningfulInsertionAtSite() =>
            m_HasLatchedNearSiteEngagement && m_PeakInsertionDepthCm >= m_MinDepthBeforeDispenseCm;

        /// <summary>
        /// On the removal card: auto-advance when withdrawing or after holding steady at the site.
        /// </summary>
        void TickPostInjectionAutoAdvance(bool hasTracking)
        {
            if (!HasMeaningfulInsertionAtSite())
                return;

            m_PeakInsertionDepthCm = Mathf.Max(m_PeakInsertionDepthCm, m_CurrentInsertionDepthCm);

            var depthWithdrawing = m_PeakInsertionDepthCm - m_CurrentInsertionDepthCm >= m_WithdrawalDepthDeltaCm;
            var axialWithdrawing = m_CurrentAxialNeedleSpeedCmPerSec <= -m_MinWithdrawalAxialSpeedCmPerSec;

            var handAwaySpeed = 0f;
            if (m_HasPreviousHandDistance)
                handAwaySpeed = (m_HandDistanceFromCenterCm - m_PreviousHandDistanceFromCenterCm) / Mathf.Max(Time.deltaTime, 0.0001f);
            var handMovingAway = handAwaySpeed >= m_MinHandAwayFromCenterCmPerSec;

            if (depthWithdrawing || axialWithdrawing || handMovingAway)
            {
                GoToStep(TutorialStep.FinalScore);
                return;
            }

            if (!hasTracking || !m_Tracker.isThumbToCenterMode)
            {
                m_PostInjectionDepthHoldProgress = 0f;
                return;
            }

            var stableEnough = m_CurrentLateralStabilityCmPerSec <= m_MaxLateralStabilityCmPerSec;
            if (stableEnough)
                m_PostInjectionDepthHoldProgress += Time.deltaTime;
            else
                m_PostInjectionDepthHoldProgress = 0f;

            if (m_PostInjectionDepthHoldProgress >= m_PostInjectionHoldSeconds)
                GoToStep(TutorialStep.FinalScore);
        }

        void TickRemovalSpeedStep()
        {
            if (!HasMeaningfulInsertionAtSite())
                return;

            var hasTracking = TryUpdatePoseMetrics();
            if (!hasTracking)
                return;

            var t = Mathf.Clamp01(m_RemovalLateralSmoothing * Time.deltaTime);
            m_RemovalLateralSmoothedCmPerSec = Mathf.Lerp(m_RemovalLateralSmoothedCmPerSec, m_RemovalLateralSpeedCmPerSec, t);

            m_PeakInsertionDepthCm = Mathf.Max(m_PeakInsertionDepthCm, m_CurrentInsertionDepthCm);

            if (m_PeakInsertionDepthCm >= m_MinDepthBeforeDispenseCm &&
                m_CurrentInsertionDepthCm > m_RemovalCompleteDepthCm)
                m_HasBeenInsertedDuringRemoval = true;

            if (m_HasBeenInsertedDuringRemoval &&
                m_PeakInsertionDepthCm >= m_MinDepthBeforeDispenseCm &&
                m_CurrentInsertionDepthCm <= m_RemovalCompleteDepthCm)
            {
                GoToStep(TutorialStep.FinalScore);
                return;
            }

            var removeOk = Mathf.Abs(m_RemovalSpeedCmPerSec - m_TargetRemovalSpeedCmPerSec) <= 1.5f;
            var stabilityOk = m_RemovalLateralSmoothedCmPerSec <= m_MaxRemovalLateralSpeedCmPerSec;
            if (removeOk && stabilityOk)
                m_RemovalHoldProgress += Time.deltaTime;
            else
                m_RemovalHoldProgress = 0f;

            if (m_RemovalHoldProgress >= m_SpeedHoldSeconds)
                GoToStep(TutorialStep.FinalScore);

            TickPostInjectionAutoAdvance(hasTracking);
        }

        bool TryUpdatePoseMetrics()
        {
            if (m_Tracker == null || !m_Tracker.TryGetSyringePose(out var pose))
                return false;

            var dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Pose surfacePose = default;
            var hasSurface = m_SurfaceSelectionTool != null &&
                             m_SurfaceSelectionTool.TryGetPlacedSurface(out surfacePose, out _);

            if (hasSurface)
            {
                var normal = surfacePose.up;
                if (normal.sqrMagnitude < 0.000001f)
                    normal = Vector3.up;
                else
                    normal.Normalize();

                var plane = new Plane(normal, surfacePose.position);

                // The needle is assumed to enter at the target-spot center. Snapping the contact
                // point there pins one end of the syringe to a stable, known location so depth and
                // angle ride a long lever instead of the jittery, occlusion-prone needle tip.
                var spot = surfacePose.position;
                var snapStep = IsSiteSnapStep(m_CurrentStep);
                var lockStep = m_CurrentStep == TutorialStep.Insertion ||
                               m_CurrentStep == TutorialStep.FlowRate ||
                               m_CurrentStep == TutorialStep.RemoveSpeed ||
                               m_CurrentStep == TutorialStep.InsertionSpeedFlowRate;
                var tipPlaneDistance = Mathf.Abs(plane.GetDistanceToPoint(pose.needleTip));

                // Once the needle first reaches the spot during insertion/removal, lock the entry so
                // finger occlusion while inserted can't drag the contact point around.
                if (lockStep && !m_HasLockedContactPoint && tipPlaneDistance <= m_ContactSnapDistanceMeters)
                {
                    m_LockedContactPoint = spot;
                    m_HasLockedContactPoint = true;
                }

                m_InjectionContactPoint = m_HasLockedContactPoint ? m_LockedContactPoint : spot;
                m_HasContactPoint = snapStep;

                // Drive the tracker's hard center-lock so the rendered needle (and this pose) snap
                // their entry to the site center while near it.
                m_Tracker.SetInjectionSnapTarget(spot, normal, m_CenterLockRadius, snapStep, m_AbsoluteCenterLock);

                var useThumbAxis = m_Tracker != null && m_Tracker.isThumbToCenterMode;
                var nearSite = m_Tracker != null &&
                               (m_Tracker.isThumbToCenterMode || m_Tracker.isNearInjectionSite);
                var approachAxis = useThumbAxis
                    ? m_Tracker.thumbToCenterAxis
                    : nearSite && lockStep
                        ? (spot - m_Tracker.rawThumbTip).normalized
                        : pose.forward;

                // Ideal needle path: thumb-to-center axis when proximity-locked, otherwise live heading.
                m_IdealInjectionAxis = useThumbAxis || (nearSite && lockStep)
                    ? approachAxis
                    : ComputeIdealInjectionAxis(normal, pose.forward);

                m_InjectionAngleDegrees = useThumbAxis || (nearSite && lockStep)
                    ? AngleFromSurface(approachAxis, normal)
                    : ComputeInjectionAngleDegrees(pose, normal, m_InjectionContactPoint);

                // Depth = inserted length along the ideal axis, measured from the snapped entry,
                // capped at the per-type max (which is itself capped at needle length).
                var depthCapCm = currentInjectionMaxDepthCm;
                var thumbPlaneDistCm = Mathf.Abs(plane.GetDistanceToPoint(pose.plunger)) * 100f;

                if (useThumbAxis && m_CurrentStep == TutorialStep.Insertion)
                {
                    if (!m_HasInsertionDepthBaseline &&
                        thumbPlaneDistCm <= m_MaxThumbPlaneDistForDepthBaselineCm)
                    {
                        m_InsertionDepthBaselineThumbPlaneCm = thumbPlaneDistCm;
                        m_HasInsertionDepthBaseline = true;
                    }

                    if (m_HasInsertionDepthBaseline)
                    {
                        var thumbApproachCm = Mathf.Max(0f, m_InsertionDepthBaselineThumbPlaneCm - thumbPlaneDistCm);
                        m_CurrentInsertionDepthCm = Mathf.Clamp(thumbApproachCm, 0f, depthCapCm);
                    }
                    else
                    {
                        var depthAlongCm = Vector3.Dot(pose.needleTip - m_InjectionContactPoint, m_IdealInjectionAxis) * 100f;
                        m_CurrentInsertionDepthCm = Mathf.Clamp(depthAlongCm, 0f, depthCapCm);
                    }
                }
                else
                {
                    var depthAlongCm = Vector3.Dot(pose.needleTip - m_InjectionContactPoint, m_IdealInjectionAxis) * 100f;
                    m_CurrentInsertionDepthCm = Mathf.Clamp(depthAlongCm, 0f, depthCapCm);
                }

                m_EffectiveInsertionDepthTargetCm = Mathf.Min(currentInjectionGreenDepthCm, depthCapCm);

                if (IsInsertionPipelineStep(m_CurrentStep) && !useThumbAxis)
                    m_CurrentInsertionDepthCm = 0f;

                if (useThumbAxis)
                    m_PeakInsertionDepthCm = Mathf.Max(m_PeakInsertionDepthCm, m_CurrentInsertionDepthCm);

                if (m_HasPreviousNeedleTip)
                {
                    var needleVelocity = (pose.needleTip - m_PreviousNeedleTip) / dt;
                    var inwardAxis = m_IdealInjectionAxis.sqrMagnitude > 0.000001f
                        ? m_IdealInjectionAxis.normalized
                        : -normal;
                    var axialCmPerSec = Vector3.Dot(needleVelocity, inwardAxis) * 100f;
                    var lateralCmPerSec = Vector3.ProjectOnPlane(needleVelocity, inwardAxis).magnitude * 100f;

                    // Keep the raw axial/lateral world-velocity for the stability readout, but the
                    // displayed insertion speed is derived from the tracked depth below (the lateral
                    // center-snap pollutes world velocity and can read ~0).
                    m_CurrentAxialNeedleSpeedCmPerSec = axialCmPerSec;
                    m_CurrentLateralStabilityCmPerSec = lateralCmPerSec;
                }

                var handOnPlane = Vector3.ProjectOnPlane(pose.wingsCenter - spot, normal);
                m_PreviousHandDistanceFromCenterCm = m_HasPreviousHandDistance
                    ? m_HandDistanceFromCenterCm
                    : handOnPlane.magnitude * 100f;
                m_HandDistanceFromCenterCm = handOnPlane.magnitude * 100f;
                m_HasPreviousHandDistance = true;
            }
            else
            {
                m_InjectionAngleDegrees = Vector3.Angle(pose.forward, Vector3.down);
                m_CurrentInsertionDepthCm = 0f;
                m_EffectiveInsertionDepthTargetCm = m_MinInsertionDepthCm;
                m_CurrentLateralStabilityCmPerSec = 0f;
                m_CurrentAxialNeedleSpeedCmPerSec = 0f;
                m_HasPreviousHandDistance = false;
                m_HasContactPoint = false;
                m_Tracker.SetInjectionSnapTarget(Vector3.zero, Vector3.up, 0f, false);
            }

            if (m_HasPreviousNeedleTip)
            {
                var needleVelocity = (pose.needleTip - m_PreviousNeedleTip) / dt;
                var forwardSpeedCmPerSec = Vector3.Dot(needleVelocity, pose.forward) * 100f;

                if (m_CurrentStep == TutorialStep.Insertion ||
                    m_CurrentStep == TutorialStep.FlowRate ||
                    m_CurrentStep == TutorialStep.InsertionSpeedFlowRate)
                {
                    if (!hasSurface)
                        m_InsertionSpeedCmPerSec = Mathf.Max(0f, forwardSpeedCmPerSec);
                }
                else if (m_CurrentStep == TutorialStep.RemoveSpeed)
                {
                    m_RemovalSpeedCmPerSec = Mathf.Max(0f, -forwardSpeedCmPerSec);
                    var axialMetersPerSec = Vector3.Dot(needleVelocity, pose.forward);
                    var lateralVec = needleVelocity - pose.forward * axialMetersPerSec;
                    m_RemovalLateralSpeedCmPerSec = lateralVec.magnitude * 100f;
                }
            }

            var plungerToWingsCm = Vector3.Distance(pose.plunger, pose.wingsCenter) * 100f;
            if (!m_HasInitialPlungerDistance &&
                (m_CurrentStep == TutorialStep.FlowRate || m_CurrentStep == TutorialStep.InsertionSpeedFlowRate))
            {
                m_InitialPlungerToWingsDistanceCm = plungerToWingsCm;
                m_HasInitialPlungerDistance = true;
            }

            // Once inserted at the site (latched on entering Insertion), keep reading the dispense
            // rate even if the per-frame thumb-engagement flag flickers — otherwise flow rate stays 0
            // and the dispense detection that advances the step never fires.
            var thumbMetricsActive = IsInsertionPipelineStep(m_CurrentStep) &&
                (m_HasLatchedNearSiteEngagement || IsThumbEngagedAtSite() ||
                 (m_Tracker != null && m_Tracker.isNearInjectionSite));
            if (thumbMetricsActive && m_Tracker != null)
            {
                m_ThumbTowardKnucklesRateCmPerSec = m_Tracker.rawThumbTowardKnucklesRateCmPerSec;
                m_CurrentPlungerPushRateCmPerSec = Mathf.Max(0f, m_ThumbTowardKnucklesRateCmPerSec);
            }
            else if (IsInsertionPipelineStep(m_CurrentStep))
            {
                m_ThumbTowardKnucklesRateCmPerSec = 0f;
                m_CurrentPlungerPushRateCmPerSec = 0f;
            }

            if (m_HasInitialPlungerDistance)
            {
                var travelCm = Mathf.Max(0f, m_InitialPlungerToWingsDistanceCm - plungerToWingsCm);
                m_PlungerTravelNormalized = Mathf.Clamp01(travelCm / Mathf.Max(0.01f, m_MinPlungerTravelToCompleteCm));
                var rawFlow = Mathf.Clamp(m_ThumbTowardKnucklesRateCmPerSec * 0.38f, 0f, 2.5f);
                m_FlowRateMlPerSec = Mathf.Lerp(m_FlowRateMlPerSec, rawFlow, Mathf.Clamp01(m_SpeedDisplaySmoothing * dt));
            }

            // Displayed insertion speed = low-passed rate of change of the tracked insertion depth.
            // Depth runs along the injection axis (and uses the snap-immune thumb-plane approach when
            // inserted), so this registers the real push and is not polluted by the lateral snap.
            if (IsInsertionPipelineStep(m_CurrentStep) && hasSurface)
            {
                if (m_HasPreviousInsertionDepth)
                {
                    var rawInsertionSpeed = Mathf.Max(0f, (m_CurrentInsertionDepthCm - m_PreviousInsertionDepthCm) / dt);
                    m_InsertionSpeedCmPerSec = Mathf.Lerp(m_InsertionSpeedCmPerSec, rawInsertionSpeed, Mathf.Clamp01(m_SpeedDisplaySmoothing * dt));
                }
                m_PreviousInsertionDepthCm = m_CurrentInsertionDepthCm;
                m_HasPreviousInsertionDepth = true;
            }
            else
            {
                m_HasPreviousInsertionDepth = false;
            }

            m_PreviousNeedleTip = pose.needleTip;
            m_HasPreviousNeedleTip = true;
            m_PlungerWorldPrev = pose.plunger;
            return true;
        }

        void FinalizeScore()
        {
            if (m_IsFinished)
                return;

            // Markerless trainer — the calibration step is grip/orientation only (marker assist is
            // dormant), so isMarkerCalibrated is never set. Award full credit for reaching the end.
            var calibrationScore = 20f;
            var typeScore = m_SelectedInjectionType == InjectionType.None ? 0f : 10f;
            var fillScore = Mathf.Clamp01(1f - Mathf.Abs(m_FillAmountNormalized - m_TargetFillAmount)) * 12f;
            var bubbleScore = m_BubbleCheckCompleted ? 10f : 0f;
            var surfaceCleanScore = m_SurfaceCleanCompleted ? 8f : 0f;

            // Angle: full credit anywhere inside the selected type's valid band (what the blue cone
            // shows), tapering off outside it — so a wide-range type (IM) is not scored on the same
            // fixed tolerance as a tight one (ID).
            var angleRange = GetTargetInjectionAngleRangeForSelectedType();
            var angleMidpoint = 0.5f * (angleRange.x + angleRange.y);
            var angleHalfWidth = Mathf.Max(2f, 0.5f * (angleRange.y - angleRange.x));
            var angleErrOutsideBand = Mathf.Max(0f, Mathf.Abs(m_InjectionAngleDegrees - angleMidpoint) - angleHalfWidth);
            var angleScore = Mathf.Clamp01(1f - angleErrOutsideBand / Mathf.Max(8f, angleHalfWidth)) * 12f;
            var insertionScore = Mathf.Clamp01(1f - Mathf.Abs(m_InsertionSpeedCmPerSec - m_TargetInsertionSpeedCmPerSec) / 4f) * 8f;
            var flowScore = Mathf.Clamp01(1f - Mathf.Abs(m_FlowRateMlPerSec - m_TargetFlowRateMlPerSec) / 0.7f) * 5f;
            var removeSpeedQuality = Mathf.Clamp01(1f - Mathf.Abs(m_RemovalSpeedCmPerSec - m_TargetRemovalSpeedCmPerSec) / 4f);
            var removeStabilityQuality = Mathf.Clamp01(1f - m_RemovalLateralSmoothedCmPerSec / m_RemovalLateralScoreRefCmPerSec);
            var removeScore = (removeSpeedQuality * 0.65f + removeStabilityQuality * 0.35f) * 15f;

            var sum = calibrationScore + typeScore + fillScore + bubbleScore + surfaceCleanScore + angleScore + insertionScore + flowScore + removeScore;
            m_FinalScore = Mathf.Clamp(sum, 0f, 100f);

            m_LastScoreBreakdown = new ScoreBreakdown
            {
                calibration = calibrationScore,
                injectionType = typeScore,
                fill = fillScore,
                bubble = bubbleScore,
                surfaceClean = surfaceCleanScore,
                angle = angleScore,
                insertion = insertionScore,
                flow = flowScore,
                removal = removeScore,
                total = m_FinalScore,
            };

            m_IsFinished = true;
            m_IsTutorialRunning = false;
            Debug.Log($"[Injection Tutorial Stub] Complete. Final score: {m_FinalScore:F1}/100", this);
        }

        void GoToStep(TutorialStep nextStep)
        {
            m_CurrentStep = nextStep;
            m_StepElapsedSeconds = 0f;

            // Release the tracker center-lock outside the angle/insertion/removal steps so the needle
            // is not snapped to a site on unrelated steps.
            var snapStep = IsSiteSnapStep(nextStep);
            if (m_Tracker != null && !snapStep)
                m_Tracker.SetInjectionSnapTarget(Vector3.zero, Vector3.up, 0f, false);

            if (nextStep == TutorialStep.InjectionAngle)
                m_AngleHoldProgress = 0f;

            if (nextStep == TutorialStep.Insertion)
            {
                m_InsertionHoldProgress = 0f;
                m_InsertionStableHoldProgress = 0f;
                m_HasCompletedInsertionDepth = false;
                m_HasStartedDispensePress = false;
                m_DispenseStopProgress = 0f;
                m_DispenseElapsedSeconds = 0f;
                m_CurrentInsertionDepthCm = 0f;
                m_EffectiveInsertionDepthTargetCm = m_MinInsertionDepthCm;
                m_CurrentLateralStabilityCmPerSec = 0f;
                m_CurrentPlungerPushRateCmPerSec = 0f;
                m_ThumbTowardKnucklesRateCmPerSec = 0f;
                m_PlungerTravelNormalized = 0f;
                m_HasInitialPlungerDistance = false;
                m_HasPreviousNeedleTip = false;
                m_PostInjectionDepthHoldProgress = 0f;
                m_PeakInsertionDepthCm = 0f;
                m_HandDistanceFromCenterCm = 0f;
                m_PreviousHandDistanceFromCenterCm = 0f;
                m_CurrentAxialNeedleSpeedCmPerSec = 0f;
                m_HasPreviousHandDistance = false;
                m_HasInsertionDepthBaseline = false;
                m_InsertionDepthBaselineThumbPlaneCm = 0f;
                m_HasLatchedNearSiteEngagement = false;
                m_ThumbDispenseSustainProgress = 0f;
                m_InsertionThumbActivityProgress = 0f;
                m_InsertionAtDepthHoldProgress = 0f;
                m_PeakInsertionDepthCm = 0f;
                m_HasLockedContactPoint = false;
            }

            if (nextStep == TutorialStep.FlowRate)
            {
                m_HasStartedDispensePress = false;
                m_DispenseStopProgress = 0f;
                m_DispenseElapsedSeconds = 0f;
                m_InsertionHoldProgress = 0f;
                m_HasInitialPlungerDistance = false;
                m_HasPreviousNeedleTip = false;
            }

            if (nextStep == TutorialStep.InsertionSpeedFlowRate)
            {
                m_InsertionHoldProgress = 0f;
                m_InsertionStableHoldProgress = 0f;
                m_HasCompletedInsertionDepth = false;
                m_HasStartedDispensePress = false;
                m_DispenseStopProgress = 0f;
                m_DispenseElapsedSeconds = 0f;
                m_CurrentInsertionDepthCm = 0f;
                m_EffectiveInsertionDepthTargetCm = m_MinInsertionDepthCm;
                m_CurrentLateralStabilityCmPerSec = 0f;
                m_CurrentPlungerPushRateCmPerSec = 0f;
                m_ThumbTowardKnucklesRateCmPerSec = 0f;
                m_PlungerTravelNormalized = 0f;
                m_HasInitialPlungerDistance = false;
                m_HasPreviousNeedleTip = false;
                m_PostInjectionDepthHoldProgress = 0f;
                m_PeakInsertionDepthCm = 0f;
                m_HandDistanceFromCenterCm = 0f;
                m_PreviousHandDistanceFromCenterCm = 0f;
                m_CurrentAxialNeedleSpeedCmPerSec = 0f;
                m_HasPreviousHandDistance = false;
                m_HasInsertionDepthBaseline = false;
                m_InsertionDepthBaselineThumbPlaneCm = 0f;
                m_HasLockedContactPoint = false;
            }

            if (nextStep == TutorialStep.RemoveSpeed)
            {
                m_RemovalHoldProgress = 0f;
                m_RemovalLateralSpeedCmPerSec = 0f;
                m_RemovalLateralSmoothedCmPerSec = 0f;
                m_HasBeenInsertedDuringRemoval = false;
                m_HasPreviousNeedleTip = false;
            }

            if (nextStep == TutorialStep.FinalScore)
                FinalizeScore();

            if (m_PlaneAngleOverlay != null)
                m_PlaneAngleOverlay.SetGuidanceStep(nextStep);

            Debug.Log($"[Injection Tutorial Stub] Step: {nextStep}", this);
        }

        Vector2 GetTargetInjectionAngleRangeForSelectedType()
        {
            var range = m_SelectedInjectionType switch
            {
                InjectionType.Intramuscular => m_IntramuscularAngleRange,
                InjectionType.Subcutaneous => m_SubcutaneousAngleRange,
                InjectionType.Intradermal => m_IntradermalAngleRange,
                InjectionType.Intravenous => m_IntravenousAngleRange,
                _ => m_DefaultInjectionAngleRange,
            };

            if (range.x > range.y)
                (range.x, range.y) = (range.y, range.x);

            m_TargetInjectionAngleRange = range;
            return range;
        }

        /// <summary>Per-type (green, max) needle depth in cm. Green clamped below max; max clamped to needle length.</summary>
        Vector2 GetDepthRangeForSelectedType()
        {
            var depth = m_SelectedInjectionType switch
            {
                InjectionType.Intramuscular => m_IntramuscularDepthCm,
                InjectionType.Subcutaneous => m_SubcutaneousDepthCm,
                InjectionType.Intradermal => m_IntradermalDepthCm,
                InjectionType.Intravenous => m_IntravenousDepthCm,
                _ => m_DefaultDepthCm,
            };

            // Needle metal is 4.7 cm; nothing can go deeper than the needle is long.
            const float needleLengthCm = 4.7f;
            depth.y = Mathf.Clamp(depth.y, 0.05f, needleLengthCm);
            depth.x = Mathf.Clamp(depth.x, 0.02f, depth.y);
            return depth;
        }

        /// <summary>
        /// Unit direction the needle ideally travels below the surface for the selected type:
        /// the nominal (range-midpoint) angle, with azimuth taken from the live syringe heading
        /// so the depth guide lines up under the user's actual approach.
        /// </summary>
        Vector3 ComputeIdealInjectionAxis(Vector3 normal, Vector3 syringeForward)
        {
            var down = -normal;

            // In-plane heading the user is approaching from (fallback: camera, then arbitrary).
            var heading = Vector3.ProjectOnPlane(syringeForward, normal);
            if (heading.sqrMagnitude < 0.000001f)
            {
                var cam = Camera.main;
                if (cam != null)
                    heading = Vector3.ProjectOnPlane(cam.transform.forward, normal);
            }
            if (heading.sqrMagnitude < 0.000001f)
                heading = Vector3.ProjectOnPlane(Vector3.forward, normal);
            if (heading.sqrMagnitude < 0.000001f)
                return down;
            heading.Normalize();

            // Angle from the surface -> angle from the normal (0 = straight in, 90 = flat).
            var fromSurface = Mathf.Clamp(idealInjectionAngleDegrees, 0f, 90f);
            var fromNormalRad = (90f - fromSurface) * Mathf.Deg2Rad;
            var axis = down * Mathf.Cos(fromNormalRad) + heading * Mathf.Sin(fromNormalRad);
            return axis.sqrMagnitude < 0.000001f ? down : axis.normalized;
        }

        /// <summary>Angle of <paramref name="v"/> above the surface plane, 0 = parallel, 90 = perpendicular.</summary>
        static float AngleFromSurface(Vector3 v, Vector3 normal)
        {
            var along = Vector3.ProjectOnPlane(v, normal);
            if (along.sqrMagnitude < 0.000001f || v.sqrMagnitude < 0.000001f)
                return 90f;
            return Vector3.Angle(along.normalized, v.normalized);
        }

        /// <summary>
        /// Syringe-vs-surface angle per <see cref="m_AngleEstimationMode"/>. The long-lever modes
        /// use (plunger - snapped contact) as a stable, occlusion-resistant axis.
        /// </summary>
        float ComputeInjectionAngleDegrees(SyringeOverlayTracker.SyringePoseData pose, Vector3 normal, Vector3 contact)
        {
            var angleAxis = AngleFromSurface(pose.forward, normal);
            var angleLever = AngleFromSurface(pose.plunger - contact, normal);

            float measured;
            switch (m_AngleEstimationMode)
            {
                case AngleEstimationMode.AxisWithSmoothing:
                    measured = angleAxis;
                    var t = 1f - Mathf.Clamp01(m_AngleSmoothing);
                    if (!m_HasSmoothedAngle)
                    {
                        m_SmoothedAngleDegrees = measured;
                        m_HasSmoothedAngle = true;
                    }
                    else
                    {
                        m_SmoothedAngleDegrees = Mathf.Lerp(m_SmoothedAngleDegrees, measured, t);
                    }
                    return m_SmoothedAngleDegrees;

                case AngleEstimationMode.FuseLeverAndAxis:
                    measured = Mathf.Lerp(angleLever, angleAxis, Mathf.Clamp01(m_AngleFuseAxisWeight));
                    break;

                case AngleEstimationMode.LongLeverFromSpot:
                default:
                    measured = angleLever;
                    break;
            }

            m_HasSmoothedAngle = false;
            return measured;
        }

#if UNITY_EDITOR
        void HandleEditorDebugInput()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Alpha1))
                SetInjectionType(InjectionType.Intramuscular);
            else if (Input.GetKeyDown(KeyCode.Alpha2))
                SetInjectionType(InjectionType.Subcutaneous);
            else if (Input.GetKeyDown(KeyCode.Alpha3))
                SetInjectionType(InjectionType.Intradermal);
            else if (Input.GetKeyDown(KeyCode.Alpha4))
                SetInjectionType(InjectionType.Intravenous);

            if (Input.GetKeyDown(KeyCode.B))
                MarkBubbleCheckCompleted();

            if (Input.GetKeyDown(KeyCode.W))
                MarkSurfaceCleanCompleted();

            if (Input.GetKeyDown(KeyCode.N))
                AdvanceStep();
#endif
        }
#endif
    }
}