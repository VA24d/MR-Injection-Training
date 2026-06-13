using UnityEngine.Rendering;

namespace UnityEngine.XR.Templates.MR
{
    /// <summary>
    /// Draws the 3D injection target guide at the pinched-surface center:
    ///  - a spot disc that shrinks (10 mm -> 4 mm) as the needle approaches,
    ///  - a blue valid-angle band (two cones) for the selected injection type,
    ///  - a green correct-depth segment and red over-depth segment along the ideal needle path,
    ///  - an orange max-depth plane.
    /// Surface + per-type config come from <see cref="SurfaceSelectionTool"/> and
    /// <see cref="SyringeCalibrationButtonBridge"/>; the live syringe pose only refines the spot
    /// size and the depth-guide azimuth, so the guide still renders before the syringe is tracked.
    /// </summary>
    public class InjectionTargetGuide : MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        SurfaceSelectionTool m_SurfaceSelectionTool;

        [SerializeField]
        SyringeOverlayTracker m_Tracker;

        [SerializeField]
        SyringeCalibrationButtonBridge m_Tutorial;

        [SerializeField]
        Camera m_MainCamera;

        [Header("Spot")]
        [SerializeField, Min(0.001f)]
        float m_SpotMaxDiameter = 0.010f;

        [SerializeField, Min(0.001f)]
        float m_SpotMinDiameter = 0.004f;

        [SerializeField, Min(0.005f)]
        [Tooltip("Needle-to-surface distance (m) at or beyond which the spot is at full size.")]
        float m_SpotShrinkRangeMeters = 0.12f;

        [Header("Angle band (blue cone)")]
        [SerializeField, Min(0.005f)]
        [Tooltip("Slant length of the valid-angle band (m) — how far it sits out from the spot. Fixed regardless of angle, so wide-angle types do not balloon.")]
        float m_ConeSlantMeters = 0.06f;

        [SerializeField, Range(8, 64)]
        int m_ConeSegments = 32;

        [SerializeField, Min(0)]
        [Tooltip("Number of radial ribs (apex->rim spokes) drawn so the band reads as a cone anchored at the spot. 0 = no ribs.")]
        int m_ConeRibCount = 12;

        [Header("Depth guide")]
        [SerializeField, Min(0.0005f)]
        float m_DepthLineWidth = 0.004f;

        [SerializeField, Min(0.002f)]
        float m_OrangeDiscRadius = 0.02f;

        [SerializeField, Min(0.001f)]
        [Tooltip("Radius (m) of the depth channel at the skin; tapers to the tip radius at max depth.")]
        float m_DepthChannelTopRadius = 0.008f;

        [SerializeField, Min(0.0005f)]
        [Tooltip("Radius (m) of the depth channel at max depth (needle-tip end).")]
        float m_DepthChannelTipRadius = 0.0015f;

        [Header("Colors")]
        [SerializeField]
        Color m_SpotColor = new Color(1f, 1f, 1f, 0.6f);

        [SerializeField]
        Color m_BandColor = new Color(0.2f, 0.5f, 1f, 0.35f);

        [SerializeField]
        [Tooltip("Band tint while the live injection angle is inside the type's valid range.")]
        Color m_BandInRangeColor = new Color(0.22f, 0.95f, 0.55f, 0.45f);

        [SerializeField]
        Color m_GreenColor = new Color(0.15f, 1f, 0.25f, 0.5f);

        [SerializeField]
        Color m_RedColor = new Color(1f, 0.2f, 0.15f, 0.6f);

        [SerializeField]
        Color m_OrangeColor = new Color(1f, 0.55f, 0.1f, 0.4f);

        Transform m_Root;
        Transform m_Spot;
        Transform m_ConeBand;
        Transform m_OrangePlane;
        Transform m_GreenChannel;
        Transform m_RedChannel;
        LineRenderer m_ConeRibs;

        Mesh m_DiscMesh;
        Mesh m_ConeBandMesh;
        Mesh m_GreenChannelMesh;
        Mesh m_RedChannelMesh;
        Material m_SpotMat;
        Material m_BandMat;
        Material m_GreenMat;
        Material m_RedMat;
        Material m_OrangeMat;

        // Caches so meshes are only rebuilt when their driving values change.
        Vector2 m_BuiltBandFromNormal = new Vector2(-1f, -1f);
        float m_BuiltGreenMeters = -1f;
        float m_BuiltMaxMeters = -1f;

        // Type-screen preview: anchored ONCE in front of the user (world-fixed), so it does not
        // follow the head/hand or pulse. Cleared when a surface is placed or the step changes.
        bool m_PreviewActive;
        Vector3 m_PreviewSpot;
        Vector3 m_PreviewHeading = Vector3.forward;

        void Awake()
        {
            CreateVisuals();
            ResolveReferences();
        }

        void Update()
        {
            ResolveReferences();

            if (!TryBuildGuide())
                SetVisible(false);
        }

        void OnDestroy()
        {
            DestroySafe(m_SpotMat);
            DestroySafe(m_BandMat);
            DestroySafe(m_GreenMat);
            DestroySafe(m_RedMat);
            DestroySafe(m_OrangeMat);
            DestroySafe(m_DiscMesh);
            DestroySafe(m_ConeBandMesh);
            DestroySafe(m_GreenChannelMesh);
            DestroySafe(m_RedChannelMesh);
        }

        void ResolveReferences()
        {
            if (m_SurfaceSelectionTool == null)
                m_SurfaceSelectionTool = FindAnyObjectByType<SurfaceSelectionTool>();
            if (m_Tracker == null)
                m_Tracker = FindAnyObjectByType<SyringeOverlayTracker>();
            if (m_Tutorial == null)
                m_Tutorial = FindAnyObjectByType<SyringeCalibrationButtonBridge>();
            if (m_MainCamera == null)
                m_MainCamera = Camera.main;
        }

        bool TryBuildGuide()
        {
            if (m_Tutorial == null)
                return false;

            // Show whenever a surface is placed, from the Injection Type screen through the injection
            // steps — so the cone appears as soon as the surface exists and the user watches it change
            // as they cycle injection type. Hidden only before type selection and at the score screen.
            var step = m_Tutorial.currentStep;
            if (step == SyringeCalibrationButtonBridge.TutorialStep.Start ||
                step == SyringeCalibrationButtonBridge.TutorialStep.Calibration ||
                step == SyringeCalibrationButtonBridge.TutorialStep.FinalScore)
                return false;

            Vector3 spot;
            Vector3 normal;
            bool previewMode;
            if (m_SurfaceSelectionTool != null && m_SurfaceSelectionTool.TryGetPlacedSurface(out var surfacePose, out _))
            {
                spot = surfacePose.position;
                normal = surfacePose.up;
                normal = normal.sqrMagnitude < 0.000001f ? Vector3.up : normal.normalized;
                previewMode = false;
                m_PreviewActive = false;
            }
            else if (step == SyringeCalibrationButtonBridge.TutorialStep.InjectionType && m_MainCamera != null)
            {
                // No surface yet on the type screen — show a preview cone. Anchor it ONCE in front of
                // the user and leave it world-fixed, so it does not follow the head/hand or pulse.
                if (!m_PreviewActive)
                {
                    var cam = m_MainCamera.transform;
                    m_PreviewSpot = cam.position + cam.forward * 0.5f - cam.up * 0.1f;
                    var heading = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
                    m_PreviewHeading = heading.sqrMagnitude > 0.000001f ? heading.normalized : Vector3.forward;
                    m_PreviewActive = true;
                }
                spot = m_PreviewSpot;
                normal = Vector3.up;
                previewMode = true;
            }
            else
            {
                m_PreviewActive = false;
                return false;
            }

            // Declared + defaulted before the short-circuit so it is definitely assigned even when
            // m_Tracker is null (TryGetSyringePose is then never called).
            SyringeOverlayTracker.SyringePoseData syringe = default;
            var hasPose = m_Tracker != null && m_Tracker.TryGetSyringePose(out syringe);
            var syringeForward = hasPose ? syringe.forward : Vector3.zero;

            // Spot disc shrinks as the needle nears the surface — but stays fixed in preview (no
            // real surface, so the hand-to-spot distance is meaningless and would make it pulse).
            float diameter;
            if (previewMode)
            {
                diameter = m_SpotMaxDiameter;
            }
            else
            {
                var needleDistance = hasPose
                    ? Mathf.Abs(Vector3.Dot(syringe.needleTip - spot, normal))
                    : m_SpotShrinkRangeMeters;
                diameter = Mathf.Lerp(m_SpotMinDiameter, m_SpotMaxDiameter,
                    Mathf.Clamp01(needleDistance / Mathf.Max(0.001f, m_SpotShrinkRangeMeters)));
                diameter = Mathf.Max(diameter, m_SpotMinDiameter);
            }
            PlaceDisc(m_Spot, spot + normal * 0.001f, normal, diameter * 0.5f);

            // Blue band: a slant ribbon between the steep (inner) and shallow (outer) valid angles,
            // measured from the surface normal. Fixed slant from the spot, so it never balloons.
            var range = m_Tutorial.targetInjectionAngleRange;
            var bandFromNormal = new Vector2(
                Mathf.Clamp(90f - range.y, 0f, 89f),    // steepest valid (closest to the normal)
                Mathf.Clamp(90f - range.x, 0.5f, 89f)); // shallowest valid
            EnsureConeBandMesh(bandFromNormal);
            m_ConeBand.SetPositionAndRotation(spot, Quaternion.FromToRotation(Vector3.up, normal));
            BuildConeRibs(bandFromNormal);

            // Tint the band green while the live injection angle is inside the valid range.
            var angle = m_Tutorial.injectionAngleDegrees;
            var inRange = angle >= range.x && angle <= range.y;
            SetBandColor(inRange ? m_BandInRangeColor : m_BandColor);

            // Depth zones: a tapering channel below the skin along the ideal needle path. Green =
            // correct depth (skin -> accurate), red = too deep (accurate -> max), orange disk = max.
            // In preview, drive the depth azimuth from the fixed anchored heading (not the live hand),
            // so the channel does not swing around with hand movement.
            var headingForward = previewMode ? m_PreviewHeading : syringeForward;
            var ideal = ComputeIdealInjectionAxis(normal, headingForward);
            var greenMeters = m_Tutorial.currentInjectionGreenDepthCm * 0.01f;
            var maxMeters = Mathf.Max(greenMeters + 0.001f, m_Tutorial.currentInjectionMaxDepthCm * 0.01f);
            EnsureDepthMeshes(greenMeters, maxMeters);

            var depthRotation = Quaternion.FromToRotation(Vector3.up, ideal);
            m_GreenChannel.SetPositionAndRotation(spot, depthRotation);
            m_RedChannel.SetPositionAndRotation(spot, depthRotation);
            PlaceDisc(m_OrangePlane, spot + ideal * maxMeters, ideal, DepthRadiusAt(maxMeters, maxMeters) + 0.003f);

            SetVisible(true);
            return true;
        }

        // Channel radius tapers from the skin (wide) down to the needle tip (narrow) at max depth.
        float DepthRadiusAt(float depthMeters, float maxMeters)
        {
            var t = maxMeters > 0.0001f ? Mathf.Clamp01(depthMeters / maxMeters) : 0f;
            return Mathf.Lerp(m_DepthChannelTopRadius, m_DepthChannelTipRadius, t);
        }

        void EnsureDepthMeshes(float greenMeters, float maxMeters)
        {
            if (Mathf.Approximately(greenMeters, m_BuiltGreenMeters) &&
                Mathf.Approximately(maxMeters, m_BuiltMaxMeters))
                return;

            // Local +Y runs inward along the ideal axis; y = depth below the skin.
            BuildFrustumMesh(m_GreenChannelMesh,
                0f, DepthRadiusAt(0f, maxMeters),
                greenMeters, DepthRadiusAt(greenMeters, maxMeters));
            BuildFrustumMesh(m_RedChannelMesh,
                greenMeters, DepthRadiusAt(greenMeters, maxMeters),
                maxMeters, DepthRadiusAt(maxMeters, maxMeters));

            m_BuiltGreenMeters = greenMeters;
            m_BuiltMaxMeters = maxMeters;
        }

        // Double-sided conical frustum lateral surface between (yTop,rTop) and (yBottom,rBottom) along +Y.
        void BuildFrustumMesh(Mesh mesh, float yTop, float rTop, float yBottom, float rBottom)
        {
            mesh.Clear();
            var seg = Mathf.Max(8, m_ConeSegments);
            var verts = new Vector3[(seg + 1) * 2];
            var tris = new int[seg * 12];

            for (var i = 0; i <= seg; i++)
            {
                var az = (i / (float)seg) * Mathf.PI * 2f;
                var c = Mathf.Cos(az);
                var s = Mathf.Sin(az);
                verts[i * 2] = new Vector3(c * rTop, yTop, s * rTop);
                verts[i * 2 + 1] = new Vector3(c * rBottom, yBottom, s * rBottom);
            }

            var t = 0;
            for (var i = 0; i < seg; i++)
            {
                int a = i * 2, b = i * 2 + 1, cc = (i + 1) * 2, d = (i + 1) * 2 + 1;
                tris[t++] = a; tris[t++] = cc; tris[t++] = b;
                tris[t++] = b; tris[t++] = cc; tris[t++] = d;
                tris[t++] = b; tris[t++] = cc; tris[t++] = a;
                tris[t++] = d; tris[t++] = cc; tris[t++] = b;
            }

            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// Ideal needle direction below the surface: nominal (range-midpoint) angle, azimuth from the
        /// live syringe heading so the depth guide aligns under the user's approach. Mirrors the
        /// equivalent helper in <see cref="SyringeCalibrationButtonBridge"/>.
        /// </summary>
        Vector3 ComputeIdealInjectionAxis(Vector3 normal, Vector3 syringeForward)
        {
            var down = -normal;

            var heading = Vector3.ProjectOnPlane(syringeForward, normal);
            if (heading.sqrMagnitude < 0.000001f && m_MainCamera != null)
                heading = Vector3.ProjectOnPlane(m_MainCamera.transform.forward, normal);
            if (heading.sqrMagnitude < 0.000001f)
                heading = Vector3.ProjectOnPlane(Vector3.forward, normal);
            if (heading.sqrMagnitude < 0.000001f)
                return down;
            heading.Normalize();

            var fromSurface = Mathf.Clamp(m_Tutorial.idealInjectionAngleDegrees, 0f, 90f);
            var fromNormalRad = (90f - fromSurface) * Mathf.Deg2Rad;
            var axis = down * Mathf.Cos(fromNormalRad) + heading * Mathf.Sin(fromNormalRad);
            return axis.sqrMagnitude < 0.000001f ? down : axis.normalized;
        }

        void PlaceDisc(Transform disc, Vector3 position, Vector3 axisUp, float radius)
        {
            disc.position = position;
            disc.rotation = Quaternion.FromToRotation(Vector3.up, axisUp.normalized);
            disc.localScale = new Vector3(radius, 1f, radius);
        }

        // Unit direction at angleFromNormal degrees off local +Y (the surface normal), swept by azimuth.
        static Vector3 ConeDirection(float angleFromNormalDeg, float azimuthRad)
        {
            var a = angleFromNormalDeg * Mathf.Deg2Rad;
            var s = Mathf.Sin(a);
            return new Vector3(s * Mathf.Cos(azimuthRad), Mathf.Cos(a), s * Mathf.Sin(azimuthRad));
        }

        void EnsureConeBandMesh(Vector2 bandFromNormal)
        {
            if (Mathf.Approximately(bandFromNormal.x, m_BuiltBandFromNormal.x) &&
                Mathf.Approximately(bandFromNormal.y, m_BuiltBandFromNormal.y))
                return;

            BuildConeBandMesh(m_ConeBandMesh, bandFromNormal, m_ConeSlantMeters);
            m_BuiltBandFromNormal = bandFromNormal;
        }

        // Conical ribbon between the steep (x) and shallow (y) edges, at fixed slant from the apex.
        void BuildConeBandMesh(Mesh mesh, Vector2 bandDeg, float slant)
        {
            mesh.Clear();
            var seg = Mathf.Max(8, m_ConeSegments);
            var verts = new Vector3[(seg + 1) * 2];
            var tris = new int[seg * 12];

            for (var i = 0; i <= seg; i++)
            {
                var az = (i / (float)seg) * Mathf.PI * 2f;
                verts[i * 2] = ConeDirection(bandDeg.x, az) * slant;     // inner edge (steepest)
                verts[i * 2 + 1] = ConeDirection(bandDeg.y, az) * slant; // outer edge (shallowest)
            }

            var t = 0;
            for (var i = 0; i < seg; i++)
            {
                int a = i * 2, b = i * 2 + 1, c = (i + 1) * 2, d = (i + 1) * 2 + 1;
                // Front faces.
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
                // Back faces (double-sided so the band is visible from inside the cone too).
                tris[t++] = b; tris[t++] = c; tris[t++] = a;
                tris[t++] = d; tris[t++] = c; tris[t++] = b;
            }

            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
        }

        // Apex->rim->apex polyline (in band-local space) so the band reads as a cone anchored at the spot.
        void BuildConeRibs(Vector2 bandDeg)
        {
            if (m_ConeRibs == null)
                return;

            var ribs = m_ConeRibCount;
            if (ribs < 1)
            {
                m_ConeRibs.positionCount = 0;
                return;
            }

            var mid = 0.5f * (bandDeg.x + bandDeg.y);
            var points = new Vector3[ribs * 2 + 1];
            var idx = 0;
            points[idx++] = Vector3.zero;
            for (var i = 0; i < ribs; i++)
            {
                var az = (i / (float)ribs) * Mathf.PI * 2f;
                points[idx++] = ConeDirection(mid, az) * m_ConeSlantMeters;
                points[idx++] = Vector3.zero;
            }

            m_ConeRibs.positionCount = points.Length;
            m_ConeRibs.SetPositions(points);
        }

        void SetBandColor(Color color)
        {
            if (m_BandMat != null)
                m_BandMat.color = color;
            if (m_ConeRibs != null)
            {
                m_ConeRibs.startColor = color;
                m_ConeRibs.endColor = color;
            }
        }

        void SetVisible(bool visible)
        {
            if (m_Root != null && m_Root.gameObject.activeSelf != visible)
                m_Root.gameObject.SetActive(visible);
        }

        void CreateVisuals()
        {
            if (m_Root != null)
                return;

            m_Root = new GameObject("Injection Target Guide").transform;
            m_Root.SetParent(transform, false);

            m_DiscMesh = BuildDiscMesh(48);
            m_ConeBandMesh = new Mesh { name = "GuideConeBand" };     // built on demand in EnsureConeBandMesh
            m_GreenChannelMesh = new Mesh { name = "GuideDepthGreen" }; // built on demand in EnsureDepthMeshes
            m_RedChannelMesh = new Mesh { name = "GuideDepthRed" };

            m_SpotMat = CreateColoredMaterial(m_SpotColor);
            m_BandMat = CreateColoredMaterial(m_BandColor);
            m_GreenMat = CreateColoredMaterial(m_GreenColor);
            m_RedMat = CreateColoredMaterial(m_RedColor);
            m_OrangeMat = CreateColoredMaterial(m_OrangeColor);

            m_Spot = CreateMeshObject("Injection Spot", m_DiscMesh, m_SpotMat);
            m_ConeBand = CreateMeshObject("Angle Band", m_ConeBandMesh, m_BandMat);
            m_GreenChannel = CreateMeshObject("Correct Depth", m_GreenChannelMesh, m_GreenMat);
            m_RedChannel = CreateMeshObject("Over Depth", m_RedChannelMesh, m_RedMat);
            m_OrangePlane = CreateMeshObject("Max Depth Plane", m_DiscMesh, m_OrangeMat);

            // Ribs live in the band's local space (apex at spot, +Y = surface normal).
            m_ConeRibs = CreateLine("Angle Band Ribs", m_BandMat, m_ConeBand, worldSpace: false);
            m_ConeRibs.startWidth = m_DepthLineWidth * 0.4f;
            m_ConeRibs.endWidth = m_DepthLineWidth * 0.4f;

            SetVisible(false);
        }

        Transform CreateMeshObject(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(m_Root, false);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            return go.transform;
        }

        LineRenderer CreateLine(string name, Material material, Transform parent, bool worldSpace)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.material = material;
            line.useWorldSpace = worldSpace;
            line.numCapVertices = 4;
            line.startWidth = m_DepthLineWidth;
            line.endWidth = m_DepthLineWidth;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off;
            return line;
        }

        // Unit disc of radius 1 in the local XZ plane (normal +Y), rendered double-sided.
        static Mesh BuildDiscMesh(int segments)
        {
            segments = Mathf.Max(8, segments);
            var vertices = new Vector3[segments + 1];
            vertices[0] = Vector3.zero;
            for (var i = 0; i < segments; ++i)
            {
                var a = (i / (float)segments) * Mathf.PI * 2f;
                vertices[i + 1] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            }

            var triangles = new int[segments * 6];
            var t = 0;
            for (var i = 0; i < segments; ++i)
            {
                var next = (i + 1) % segments;
                // Top face.
                triangles[t++] = 0;
                triangles[t++] = i + 1;
                triangles[t++] = next + 1;
                // Bottom face (reverse winding) so it shows from both sides.
                triangles[t++] = 0;
                triangles[t++] = next + 1;
                triangles[t++] = i + 1;
            }

            var mesh = new Mesh { name = "GuideDisc" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Material CreateColoredMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Sprites/Default") ??
                         Shader.Find("Unlit/Color") ??
                         Shader.Find("Standard");

            var material = new Material(shader);
            // Configure URP Unlit as transparent. The keyword + blend MUST be set together or the
            // surface renders opaque/garbage ("broken material") on URP builds.
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f); // 0 = opaque, 1 = transparent
            if (material.HasProperty("_Blend"))
                material.SetFloat("_Blend", 0f);    // alpha blend
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            material.color = color;
            if (material.HasProperty("_SrcBlend"))
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite"))
                material.SetInt("_ZWrite", 0);
            if (material.HasProperty("_Cull"))
                material.SetInt("_Cull", (int)CullMode.Off);
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        static void DestroySafe(Object obj)
        {
            if (obj != null)
                Destroy(obj);
        }
    }
}
