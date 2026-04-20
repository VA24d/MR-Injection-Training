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
            Start,
            Calibration,
            InjectionType,
            FillSyringe,
            BubbleCheckManual,
            InjectionAngle,
            InsertionSpeedFlowRate,
            RemoveSpeed,
            FinalScore,
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

        [SerializeField]
        Vector2 m_TargetInjectionAngleRange = new Vector2(25f, 55f);

        [SerializeField, Min(0.1f)]
        float m_AngleHoldSeconds = 0.8f;

        [SerializeField, Min(0.1f)]
        float m_TargetInsertionSpeedCmPerSec = 3.5f;

        [SerializeField, Min(0.1f)]
        float m_TargetFlowRateMlPerSec = 0.6f;

        [SerializeField, Min(0.1f)]
        float m_TargetRemovalSpeedCmPerSec = 4.5f;

        [SerializeField, Min(0.1f)]
        float m_SpeedHoldSeconds = 0.8f;

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
        float m_InjectionAngleDegrees;

        [SerializeField]
        float m_InsertionSpeedCmPerSec;

        [SerializeField]
        float m_FlowRateMlPerSec;

        [SerializeField]
        float m_RemovalSpeedCmPerSec;

        [SerializeField, Range(0f, 100f)]
        float m_FinalScore;

        [SerializeField]
        ScoreBreakdown m_LastScoreBreakdown;

        [SerializeField]
        float m_StepElapsedSeconds;

        float m_AngleHoldProgress;
        float m_InsertionHoldProgress;
        float m_RemovalHoldProgress;
        Vector3 m_PreviousNeedleTip;
        bool m_HasPreviousNeedleTip;

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
        public bool bubbleCheckCompleted => m_BubbleCheckCompleted;

        public float angleHoldProgressNormalized =>
            m_AngleHoldSeconds > 0.01f ? Mathf.Clamp01(m_AngleHoldProgress / m_AngleHoldSeconds) : 0f;

        public float insertionFlowHoldProgressNormalized =>
            m_SpeedHoldSeconds > 0.01f ? Mathf.Clamp01(m_InsertionHoldProgress / m_SpeedHoldSeconds) : 0f;

        public float removalHoldProgressNormalized =>
            m_SpeedHoldSeconds > 0.01f ? Mathf.Clamp01(m_RemovalHoldProgress / m_SpeedHoldSeconds) : 0f;

        public Vector2 targetInjectionAngleRange => m_TargetInjectionAngleRange;
        public float targetInsertionSpeedCmPerSec => m_TargetInsertionSpeedCmPerSec;
        public float targetFlowRateMlPerSec => m_TargetFlowRateMlPerSec;
        public float targetRemovalSpeedCmPerSec => m_TargetRemovalSpeedCmPerSec;
        public float targetFillAmountNormalized => m_TargetFillAmount;

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
            m_IsTutorialRunning = true;
            m_IsFinished = false;
            m_FinalScore = 0f;
            m_SelectedInjectionType = InjectionType.None;
            m_FillAmountNormalized = 0f;
            m_BubbleCheckCompleted = false;
            m_InjectionAngleDegrees = 0f;
            m_InsertionSpeedCmPerSec = 0f;
            m_FlowRateMlPerSec = 0f;
            m_RemovalSpeedCmPerSec = 0f;
            m_HasPreviousNeedleTip = false;
            m_LastScoreBreakdown = default;

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
                GoToStep(TutorialStep.InjectionAngle);
        }

        void TickInjectionAngleStep()
        {
            var hasTracking = TryUpdatePoseMetrics();
            if (!hasTracking && m_AutoAdvanceStub)
            {
                m_InjectionAngleDegrees = Mathf.Lerp(m_InjectionAngleDegrees, 40f, 4f * Time.deltaTime);
            }

            var isWithinRange =
                m_InjectionAngleDegrees >= m_TargetInjectionAngleRange.x &&
                m_InjectionAngleDegrees <= m_TargetInjectionAngleRange.y;

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
                m_InsertionSpeedCmPerSec = Mathf.Lerp(m_InsertionSpeedCmPerSec, m_TargetInsertionSpeedCmPerSec, 3f * Time.deltaTime);
                m_FlowRateMlPerSec = Mathf.Lerp(m_FlowRateMlPerSec, m_TargetFlowRateMlPerSec, 3f * Time.deltaTime);
            }

            var insertionOk = Mathf.Abs(m_InsertionSpeedCmPerSec - m_TargetInsertionSpeedCmPerSec) <= 1.5f;
            var flowOk = Mathf.Abs(m_FlowRateMlPerSec - m_TargetFlowRateMlPerSec) <= 0.25f;

            if (insertionOk && flowOk)
                m_InsertionHoldProgress += Time.deltaTime;
            else
                m_InsertionHoldProgress = 0f;

            if (m_InsertionHoldProgress >= m_SpeedHoldSeconds)
                GoToStep(TutorialStep.RemoveSpeed);
        }

        void TickRemovalSpeedStep()
        {
            var hasTracking = TryUpdatePoseMetrics();
            if (!hasTracking && m_AutoAdvanceStub)
            {
                m_RemovalSpeedCmPerSec = Mathf.Lerp(m_RemovalSpeedCmPerSec, m_TargetRemovalSpeedCmPerSec, 3f * Time.deltaTime);
            }

            var removeOk = Mathf.Abs(m_RemovalSpeedCmPerSec - m_TargetRemovalSpeedCmPerSec) <= 1.5f;
            if (removeOk)
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

            m_InjectionAngleDegrees = Vector3.Angle(pose.forward, Vector3.down);

            if (m_HasPreviousNeedleTip)
            {
                var needleVelocity = (pose.needleTip - m_PreviousNeedleTip) / dt;
                var forwardSpeedCmPerSec = Vector3.Dot(needleVelocity, pose.forward) * 100f;

                if (m_CurrentStep == TutorialStep.InsertionSpeedFlowRate)
                {
                    m_InsertionSpeedCmPerSec = Mathf.Max(0f, forwardSpeedCmPerSec);
                    m_FlowRateMlPerSec = Mathf.Clamp(m_InsertionSpeedCmPerSec * 0.15f, 0f, 1.5f);
                }
                else if (m_CurrentStep == TutorialStep.RemoveSpeed)
                {
                    m_RemovalSpeedCmPerSec = Mathf.Max(0f, -forwardSpeedCmPerSec);
                }
            }

            m_PreviousNeedleTip = pose.needleTip;
            m_HasPreviousNeedleTip = true;
            return true;
        }

        void FinalizeScore()
        {
            if (m_IsFinished)
                return;

            var calibrationScore = (m_Tracker == null || m_Tracker.isMarkerCalibrated) ? 20f : 12f;
            var typeScore = m_SelectedInjectionType == InjectionType.None ? 0f : 10f;
            var fillScore = Mathf.Clamp01(1f - Mathf.Abs(m_FillAmountNormalized - m_TargetFillAmount)) * 15f;
            var bubbleScore = m_BubbleCheckCompleted ? 10f : 0f;
            var angleMidpoint = 0.5f * (m_TargetInjectionAngleRange.x + m_TargetInjectionAngleRange.y);
            var angleScore = Mathf.Clamp01(1f - Mathf.Abs(m_InjectionAngleDegrees - angleMidpoint) / 25f) * 15f;
            var insertionScore = Mathf.Clamp01(1f - Mathf.Abs(m_InsertionSpeedCmPerSec - m_TargetInsertionSpeedCmPerSec) / 4f) * 8f;
            var flowScore = Mathf.Clamp01(1f - Mathf.Abs(m_FlowRateMlPerSec - m_TargetFlowRateMlPerSec) / 0.7f) * 7f;
            var removeScore = Mathf.Clamp01(1f - Mathf.Abs(m_RemovalSpeedCmPerSec - m_TargetRemovalSpeedCmPerSec) / 4f) * 15f;

            var sum = calibrationScore + typeScore + fillScore + bubbleScore + angleScore + insertionScore + flowScore + removeScore;
            m_FinalScore = Mathf.Clamp(sum, 0f, 100f);

            m_LastScoreBreakdown = new ScoreBreakdown
            {
                calibration = calibrationScore,
                injectionType = typeScore,
                fill = fillScore,
                bubble = bubbleScore,
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
                m_InsertionHoldProgress = 0f;

            if (nextStep == TutorialStep.RemoveSpeed)
                m_RemovalHoldProgress = 0f;

            if (nextStep == TutorialStep.FinalScore)
                FinalizeScore();

            Debug.Log($"[Injection Tutorial Stub] Step: {nextStep}", this);
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

            if (Input.GetKeyDown(KeyCode.N))
                AdvanceStep();
#endif
        }
#endif
    }
}