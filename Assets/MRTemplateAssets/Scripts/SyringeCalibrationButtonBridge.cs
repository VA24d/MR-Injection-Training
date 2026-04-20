namespace UnityEngine.XR.Templates.MR
{
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
        bool m_DisableWristMenu;

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
        float m_StepElapsedSeconds;

        float m_AngleHoldProgress;
        float m_InsertionHoldProgress;
        float m_RemovalHoldProgress;
        Vector3 m_PreviousNeedleTip;
        bool m_HasPreviousNeedleTip;

        public TutorialStep currentStep => m_CurrentStep;
        public bool isTutorialRunning => m_IsTutorialRunning;
        public bool isFinished => m_IsFinished;
        public float fillAmountNormalized => m_FillAmountNormalized;
        public float finalScore => m_FinalScore;

        void Start()
        {
            ResolveReferences();

            if (m_DisableWristMenu)
                DisableWristMenu();
            else
                EnableWristMenu();

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

            GoToStep(TutorialStep.Start);
        }

        public void SetInjectionType(InjectionType type)
        {
            m_SelectedInjectionType = type;
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
            switch (m_CurrentStep)
            {
                case TutorialStep.Start:
                    GoToStep(TutorialStep.Calibration);
                    break;
                case TutorialStep.Calibration:
                    GoToStep(TutorialStep.InjectionType);
                    break;
                case TutorialStep.InjectionType:
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
                    FinalizeScore();
                    break;
            }
        }

        void TickStartStep()
        {
            if (m_AutoAdvanceStub && m_StepElapsedSeconds >= m_IntroDelaySeconds)
                GoToStep(TutorialStep.Calibration);
        }

        void TickCalibrationStep()
        {
            if (m_Tracker != null)
            {
                if (m_AutoAdvanceStub && !m_Tracker.isMarkerCalibrated && !m_Tracker.isCalibratingMarker)
                    m_Tracker.StartMarkerCalibration();

                if (m_Tracker.isMarkerCalibrated)
                {
                    GoToStep(TutorialStep.InjectionType);
                    return;
                }
            }

            if (m_AutoAdvanceStub && m_StepElapsedSeconds > 6f)
                GoToStep(TutorialStep.InjectionType);
        }

        void TickInjectionTypeStep()
        {
            if (m_SelectedInjectionType == InjectionType.None &&
                m_AutoAdvanceStub &&
                m_StepElapsedSeconds >= m_TypeSelectionDelaySeconds)
            {
                m_SelectedInjectionType = m_DefaultInjectionType;
            }

            if (m_SelectedInjectionType != InjectionType.None)
                GoToStep(TutorialStep.FillSyringe);
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
            var calibrationScore = (m_Tracker == null || m_Tracker.isMarkerCalibrated) ? 20f : 12f;
            var typeScore = m_SelectedInjectionType == InjectionType.None ? 0f : 10f;
            var fillScore = Mathf.Clamp01(1f - Mathf.Abs(m_FillAmountNormalized - m_TargetFillAmount)) * 15f;
            var bubbleScore = m_BubbleCheckCompleted ? 10f : 0f;
            var angleMidpoint = 0.5f * (m_TargetInjectionAngleRange.x + m_TargetInjectionAngleRange.y);
            var angleScore = Mathf.Clamp01(1f - Mathf.Abs(m_InjectionAngleDegrees - angleMidpoint) / 25f) * 15f;
            var insertionScore = Mathf.Clamp01(1f - Mathf.Abs(m_InsertionSpeedCmPerSec - m_TargetInsertionSpeedCmPerSec) / 4f) * 8f;
            var flowScore = Mathf.Clamp01(1f - Mathf.Abs(m_FlowRateMlPerSec - m_TargetFlowRateMlPerSec) / 0.7f) * 7f;
            var removeScore = Mathf.Clamp01(1f - Mathf.Abs(m_RemovalSpeedCmPerSec - m_TargetRemovalSpeedCmPerSec) / 4f) * 15f;

            m_FinalScore = Mathf.Clamp(
                calibrationScore +
                typeScore +
                fillScore +
                bubbleScore +
                angleScore +
                insertionScore +
                flowScore +
                removeScore,
                0f,
                100f);

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
        }
#endif
    }
}