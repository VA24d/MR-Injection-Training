using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Hands;

namespace UnityEngine.XR.Templates.MR
{
    /// <summary>
    /// Tracks a syringe-like overlay from left-hand joints and exposes key syringe points.
    /// Includes marker-assisted calibration/tracking to stabilize direction under motion.
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

        struct MarkerDetectionDebugInfo
        {
            public Vector2 centerScreen;
            public Vector2 searchMinScreen;
            public Vector2 searchMaxScreen;
            public Vector2 matchedScreen;
            public Vector3 matchedHsv;
            public int hitCount;
            public float bestScore;
            public bool hasMatch;
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

        [SerializeField]
        bool m_TrackLeftHand = true;

        [SerializeField, Range(0f, 1f)]
        float m_HandFrameDirectionBlend = 0.72f;

        [SerializeField, Range(0f, 1f)]
        float m_DirectionSmoothing = 0.68f;

        [SerializeField]
        bool m_AutoGripCalibration = true;

        [SerializeField, Range(0f, 1f)]
        float m_GripCalibrationRate = 0.15f;

        [Header("Marker calibration")]
        [SerializeField]
        bool m_EnableMarkerAssist = true;

        [SerializeField, Min(1)]
        int m_RequiredCalibrationTaps = 4;

        [SerializeField, Min(0.005f)]
        float m_RightTapDistanceThreshold = 0.022f;

        [SerializeField, Min(0.01f)]
        float m_RightTapArmingSpeed = 0.18f;

        [SerializeField, Min(0.005f)]
        float m_RightTapReleaseSpeed = 0.05f;

        [SerializeField, Min(0.05f)]
        float m_RightTapCooldown = 0.2f;

        [SerializeField, Range(0f, 1f)]
        float m_MarkerDirectionBlend = 0.8f;

        [SerializeField, Range(0f, 1f)]
        float m_MarkerPredictionSmoothing = 0.18f;

        [SerializeField, Range(0f, 1f)]
        float m_MarkerVisibilityDecayPerSecond = 0.35f;

        [SerializeField, Range(0f, 1f)]
        float m_MarkerVisibilityRecoverPerSecond = 0.9f;

        [SerializeField, Min(8f)]
        float m_MarkerSearchRadiusPixels = 42f;

        [SerializeField, Range(0.01f, 0.3f)]
        float m_MarkerColorTolerance = 0.16f;

        [SerializeField, Range(0.01f, 0.2f)]
        float m_CpuImageUpdateInterval = 0.05f;

        [SerializeField]
        XRCpuImage.Transformation m_CpuImageTransformation = XRCpuImage.Transformation.None;

        [SerializeField]
        Color m_MarkerDotColor = new Color(1f, 0.92f, 0.1f, 0.95f);

        [SerializeField, Range(0.001f, 0.012f)]
        float m_MarkerDotSize = 0.0045f;

        [SerializeField]
        bool m_LogCalibrationEvents;

        [Header("Marker debugging")]
        [SerializeField]
        bool m_ForceMarkerOnlyAfterCalibration = true;

        [SerializeField]
        bool m_EnableMarkerDebug = true;

        [SerializeField]
        bool m_ShowSearchRegion = true;

        [SerializeField]
        bool m_ShowMarkerTrail = true;

        [SerializeField]
        bool m_ShowMarkerDebugText = true;

        [SerializeField]
        bool m_UseDisplayMatrixForCpuMapping = false;

        [SerializeField, Min(1)]
        int m_MarkerSearchStep = 1;

        [SerializeField, Min(1)]
        int m_MinMarkerHitsForDetection = 2;

        [SerializeField, Min(1f)]
        float m_MaxMarkerJumpPixels = 72f;

        [SerializeField, Range(8, 256)]
        int m_MarkerTrailMaxPoints = 96;

        [SerializeField, Min(0.0005f)]
        float m_MarkerTrailMinPointDistance = 0.002f;

        [SerializeField]
        Color m_MarkerTrailColor = new Color(1f, 0.2f, 0.2f, 0.9f);

        [SerializeField]
        Color m_SearchRegionColor = new Color(1f, 0.55f, 0.1f, 0.95f);

        [SerializeField]
        Color m_DetectedCandidateColor = new Color(0.1f, 1f, 0.1f, 0.95f);

        [Header("Debug state")]
        [SerializeField]
        bool m_HasValidTracking;

        [SerializeField]
        bool m_IsCalibratingMarker;

        [SerializeField]
        bool m_IsMarkerCalibrated;

        [SerializeField]
        int m_CalibrationTapCount;

        [SerializeField, Range(0f, 1f)]
        float m_MarkerConfidence;

        [SerializeField]
        Vector3 m_TargetMarkerHsv;

        [SerializeField]
        Vector3 m_LastMatchedMarkerHsv;

        [SerializeField]
        int m_LastMarkerHitCount;

        [SerializeField]
        float m_LastMarkerBestScore;

        [SerializeField]
        bool m_LastMarkerVisible;

        [SerializeField]
        Vector3 m_MarkerWorldPoint;

        [SerializeField]
        Vector3 m_PlungerPoint;

        [SerializeField]
        Vector3 m_WingsCenterPoint;

        [SerializeField]
        Vector3 m_BarrelEndPoint;

        [SerializeField]
        Vector3 m_NeedleTipPoint;

        public bool hasValidTracking => m_HasValidTracking;
        public bool isTrackingLeftHand => m_TrackLeftHand;
        public Vector3 plungerPoint => m_PlungerPoint;
        public Vector3 leftWingPoint => m_SmoothedPoints.leftWing;
        public Vector3 rightWingPoint => m_SmoothedPoints.rightWing;
        public Vector3 wingsCenterPoint => m_WingsCenterPoint;
        public Vector3 barrelEndPoint => m_BarrelEndPoint;
        public Vector3 needleBasePoint => m_SmoothedPoints.needleBase;
        public Vector3 needleTipPoint => m_NeedleTipPoint;
        public Vector3 syringeDirection => (m_NeedleTipPoint - m_PlungerPoint).normalized;

        public bool isCalibratingMarker => m_IsCalibratingMarker;
        public bool isMarkerCalibrated => m_IsMarkerCalibrated;
        public int calibrationTapCount => m_CalibrationTapCount;
        public int requiredCalibrationTaps => m_RequiredCalibrationTaps;
        public float markerConfidence => m_MarkerConfidence;
        public Vector3 markerWorldPoint => m_MarkerWorldPoint;

        XRHandSubsystem m_HandSubsystem;
        static List<XRHandSubsystem> s_HandSubsystems;

        Camera m_MainCamera;
        ARCameraManager m_ARCameraManager;
        Matrix4x4 m_LastDisplayMatrix;
        Matrix4x4 m_InverseDisplayMatrix;
        bool m_HasDisplayMatrix;
        bool m_HasInverseDisplayMatrix;
        Texture2D m_CpuImageTexture;
        int m_CpuImageWidth;
        int m_CpuImageHeight;
        float m_LastCpuImageUpdateTime;

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
        Transform m_MarkerDot;
        Transform m_DetectedMarker;
        LineRenderer m_MarkerTrailLine;
        LineRenderer m_SearchRegionLine;
        TextMesh m_MarkerDebugText;

        Material m_RuntimeLineMaterial;
        Material m_RuntimeMarkerDotMaterial;
        bool m_OverlayVisible;
        bool m_HasSmoothedPose;
        bool m_HasSmoothedDirection;
        bool m_HasGripCalibration;
        SyringePoints m_SmoothedPoints;
        Vector3 m_SmoothedDirection;
        Vector3 m_LocalSyringeAxisInHand;

        bool m_RightTapArmed;
        float m_RightTapArmedAt;
        float m_LastRightTapTime;
        bool m_HasPreviousRightTip;
        Vector3 m_PreviousRightTipPosition;

        readonly List<Color> m_MarkerColorSamples = new();
        readonly List<Vector3> m_MarkerTapWorldSamples = new();
        Vector3 m_MarkerColorHsv;
        bool m_HasMarkerColor;
        bool m_HasMarkerDepth;
        float m_MarkerDepthFromCamera;
        bool m_HasLocalMarkerOffset;
        Vector3 m_LocalMarkerOffsetInHand;
        readonly Queue<Vector3> m_MarkerTrailPoints = new();
        MarkerDetectionDebugInfo m_LastMarkerDebugInfo;
        Vector3 m_LastPredictedMarkerWorld;
        readonly StringBuilder m_DebugTextBuilder = new();
        MaterialPropertyBlock m_MaterialPropertyBlock;

        void Awake()
        {
            CreateOverlayVisuals();
            EnsureCameraReferences();
        }

        void OnEnable()
        {
            if (m_HandSubsystem == null)
                TryGetHandSubsystem(out m_HandSubsystem);

            EnsureCameraReferences();
            if (m_ARCameraManager != null)
                m_ARCameraManager.frameReceived += OnCameraFrameReceived;
        }

        void OnDisable()
        {
            if (m_ARCameraManager != null)
                m_ARCameraManager.frameReceived -= OnCameraFrameReceived;

            SetOverlayVisible(false);
            m_HasValidTracking = false;
            m_HasSmoothedDirection = false;
            m_HasGripCalibration = false;
            m_HasSmoothedPose = false;
            m_HasPreviousRightTip = false;
            m_HasDisplayMatrix = false;
            m_HasInverseDisplayMatrix = false;
            ClearMarkerDebugVisuals();
        }

        void OnDestroy()
        {
            if (m_RuntimeLineMaterial != null)
                Destroy(m_RuntimeLineMaterial);
            if (m_RuntimeMarkerDotMaterial != null)
                Destroy(m_RuntimeMarkerDotMaterial);
            if (m_CpuImageTexture != null)
                Destroy(m_CpuImageTexture);
        }

        void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
        {
            if (eventArgs.displayMatrix.HasValue)
            {
                m_LastDisplayMatrix = eventArgs.displayMatrix.Value;
                m_HasDisplayMatrix = true;
                m_HasInverseDisplayMatrix = TryGetInverseDisplayMatrix(m_LastDisplayMatrix, out m_InverseDisplayMatrix);
            }
        }

        void Update()
        {
            EnsureHandSubsystem();
            EnsureCameraReferences();

            if (m_HandSubsystem == null || !m_HandSubsystem.running)
            {
                SetOverlayVisible(false);
                m_HasValidTracking = false;
                m_HasSmoothedPose = false;
                m_HasSmoothedDirection = false;
                m_HasPreviousRightTip = false;
                ClearMarkerDebugVisuals();
                return;
            }

            var syringeHand = m_TrackLeftHand ? m_HandSubsystem.leftHand : m_HandSubsystem.rightHand;
            if (!syringeHand.isTracked || !TryBuildSyringePoints(syringeHand, out var points))
            {
                SetOverlayVisible(false);
                m_HasValidTracking = false;
                m_HasSmoothedPose = false;
                m_HasSmoothedDirection = false;
                m_HasPreviousRightTip = false;
                ClearMarkerDebugVisuals();
                return;
            }

            var tapHand = m_TrackLeftHand ? m_HandSubsystem.rightHand : m_HandSubsystem.leftHand;
            if (m_IsCalibratingMarker)
                ProcessCalibrationTapInput(syringeHand, tapHand, points);

            if (m_EnableMarkerAssist && m_IsMarkerCalibrated)
                ApplyMarkerAssistedDirection(syringeHand, ref points);

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
            UpdateMarkerDotVisual();
            UpdateMarkerDebugVisuals();
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

        public void StartMarkerCalibration()
        {
            m_IsCalibratingMarker = true;
            m_CalibrationTapCount = 0;
            m_MarkerColorSamples.Clear();
            m_MarkerTapWorldSamples.Clear();
            m_HasMarkerColor = false;
            m_RightTapArmed = false;
            m_HasPreviousRightTip = false;
            m_LastMarkerDebugInfo = default;
            m_LastMarkerVisible = false;
            m_LastMarkerHitCount = 0;
            m_LastMarkerBestScore = 0f;
            m_LastMatchedMarkerHsv = Vector3.zero;
            m_MarkerTrailPoints.Clear();

            if (m_LogCalibrationEvents)
                Debug.Log("[Syringe] Marker calibration started.", this);
        }

        public void CancelMarkerCalibration()
        {
            m_IsCalibratingMarker = false;
            m_CalibrationTapCount = 0;
            m_MarkerColorSamples.Clear();
            m_MarkerTapWorldSamples.Clear();
            m_RightTapArmed = false;
            m_HasPreviousRightTip = false;

            if (m_LogCalibrationEvents)
                Debug.Log("[Syringe] Marker calibration canceled.", this);
        }

        public void ResetMarkerCalibration()
        {
            CancelMarkerCalibration();
            m_IsMarkerCalibrated = false;
            m_MarkerConfidence = 0f;
            m_HasMarkerColor = false;
            m_HasMarkerDepth = false;
            m_HasLocalMarkerOffset = false;
            m_MarkerWorldPoint = Vector3.zero;
            m_TargetMarkerHsv = Vector3.zero;
            m_LastMatchedMarkerHsv = Vector3.zero;
            m_LastMarkerHitCount = 0;
            m_LastMarkerBestScore = 0f;
            m_LastMarkerVisible = false;
            m_LastMarkerDebugInfo = default;
            m_MarkerTrailPoints.Clear();
            UpdateMarkerDotVisual();
            ClearMarkerDebugVisuals();

            if (m_LogCalibrationEvents)
                Debug.Log("[Syringe] Marker calibration reset.", this);
        }

        public void ResetGripCalibration()
        {
            m_HasGripCalibration = false;
        }

        public void SetTrackingHand(bool trackLeftHand)
        {
            if (m_TrackLeftHand == trackLeftHand)
                return;

            m_TrackLeftHand = trackLeftHand;
            ResetTrackingStateForHandSwap();
        }

        public void SwapTrackingHand()
        {
            SetTrackingHand(!m_TrackLeftHand);
        }

        void ResetTrackingStateForHandSwap()
        {
            m_HasValidTracking = false;
            m_HasSmoothedPose = false;
            m_HasSmoothedDirection = false;
            m_HasGripCalibration = false;
            m_HasPreviousRightTip = false;
            m_RightTapArmed = false;
            m_CalibrationTapCount = 0;
            m_MarkerTapWorldSamples.Clear();
            m_MarkerColorSamples.Clear();
            ClearMarkerDebugVisuals();
            SetOverlayVisible(false);
        }

        void EnsureHandSubsystem()
        {
            if (m_HandSubsystem != null && m_HandSubsystem.running)
                return;

            TryGetHandSubsystem(out m_HandSubsystem);

            // Subsystem can be created but not running yet (or was stopped). Syringe/UI depend on
            // joint updates — start when available so hand-driven overlays and interactions work.
            if (m_HandSubsystem != null && !m_HandSubsystem.running)
                m_HandSubsystem.Start();
        }

        void EnsureCameraReferences()
        {
            if (m_MainCamera == null)
                m_MainCamera = Camera.main;

            if (m_ARCameraManager == null)
                m_ARCameraManager = FindAnyObjectByType<ARCameraManager>();
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

        void ProcessCalibrationTapInput(XRHand leftHand, XRHand rightHand, in SyringePoints currentPoints)
        {
            if (!TryDetectRightIndexTap(rightHand, currentPoints, out var rightIndexPose))
                return;

            m_CalibrationTapCount++;
            m_MarkerTapWorldSamples.Add(rightIndexPose.position);

            if (TrySampleMarkerColorAtWorldPoint(rightIndexPose.position, out var sampledColor))
                m_MarkerColorSamples.Add(sampledColor);

            if (m_LogCalibrationEvents)
                Debug.Log($"[Syringe] Calibration tap {m_CalibrationTapCount}/{m_RequiredCalibrationTaps}", this);

            if (m_CalibrationTapCount >= m_RequiredCalibrationTaps)
                FinalizeMarkerCalibration(leftHand, currentPoints);
        }

        bool TryDetectRightIndexTap(XRHand rightHand, in SyringePoints currentPoints, out Pose rightIndexTipPose)
        {
            rightIndexTipPose = default;
            if (!rightHand.isTracked || !TryGetJointPose(rightHand, XRHandJointID.IndexTip, out rightIndexTipPose))
            {
                m_HasPreviousRightTip = false;
                m_RightTapArmed = false;
                return false;
            }

            if (!m_HasPreviousRightTip)
            {
                m_PreviousRightTipPosition = rightIndexTipPose.position;
                m_HasPreviousRightTip = true;
                m_RightTapArmed = false;
                return false;
            }

            var deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
            var speed = Vector3.Distance(rightIndexTipPose.position, m_PreviousRightTipPosition) / deltaTime;
            m_PreviousRightTipPosition = rightIndexTipPose.position;

            var distanceToSyringe = DistancePointToSegment(
                rightIndexTipPose.position,
                currentPoints.plunger,
                currentPoints.barrelEnd);

            var isNearSyringe = distanceToSyringe <= m_RightTapDistanceThreshold;
            var cooldownElapsed = Time.time - m_LastRightTapTime >= m_RightTapCooldown;

            if (!m_RightTapArmed)
            {
                if (cooldownElapsed && isNearSyringe && speed >= m_RightTapArmingSpeed)
                {
                    m_RightTapArmed = true;
                    m_RightTapArmedAt = Time.time;
                }

                return false;
            }

            if (!isNearSyringe || Time.time - m_RightTapArmedAt > 0.35f)
            {
                m_RightTapArmed = false;
                return false;
            }

            if (speed <= m_RightTapReleaseSpeed)
            {
                m_RightTapArmed = false;
                m_LastRightTapTime = Time.time;
                return true;
            }

            return false;
        }

        void FinalizeMarkerCalibration(XRHand leftHand, in SyringePoints currentPoints)
        {
            m_IsCalibratingMarker = false;

            if (m_MarkerTapWorldSamples.Count == 0)
            {
                if (m_LogCalibrationEvents)
                    Debug.LogWarning("[Syringe] Calibration finished without marker tap points.", this);
                return;
            }

            var markerWorld = Vector3.zero;
            for (var i = 0; i < m_MarkerTapWorldSamples.Count; ++i)
                markerWorld += m_MarkerTapWorldSamples[i];
            markerWorld /= m_MarkerTapWorldSamples.Count;

            m_MarkerWorldPoint = markerWorld;
            m_IsMarkerCalibrated = true;
            m_MarkerConfidence = 1f;

            if (m_MainCamera != null)
            {
                m_HasMarkerDepth = true;
                m_MarkerDepthFromCamera = Vector3.Distance(m_MainCamera.transform.position, m_MarkerWorldPoint);
            }

            m_HasLocalMarkerOffset = true;
            m_LocalMarkerOffsetInHand = Quaternion.Inverse(leftHand.rootPose.rotation) * (m_MarkerWorldPoint - leftHand.rootPose.position);

            if (m_MarkerColorSamples.Count > 0)
            {
                m_MarkerColorHsv = ComputeAverageHueSaturationValue(m_MarkerColorSamples);
                m_TargetMarkerHsv = m_MarkerColorHsv;
                m_HasMarkerColor = true;
            }
            else
            {
                m_HasMarkerColor = false;
                m_TargetMarkerHsv = Vector3.zero;
            }

            m_LastMatchedMarkerHsv = Vector3.zero;
            m_LastMarkerHitCount = 0;
            m_LastMarkerBestScore = 0f;
            m_LastMarkerVisible = false;

            if (m_LogCalibrationEvents)
            {
                var colorState = m_HasMarkerColor ? "marker color locked" : "marker color unavailable (pose-only tracking fallback)";
                Debug.Log($"[Syringe] Calibration complete. {colorState}", this);
            }

            // Align marker direction with current syringe direction immediately after calibration.
            var currentAxis = (currentPoints.needleTip - currentPoints.plunger).normalized;
            var markerAxis = (m_MarkerWorldPoint - currentPoints.plunger).normalized;
            if (markerAxis.sqrMagnitude > 0.0000001f && currentAxis.sqrMagnitude > 0.0000001f && Vector3.Dot(markerAxis, currentAxis) < 0f)
            {
                m_MarkerWorldPoint = currentPoints.plunger - markerAxis * Vector3.Distance(currentPoints.plunger, m_MarkerWorldPoint);
            }
        }

        void ApplyMarkerAssistedDirection(XRHand leftHand, ref SyringePoints points)
        {
            var predictedMarkerWorld = m_HasLocalMarkerOffset
                ? leftHand.rootPose.position + leftHand.rootPose.rotation * m_LocalMarkerOffsetInHand
                : m_MarkerWorldPoint;

            m_LastPredictedMarkerWorld = predictedMarkerWorld;

            var observedMarkerWorld = predictedMarkerWorld;
            var markerVisible = false;
            m_LastMarkerDebugInfo = default;

            if (m_HasMarkerColor && TryFindMarkerInImage(predictedMarkerWorld, out var markerScreenPosition, out var markerDebugInfo))
            {
                markerVisible = true;
                m_LastMarkerDebugInfo = markerDebugInfo;
                m_LastMarkerDebugInfo.hasMatch = true;
                if (m_MainCamera != null)
                {
                    var ray = m_MainCamera.ScreenPointToRay(new Vector3(markerScreenPosition.x, markerScreenPosition.y, 0f));
                    var depth = m_HasMarkerDepth
                        ? m_MarkerDepthFromCamera
                        : Vector3.Distance(m_MainCamera.transform.position, predictedMarkerWorld);

                    observedMarkerWorld = ray.GetPoint(depth);

                    if (m_HasMarkerDepth)
                    {
                        var newDepth = Vector3.Distance(m_MainCamera.transform.position, observedMarkerWorld);
                        m_MarkerDepthFromCamera = Mathf.Lerp(m_MarkerDepthFromCamera, newDepth, 0.35f);
                    }
                    else
                    {
                        m_HasMarkerDepth = true;
                        m_MarkerDepthFromCamera = depth;
                    }
                }
            }
            else if (m_MainCamera != null)
            {
                var projected = m_MainCamera.WorldToScreenPoint(predictedMarkerWorld);
                var radius = m_MarkerSearchRadiusPixels;
                m_LastMarkerDebugInfo.centerScreen = new Vector2(projected.x, projected.y);
                m_LastMarkerDebugInfo.searchMinScreen = new Vector2(projected.x - radius, projected.y - radius);
                m_LastMarkerDebugInfo.searchMaxScreen = new Vector2(projected.x + radius, projected.y + radius);
            }

            var blendRate = 1f - Mathf.Pow(1f - m_MarkerPredictionSmoothing, Time.deltaTime * 90f);
            m_MarkerWorldPoint = Vector3.Lerp(m_MarkerWorldPoint, observedMarkerWorld, blendRate);

            if (m_HasLocalMarkerOffset)
            {
                var localMarkerNow = Quaternion.Inverse(leftHand.rootPose.rotation) * (m_MarkerWorldPoint - leftHand.rootPose.position);
                var localBlend = markerVisible ? 0.2f : 0.08f;
                m_LocalMarkerOffsetInHand = Vector3.Slerp(m_LocalMarkerOffsetInHand, localMarkerNow, localBlend).normalized * localMarkerNow.magnitude;
            }
            else
            {
                m_HasLocalMarkerOffset = true;
                m_LocalMarkerOffsetInHand = Quaternion.Inverse(leftHand.rootPose.rotation) * (m_MarkerWorldPoint - leftHand.rootPose.position);
            }

            var confidenceDelta = markerVisible
                ? m_MarkerVisibilityRecoverPerSecond * Time.deltaTime
                : -m_MarkerVisibilityDecayPerSecond * Time.deltaTime;
            m_MarkerConfidence = Mathf.Clamp01(m_MarkerConfidence + confidenceDelta);
            m_LastMarkerVisible = markerVisible;
            m_LastMarkerHitCount = m_LastMarkerDebugInfo.hitCount;
            m_LastMarkerBestScore = m_LastMarkerDebugInfo.bestScore;
            m_LastMatchedMarkerHsv = m_LastMarkerDebugInfo.matchedHsv;

            var jointAxis = (points.wingsCenter - points.plunger).normalized;
            var markerAxis = (m_MarkerWorldPoint - points.plunger);
            if (markerAxis.sqrMagnitude < 0.0000001f || jointAxis.sqrMagnitude < 0.0000001f)
                return;

            markerAxis.Normalize();
            if (Vector3.Dot(markerAxis, jointAxis) < 0f)
                markerAxis = -markerAxis;

            Vector3 fusedAxis;
            if (m_ForceMarkerOnlyAfterCalibration)
            {
                // Marker-only debug mode: directly trust marker direction after calibration.
                fusedAxis = markerAxis;
            }
            else
            {
                var markerWeight = m_MarkerDirectionBlend * Mathf.Clamp01(m_MarkerConfidence);
                fusedAxis = Vector3.Slerp(jointAxis, markerAxis, markerWeight).normalized;
            }

            RebuildSyringePointsWithAxis(ref points, leftHand, fusedAxis);
        }

        void RebuildSyringePointsWithAxis(ref SyringePoints points, XRHand leftHand, Vector3 axis)
        {
            var plunger = points.plunger;
            var plungerToWingsDistance = Mathf.Clamp(Vector3.Distance(plunger, points.wingsCenter), m_MinPlungerToWings, m_MaxPlungerToWings);
            var wingsCenter = plunger + axis * plungerToWingsDistance;

            var wingVector = points.leftWing - points.rightWing;
            wingVector = Vector3.ProjectOnPlane(wingVector, axis);
            if (wingVector.sqrMagnitude < 0.0000001f)
                wingVector = Vector3.ProjectOnPlane(leftHand.rootPose.right, axis);

            var sideAxis = wingVector.sqrMagnitude > 0.0000001f ? wingVector.normalized : Vector3.right;
            var halfWingSpan = Mathf.Max(0.005f, wingVector.magnitude * 0.5f);

            points.leftWing = wingsCenter + sideAxis * halfWingSpan;
            points.rightWing = wingsCenter - sideAxis * halfWingSpan;
            points.wingsCenter = wingsCenter;
            points.barrelEnd = wingsCenter + axis * m_WingsToBarrelEnd;
            points.needleTip = points.barrelEnd + axis * m_BarrelEndToNeedleTip;
            var metalNeedleLength = Mathf.Min(m_MetalNeedleLength, m_BarrelEndToNeedleTip);
            points.needleBase = points.needleTip - axis * metalNeedleLength;
        }

        bool TrySampleMarkerColorAtWorldPoint(Vector3 worldPoint, out Color sampledColor)
        {
            sampledColor = default;
            if (m_MainCamera == null)
                return false;

            var screenPoint = m_MainCamera.WorldToScreenPoint(worldPoint);
            if (screenPoint.z <= 0f)
                return false;

            return TrySampleMarkerColorAtScreenPoint(new Vector2(screenPoint.x, screenPoint.y), out sampledColor);
        }

        bool TrySampleMarkerColorAtScreenPoint(Vector2 screenPoint, out Color sampledColor)
        {
            sampledColor = default;
            if (!TryUpdateCpuImageTexture(true))
                return false;

            if (!TryScreenPointToImagePixel(screenPoint, out var imageX, out var imageY, out _))
                return false;

            var pixels = m_CpuImageTexture.GetRawTextureData<Color32>();
            return TryComputeAverageColorAroundPixel(pixels, m_CpuImageWidth, m_CpuImageHeight, imageX, imageY, 2, out sampledColor);
        }

        bool TryFindMarkerInImage(Vector3 predictedMarkerWorld, out Vector2 markerScreen, out MarkerDetectionDebugInfo debugInfo)
        {
            markerScreen = default;
            debugInfo = default;
            if (!m_HasMarkerColor || m_MainCamera == null)
                return false;

            var projected = m_MainCamera.WorldToScreenPoint(predictedMarkerWorld);
            if (projected.z <= 0f)
                return false;

            debugInfo.centerScreen = new Vector2(projected.x, projected.y);
            debugInfo.searchMinScreen = new Vector2(projected.x - m_MarkerSearchRadiusPixels, projected.y - m_MarkerSearchRadiusPixels);
            debugInfo.searchMaxScreen = new Vector2(projected.x + m_MarkerSearchRadiusPixels, projected.y + m_MarkerSearchRadiusPixels);

            if (!TryUpdateCpuImageTexture(false))
                return false;

            if (!TryScreenPointToImagePixel(new Vector2(projected.x, projected.y), out var centerX, out var centerY, out _))
                return false;

            var pixels = m_CpuImageTexture.GetRawTextureData<Color32>();
            if (!TryFindBestColorMatchPixel(
                    pixels,
                    m_CpuImageWidth,
                    m_CpuImageHeight,
                    centerX,
                    centerY,
                    out var matchX,
                    out var matchY,
                    out var hitCount,
                    out var bestScore,
                    out var bestColor))
                return false;

            markerScreen = ImagePixelToScreenPoint(matchX, matchY);

            var displacement = Vector2.Distance(markerScreen, debugInfo.centerScreen);
            if (m_MarkerConfidence > 0.3f && displacement > m_MaxMarkerJumpPixels)
                return false;

            debugInfo.matchedScreen = markerScreen;
            debugInfo.hitCount = hitCount;
            debugInfo.bestScore = bestScore;
            var bestColorFloat = (Color)bestColor;
            Color.RGBToHSV(bestColorFloat, out var h, out var s, out var v);
            debugInfo.matchedHsv = new Vector3(h, s, v);
            debugInfo.hasMatch = true;
            return true;
        }

        bool TryFindBestColorMatchPixel(
            NativeArray<Color32> pixels,
            int width,
            int height,
            int centerX,
            int centerY,
            out int bestX,
            out int bestY,
            out int hitCount,
            out float bestScore,
            out Color32 bestColor)
        {
            bestX = 0;
            bestY = 0;
            hitCount = 0;
            bestScore = float.MaxValue;
            bestColor = default;

            if (!pixels.IsCreated || pixels.Length == 0)
                return false;

            var radius = Mathf.RoundToInt(m_MarkerSearchRadiusPixels * (width / Mathf.Max(1f, (float)Screen.width)));
            radius = Mathf.Clamp(radius, 10, 120);

            var minX = Mathf.Max(0, centerX - radius);
            var maxX = Mathf.Min(width - 1, centerX + radius);
            var minY = Mathf.Max(0, centerY - radius);
            var maxY = Mathf.Min(height - 1, centerY + radius);

            var found = false;
            var step = Mathf.Max(1, m_MarkerSearchStep);

            for (var y = minY; y <= maxY; y += step)
            {
                var rowOffset = y * width;
                for (var x = minX; x <= maxX; x += step)
                {
                    var color = pixels[rowOffset + x];
                    var colorDistance = ComputeMarkerColorDistance(color);
                    if (colorDistance > m_MarkerColorTolerance)
                        continue;

                    hitCount++;

                    var spatial = ((x - centerX) * (x - centerX) + (y - centerY) * (y - centerY)) * 0.00001f;
                    var score = colorDistance + spatial;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestX = x;
                        bestY = y;
                        bestColor = color;
                        found = true;
                    }
                }
            }

            if (!found)
                return false;

            if (hitCount < m_MinMarkerHitsForDetection)
                return false;

            return true;
        }

        float ComputeMarkerColorDistance(Color32 color)
        {
            var colorFloat = (Color)color;
            Color.RGBToHSV(colorFloat, out var hue, out var saturation, out var value);

            var hueDelta = Mathf.Abs(hue - m_MarkerColorHsv.x);
            hueDelta = Mathf.Min(hueDelta, 1f - hueDelta);

            var satDelta = Mathf.Abs(saturation - m_MarkerColorHsv.y);
            var valDelta = Mathf.Abs(value - m_MarkerColorHsv.z);

            return hueDelta * 0.65f + satDelta * 0.25f + valDelta * 0.10f;
        }

        static bool TryComputeAverageColorAroundPixel(
            NativeArray<Color32> pixels,
            int width,
            int height,
            int x,
            int y,
            int radius,
            out Color avgColor)
        {
            avgColor = default;
            if (!pixels.IsCreated || pixels.Length == 0)
                return false;

            var minX = Mathf.Max(0, x - radius);
            var maxX = Mathf.Min(width - 1, x + radius);
            var minY = Mathf.Max(0, y - radius);
            var maxY = Mathf.Min(height - 1, y + radius);

            var sum = Vector3.zero;
            var count = 0;

            for (var yy = minY; yy <= maxY; ++yy)
            {
                var row = yy * width;
                for (var xx = minX; xx <= maxX; ++xx)
                {
                    var c = (Color)pixels[row + xx];
                    sum += new Vector3(c.r, c.g, c.b);
                    count++;
                }
            }

            if (count <= 0)
                return false;

            sum /= count;
            avgColor = new Color(sum.x, sum.y, sum.z, 1f);
            return true;
        }

        Vector3 ComputeAverageHueSaturationValue(List<Color> colors)
        {
            var hueX = 0f;
            var hueY = 0f;
            var sat = 0f;
            var val = 0f;

            for (var i = 0; i < colors.Count; ++i)
            {
                Color.RGBToHSV(colors[i], out var h, out var s, out var v);
                var angle = h * Mathf.PI * 2f;
                hueX += Mathf.Cos(angle);
                hueY += Mathf.Sin(angle);
                sat += s;
                val += v;
            }

            var avgHue = Mathf.Atan2(hueY, hueX) / (Mathf.PI * 2f);
            if (avgHue < 0f)
                avgHue += 1f;

            var count = Mathf.Max(colors.Count, 1);
            return new Vector3(avgHue, sat / count, val / count);
        }

        bool TryUpdateCpuImageTexture(bool force)
        {
            if (m_ARCameraManager == null)
                return false;

            if (!force && Time.unscaledTime - m_LastCpuImageUpdateTime < m_CpuImageUpdateInterval)
                return m_CpuImageTexture != null;

            if (!m_ARCameraManager.TryAcquireLatestCpuImage(out var cpuImage))
                return false;

            using (cpuImage)
            {
                var conversionParams = new XRCpuImage.ConversionParams
                {
                    inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
                    outputDimensions = new Vector2Int(cpuImage.width, cpuImage.height),
                    outputFormat = TextureFormat.RGBA32,
                    transformation = m_CpuImageTransformation,
                };

                if (m_CpuImageTexture == null ||
                    m_CpuImageWidth != cpuImage.width ||
                    m_CpuImageHeight != cpuImage.height)
                {
                    if (m_CpuImageTexture != null)
                        Destroy(m_CpuImageTexture);

                    m_CpuImageTexture = new Texture2D(cpuImage.width, cpuImage.height, TextureFormat.RGBA32, false)
                    {
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                    };

                    m_CpuImageWidth = cpuImage.width;
                    m_CpuImageHeight = cpuImage.height;
                }

                var rawTextureData = m_CpuImageTexture.GetRawTextureData<byte>();
                cpuImage.Convert(conversionParams, rawTextureData);
                m_CpuImageTexture.Apply(false, false);
            }

            m_LastCpuImageUpdateTime = Time.unscaledTime;
            return true;
        }

        bool TryScreenPointToImagePixel(Vector2 screenPoint, out int imageX, out int imageY, out Vector2 uv)
        {
            imageX = 0;
            imageY = 0;
            uv = Vector2.zero;

            if (m_CpuImageWidth <= 0 || m_CpuImageHeight <= 0)
                return false;

            var viewportUv = new Vector2(
                Mathf.Clamp01(screenPoint.x / Mathf.Max(1f, Screen.width)),
                Mathf.Clamp01(screenPoint.y / Mathf.Max(1f, Screen.height)));

            var textureUv = viewportUv;
            if (m_UseDisplayMatrixForCpuMapping && m_HasDisplayMatrix)
                textureUv = ApplyUvMatrix(m_LastDisplayMatrix, viewportUv);

            textureUv.x = Mathf.Clamp01(textureUv.x);
            textureUv.y = Mathf.Clamp01(textureUv.y);
            uv = textureUv;

            imageX = Mathf.Clamp(Mathf.RoundToInt(textureUv.x * (m_CpuImageWidth - 1)), 0, m_CpuImageWidth - 1);
            imageY = Mathf.Clamp(Mathf.RoundToInt(textureUv.y * (m_CpuImageHeight - 1)), 0, m_CpuImageHeight - 1);
            return true;
        }

        Vector2 ImagePixelToScreenPoint(int imageX, int imageY)
        {
            if (m_CpuImageWidth <= 1 || m_CpuImageHeight <= 1)
                return Vector2.zero;

            var textureUv = new Vector2(
                imageX / (float)(m_CpuImageWidth - 1),
                imageY / (float)(m_CpuImageHeight - 1));

            var viewportUv = textureUv;
            if (m_UseDisplayMatrixForCpuMapping && m_HasInverseDisplayMatrix)
                viewportUv = ApplyUvMatrix(m_InverseDisplayMatrix, textureUv);

            viewportUv.x = Mathf.Clamp01(viewportUv.x);
            viewportUv.y = Mathf.Clamp01(viewportUv.y);
            return new Vector2(viewportUv.x * Screen.width, viewportUv.y * Screen.height);
        }

        static Vector2 ApplyUvMatrix(Matrix4x4 matrix, Vector2 uv)
        {
            var transformed = matrix * new Vector4(uv.x, uv.y, 0f, 1f);
            if (Mathf.Abs(transformed.w) > 0.000001f)
                return new Vector2(transformed.x / transformed.w, transformed.y / transformed.w);

            return new Vector2(transformed.x, transformed.y);
        }

        static bool TryGetInverseDisplayMatrix(Matrix4x4 matrix, out Matrix4x4 inverse)
        {
            var determinant = matrix.determinant;
            if (Mathf.Abs(determinant) < 0.0000001f)
            {
                inverse = Matrix4x4.identity;
                return false;
            }

            inverse = matrix.inverse;
            return true;
        }

        static float DistancePointToSegment(Vector3 point, Vector3 segmentStart, Vector3 segmentEnd)
        {
            var segment = segmentEnd - segmentStart;
            var segmentLengthSq = segment.sqrMagnitude;
            if (segmentLengthSq < 0.0000001f)
                return Vector3.Distance(point, segmentStart);

            var t = Vector3.Dot(point - segmentStart, segment) / segmentLengthSq;
            t = Mathf.Clamp01(t);
            var projection = segmentStart + segment * t;
            return Vector3.Distance(point, projection);
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
            m_PlungerMarker = CreateMarker("Plunger Marker", lineMaterial, m_JointMarkerSize);
            m_LeftWingMarker = CreateMarker("Left Wing Marker", lineMaterial, m_JointMarkerSize);
            m_RightWingMarker = CreateMarker("Right Wing Marker", lineMaterial, m_JointMarkerSize);
            m_WingsCenterMarker = CreateMarker("Wings Center Marker", lineMaterial, m_JointMarkerSize);
            m_BarrelEndMarker = CreateMarker("Barrel End Marker", lineMaterial, m_JointMarkerSize);
            m_NeedleBaseMarker = CreateMarker("Needle Base Marker", lineMaterial, m_JointMarkerSize);
            m_NeedleTipMarker = CreateMarker("Needle Tip Marker", lineMaterial, m_JointMarkerSize);

            var markerDotMaterial = GetMarkerDotMaterial();
            m_MarkerDot = CreateMarker("Marker Dot", markerDotMaterial, m_MarkerDotSize);
            m_MarkerDot.gameObject.SetActive(false);

            m_DetectedMarker = CreateMarker("Detected Marker", markerDotMaterial, m_MarkerDotSize * 0.75f);
            if (m_DetectedMarker.TryGetComponent<Renderer>(out var detectedRenderer))
                SetRendererColor(detectedRenderer, m_DetectedCandidateColor);
            m_DetectedMarker.gameObject.SetActive(false);

            m_SearchRegionLine = CreateLine("Marker Search Region", lineMaterial);
            m_SearchRegionLine.positionCount = 5;
            m_SearchRegionLine.startWidth = Mathf.Max(0.0006f, m_LineWidth * 0.35f);
            m_SearchRegionLine.endWidth = Mathf.Max(0.0006f, m_LineWidth * 0.35f);
            m_SearchRegionLine.startColor = m_SearchRegionColor;
            m_SearchRegionLine.endColor = m_SearchRegionColor;
            m_SearchRegionLine.enabled = false;

            m_MarkerTrailLine = CreateLine("Marker Trail", lineMaterial);
            m_MarkerTrailLine.positionCount = 0;
            m_MarkerTrailLine.startWidth = Mathf.Max(0.0005f, m_LineWidth * 0.35f);
            m_MarkerTrailLine.endWidth = Mathf.Max(0.0004f, m_LineWidth * 0.15f);
            m_MarkerTrailLine.startColor = m_MarkerTrailColor;
            var trailEndColor = m_MarkerTrailColor;
            trailEndColor.a *= 0.2f;
            m_MarkerTrailLine.endColor = trailEndColor;
            m_MarkerTrailLine.enabled = false;

            var debugTextObject = new GameObject("Marker Debug Text");
            debugTextObject.transform.SetParent(m_OverlayRoot, false);
            m_MarkerDebugText = debugTextObject.AddComponent<TextMesh>();
            m_MarkerDebugText.fontSize = 36;
            m_MarkerDebugText.characterSize = 0.0018f;
            m_MarkerDebugText.anchor = TextAnchor.LowerLeft;
            m_MarkerDebugText.alignment = TextAlignment.Left;
            m_MarkerDebugText.color = Color.white;
            m_MarkerDebugText.text = string.Empty;
            m_MarkerDebugText.gameObject.SetActive(false);

            SetOverlayVisible(false);
        }

        void SetRendererColor(Renderer renderer, Color color)
        {
            if (renderer == null)
                return;

            m_MaterialPropertyBlock ??= new MaterialPropertyBlock();

            renderer.GetPropertyBlock(m_MaterialPropertyBlock);
            m_MaterialPropertyBlock.SetColor("_BaseColor", color);
            m_MaterialPropertyBlock.SetColor("_Color", color);
            renderer.SetPropertyBlock(m_MaterialPropertyBlock);
        }

        Material GetLineMaterial()
        {
            if (m_LineMaterialOverride != null)
                return m_LineMaterialOverride;

            if (m_RuntimeLineMaterial != null)
                return m_RuntimeLineMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Sprites/Default") ??
                         Shader.Find("Unlit/Color") ??
                         Shader.Find("Standard");

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

        Material GetMarkerDotMaterial()
        {
            if (m_RuntimeMarkerDotMaterial != null)
                return m_RuntimeMarkerDotMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Sprites/Default") ??
                         Shader.Find("Unlit/Color") ??
                         Shader.Find("Standard");

            if (shader == null)
                return GetLineMaterial();

            m_RuntimeMarkerDotMaterial = new Material(shader);
            if (m_RuntimeMarkerDotMaterial.HasProperty("_BaseColor"))
                m_RuntimeMarkerDotMaterial.SetColor("_BaseColor", m_MarkerDotColor);
            if (m_RuntimeMarkerDotMaterial.HasProperty("_Color"))
                m_RuntimeMarkerDotMaterial.SetColor("_Color", m_MarkerDotColor);
            return m_RuntimeMarkerDotMaterial;
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

        Transform CreateMarker(string markerName, Material markerMaterial, float markerSize)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = markerName;
            marker.transform.SetParent(m_MarkersRoot, false);
            marker.transform.localScale = Vector3.one * markerSize;

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

        void UpdateMarkerDotVisual()
        {
            if (m_MarkerDot == null)
                return;

            var visible = m_IsMarkerCalibrated;
            m_MarkerDot.gameObject.SetActive(visible);
            if (!visible)
                return;

            m_MarkerDot.position = m_MarkerWorldPoint;
            m_MarkerDot.localScale = Vector3.one * m_MarkerDotSize;
        }

        void UpdateMarkerDebugVisuals()
        {
            if (!m_EnableMarkerDebug || !m_IsMarkerCalibrated || !m_OverlayVisible || m_MainCamera == null)
            {
                ClearMarkerDebugVisuals();
                return;
            }

            var cameraTransform = m_MainCamera.transform;
            var markerDepth = m_HasMarkerDepth
                ? m_MarkerDepthFromCamera
                : Vector3.Distance(cameraTransform.position, m_MarkerWorldPoint);
            markerDepth = Mathf.Max(markerDepth, m_MainCamera.nearClipPlane + 0.05f);

            var centerScreen = m_LastMarkerDebugInfo.centerScreen;
            var minScreen = m_LastMarkerDebugInfo.searchMinScreen;
            var maxScreen = m_LastMarkerDebugInfo.searchMaxScreen;

            if (m_ShowSearchRegion && m_SearchRegionLine != null)
            {
                var bottomLeft = ScreenToWorldAtDepth(new Vector2(minScreen.x, minScreen.y), markerDepth);
                var bottomRight = ScreenToWorldAtDepth(new Vector2(maxScreen.x, minScreen.y), markerDepth);
                var topRight = ScreenToWorldAtDepth(new Vector2(maxScreen.x, maxScreen.y), markerDepth);
                var topLeft = ScreenToWorldAtDepth(new Vector2(minScreen.x, maxScreen.y), markerDepth);

                m_SearchRegionLine.positionCount = 5;
                m_SearchRegionLine.SetPosition(0, bottomLeft);
                m_SearchRegionLine.SetPosition(1, bottomRight);
                m_SearchRegionLine.SetPosition(2, topRight);
                m_SearchRegionLine.SetPosition(3, topLeft);
                m_SearchRegionLine.SetPosition(4, bottomLeft);
                m_SearchRegionLine.startColor = m_SearchRegionColor;
                m_SearchRegionLine.endColor = m_SearchRegionColor;
                m_SearchRegionLine.enabled = true;
            }
            else if (m_SearchRegionLine != null)
            {
                m_SearchRegionLine.enabled = false;
            }

            if (m_DetectedMarker != null)
            {
                if (m_LastMarkerDebugInfo.hasMatch)
                {
                    var matchRay = m_MainCamera.ScreenPointToRay(new Vector3(
                        m_LastMarkerDebugInfo.matchedScreen.x,
                        m_LastMarkerDebugInfo.matchedScreen.y,
                        0f));
                    m_DetectedMarker.position = matchRay.origin + matchRay.direction * markerDepth;
                    m_DetectedMarker.localScale = Vector3.one * (m_MarkerDotSize * 0.75f);
                    m_DetectedMarker.gameObject.SetActive(true);
                }
                else
                {
                    m_DetectedMarker.gameObject.SetActive(false);
                }
            }

            if (m_ShowMarkerTrail && m_MarkerTrailLine != null)
            {
                var addTrailPoint = m_LastMarkerVisible || m_MarkerConfidence > 0.05f;
                if (addTrailPoint)
                {
                    var shouldEnqueue = true;
                    if (m_MarkerTrailPoints.Count > 0)
                    {
                        Vector3 latestPoint = default;
                        foreach (var trailPoint in m_MarkerTrailPoints)
                            latestPoint = trailPoint;

                        if (Vector3.Distance(latestPoint, m_MarkerWorldPoint) < m_MarkerTrailMinPointDistance)
                            shouldEnqueue = false;
                    }

                    if (shouldEnqueue)
                    {
                        m_MarkerTrailPoints.Enqueue(m_MarkerWorldPoint);
                        while (m_MarkerTrailPoints.Count > m_MarkerTrailMaxPoints)
                            m_MarkerTrailPoints.Dequeue();
                    }
                }

                if (m_MarkerTrailPoints.Count > 1)
                {
                    m_MarkerTrailLine.positionCount = m_MarkerTrailPoints.Count;
                    var index = 0;
                    foreach (var trailPoint in m_MarkerTrailPoints)
                        m_MarkerTrailLine.SetPosition(index++, trailPoint);

                    m_MarkerTrailLine.startColor = m_MarkerTrailColor;
                    var trailEnd = m_MarkerTrailColor;
                    trailEnd.a *= 0.2f;
                    m_MarkerTrailLine.endColor = trailEnd;
                    m_MarkerTrailLine.enabled = true;
                }
                else
                {
                    m_MarkerTrailLine.enabled = false;
                    m_MarkerTrailLine.positionCount = 0;
                }
            }
            else if (m_MarkerTrailLine != null)
            {
                m_MarkerTrailLine.enabled = false;
                m_MarkerTrailLine.positionCount = 0;
            }

            if (m_ShowMarkerDebugText && m_MarkerDebugText != null)
            {
                m_MarkerDebugText.gameObject.SetActive(true);
                m_MarkerDebugText.color = m_LastMarkerVisible ? m_DetectedCandidateColor : m_SearchRegionColor;
                m_MarkerDebugText.transform.position = m_MarkerWorldPoint + cameraTransform.up * 0.022f + cameraTransform.right * 0.016f;

                var lookDirection = m_MarkerDebugText.transform.position - cameraTransform.position;
                if (lookDirection.sqrMagnitude > 0.0001f)
                    m_MarkerDebugText.transform.rotation = Quaternion.LookRotation(lookDirection.normalized, cameraTransform.up);

                m_DebugTextBuilder.Clear();
                m_DebugTextBuilder.Append(m_ForceMarkerOnlyAfterCalibration ? "MODE marker-only" : "MODE fused");
                m_DebugTextBuilder.Append("\nvis:");
                m_DebugTextBuilder.Append(m_LastMarkerVisible ? "yes" : "no");
                m_DebugTextBuilder.Append(" conf:");
                m_DebugTextBuilder.Append(m_MarkerConfidence.ToString("0.00"));
                m_DebugTextBuilder.Append(" hits:");
                m_DebugTextBuilder.Append(m_LastMarkerHitCount);
                m_DebugTextBuilder.Append(" score:");
                m_DebugTextBuilder.Append(m_LastMarkerBestScore.ToString("0.000"));
                m_DebugTextBuilder.Append("\ntarget hsv:");
                AppendVector3(m_DebugTextBuilder, m_TargetMarkerHsv);
                m_DebugTextBuilder.Append("\nmatch hsv:");
                AppendVector3(m_DebugTextBuilder, m_LastMatchedMarkerHsv);
                m_DebugTextBuilder.Append("\ncenter:");
                AppendVector2(m_DebugTextBuilder, centerScreen);
                m_DebugTextBuilder.Append(" match:");
                AppendVector2(m_DebugTextBuilder, m_LastMarkerDebugInfo.matchedScreen);
                m_MarkerDebugText.text = m_DebugTextBuilder.ToString();
            }
            else if (m_MarkerDebugText != null)
            {
                m_MarkerDebugText.text = string.Empty;
                m_MarkerDebugText.gameObject.SetActive(false);
            }
        }

        void ClearMarkerDebugVisuals()
        {
            if (m_SearchRegionLine != null)
                m_SearchRegionLine.enabled = false;

            if (m_DetectedMarker != null)
                m_DetectedMarker.gameObject.SetActive(false);

            if (m_MarkerTrailLine != null)
            {
                m_MarkerTrailLine.enabled = false;
                m_MarkerTrailLine.positionCount = 0;
            }

            if (m_MarkerDebugText != null)
            {
                m_MarkerDebugText.text = string.Empty;
                m_MarkerDebugText.gameObject.SetActive(false);
            }

            m_MarkerTrailPoints.Clear();
        }

        Vector3 ScreenToWorldAtDepth(Vector2 screenPoint, float depth)
        {
            var clampedScreen = new Vector3(
                Mathf.Clamp(screenPoint.x, 0f, Screen.width),
                Mathf.Clamp(screenPoint.y, 0f, Screen.height),
                depth);
            return m_MainCamera.ScreenToWorldPoint(clampedScreen);
        }

        static void AppendVector2(StringBuilder builder, Vector2 value)
        {
            builder.Append(value.x.ToString("0.0"));
            builder.Append(',');
            builder.Append(value.y.ToString("0.0"));
        }

        static void AppendVector3(StringBuilder builder, Vector3 value)
        {
            builder.Append(value.x.ToString("0.00"));
            builder.Append(',');
            builder.Append(value.y.ToString("0.00"));
            builder.Append(',');
            builder.Append(value.z.ToString("0.00"));
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