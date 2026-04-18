using System.Collections.Generic;
using UnityEngine.XR.Hands;

namespace UnityEngine.XR.Templates.MR
{
    /// <summary>
    /// Tracks a syringe-like overlay from left-hand joints and exposes key syringe points.
    /// </summary>
    public class SyringeOverlayTracker : MonoBehaviour
    {
        struct SyringePoints
        {
            public Vector3 plunger;
            public Vector3 leftWing;
            public Vector3 rightWing;
            public Vector3 wingsCenter;
            public Vector3 barrelEnd;
            public Vector3 needleBase;
            public Vector3 needleTip;
        }

        public struct SyringePoseData
        {
            public Vector3 plunger;
            public Vector3 leftWing;
            public Vector3 rightWing;
            public Vector3 wingsCenter;
            public Vector3 barrelEnd;
            public Vector3 needleBase;
            public Vector3 needleTip;
            public Vector3 forward;
        }

        [Header("Syringe dimensions (meters)")]
        [SerializeField, Min(0f)]
        float m_MinPlungerToWings = 0.016f;

        [SerializeField, Min(0f)]
        float m_MaxPlungerToWings = 0.065f;

        [SerializeField, Min(0f)]
        float m_WingsToBarrelEnd = 0.06f;

        [SerializeField, Min(0f)]
        float m_BarrelEndToNeedleTip = 0.048f;

        [SerializeField, Min(0f)]
        float m_MetalNeedleLength = 0.025f;

        [Header("Overlay visuals")]
        [SerializeField]
        Material m_LineMaterialOverride;

        [SerializeField]
        Color m_OverlayColor = new Color(0.0f, 0.95f, 1.0f, 0.95f);

        [SerializeField, Range(0.0005f, 0.01f)]
        float m_LineWidth = 0.0025f;

        [SerializeField, Range(0.001f, 0.012f)]
        float m_JointMarkerSize = 0.003f;

        [SerializeField]
        bool m_DrawJointMarkers = true;

        [SerializeField, Range(0f, 1f)]
        float m_PositionSmoothing = 0.35f;

        [Header("Direction stability")]
        [SerializeField]
        bool m_UseHandFrameDirectionAssist = true;

        [SerializeField, Range(0f, 1f)]
        float m_HandFrameDirectionBlend = 0.65f;

        [SerializeField, Range(0f, 1f)]
        float m_DirectionSmoothing = 0.6f;

        [SerializeField]
        bool m_AutoGripCalibration = true;

        [SerializeField, Range(0f, 1f)]
        float m_GripCalibrationRate = 0.15f;

        [Header("Debug state")]
        [SerializeField]
        bool m_HasValidTracking;

        [SerializeField]
        Vector3 m_PlungerPoint;

        [SerializeField]
        Vector3 m_WingsCenterPoint;

        [SerializeField]
        Vector3 m_BarrelEndPoint;

        [SerializeField]
        Vector3 m_NeedleTipPoint;

        public bool hasValidTracking => m_HasValidTracking;
        public Vector3 plungerPoint => m_PlungerPoint;
        public Vector3 leftWingPoint => m_SmoothedPoints.leftWing;
        public Vector3 rightWingPoint => m_SmoothedPoints.rightWing;
        public Vector3 wingsCenterPoint => m_WingsCenterPoint;
        public Vector3 barrelEndPoint => m_BarrelEndPoint;
        public Vector3 needleBasePoint => m_SmoothedPoints.needleBase;
        public Vector3 needleTipPoint => m_NeedleTipPoint;
        public Vector3 syringeDirection => (m_NeedleTipPoint - m_PlungerPoint).normalized;

        XRHandSubsystem m_HandSubsystem;
        static List<XRHandSubsystem> s_HandSubsystems;

        Transform m_OverlayRoot;
        LineRenderer m_PlungerLine;
        LineRenderer m_WingsLine;
        LineRenderer m_BarrelLine;
        LineRenderer m_HubLine;
        LineRenderer m_NeedleLine;

        Transform m_MarkersRoot;
        Transform m_PlungerMarker;
        Transform m_LeftWingMarker;
        Transform m_RightWingMarker;
        Transform m_WingsCenterMarker;
        Transform m_BarrelEndMarker;
        Transform m_NeedleBaseMarker;
        Transform m_NeedleTipMarker;

        Material m_RuntimeLineMaterial;
        bool m_OverlayVisible;
        bool m_HasSmoothedPose;
        bool m_HasSmoothedDirection;
        bool m_HasGripCalibration;
        SyringePoints m_SmoothedPoints;
        Vector3 m_SmoothedDirection;
        Vector3 m_LocalSyringeAxisInHand;

        void Awake()
        {
            CreateOverlayVisuals();
        }

        void OnEnable()
        {
            if (m_HandSubsystem == null)
                TryGetHandSubsystem(out m_HandSubsystem);
        }

        void OnDisable()
        {
            SetOverlayVisible(false);
            m_HasValidTracking = false;
            m_HasSmoothedDirection = false;
            m_HasGripCalibration = false;
        }

        void OnDestroy()
        {
            if (m_RuntimeLineMaterial != null)
                Destroy(m_RuntimeLineMaterial);
        }

        void Update()
        {
            EnsureHandSubsystem();

            if (m_HandSubsystem == null || !m_HandSubsystem.running)
            {
                SetOverlayVisible(false);
                m_HasValidTracking = false;
                m_HasSmoothedPose = false;
                m_HasSmoothedDirection = false;
                return;
            }

            var leftHand = m_HandSubsystem.leftHand;
            if (!leftHand.isTracked || !TryBuildSyringePoints(leftHand, out var points))
            {
                SetOverlayVisible(false);
                m_HasValidTracking = false;
                m_HasSmoothedPose = false;
                m_HasSmoothedDirection = false;
                return;
            }

            if (!m_HasSmoothedPose)
            {
                m_SmoothedPoints = points;
                m_HasSmoothedPose = true;
            }
            else
            {
                var t = 1f - Mathf.Pow(1f - m_PositionSmoothing, Time.deltaTime * 90f);
                SmoothTo(ref m_SmoothedPoints.plunger, points.plunger, t);
                SmoothTo(ref m_SmoothedPoints.leftWing, points.leftWing, t);
                SmoothTo(ref m_SmoothedPoints.rightWing, points.rightWing, t);
                SmoothTo(ref m_SmoothedPoints.wingsCenter, points.wingsCenter, t);
                SmoothTo(ref m_SmoothedPoints.barrelEnd, points.barrelEnd, t);
                SmoothTo(ref m_SmoothedPoints.needleBase, points.needleBase, t);
                SmoothTo(ref m_SmoothedPoints.needleTip, points.needleTip, t);
            }

            UpdateVisuals(m_SmoothedPoints);
            SetOverlayVisible(true);

            m_HasValidTracking = true;
            m_PlungerPoint = m_SmoothedPoints.plunger;
            m_WingsCenterPoint = m_SmoothedPoints.wingsCenter;
            m_BarrelEndPoint = m_SmoothedPoints.barrelEnd;
            m_NeedleTipPoint = m_SmoothedPoints.needleTip;
        }

        public bool TryGetSyringePose(out SyringePoseData pose)
        {
            if (!m_HasValidTracking)
            {
                pose = default;
                return false;
            }

            var forward = (m_SmoothedPoints.needleTip - m_SmoothedPoints.plunger).normalized;
            pose = new SyringePoseData
            {
                plunger = m_SmoothedPoints.plunger,
                leftWing = m_SmoothedPoints.leftWing,
                rightWing = m_SmoothedPoints.rightWing,
                wingsCenter = m_SmoothedPoints.wingsCenter,
                barrelEnd = m_SmoothedPoints.barrelEnd,
                needleBase = m_SmoothedPoints.needleBase,
                needleTip = m_SmoothedPoints.needleTip,
                forward = forward,
            };

            return true;
        }

        public void ResetGripCalibration()
        {
            m_HasGripCalibration = false;
        }

        void EnsureHandSubsystem()
        {
            if (m_HandSubsystem != null && m_HandSubsystem.running)
                return;

            TryGetHandSubsystem(out m_HandSubsystem);
        }

        static void SmoothTo(ref Vector3 current, Vector3 target, float t)
        {
            current = Vector3.Lerp(current, target, t);
        }

        bool TryBuildSyringePoints(XRHand leftHand, out SyringePoints points)
        {
            points = default;

            if (!TryGetJointPose(leftHand, XRHandJointID.ThumbTip, out var thumbTip) ||
                !TryGetJointPose(leftHand, XRHandJointID.ThumbDistal, out var thumbIp) ||
                !TryGetJointPose(leftHand, XRHandJointID.IndexTip, out var indexTip) ||
                !TryGetJointPose(leftHand, XRHandJointID.IndexDistal, out var indexDip) ||
                !TryGetJointPose(leftHand, XRHandJointID.IndexIntermediate, out var indexPip) ||
                !TryGetJointPose(leftHand, XRHandJointID.MiddleTip, out var middleTip) ||
                !TryGetJointPose(leftHand, XRHandJointID.MiddleDistal, out var middleDip) ||
                !TryGetJointPose(leftHand, XRHandJointID.MiddleIntermediate, out var middlePip))
            {
                return false;
            }

            var plunger = 0.5f * (thumbTip.position + thumbIp.position);
            var indexWingRaw = BlendFingerSupport(indexTip.position, indexDip.position, indexPip.position);
            var middleWingRaw = BlendFingerSupport(middleTip.position, middleDip.position, middlePip.position);
            var wingsCenterRaw = 0.5f * (indexWingRaw + middleWingRaw);

            var axisVector = wingsCenterRaw - plunger;
            if (axisVector.sqrMagnitude < 0.0000001f)
                return false;

            var axisFromContacts = axisVector.normalized;
            var axis = GetStabilizedDirection(leftHand, axisFromContacts, indexWingRaw, middleWingRaw);

            var projectedDistance = Mathf.Abs(Vector3.Dot(axisVector, axis));
            if (projectedDistance < 0.0001f)
                projectedDistance = axisVector.magnitude;

            var plungerToWingsDistance = Mathf.Clamp(projectedDistance, m_MinPlungerToWings, m_MaxPlungerToWings);
            var wingsCenter = plunger + axis * plungerToWingsDistance;

            var wingOffset = Vector3.ProjectOnPlane(indexWingRaw - middleWingRaw, axis);
            Vector3 leftWing;
            Vector3 rightWing;
            if (wingOffset.sqrMagnitude < 0.0000001f)
            {
                var sideAxis = Vector3.ProjectOnPlane(leftHand.rootPose.right, axis).normalized;
                if (sideAxis.sqrMagnitude < 0.0000001f)
                    sideAxis = Vector3.right;

                var halfWingSpan = 0.01f;
                leftWing = wingsCenter + sideAxis * halfWingSpan;
                rightWing = wingsCenter - sideAxis * halfWingSpan;
            }
            else
            {
                var halfWing = wingOffset.normalized * Mathf.Max(0.005f, wingOffset.magnitude * 0.5f);
                leftWing = wingsCenter + halfWing;
                rightWing = wingsCenter - halfWing;
            }

            var barrelEnd = wingsCenter + axis * m_WingsToBarrelEnd;
            var needleTip = barrelEnd + axis * m_BarrelEndToNeedleTip;
            var metalNeedleLength = Mathf.Min(m_MetalNeedleLength, m_BarrelEndToNeedleTip);
            var needleBase = needleTip - axis * metalNeedleLength;

            points.plunger = plunger;
            points.leftWing = leftWing;
            points.rightWing = rightWing;
            points.wingsCenter = wingsCenter;
            points.barrelEnd = barrelEnd;
            points.needleBase = needleBase;
            points.needleTip = needleTip;
            return true;
        }

        Vector3 GetStabilizedDirection(XRHand leftHand, Vector3 axisFromContacts, Vector3 indexWingRaw, Vector3 middleWingRaw)
        {
            var axis = axisFromContacts;

            if (m_UseHandFrameDirectionAssist)
            {
                if (TryGetCalibratedHandDirection(leftHand, axisFromContacts, out var axisFromHandFrame))
                {
                    axis = Vector3.Slerp(axisFromContacts, axisFromHandFrame, m_HandFrameDirectionBlend).normalized;
                }
                else
                {
                    var sideAxis = indexWingRaw - middleWingRaw;
                    if (sideAxis.sqrMagnitude < 0.0000001f)
                        sideAxis = leftHand.rootPose.right;

                    sideAxis = Vector3.ProjectOnPlane(sideAxis, axisFromContacts);
                    if (sideAxis.sqrMagnitude < 0.0000001f)
                        sideAxis = Vector3.ProjectOnPlane(leftHand.rootPose.right, axisFromContacts);

                    if (sideAxis.sqrMagnitude > 0.0000001f)
                    {
                        sideAxis.Normalize();
                        var palmNormal = leftHand.rootPose.up;
                        palmNormal = Vector3.ProjectOnPlane(palmNormal, sideAxis);

                        if (palmNormal.sqrMagnitude > 0.0000001f)
                        {
                            palmNormal.Normalize();
                            axisFromHandFrame = Vector3.Cross(sideAxis, palmNormal).normalized;
                            if (axisFromHandFrame.sqrMagnitude > 0.0000001f)
                            {
                                if (Vector3.Dot(axisFromHandFrame, axisFromContacts) < 0f)
                                    axisFromHandFrame = -axisFromHandFrame;

                                axis = Vector3.Slerp(axisFromContacts, axisFromHandFrame, m_HandFrameDirectionBlend).normalized;
                            }
                        }
                    }
                }
            }

            if (m_HasSmoothedDirection && Vector3.Dot(axis, m_SmoothedDirection) < 0f)
                axis = -axis;

            if (!m_HasSmoothedDirection)
            {
                m_SmoothedDirection = axis;
                m_HasSmoothedDirection = true;
                return axis;
            }

            var t = 1f - Mathf.Pow(1f - m_DirectionSmoothing, Time.deltaTime * 90f);
            m_SmoothedDirection = Vector3.Slerp(m_SmoothedDirection, axis, t).normalized;
            return m_SmoothedDirection;
        }

        bool TryGetCalibratedHandDirection(XRHand leftHand, Vector3 axisFromContacts, out Vector3 axisFromHandFrame)
        {
            axisFromHandFrame = default;

            var handRotation = leftHand.rootPose.rotation;
            var contactAxisLocal = Quaternion.Inverse(handRotation) * axisFromContacts;
            if (contactAxisLocal.sqrMagnitude < 0.0000001f)
                return false;

            contactAxisLocal.Normalize();

            if (!m_HasGripCalibration)
            {
                m_LocalSyringeAxisInHand = contactAxisLocal;
                m_HasGripCalibration = true;
            }
            else if (m_AutoGripCalibration)
            {
                if (Vector3.Dot(m_LocalSyringeAxisInHand, contactAxisLocal) < 0f)
                    contactAxisLocal = -contactAxisLocal;

                var t = 1f - Mathf.Pow(1f - m_GripCalibrationRate, Time.deltaTime * 90f);
                m_LocalSyringeAxisInHand = Vector3.Slerp(m_LocalSyringeAxisInHand, contactAxisLocal, t).normalized;
            }

            axisFromHandFrame = (handRotation * m_LocalSyringeAxisInHand).normalized;
            if (axisFromHandFrame.sqrMagnitude < 0.0000001f)
                return false;

            if (Vector3.Dot(axisFromHandFrame, axisFromContacts) < 0f)
                axisFromHandFrame = -axisFromHandFrame;

            return true;
        }

        static Vector3 BlendFingerSupport(Vector3 tip, Vector3 dip, Vector3 pip)
        {
            return 0.2f * tip + 0.3f * dip + 0.5f * pip;
        }

        static bool TryGetJointPose(XRHand hand, XRHandJointID jointId, out Pose pose)
        {
            var joint = hand.GetJoint(jointId);
            return joint.TryGetPose(out pose);
        }

        void CreateOverlayVisuals()
        {
            if (m_OverlayRoot != null)
                return;

            m_OverlayRoot = new GameObject("Syringe Overlay").transform;
            m_OverlayRoot.SetParent(transform, false);

            var lineMaterial = GetLineMaterial();
            m_PlungerLine = CreateLine("Plunger Segment", lineMaterial);
            m_WingsLine = CreateLine("Wing Segment", lineMaterial);
            m_BarrelLine = CreateLine("Barrel Segment", lineMaterial);
            m_HubLine = CreateLine("Hub Segment", lineMaterial);
            m_NeedleLine = CreateLine("Needle Segment", lineMaterial);

            m_MarkersRoot = new GameObject("Syringe Markers").transform;
            m_MarkersRoot.SetParent(m_OverlayRoot, false);
            m_PlungerMarker = CreateMarker("Plunger Marker", lineMaterial);
            m_LeftWingMarker = CreateMarker("Left Wing Marker", lineMaterial);
            m_RightWingMarker = CreateMarker("Right Wing Marker", lineMaterial);
            m_WingsCenterMarker = CreateMarker("Wings Center Marker", lineMaterial);
            m_BarrelEndMarker = CreateMarker("Barrel End Marker", lineMaterial);
            m_NeedleBaseMarker = CreateMarker("Needle Base Marker", lineMaterial);
            m_NeedleTipMarker = CreateMarker("Needle Tip Marker", lineMaterial);

            SetOverlayVisible(false);
        }

        Material GetLineMaterial()
        {
            if (m_LineMaterialOverride != null)
                return m_LineMaterialOverride;

            if (m_RuntimeLineMaterial != null)
                return m_RuntimeLineMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Standard");

            if (shader == null)
            {
                Debug.LogError("SyringeOverlayTracker could not find a shader for line rendering.", this);
                return null;
            }

            m_RuntimeLineMaterial = new Material(shader);

            if (m_RuntimeLineMaterial.HasProperty("_BaseColor"))
                m_RuntimeLineMaterial.SetColor("_BaseColor", m_OverlayColor);
            if (m_RuntimeLineMaterial.HasProperty("_Color"))
                m_RuntimeLineMaterial.SetColor("_Color", m_OverlayColor);

            return m_RuntimeLineMaterial;
        }

        LineRenderer CreateLine(string lineName, Material lineMaterial)
        {
            var lineObject = new GameObject(lineName);
            lineObject.transform.SetParent(m_OverlayRoot, false);

            var lineRenderer = lineObject.AddComponent<LineRenderer>();
            lineRenderer.positionCount = 2;
            lineRenderer.useWorldSpace = true;
            lineRenderer.material = lineMaterial;
            lineRenderer.startWidth = m_LineWidth;
            lineRenderer.endWidth = m_LineWidth;
            lineRenderer.numCapVertices = 4;
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;
            lineRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            return lineRenderer;
        }

        Transform CreateMarker(string markerName, Material markerMaterial)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = markerName;
            marker.transform.SetParent(m_MarkersRoot, false);
            marker.transform.localScale = Vector3.one * m_JointMarkerSize;

            if (marker.TryGetComponent<Collider>(out var collider))
                Destroy(collider);

            if (marker.TryGetComponent<Renderer>(out var renderer))
                renderer.sharedMaterial = markerMaterial;

            return marker.transform;
        }

        void UpdateVisuals(SyringePoints points)
        {
            m_PlungerLine.startWidth = m_LineWidth;
            m_PlungerLine.endWidth = m_LineWidth;
            m_WingsLine.startWidth = m_LineWidth;
            m_WingsLine.endWidth = m_LineWidth;
            m_BarrelLine.startWidth = m_LineWidth;
            m_BarrelLine.endWidth = m_LineWidth;
            m_HubLine.startWidth = m_LineWidth;
            m_HubLine.endWidth = m_LineWidth;
            m_NeedleLine.startWidth = Mathf.Max(0.0004f, m_LineWidth * 0.6f);
            m_NeedleLine.endWidth = Mathf.Max(0.0004f, m_LineWidth * 0.6f);

            m_PlungerLine.SetPosition(0, points.plunger);
            m_PlungerLine.SetPosition(1, points.wingsCenter);
            m_WingsLine.SetPosition(0, points.leftWing);
            m_WingsLine.SetPosition(1, points.rightWing);
            m_BarrelLine.SetPosition(0, points.wingsCenter);
            m_BarrelLine.SetPosition(1, points.barrelEnd);
            m_HubLine.SetPosition(0, points.barrelEnd);
            m_HubLine.SetPosition(1, points.needleBase);
            m_NeedleLine.SetPosition(0, points.needleBase);
            m_NeedleLine.SetPosition(1, points.needleTip);

            m_PlungerMarker.position = points.plunger;
            m_LeftWingMarker.position = points.leftWing;
            m_RightWingMarker.position = points.rightWing;
            m_WingsCenterMarker.position = points.wingsCenter;
            m_BarrelEndMarker.position = points.barrelEnd;
            m_NeedleBaseMarker.position = points.needleBase;
            m_NeedleTipMarker.position = points.needleTip;

            var markerState = m_OverlayVisible && m_DrawJointMarkers;
            m_PlungerMarker.gameObject.SetActive(markerState);
            m_LeftWingMarker.gameObject.SetActive(markerState);
            m_RightWingMarker.gameObject.SetActive(markerState);
            m_WingsCenterMarker.gameObject.SetActive(markerState);
            m_BarrelEndMarker.gameObject.SetActive(markerState);
            m_NeedleBaseMarker.gameObject.SetActive(markerState);
            m_NeedleTipMarker.gameObject.SetActive(markerState);
        }

        void SetOverlayVisible(bool visible)
        {
            if (m_OverlayVisible == visible)
                return;

            m_OverlayVisible = visible;

            if (m_PlungerLine == null || m_WingsLine == null || m_BarrelLine == null || m_HubLine == null || m_NeedleLine == null)
                return;

            m_PlungerLine.enabled = visible;
            m_WingsLine.enabled = visible;
            m_BarrelLine.enabled = visible;
            m_HubLine.enabled = visible;
            m_NeedleLine.enabled = visible;

            var markerState = visible && m_DrawJointMarkers;
            if (m_PlungerMarker != null)
                m_PlungerMarker.gameObject.SetActive(markerState);
            if (m_LeftWingMarker != null)
                m_LeftWingMarker.gameObject.SetActive(markerState);
            if (m_RightWingMarker != null)
                m_RightWingMarker.gameObject.SetActive(markerState);
            if (m_WingsCenterMarker != null)
                m_WingsCenterMarker.gameObject.SetActive(markerState);
            if (m_BarrelEndMarker != null)
                m_BarrelEndMarker.gameObject.SetActive(markerState);
            if (m_NeedleBaseMarker != null)
                m_NeedleBaseMarker.gameObject.SetActive(markerState);
            if (m_NeedleTipMarker != null)
                m_NeedleTipMarker.gameObject.SetActive(markerState);
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

            if (s_HandSubsystems.Count > 1)
            {
                for (var i = 0; i < s_HandSubsystems.Count; ++i)
                {
                    handSubsystem = s_HandSubsystems[i];
                    if (handSubsystem.running)
                        return true;
                }
            }

            handSubsystem = s_HandSubsystems[0];
            return true;
        }
    }
}