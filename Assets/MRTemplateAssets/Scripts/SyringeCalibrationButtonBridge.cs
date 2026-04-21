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
        }

        public enum InjectionType
        {
            None,
            Intramuscular,
            Subcutaneous,
            Intradermal,
            Intravenous,
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
        float m_TargetDispensePlungerRateCmPerSec = 1.6f;

        [SerializeField, Min(0.01f)]
        float m_MinPlungerTravelToCompleteCm = 0.8f;

        [SerializeField, Min(0.01f)]
        float m_DispenseStartPlungerRateCmPerSec = 0.12f;

        [SerializeField, Min(0.01f)]
        float m_DispenseStopPlungerRateCmPerSec = 0.08f;

        [SerializeField, Min(0f)]
        float m_DispenseStopHoldSeconds = 0.2f;

        [SerializeField, Min(0.1f)]
        float m_DispenseAutoAdvanceSeconds = 4f;

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

        [SerializeField, Min(0f)]
        [Tooltip("Depth threshold in cm considered fully removed from the surface during removal step.")]
        float m_RemovalCompleteDepthCm = 0.02f;

        [SerializeField, Min(0f)]
        [Tooltip("After this many seconds on the syringe removal card, auto-advance to the score screen (if not already advanced).")]
        float m_RemovalStepAutoAdvanceSeconds = 5f;

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
        bool m_IsDispensePhase;

        [SerializeField]
        float m_CurrentInsertionDepthCm;

        [SerializeField]
        float m_EffectiveInsertionDepthTargetCm;

        [SerializeField]
        float m_CurrentLateralStabilityCmPerSec;

        [SerializeField]
        float m_CurrentPlungerPushRateCmPerSec;

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
        float m_InsertionStableHoldProgress;
        float m_InitialPlungerToWingsDistanceCm;
        bool m_HasInitialPlungerDistance;

        public TutorialStep currentStep => m_CurrentStep;
        public bool isTutorialRunning => m_IsTutorialRunning;
        public bool isFinished => m_IsFinished;
        public InjectionType selectedInjectionType => m_SelectedInjectionType;
        public float fillAmountNormalized => m_FillAmountNormalized;
        public float finalScore => m_FinalScore;
        public ScoreBreakdown lastScoreBreakdown => m_LastScoreBreakdown;

        public float injectionAngleDegrees => m_InjectionAngleDegrees;
        public float insertionSpeedCmPerSec => m_InsertionSpeedCmPerSec;
        public float flowRateMlPerSec => m_FlowRateMlPerSec;
        public float removalSpeedCmPerSec => m_RemovalSpeedCmPerSec;
        /// <summary>Instantaneous lateral needle speed during withdrawal (cm/s), perpendicular to syringe forward.</summary>
        public float removalLateralSpeedCmPerSec => m_RemovalLateralSpeedCmPerSec;
        /// <summary>Smoothed lateral speed used for stability gate and scoring.</summary>
        public float removalLateralSmoothedCmPerSec => m_RemovalLateralSmoothedCmPerSec;
        public float maxRemovalLateralSpeedCmPerSec => m_MaxRemovalLateralSpeedCmPerSec;
        /// <summary>When false, the coaching Next button is disabled. On the removal step, manual advance to the score screen is always allowed.</summary>
        public bool removalCheckpointMet => true;
        public bool bubbleCheckCompleted => m_BubbleCheckCompleted;
        public bool surfaceCleanCompleted => m_SurfaceCleanCompleted;

        public float angleHoldProgressNormalized =>
            m_AngleHoldSeconds > 0.01f ? Mathf.Clamp01(m_AngleHoldProgress / m_AngleHoldSeconds) : 0f;

        public float insertionFlowHoldProgressNormalized =>
            m_SpeedHoldSeconds > 0.01f ? Mathf.Clamp01(m_InsertionHoldProgress / m_SpeedHoldSeconds) : 0f;

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

        public bool isDispensePhase => m_IsDispensePhase;
        public float currentInsertionDepthCm => m_CurrentInsertionDepthCm;
        public float effectiveInsertionDepthTargetCm => m_EffectiveInsertionDepthTargetCm;
        public float currentLateralStabilityCmPerSec => m_CurrentLateralStabilityCmPerSec;
        public float currentPlungerPushRateCmPerSec => m_CurrentPlungerPushRateCmPerSec;
        public float plungerTravelNormalized => m_PlungerTravelNormalized;
        public float angleGuidanceErrorDegrees => m_AngleGuidanceErrorDegrees;
        public float angleHoldSecondsRemaining => Mathf.Max(0f, m_AngleHoldSeconds - m_AngleHoldProgress);
        public float minInsertionDepthCm => m_MinInsertionDepthCm;
        public float maxInsertionDepthCm => m_MaxInsertionDepthCm;
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
            m_IsDispensePhase = false;
            m_CurrentInsertionDepthCm = 0f;
            m_EffectiveInsertionDepthTargetCm = m_MinInsertionDepthCm;
            m_CurrentLateralStabilityCmPerSec = 0f;
            m_CurrentPlungerPushRateCmPerSec = 0f;
            m_PlungerTravelNormalized = 0f;
            m_HasCompletedInsertionDepth = false;
            m_HasStartedDispensePress = false;
            m_DispenseStopProgress = 0f;
            m_HasBeenInsertedDuringRemoval = false;
            m_AngleGuidanceErrorDegrees = 0f;
            m_InsertionStableHoldProgress = 0f;
            m_HasInitialPlungerDistance = false;
            m_DispenseElapsedSeconds = 0f;

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
                    GoToStep(TutorialStep.InsertionSpeedFlowRate);
                    break;
                case TutorialStep.InsertionSpeedFlowRate:
                    GoToStep(TutorialStep.RemoveSpeed);
                    break;
                case TutorialStep.RemoveSpeed:
                    GoToStep(TutorialStep.FinalScore);
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
                case TutorialStep.InjectionAngle:
                    GoToStep(TutorialStep.CleanSurfaceAlcohol);
                    break;
                case TutorialStep.CleanSurfaceAlcohol:
                    GoToStep(TutorialStep.BubbleCheckManual);
                    break;
                case TutorialStep.InsertionSpeedFlowRate:
                    GoToStep(TutorialStep.InjectionAngle);
                    break;
                case TutorialStep.RemoveSpeed:
                    GoToStep(TutorialStep.InsertionSpeedFlowRate);
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
                case TutorialStep.InsertionSpeedFlowRate:
                    TickInsertionAndFlowStep();
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

            if (m_SurfaceCleanCompleted)
                GoToStep(TutorialStep.InjectionAngle);
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
                GoToStep(TutorialStep.InsertionSpeedFlowRate);
        }

        void TickInsertionAndFlowStep()
        {
            var hasTracking = TryUpdatePoseMetrics();
            if (!hasTracking && m_AutoAdvanceStub)
            {
                if (!m_HasCompletedInsertionDepth)
                {
                    m_InsertionSpeedCmPerSec = Mathf.Lerp(m_InsertionSpeedCmPerSec, m_TargetInsertionSpeedCmPerSec, 3f * Time.deltaTime);
                    m_CurrentInsertionDepthCm = Mathf.Lerp(m_CurrentInsertionDepthCm, m_EffectiveInsertionDepthTargetCm + 0.1f, 2.5f * Time.deltaTime);
                    m_CurrentLateralStabilityCmPerSec = Mathf.Lerp(m_CurrentLateralStabilityCmPerSec, 0.6f, 2.5f * Time.deltaTime);
                    m_InsertionStableHoldProgress += Time.deltaTime;
                    if (m_InsertionStableHoldProgress >= m_InsertedStabilityHoldSeconds)
                        m_HasCompletedInsertionDepth = true;
                }
                else
                {
                    m_IsDispensePhase = true;
                    m_CurrentPlungerPushRateCmPerSec = Mathf.Lerp(m_CurrentPlungerPushRateCmPerSec, m_TargetDispensePlungerRateCmPerSec, 3f * Time.deltaTime);
                    m_FlowRateMlPerSec = Mathf.Lerp(m_FlowRateMlPerSec, m_TargetFlowRateMlPerSec, 3f * Time.deltaTime);
                    m_PlungerTravelNormalized = Mathf.Clamp01(m_PlungerTravelNormalized + Time.deltaTime * 0.4f);
                }
            }

            if (!m_HasCompletedInsertionDepth)
            {
                var insertedEnough = m_CurrentInsertionDepthCm >= m_EffectiveInsertionDepthTargetCm;
                var stableEnough = m_CurrentLateralStabilityCmPerSec <= m_MaxLateralStabilityCmPerSec;
                var movingForwardEnough = m_InsertionSpeedCmPerSec >= 0.15f;

                if (insertedEnough && stableEnough && movingForwardEnough)
                    m_InsertionStableHoldProgress += Time.deltaTime;
                else
                    m_InsertionStableHoldProgress = 0f;

                m_InsertionHoldProgress = Mathf.Clamp01(m_InsertionStableHoldProgress / Mathf.Max(0.01f, m_InsertedStabilityHoldSeconds)) * m_SpeedHoldSeconds;

                if (m_InsertionStableHoldProgress >= m_InsertedStabilityHoldSeconds)
                {
                    m_HasCompletedInsertionDepth = true;
                    m_InsertionHoldProgress = 0f;
                }
            }

            if (!m_IsDispensePhase)
            {
                // One-way latch: once insertion depth is completed, plunger motion starts flow mode and never reverts.
                if (m_HasCompletedInsertionDepth && m_CurrentPlungerPushRateCmPerSec >= m_DispenseStartPlungerRateCmPerSec)
                    m_IsDispensePhase = true;
            }

            if (m_IsDispensePhase)
            {
                if (m_HasStartedDispensePress)
                    m_DispenseElapsedSeconds += Time.deltaTime;

                if (m_DispenseElapsedSeconds >= m_DispenseAutoAdvanceSeconds)
                {
                    GoToStep(TutorialStep.RemoveSpeed);
                    return;
                }

                if (m_CurrentPlungerPushRateCmPerSec >= m_DispenseStartPlungerRateCmPerSec)
                    m_HasStartedDispensePress = true;

                var flowOk = Mathf.Abs(m_FlowRateMlPerSec - m_TargetFlowRateMlPerSec) <= 0.22f;
                var plungerRateOk = Mathf.Abs(m_CurrentPlungerPushRateCmPerSec - m_TargetDispensePlungerRateCmPerSec) <= 0.8f;
                var stableEnough = m_CurrentLateralStabilityCmPerSec <= m_MaxLateralStabilityCmPerSec;

                if (flowOk && plungerRateOk && stableEnough)
                    m_InsertionHoldProgress += Time.deltaTime;
                else
                    m_InsertionHoldProgress = Mathf.Max(0f, m_InsertionHoldProgress - Time.deltaTime * 0.8f);

                // User-requested behavior: once dispensing started, stopping plunger press advances to the next card.
                if (m_HasStartedDispensePress)
                {
                    if (m_CurrentPlungerPushRateCmPerSec <= m_DispenseStopPlungerRateCmPerSec)
                        m_DispenseStopProgress += Time.deltaTime;
                    else
                        m_DispenseStopProgress = 0f;

                    if (m_DispenseStopProgress >= m_DispenseStopHoldSeconds)
                    {
                        GoToStep(TutorialStep.RemoveSpeed);
                        return;
                    }
                }

                var plungerComplete = m_PlungerTravelNormalized >= 0.98f;
                if (plungerComplete)
                    GoToStep(TutorialStep.RemoveSpeed);
            }
        }

        void TickRemovalSpeedStep()
        {
            if (m_StepElapsedSeconds >= m_RemovalStepAutoAdvanceSeconds)
            {
                GoToStep(TutorialStep.FinalScore);
                return;
            }

            var hasTracking = TryUpdatePoseMetrics();
            if (!hasTracking && m_AutoAdvanceStub)
            {
                m_RemovalSpeedCmPerSec = Mathf.Lerp(m_RemovalSpeedCmPerSec, m_TargetRemovalSpeedCmPerSec, 3f * Time.deltaTime);
                m_RemovalLateralSmoothedCmPerSec = Mathf.Lerp(m_RemovalLateralSmoothedCmPerSec, 0f, 5f * Time.deltaTime);
            }
            else if (hasTracking)
            {
                var t = Mathf.Clamp01(m_RemovalLateralSmoothing * Time.deltaTime);
                m_RemovalLateralSmoothedCmPerSec = Mathf.Lerp(m_RemovalLateralSmoothedCmPerSec, m_RemovalLateralSpeedCmPerSec, t);
            }

            if (m_CurrentInsertionDepthCm > m_RemovalCompleteDepthCm)
                m_HasBeenInsertedDuringRemoval = true;

            if (m_HasBeenInsertedDuringRemoval && m_CurrentInsertionDepthCm <= m_RemovalCompleteDepthCm)
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

                // Injection angle is measured from the surface plane (0 = parallel to surface, 90 = perpendicular).
                var alongSurface = Vector3.ProjectOnPlane(pose.forward, normal);
                if (alongSurface.sqrMagnitude < 0.000001f)
                    m_InjectionAngleDegrees = 90f;
                else
                    m_InjectionAngleDegrees = Vector3.Angle(alongSurface.normalized, pose.forward);

                var signedTip = plane.GetDistanceToPoint(pose.needleTip);
                var signedBase = plane.GetDistanceToPoint(pose.needleBase);
                var needleLengthCm = Vector3.Distance(pose.needleTip, pose.needleBase) * 100f;

                // Keep insertion goal strictly below the visible needle length.
                var clampedNeedleTarget = Mathf.Max(0.06f, needleLengthCm * 0.65f);
                m_EffectiveInsertionDepthTargetCm = Mathf.Min(m_MinInsertionDepthCm, clampedNeedleTarget, m_MaxInsertionDepthCm);

                float penetrationCm;
                if (signedTip * signedBase <= 0f)
                {
                    // Needle segment crosses the plane; depth is bounded by the visible needle length.
                    penetrationCm = Mathf.Min(Mathf.Abs(signedTip) * 100f, needleLengthCm);
                }
                else
                {
                    var inwardSign = Mathf.Sign(-Vector3.Dot(pose.forward, normal));
                    if (Mathf.Approximately(inwardSign, 0f))
                        inwardSign = -1f;

                    var depthCandidate = inwardSign * signedTip * 100f;
                    penetrationCm = Mathf.Clamp(depthCandidate, 0f, needleLengthCm);
                }

                m_CurrentInsertionDepthCm = Mathf.Min(penetrationCm, m_MaxInsertionDepthCm);

                if (m_HasPreviousNeedleTip)
                {
                    var needleVelocity = (pose.needleTip - m_PreviousNeedleTip) / dt;
                    var inwardAxis = -normal * Mathf.Sign(-Vector3.Dot(pose.forward, normal));
                    if (inwardAxis.sqrMagnitude < 0.000001f)
                        inwardAxis = -normal;
                    inwardAxis.Normalize();
                    var axialCmPerSec = Vector3.Dot(needleVelocity, inwardAxis) * 100f;
                    var lateralCmPerSec = Vector3.ProjectOnPlane(needleVelocity, inwardAxis).magnitude * 100f;

                    m_InsertionSpeedCmPerSec = Mathf.Max(0f, axialCmPerSec);
                    m_CurrentLateralStabilityCmPerSec = lateralCmPerSec;
                }
            }
            else
            {
                m_InjectionAngleDegrees = Vector3.Angle(pose.forward, Vector3.down);
                m_CurrentInsertionDepthCm = 0f;
                m_EffectiveInsertionDepthTargetCm = m_MinInsertionDepthCm;
                m_CurrentLateralStabilityCmPerSec = 0f;
            }

            if (m_HasPreviousNeedleTip)
            {
                var needleVelocity = (pose.needleTip - m_PreviousNeedleTip) / dt;
                var forwardSpeedCmPerSec = Vector3.Dot(needleVelocity, pose.forward) * 100f;

                if (m_CurrentStep == TutorialStep.InsertionSpeedFlowRate)
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
            if (!m_HasInitialPlungerDistance && m_CurrentStep == TutorialStep.InsertionSpeedFlowRate)
            {
                m_InitialPlungerToWingsDistanceCm = plungerToWingsCm;
                m_HasInitialPlungerDistance = true;
            }

            if (m_HasPreviousNeedleTip)
            {
                var plungerVelocity = (pose.plunger - m_PlungerWorldPrev) / dt;
                m_CurrentPlungerPushRateCmPerSec = Mathf.Max(0f, Vector3.Dot(plungerVelocity, pose.forward) * 100f);
            }

            if (m_HasInitialPlungerDistance)
            {
                var travelCm = Mathf.Max(0f, m_InitialPlungerToWingsDistanceCm - plungerToWingsCm);
                m_PlungerTravelNormalized = Mathf.Clamp01(travelCm / Mathf.Max(0.01f, m_MinPlungerTravelToCompleteCm));
                m_FlowRateMlPerSec = Mathf.Clamp(m_CurrentPlungerPushRateCmPerSec * 0.38f, 0f, 2.5f);
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

            var calibrationScore = (m_Tracker == null || m_Tracker.isMarkerCalibrated) ? 20f : 12f;
            var typeScore = m_SelectedInjectionType == InjectionType.None ? 0f : 10f;
            var fillScore = Mathf.Clamp01(1f - Mathf.Abs(m_FillAmountNormalized - m_TargetFillAmount)) * 12f;
            var bubbleScore = m_BubbleCheckCompleted ? 10f : 0f;
            var surfaceCleanScore = m_SurfaceCleanCompleted ? 8f : 0f;
            var angleMidpoint = 0.5f * (m_TargetInjectionAngleRange.x + m_TargetInjectionAngleRange.y);
            var angleScore = Mathf.Clamp01(1f - Mathf.Abs(m_InjectionAngleDegrees - angleMidpoint) / 25f) * 12f;
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

            if (nextStep == TutorialStep.InjectionAngle)
                m_AngleHoldProgress = 0f;

            if (nextStep == TutorialStep.InsertionSpeedFlowRate)
            {
                m_InsertionHoldProgress = 0f;
                m_InsertionStableHoldProgress = 0f;
                m_IsDispensePhase = false;
                m_HasCompletedInsertionDepth = false;
                m_HasStartedDispensePress = false;
                m_DispenseStopProgress = 0f;
                m_DispenseElapsedSeconds = 0f;
                m_CurrentInsertionDepthCm = 0f;
                m_EffectiveInsertionDepthTargetCm = m_MinInsertionDepthCm;
                m_CurrentLateralStabilityCmPerSec = 0f;
                m_CurrentPlungerPushRateCmPerSec = 0f;
                m_PlungerTravelNormalized = 0f;
                m_HasInitialPlungerDistance = false;
                m_HasPreviousNeedleTip = false;
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