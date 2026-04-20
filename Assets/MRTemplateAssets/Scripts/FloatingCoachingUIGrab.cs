using System.Collections;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.UI;
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
        [Range(0.05f, 0.35f)]
        float m_GrabStripHeightFraction = 0.18f;

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

        [SerializeField]
        [Min(8f)]
        [Tooltip("Maximum visual height for the blue drag bar in canvas local units.")]
        float m_HandleVisualMaxHeight = 32f;

        XRGrabInteractable m_Grab;
        Rigidbody m_Rigidbody;
        BoxCollider m_Box;
        Transform m_GrabStripTransform;
        RectTransform m_GrabHandleVisual;
        Image m_GrabHandleVisualImage;

        LazyFollow.PositionFollowMode m_StoredPositionMode = LazyFollow.PositionFollowMode.Follow;
        LazyFollow.RotationFollowMode m_StoredRotationMode = LazyFollow.RotationFollowMode.LookAtWithWorldUp;

        bool m_UserPinned;
        int m_RefitFramesRemaining;

        /// <summary>
        /// When false, <see cref="GoalManager"/> must not turn LazyFollow back on.
        /// Auto-follow is blocked while pinned by user and while actively grabbed.
        /// </summary>
        public bool AllowGoalManagerAutoFollow => !m_UserPinned && (m_Grab == null || !m_Grab.isSelected);

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
            // All interaction layers — project/Quest presets often use non-default layers; mask "1" breaks far-ray grab.
            m_Grab.interactionLayers = new InteractionLayerMask { value = -1 };
            m_Grab.trackPosition = true;
            m_Grab.trackRotation = !m_PositionOnlyWhileGrabbed;
            // Same movement mode as Spatial Panel Manipulator on the demo video (m_MovementType: VelocityTracking).
            m_Grab.movementType = MovementType.VelocityTracking;
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
                var y = m_GrabStripAnchor == GrabStripVerticalAnchor.Top ? 0.12f : -0.12f;
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
            var stripHCanvasLocal = Mathf.Max(rt.rect.height * m_GrabStripHeightFraction, 16f);

            Vector3 stripCenterWorld;
            if (m_GrabStripAnchor == GrabStripVerticalAnchor.Top)
            {
                var topMid = (corners[1] + corners[2]) * 0.5f;
                stripCenterWorld = topMid - up * (stripH * 0.5f);
            }
            else
            {
                var bottomMid = (corners[0] + corners[3]) * 0.5f;
                stripCenterWorld = bottomMid + up * (stripH * 0.5f);
            }

            // Keep the strip aligned to the canvas in the root's local space (handles scale/rotation).
            m_GrabStripTransform.localPosition = transform.InverseTransformPoint(stripCenterWorld);
            m_GrabStripTransform.localRotation = Quaternion.Inverse(transform.rotation) * rt.rotation;

            // Collider size is in the strip's local space; account for root/canvas scale.
            var wLocal = m_GrabStripTransform.InverseTransformVector(right * fullWidth).magnitude;
            var hLocal = m_GrabStripTransform.InverseTransformVector(up * stripH).magnitude;
            var zDepth = Mathf.Max(m_ColliderDepth, 0.02f);

            m_Box.center = Vector3.zero;
            m_Box.size = new Vector3(
                Mathf.Max(wLocal, 0.02f),
                Mathf.Max(hLocal, 0.015f),
                zDepth);
            m_Box.isTrigger = false;

            m_GrabStripTransform.gameObject.layer = gameObject.layer;

            var visualHeight = Mathf.Min(stripHCanvasLocal, m_HandleVisualMaxHeight);
            UpdateGrabHandleVisual(rt, visualHeight);
        }

        void UpdateGrabHandleVisual(RectTransform canvasRect, float stripHeightCanvasLocal)
        {
            if (!m_ShowHandleVisual)
            {
                if (m_GrabHandleVisual != null)
                    m_GrabHandleVisual.gameObject.SetActive(false);
                return;
            }

            if (m_GrabHandleVisual == null || m_GrabHandleVisualImage == null)
            {
                var existing = canvasRect.Find(GrabHandleVisualName);
                if (existing != null)
                {
                    m_GrabHandleVisual = existing as RectTransform;
                    if (m_GrabHandleVisual != null)
                        m_GrabHandleVisualImage = m_GrabHandleVisual.GetComponent<Image>();
                }

                if (m_GrabHandleVisual == null)
                {
                    var go = new GameObject(GrabHandleVisualName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    m_GrabHandleVisual = go.GetComponent<RectTransform>();
                    m_GrabHandleVisual.SetParent(canvasRect, false);
                    m_GrabHandleVisualImage = go.GetComponent<Image>();
                }
            }

            if (m_GrabHandleVisual == null || m_GrabHandleVisualImage == null)
                return;

            m_GrabHandleVisual.gameObject.SetActive(true);
            m_GrabHandleVisualImage.raycastTarget = false;
            m_GrabHandleVisualImage.color = m_HandleVisualColor;

            if (m_GrabStripAnchor == GrabStripVerticalAnchor.Top)
            {
                m_GrabHandleVisual.anchorMin = new Vector2(0f, 1f);
                m_GrabHandleVisual.anchorMax = new Vector2(1f, 1f);
                m_GrabHandleVisual.pivot = new Vector2(0.5f, 1f);
                m_GrabHandleVisual.anchoredPosition = Vector2.zero;
            }
            else
            {
                m_GrabHandleVisual.anchorMin = new Vector2(0f, 0f);
                m_GrabHandleVisual.anchorMax = new Vector2(1f, 0f);
                m_GrabHandleVisual.pivot = new Vector2(0.5f, 0f);
                m_GrabHandleVisual.anchoredPosition = Vector2.zero;
            }

            m_GrabHandleVisual.sizeDelta = new Vector2(0f, stripHeightCanvasLocal);
            m_GrabHandleVisual.SetAsLastSibling();
        }

        void OnSelectEntered(SelectEnterEventArgs _)
        {
            if (m_LockRigidbodyWhenNotGrabbed)
                m_Rigidbody.isKinematic = false;

            m_UserPinned = false;
            m_Rigidbody.linearVelocity = Vector3.zero;
            m_Rigidbody.angularVelocity = Vector3.zero;

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
            m_Rigidbody.linearVelocity = Vector3.zero;
            m_Rigidbody.angularVelocity = Vector3.zero;

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
                m_Rigidbody.linearVelocity = Vector3.zero;
                m_Rigidbody.angularVelocity = Vector3.zero;
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
                m_Rigidbody.linearVelocity = Vector3.zero;
                m_Rigidbody.angularVelocity = Vector3.zero;
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
