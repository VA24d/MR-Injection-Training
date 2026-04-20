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
        [Tooltip("Optional. When set (or present on m_LearnModal), open/close uses alpha and raycast blocking instead of scaling the modal to zero.")]
        CanvasGroup m_LearnModalCanvasGroup;

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

        [SerializeField]
        HandOverlaySkeletonToggleBridge m_HandOverlayBridge;

        [Header("Coaching text")]
        [SerializeField]
        TextMeshProUGUI m_CoachingTitleText;

        [SerializeField]
        TextMeshProUGUI m_CoachingBodyText;

        [SerializeField]
        bool m_SyncCoachingText = true;

        [SerializeField]
        bool m_AutoHideLearnButton = true;

        [Header("Coaching panel placement")]
        [SerializeField]
        [Tooltip("When true, every tutorial UI sync forces LazyFollow back on — the panel keeps sliding with your head. Turn off so it only moves when you grab it (or use Pin/Resume below).")]
        bool m_ReapplyHeadFollowOnTutorialSync = false;

        [SerializeField]
        [Tooltip("On startup, attach the coaching UI to head follow. Off = world-anchored at scene pose until ResumeCoachingPanelHeadFollow() or reapply is enabled.")]
        bool m_StartWithCoachingHeadFollow = false;

        [Header("Coaching action buttons")]
        [SerializeField]
        bool m_AddActionButtonsToCoachingMenu = true;

        [SerializeField]
        string m_CalibrateButtonName = "Calibrate Syringe Button";

        [SerializeField]
        string m_CreateSurfaceButtonName = "Create Surface Button";

        [SerializeField]
        string m_SkeletonToggleButtonName = "Skeleton Toggle Button";

        [SerializeField]
        string m_PreviousStepButtonName = "Previous Step Button";

        [SerializeField]
        string m_NextStepButtonName = "Next Step Button";

        [SerializeField]
        string m_SwapHandsButtonName = "Swap Hands Button";

        [SerializeField]
        string m_InjectionTypeButtonName = "Injection Type Button";

        [SerializeField]
        string m_DemoVideoButtonName = "Demo Video Button";

        [SerializeField]
        string m_DemoVideoCaption = "Demo Video";

        [SerializeField]
        [Tooltip("When true, Calibrate / Surface / Skeleton / etc. are shown only on steps where they apply.")]
        bool m_ShowActionButtonsForCurrentStepOnly = true;

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

        [SerializeField]
        string m_ShowSkeletonLabel = "Show Skeleton";

        [SerializeField]
        string m_HideSkeletonLabel = "Hide Skeleton";

        [SerializeField]
        string m_PreviousStepLabel = "Previous Step";

        [SerializeField]
        string m_NextStepLabel = "Next Step";

        [SerializeField]
        string m_UseLeftHandLabel = "Use Left Hand";

        [SerializeField]
        string m_UseRightHandLabel = "Use Right Hand";

        [SerializeField]
        string m_InjectionTypeLabelPrefix = "Injection Type";

        [SerializeField, Min(0f)]
        float m_ActionButtonVerticalSpacing = 6f;

        [SerializeField, Min(0f)]
        float m_NavigationRowPairOffset = 78f;

        [SerializeField, Min(0f)]
        float m_CoachingCardExtraHeightForActions = 220f;

        [Header("Coaching metrics HUD")]
        [SerializeField]
        [Tooltip("Live / mock metrics band between the modal and the action buttons.")]
        bool m_ShowCoachingMetricsHud = false;

        [SerializeField, Min(4f)]
        float m_MetricsHudGapPx = 10f;

        [SerializeField, Min(24f)]
        float m_MetricsHudMinHeight = 52f;

        [SerializeField, Min(40f)]
        float m_MetricsHudMaxHeight = 200f;

        RectTransform m_MetricsHudRoot;
        TextMeshProUGUI m_MetricsHudText;
        RectTransform m_CachedModalRt;

        SyringeCalibrationButtonBridge.TutorialStep m_LastDisplayedStep;
        bool m_LastFinishedState;
        float m_LastScore = -1f;
        float m_LastSyncedFillNormalized = float.NaN;
        int m_LastSyncedCalibrationTaps = int.MinValue;
        SyringeCalibrationButtonBridge.InjectionType m_LastSyncedInjectionType;
        string m_LastScoreBreakdownSnapshot = string.Empty;

        /// <summary>
        /// <see cref="ApplyTutorialEnvironmentEffects"/> must not run on every UI sync — e.g. cycling injection type
        /// re-ran passthrough/skybox and felt like the view &quot;mode&quot; broke.
        /// </summary>
        SyringeCalibrationButtonBridge.TutorialStep? m_LastTutorialStepForEnvironmentEffects;

        ActionButtonSnapshot m_LastActionButtonSnapshot;
        readonly List<TextMeshProUGUI> m_FallbackStepTexts = new();

        struct ActionButtonSnapshot : IEquatable<ActionButtonSnapshot>
        {
            public int CalibrationTapCount;
            public bool IsCalibratingMarker;
            public bool IsMarkerCalibrated;
            public bool IsSelectingSurface;
            public bool HasPlacedSurface;
            public bool SkeletonVisible;
            public bool IsTrackingLeftHand;
            public SyringeCalibrationButtonBridge.InjectionType InjectionType;
            public SyringeCalibrationButtonBridge.TutorialStep Step;

            public bool Equals(ActionButtonSnapshot other)
            {
                return CalibrationTapCount == other.CalibrationTapCount &&
                       IsCalibratingMarker == other.IsCalibratingMarker &&
                       IsMarkerCalibrated == other.IsMarkerCalibrated &&
                       IsSelectingSurface == other.IsSelectingSurface &&
                       HasPlacedSurface == other.HasPlacedSurface &&
                       SkeletonVisible == other.SkeletonVisible &&
                       IsTrackingLeftHand == other.IsTrackingLeftHand &&
                       InjectionType == other.InjectionType &&
                       Step == other.Step;
            }
        }

        SurfaceSelectionTool m_SurfaceSelectionTool;
        GameObject m_CalibrateButtonObject;
        Button m_CalibrateButton;
        TextMeshProUGUI m_CalibrateButtonLabel;
        string m_LastCalibrateButtonText = string.Empty;
        GameObject m_CreateSurfaceButtonObject;
        Button m_CreateSurfaceButton;
        TextMeshProUGUI m_CreateSurfaceButtonLabel;
        string m_LastCreateSurfaceButtonText = string.Empty;
        GameObject m_SkeletonToggleButtonObject;
        Button m_SkeletonToggleButton;
        TextMeshProUGUI m_SkeletonToggleButtonLabel;
        string m_LastSkeletonToggleButtonText = string.Empty;
        GameObject m_PreviousStepButtonObject;
        Button m_PreviousStepButton;
        TextMeshProUGUI m_PreviousStepButtonLabel;
        string m_LastPreviousStepButtonText = string.Empty;
        GameObject m_NextStepButtonObject;
        Button m_NextStepButton;
        TextMeshProUGUI m_NextStepButtonLabel;
        string m_LastNextStepButtonText = string.Empty;
        GameObject m_SwapHandsButtonObject;
        Button m_SwapHandsButton;
        TextMeshProUGUI m_SwapHandsButtonLabel;
        string m_LastSwapHandsButtonText = string.Empty;
        GameObject m_InjectionTypeButtonObject;
        Button m_InjectionTypeButton;
        TextMeshProUGUI m_InjectionTypeButtonLabel;
        string m_LastInjectionTypeButtonText = string.Empty;
        GameObject m_DemoVideoButtonObject;
        Button m_DemoVideoButton;
        TextMeshProUGUI m_DemoVideoLabel;
        string m_LastDemoVideoCaption = string.Empty;

        RectTransform m_CoachingCardRoot;
        Vector2 m_CoachingCardBaseSize;
        bool m_CoachingCardSizeCaptured;

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

            if (m_HandOverlayBridge == null)
                m_HandOverlayBridge = FindAnyObjectByType<HandOverlaySkeletonToggleBridge>();
#else
            if (m_InjectionTutorial == null)
                m_InjectionTutorial = FindObjectOfType<SyringeCalibrationButtonBridge>();

            if (m_Tracker == null)
                m_Tracker = FindObjectOfType<SyringeOverlayTracker>();

            if (m_SurfaceSelectionTool == null)
                m_SurfaceSelectionTool = FindObjectOfType<SurfaceSelectionTool>();

            if (m_PlaneAngleOverlay == null)
                m_PlaneAngleOverlay = FindObjectOfType<SyringePlaneAngleOverlay>();

            if (m_HandOverlayBridge == null)
                m_HandOverlayBridge = FindObjectOfType<HandOverlaySkeletonToggleBridge>();
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

            EnsureLearnModalCanvasGroup();
        }

        void EnsureLearnModalCanvasGroup()
        {
            if (m_LearnModal == null)
                return;

            if (m_LearnModalCanvasGroup == null)
                m_LearnModal.TryGetComponent(out m_LearnModalCanvasGroup);
        }

        void SetLearnModalOpen(bool open)
        {
            if (m_LearnModal == null)
                return;

            EnsureLearnModalCanvasGroup();

            if (m_LearnModalCanvasGroup != null)
            {
                m_LearnModalCanvasGroup.alpha = open ? 1f : 0f;
                m_LearnModalCanvasGroup.interactable = open;
                m_LearnModalCanvasGroup.blocksRaycasts = open;
                m_LearnModal.SetActive(true);
            }
            else
            {
                m_LearnModal.transform.localScale = open ? Vector3.one : Vector3.zero;
            }
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

            SetLearnModalOpen(false);

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
                m_SkipButton.SetActive(!m_AddActionButtonsToCoachingMenu);

            CreateOrBindActionButtons();
            RefreshActionButtons(force: true);
            EnsureCoachingMetricsHud();
            ApplyInitialCoachingPanelPlacement();
        }

        void ApplyInitialCoachingPanelPlacement()
        {
            if (m_GoalPanelLazyFollow == null)
                return;

            if (m_StartWithCoachingHeadFollow)
            {
                if (m_GoalPanelLazyFollow.TryGetComponent<FloatingCoachingUIGrab>(out var grab))
                    grab.ResumeFollowingHead();
                else
                {
                    m_GoalPanelLazyFollow.positionFollowMode = LazyFollow.PositionFollowMode.Follow;
                    m_GoalPanelLazyFollow.rotationFollowMode = LazyFollow.RotationFollowMode.LookAtWithWorldUp;
                }
            }
            else if (m_GoalPanelLazyFollow.TryGetComponent<FloatingCoachingUIGrab>(out var grab))
            {
                grab.PinToWorldSpace();
            }
            else
            {
                m_GoalPanelLazyFollow.positionFollowMode = LazyFollow.PositionFollowMode.None;
                m_GoalPanelLazyFollow.rotationFollowMode = LazyFollow.RotationFollowMode.None;
            }
        }

        /// <summary>Re-attach the coaching panel to head/camera LazyFollow (e.g. bind to a \"Follow view\" UI button).</summary>
        public void ResumeCoachingPanelHeadFollow()
        {
            if (m_GoalPanelLazyFollow != null &&
                m_GoalPanelLazyFollow.TryGetComponent<FloatingCoachingUIGrab>(out var grab))
                grab.ResumeFollowingHead();
        }

        /// <summary>Stop head follow and keep the panel in world space (e.g. after placing it by hand).</summary>
        public void PinCoachingPanelToWorldSpace()
        {
            if (m_GoalPanelLazyFollow != null &&
                m_GoalPanelLazyFollow.TryGetComponent<FloatingCoachingUIGrab>(out var grab))
                grab.PinToWorldSpace();
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

        void LateUpdate()
        {
            if (!m_ShowCoachingMetricsHud || m_InjectionTutorial == null || m_MetricsHudText == null)
                return;

            var step = m_InjectionTutorial.currentStep;
            if (!CoachingMetricsStepNeedsPerFrameText(step))
                return;

            m_MetricsHudText.text = BuildCoachingMetricsHudText(step);
        }

        void OpenModal()
        {
            SetLearnModalOpen(true);
        }

        void CloseModal()
        {
            SetLearnModalOpen(false);
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

            var skeletonExisting = parent.Find(m_SkeletonToggleButtonName);
            if (skeletonExisting == null)
            {
                m_SkeletonToggleButtonObject = Instantiate(template, parent);
                m_SkeletonToggleButtonObject.name = m_SkeletonToggleButtonName;
            }
            else
            {
                m_SkeletonToggleButtonObject = skeletonExisting.gameObject;
            }

            var previousExisting = parent.Find(m_PreviousStepButtonName);
            if (previousExisting == null)
            {
                m_PreviousStepButtonObject = Instantiate(template, parent);
                m_PreviousStepButtonObject.name = m_PreviousStepButtonName;
            }
            else
            {
                m_PreviousStepButtonObject = previousExisting.gameObject;
            }

            var nextExisting = parent.Find(m_NextStepButtonName);
            if (nextExisting == null)
            {
                m_NextStepButtonObject = Instantiate(template, parent);
                m_NextStepButtonObject.name = m_NextStepButtonName;
            }
            else
            {
                m_NextStepButtonObject = nextExisting.gameObject;
            }

            var swapHandsExisting = parent.Find(m_SwapHandsButtonName);
            if (swapHandsExisting == null)
            {
                m_SwapHandsButtonObject = Instantiate(template, parent);
                m_SwapHandsButtonObject.name = m_SwapHandsButtonName;
            }
            else
            {
                m_SwapHandsButtonObject = swapHandsExisting.gameObject;
            }

            var injectionTypeExisting = parent.Find(m_InjectionTypeButtonName);
            if (injectionTypeExisting == null)
            {
                m_InjectionTypeButtonObject = Instantiate(template, parent);
                m_InjectionTypeButtonObject.name = m_InjectionTypeButtonName;
            }
            else
            {
                m_InjectionTypeButtonObject = injectionTypeExisting.gameObject;
            }

            var demoVideoExisting = parent.Find(m_DemoVideoButtonName);
            if (demoVideoExisting == null)
            {
                m_DemoVideoButtonObject = Instantiate(template, parent);
                m_DemoVideoButtonObject.name = m_DemoVideoButtonName;
            }
            else
            {
                m_DemoVideoButtonObject = demoVideoExisting.gameObject;
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

            BindActionButton(
                m_SkeletonToggleButtonObject,
                out m_SkeletonToggleButton,
                out m_SkeletonToggleButtonLabel,
                OnSkeletonToggleButtonClicked);

            BindActionButton(
                m_PreviousStepButtonObject,
                out m_PreviousStepButton,
                out m_PreviousStepButtonLabel,
                OnPreviousStepButtonClicked);

            BindActionButton(
                m_NextStepButtonObject,
                out m_NextStepButton,
                out m_NextStepButtonLabel,
                OnNextStepButtonClicked);

            BindActionButton(
                m_SwapHandsButtonObject,
                out m_SwapHandsButton,
                out m_SwapHandsButtonLabel,
                OnSwapHandsButtonClicked);

            BindActionButton(
                m_InjectionTypeButtonObject,
                out m_InjectionTypeButton,
                out m_InjectionTypeButtonLabel,
                OnInjectionTypeButtonClicked);

            BindActionButton(
                m_DemoVideoButtonObject,
                out m_DemoVideoButton,
                out m_DemoVideoLabel,
                OnDemoVideoButtonClicked);

            var baseIndex = template.transform.GetSiblingIndex();
            m_PreviousStepButtonObject.transform.SetSiblingIndex(Mathf.Min(baseIndex + 1, parent.childCount - 1));
            m_NextStepButtonObject.transform.SetSiblingIndex(Mathf.Min(baseIndex + 2, parent.childCount - 1));
            m_CalibrateButtonObject.transform.SetSiblingIndex(Mathf.Min(baseIndex + 3, parent.childCount - 1));
            m_CreateSurfaceButtonObject.transform.SetSiblingIndex(Mathf.Min(baseIndex + 4, parent.childCount - 1));
            m_SkeletonToggleButtonObject.transform.SetSiblingIndex(Mathf.Min(baseIndex + 5, parent.childCount - 1));
            m_SwapHandsButtonObject.transform.SetSiblingIndex(Mathf.Min(baseIndex + 6, parent.childCount - 1));
            m_InjectionTypeButtonObject.transform.SetSiblingIndex(Mathf.Min(baseIndex + 7, parent.childCount - 1));
            m_DemoVideoButtonObject.transform.SetSiblingIndex(Mathf.Min(baseIndex + 8, parent.childCount - 1));

            RelayoutVisibleActionBar();
            ApplyCoachingCardHeightForActionBar();
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
                // Cloned buttons inherit template listeners; clear them so each action button has one responsibility.
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
            var verticalStep = Mathf.Max(templateRect.rect.height, 36f) + m_ActionButtonVerticalSpacing;
            actionRect.anchoredPosition = new Vector2(basePos.x, basePos.y - (verticalStep * slot));
        }

        /// <summary>
        /// Places Previous/Next under the skip template, then stacks only <b>active</b> secondary buttons
        /// so hidden controls do not leave empty gaps (e.g. Injection Type sits directly under nav on that step).
        /// </summary>
        void RelayoutVisibleActionBar()
        {
            if (!m_AddActionButtonsToCoachingMenu || m_SkipButton == null)
                return;

            var template = m_SkipButton;
            if (!template.TryGetComponent<RectTransform>(out var templateRect))
                return;

            var basePos = templateRect.anchoredPosition;
            var rowStep = Mathf.Max(templateRect.rect.height, 34f) + m_ActionButtonVerticalSpacing;

            bool TryRt(GameObject go, out RectTransform rt)
            {
                rt = null;
                return go != null && go.TryGetComponent(out rt);
            }

            if (TryRt(m_PreviousStepButtonObject, out var prevRt) && TryRt(m_NextStepButtonObject, out var nextRt))
            {
                var navY = basePos.y - rowStep;
                prevRt.anchoredPosition = new Vector2(basePos.x - m_NavigationRowPairOffset, navY);
                nextRt.anchoredPosition = new Vector2(basePos.x + m_NavigationRowPairOffset, navY);
            }

            var row = 2;
            var ordered = new[]
            {
                m_CalibrateButtonObject,
                m_CreateSurfaceButtonObject,
                m_SkeletonToggleButtonObject,
                m_SwapHandsButtonObject,
                m_InjectionTypeButtonObject,
                m_DemoVideoButtonObject,
            };

            for (var i = 0; i < ordered.Length; i++)
            {
                var go = ordered[i];
                if (go == null || !go.activeSelf)
                    continue;

                OffsetActionButtonFromTemplate(template, go, row);
                row++;
            }
        }

        RectTransform FindCoachingCardRoot()
        {
            if (m_CoachingUIParent == null)
                return null;

            var roots = m_CoachingUIParent.GetComponentsInChildren<RectTransform>(true);
            for (var i = 0; i < roots.Length; i++)
            {
                if (roots[i] != null && roots[i].name == "CoachingCardRoot")
                    return roots[i];
            }

            return null;
        }

        void ApplyCoachingCardHeightForActionBar()
        {
            if (m_CoachingCardExtraHeightForActions <= 0f)
                return;

            if (m_CoachingCardRoot == null)
                m_CoachingCardRoot = FindCoachingCardRoot();

            if (m_CoachingCardRoot == null)
                return;

            if (!m_CoachingCardSizeCaptured)
            {
                m_CoachingCardBaseSize = m_CoachingCardRoot.sizeDelta;
                m_CoachingCardSizeCaptured = true;
            }

            m_CoachingCardRoot.sizeDelta = new Vector2(
                m_CoachingCardBaseSize.x,
                m_CoachingCardBaseSize.y + m_CoachingCardExtraHeightForActions);
        }

        void EnsureCoachingMetricsHud()
        {
            if (!m_ShowCoachingMetricsHud || m_CoachingUIParent == null)
                return;

            if (m_CoachingCardRoot == null)
                m_CoachingCardRoot = FindCoachingCardRoot();

            if (m_CoachingCardRoot == null)
                return;

            CoachingFloatingMetricsHud.EnsureRoot(m_CoachingCardRoot, m_CoachingBodyText, out m_MetricsHudRoot, out m_MetricsHudText);
        }

        void UpdateCoachingMetricsHud()
        {
            if (!m_ShowCoachingMetricsHud || m_InjectionTutorial == null)
            {
                if (m_MetricsHudRoot != null)
                    m_MetricsHudRoot.gameObject.SetActive(false);
                return;
            }

            EnsureCoachingMetricsHud();
            if (m_MetricsHudRoot == null || m_MetricsHudText == null)
                return;

            if (m_CoachingCardRoot == null)
                m_CoachingCardRoot = FindCoachingCardRoot();

            if (m_CoachingCardRoot == null)
                return;

            m_MetricsHudRoot.gameObject.SetActive(true);
            m_MetricsHudText.text = BuildCoachingMetricsHudText(m_InjectionTutorial.currentStep);

            if (m_CachedModalRt == null && m_CoachingCardRoot != null)
            {
                var modalT = m_CoachingCardRoot.Find("Modal");
                if (modalT != null)
                    m_CachedModalRt = modalT.GetComponent<RectTransform>();
            }

            var rects = new List<RectTransform>(12);
            GatherVisibleActionButtonRects(rects);
            CoachingFloatingMetricsHud.LayoutBetweenModalAndActions(
                m_CoachingCardRoot,
                m_MetricsHudRoot,
                m_CachedModalRt,
                rects,
                m_MetricsHudGapPx,
                m_MetricsHudMinHeight,
                m_MetricsHudMaxHeight);

            // Keep the strip directly under the Modal so it does not sit last (on top of every action button).
            if (m_CachedModalRt != null && m_MetricsHudRoot.parent == m_CachedModalRt.parent)
            {
                var parent = m_MetricsHudRoot.parent;
                var targetIx = Mathf.Clamp(m_CachedModalRt.GetSiblingIndex() + 1, 0, parent.childCount - 1);
                if (m_MetricsHudRoot.GetSiblingIndex() != targetIx)
                    m_MetricsHudRoot.SetSiblingIndex(targetIx);
            }

            Canvas.ForceUpdateCanvases();
            m_MetricsHudText.ForceMeshUpdate(true);
        }

        void GatherVisibleActionButtonRects(List<RectTransform> list)
        {
            list.Clear();

            void Add(GameObject go)
            {
                if (go == null || !go.activeInHierarchy)
                    return;
                if (go.TryGetComponent<RectTransform>(out var rt))
                    list.Add(rt);
            }

            Add(m_SkipButton);
            Add(m_PreviousStepButtonObject);
            Add(m_NextStepButtonObject);
            Add(m_CalibrateButtonObject);
            Add(m_CreateSurfaceButtonObject);
            Add(m_SkeletonToggleButtonObject);
            Add(m_SwapHandsButtonObject);
            Add(m_InjectionTypeButtonObject);
            Add(m_DemoVideoButtonObject);
        }

        static bool CoachingMetricsStepNeedsPerFrameText(SyringeCalibrationButtonBridge.TutorialStep step)
        {
            switch (step)
            {
                case SyringeCalibrationButtonBridge.TutorialStep.Calibration:
                case SyringeCalibrationButtonBridge.TutorialStep.FillSyringe:
                case SyringeCalibrationButtonBridge.TutorialStep.BubbleCheckManual:
                case SyringeCalibrationButtonBridge.TutorialStep.InjectionAngle:
                case SyringeCalibrationButtonBridge.TutorialStep.InsertionSpeedFlowRate:
                case SyringeCalibrationButtonBridge.TutorialStep.RemoveSpeed:
                    return true;
                default:
                    return false;
            }
        }

        string BuildCoachingMetricsHudText(SyringeCalibrationButtonBridge.TutorialStep step)
        {
            if (m_InjectionTutorial == null)
                return string.Empty;

            var t = m_InjectionTutorial;
            var demo = Mathf.Sin(Time.realtimeSinceStartup * 0.7f);

            switch (step)
            {
                case SyringeCalibrationButtonBridge.TutorialStep.Start:
                    return
                        "Session (mock)\n" +
                        "Tracking: Ready\n" +
                        "Latency: " + (18f + demo * 2f).ToString("F0") + " ms\n" +
                        "Comfort: OK";

                case SyringeCalibrationButtonBridge.TutorialStep.InjectionType:
                    return
                        "Selection (mock)\n" +
                        "Current: " + GetInjectionTypeShortLabel(t.selectedInjectionType) + "\n" +
                        "Valid routes: IM / SC / ID / IV";

                case SyringeCalibrationButtonBridge.TutorialStep.Calibration:
                {
                    var taps = m_Tracker != null ? m_Tracker.calibrationTapCount : 0;
                    var req = m_Tracker != null ? m_Tracker.requiredCalibrationTaps : 3;
                    var surface = m_SurfaceSelectionTool != null;
                    var sel = surface && m_SurfaceSelectionTool.isSelectingSurface;
                    var placed = surface && m_SurfaceSelectionTool.hasPlacedSurface;
                    return
                        "Calibration\n" +
                        "Syringe taps: " + taps + " / " + req + "\n" +
                        "Surface: " + (sel ? "placing..." : placed ? "placed" : "not placed") + "\n" +
                        "Marker stable (mock): " + (72f + demo * 6f).ToString("F0") + "%";
                }

                case SyringeCalibrationButtonBridge.TutorialStep.FillSyringe:
                    return
                        "Fill\n" +
                        "Volume: " + (t.fillAmountNormalized * 100f).ToString("F0") + "%\n" +
                        "Target: " + (t.targetFillAmountNormalized * 100f).ToString("F0") + "%\n" +
                        "Rate (mock): " + (2.8f + demo * 0.4f).ToString("F1") + " ml/s";

                case SyringeCalibrationButtonBridge.TutorialStep.BubbleCheckManual:
                    return
                        "Bubble check\n" +
                        "Status: " + (t.bubbleCheckCompleted ? "cleared" : "pending") + "\n" +
                        "Camera assist (mock): " + (t.bubbleCheckCompleted ? "idle" : "scanning");

                case SyringeCalibrationButtonBridge.TutorialStep.InjectionAngle:
                {
                    var r = t.targetInjectionAngleRange;
                    return
                        "Angle\n" +
                        "Current: " + t.injectionAngleDegrees.ToString("F1") + " deg\n" +
                        "Band: " + r.x.ToString("F0") + " - " + r.y.ToString("F0") + " deg\n" +
                        "Hold: " + (t.angleHoldProgressNormalized * 100f).ToString("F0") + "%";
                }

                case SyringeCalibrationButtonBridge.TutorialStep.InsertionSpeedFlowRate:
                    return
                        "Insertion + Flow\n" +
                        "Insertion: " + t.insertionSpeedCmPerSec.ToString("F1") + " / " + t.targetInsertionSpeedCmPerSec.ToString("F1") + " cm/s\n" +
                        "Flow: " + t.flowRateMlPerSec.ToString("F2") + " / " + t.targetFlowRateMlPerSec.ToString("F2") + " ml/s\n" +
                        "Lock-in: " + (t.insertionFlowHoldProgressNormalized * 100f).ToString("F0") + "%";

                case SyringeCalibrationButtonBridge.TutorialStep.RemoveSpeed:
                    return
                        "Withdrawal\n" +
                        "Speed: " + t.removalSpeedCmPerSec.ToString("F1") + " / " + t.targetRemovalSpeedCmPerSec.ToString("F1") + " cm/s\n" +
                        "Steady: " + (t.removalHoldProgressNormalized * 100f).ToString("F0") + "%";

                case SyringeCalibrationButtonBridge.TutorialStep.FinalScore:
                    return
                        "Scoreboard\n" +
                        "Total: " + t.finalScore.ToString("F1") + " / 100\n" +
                        "(See breakdown in body text)";

                default:
                    return "Metrics\nStep: " + step;
            }
        }

        void OnCalibrateSyringeButtonClicked()
        {
            if (m_Tracker == null)
                return;

            if (m_InjectionTutorial != null &&
                m_InjectionTutorial.currentStep == SyringeCalibrationButtonBridge.TutorialStep.Start)
            {
                // Flow is Start -> Injection Type -> Calibration; reach Calibration so calibration taps apply.
                m_InjectionTutorial.AdvanceStep();
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

        void OnSkeletonToggleButtonClicked()
        {
            if (m_HandOverlayBridge == null)
                return;

            m_HandOverlayBridge.ToggleSkeleton();
            RefreshActionButtons(force: true);
        }

        void OnPreviousStepButtonClicked()
        {
            ForceEndAllGoals();
            RefreshActionButtons(force: true);
        }

        void OnNextStepButtonClicked()
        {
            ForceCompleteGoal();
            RefreshActionButtons(force: true);
        }

        void OnSwapHandsButtonClicked()
        {
            if (m_Tracker == null)
                return;

            m_Tracker.SwapTrackingHand();
            RefreshActionButtons(force: true);
            SyncTutorialToUI(force: true);
        }

        void OnInjectionTypeButtonClicked()
        {
            if (m_InjectionTutorial == null)
                return;

            m_InjectionTutorial.CycleInjectionType();
            RefreshActionButtons(force: true);
            SyncTutorialToUI(force: true);
        }

        void OnDemoVideoButtonClicked()
        {
            TogglePlayer(true);
        }

        void ApplyActionButtonVisibilityForStep(SyringeCalibrationButtonBridge.TutorialStep step)
        {
            if (!m_AddActionButtonsToCoachingMenu)
                return;

            if (!m_ShowActionButtonsForCurrentStepOnly)
            {
                SetAllActionBarButtonsActive(true);
                return;
            }

            void Set(GameObject go, bool on)
            {
                if (go != null)
                    go.SetActive(on);
            }

            var demoOk = m_VideoPlayer != null;

            switch (step)
            {
                case SyringeCalibrationButtonBridge.TutorialStep.Start:
                    Set(m_CalibrateButtonObject, false);
                    Set(m_CreateSurfaceButtonObject, false);
                    Set(m_SkeletonToggleButtonObject, false);
                    Set(m_PreviousStepButtonObject, false);
                    Set(m_NextStepButtonObject, true);
                    Set(m_SwapHandsButtonObject, false);
                    Set(m_InjectionTypeButtonObject, false);
                    Set(m_DemoVideoButtonObject, demoOk);
                    break;

                case SyringeCalibrationButtonBridge.TutorialStep.Calibration:
                    Set(m_CalibrateButtonObject, true);
                    Set(m_CreateSurfaceButtonObject, true);
                    Set(m_SkeletonToggleButtonObject, true);
                    Set(m_PreviousStepButtonObject, true);
                    Set(m_NextStepButtonObject, true);
                    Set(m_SwapHandsButtonObject, true);
                    Set(m_InjectionTypeButtonObject, false);
                    Set(m_DemoVideoButtonObject, demoOk);
                    break;

                case SyringeCalibrationButtonBridge.TutorialStep.InjectionType:
                    Set(m_CalibrateButtonObject, false);
                    Set(m_CreateSurfaceButtonObject, false);
                    Set(m_SkeletonToggleButtonObject, false);
                    Set(m_PreviousStepButtonObject, true);
                    Set(m_NextStepButtonObject, true);
                    Set(m_SwapHandsButtonObject, false);
                    Set(m_InjectionTypeButtonObject, true);
                    Set(m_DemoVideoButtonObject, false);
                    break;

                case SyringeCalibrationButtonBridge.TutorialStep.FinalScore:
                    Set(m_CalibrateButtonObject, false);
                    Set(m_CreateSurfaceButtonObject, false);
                    Set(m_SkeletonToggleButtonObject, false);
                    Set(m_PreviousStepButtonObject, true);
                    Set(m_NextStepButtonObject, false);
                    Set(m_SwapHandsButtonObject, false);
                    Set(m_InjectionTypeButtonObject, false);
                    Set(m_DemoVideoButtonObject, demoOk);
                    break;

                default:
                    Set(m_CalibrateButtonObject, false);
                    Set(m_CreateSurfaceButtonObject, true);
                    Set(m_SkeletonToggleButtonObject, false);
                    Set(m_PreviousStepButtonObject, true);
                    Set(m_NextStepButtonObject, true);
                    Set(m_SwapHandsButtonObject, false);
                    Set(m_InjectionTypeButtonObject, false);
                    Set(m_DemoVideoButtonObject, demoOk);
                    break;
            }
        }

        void SetAllActionBarButtonsActive(bool active)
        {
            var demoOk = active && m_VideoPlayer != null;
            if (m_CalibrateButtonObject != null)
                m_CalibrateButtonObject.SetActive(active);
            if (m_CreateSurfaceButtonObject != null)
                m_CreateSurfaceButtonObject.SetActive(active);
            if (m_SkeletonToggleButtonObject != null)
                m_SkeletonToggleButtonObject.SetActive(active);
            if (m_PreviousStepButtonObject != null)
                m_PreviousStepButtonObject.SetActive(active);
            if (m_NextStepButtonObject != null)
                m_NextStepButtonObject.SetActive(active);
            if (m_SwapHandsButtonObject != null)
                m_SwapHandsButtonObject.SetActive(active);
            if (m_InjectionTypeButtonObject != null)
                m_InjectionTypeButtonObject.SetActive(active);
            if (m_DemoVideoButtonObject != null)
                m_DemoVideoButtonObject.SetActive(demoOk);
        }

        ActionButtonSnapshot CaptureActionButtonSnapshot()
        {
            var injection = m_InjectionTutorial != null ? m_InjectionTutorial.selectedInjectionType : default;
            var step = m_InjectionTutorial != null ? m_InjectionTutorial.currentStep : default;
            return new ActionButtonSnapshot
            {
                CalibrationTapCount = m_Tracker != null ? m_Tracker.calibrationTapCount : 0,
                IsCalibratingMarker = m_Tracker != null && m_Tracker.isCalibratingMarker,
                IsMarkerCalibrated = m_Tracker != null && m_Tracker.isMarkerCalibrated,
                IsSelectingSurface = m_SurfaceSelectionTool != null && m_SurfaceSelectionTool.isSelectingSurface,
                HasPlacedSurface = m_SurfaceSelectionTool != null && m_SurfaceSelectionTool.hasPlacedSurface,
                SkeletonVisible = m_HandOverlayBridge != null && m_HandOverlayBridge.isSkeletonVisible,
                IsTrackingLeftHand = m_Tracker != null && m_Tracker.isTrackingLeftHand,
                InjectionType = injection,
                Step = step,
            };
        }

        void RefreshActionButtons(bool force)
        {
            if (!m_AddActionButtonsToCoachingMenu)
                return;

            var snapshot = CaptureActionButtonSnapshot();
            if (!force && snapshot.Equals(m_LastActionButtonSnapshot))
                return;

            m_LastActionButtonSnapshot = snapshot;

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

            if (m_SkeletonToggleButtonLabel != null)
            {
                var text = BuildSkeletonToggleButtonLabel();
                if (force || !string.Equals(text, m_LastSkeletonToggleButtonText, StringComparison.Ordinal))
                {
                    m_SkeletonToggleButtonLabel.text = text;
                    m_LastSkeletonToggleButtonText = text;
                }
            }

            if (m_PreviousStepButtonLabel != null)
            {
                var text = BuildPreviousStepButtonLabel();
                if (force || !string.Equals(text, m_LastPreviousStepButtonText, StringComparison.Ordinal))
                {
                    m_PreviousStepButtonLabel.text = text;
                    m_LastPreviousStepButtonText = text;
                }
            }

            if (m_NextStepButtonLabel != null)
            {
                var text = BuildNextStepButtonLabel();
                if (force || !string.Equals(text, m_LastNextStepButtonText, StringComparison.Ordinal))
                {
                    m_NextStepButtonLabel.text = text;
                    m_LastNextStepButtonText = text;
                }
            }

            if (m_SwapHandsButtonLabel != null)
            {
                var text = BuildSwapHandsButtonLabel();
                if (force || !string.Equals(text, m_LastSwapHandsButtonText, StringComparison.Ordinal))
                {
                    m_SwapHandsButtonLabel.text = text;
                    m_LastSwapHandsButtonText = text;
                }
            }

            if (m_InjectionTypeButtonLabel != null)
            {
                var text = BuildInjectionTypeButtonLabel();
                if (force || !string.Equals(text, m_LastInjectionTypeButtonText, StringComparison.Ordinal))
                {
                    m_InjectionTypeButtonLabel.text = text;
                    m_LastInjectionTypeButtonText = text;
                }
            }

            if (m_DemoVideoLabel != null)
            {
                var caption = m_DemoVideoCaption;
                if (force || !string.Equals(caption, m_LastDemoVideoCaption, StringComparison.Ordinal))
                {
                    m_DemoVideoLabel.text = caption;
                    m_LastDemoVideoCaption = caption;
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

        string BuildSkeletonToggleButtonLabel()
        {
            if (m_HandOverlayBridge == null)
                return m_ShowSkeletonLabel;

            return m_HandOverlayBridge.isSkeletonVisible ? m_HideSkeletonLabel : m_ShowSkeletonLabel;
        }

        string BuildPreviousStepButtonLabel()
        {
            return m_PreviousStepLabel;
        }

        string BuildNextStepButtonLabel()
        {
            return m_NextStepLabel;
        }

        string BuildSwapHandsButtonLabel()
        {
            if (m_Tracker == null)
                return m_UseRightHandLabel;

            return m_Tracker.isTrackingLeftHand ? m_UseRightHandLabel : m_UseLeftHandLabel;
        }

        string BuildInjectionTypeButtonLabel()
        {
            if (m_InjectionTutorial == null)
                return m_InjectionTypeLabelPrefix;

            return $"{m_InjectionTypeLabelPrefix}: {GetInjectionTypeShortLabel(m_InjectionTutorial.selectedInjectionType)}";
        }

        static string GetInjectionTypeShortLabel(SyringeCalibrationButtonBridge.InjectionType type)
        {
            return type switch
            {
                SyringeCalibrationButtonBridge.InjectionType.Intramuscular => "IM",
                SyringeCalibrationButtonBridge.InjectionType.Subcutaneous => "SC",
                SyringeCalibrationButtonBridge.InjectionType.Intradermal => "ID",
                SyringeCalibrationButtonBridge.InjectionType.Intravenous => "IV",
                _ => "None",
            };
        }

        bool IsTutorialVisualStateUnchanged(SyringeCalibrationButtonBridge.TutorialStep step, bool finished, float score)
        {
            if (step != m_LastDisplayedStep || finished != m_LastFinishedState || !Mathf.Approximately(score, m_LastScore))
                return false;

            if (m_InjectionTutorial == null)
                return true;

            switch (step)
            {
                case SyringeCalibrationButtonBridge.TutorialStep.FillSyringe:
                    return Mathf.Approximately(m_LastSyncedFillNormalized, m_InjectionTutorial.fillAmountNormalized);
                case SyringeCalibrationButtonBridge.TutorialStep.Calibration:
                    var taps = m_Tracker != null ? m_Tracker.calibrationTapCount : 0;
                    return m_LastSyncedCalibrationTaps == taps;
                case SyringeCalibrationButtonBridge.TutorialStep.InjectionType:
                    return m_LastSyncedInjectionType == m_InjectionTutorial.selectedInjectionType;
                case SyringeCalibrationButtonBridge.TutorialStep.FinalScore:
                    return m_InjectionTutorial != null &&
                           string.Equals(
                               m_LastScoreBreakdownSnapshot,
                               m_InjectionTutorial.GetScoreBreakdownDisplayString(),
                               StringComparison.Ordinal);
                default:
                    return true;
            }
        }

        void UpdateTutorialSyncKey()
        {
            if (m_InjectionTutorial == null)
                return;

            m_LastSyncedFillNormalized = m_InjectionTutorial.fillAmountNormalized;
            m_LastSyncedInjectionType = m_InjectionTutorial.selectedInjectionType;
            m_LastSyncedCalibrationTaps = m_Tracker != null ? m_Tracker.calibrationTapCount : 0;

            if (m_InjectionTutorial.currentStep == SyringeCalibrationButtonBridge.TutorialStep.FinalScore)
                m_LastScoreBreakdownSnapshot = m_InjectionTutorial.GetScoreBreakdownDisplayString();
            else
                m_LastScoreBreakdownSnapshot = string.Empty;
        }

        void ApplyTutorialEnvironmentEffects()
        {
            if (m_PassthroughToggle != null)
                m_PassthroughToggle.SetIsOnWithoutNotify(true);

            if (m_FadeMaterial != null)
                m_FadeMaterial.FadeSkybox(true);
        }

        void TryApplyGoalPanelFollow()
        {
            if (m_GoalPanelLazyFollow == null)
                return;

            var grab = m_GoalPanelLazyFollow.GetComponent<FloatingCoachingUIGrab>();
            if (grab != null && !grab.AllowGoalManagerAutoFollow)
                return;

            m_GoalPanelLazyFollow.positionFollowMode = LazyFollow.PositionFollowMode.Follow;
        }

        void SyncTutorialToUI(bool force)
        {
            if (m_InjectionTutorial == null)
                return;

            var step = m_InjectionTutorial.currentStep;
            var finished = m_InjectionTutorial.isFinished;
            var score = m_InjectionTutorial.finalScore;

            if (!force && IsTutorialVisualStateUnchanged(step, finished, score))
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
                {
                    m_CoachingBodyText.text = stepBody;
                    m_CoachingBodyText.textWrappingMode = TextWrappingModes.Normal;
                    if (step == SyringeCalibrationButtonBridge.TutorialStep.FinalScore)
                        m_CoachingBodyText.overflowMode = TextOverflowModes.Overflow;
                }

                ApplyTextToActiveStepPanel(activePanelIndex, stepTitle, stepBody, step);
            }

            var showLegacyStepButton = !m_AddActionButtonsToCoachingMenu;
            // Always drive the primary blue poke label when wired — action-bar mode still leaves "Continue/Next" visible without text otherwise.
            if (m_StepButtonTextField != null)
                m_StepButtonTextField.text = GetActionButtonLabel(step, finished, score);

            if (m_SkipButton != null)
                m_SkipButton.SetActive(showLegacyStepButton && !finished);

            ApplyActionButtonVisibilityForStep(step);
            RelayoutVisibleActionBar();
            UpdateCoachingMetricsHud();

            if (m_GoalPanelLazyFollow != null &&
                m_GoalPanelLazyFollow.TryGetComponent<FloatingCoachingUIGrab>(out var coachingGrab))
                coachingGrab.RefitGrabCollider();

            if (m_ReapplyHeadFollowOnTutorialSync)
                TryApplyGoalPanelFollow();

            if (!m_LastTutorialStepForEnvironmentEffects.HasValue ||
                m_LastTutorialStepForEnvironmentEffects.Value != step)
            {
                ApplyTutorialEnvironmentEffects();
                m_LastTutorialStepForEnvironmentEffects = step;
            }

            UpdateTutorialSyncKey();
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

        void ApplyTextToActiveStepPanel(
            int panelIndex,
            string title,
            string body,
            SyringeCalibrationButtonBridge.TutorialStep step)
        {
            if (panelIndex < 0 || panelIndex >= m_StepList.Count)
                return;

            var panel = m_StepList[panelIndex].stepObject;
            if (panel == null)
                return;

            var texts = panel.GetComponentsInChildren<TextMeshProUGUI>(true);
            TextMeshProUGUI titleTmp = null;
            TextMeshProUGUI bodyTmp = null;

            for (var i = 0; i < texts.Length; i++)
            {
                var t = texts[i];
                if (t == null || t == m_StepButtonTextField)
                    continue;

                var name = t.gameObject.name;
                if (titleTmp == null && name.IndexOf("title", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    titleTmp = t;
                    continue;
                }

                if (bodyTmp == null && name.IndexOf("body", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    bodyTmp = t;
                    continue;
                }
            }

            if (titleTmp == null)
            {
                foreach (var t in texts)
                {
                    if (t == null || t == m_StepButtonTextField)
                        continue;

                    var name = t.gameObject.name;
                    if (name.IndexOf("body", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;

                    titleTmp = t;
                    break;
                }
            }

            var bestBodyScore = -1f;
            for (var i = 0; i < texts.Length; i++)
            {
                var t = texts[i];
                if (t == null || t == m_StepButtonTextField || t == titleTmp)
                    continue;

                var name = t.gameObject.name;
                if (name.IndexOf("title", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                var r = t.rectTransform.rect;
                var area = r.width * r.height;
                if (name.IndexOf("body", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("description", StringComparison.OrdinalIgnoreCase) >= 0)
                    area *= 1.6f;

                if (area > bestBodyScore)
                {
                    bestBodyScore = area;
                    bodyTmp = t;
                }
            }

            if (titleTmp != null)
                titleTmp.text = title;

            if (bodyTmp != null)
            {
                bodyTmp.text = body;
                bodyTmp.textWrappingMode = TextWrappingModes.Normal;
                if (step == SyringeCalibrationButtonBridge.TutorialStep.FinalScore)
                    bodyTmp.overflowMode = TextOverflowModes.Overflow;
            }
        }

        int GetFlowIndex(SyringeCalibrationButtonBridge.TutorialStep step)
        {
            return step switch
            {
                SyringeCalibrationButtonBridge.TutorialStep.Start => 0,
                SyringeCalibrationButtonBridge.TutorialStep.InjectionType => 1,
                SyringeCalibrationButtonBridge.TutorialStep.Calibration => 2,
                SyringeCalibrationButtonBridge.TutorialStep.FillSyringe => 3,
                SyringeCalibrationButtonBridge.TutorialStep.BubbleCheckManual => 4,
                SyringeCalibrationButtonBridge.TutorialStep.InjectionAngle => 5,
                SyringeCalibrationButtonBridge.TutorialStep.InsertionSpeedFlowRate => 6,
                SyringeCalibrationButtonBridge.TutorialStep.RemoveSpeed => 7,
                SyringeCalibrationButtonBridge.TutorialStep.FinalScore => 8,
                _ => 0,
            };
        }

        static void GetDisplayedFlowStep(int flowIndex, out int displayNumber, out int totalSteps)
        {
            totalSteps = 9;
            displayNumber = flowIndex + 1;
        }

        string GetFlowTitle(SyringeCalibrationButtonBridge.TutorialStep step, int stepIndex)
        {
            var label = step switch
            {
                SyringeCalibrationButtonBridge.TutorialStep.Start => "Welcome",
                SyringeCalibrationButtonBridge.TutorialStep.Calibration => "Surface + Syringe",
                SyringeCalibrationButtonBridge.TutorialStep.InjectionType => "Injection Type",
                SyringeCalibrationButtonBridge.TutorialStep.FillSyringe => "Fill Syringe",
                SyringeCalibrationButtonBridge.TutorialStep.BubbleCheckManual => "Bubble Check",
                SyringeCalibrationButtonBridge.TutorialStep.InjectionAngle => "Injection Angle",
                SyringeCalibrationButtonBridge.TutorialStep.InsertionSpeedFlowRate => "Insertion + Flow Rate",
                SyringeCalibrationButtonBridge.TutorialStep.RemoveSpeed => "Remove Speed",
                SyringeCalibrationButtonBridge.TutorialStep.FinalScore => "Final Score",
                _ => "Injection Flow",
            };

            GetDisplayedFlowStep(stepIndex, out var n, out var total);
            return $"Step {n}/{total} - {label}";
        }

        string BuildFlowDescription(SyringeCalibrationButtonBridge.TutorialStep step)
        {
            switch (step)
            {
                case SyringeCalibrationButtonBridge.TutorialStep.Start:
                    return "This app is a mixed-reality injection training coach: step-by-step guidance, hand tracking, and a scored run-through. When you are ready, continue with Next.";

                case SyringeCalibrationButtonBridge.TutorialStep.Calibration:
                    if (m_Tracker != null && m_Tracker.isCalibratingMarker)
                        return $"Create a surface if you have not yet, then finish syringe calibration ({m_Tracker.calibrationTapCount}/{m_Tracker.requiredCalibrationTaps} taps).";

                    if (m_Tracker != null && m_Tracker.isMarkerCalibrated)
                        return "Marker calibration is complete. Adjust the surface if needed, then tap Next.";

                    return "Create a placement surface for your workspace, then calibrate the syringe marker. Use Create Surface and Calibrate Syringe below; tap Next when you are ready to continue.";

                case SyringeCalibrationButtonBridge.TutorialStep.InjectionType:
                    return "Choose the injection type only on this step. Tap Injection Type to cycle IM / SC / ID / IV, then Next.";

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
                    return m_InjectionTutorial != null
                        ? "Results by category:\n" + m_InjectionTutorial.GetScoreBreakdownDisplayString()
                        : string.Empty;

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
                SyringeCalibrationButtonBridge.TutorialStep.InjectionType => "Next",
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

            if (m_InjectionTutorial.currentStep == SyringeCalibrationButtonBridge.TutorialStep.FinalScore ||
                m_InjectionTutorial.isFinished)
            {
                ResetCoaching();
                return;
            }

            if (m_InjectionTutorial.currentStep == SyringeCalibrationButtonBridge.TutorialStep.BubbleCheckManual)
                m_InjectionTutorial.MarkBubbleCheckCompleted();

            m_InjectionTutorial.AdvanceStep();
            SyncTutorialToUI(force: true);
        }

        public void ForceEndAllGoals()
        {
            if (m_InjectionTutorial == null)
                return;

            m_InjectionTutorial.PreviousStep();

            SyncTutorialToUI(force: true);
        }

        public void ResetCoaching()
        {
            if (m_InjectionTutorial != null)
                m_InjectionTutorial.BeginTutorial();

            m_LastTutorialStepForEnvironmentEffects = null;

            if (m_CoachingUIParent != null)
                m_CoachingUIParent.transform.localScale = Vector3.one;

            SetLearnModalOpen(false);

            if (m_GoalPanelLazyFollow != null)
            {
                var grab = m_GoalPanelLazyFollow.GetComponent<FloatingCoachingUIGrab>();
                grab?.ResumeFollowingHead();
            }

            if (m_VideoPlayer != null)
                m_VideoPlayer.SetActive(false);

            if (m_VideoPlayerToggle != null)
                m_VideoPlayerToggle.isOn = false;

            SyncTutorialToUI(force: true);
        }

        public void TogglePlayer(bool visibility)
        {
            if (visibility)
                TurnOnVideoPlayer();
            else if (m_VideoPlayer != null && m_VideoPlayer.activeSelf)
                m_VideoPlayer.SetActive(false);
        }

        [Obsolete("Use TogglePlayer — fixes the legacy typo.")]
        public void TooglePlayer(bool visibility)
        {
            TogglePlayer(visibility);
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
