using UnityEngine.Rendering;

namespace UnityEngine.XR.Templates.MR
{
    /// <summary>
    /// Renders a live syringe-vs-surface angle overlay when the needle tip is near a placed surface.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class SyringePlaneAngleOverlay : MonoBehaviour
    {
        struct OverlayData
        {
            public Vector3 tip;
            public Vector3 projectedTip;
            public Vector3 insertionPoint;
            public Vector3 planeNormal;
            public Vector3 planeDirection;
            public Vector3 tipDirection;
            public float angleDegrees;
            public float distanceMeters;
            public float arcRadius;
        }

        [Header("References")]
        [SerializeField]
        SyringeOverlayTracker m_Tracker;

        [SerializeField]
        SurfaceSelectionTool m_SurfaceSelectionTool;

        [SerializeField]
        SyringeCalibrationButtonBridge m_Tutorial;

        [SerializeField]
        Camera m_MainCamera;

        [Header("Visibility")]
        [SerializeField, Min(0.005f)]
        float m_MaxTipPlaneDistance = 0.035f;

        [SerializeField]
        bool m_RequireProjectionInsideSurface = false;

        [SerializeField, Min(0.005f)]
        float m_AngleModeMaxTipPlaneDistance = 0.04f;

        [SerializeField, Min(0f)]
        float m_SurfaceBoundsMargin = 0.008f;

        [Header("Arc style")]
        [SerializeField, Range(8, 64)]
        int m_ArcSegments = 28;

        [SerializeField]
        bool m_ShowArc = true;

        [SerializeField, Min(0.0002f)]
        float m_LineWidth = 0.0018f;

        [SerializeField]
        Color m_ArcColor = new Color(1f, 0.85f, 0.2f, 0.95f);

        [SerializeField]
        Color m_DropLineColor = new Color(0.2f, 1f, 0.9f, 0.95f);

        [SerializeField]
        Color m_PlaneReferenceColor = new Color(0.55f, 0.95f, 1f, 0.95f);

        [SerializeField]
        Color m_ThumbToCenterLineColor = new Color(0.35f, 1f, 0.55f, 0.92f);

        [SerializeField, Min(0.0002f)]
        float m_ThumbToCenterLineWidth = 0.0022f;

        [Header("Angle text")]
        [SerializeField]
        bool m_ShowAngleText = true;

        [Header("Guidance overlay")]
        [SerializeField]
        bool m_ShowGuidanceArrows = true;

        [SerializeField]
        Color m_GuidanceGeometryColor = InjectionGuidancePalette.GeometryAmber;

        [SerializeField]
        Color m_GuidanceOptimalColor = InjectionGuidancePalette.GeometryOptimal;

        [SerializeField]
        Color m_GuidanceTextOptimalColor = InjectionGuidancePalette.TextOptimal;

        [SerializeField]
        Color m_GuidanceTextWarningColor = InjectionGuidancePalette.TextWarning;

        [SerializeField, Min(0.001f)]
        float m_GuidanceArrowLength = 0.09f;

        [SerializeField, Min(0.0005f)]
        float m_GuidanceArrowHeadLength = 0.015f;

        [SerializeField, Min(0.0002f)]
        float m_GuidanceArrowWidth = 0.006f;

        [SerializeField, Min(0.0005f)]
        float m_GuidanceDotSize = 0.007f;

        [SerializeField, Min(0.0005f)]
        float m_GuidanceTextCharacterSize = 0.0038f;

        [SerializeField]
        Color m_TextColorInBand = InjectionGuidancePalette.TextOptimal;

        [SerializeField]
        Color m_TextColorOutOfBand = InjectionGuidancePalette.TextWarning;

        [SerializeField, Min(0.0005f)]
        float m_TextCharacterSize = 0.0021f;

        [SerializeField, Min(0f)]
        float m_TextOffsetFromArc = 0.007f;

        [SerializeField, Min(0f)]
        float m_TextLiftAbovePlane = 0.04f;

        [SerializeField, Min(0f)]
        float m_GuidanceTextLiftAboveBasePoint = 0.09f;

        [Header("Runtime state")]
        [SerializeField]
        bool m_IsVisible;

        [SerializeField]
        float m_CurrentAngleDegrees;

        [SerializeField]
        float m_CurrentDistanceCentimeters;

        public bool isVisible => m_IsVisible;
        public float currentAngleDegrees => m_CurrentAngleDegrees;
        public float currentDistanceCentimeters => m_CurrentDistanceCentimeters;

        Transform m_Root;
        LineRenderer m_DropLine;
        LineRenderer m_PlaneReferenceLine;
        LineRenderer m_ThumbToCenterLine;
        LineRenderer m_ArcLine;
        TextMesh m_AngleText;
        LineRenderer m_GuidanceArrow;
        Transform m_GuidanceDot;
        TextMesh m_GuidanceText;
        Material m_RuntimeLineMaterial;
        Material m_RuntimeGuidanceMaterial;
        SyringeCalibrationButtonBridge.TutorialStep m_GuidanceStep;

        void Awake()
        {
            // Enforce guidance mode for the injection coaching flow.
            m_ShowGuidanceArrows = true;
            m_ShowArc = false;
            m_ShowAngleText = true;

            CreateVisuals();
            ResolveReferences();
        }

        void Update()
        {
            ResolveReferences();

            if (!TryBuildOverlayData(out var overlayData))
            {
                SetOverlayVisible(false);
                return;
            }

            RenderOverlay(overlayData);
        }

        void OnDestroy()
        {
            if (m_RuntimeLineMaterial != null)
                Destroy(m_RuntimeLineMaterial);
            if (m_RuntimeGuidanceMaterial != null)
                Destroy(m_RuntimeGuidanceMaterial);
        }

        void ResolveReferences()
        {
            if (m_Tracker == null)
                m_Tracker = GetComponent<SyringeOverlayTracker>() ?? FindAnyObjectByType<SyringeOverlayTracker>();

            if (m_SurfaceSelectionTool == null)
                m_SurfaceSelectionTool = GetComponent<SurfaceSelectionTool>() ?? FindAnyObjectByType<SurfaceSelectionTool>();

            if (m_Tutorial == null)
                m_Tutorial = GetComponent<SyringeCalibrationButtonBridge>() ?? FindAnyObjectByType<SyringeCalibrationButtonBridge>();

            if (m_MainCamera == null)
                m_MainCamera = Camera.main;
        }

        bool TryBuildOverlayData(out OverlayData overlayData)
        {
            overlayData = default;

            if (m_Tracker == null || m_SurfaceSelectionTool == null)
                return false;

            if (!m_Tracker.TryGetSyringePose(out var syringePose))
                return false;

            if (!m_SurfaceSelectionTool.TryGetPlacedSurface(out var surfacePose, out var surfaceSizeMeters))
                return false;

            var planeNormal = surfacePose.up;
            if (planeNormal.sqrMagnitude < 0.000001f)
                planeNormal = Vector3.up;
            else
                planeNormal.Normalize();

            var step = m_Tutorial != null ? m_Tutorial.currentStep : SyringeCalibrationButtonBridge.TutorialStep.Start;
            var pipelineStep = step == SyringeCalibrationButtonBridge.TutorialStep.Insertion ||
                               step == SyringeCalibrationButtonBridge.TutorialStep.FlowRate ||
                               step == SyringeCalibrationButtonBridge.TutorialStep.InsertionSpeedFlowRate;
            var siteStep = step == SyringeCalibrationButtonBridge.TutorialStep.InjectionAngle || pipelineStep;

            if (!siteStep)
                return false;

            // Insertion pipeline: angle + coaching arrows only at the placed-surface center once the
            // hand is approaching or locked to the site — never at arbitrary plane intersections.
            var nearSite = m_Tracker.isThumbToCenterMode || m_Tracker.isNearInjectionSite;
            if (pipelineStep && !nearSite)
                return false;

            if (nearSite)
            {
                var centerPoint = m_Tracker.injectionSiteCenter;
                if (centerPoint.sqrMagnitude < 0.000001f)
                    centerPoint = surfacePose.position;

                var thumbTip = m_Tracker.rawThumbTip;
                var axis = m_Tracker.isThumbToCenterMode
                    ? m_Tracker.thumbToCenterAxis
                    : centerPoint - thumbTip;
                if (axis.sqrMagnitude < 0.000001f)
                    return false;
                axis.Normalize();

                var centerArc = Mathf.Max(0.012f, Vector3.Distance(thumbTip, centerPoint));
                var centerTipDir = axis;
                var centerPlaneDir = Vector3.ProjectOnPlane(centerTipDir, planeNormal);
                if (centerPlaneDir.sqrMagnitude < 0.000001f)
                    centerPlaneDir = Vector3.ProjectOnPlane(surfacePose.forward, planeNormal);
                if (centerPlaneDir.sqrMagnitude < 0.000001f)
                    centerPlaneDir = Vector3.Cross(planeNormal, Vector3.right);
                centerPlaneDir.Normalize();

                var centerAngle = m_Tutorial != null
                    ? m_Tutorial.injectionAngleDegrees
                    : Vector3.Angle(centerPlaneDir, centerTipDir);
                overlayData = new OverlayData
                {
                    tip = thumbTip,
                    projectedTip = centerPoint,
                    insertionPoint = centerPoint,
                    planeNormal = planeNormal,
                    planeDirection = centerPlaneDir,
                    tipDirection = centerTipDir,
                    angleDegrees = centerAngle,
                    distanceMeters = centerArc,
                    arcRadius = centerArc,
                };
                return true;
            }

            if (pipelineStep)
                return false;

            var syringeDirection = syringePose.forward;
            if (syringeDirection.sqrMagnitude < 0.000001f)
                return false;
            syringeDirection.Normalize();

            var plane = new Plane(planeNormal, surfacePose.position);
            var tip = syringePose.needleTip;
            var signedDistance = plane.GetDistanceToPoint(tip);
            var distanceMeters = Mathf.Abs(signedDistance);
            var effectiveMaxTipPlaneDistance = m_MaxTipPlaneDistance;
            var requireInsideSurface = m_RequireProjectionInsideSurface;

            if (m_Tutorial != null)
            {
                if (step == SyringeCalibrationButtonBridge.TutorialStep.InjectionAngle)
                {
                    effectiveMaxTipPlaneDistance = Mathf.Max(effectiveMaxTipPlaneDistance, m_AngleModeMaxTipPlaneDistance);
                    requireInsideSurface = false;
                }
            }

            if (distanceMeters > effectiveMaxTipPlaneDistance)
                return false;

            var projectedTip = tip - planeNormal * signedDistance;
            if (requireInsideSurface &&
                !IsPointInsideSurfaceBounds(projectedTip, surfacePose, surfaceSizeMeters, m_SurfaceBoundsMargin))
            {
                return false;
            }

            // Angle vertex is where the syringe axis intersects the plane.
            var insertionPoint = projectedTip;
            var denominator = Vector3.Dot(planeNormal, syringeDirection);
            if (Mathf.Abs(denominator) > 0.000001f)
            {
                var t = -Vector3.Dot(planeNormal, tip - surfacePose.position) / denominator;
                insertionPoint = tip + syringeDirection * t;
            }

            if (requireInsideSurface &&
                !IsPointInsideSurfaceBounds(insertionPoint, surfacePose, surfaceSizeMeters, m_SurfaceBoundsMargin))
            {
                return false;
            }

            var tipDirection = tip - insertionPoint;
            var arcRadius = tipDirection.magnitude;
            if (arcRadius < 0.000001f)
                return false;
            tipDirection /= arcRadius;

            var planeDirection = Vector3.ProjectOnPlane(tipDirection, planeNormal);
            if (planeDirection.sqrMagnitude < 0.000001f)
            {
                planeDirection = Vector3.ProjectOnPlane(surfacePose.forward, planeNormal);
                if (planeDirection.sqrMagnitude < 0.000001f)
                    planeDirection = Vector3.Cross(planeNormal, Vector3.right);
            }

            if (planeDirection.sqrMagnitude < 0.000001f)
                return false;

            planeDirection.Normalize();

            var angleDegrees = Vector3.Angle(planeDirection, tipDirection);

            overlayData = new OverlayData
            {
                tip = tip,
                projectedTip = projectedTip,
                insertionPoint = insertionPoint,
                planeNormal = planeNormal,
                planeDirection = planeDirection,
                tipDirection = tipDirection,
                angleDegrees = angleDegrees,
                distanceMeters = distanceMeters,
                arcRadius = arcRadius,
            };

            return true;
        }

        static bool IsPointInsideSurfaceBounds(Vector3 worldPoint, Pose surfacePose, Vector2 surfaceSizeMeters, float margin)
        {
            var localPoint = Quaternion.Inverse(surfacePose.rotation) * (worldPoint - surfacePose.position);
            var halfWidth = surfaceSizeMeters.x * 0.5f + margin;
            var halfDepth = surfaceSizeMeters.y * 0.5f + margin;
            return Mathf.Abs(localPoint.x) <= halfWidth && Mathf.Abs(localPoint.z) <= halfDepth;
        }

        void RenderOverlay(OverlayData data)
        {
            m_CurrentAngleDegrees = data.angleDegrees;
            m_CurrentDistanceCentimeters = data.distanceMeters * 100f;

            var radius = Mathf.Max(0.0005f, data.arcRadius);
            var segments = Mathf.Max(8, m_ArcSegments);

            if (m_DropLine != null)
            {
                var showAngleLines = m_Tutorial != null &&
                    (m_Tutorial.currentStep == SyringeCalibrationButtonBridge.TutorialStep.InjectionAngle ||
                     (m_Tutorial.currentStep == SyringeCalibrationButtonBridge.TutorialStep.Insertion &&
                      m_Tracker != null &&
                      (m_Tracker.isThumbToCenterMode || m_Tracker.isNearInjectionSite)));
                m_DropLine.positionCount = 2;
                m_DropLine.startWidth = m_LineWidth;
                m_DropLine.endWidth = m_LineWidth;
                m_DropLine.startColor = m_DropLineColor;
                m_DropLine.endColor = m_DropLineColor;
                m_DropLine.SetPosition(0, data.tip);
                m_DropLine.SetPosition(1, data.projectedTip);
                m_DropLine.enabled = showAngleLines;
            }

            if (m_ThumbToCenterLine != null)
            {
                var showThumbLine = m_Tracker != null &&
                    (m_Tracker.isThumbToCenterMode || m_Tracker.isNearInjectionSite) &&
                    m_Tutorial != null &&
                    (m_Tutorial.currentStep == SyringeCalibrationButtonBridge.TutorialStep.Insertion ||
                     m_Tutorial.currentStep == SyringeCalibrationButtonBridge.TutorialStep.FlowRate ||
                     m_Tutorial.currentStep == SyringeCalibrationButtonBridge.TutorialStep.InsertionSpeedFlowRate);
                m_ThumbToCenterLine.positionCount = 2;
                m_ThumbToCenterLine.startWidth = m_ThumbToCenterLineWidth;
                m_ThumbToCenterLine.endWidth = m_ThumbToCenterLineWidth;
                m_ThumbToCenterLine.startColor = m_ThumbToCenterLineColor;
                m_ThumbToCenterLine.endColor = m_ThumbToCenterLineColor;
                m_ThumbToCenterLine.SetPosition(0, data.tip);
                m_ThumbToCenterLine.SetPosition(1, data.insertionPoint);
                m_ThumbToCenterLine.enabled = showThumbLine;
            }

            if (m_PlaneReferenceLine != null)
            {
                var showAngleLines = m_Tutorial != null &&
                    (m_Tutorial.currentStep == SyringeCalibrationButtonBridge.TutorialStep.InjectionAngle ||
                     (m_Tutorial.currentStep == SyringeCalibrationButtonBridge.TutorialStep.Insertion &&
                      m_Tracker != null &&
                      (m_Tracker.isThumbToCenterMode || m_Tracker.isNearInjectionSite)));
                m_PlaneReferenceLine.positionCount = 2;
                m_PlaneReferenceLine.startWidth = m_LineWidth;
                m_PlaneReferenceLine.endWidth = m_LineWidth;
                m_PlaneReferenceLine.startColor = m_PlaneReferenceColor;
                m_PlaneReferenceLine.endColor = m_PlaneReferenceColor;
                // Keep this anchored at the same point as the tip drop line for a clean intersection.
                m_PlaneReferenceLine.SetPosition(0, data.projectedTip);
                m_PlaneReferenceLine.SetPosition(1, data.insertionPoint);
                m_PlaneReferenceLine.enabled = showAngleLines;
            }

            // Keep the legacy arc rendering code available for quick re-enable, but leave it disabled for guidance mode.
            if (false && m_ArcLine != null)
            {
                var pointCount = segments + 1;
                m_ArcLine.positionCount = pointCount;
                m_ArcLine.startWidth = m_LineWidth;
                m_ArcLine.endWidth = m_LineWidth;
                m_ArcLine.startColor = m_ArcColor;
                m_ArcLine.endColor = m_ArcColor;

                for (var i = 0; i < pointCount; ++i)
                {
                    var t = i / (float)segments;
                    var arcDirection = Vector3.Slerp(data.planeDirection, data.tipDirection, t).normalized;
                    m_ArcLine.SetPosition(i, data.insertionPoint + arcDirection * radius);
                }
            }

            if (m_AngleText != null)
            {
                var isAngleStep = m_Tutorial != null &&
                    (m_Tutorial.currentStep == SyringeCalibrationButtonBridge.TutorialStep.InjectionAngle ||
                     (m_Tutorial.currentStep == SyringeCalibrationButtonBridge.TutorialStep.Insertion &&
                      m_Tracker != null &&
                      (m_Tracker.isThumbToCenterMode || m_Tracker.isNearInjectionSite)));
                var textVisible = m_ShowAngleText && isAngleStep;
                m_AngleText.gameObject.SetActive(textVisible);
                if (textVisible)
                {
                    var textDirection = Vector3.Slerp(data.planeDirection, data.tipDirection, 0.5f);
                    textDirection = Vector3.ProjectOnPlane(textDirection, data.planeNormal);
                    if (textDirection.sqrMagnitude < 0.000001f)
                        textDirection = data.planeDirection;
                    textDirection.Normalize();

                    m_AngleText.text = $"{data.angleDegrees:0.0}°";
                    var angleInBand = m_Tutorial != null &&
                        data.angleDegrees >= m_Tutorial.targetInjectionAngleRange.x &&
                        data.angleDegrees <= m_Tutorial.targetInjectionAngleRange.y;
                    m_AngleText.color = angleInBand ? m_TextColorInBand : m_TextColorOutOfBand;
                    m_AngleText.characterSize = m_TextCharacterSize;
                    m_AngleText.transform.position =
                        data.insertionPoint +
                        textDirection * (radius + m_TextOffsetFromArc) +
                        data.planeNormal * m_TextLiftAbovePlane;

                    if (m_MainCamera != null)
                    {
                        var textPosition = m_AngleText.transform.position;
                        var toCamera = m_MainCamera.transform.position - textPosition;
                        var facingOnPlane = Vector3.ProjectOnPlane(toCamera, data.planeNormal);
                        if (facingOnPlane.sqrMagnitude < 0.000001f)
                            facingOnPlane = Vector3.ProjectOnPlane(-m_MainCamera.transform.forward, data.planeNormal);
                        if (facingOnPlane.sqrMagnitude < 0.000001f)
                            facingOnPlane = data.planeDirection;

                        facingOnPlane.Normalize();
                        var textRotation = Quaternion.LookRotation(facingOnPlane, data.planeNormal);

                        // TextMesh front faces opposite to the desired camera-facing direction here.
                        // Flip around the plane normal so glyphs are not mirrored left-right.
                        textRotation = Quaternion.AngleAxis(180f, data.planeNormal) * textRotation;
                        m_AngleText.transform.rotation = textRotation;

                        // Ensure text is upright relative to the surface normal.
                        if (Vector3.Dot(m_AngleText.transform.up, data.planeNormal) < 0f)
                        {
                            m_AngleText.transform.rotation =
                                Quaternion.AngleAxis(180f, m_AngleText.transform.forward) * m_AngleText.transform.rotation;
                        }
                    }
                }
            }

            DrawGuidanceOverlay(data);
            SetOverlayVisible(true);
        }

        public void SetGuidanceStep(SyringeCalibrationButtonBridge.TutorialStep step)
        {
            m_GuidanceStep = step;
        }

        void DrawGuidanceOverlay(OverlayData data)
        {
            if (!m_ShowGuidanceArrows || m_Tutorial == null || m_GuidanceArrow == null || m_GuidanceText == null || m_GuidanceDot == null)
                return;

            // Source the active guidance step from tutorial state each frame.
            m_GuidanceStep = m_Tutorial.currentStep;

            var showArrow = false;
            var showDot = false;
            var isOptimal = false;
            var text = string.Empty;
            var basePoint = data.insertionPoint + data.planeNormal * 0.025f;
            var arrowDir = data.planeNormal;

            if (m_GuidanceStep == SyringeCalibrationButtonBridge.TutorialStep.InjectionAngle)
            {
                var range = m_Tutorial.targetInjectionAngleRange;
                var angle = m_Tutorial.injectionAngleDegrees;
                var inRange = angle >= range.x && angle <= range.y;

                if (inRange)
                {
                    showDot = true;
                    isOptimal = true;
                    text = "Maintain angle";
                    if (m_Tutorial.angleHoldSecondsRemaining > 0.01f)
                        text += " (" + m_Tutorial.angleHoldSecondsRemaining.ToString("F1") + "s)";
                }
                else
                {
                    showArrow = true;
                    var needHigher = m_Tutorial.angleGuidanceErrorDegrees > 0f;
                    arrowDir = needHigher ? data.planeNormal : -data.planeNormal;
                    text = needHigher ? "Lift syringe higher" : "Lower syringe";
                    text += " (target " + range.x.ToString("F0") + "-" + range.y.ToString("F0") + " deg)";
                }
            }
            else if (m_GuidanceStep == SyringeCalibrationButtonBridge.TutorialStep.Insertion)
            {
                var needDepth = m_Tutorial.currentInsertionDepthCm < m_Tutorial.effectiveInsertionDepthTargetCm * 0.85f;
                var unstable = m_Tutorial.currentLateralStabilityCmPerSec > m_Tutorial.maxLateralStabilityCmPerSec;

                if (needDepth)
                {
                    showArrow = true;
                    arrowDir = -data.planeNormal;
                    text = "Move hand closer / insert";
                }
                else if (unstable)
                {
                    showArrow = true;
                    arrowDir = -Vector3.ProjectOnPlane(data.tipDirection, data.planeNormal);
                    if (arrowDir.sqrMagnitude < 0.000001f)
                        arrowDir = data.planeNormal;
                    text = "Steady hand";
                }
                else
                {
                    showDot = true;
                    isOptimal = true;
                    text = "Hold or start dispensing";
                }
            }
            else if (m_GuidanceStep == SyringeCalibrationButtonBridge.TutorialStep.FlowRate ||
                     m_GuidanceStep == SyringeCalibrationButtonBridge.TutorialStep.InsertionSpeedFlowRate)
            {
                var flowDelta = m_Tutorial.flowRateMlPerSec - m_Tutorial.targetFlowRateMlPerSec;
                var unstable = m_Tutorial.currentLateralStabilityCmPerSec > m_Tutorial.maxLateralStabilityCmPerSec;

                if (unstable)
                {
                    showDot = true;
                    text = "Keep syringe steady";
                }
                else if (flowDelta < -0.25f)
                {
                    showArrow = true;
                    arrowDir = data.tipDirection;
                    text = "Close thumb faster";
                }
                else if (flowDelta > 0.25f)
                {
                    showArrow = true;
                    arrowDir = -data.tipDirection;
                    text = "Ease thumb pressure";
                }
                else
                {
                    showArrow = true;
                    arrowDir = data.tipDirection;
                    isOptimal = true;
                    text = "Maintain flow";
                }

                text += " (" + (m_Tutorial.plungerTravelNormalized * 100f).ToString("F0") + "%)";
            }
            else if (m_GuidanceStep == SyringeCalibrationButtonBridge.TutorialStep.InsertionSpeedFlowRate)
            {
                if (!m_Tutorial.isDispensePhase)
                {
                    var needDepth = m_Tutorial.currentInsertionDepthCm < m_Tutorial.effectiveInsertionDepthTargetCm * 0.85f;
                    var unstable = m_Tutorial.currentLateralStabilityCmPerSec > m_Tutorial.maxLateralStabilityCmPerSec;

                    if (needDepth)
                    {
                        showArrow = true;
                        arrowDir = -data.planeNormal;
                        text = "Insert toward center";
                    }
                    else if (unstable)
                    {
                        showArrow = true;
                        arrowDir = -Vector3.ProjectOnPlane(data.tipDirection, data.planeNormal);
                        if (arrowDir.sqrMagnitude < 0.000001f)
                            arrowDir = data.planeNormal;
                        text = "Steady hand";
                    }
                    else
                    {
                        showArrow = true;
                        arrowDir = -data.planeNormal;
                        isOptimal = true;
                        text = "Hold or dispense";
                    }
                }
                else
                {
                    var flowDelta = m_Tutorial.flowRateMlPerSec - m_Tutorial.targetFlowRateMlPerSec;
                    var unstable = m_Tutorial.currentLateralStabilityCmPerSec > m_Tutorial.maxLateralStabilityCmPerSec;

                    if (unstable)
                    {
                        showDot = true;
                        text = "Keep syringe steady";
                    }
                    else if (flowDelta < -0.25f)
                    {
                        showArrow = true;
                        arrowDir = data.tipDirection;
                        text = "Close thumb faster";
                    }
                    else if (flowDelta > 0.25f)
                    {
                        showArrow = true;
                        arrowDir = -data.tipDirection;
                        text = "Ease thumb pressure";
                    }
                    else
                    {
                        showArrow = true;
                        arrowDir = data.tipDirection;
                        isOptimal = true;
                        text = "Maintain flow";
                    }

                    text += " (" + (m_Tutorial.plungerTravelNormalized * 100f).ToString("F0") + "%)";
                }
            }

            var geometryColor = isOptimal ? m_GuidanceOptimalColor : m_GuidanceGeometryColor;
            var textColor = isOptimal ? m_GuidanceTextOptimalColor : m_GuidanceTextWarningColor;

            m_GuidanceArrow.enabled = showArrow;
            m_GuidanceDot.gameObject.SetActive(showDot);
            m_GuidanceText.gameObject.SetActive(showArrow || showDot);

            if (showArrow)
            {
                arrowDir = arrowDir.sqrMagnitude < 0.000001f ? data.planeNormal : arrowDir.normalized;
                var tail = basePoint - arrowDir * (m_GuidanceArrowLength * 0.5f);
                var head = basePoint + arrowDir * (m_GuidanceArrowLength * 0.5f);
                var sideAxis = Vector3.Cross(arrowDir, data.planeNormal);
                if (sideAxis.sqrMagnitude < 0.000001f)
                    sideAxis = Vector3.Cross(arrowDir, Vector3.up);
                sideAxis.Normalize();

                var wingA = head - arrowDir * m_GuidanceArrowHeadLength + sideAxis * (m_GuidanceArrowHeadLength * 0.45f);
                var wingB = head - arrowDir * m_GuidanceArrowHeadLength - sideAxis * (m_GuidanceArrowHeadLength * 0.45f);

                m_GuidanceArrow.positionCount = 5;
                m_GuidanceArrow.SetPosition(0, tail);
                m_GuidanceArrow.SetPosition(1, head);
                m_GuidanceArrow.SetPosition(2, wingA);
                m_GuidanceArrow.SetPosition(3, head);
                m_GuidanceArrow.SetPosition(4, wingB);
                m_GuidanceArrow.startWidth = m_GuidanceArrowWidth;
                m_GuidanceArrow.endWidth = m_GuidanceArrowWidth;
                m_GuidanceArrow.startColor = geometryColor;
                m_GuidanceArrow.endColor = geometryColor;
            }

            if (showDot)
            {
                m_GuidanceDot.position = basePoint;
                m_GuidanceDot.localScale = Vector3.one * m_GuidanceDotSize;
                if (m_GuidanceDot.TryGetComponent<Renderer>(out var dotRenderer) && dotRenderer.sharedMaterial != null)
                    dotRenderer.sharedMaterial.color = geometryColor;
            }

            if (m_GuidanceText.gameObject.activeSelf)
            {
                m_GuidanceText.text = text;
                m_GuidanceText.color = textColor;
                m_GuidanceText.characterSize = m_GuidanceTextCharacterSize;
                m_GuidanceText.fontSize = 64;
                m_GuidanceText.transform.position = basePoint + data.planeNormal * m_GuidanceTextLiftAboveBasePoint;

                if (m_MainCamera != null)
                    m_GuidanceText.transform.rotation = Quaternion.LookRotation(m_GuidanceText.transform.position - m_MainCamera.transform.position, data.planeNormal);
            }
        }

        void SetOverlayVisible(bool visible)
        {
            m_IsVisible = visible;

            if (!visible)
            {
                m_CurrentAngleDegrees = 0f;
                m_CurrentDistanceCentimeters = 0f;
            }

            if (m_DropLine != null)
                m_DropLine.enabled = visible && m_DropLine.enabled;

            if (m_ThumbToCenterLine != null)
                m_ThumbToCenterLine.enabled = visible && m_ThumbToCenterLine.enabled;

            if (m_PlaneReferenceLine != null)
                m_PlaneReferenceLine.enabled = visible && m_PlaneReferenceLine.enabled;

            if (m_ArcLine != null)
                m_ArcLine.enabled = visible && m_ShowArc;

            if (m_AngleText != null)
                m_AngleText.gameObject.SetActive(visible && m_ShowAngleText && m_AngleText.gameObject.activeSelf);

            if (m_GuidanceArrow != null)
                m_GuidanceArrow.enabled = visible && m_ShowGuidanceArrows && m_GuidanceArrow.enabled;

            if (m_GuidanceDot != null)
                m_GuidanceDot.gameObject.SetActive(visible && m_ShowGuidanceArrows && m_GuidanceDot.gameObject.activeSelf);

            if (m_GuidanceText != null)
                m_GuidanceText.gameObject.SetActive(visible && m_ShowGuidanceArrows && m_GuidanceText.gameObject.activeSelf);
        }

        void CreateVisuals()
        {
            if (m_Root != null)
                return;

            m_Root = new GameObject("Syringe Angle Overlay").transform;
            m_Root.SetParent(transform, false);

            m_RuntimeLineMaterial = CreateRuntimeMaterial();
            m_RuntimeGuidanceMaterial = CreateRuntimeMaterial();

            m_DropLine = CreateLine("Needle To Surface", m_RuntimeLineMaterial);
            m_ThumbToCenterLine = CreateLine("Thumb To Center", m_RuntimeLineMaterial);
            m_PlaneReferenceLine = CreateLine("Surface Direction Reference", m_RuntimeLineMaterial);
            m_ArcLine = CreateLine("Syringe Angle Arc", m_RuntimeLineMaterial);
            m_ArcLine.numCornerVertices = 3;

            m_GuidanceArrow = CreateLine("Guidance Arrow", m_RuntimeGuidanceMaterial);
            m_GuidanceArrow.startWidth = m_GuidanceArrowWidth;
            m_GuidanceArrow.endWidth = m_GuidanceArrowWidth;

            var textObject = new GameObject("Syringe Angle Text");
            textObject.transform.SetParent(m_Root, false);
            m_AngleText = textObject.AddComponent<TextMesh>();
            m_AngleText.fontSize = 40;
            m_AngleText.anchor = TextAnchor.MiddleCenter;
            m_AngleText.alignment = TextAlignment.Center;
            m_AngleText.characterSize = m_TextCharacterSize;
            m_AngleText.color = m_TextColorOutOfBand;
            m_AngleText.text = string.Empty;

            var guidanceTextObject = new GameObject("Guidance Text");
            guidanceTextObject.transform.SetParent(m_Root, false);
            m_GuidanceText = guidanceTextObject.AddComponent<TextMesh>();
            m_GuidanceText.fontSize = 64;
            m_GuidanceText.anchor = TextAnchor.MiddleCenter;
            m_GuidanceText.alignment = TextAlignment.Center;
            m_GuidanceText.characterSize = m_GuidanceTextCharacterSize;
            m_GuidanceText.color = m_GuidanceTextWarningColor;
            m_GuidanceText.richText = true;
            m_GuidanceText.text = string.Empty;

            var dotObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dotObj.name = "Guidance Dot";
            dotObj.transform.SetParent(m_Root, false);
            dotObj.transform.localScale = Vector3.one * m_GuidanceDotSize;
            if (dotObj.TryGetComponent<Collider>(out var dotCollider))
                Destroy(dotCollider);
            if (dotObj.TryGetComponent<Renderer>(out var dotRenderer))
            {
                dotRenderer.sharedMaterial = m_RuntimeGuidanceMaterial;
                dotRenderer.sharedMaterial.color = m_GuidanceOptimalColor;
            }
            m_GuidanceDot = dotObj.transform;
            m_GuidanceDot.gameObject.SetActive(false);
            m_GuidanceText.gameObject.SetActive(false);

            SetOverlayVisible(false);
        }

        LineRenderer CreateLine(string name, Material material)
        {
            var lineObject = new GameObject(name);
            lineObject.transform.SetParent(m_Root, false);

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

        static Material CreateRuntimeMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Sprites/Default") ??
                         Shader.Find("Unlit/Color") ??
                         Shader.Find("Standard");

            var material = new Material(shader);
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_SrcBlend"))
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite"))
                material.SetInt("_ZWrite", 0);
            if (material.HasProperty("_ZTest"))
                material.SetInt("_ZTest", (int)CompareFunction.Always);
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }
    }
}