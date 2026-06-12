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

        [Header("Angle band (blue cones)")]
        [SerializeField, Min(0.005f)]
        float m_ConeHeight = 0.06f;

        [SerializeField, Range(8, 64)]
        int m_ConeSegments = 32;

        [Header("Depth guide")]
        [SerializeField, Min(0.0005f)]
        float m_DepthLineWidth = 0.004f;

        [SerializeField, Min(0.002f)]
        float m_OrangeDiscRadius = 0.02f;

        [Header("Colors")]
        [SerializeField]
        Color m_SpotColor = new Color(1f, 1f, 1f, 0.6f);

        [SerializeField]
        Color m_BandColor = new Color(0.2f, 0.5f, 1f, 0.35f);

        [SerializeField]
        Color m_GreenColor = new Color(0.15f, 1f, 0.25f, 0.5f);

        [SerializeField]
        Color m_RedColor = new Color(1f, 0.2f, 0.15f, 0.6f);

        [SerializeField]
        Color m_OrangeColor = new Color(1f, 0.55f, 0.1f, 0.4f);

        Transform m_Root;
        Transform m_Spot;
        Transform m_OuterCone;
        Transform m_InnerCone;
        Transform m_OrangePlane;
        LineRenderer m_GreenLine;
        LineRenderer m_RedLine;

        Mesh m_DiscMesh;
        Mesh m_ConeMesh;
        Material m_SpotMat;
        Material m_BandMat;
        Material m_GreenMat;
        Material m_RedMat;
        Material m_OrangeMat;

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
            DestroySafe(m_ConeMesh);
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
            if (m_SurfaceSelectionTool == null || m_Tutorial == null)
                return false;

            var step = m_Tutorial.currentStep;
            if (step != SyringeCalibrationButtonBridge.TutorialStep.InjectionAngle &&
                step != SyringeCalibrationButtonBridge.TutorialStep.InsertionSpeedFlowRate)
                return false;

            if (!m_SurfaceSelectionTool.TryGetPlacedSurface(out var surfacePose, out _))
                return false;

            var spot = surfacePose.position;
            var normal = surfacePose.up;
            normal = normal.sqrMagnitude < 0.000001f ? Vector3.up : normal.normalized;

            // Declared + defaulted before the short-circuit so it is definitely assigned even when
            // m_Tracker is null (TryGetSyringePose is then never called).
            SyringeOverlayTracker.SyringePoseData syringe = default;
            var hasPose = m_Tracker != null && m_Tracker.TryGetSyringePose(out syringe);
            var syringeForward = hasPose ? syringe.forward : Vector3.zero;

            // Spot disc shrinks as the needle nears the surface.
            var needleDistance = hasPose
                ? Mathf.Abs(Vector3.Dot(syringe.needleTip - spot, normal))
                : m_SpotShrinkRangeMeters;
            var diameter = Mathf.Lerp(m_SpotMinDiameter, m_SpotMaxDiameter,
                Mathf.Clamp01(needleDistance / Mathf.Max(0.001f, m_SpotShrinkRangeMeters)));
            diameter = Mathf.Max(diameter, m_SpotMinDiameter);
            PlaceDisc(m_Spot, spot + normal * 0.001f, normal, diameter * 0.5f);

            // Blue band: outer cone = shallowest valid angle, inner cone = steepest valid angle.
            var range = m_Tutorial.targetInjectionAngleRange;
            var outerHalf = Mathf.Clamp(90f - range.x, 0.5f, 89f);
            var innerHalf = Mathf.Clamp(90f - range.y, 0f, 89f);
            PlaceCone(m_OuterCone, spot, normal, outerHalf, m_ConeHeight);
            var showInner = innerHalf > 0.75f;
            m_InnerCone.gameObject.SetActive(showInner);
            if (showInner)
                PlaceCone(m_InnerCone, spot, normal, innerHalf, m_ConeHeight);

            // Depth zones run along the ideal needle path (nominal type angle, live azimuth).
            var ideal = ComputeIdealInjectionAxis(normal, syringeForward);
            var greenMeters = m_Tutorial.currentInjectionGreenDepthCm * 0.01f;
            var maxMeters = m_Tutorial.currentInjectionMaxDepthCm * 0.01f;
            var greenEnd = spot + ideal * greenMeters;
            var maxEnd = spot + ideal * maxMeters;

            SetLine(m_GreenLine, spot, greenEnd, m_GreenColor);
            SetLine(m_RedLine, greenEnd, maxEnd, m_RedColor);
            PlaceDisc(m_OrangePlane, maxEnd, ideal, m_OrangeDiscRadius);

            SetVisible(true);
            return true;
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

        void PlaceCone(Transform cone, Vector3 apex, Vector3 axisUp, float halfAngleDeg, float height)
        {
            cone.position = apex;
            cone.rotation = Quaternion.FromToRotation(Vector3.up, axisUp.normalized);
            var radius = height * Mathf.Tan(halfAngleDeg * Mathf.Deg2Rad);
            cone.localScale = new Vector3(radius, height, radius);
        }

        void SetLine(LineRenderer line, Vector3 a, Vector3 b, Color color)
        {
            line.positionCount = 2;
            line.SetPosition(0, a);
            line.SetPosition(1, b);
            line.startWidth = m_DepthLineWidth;
            line.endWidth = m_DepthLineWidth;
            line.startColor = color;
            line.endColor = color;
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
            m_ConeMesh = BuildConeMesh(Mathf.Max(8, m_ConeSegments));

            m_SpotMat = CreateColoredMaterial(m_SpotColor);
            m_BandMat = CreateColoredMaterial(m_BandColor);
            m_GreenMat = CreateColoredMaterial(m_GreenColor);
            m_RedMat = CreateColoredMaterial(m_RedColor);
            m_OrangeMat = CreateColoredMaterial(m_OrangeColor);

            m_Spot = CreateMeshObject("Injection Spot", m_DiscMesh, m_SpotMat);
            m_OuterCone = CreateMeshObject("Angle Band Outer", m_ConeMesh, m_BandMat);
            m_InnerCone = CreateMeshObject("Angle Band Inner", m_ConeMesh, m_BandMat);
            m_OrangePlane = CreateMeshObject("Max Depth Plane", m_DiscMesh, m_OrangeMat);

            m_GreenLine = CreateLine("Correct Depth", m_GreenMat);
            m_RedLine = CreateLine("Over Depth", m_RedMat);

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

        LineRenderer CreateLine(string name, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(m_Root, false);
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.material = material;
            line.useWorldSpace = true;
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

        // Unit cone: apex at origin, ring at local +Y = 1 with radius 1, lateral surface, double-sided.
        static Mesh BuildConeMesh(int segments)
        {
            segments = Mathf.Max(8, segments);
            var vertices = new Vector3[segments + 1];
            vertices[0] = Vector3.zero; // apex
            for (var i = 0; i < segments; ++i)
            {
                var a = (i / (float)segments) * Mathf.PI * 2f;
                vertices[i + 1] = new Vector3(Mathf.Cos(a), 1f, Mathf.Sin(a));
            }

            var triangles = new int[segments * 6];
            var t = 0;
            for (var i = 0; i < segments; ++i)
            {
                var ring = i + 1;
                var ringNext = (i + 1) % segments + 1;
                triangles[t++] = 0;
                triangles[t++] = ring;
                triangles[t++] = ringNext;
                // Reverse face.
                triangles[t++] = 0;
                triangles[t++] = ringNext;
                triangles[t++] = ring;
            }

            var mesh = new Mesh { name = "GuideCone" };
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
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
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
