using TMPro;
using UnityEngine.UI;

namespace UnityEngine.XR.Templates.MR
{
    /// <summary>
    /// Adds runtime syringe controls to the hand menu and binds them to tracker tools.
    /// </summary>
    public class SyringeCalibrationButtonBridge : MonoBehaviour
    {
        [SerializeField]
        string m_SettingsTabPath =
            "UI/Hand Menu Setup MR Template Variant/Follow GameObject/Spatial Panel Scroll/Main Menu/Settings Tab";

        [SerializeField]
        string m_TemplateButtonPath =
            "UI/Hand Menu Setup MR Template Variant/Follow GameObject/Spatial Panel Scroll/Main Menu/Settings Tab/Relaunch Coaching Button";

        [SerializeField]
        string m_CalibrationButtonName = "Syringe Calibration Button";

        [SerializeField]
        string m_SurfaceButtonName = "Select Surface Button";

        [SerializeField]
        bool m_AddAsLastSibling = true;

        [SerializeField]
        string m_IdleLabel = "Start Calibration";

        [SerializeField]
        string m_ReadyLabel = "Recalibrate Syringe";

        [SerializeField]
        string m_MissingTrackerLabel = "Calibrate Syringe";

        [SerializeField]
        string m_CalibratingLabelPrefix = "Calibrating";

        [Header("Surface button labels")]
        [SerializeField]
        string m_SurfaceIdleLabel = "Select Surface";

        [SerializeField]
        string m_SurfaceSelectingLabel = "Selecting Surface";

        [SerializeField]
        string m_SurfaceMissingToolLabel = "Select Surface";

        SyringeOverlayTracker m_Tracker;
        SurfaceSelectionTool m_SurfaceTool;

        GameObject m_CalibrationButtonObject;
        Button m_CalibrationButton;
        TextMeshProUGUI m_CalibrationLabel;
        string m_LastCalibrationText = string.Empty;

        GameObject m_SurfaceButtonObject;
        Button m_SurfaceButton;
        TextMeshProUGUI m_SurfaceLabel;
        string m_LastSurfaceText = string.Empty;

        void Start()
        {
            m_Tracker = GetComponent<SyringeOverlayTracker>();
            if (m_Tracker == null)
                m_Tracker = FindAnyObjectByType<SyringeOverlayTracker>();

            m_SurfaceTool = GetComponent<SurfaceSelectionTool>();
            if (m_SurfaceTool == null)
                m_SurfaceTool = gameObject.AddComponent<SurfaceSelectionTool>();

            CreateOrFindCalibrationButton();
            CreateOrFindSurfaceButton();
            PositionSurfaceButtonBelowCalibration();
            RefreshButtonLabels(force: true);
        }

        void Update()
        {
            PositionSurfaceButtonBelowCalibration();
            RefreshButtonLabels(force: false);
        }

        void OnDestroy()
        {
            if (m_CalibrationButton != null)
                m_CalibrationButton.onClick.RemoveListener(OnCalibrationButtonClicked);

            if (m_SurfaceButton != null)
                m_SurfaceButton.onClick.RemoveListener(OnSurfaceButtonClicked);
        }

        void CreateOrFindCalibrationButton()
        {
            var existing = GameObject.Find($"{m_SettingsTabPath}/{m_CalibrationButtonName}");
            if (existing != null)
            {
                BindCalibrationButton(existing);
                return;
            }

            var template = GameObject.Find(m_TemplateButtonPath);
            var settingsTab = GameObject.Find(m_SettingsTabPath);
            if (template == null || settingsTab == null)
            {
                Debug.LogWarning("Syringe calibration button could not be created (template/settings tab not found).", this);
                return;
            }

            var buttonClone = Instantiate(template, settingsTab.transform);
            buttonClone.name = m_CalibrationButtonName;
            if (m_AddAsLastSibling)
                buttonClone.transform.SetAsLastSibling();

            BindCalibrationButton(buttonClone);
        }

        void CreateOrFindSurfaceButton()
        {
            var existing = GameObject.Find($"{m_SettingsTabPath}/{m_SurfaceButtonName}");
            if (existing != null)
            {
                BindSurfaceButton(existing);
                return;
            }

            var template = GameObject.Find(m_TemplateButtonPath);
            var settingsTab = GameObject.Find(m_SettingsTabPath);
            if (template == null || settingsTab == null)
            {
                Debug.LogWarning("Surface selection button could not be created (template/settings tab not found).", this);
                return;
            }

            var buttonClone = Instantiate(template, settingsTab.transform);
            buttonClone.name = m_SurfaceButtonName;
            if (m_AddAsLastSibling)
                buttonClone.transform.SetAsLastSibling();

            BindSurfaceButton(buttonClone);
        }

        void BindCalibrationButton(GameObject buttonObject)
        {
            m_CalibrationButtonObject = buttonObject;
            m_CalibrationButton = buttonObject.GetComponentInChildren<Button>(true);
            if (m_CalibrationButton == null)
            {
                Debug.LogWarning("Syringe calibration button missing Button component.", this);
                return;
            }

            // Replace persistent callbacks inherited from the template.
            m_CalibrationButton.onClick = new Button.ButtonClickedEvent();
            m_CalibrationButton.onClick.AddListener(OnCalibrationButtonClicked);

            m_CalibrationLabel = buttonObject.GetComponentInChildren<TextMeshProUGUI>(true);
            if (m_CalibrationLabel == null)
                Debug.LogWarning("Syringe calibration button missing TextMeshProUGUI label.", this);
        }

        void BindSurfaceButton(GameObject buttonObject)
        {
            m_SurfaceButtonObject = buttonObject;
            m_SurfaceButton = buttonObject.GetComponentInChildren<Button>(true);
            if (m_SurfaceButton == null)
            {
                Debug.LogWarning("Surface selection button missing Button component.", this);
                return;
            }

            m_SurfaceButton.onClick = new Button.ButtonClickedEvent();
            m_SurfaceButton.onClick.AddListener(OnSurfaceButtonClicked);

            m_SurfaceLabel = buttonObject.GetComponentInChildren<TextMeshProUGUI>(true);
            if (m_SurfaceLabel == null)
                Debug.LogWarning("Surface selection button missing TextMeshProUGUI label.", this);
        }

        void OnCalibrationButtonClicked()
        {
            if (m_Tracker == null)
                return;

            if (m_Tracker.isCalibratingMarker)
                m_Tracker.CancelMarkerCalibration();
            else
                m_Tracker.StartMarkerCalibration();

            RefreshButtonLabels(force: true);
        }

        void OnSurfaceButtonClicked()
        {
            if (m_SurfaceTool == null)
                return;

            if (m_SurfaceTool.isSelectingSurface)
                m_SurfaceTool.CancelSurfaceSelection();
            else
                m_SurfaceTool.BeginSurfaceSelection();

            RefreshButtonLabels(force: true);
        }

        void PositionSurfaceButtonBelowCalibration()
        {
            if (m_CalibrationButtonObject == null || m_SurfaceButtonObject == null)
                return;

            var calibrationTransform = m_CalibrationButtonObject.transform;
            var surfaceTransform = m_SurfaceButtonObject.transform;
            if (calibrationTransform.parent == null || surfaceTransform.parent == null)
                return;
            if (calibrationTransform.parent != surfaceTransform.parent)
                return;

            var desiredIndex = Mathf.Min(
                calibrationTransform.GetSiblingIndex() + 1,
                calibrationTransform.parent.childCount - 1);

            if (surfaceTransform.GetSiblingIndex() != desiredIndex)
                surfaceTransform.SetSiblingIndex(desiredIndex);
        }

        void RefreshButtonLabels(bool force)
        {
            RefreshCalibrationButtonLabel(force);
            RefreshSurfaceButtonLabel(force);
        }

        void RefreshCalibrationButtonLabel(bool force)
        {
            if (m_CalibrationLabel == null)
                return;

            var newText = BuildButtonLabel();
            if (!force && newText == m_LastCalibrationText)
                return;

            m_CalibrationLabel.text = newText;
            m_LastCalibrationText = newText;
        }

        void RefreshSurfaceButtonLabel(bool force)
        {
            if (m_SurfaceLabel == null)
                return;

            var newText = BuildSurfaceButtonLabel();
            if (!force && newText == m_LastSurfaceText)
                return;

            m_SurfaceLabel.text = newText;
            m_LastSurfaceText = newText;
        }

        string BuildButtonLabel()
        {
            if (m_Tracker == null)
                return m_MissingTrackerLabel;

            if (m_Tracker.isCalibratingMarker)
                return $"{m_CalibratingLabelPrefix} ({m_Tracker.calibrationTapCount}/{m_Tracker.requiredCalibrationTaps})";

            if (m_Tracker.isMarkerCalibrated)
                return m_ReadyLabel;

            return m_IdleLabel;
        }

        string BuildSurfaceButtonLabel()
        {
            if (m_SurfaceTool == null)
                return m_SurfaceMissingToolLabel;

            if (m_SurfaceTool.isSelectingSurface)
                return m_SurfaceSelectingLabel;

            return m_SurfaceIdleLabel;
        }
    }
}