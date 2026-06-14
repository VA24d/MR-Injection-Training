using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands;

namespace UnityEngine.XR.Templates.MR
{
    /// <summary>
    /// Supports a 3-pinch workflow for selecting a horizontal surface from hand tracking.
    /// </summary>
    public class SurfaceSelectionTool : MonoBehaviour
    {
        [Header("View placement")]
        [SerializeField, Min(0.1f)]
        [Tooltip("Distance (m) in front of the headset at which RecenterToView drops the target.")]
        float m_ViewPlaceDistance = 0.45f;

        [SerializeField, Min(0.02f)]
        [Tooltip("Diameter (m) of the view-placed target surface.")]
        float m_ViewSurfaceDiameter = 0.12f;

        [Header("Pinch input")]
        [SerializeField, Min(0.005f)]
        float m_PinchEngageDistance = 0.018f;

        [SerializeField, Min(0.005f)]
        float m_PinchReleaseDistance = 0.026f;

        [Header("Surface dimensions")]
        [SerializeField, Min(0.0005f)]
        float m_MinRenderableDimension = 0.001f;

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

        [Header("Height handle")]
        [SerializeField, Min(0.005f)]
        float m_HandleGrabRadius = 0.035f;

        [SerializeField, Min(0.02f)]
        float m_HandleStemHeight = 0.06f;

        [SerializeField, Min(0.001f)]
        float m_HandleStemWidth = 0.004f;

        [SerializeField, Min(0.001f)]
        float m_HandleKnobSize = 0.012f;

        [SerializeField]
        Color m_HandleColor = new Color(1f, 0.72f, 0.18f, 0.95f);

        public bool isSelectingSurface => m_IsSelectingSurface;
        public bool hasPlacedSurface => m_HasPlacedSurface;
        public int completedPinches => m_HasPlacedSurface ? 3 : (m_HasSecondPoint ? 2 : (m_HasFirstPoint ? 1 : 0));

        public bool TryGetPlacedSurface(out Pose surfacePose, out Vector2 sizeMeters)
        {
            surfacePose = default;
            sizeMeters = default;

            if (!m_HasPlacedSurface || m_SurfacePlane == null || !m_SurfacePlane.gameObject.activeInHierarchy)
                return false;

            surfacePose = new Pose(m_SurfacePlane.position, m_SurfacePlane.rotation);

            // Unity Plane primitive is 10x10 units before scaling.
            var scale = m_SurfacePlane.lossyScale;
            sizeMeters = new Vector2(Mathf.Abs(scale.x) * 10f, Mathf.Abs(scale.z) * 10f);
            return true;
        }

        XRHandSubsystem m_HandSubsystem;
        static List<XRHandSubsystem> s_HandSubsystems;

        Transform m_SurfaceRoot;
        Transform m_FirstPointDot;
        Transform m_SecondPointDot;
        Transform m_ThirdPointDot;
        Transform m_SurfacePlane;
        LineRenderer m_FirstSegmentLine;
        LineRenderer m_SecondSegmentLine;
        Transform m_HeightHandleRoot;
        Transform m_HeightHandleKnob;
        LineRenderer m_HeightHandleStem;

        Material m_RuntimePointMaterial;
        Material m_RuntimeHandleMaterial;
        Material m_RuntimeLineMaterial;
        Material m_RuntimeSurfaceMaterial;

        bool m_IsSelectingSurface;
        bool m_HasFirstPoint;
        bool m_HasSecondPoint;
        bool m_HasPlacedSurface;
        bool m_PinchEngaged;
        // Once the first point is pinched, lock the remaining points to that same hand so the user
        // can't accidentally place mixed-hand points. Reset when selection starts/cancels/completes.
        Handedness m_LockedSelectionHand = Handedness.Invalid;
        bool m_IsDraggingHeightHandle;
        float m_HeightDragStartY;
        float m_HeightDragSurfaceStartY;

        // Carry the placed surface (and thus the injection guide cone + snap target) with the coaching
        // panel while the user grabs and moves it.
        FloatingCoachingUIGrab m_PanelGrab;
        bool m_HasPrevPanelPose;
        Vector3 m_PrevPanelPos;
        Quaternion m_PrevPanelRot = Quaternion.identity;

        Vector3 m_FirstPoint;
        Vector3 m_SecondPoint;
        Vector3 m_ThirdPoint;

        void Awake()
        {
            CreateVisuals();
            EnsureHandSubsystem();
        }

        void Update()
        {
            EnsureHandSubsystem();

            if (m_IsSelectingSurface)
            {
                ProcessSelectionStep();
                return;
            }

            if (m_HasPlacedSurface)
                ProcessHeightHandleDrag();

            CarrySurfaceWithPanel();
        }

        // Rigidly carry the placed surface with the coaching panel while it is grabbed/moved, so the
        // injection guide cone and the syringe snap target travel with the UI. Only while grabbed —
        // head-follow of the panel does not drag the target.
        void CarrySurfaceWithPanel()
        {
            if (m_PanelGrab == null)
                m_PanelGrab = FindAnyObjectByType<FloatingCoachingUIGrab>();
            if (m_PanelGrab == null || m_SurfacePlane == null || !m_HasPlacedSurface)
            {
                m_HasPrevPanelPose = false;
                return;
            }

            var panel = m_PanelGrab.transform;
            if (m_PanelGrab.isGrabbed && m_HasPrevPanelPose &&
                !m_IsSelectingSurface && !m_IsDraggingHeightHandle)
            {
                // Re-express the surface in the panel's previous frame, then map back through the new
                // panel pose (equivalent to parenting to the panel for this frame's motion).
                var invOld = Quaternion.Inverse(m_PrevPanelRot);
                var localPos = invOld * (m_SurfacePlane.position - m_PrevPanelPos);
                var localRot = invOld * m_SurfacePlane.rotation;
                m_SurfacePlane.SetPositionAndRotation(
                    panel.position + panel.rotation * localPos,
                    panel.rotation * localRot);
                UpdateHeightHandleVisual();
            }

            m_PrevPanelPos = panel.position;
            m_PrevPanelRot = panel.rotation;
            m_HasPrevPanelPose = true;
        }

        void OnDestroy()
        {
            if (m_RuntimePointMaterial != null)
                Destroy(m_RuntimePointMaterial);
            if (m_RuntimeLineMaterial != null)
                Destroy(m_RuntimeLineMaterial);
            if (m_RuntimeSurfaceMaterial != null)
                Destroy(m_RuntimeSurfaceMaterial);
            if (m_RuntimeHandleMaterial != null)
                Destroy(m_RuntimeHandleMaterial);
        }

        public void BeginSurfaceSelection()
        {
            m_IsSelectingSurface = true;
            m_PinchEngaged = false;
            m_LockedSelectionHand = Handedness.Invalid;
            m_HasFirstPoint = false;
            m_HasSecondPoint = false;
            m_HasPlacedSurface = false;

            if (m_FirstPointDot != null)
                m_FirstPointDot.gameObject.SetActive(false);
            if (m_SecondPointDot != null)
                m_SecondPointDot.gameObject.SetActive(false);
            if (m_ThirdPointDot != null)
                m_ThirdPointDot.gameObject.SetActive(false);
            if (m_SurfacePlane != null)
                m_SurfacePlane.gameObject.SetActive(false);
            SetHeightHandleVisible(false);
            m_IsDraggingHeightHandle = false;
            if (m_FirstSegmentLine != null)
                m_FirstSegmentLine.enabled = false;
            if (m_SecondSegmentLine != null)
                m_SecondSegmentLine.enabled = false;
        }

        public void CancelSurfaceSelection()
        {
            m_IsSelectingSurface = false;
            m_PinchEngaged = false;
            m_LockedSelectionHand = Handedness.Invalid;
            m_HasFirstPoint = false;
            m_HasSecondPoint = false;
            m_IsDraggingHeightHandle = false;

            if (m_FirstPointDot != null)
                m_FirstPointDot.gameObject.SetActive(false);
            if (m_SecondPointDot != null)
                m_SecondPointDot.gameObject.SetActive(false);
            if (m_ThirdPointDot != null)
                m_ThirdPointDot.gameObject.SetActive(false);
            if (m_FirstSegmentLine != null)
                m_FirstSegmentLine.enabled = false;
            if (m_SecondSegmentLine != null)
                m_SecondSegmentLine.enabled = false;
        }

        /// <summary>Drops the target surface at the center of the headset's field of view, facing the
        /// user. Replaces the pinch workflow; also used by the Recenter button.</summary>
        public void RecenterToView()
        {
            var cam = Camera.main;
            if (cam != null)
                PlaceSurfaceAtView(cam, m_ViewPlaceDistance, m_ViewSurfaceDiameter);
        }

        /// <summary>True once a target has been placed (pinch or view).</summary>
        public void PlaceSurfaceAtView(Camera cam, float distance, float diameter)
        {
            if (m_SurfacePlane == null || cam == null)
                return;

            var camT = cam.transform;
            var center = camT.position + camT.forward * Mathf.Max(0.1f, distance);

            // Surface normal faces the user so the needle approaches from the user's side and the
            // depth channel runs away from them (into the virtual body).
            var normal = camT.position - center;
            normal = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;

            var planeRight = Vector3.ProjectOnPlane(camT.right, normal);
            if (planeRight.sqrMagnitude < 1e-6f)
                planeRight = Vector3.ProjectOnPlane(Vector3.right, normal);
            planeRight.Normalize();
            var planeForward = Vector3.Cross(planeRight, normal).normalized;

            m_IsSelectingSurface = false;
            m_PinchEngaged = false;
            m_HasFirstPoint = false;
            m_HasSecondPoint = false;
            m_HasPlacedSurface = true;
            m_IsDraggingHeightHandle = false;

            m_SurfacePlane.position = center;
            m_SurfacePlane.rotation = Quaternion.LookRotation(planeForward, normal);
            m_SurfacePlane.localScale = new Vector3(diameter / 10f, 1f, diameter / 10f);
            m_SurfacePlane.gameObject.SetActive(true);

            // No pinch dots / segments / height handle for a view-placed target.
            if (m_FirstPointDot != null)
                m_FirstPointDot.gameObject.SetActive(false);
            if (m_SecondPointDot != null)
                m_SecondPointDot.gameObject.SetActive(false);
            if (m_ThirdPointDot != null)
                m_ThirdPointDot.gameObject.SetActive(false);
            if (m_FirstSegmentLine != null)
                m_FirstSegmentLine.enabled = false;
            if (m_SecondSegmentLine != null)
                m_SecondSegmentLine.enabled = false;
            SetHeightHandleVisible(false);
        }

        public void ClearPlacedSurface()
        {
            m_IsSelectingSurface = false;
            m_PinchEngaged = false;
            m_LockedSelectionHand = Handedness.Invalid;
            m_HasFirstPoint = false;
            m_HasSecondPoint = false;
            m_HasPlacedSurface = false;

            if (m_FirstPointDot != null)
                m_FirstPointDot.gameObject.SetActive(false);
            if (m_SecondPointDot != null)
                m_SecondPointDot.gameObject.SetActive(false);
            if (m_ThirdPointDot != null)
                m_ThirdPointDot.gameObject.SetActive(false);
            if (m_SurfacePlane != null)
                m_SurfacePlane.gameObject.SetActive(false);
            SetHeightHandleVisible(false);
            m_IsDraggingHeightHandle = false;
            if (m_FirstSegmentLine != null)
                m_FirstSegmentLine.enabled = false;
            if (m_SecondSegmentLine != null)
                m_SecondSegmentLine.enabled = false;
        }

        void EnsureHandSubsystem()
        {
            if (m_HandSubsystem == null)
                TryGetHandSubsystem(out m_HandSubsystem);
        }

        void ProcessSelectionStep()
        {
            if (!TryGetPinchPose(out var pinchPoint, out var pinchDistance, out var pinchHand))
            {
                UpdateSelectionPreviewLines(default, hasPinchPose: false);
                return;
            }

            UpdateSelectionPreviewLines(pinchPoint, hasPinchPose: true);

            if (!m_PinchEngaged && pinchDistance <= m_PinchEngageDistance)
            {
                m_PinchEngaged = true;
                RegisterPinchPoint(pinchPoint, pinchHand);
            }
            else if (m_PinchEngaged && pinchDistance >= m_PinchReleaseDistance)
            {
                m_PinchEngaged = false;
            }
        }

        void UpdateSelectionPreviewLines(Vector3 pinchPoint, bool hasPinchPose)
        {
            if (m_FirstSegmentLine == null || m_SecondSegmentLine == null)
                return;

            if (!m_HasFirstPoint)
            {
                m_FirstSegmentLine.enabled = false;
                m_SecondSegmentLine.enabled = false;
                return;
            }

            if (!m_HasSecondPoint)
            {
                m_FirstSegmentLine.enabled = true;
                var dynamicEnd = hasPinchPose ? pinchPoint : m_FirstPoint;
                m_FirstSegmentLine.SetPosition(0, m_FirstPoint);
                m_FirstSegmentLine.SetPosition(1, dynamicEnd);
                m_SecondSegmentLine.enabled = false;
                return;
            }

            m_FirstSegmentLine.enabled = true;
            m_FirstSegmentLine.SetPosition(0, m_FirstPoint);
            m_FirstSegmentLine.SetPosition(1, m_SecondPoint);

            m_SecondSegmentLine.enabled = true;
            var secondDynamicEnd = hasPinchPose ? pinchPoint : m_SecondPoint;
            m_SecondSegmentLine.SetPosition(0, m_SecondPoint);
            m_SecondSegmentLine.SetPosition(1, secondDynamicEnd);
        }

        void RegisterPinchPoint(Vector3 pinchPoint, Handedness pinchHand)
        {
            if (!m_HasFirstPoint)
            {
                // Lock subsequent points to whichever hand placed the first point.
                m_LockedSelectionHand = pinchHand;
                m_FirstPoint = pinchPoint;
                m_HasFirstPoint = true;
                m_HasSecondPoint = false;
                m_HasPlacedSurface = false;

                if (m_FirstPointDot != null)
                {
                    m_FirstPointDot.position = m_FirstPoint;
                    m_FirstPointDot.gameObject.SetActive(true);
                }

                if (m_SecondPointDot != null)
                    m_SecondPointDot.gameObject.SetActive(false);
                if (m_ThirdPointDot != null)
                    m_ThirdPointDot.gameObject.SetActive(false);

                return;
            }

            if (!m_HasSecondPoint)
            {
                m_SecondPoint = pinchPoint;
                m_HasSecondPoint = true;

                if (m_SecondPointDot != null)
                {
                    m_SecondPointDot.position = m_SecondPoint;
                    m_SecondPointDot.gameObject.SetActive(true);
                }

                if (m_FirstSegmentLine != null)
                {
                    m_FirstSegmentLine.enabled = true;
                    m_FirstSegmentLine.SetPosition(0, m_FirstPoint);
                    m_FirstSegmentLine.SetPosition(1, m_SecondPoint);
                }

                return;
            }

            m_ThirdPoint = pinchPoint;
            m_HasPlacedSurface = true;
            m_IsSelectingSurface = false;
            m_HasFirstPoint = false;
            m_HasSecondPoint = false;
            m_LockedSelectionHand = Handedness.Invalid;

            if (m_ThirdPointDot != null)
            {
                m_ThirdPointDot.position = m_ThirdPoint;
                m_ThirdPointDot.gameObject.SetActive(true);
            }

            if (m_SecondSegmentLine != null)
            {
                m_SecondSegmentLine.enabled = true;
                m_SecondSegmentLine.SetPosition(0, m_SecondPoint);
                m_SecondSegmentLine.SetPosition(1, m_ThirdPoint);
            }

            UpdatePlaneFromPoints();
        }

        bool TryGetPinchPose(out Vector3 pinchPoint, out float pinchDistance)
            => TryGetPinchPose(out pinchPoint, out pinchDistance, out _);

        // The first point may be pinched with either hand (smallest thumb-index gap wins). Once a hand
        // is locked (m_LockedSelectionHand), only that hand is evaluated so later points stay on it.
        bool TryGetPinchPose(out Vector3 pinchPoint, out float pinchDistance, out Handedness pinchHand)
        {
            pinchPoint = default;
            pinchDistance = 0f;
            pinchHand = Handedness.Invalid;

            if (m_HandSubsystem == null || !m_HandSubsystem.running)
                return false;

            var found = false;
            var best = float.MaxValue;
            if (m_LockedSelectionHand != Handedness.Right)
                EvaluatePinchHand(m_HandSubsystem.leftHand, Handedness.Left, ref found, ref best, ref pinchPoint, ref pinchDistance, ref pinchHand);
            if (m_LockedSelectionHand != Handedness.Left)
                EvaluatePinchHand(m_HandSubsystem.rightHand, Handedness.Right, ref found, ref best, ref pinchPoint, ref pinchDistance, ref pinchHand);
            return found;
        }

        static void EvaluatePinchHand(XRHand hand, Handedness handedness, ref bool found, ref float best,
            ref Vector3 pinchPoint, ref float pinchDistance, ref Handedness pinchHand)
        {
            if (!hand.isTracked)
                return;
            if (!TryGetJointPose(hand, XRHandJointID.IndexTip, out var indexTipPose) ||
                !TryGetJointPose(hand, XRHandJointID.ThumbTip, out var thumbTipPose))
                return;

            var d = Vector3.Distance(indexTipPose.position, thumbTipPose.position);
            if (d < best)
            {
                best = d;
                pinchPoint = 0.5f * (indexTipPose.position + thumbTipPose.position);
                pinchDistance = d;
                pinchHand = handedness;
                found = true;
            }
        }

        void UpdatePlaneFromPoints()
        {
            if (m_SurfacePlane == null)
                return;

            var first = m_FirstPoint;
            var second = m_SecondPoint;
            var third = m_ThirdPoint;

            var y = (first.y + second.y + third.y) / 3f + m_SurfaceVerticalOffset;
            var firstFlat = new Vector3(first.x, y, first.z);
            var secondFlat = new Vector3(second.x, y, second.z);
            var thirdFlat = new Vector3(third.x, y, third.z);

            // Use three pinches to estimate plane orientation and rectangle extents from X/Z coordinates.
            var right = Vector3.ProjectOnPlane(secondFlat - firstFlat, Vector3.up);
            if (right.sqrMagnitude < 0.000001f)
                right = Vector3.ProjectOnPlane(thirdFlat - firstFlat, Vector3.up);
            if (right.sqrMagnitude < 0.000001f)
                right = Vector3.right;
            right.Normalize();

            var forward = Vector3.Cross(Vector3.up, right);
            if (forward.sqrMagnitude < 0.000001f)
                forward = Vector3.forward;
            forward.Normalize();

            var u0 = 0f;
            var v0 = 0f;
            var dSecond = secondFlat - firstFlat;
            var dThird = thirdFlat - firstFlat;
            var u1 = Vector3.Dot(dSecond, right);
            var v1 = Vector3.Dot(dSecond, forward);
            var u2 = Vector3.Dot(dThird, right);
            var v2 = Vector3.Dot(dThird, forward);

            var minU = Mathf.Min(u0, u1, u2);
            var maxU = Mathf.Max(u0, u1, u2);
            var minV = Mathf.Min(v0, v1, v2);
            var maxV = Mathf.Max(v0, v1, v2);

            var width = maxU - minU;
            var depth = maxV - minV;
            if (width < m_MinRenderableDimension)
                width = m_MinRenderableDimension;
            if (depth < m_MinRenderableDimension)
                depth = m_MinRenderableDimension;

            var center = firstFlat + right * ((minU + maxU) * 0.5f) + forward * ((minV + maxV) * 0.5f);

            m_FirstPoint = firstFlat;
            m_SecondPoint = secondFlat;
            m_ThirdPoint = thirdFlat;
            if (m_FirstPointDot != null)
                m_FirstPointDot.position = m_FirstPoint;
            if (m_SecondPointDot != null)
                m_SecondPointDot.position = m_SecondPoint;
            if (m_ThirdPointDot != null)
                m_ThirdPointDot.position = m_ThirdPoint;
            if (m_FirstSegmentLine != null)
            {
                m_FirstSegmentLine.enabled = true;
                m_FirstSegmentLine.SetPosition(0, m_FirstPoint);
                m_FirstSegmentLine.SetPosition(1, m_SecondPoint);
            }
            if (m_SecondSegmentLine != null)
            {
                m_SecondSegmentLine.enabled = true;
                m_SecondSegmentLine.SetPosition(0, m_SecondPoint);
                m_SecondSegmentLine.SetPosition(1, m_ThirdPoint);
            }

            m_SurfacePlane.position = center;
            m_SurfacePlane.rotation = Quaternion.LookRotation(forward, Vector3.up);
            m_SurfacePlane.localScale = new Vector3(width / 10f, 1f, depth / 10f);
            m_SurfacePlane.gameObject.SetActive(true);
            UpdateHeightHandleVisual();
            SetHeightHandleVisible(true);
        }

        void ProcessHeightHandleDrag()
        {
            UpdateHeightHandleVisual();

            if (!TryGetPinchPose(out var pinchPoint, out var pinchDistance))
            {
                m_IsDraggingHeightHandle = false;
                return;
            }

            if (m_IsDraggingHeightHandle)
            {
                if (pinchDistance >= m_PinchReleaseDistance)
                {
                    m_IsDraggingHeightHandle = false;
                    return;
                }

                var deltaY = pinchPoint.y - m_HeightDragStartY;
                ApplySurfaceVerticalOffset(m_HeightDragSurfaceStartY + deltaY);
                return;
            }

            if (pinchDistance > m_PinchEngageDistance)
                return;

            var handleGrabPoint = GetHeightHandleGrabPoint();
            if (Vector3.Distance(pinchPoint, handleGrabPoint) > m_HandleGrabRadius)
                return;

            m_IsDraggingHeightHandle = true;
            m_HeightDragStartY = pinchPoint.y;
            m_HeightDragSurfaceStartY = m_SurfacePlane.position.y;
        }

        void ApplySurfaceVerticalOffset(float targetY)
        {
            if (m_SurfacePlane == null)
                return;

            var deltaY = targetY - m_SurfacePlane.position.y;
            if (Mathf.Abs(deltaY) < 0.000001f)
                return;

            m_SurfacePlane.position += Vector3.up * deltaY;
            m_FirstPoint += Vector3.up * deltaY;
            m_SecondPoint += Vector3.up * deltaY;
            m_ThirdPoint += Vector3.up * deltaY;

            if (m_FirstPointDot != null)
                m_FirstPointDot.position = m_FirstPoint;
            if (m_SecondPointDot != null)
                m_SecondPointDot.position = m_SecondPoint;
            if (m_ThirdPointDot != null)
                m_ThirdPointDot.position = m_ThirdPoint;

            if (m_FirstSegmentLine != null)
            {
                m_FirstSegmentLine.SetPosition(0, m_FirstPoint);
                m_FirstSegmentLine.SetPosition(1, m_SecondPoint);
            }

            if (m_SecondSegmentLine != null)
            {
                m_SecondSegmentLine.SetPosition(0, m_SecondPoint);
                m_SecondSegmentLine.SetPosition(1, m_ThirdPoint);
            }

            UpdateHeightHandleVisual();
        }

        Vector3 GetHeightHandleGrabPoint()
        {
            if (m_HeightHandleKnob != null)
                return m_HeightHandleKnob.position;

            return m_SurfacePlane != null ? m_SurfacePlane.position : transform.position;
        }

        void UpdateHeightHandleVisual()
        {
            if (m_HeightHandleRoot == null || m_SurfacePlane == null || !m_HasPlacedSurface)
                return;

            m_HeightHandleRoot.position = m_SurfacePlane.position;
            m_HeightHandleRoot.rotation = m_SurfacePlane.rotation;

            var halfWidth = Mathf.Abs(m_SurfacePlane.lossyScale.x) * 5f;
            var edgeLocal = new Vector3(halfWidth, 0f, 0f);
            var edgeWorld = m_HeightHandleRoot.TransformPoint(edgeLocal);
            var stemTop = edgeWorld + Vector3.up * m_HandleStemHeight;

            if (m_HeightHandleStem != null)
            {
                m_HeightHandleStem.SetPosition(0, edgeWorld);
                m_HeightHandleStem.SetPosition(1, stemTop);
            }

            if (m_HeightHandleKnob != null)
                m_HeightHandleKnob.position = stemTop;
        }

        void SetHeightHandleVisible(bool visible)
        {
            if (m_HeightHandleRoot != null)
                m_HeightHandleRoot.gameObject.SetActive(visible);
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
            m_ThirdPointDot = CreatePoint("Surface Point C", m_RuntimePointMaterial, m_PointSize);
            m_FirstPointDot.gameObject.SetActive(false);
            m_SecondPointDot.gameObject.SetActive(false);
            m_ThirdPointDot.gameObject.SetActive(false);

            m_FirstSegmentLine = CreateLine("Surface Selection Line A-B", m_RuntimeLineMaterial);
            m_SecondSegmentLine = CreateLine("Surface Selection Line B-C", m_RuntimeLineMaterial);

            m_RuntimeHandleMaterial = CreateUnlitMaterial(m_HandleColor);
            m_HeightHandleRoot = new GameObject("Surface Height Handle").transform;
            m_HeightHandleRoot.SetParent(m_SurfaceRoot, false);
            m_HeightHandleStem = CreateLine("Surface Height Handle Stem", m_RuntimeHandleMaterial);
            m_HeightHandleStem.startWidth = m_HandleStemWidth;
            m_HeightHandleStem.endWidth = m_HandleStemWidth;
            m_HeightHandleStem.transform.SetParent(m_HeightHandleRoot, false);
            m_HeightHandleKnob = CreatePoint("Surface Height Handle Knob", m_RuntimeHandleMaterial, m_HandleKnobSize);
            m_HeightHandleKnob.SetParent(m_HeightHandleRoot, false);
            SetHeightHandleVisible(false);

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

        LineRenderer CreateLine(string name, Material material)
        {
            var lineObject = new GameObject(name);
            lineObject.transform.SetParent(m_SurfaceRoot, false);

            var line = lineObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.material = material;
            line.useWorldSpace = true;
            line.startWidth = m_LineWidth;
            line.endWidth = m_LineWidth;
            line.numCapVertices = 4;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off;
            line.enabled = false;
            return line;
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
