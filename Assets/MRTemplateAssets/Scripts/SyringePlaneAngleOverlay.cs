using UnityEngine.Rendering;

namespace UnityEngine.XR.Templates.MR
{
    /// <summary>
    /// Renders a live syringe-vs-surface angle overlay when the needle tip is near a placed surface.
    /// </summary>
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
        Camera m_MainCamera;

        [Header("Visibility")]
        [SerializeField, Min(0.005f)]
        float m_MaxTipPlaneDistance = 0.03f;

        [SerializeField]
        bool m_RequireProjectionInsideSurface = true;

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

        [Header("Angle text")]
        [SerializeField]
        bool m_ShowAngleText = true;

        [SerializeField]
        Color m_TextColor = Color.white;

        [SerializeField, Min(0.0005f)]
        float m_TextCharacterSize = 0.0021f;

        [SerializeField, Min(0f)]
        float m_TextOffsetFromArc = 0.007f;

        [SerializeField, Min(0f)]
        float m_TextLiftAbovePlane = 0.015f;

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
        LineRenderer m_ArcLine;
        TextMesh m_AngleText;
        Material m_RuntimeLineMaterial;

        void Awake()
        {
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
        }

        void ResolveReferences()
        {
            if (m_Tracker == null)
                m_Tracker = GetComponent<SyringeOverlayTracker>() ?? FindAnyObjectByType<SyringeOverlayTracker>();

            if (m_SurfaceSelectionTool == null)
                m_SurfaceSelectionTool = GetComponent<SurfaceSelectionTool>() ?? FindAnyObjectByType<SurfaceSelectionTool>();

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

            var syringeDirection = syringePose.forward;
            if (syringeDirection.sqrMagnitude < 0.000001f)
                return false;
            syringeDirection.Normalize();

            var planeNormal = surfacePose.up;
            if (planeNormal.sqrMagnitude < 0.000001f)
                planeNormal = Vector3.up;
            else
                planeNormal.Normalize();

            var plane = new Plane(planeNormal, surfacePose.position);
            var tip = syringePose.needleTip;
            var signedDistance = plane.GetDistanceToPoint(tip);
            var distanceMeters = Mathf.Abs(signedDistance);
            if (distanceMeters > m_MaxTipPlaneDistance)
                return false;

            var projectedTip = tip - planeNormal * signedDistance;
            if (m_RequireProjectionInsideSurface &&
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

            if (m_RequireProjectionInsideSurface &&
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
                m_DropLine.positionCount = 2;
                m_DropLine.startWidth = m_LineWidth;
                m_DropLine.endWidth = m_LineWidth;
                m_DropLine.startColor = m_DropLineColor;
                m_DropLine.endColor = m_DropLineColor;
                m_DropLine.SetPosition(0, data.tip);
                m_DropLine.SetPosition(1, data.projectedTip);
            }

            if (m_PlaneReferenceLine != null)
            {
                m_PlaneReferenceLine.positionCount = 2;
                m_PlaneReferenceLine.startWidth = m_LineWidth;
                m_PlaneReferenceLine.endWidth = m_LineWidth;
                m_PlaneReferenceLine.startColor = m_PlaneReferenceColor;
                m_PlaneReferenceLine.endColor = m_PlaneReferenceColor;
                // Keep this anchored at the same point as the tip drop line for a clean intersection.
                m_PlaneReferenceLine.SetPosition(0, data.projectedTip);
                m_PlaneReferenceLine.SetPosition(1, data.insertionPoint);
            }

            if (m_ArcLine != null)
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
                var textVisible = m_ShowAngleText;
                m_AngleText.gameObject.SetActive(textVisible);
                if (textVisible)
                {
                    var textDirection = Vector3.Slerp(data.planeDirection, data.tipDirection, 0.5f);
                    textDirection = Vector3.ProjectOnPlane(textDirection, data.planeNormal);
                    if (textDirection.sqrMagnitude < 0.000001f)
                        textDirection = data.planeDirection;
                    textDirection.Normalize();

                    m_AngleText.text = $"{data.angleDegrees:0.0}°";
                    m_AngleText.color = m_TextColor;
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

            SetOverlayVisible(true);
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
                m_DropLine.enabled = visible;

            if (m_PlaneReferenceLine != null)
                m_PlaneReferenceLine.enabled = visible;

            if (m_ArcLine != null)
                m_ArcLine.enabled = visible && m_ShowArc;

            if (m_AngleText != null)
                m_AngleText.gameObject.SetActive(visible && m_ShowAngleText);
        }

        void CreateVisuals()
        {
            if (m_Root != null)
                return;

            m_Root = new GameObject("Syringe Angle Overlay").transform;
            m_Root.SetParent(transform, false);

            m_RuntimeLineMaterial = CreateRuntimeMaterial();

            m_DropLine = CreateLine("Needle To Surface", m_RuntimeLineMaterial);
            m_PlaneReferenceLine = CreateLine("Surface Direction Reference", m_RuntimeLineMaterial);
            m_ArcLine = CreateLine("Syringe Angle Arc", m_RuntimeLineMaterial);
            m_ArcLine.numCornerVertices = 3;

            var textObject = new GameObject("Syringe Angle Text");
            textObject.transform.SetParent(m_Root, false);
            m_AngleText = textObject.AddComponent<TextMesh>();
            m_AngleText.fontSize = 40;
            m_AngleText.anchor = TextAnchor.MiddleCenter;
            m_AngleText.alignment = TextAlignment.Center;
            m_AngleText.characterSize = m_TextCharacterSize;
            m_AngleText.color = m_TextColor;
            m_AngleText.text = string.Empty;

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