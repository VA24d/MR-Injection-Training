using System.Collections;
using TMPro;
using UnityEngine.UI;

namespace UnityEngine.XR.Templates.MR
{
    /// <summary>
    /// Reuses the existing hand overlay toggle to control hand skeleton visibility,
    /// while keeping synthetic hand mesh/hand-removal overlay disabled.
    /// </summary>
    public class HandOverlaySkeletonToggleBridge : MonoBehaviour
    {
        [SerializeField]
        bool m_ShowSkeletonByDefault = true;

        [SerializeField]
        [Tooltip("When true, HandVisualizer draws tracked hand meshes (better depth cues and alignment for poke/ray). When false, only joint debug lines.")]
        bool m_DrawHandMeshes = true;

        [SerializeField]
        bool m_ForceDisableHandRemoval = true;

        [SerializeField]
        [Tooltip("Optional. Assign the hand-menu skeleton toggle here to avoid fragile path-based lookup.")]
        Toggle m_HandMenuToggleReference;

        [SerializeField]
        [Tooltip("Optional. Label next to the toggle; if unset, m_HandMenuLabelPath is used.")]
        TextMeshProUGUI m_HandMenuLabelReference;

        [SerializeField]
        string m_HandMenuTogglePath =
            "UI/Hand Menu Setup MR Template Variant/Follow GameObject/Spatial Panel Scroll/Main Menu/Occlusion Tab/Quest Settings/List Item Boolean Toggle/Offset Anchor/Boolean Toggle";

        [SerializeField]
        string m_HandMenuLabelPath =
            "UI/Hand Menu Setup MR Template Variant/Follow GameObject/Spatial Panel Scroll/Main Menu/Occlusion Tab/Quest Settings/List Item Boolean Toggle/Label";

        [SerializeField]
        string m_HandVisualizerPath = "MR Interaction Setup/XR Origin (XR Rig)/Camera Offset/Hand Visualizer";

        Toggle m_Toggle;
        Component m_HandVisualizer;
        OcclusionManager m_OcclusionManager;
        bool m_IsSkeletonVisible;

        public bool isSkeletonVisible => m_IsSkeletonVisible;

        void Start()
        {
            ResolveReferences();
            ApplyLabelText();
            BindToggle();
            ApplyState(m_ShowSkeletonByDefault);
            StartCoroutine(ApplyDeferredOcclusionDisable());
        }

        void OnDestroy()
        {
            if (m_Toggle != null)
                m_Toggle.onValueChanged.RemoveListener(OnToggleValueChanged);
        }

        void ResolveReferences()
        {
            if (m_HandVisualizer == null)
            {
                var visualizerObject = GameObject.Find(m_HandVisualizerPath) ?? GameObject.Find("Hand Visualizer");
                if (visualizerObject != null)
                    m_HandVisualizer = visualizerObject.GetComponent("HandVisualizer");
            }

            if (m_OcclusionManager == null)
                m_OcclusionManager = FindAnyObjectByType<OcclusionManager>();

            if (m_Toggle == null)
            {
                if (m_HandMenuToggleReference != null)
                    m_Toggle = m_HandMenuToggleReference;
                else
                {
                    var toggleObject = GameObject.Find(m_HandMenuTogglePath);
                    if (toggleObject != null)
                        m_Toggle = toggleObject.GetComponent<Toggle>();
                }
            }
        }

        void BindToggle()
        {
            if (m_Toggle == null)
            {
                Debug.LogWarning("Hand overlay toggle not found. Skeleton toggle bridge could not bind.", this);
                return;
            }

            m_Toggle.onValueChanged.RemoveListener(OnToggleValueChanged);
            m_Toggle.onValueChanged.AddListener(OnToggleValueChanged);
            m_Toggle.SetIsOnWithoutNotify(m_ShowSkeletonByDefault);
        }

        void ApplyLabelText()
        {
            if (m_HandMenuLabelReference != null)
            {
                m_HandMenuLabelReference.text = "Toggle Skeleton";
                return;
            }

            var labelObject = GameObject.Find(m_HandMenuLabelPath);
            if (labelObject == null)
                return;

            if (labelObject.TryGetComponent<TextMeshProUGUI>(out var label))
                label.text = "Toggle Skeleton";
        }

        void OnToggleValueChanged(bool isOn)
        {
            ApplyState(isOn);
            StartCoroutine(ApplyDeferredOcclusionDisable());
        }

        public void ToggleSkeleton()
        {
            SetSkeletonVisible(!m_IsSkeletonVisible);
        }

        public void SetSkeletonVisible(bool showSkeleton)
        {
            if (m_Toggle != null)
                m_Toggle.SetIsOnWithoutNotify(showSkeleton);

            ApplyState(showSkeleton);
            StartCoroutine(ApplyDeferredOcclusionDisable());
        }

        void ApplyState(bool showSkeleton)
        {
            m_IsSkeletonVisible = showSkeleton;
            SetVisualizerBool("drawMeshes", m_DrawHandMeshes);
            SetVisualizerBool("debugDrawJoints", showSkeleton);

            if (m_ForceDisableHandRemoval && m_OcclusionManager != null)
                m_OcclusionManager.SetHandHandRemovalEnabled(false);
        }

        IEnumerator ApplyDeferredOcclusionDisable()
        {
            if (!m_ForceDisableHandRemoval || m_OcclusionManager == null)
                yield break;

            // Ensure any existing persistent UI callback cannot leave hand-removal enabled.
            yield return null;
            m_OcclusionManager.SetHandHandRemovalEnabled(false);
        }

        void SetVisualizerBool(string propertyName, bool value)
        {
            if (m_HandVisualizer == null)
                return;

            var property = m_HandVisualizer.GetType().GetProperty(propertyName);
            if (property != null && property.CanWrite)
            {
                property.SetValue(m_HandVisualizer, value);
                return;
            }

            var field = m_HandVisualizer.GetType().GetField(propertyName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);

            if (field != null && field.FieldType == typeof(bool))
                field.SetValue(m_HandVisualizer, value);
        }
    }
}