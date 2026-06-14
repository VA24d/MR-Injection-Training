using System.Collections;
using System;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using MovementType = UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable.MovementType;
using LazyFollow = UnityEngine.XR.Interaction.Toolkit.UI.LazyFollow;

namespace UnityEngine.XR.Templates.MR
{
    /// <summary>Which vertical edge hosts the thin ray-grab strip.</summary>
    public enum GrabStripVerticalAnchor
    {
        /// <summary>Bottom strip, matching the demo <c>Spatial Panel Manipulator</c> grab volume (below panel center).</summary>
        Bottom,
        /// <summary>Top strip — can reduce overlap with bottom navigation buttons when using far-ray grab.</summary>
        Top
    }

    /// <summary>
    /// Lets the player grab the floating coaching UI with the XR ray, move it, and leave it pinned in world space.
    /// Disables <see cref="LazyFollow"/> while grabbed; after release the panel stays pinned until <see cref="ResumeFollowingHead"/>.
    /// Ray grab uses a thin collider along the bottom or top canvas edge (default: bottom, like the blue action bar / demo panel handle).
    /// Grab tuning matches the scene <c>Spatial Panel Manipulator</c> used for the demo video (velocity tracking + smoothing).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class FloatingCoachingUIGrab : MonoBehaviour
    {
        const string GrabStripObjectName = "RayGrabStrip";
        const string GrabHandleVisualName = "Reposition Handle Visual";
        const string DemoHandleSourceObjectName = "Spatial Panel Manipulator";

        [SerializeField]
        [Tooltip("If null, uses LazyFollow on this GameObject.")]
        LazyFollow m_LazyFollow;

        [SerializeField]
        [Tooltip("When true, releasing the grab keeps the panel pinned (no follow). When false, follow resumes after release.")]
        bool m_PinInWorldOnRelease = true;

        [SerializeField]
        [Tooltip("When true, RB is kinematic while not grabbed (less drift). When true, far-ray VelocityTracking grab often fails — prefer off to match Spatial Panel Manipulator.")]
        bool m_LockRigidbodyWhenNotGrabbed = false;

        [SerializeField]
        [Tooltip("If true, rotation does not track the controller while grabbing (translation only).")]
        bool m_PositionOnlyWhileGrabbed = true;

        [SerializeField]
        [Tooltip("Which canvas edge gets the ray-grab strip (bottom matches the demo spatial panel handle / coaching action bar).")]
        GrabStripVerticalAnchor m_GrabStripAnchor = GrabStripVerticalAnchor.Bottom;

        [SerializeField]
        [Tooltip("Fraction of canvas height used for the ray-grab strip along the chosen edge.")]
        [Range(0.04f, 0.2f)]
        float m_GrabStripHeightFraction = 0.09f;

        [SerializeField]
        [Tooltip("Offset in meters that places the grab strip outside the panel edge (below for Bottom, above for Top).")]
        [Min(0f)]
        float m_GrabStripOutsideOffset = 0.012f;

        [SerializeField]
        [Tooltip("Extra depth (meters) added to the grab collider along local Z (toward the user).")]
        float m_ColliderDepth = 0.14f;

        [SerializeField]
        [Tooltip("Re-fit the grab strip for this many frames so layout scale is correct.")]
        int m_RefitFramesAfterStart = 4;

        [SerializeField]
        [Tooltip("Shows a visible blue drag bar on the chosen edge, matching the demo video panel style.")]
        bool m_ShowHandleVisual = true;

        [SerializeField]
        [Tooltip("Color for the visible drag bar.")]
        Color m_HandleVisualColor = new(0.1254902f, 0.5882353f, 0.9529412f, 1f);


        XRGrabInteractable m_Grab;
        Rigidbody m_Rigidbody;
        BoxCollider m_Box;
        Transform m_GrabStripTransform;
        Transform m_GrabHandleVisualModel;
        SkinnedMeshRenderer m_GrabHandleVisualRenderer;
        Mesh m_DemoHandleMesh;
        Material m_DemoHandleMaterial;
        Vector3 m_DemoHandleScale = new(0.2f, 0.2f, 0.2f);
        float m_DemoHandleBlendShape0 = 5f;

        LazyFollow.PositionFollowMode m_StoredPositionMode = LazyFollow.PositionFollowMode.Follow;
        LazyFollow.RotationFollowMode m_StoredRotationMode = LazyFollow.RotationFollowMode.LookAtWithWorldUp;

        bool m_UserPinned;
        int m_RefitFramesRemaining;

        /// <summary>
        /// When false, <see cref="GoalManager"/> must not turn LazyFollow back on.
        /// Auto-follow is blocked while pinned by user and while actively grabbed.
        /// </summary>
        public bool AllowGoalManagerAutoFollow => !m_UserPinned && (m_Grab == null || !m_Grab.isSelected);

        /// <summary>True while the panel is being actively grabbed/moved by the user.</summary>
        public bool isGrabbed => m_Grab != null && m_Grab.isSelected;

        /// <summary>
        /// Call after the coaching card or canvas layout changes so the ray-grab strip matches the new bounds.
        /// </summary>
        public void RefitGrabCollider()
        {
            Canvas.ForceUpdateCanvases();
            FitColliderToCanvasEdgeStrip();
            m_RefitFramesRemaining = Mathf.Max(m_RefitFramesRemaining, 2);
        }

        void Awake()
        {
            if (m_LazyFollow == null)
                m_LazyFollow = GetComponent<LazyFollow>();

            m_Rigidbody = GetComponent<Rigidbody>();
            // Keep physics from drifting the panel when idle; switch to dynamic only while grabbed.
            m_Rigidbody.isKinematic = m_LockRigidbodyWhenNotGrabbed;
            m_Rigidbody.useGravity = false;
            m_Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            m_Rigidbody.constraints = (RigidbodyConstraints)112;

            // Do not keep a full-panel collider on the root — it steals pokes from UI.
            var rootBoxes = GetComponents<BoxCollider>();
            for (var i = 0; i < rootBoxes.Length; i++)
                Destroy(rootBoxes[i]);

            m_Grab = GetComponent<XRGrabInteractable>();
            if (m_Grab == null)
                m_Grab = gameObject.AddComponent<XRGrabInteractable>();

            var stripGo = new GameObject(GrabStripObjectName);
            m_GrabStripTransform = stripGo.transform;
            m_GrabStripTransform.SetParent(transform, false);
            m_Box = stripGo.AddComponent<BoxCollider>();

            ConfigureGrabInteractable();
            m_Grab.colliders.Clear();
            m_Grab.colliders.Add(m_Box);

            m_Grab.selectEntered.AddListener(OnSelectEntered);
            m_Grab.selectExited.AddListener(OnSelectExited);

            m_RefitFramesRemaining = m_RefitFramesAfterStart;
        }

        void Start()
        {
            StartCoroutine(FitColliderEndOfFrame());
        }

        void LateUpdate()
        {
            if (m_RefitFramesRemaining <= 0)
                return;

            m_RefitFramesRemaining--;
            Canvas.ForceUpdateCanvases();
            FitColliderToCanvasEdgeStrip();
        }

        IEnumerator FitColliderEndOfFrame()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            FitColliderToCanvasEdgeStrip();
        }

        void OnDestroy()
        {
            if (m_Grab != null)
            {
                m_Grab.selectEntered.RemoveListener(OnSelectEntered);
                m_Grab.selectExited.RemoveListener(OnSelectExited);
            }
        }

        void ConfigureGrabInteractable()
        {
            // Match the working Spatial Panel Manipulator interaction mask.
            m_Grab.interactionLayers = new InteractionLayerMask { value = 1 };
            m_Grab.useDynamicAttach = true;
            m_Grab.matchAttachPosition = true;
            m_Grab.matchAttachRotation = true;
            m_Grab.snapToColliderVolume = true;
            m_Grab.reinitializeDynamicAttachEverySingleGrab = true;
            m_Grab.attachEaseInTime = 0f;
            m_Grab.trackPosition = true;
            m_Grab.trackRotation = !m_PositionOnlyWhileGrabbed;
            // Match Spatial Panel Manipulator (runtime reports Instantaneous for this object).
            m_Grab.movementType = MovementType.Instantaneous;
            m_Grab.throwOnDetach = false;
            m_Grab.smoothPosition = true;
            m_Grab.smoothPositionAmount = 5f;
            m_Grab.tightenPosition = 0.068f;
            m_Grab.smoothRotation = true;
            m_Grab.smoothRotationAmount = 5f;
            m_Grab.tightenRotation = 0.077f;
        }

        Canvas FindCoachingCardCanvas()
        {
            var t = transform.Find("CoachingCardRoot");
            if (t != null && t.TryGetComponent<Canvas>(out var c))
                return c;

            var canvases = GetComponentsInChildren<Canvas>(true);
            return canvases.Length > 0 ? canvases[0] : null;
        }

        void FitColliderToCanvasEdgeStrip()
        {
            if (m_Box == null)
                return;

            var canvas = FindCoachingCardCanvas();
            if (canvas == null)
            {
                var yBase = m_GrabStripAnchor == GrabStripVerticalAnchor.Top ? 0.12f : -0.12f;
                var y = m_GrabStripAnchor == GrabStripVerticalAnchor.Top
                    ? yBase + m_GrabStripOutsideOffset
                    : yBase - m_GrabStripOutsideOffset;
                m_GrabStripTransform.localPosition = new Vector3(0f, y, 0f);
                m_Box.center = Vector3.zero;
                m_Box.size = new Vector3(0.35f, 0.05f, m_ColliderDepth);
                return;
            }

            var rt = canvas.GetComponent<RectTransform>();
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);

            var up = (corners[1] - corners[0]).normalized;
            var right = (corners[2] - corners[1]).normalized;
            var fullWidth = (corners[2] - corners[1]).magnitude;
            var fullHeight = (corners[1] - corners[0]).magnitude;
            var stripH = Mathf.Max(fullHeight * m_GrabStripHeightFraction, 0.015f);
            var outsideOffsetWorld = Mathf.Max(m_GrabStripOutsideOffset, 0f);

            Vector3 stripCenterWorld;
            if (m_GrabStripAnchor == GrabStripVerticalAnchor.Top)
            {
                var topMid = (corners[1] + corners[2]) * 0.5f;
                stripCenterWorld = topMid + up * (outsideOffsetWorld + stripH * 0.5f);
            }
            else
            {
                var bottomMid = (corners[0] + corners[3]) * 0.5f;
                stripCenterWorld = bottomMid - up * (outsideOffsetWorld + stripH * 0.5f);
            }

            // Keep the strip aligned to the canvas in the root's local space (handles scale/rotation).
            m_GrabStripTransform.localPosition = transform.InverseTransformPoint(stripCenterWorld);
            m_GrabStripTransform.localRotation = Quaternion.Inverse(transform.rotation) * rt.rotation;

            // Collider size is in the strip's local space; account for root/canvas scale.
            var wLocal = m_GrabStripTransform.InverseTransformVector(right * fullWidth).magnitude;
            var hLocal = m_GrabStripTransform.InverseTransformVector(up * stripH).magnitude;
            var zDepth = Mathf.Max(m_ColliderDepth, 0.04f);

            // We do not want the grab strip to significantly overlap the UI canvas, 
            // otherwise the XRGrabInteractable steals raycasts from UI buttons!
            // If we enforce a minimum height, we must appropriately center the box down further.
            float finalH = Mathf.Max(hLocal, 0.06f); // 6cm minimum height instead of 10cm
            
            // Re-adjust local center so the top edge of this box doesn't bleed into the canvas
            // If finalH > hLocal, we push the center down by half the difference.
            float yOffset = 0f;
            if (finalH > hLocal) {
                yOffset = (m_GrabStripAnchor == GrabStripVerticalAnchor.Top) ? (finalH - hLocal) * 0.5f : -(finalH - hLocal) * 0.5f;
            }

            m_Box.center = new Vector3(0f, yOffset, 0f);
            m_Box.size = new Vector3(
                Mathf.Max(wLocal, 0.1f),
                finalH,
                zDepth);
            m_Box.isTrigger = false;

            // Ensure the grab strip is on the Default layer (0) so XR Ray Interactors hit it reliably,
            // even if the Coaching UI root is on the UI layer (5) which rays might ignore.
            m_GrabStripTransform.gameObject.layer = 0;
            UpdateGrabHandleVisual();
        }

        void UpdateGrabHandleVisual()
        {
            if (!m_ShowHandleVisual)
            {
                if (m_GrabHandleVisualModel != null)
                    m_GrabHandleVisualModel.gameObject.SetActive(false);
                return;
            }

            if (!EnsureGrabHandleVisualModel())
            {
                // If demo assets are unavailable, hide the handle instead of drawing a mismatched flat bar.
                if (m_GrabHandleVisualModel != null)
                    m_GrabHandleVisualModel.gameObject.SetActive(false);
                return;
            }

            if (m_GrabHandleVisualModel == null || m_GrabHandleVisualRenderer == null || m_Box == null)
                return;

            m_GrabHandleVisualModel.gameObject.SetActive(true);
            m_GrabHandleVisualModel.localPosition = Vector3.zero;
            m_GrabHandleVisualModel.localRotation = Quaternion.identity;
            m_GrabHandleVisualModel.gameObject.layer = gameObject.layer;

            m_GrabHandleVisualModel.localScale = m_DemoHandleScale;
            if (m_GrabHandleVisualRenderer.sharedMesh != null && m_GrabHandleVisualRenderer.sharedMesh.blendShapeCount > 0)
                m_GrabHandleVisualRenderer.SetBlendShapeWeight(0, m_DemoHandleBlendShape0);
        }

        bool EnsureGrabHandleVisualModel()
        {
            if (m_GrabStripTransform == null)
                return false;

            if (m_GrabHandleVisualRenderer != null && m_DemoHandleMesh != null && m_DemoHandleMaterial != null)
                return true;

            if (m_DemoHandleMesh == null || m_DemoHandleMaterial == null)
            {
                if (!TryResolveDemoHandleAssets())
                    return false;
            }

            if (m_GrabHandleVisualModel == null)
            {
                var existing = m_GrabStripTransform.Find(GrabHandleVisualName);
                if (existing != null)
                    m_GrabHandleVisualModel = existing;
            }

            if (m_GrabHandleVisualModel == null)
            {
                var go = new GameObject(GrabHandleVisualName);
                m_GrabHandleVisualModel = go.transform;
                m_GrabHandleVisualModel.SetParent(m_GrabStripTransform, false);
            }

            if (m_GrabHandleVisualRenderer == null && m_GrabHandleVisualModel != null)
                m_GrabHandleVisualRenderer = m_GrabHandleVisualModel.GetComponent<SkinnedMeshRenderer>();

            if (m_GrabHandleVisualRenderer == null && m_GrabHandleVisualModel != null)
                m_GrabHandleVisualRenderer = m_GrabHandleVisualModel.gameObject.AddComponent<SkinnedMeshRenderer>();

            if (m_GrabHandleVisualRenderer == null)
                return false;

            m_GrabHandleVisualRenderer.sharedMesh = m_DemoHandleMesh;
            m_GrabHandleVisualRenderer.sharedMaterial = m_DemoHandleMaterial;
            m_GrabHandleVisualRenderer.updateWhenOffscreen = true;
            return m_GrabHandleVisualRenderer.sharedMesh != null && m_GrabHandleVisualRenderer.sharedMaterial != null;
        }

        bool TryResolveDemoHandleAssets()
        {
            if (m_DemoHandleMesh != null && m_DemoHandleMaterial != null)
                return true;

            var renderers = Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>();
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || renderer.gameObject == null)
                    continue;

                if (!renderer.gameObject.scene.IsValid())
                    continue;

                if (!string.Equals(renderer.gameObject.name, DemoHandleSourceObjectName, StringComparison.Ordinal))
                    continue;

                if (renderer.sharedMesh == null || renderer.sharedMaterial == null)
                    continue;

                m_DemoHandleMesh = renderer.sharedMesh;
                m_DemoHandleMaterial = renderer.sharedMaterial;
                m_DemoHandleScale = renderer.transform.lossyScale;
                if (renderer.sharedMesh != null && renderer.sharedMesh.blendShapeCount > 0)
                    m_DemoHandleBlendShape0 = renderer.GetBlendShapeWeight(0);
                return true;
            }

            return false;
        }

        void OnSelectEntered(SelectEnterEventArgs _)
        {
            if (m_LockRigidbodyWhenNotGrabbed)
                m_Rigidbody.isKinematic = false;

            m_UserPinned = false;
            if (!m_Rigidbody.isKinematic)
            {
                m_Rigidbody.linearVelocity = Vector3.zero;
                m_Rigidbody.angularVelocity = Vector3.zero;
            }

            if (m_LazyFollow == null)
                return;

            m_StoredPositionMode = m_LazyFollow.positionFollowMode;
            m_StoredRotationMode = m_LazyFollow.rotationFollowMode;

            m_LazyFollow.positionFollowMode = LazyFollow.PositionFollowMode.None;
            m_LazyFollow.rotationFollowMode = LazyFollow.RotationFollowMode.None;
        }

        void OnSelectExited(SelectExitEventArgs _)
        {
            // Kill release momentum so the panel does not drift away after drag.
            if (!m_Rigidbody.isKinematic)
            {
                m_Rigidbody.linearVelocity = Vector3.zero;
                m_Rigidbody.angularVelocity = Vector3.zero;
            }

            if (m_LockRigidbodyWhenNotGrabbed)
                m_Rigidbody.isKinematic = true;

            if (m_LazyFollow == null)
                return;

            if (m_PinInWorldOnRelease)
            {
                m_UserPinned = true;
                m_LazyFollow.positionFollowMode = LazyFollow.PositionFollowMode.None;
                m_LazyFollow.rotationFollowMode = LazyFollow.RotationFollowMode.None;
            }
            else
            {
                m_LazyFollow.positionFollowMode = m_StoredPositionMode;
                m_LazyFollow.rotationFollowMode = m_StoredRotationMode;
            }
        }

        /// <summary>
        /// Call from UI (e.g. a \"Follow view\" button) or tutorial reset to attach the panel to the head/camera follow again.
        /// </summary>
        public void ResumeFollowingHead()
        {
            m_UserPinned = false;

            if (m_Rigidbody != null)
            {
                if (!m_Rigidbody.isKinematic)
                {
                    m_Rigidbody.linearVelocity = Vector3.zero;
                    m_Rigidbody.angularVelocity = Vector3.zero;
                }
                if (m_LockRigidbodyWhenNotGrabbed)
                    m_Rigidbody.isKinematic = true;
            }

            if (m_LazyFollow == null)
                return;

            m_LazyFollow.positionFollowMode = LazyFollow.PositionFollowMode.Follow;
            m_LazyFollow.rotationFollowMode = LazyFollow.RotationFollowMode.LookAtWithWorldUp;
        }

        /// <summary>
        /// Stops head/camera follow so the panel stays in world space (same effect as releasing a grab with pin).
        /// Call from <see cref="GoalManager"/> or a UI \"Dock panel\" control.
        /// </summary>
        public void PinToWorldSpace()
        {
            m_UserPinned = true;

            if (m_Rigidbody != null)
            {
                if (!m_Rigidbody.isKinematic)
                {
                    m_Rigidbody.linearVelocity = Vector3.zero;
                    m_Rigidbody.angularVelocity = Vector3.zero;
                }
                if (m_LockRigidbodyWhenNotGrabbed)
                    m_Rigidbody.isKinematic = true;
            }

            if (m_LazyFollow == null)
                return;

            m_LazyFollow.positionFollowMode = LazyFollow.PositionFollowMode.None;
            m_LazyFollow.rotationFollowMode = LazyFollow.RotationFollowMode.None;
        }
    }
}
