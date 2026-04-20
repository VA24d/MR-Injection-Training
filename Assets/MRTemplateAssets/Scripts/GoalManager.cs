using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;
using LazyFollow = UnityEngine.XR.Interaction.Toolkit.UI.LazyFollow;

namespace UnityEngine.XR.Templates.MR
{
    public class GoalManager : MonoBehaviour
    {
        [Serializable]
        class Step
        {
            [SerializeField]
            public GameObject stepObject;

            [SerializeField]
            public string buttonText;

            public bool includeSkipButton;
        }

        [SerializeField]
        List<Step> m_StepList = new();

        [SerializeField]
        public TextMeshProUGUI m_StepButtonTextField;

        [SerializeField]
        public GameObject m_SkipButton;

        [SerializeField]
        GameObject m_LearnButton;

        [SerializeField]
        GameObject m_LearnModal;

        [SerializeField]
        Button m_LearnModalButton;

        [SerializeField]
        GameObject m_CoachingUIParent;

        [SerializeField]
        LazyFollow m_GoalPanelLazyFollow;

        [SerializeField]
        GameObject m_TapTooltip;

        [SerializeField]
        GameObject m_VideoPlayer;

        [SerializeField]
        Toggle m_VideoPlayerToggle;

        // Retained to preserve prefab/scene serialization compatibility.
        [SerializeField]
        ARFeatureController m_FeatureController;

        // Retained to preserve prefab/scene serialization compatibility.
        [SerializeField]
        ObjectSpawner m_ObjectSpawner;

        [SerializeField]
        Toggle m_PassthroughToggle;

        [SerializeField]
        FadeMaterial m_FadeMaterial;

        [Header("Injection tutorial")]
        [SerializeField]
        SyringeCalibrationButtonBridge m_InjectionTutorial;

        [SerializeField]
        SyringeOverlayTracker m_Tracker;

        [SerializeField]
        SyringePlaneAngleOverlay m_PlaneAngleOverlay;

        [Header("Coaching text")]
        [SerializeField]
        TextMeshProUGUI m_CoachingTitleText;

        [SerializeField]
        TextMeshProUGUI m_CoachingBodyText;

        [SerializeField]
        bool m_SyncCoachingText = true;

        [SerializeField]
        bool m_AutoHideLearnButton = true;

        [Header("Coaching action buttons")]
        [SerializeField]
        bool m_AddActionButtonsToCoachingMenu = true;

        [SerializeField]
        string m_CalibrateButtonName = "Calibrate Syringe Button";

        [SerializeField]
        string m_CreateSurfaceButtonName = "Create Surface Button";

        [SerializeField]
        string m_CalibrateIdleLabel = "Calibrate Syringe";

        [SerializeField]
        string m_CalibratingLabelPrefix = "Calibrating";

        [SerializeField]
        string m_CalibratedLabel = "Recalibrate Syringe";

        [SerializeField]
        string m_CreateSurfaceIdleLabel = "Create Surface";

        [SerializeField]
        string m_CreateSurfaceSelectingLabel = "Creating Surface";

        [SerializeField]
        string m_CreateSurfacePlacedLabel = "Recreate Surface";

        [SerializeField, Min(0f)]
        float m_ActionButtonVerticalSpacing = 12f;

        SyringeCalibrationButtonBridge.TutorialStep m_LastDisplayedStep;
        bool m_LastFinishedState;
        float m_LastScore = -1f;
        readonly List<TextMeshProUGUI> m_FallbackStepTexts = new();

        SurfaceSelectionTool m_SurfaceSelectionTool;
        GameObject m_CalibrateButtonObject;
        Button m_CalibrateButton;
        TextMeshProUGUI m_CalibrateButtonLabel;
        string m_LastCalibrateButtonText = string.Empty;
        GameObject m_CreateSurfaceButtonObject;
        Button m_CreateSurfaceButton;
        TextMeshProUGUI m_CreateSurfaceButtonLabel;
        string m_LastCreateSurfaceButtonText = string.Empty;

        Vector3 m_TargetOffset = new(-.5f, -.25f, 1.5f);

        void Start()
        {
            ResolveReferences();
            InitializeCoachingUI();
            SyncTutorialToUI(force: true);
        }

        void OnDestroy()
        {
            if (m_LearnButton != null)
            {
                var button = m_LearnButton.GetComponent<Button>();
                if (button != null)
                    button.onClick.RemoveListener(OpenModal);
            }

            if (m_LearnModalButton != null)
                m_LearnModalButton.onClick.RemoveListener(CloseModal);
        }

        void ResolveReferences()
        {
#if UNITY_2023_1_OR_NEWER
            if (m_InjectionTutorial == null)
                m_InjectionTutorial = FindAnyObjectByType<SyringeCalibrationButtonBridge>();

            if (m_Tracker == null)
                m_Tracker = FindAnyObjectByType<SyringeOverlayTracker>();

            if (m_SurfaceSelectionTool == null)
                m_SurfaceSelectionTool = FindAnyObjectByType<SurfaceSelectionTool>();

            if (m_PlaneAngleOverlay == null)
                m_PlaneAngleOverlay = FindAnyObjectByType<SyringePlaneAngleOverlay>();
#else
            if (m_InjectionTutorial == null)
                m_InjectionTutorial = FindObjectOfType<SyringeCalibrationButtonBridge>();

            if (m_Tracker == null)
                m_Tracker = FindObjectOfType<SyringeOverlayTracker>();

            if (m_SurfaceSelectionTool == null)
                m_SurfaceSelectionTool = FindObjectOfType<SurfaceSelectionTool>();

            if (m_PlaneAngleOverlay == null)
                m_PlaneAngleOverlay = FindObjectOfType<SyringePlaneAngleOverlay>();
#endif

            var host = m_InjectionTutorial != null ? m_InjectionTutorial.gameObject : gameObject;

            if (m_SurfaceSelectionTool == null)
            {
                m_SurfaceSelectionTool = host.GetComponent<SurfaceSelectionTool>();
                if (m_SurfaceSelectionTool == null)
                    m_SurfaceSelectionTool = host.AddComponent<SurfaceSelectionTool>();
            }

            if (m_PlaneAngleOverlay == null)
            {
                m_PlaneAngleOverlay = host.GetComponent<SyringePlaneAngleOverlay>();
                if (m_PlaneAngleOverlay == null)
                    m_PlaneAngleOverlay = host.AddComponent<SyringePlaneAngleOverlay>();
            }

            if (m_CoachingTitleText == null || m_CoachingBodyText == null)
                TryResolveFallbackTexts();
        }

        void InitializeCoachingUI()
        {
            if (m_CoachingUIParent != null)
                m_CoachingUIParent.transform.localScale = Vector3.one;

            if (m_TapTooltip != null)
                m_TapTooltip.SetActive(false);

            if (m_VideoPlayer != null)
                m_VideoPlayer.SetActive(false);

            if (m_VideoPlayerToggle != null)
                m_VideoPlayerToggle.isOn = false;

            if (m_LearnModal != null)
                m_LearnModal.transform.localScale = Vector3.zero;

            if (m_LearnButton != null)
            {
                var button = m_LearnButton.GetComponent<Button>();
                if (button != null)
                {
                    button.onClick.RemoveListener(OpenModal);
                    button.onClick.AddListener(OpenModal);
                }

                if (m_AutoHideLearnButton)
                    m_LearnButton.SetActive(false);
            }

            if (m_LearnModalButton != null)
            {
                m_LearnModalButton.onClick.RemoveListener(CloseModal);
                m_LearnModalButton.onClick.AddListener(CloseModal);
            }

            for (var i = 0; i < m_StepList.Count; i++)
            {
                if (m_StepList[i].stepObject != null)
                    m_StepList[i].stepObject.SetActive(i == 0);
            }

            if (m_SkipButton != null)
                m_SkipButton.SetActive(true);

            CreateOrBindActionButtons();
            RefreshActionButtons(force: true);
        }

        void TryResolveFallbackTexts()
        {
            m_FallbackStepTexts.Clear();

            if (m_CoachingUIParent == null)
                return;

            var candidates = m_CoachingUIParent.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var text in candidates)
            {
                if (text == null || text == m_StepButtonTextField)
                    continue;

                m_FallbackStepTexts.Add(text);
            }

            if (m_CoachingTitleText == null && m_FallbackStepTexts.Count > 0)
                m_CoachingTitleText = m_FallbackStepTexts[0];

            if (m_CoachingBodyText == null && m_FallbackStepTexts.Count > 1)
                m_CoachingBodyText = m_FallbackStepTexts[1];
        }

        void Update()
        {
            SyncTutorialToUI(force: false);
            RefreshActionButtons(force: false);

#if UNITY_EDITOR
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                ForceCompleteGoal();
#endif
        }

        void OpenModal()
        {
            if (m_LearnModal != null)
                m_LearnModal.transform.localScale = Vector3.one;
        }

        void CloseModal()
        {
            if (m_LearnModal != null)
                m_LearnModal.transform.localScale = Vector3.zero;
        }

        void CreateOrBindActionButtons()
        {
            if (!m_AddActionButtonsToCoachingMenu || m_SkipButton == null)
                return;

            var template = m_SkipButton;
            var parent = template.transform.parent;
            if (parent == null)
                return;

            var calibrateExisting = parent.Find(m_CalibrateButtonName);
            if (calibrateExisting == null)
            {
                m_CalibrateButtonObject = Instantiate(template, parent);
                m_CalibrateButtonObject.name = m_CalibrateButtonName;
            }
            else
            {
                m_CalibrateButtonObject = calibrateExisting.gameObject;
            }

            var surfaceExisting = parent.Find(m_CreateSurfaceButtonName);
            if (surfaceExisting == null)
            {
                m_CreateSurfaceButtonObject = Instantiate(template, parent);
                m_CreateSurfaceButtonObject.name = m_CreateSurfaceButtonName;
            }
            else
            {
                m_CreateSurfaceButtonObject = surfaceExisting.gameObject;
            }

            BindActionButton(
                m_CalibrateButtonObject,
                out m_CalibrateButton,
                out m_CalibrateButtonLabel,
                OnCalibrateSyringeButtonClicked);

            BindActionButton(
                m_CreateSurfaceButtonObject,
                out m_CreateSurfaceButton,
                out m_CreateSurfaceButtonLabel,
                OnCreateSurfaceButtonClicked);

            var baseIndex = template.transform.GetSiblingIndex();
            m_CalibrateButtonObject.transform.SetSiblingIndex(Mathf.Min(baseIndex + 1, parent.childCount - 1));
            m_CreateSurfaceButtonObject.transform.SetSiblingIndex(Mathf.Min(baseIndex + 2, parent.childCount - 1));

            OffsetActionButtonFromTemplate(template, m_CalibrateButtonObject, 1);
            OffsetActionButtonFromTemplate(template, m_CreateSurfaceButtonObject, 2);
        }

        static void BindActionButton(
            GameObject buttonObject,
            out Button button,
            out TextMeshProUGUI label,
            UnityEngine.Events.UnityAction onClick)
        {
            button = null;
            label = null;

            if (buttonObject == null)
                return;

            button = buttonObject.GetComponentInChildren<Button>(true);
            if (button != null)
            {
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(onClick);
            }

            label = buttonObject.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        void OffsetActionButtonFromTemplate(GameObject template, GameObject actionButton, int slot)
        {
            if (template == null || actionButton == null)
                return;

            if (!template.TryGetComponent<RectTransform>(out var templateRect) ||
                !actionButton.TryGetComponent<RectTransform>(out var actionRect))
                return;

            var basePos = templateRect.anchoredPosition;
            var verticalStep = Mathf.Max(templateRect.rect.height, 40f) + m_ActionButtonVerticalSpacing;
            actionRect.anchoredPosition = new Vector2(basePos.x, basePos.y - (verticalStep * slot));
        }

        void OnCalibrateSyringeButtonClicked()
        {
            if (m_Tracker == null)
                return;

            if (m_InjectionTutorial != null &&
                m_InjectionTutorial.currentStep == SyringeCalibrationButtonBridge.TutorialStep.Start)
            {
                m_InjectionTutorial.AdvanceStep();
            }

            if (m_Tracker.isCalibratingMarker)
            {
                m_Tracker.CancelMarkerCalibration();
            }
            else
            {
                if (m_Tracker.isMarkerCalibrated)
                    m_Tracker.ResetMarkerCalibration();

                m_Tracker.StartMarkerCalibration();
            }

            RefreshActionButtons(force: true);
            SyncTutorialToUI(force: true);
        }

        void OnCreateSurfaceButtonClicked()
        {
            if (m_SurfaceSelectionTool == null)
                return;

            if (m_SurfaceSelectionTool.isSelectingSurface)
                m_SurfaceSelectionTool.CancelSurfaceSelection();
            else
                m_SurfaceSelectionTool.BeginSurfaceSelection();

            RefreshActionButtons(force: true);
        }

        void RefreshActionButtons(bool force)
        {
            if (!m_AddActionButtonsToCoachingMenu)
                return;

            if (m_CalibrateButtonLabel != null)
            {
                var text = BuildCalibrateButtonLabel();
                if (force || !string.Equals(text, m_LastCalibrateButtonText, StringComparison.Ordinal))
                {
                    m_CalibrateButtonLabel.text = text;
                    m_LastCalibrateButtonText = text;
                }
            }

            if (m_CreateSurfaceButtonLabel != null)
            {
                var text = BuildCreateSurfaceButtonLabel();
                if (force || !string.Equals(text, m_LastCreateSurfaceButtonText, StringComparison.Ordinal))
                {
                    m_CreateSurfaceButtonLabel.text = text;
                    m_LastCreateSurfaceButtonText = text;
                }
            }
        }

        string BuildCalibrateButtonLabel()
        {
            if (m_Tracker == null)
                return m_CalibrateIdleLabel;

            if (m_Tracker.isCalibratingMarker)
                return $"{m_CalibratingLabelPrefix} ({m_Tracker.calibrationTapCount}/{m_Tracker.requiredCalibrationTaps})";

            if (m_Tracker.isMarkerCalibrated)
                return m_CalibratedLabel;

            return m_CalibrateIdleLabel;
        }

        string BuildCreateSurfaceButtonLabel()
        {
            if (m_SurfaceSelectionTool == null)
                return m_CreateSurfaceIdleLabel;

            if (m_SurfaceSelectionTool.isSelectingSurface)
                return m_CreateSurfaceSelectingLabel;

            if (m_SurfaceSelectionTool.hasPlacedSurface)
                return m_CreateSurfacePlacedLabel;

            return m_CreateSurfaceIdleLabel;
        }

        void SyncTutorialToUI(bool force)
        {
            if (m_InjectionTutorial == null)
                return;

            var step = m_InjectionTutorial.currentStep;
            var finished = m_InjectionTutorial.isFinished;
            var score = m_InjectionTutorial.finalScore;

            if (!force && step == m_LastDisplayedStep && finished == m_LastFinishedState && Mathf.Approximately(score, m_LastScore))
                return;

            m_LastDisplayedStep = step;
            m_LastFinishedState = finished;
            m_LastScore = score;

            var stepIndex = GetFlowIndex(step);
            var stepTitle = GetFlowTitle(step, stepIndex);
            var stepBody = BuildFlowDescription(step);

            var activePanelIndex = UpdateStepPanels(stepIndex);

            if (m_SyncCoachingText)
            {
                if (m_CoachingTitleText != null)
                    m_CoachingTitleText.text = stepTitle;

                if (m_CoachingBodyText != null)
                    m_CoachingBodyText.text = stepBody;

                ApplyTextToActiveStepPanel(activePanelIndex, stepTitle, stepBody);
            }

            if (m_StepButtonTextField != null)
                m_StepButtonTextField.text = GetActionButtonLabel(step, finished, score);

            if (m_SkipButton != null)
                m_SkipButton.SetActive(!finished);

            if (m_GoalPanelLazyFollow != null)
                m_GoalPanelLazyFollow.positionFollowMode = LazyFollow.PositionFollowMode.Follow;

            if (m_PassthroughToggle != null)
                m_PassthroughToggle.SetIsOnWithoutNotify(true);

            if (m_FadeMaterial != null)
                m_FadeMaterial.FadeSkybox(true);
        }

        int UpdateStepPanels(int stepIndex)
        {
            if (m_StepList.Count == 0)
                return -1;

            var panelIndex = Mathf.Clamp(stepIndex, 0, m_StepList.Count - 1);
            for (var i = 0; i < m_StepList.Count; i++)
            {
                var panel = m_StepList[i].stepObject;
                if (panel != null)
                    panel.SetActive(i == panelIndex);
            }

            return panelIndex;
        }

        void ApplyTextToActiveStepPanel(int panelIndex, string title, string body)
        {
            if (panelIndex < 0 || panelIndex >= m_StepList.Count)
                return;

            var panel = m_StepList[panelIndex].stepObject;
            if (panel == null)
                return;

            var texts = panel.GetComponentsInChildren<TextMeshProUGUI>(true);
            TextMeshProUGUI first = null;
            TextMeshProUGUI second = null;

            for (var i = 0; i < texts.Length; i++)
            {
                if (texts[i] == null || texts[i] == m_StepButtonTextField)
                    continue;

                if (first == null)
                {
                    first = texts[i];
                    continue;
                }

                second = texts[i];
                break;
            }

            if (first != null)
                first.text = title;

            if (second != null)
                second.text = body;
        }

        int GetFlowIndex(SyringeCalibrationButtonBridge.TutorialStep step)
        {
            return step switch
            {
                SyringeCalibrationButtonBridge.TutorialStep.Start => 0,
                SyringeCalibrationButtonBridge.TutorialStep.Calibration => 1,
                SyringeCalibrationButtonBridge.TutorialStep.InjectionType => 2,
                SyringeCalibrationButtonBridge.TutorialStep.FillSyringe => 3,
                SyringeCalibrationButtonBridge.TutorialStep.BubbleCheckManual => 4,
                SyringeCalibrationButtonBridge.TutorialStep.InjectionAngle => 5,
                SyringeCalibrationButtonBridge.TutorialStep.InsertionSpeedFlowRate => 6,
                SyringeCalibrationButtonBridge.TutorialStep.RemoveSpeed => 7,
                SyringeCalibrationButtonBridge.TutorialStep.FinalScore => 8,
                _ => 0,
            };
        }

        string GetFlowTitle(SyringeCalibrationButtonBridge.TutorialStep step, int stepIndex)
        {
            var label = step switch
            {
                SyringeCalibrationButtonBridge.TutorialStep.Start => "Start",
                SyringeCalibrationButtonBridge.TutorialStep.Calibration => "Calibration",
                SyringeCalibrationButtonBridge.TutorialStep.InjectionType => "Injection Type",
                SyringeCalibrationButtonBridge.TutorialStep.FillSyringe => "Fill Syringe",
                SyringeCalibrationButtonBridge.TutorialStep.BubbleCheckManual => "Bubble Check",
                SyringeCalibrationButtonBridge.TutorialStep.InjectionAngle => "Injection Angle",
                SyringeCalibrationButtonBridge.TutorialStep.InsertionSpeedFlowRate => "Insertion + Flow Rate",
                SyringeCalibrationButtonBridge.TutorialStep.RemoveSpeed => "Remove Speed",
                SyringeCalibrationButtonBridge.TutorialStep.FinalScore => "Final Score",
                _ => "Injection Flow",
            };

            return $"Step {stepIndex + 1}/9 - {label}";
        }

        string BuildFlowDescription(SyringeCalibrationButtonBridge.TutorialStep step)
        {
            switch (step)
            {
                case SyringeCalibrationButtonBridge.TutorialStep.Start:
                    return "Initialize the injection tutorial and prepare hand tracking.";

                case SyringeCalibrationButtonBridge.TutorialStep.Calibration:
                    if (m_Tracker != null && m_Tracker.isCalibratingMarker)
                        return $"Calibrating syringe marker ({m_Tracker.calibrationTapCount}/{m_Tracker.requiredCalibrationTaps}).";

                    if (m_Tracker != null && m_Tracker.isMarkerCalibrated)
                        return "Syringe marker calibration complete.";

                    return "Calibrate syringe with hand overlay and marker calibration.";

                case SyringeCalibrationButtonBridge.TutorialStep.InjectionType:
                    return "Select the injection type (IM, SC, ID, or IV).";

                case SyringeCalibrationButtonBridge.TutorialStep.FillSyringe:
                    return $"Fill syringe and track amount: {(m_InjectionTutorial.fillAmountNormalized * 100f):F0}%";

                case SyringeCalibrationButtonBridge.TutorialStep.BubbleCheckManual:
                    return "Perform manual bubble check before injection.";

                case SyringeCalibrationButtonBridge.TutorialStep.InjectionAngle:
                    return "Align and hold the injection angle target.";

                case SyringeCalibrationButtonBridge.TutorialStep.InsertionSpeedFlowRate:
                    return "Maintain insertion speed and flow rate targets.";

                case SyringeCalibrationButtonBridge.TutorialStep.RemoveSpeed:
                    return "Remove syringe at controlled speed.";

                case SyringeCalibrationButtonBridge.TutorialStep.FinalScore:
                    return $"Tutorial complete. Final score: {m_InjectionTutorial.finalScore:F1}/100";

                default:
                    return string.Empty;
            }
        }

        string GetActionButtonLabel(SyringeCalibrationButtonBridge.TutorialStep step, bool finished, float score)
        {
            if (finished || step == SyringeCalibrationButtonBridge.TutorialStep.FinalScore)
                return $"Score {score:F1}/100";

            return step switch
            {
                SyringeCalibrationButtonBridge.TutorialStep.Start => "Begin",
                SyringeCalibrationButtonBridge.TutorialStep.Calibration => "Calibrating...",
                SyringeCalibrationButtonBridge.TutorialStep.InjectionType => "Select Type",
                SyringeCalibrationButtonBridge.TutorialStep.FillSyringe => "Filling...",
                SyringeCalibrationButtonBridge.TutorialStep.BubbleCheckManual => "Bubble Check",
                SyringeCalibrationButtonBridge.TutorialStep.InjectionAngle => "Hold Angle",
                SyringeCalibrationButtonBridge.TutorialStep.InsertionSpeedFlowRate => "Speed + Flow",
                SyringeCalibrationButtonBridge.TutorialStep.RemoveSpeed => "Remove",
                _ => "Continue",
            };
        }

        public void ForceCompleteGoal()
        {
            if (m_InjectionTutorial == null)
                return;

            if (m_InjectionTutorial.currentStep == SyringeCalibrationButtonBridge.TutorialStep.BubbleCheckManual)
                m_InjectionTutorial.MarkBubbleCheckCompleted();

            m_InjectionTutorial.AdvanceStep();
            SyncTutorialToUI(force: true);
        }

        public void ForceEndAllGoals()
        {
            if (m_InjectionTutorial == null)
                return;

            var guard = 0;
            while (!m_InjectionTutorial.isFinished && guard < 16)
            {
                m_InjectionTutorial.AdvanceStep();
                guard++;
            }

            SyncTutorialToUI(force: true);
        }

        public void ResetCoaching()
        {
            if (m_InjectionTutorial != null)
                m_InjectionTutorial.BeginTutorial();

            if (m_CoachingUIParent != null)
                m_CoachingUIParent.transform.localScale = Vector3.one;

            if (m_LearnModal != null)
                m_LearnModal.transform.localScale = Vector3.zero;

            if (m_VideoPlayer != null)
                m_VideoPlayer.SetActive(false);

            if (m_VideoPlayerToggle != null)
                m_VideoPlayerToggle.isOn = false;

            SyncTutorialToUI(force: true);
        }

        public void TooglePlayer(bool visibility)
        {
            if (visibility)
                TurnOnVideoPlayer();
            else if (m_VideoPlayer != null && m_VideoPlayer.activeSelf)
                m_VideoPlayer.SetActive(false);
        }

        void TurnOnVideoPlayer()
        {
            if (m_VideoPlayer == null || m_VideoPlayer.activeSelf)
                return;

            var follow = m_VideoPlayer.GetComponent<LazyFollow>();
            if (follow != null)
                follow.rotationFollowMode = LazyFollow.RotationFollowMode.None;

            var target = Camera.main != null ? Camera.main.transform : null;
            if (target == null)
            {
                m_VideoPlayer.SetActive(true);
                return;
            }

            var targetRotation = target.rotation;
            var targetEuler = targetRotation.eulerAngles;
            targetRotation = Quaternion.Euler(0f, targetEuler.y, targetEuler.z);

            target.rotation = targetRotation;
            var targetPosition = target.position + target.TransformVector(m_TargetOffset);
            m_VideoPlayer.transform.position = targetPosition;

            var forward = target.position - m_VideoPlayer.transform.position;
            var targetPlayerRotation = forward.sqrMagnitude > float.Epsilon
                ? Quaternion.LookRotation(forward, Vector3.up)
                : Quaternion.identity;

            targetPlayerRotation *= Quaternion.Euler(new Vector3(0f, 180f, 0f));
            var targetPlayerEuler = targetPlayerRotation.eulerAngles;
            var currentEuler = m_VideoPlayer.transform.rotation.eulerAngles;
            targetPlayerRotation = Quaternion.Euler(currentEuler.x, targetPlayerEuler.y, currentEuler.z);

            m_VideoPlayer.transform.rotation = targetPlayerRotation;
            m_VideoPlayer.SetActive(true);

            if (follow != null)
                follow.rotationFollowMode = LazyFollow.RotationFollowMode.LookAtWithWorldUp;
        }

        // Legacy names kept to avoid broken serialized callbacks.
        void CompleteGoal() => ForceCompleteGoal();
        void ProcessGoals() => SyncTutorialToUI(force: false);
        void DisableTooltips()
        {
            if (m_TapTooltip != null)
                m_TapTooltip.SetActive(false);
        }

        IEnumerator TurnOnPlanes(bool _)
        {
            yield break;
        }

        IEnumerator TurnOnARFeatures()
        {
            yield break;
        }

        void TurnOffARFeatureVisualization()
        {
        }

        void OnObjectSpawned(GameObject _)
        {
            ForceCompleteGoal();
        }
    }
}
