using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands;

namespace UnityEngine.XR.Templates.MR
{
    /// <summary>
    /// Supports a 2-pinch workflow for selecting a horizontal surface from hand tracking.
    /// </summary>
    public class SurfaceSelectionTool : MonoBehaviour
    {
        [Header("Pinch input")]
        [SerializeField, Min(0.005f)]
        float m_PinchEngageDistance = 0.018f;

        [SerializeField, Min(0.005f)]
        float m_PinchReleaseDistance = 0.026f;

        [Header("Surface dimensions")]
        [SerializeField, Min(0.02f)]
        float m_MinSurfaceWidth = 0.04f;

        [SerializeField, Min(0.02f)]
        float m_MinSurfaceDepth = 0.04f;

        [SerializeField]
        float m_SurfaceVerticalOffset = 0.0015f;

        [Header("Visual style")]
        [SerializeField, Range(0f, 1f)]
        float m_SurfaceOpacity = 0.28f;

        [SerializeField]
        Color m_SurfaceTint = new Color(0.12f, 0.82f, 1f, 0.28f);

        [SerializeField]
        Color m_PointColor = new Color(0.2f, 1f, 0.65f, 0.95f);

        [SerializeField]
        Color m_LineColor = new Color(0.25f, 0.95f, 1f, 0.9f);

        [SerializeField, Min(0.001f)]
        float m_PointSize = 0.008f;

        [SerializeField, Min(0.0002f)]
        float m_LineWidth = 0.0028f;

        public bool isSelectingSurface => m_IsSelectingSurface;
        public bool hasPlacedSurface => m_HasPlacedSurface;
        public int completedPinches => m_HasPlacedSurface ? 2 : (m_HasFirstPoint ? 1 : 0);

        XRHandSubsystem m_HandSubsystem;
        static List<XRHandSubsystem> s_HandSubsystems;

        Transform m_SurfaceRoot;
        Transform m_FirstPointDot;
        Transform m_SecondPointDot;
        Transform m_SurfacePlane;
        LineRenderer m_PreviewLine;

        Material m_RuntimePointMaterial;
        Material m_RuntimeLineMaterial;
        Material m_RuntimeSurfaceMaterial;

        bool m_IsSelectingSurface;
        bool m_HasFirstPoint;
        bool m_HasPlacedSurface;
        bool m_PinchEngaged;

        Vector3 m_FirstPoint;
        Vector3 m_SecondPoint;

        void Awake()
        {
            CreateVisuals();
            EnsureHandSubsystem();
        }

        void Update()
        {
            if (!m_IsSelectingSurface)
                return;

            EnsureHandSubsystem();
            ProcessSelectionStep();
        }

        void OnDestroy()
        {
            if (m_RuntimePointMaterial != null)
                Destroy(m_RuntimePointMaterial);
            if (m_RuntimeLineMaterial != null)
                Destroy(m_RuntimeLineMaterial);
            if (m_RuntimeSurfaceMaterial != null)
                Destroy(m_RuntimeSurfaceMaterial);
        }

        public void BeginSurfaceSelection()
        {
            m_IsSelectingSurface = true;
            m_PinchEngaged = false;
            m_HasFirstPoint = false;
            m_HasPlacedSurface = false;

            if (m_FirstPointDot != null)
                m_FirstPointDot.gameObject.SetActive(false);
            if (m_SecondPointDot != null)
                m_SecondPointDot.gameObject.SetActive(false);
            if (m_SurfacePlane != null)
                m_SurfacePlane.gameObject.SetActive(false);
            if (m_PreviewLine != null)
                m_PreviewLine.enabled = false;
        }

        public void CancelSurfaceSelection()
        {
            m_IsSelectingSurface = false;
            m_PinchEngaged = false;
            m_HasFirstPoint = false;

            if (m_FirstPointDot != null)
                m_FirstPointDot.gameObject.SetActive(false);
            if (m_SecondPointDot != null)
                m_SecondPointDot.gameObject.SetActive(false);
            if (m_PreviewLine != null)
                m_PreviewLine.enabled = false;
        }

        public void ClearPlacedSurface()
        {
            m_IsSelectingSurface = false;
            m_PinchEngaged = false;
            m_HasFirstPoint = false;
            m_HasPlacedSurface = false;

            if (m_FirstPointDot != null)
                m_FirstPointDot.gameObject.SetActive(false);
            if (m_SecondPointDot != null)
                m_SecondPointDot.gameObject.SetActive(false);
            if (m_SurfacePlane != null)
                m_SurfacePlane.gameObject.SetActive(false);
            if (m_PreviewLine != null)
                m_PreviewLine.enabled = false;
        }

        void EnsureHandSubsystem()
        {
            if (m_HandSubsystem == null)
                TryGetHandSubsystem(out m_HandSubsystem);
        }

        void ProcessSelectionStep()
        {
            if (!TryGetPinchPose(out var pinchPoint, out var pinchDistance))
            {
                if (m_HasFirstPoint && m_PreviewLine != null)
                {
                    m_PreviewLine.enabled = true;
                    m_PreviewLine.SetPosition(0, m_FirstPoint);
                    m_PreviewLine.SetPosition(1, m_FirstPoint);
                }
                return;
            }

            if (m_HasFirstPoint && m_PreviewLine != null)
            {
                m_PreviewLine.enabled = true;
                m_PreviewLine.SetPosition(0, m_FirstPoint);
                m_PreviewLine.SetPosition(1, pinchPoint);
            }

            if (!m_PinchEngaged && pinchDistance <= m_PinchEngageDistance)
            {
                m_PinchEngaged = true;
                RegisterPinchPoint(pinchPoint);
            }
            else if (m_PinchEngaged && pinchDistance >= m_PinchReleaseDistance)
            {
                m_PinchEngaged = false;
            }
        }

        void RegisterPinchPoint(Vector3 pinchPoint)
        {
            if (!m_HasFirstPoint)
            {
                m_FirstPoint = pinchPoint;
                m_HasFirstPoint = true;

                if (m_FirstPointDot != null)
                {
                    m_FirstPointDot.position = m_FirstPoint;
                    m_FirstPointDot.gameObject.SetActive(true);
                }

                if (m_SecondPointDot != null)
                    m_SecondPointDot.gameObject.SetActive(false);

                return;
            }

            m_SecondPoint = pinchPoint;
            m_HasPlacedSurface = true;
            m_IsSelectingSurface = false;
            m_HasFirstPoint = false;

            if (m_SecondPointDot != null)
            {
                m_SecondPointDot.position = m_SecondPoint;
                m_SecondPointDot.gameObject.SetActive(true);
            }

            if (m_PreviewLine != null)
            {
                m_PreviewLine.enabled = true;
                m_PreviewLine.SetPosition(0, m_FirstPoint);
                m_PreviewLine.SetPosition(1, m_SecondPoint);
            }

            UpdatePlaneFromPoints();
        }

        bool TryGetPinchPose(out Vector3 pinchPoint, out float pinchDistance)
        {
            pinchPoint = default;
            pinchDistance = 0f;

            if (m_HandSubsystem == null || !m_HandSubsystem.running)
                return false;

            var rightHand = m_HandSubsystem.rightHand;
            if (!rightHand.isTracked)
                return false;

            if (!TryGetJointPose(rightHand, XRHandJointID.IndexTip, out var indexTipPose) ||
                !TryGetJointPose(rightHand, XRHandJointID.ThumbTip, out var thumbTipPose))
                return false;

            pinchPoint = 0.5f * (indexTipPose.position + thumbTipPose.position);
            pinchDistance = Vector3.Distance(indexTipPose.position, thumbTipPose.position);
            return true;
        }

        void UpdatePlaneFromPoints()
        {
            if (m_SurfacePlane == null)
                return;

            var first = m_FirstPoint;
            var second = m_SecondPoint;

            var y = 0.5f * (first.y + second.y) + m_SurfaceVerticalOffset;
            var firstFlat = new Vector3(first.x, y, first.z);
            var secondFlat = new Vector3(second.x, y, second.z);

            // Treat the two pinch points as opposite diagonal corners on the horizontal XZ plane.
            var width = Mathf.Abs(secondFlat.x - firstFlat.x);
            var depth = Mathf.Abs(secondFlat.z - firstFlat.z);

            // Keep corners true to pinch positions; only guard against a fully collapsed axis.
            const float kMinRenderableDimension = 0.001f;
            if (width < kMinRenderableDimension)
                width = kMinRenderableDimension;
            if (depth < kMinRenderableDimension)
                depth = kMinRenderableDimension;

            var center = new Vector3(
                0.5f * (firstFlat.x + secondFlat.x),
                y,
                0.5f * (firstFlat.z + secondFlat.z));

            m_FirstPoint = firstFlat;
            m_SecondPoint = secondFlat;
            if (m_FirstPointDot != null)
                m_FirstPointDot.position = m_FirstPoint;
            if (m_SecondPointDot != null)
                m_SecondPointDot.position = m_SecondPoint;
            if (m_PreviewLine != null)
            {
                m_PreviewLine.SetPosition(0, m_FirstPoint);
                m_PreviewLine.SetPosition(1, m_SecondPoint);
            }

            m_SurfacePlane.position = center;
            m_SurfacePlane.rotation = Quaternion.identity;
            m_SurfacePlane.localScale = new Vector3(width / 10f, 1f, depth / 10f);
            m_SurfacePlane.gameObject.SetActive(true);
        }

        void CreateVisuals()
        {
            if (m_SurfaceRoot != null)
                return;

            m_SurfaceRoot = new GameObject("Surface Selection Visuals").transform;
            m_SurfaceRoot.SetParent(transform, false);

            m_RuntimePointMaterial = CreateUnlitMaterial(m_PointColor);
            m_RuntimeLineMaterial = CreateUnlitMaterial(m_LineColor);
            m_RuntimeSurfaceMaterial = CreateSurfaceMaterial();

            m_FirstPointDot = CreatePoint("Surface Point A", m_RuntimePointMaterial, m_PointSize);
            m_SecondPointDot = CreatePoint("Surface Point B", m_RuntimePointMaterial, m_PointSize);
            m_FirstPointDot.gameObject.SetActive(false);
            m_SecondPointDot.gameObject.SetActive(false);

            var lineObject = new GameObject("Surface Selection Line");
            lineObject.transform.SetParent(m_SurfaceRoot, false);
            m_PreviewLine = lineObject.AddComponent<LineRenderer>();
            m_PreviewLine.positionCount = 2;
            m_PreviewLine.material = m_RuntimeLineMaterial;
            m_PreviewLine.useWorldSpace = true;
            m_PreviewLine.startWidth = m_LineWidth;
            m_PreviewLine.endWidth = m_LineWidth;
            m_PreviewLine.numCapVertices = 4;
            m_PreviewLine.shadowCastingMode = ShadowCastingMode.Off;
            m_PreviewLine.receiveShadows = false;
            m_PreviewLine.lightProbeUsage = LightProbeUsage.Off;
            m_PreviewLine.enabled = false;

            var planeObject = GameObject.CreatePrimitive(PrimitiveType.Plane);
            planeObject.name = "Selected Surface Plane";
            planeObject.transform.SetParent(m_SurfaceRoot, false);
            if (planeObject.TryGetComponent<Collider>(out var collider))
                Destroy(collider);

            if (planeObject.TryGetComponent<MeshRenderer>(out var renderer))
                renderer.sharedMaterial = m_RuntimeSurfaceMaterial;

            m_SurfacePlane = planeObject.transform;
            m_SurfacePlane.gameObject.SetActive(false);
        }

        Transform CreatePoint(string name, Material material, float size)
        {
            var point = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            point.name = name;
            point.transform.SetParent(m_SurfaceRoot, false);
            point.transform.localScale = Vector3.one * size;

            if (point.TryGetComponent<Collider>(out var collider))
                Destroy(collider);

            if (point.TryGetComponent<Renderer>(out var renderer))
                renderer.sharedMaterial = material;

            return point.transform;
        }

        Material CreateSurfaceMaterial()
        {
            Material material = null;
            var boxVisualizer = FindAnyObjectByType<ARBoundingBoxDebugVisualizer>();
            if (boxVisualizer != null)
            {
                var renderer = boxVisualizer.GetComponent<MeshRenderer>();
                if (renderer == null)
                    renderer = boxVisualizer.GetComponentInChildren<MeshRenderer>();

                if (renderer != null && renderer.sharedMaterial != null)
                    material = new Material(renderer.sharedMaterial);
            }

            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                             Shader.Find("Unlit/Color") ??
                             Shader.Find("Standard");
                material = new Material(shader);
            }

            var tint = m_SurfaceTint;
            tint.a = Mathf.Clamp01(m_SurfaceOpacity);

            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", tint);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", tint);
            if (material.HasProperty("_TexColorTint"))
                material.SetColor("_TexColorTint", tint);
            if (material.HasProperty("_SrcBlend"))
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite"))
                material.SetInt("_ZWrite", 0);

            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        static Material CreateUnlitMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Unlit/Color") ??
                         Shader.Find("Standard");
            var material = new Material(shader);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        static bool TryGetHandSubsystem(out XRHandSubsystem handSubsystem)
        {
            s_HandSubsystems ??= new List<XRHandSubsystem>();
            SubsystemManager.GetSubsystems(s_HandSubsystems);
            if (s_HandSubsystems.Count == 0)
            {
                handSubsystem = default;
                return false;
            }

            for (var i = 0; i < s_HandSubsystems.Count; ++i)
            {
                if (s_HandSubsystems[i].running)
                {
                    handSubsystem = s_HandSubsystems[i];
                    return true;
                }
            }

            handSubsystem = s_HandSubsystems[0];
            return true;
        }

        static bool TryGetJointPose(XRHand hand, XRHandJointID jointId, out Pose pose)
        {
            var joint = hand.GetJoint(jointId);
            return joint.TryGetPose(out pose);
        }
    }
}
