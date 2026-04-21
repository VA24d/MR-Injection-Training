using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace UnityEngine.XR.Templates.MR
{
    /// <summary>
    /// Fixes two XRI issues on startup:
    ///
    /// 1. XRI 3.3.x init-order bug: TrackedDeviceGraphicRaycaster.OnDisable and
    ///    LazyFollow.OnEnable fire before their own Awake, throwing KeyNotFoundException
    ///    and NullReferenceException. Fixed by deactivating CoachingUI in our early Awake
    ///    so the XRI components' Awake() runs first (deferred to when SetActive(true) is called
    ///    in Start).
    ///
    /// 2. XRGrabInteractable collider registration timing bug: FloatingCoachingUIGrab.Awake()
    ///    calls AddComponent&lt;XRGrabInteractable&gt;() which immediately fires Awake+OnEnable on
    ///    the new interactable — registering it with XRInteractionManager with an EMPTY collider
    ///    list. The RayGrabStrip BoxCollider is created AFTER this, so it is never added to
    ///    XRInteractionManager's collider-to-interactable lookup map. Result: ray interactors
    ///    physically hit the grab strip collider but XRI cannot resolve it to the interactable.
    ///    Fixed by forcing re-registration on the next frame after Awake completes.
    ///
    /// 3. Spawn position: On first enable we briefly run LazyFollow in Follow mode so
    ///    SnapOnEnable places the panel in the user's viewport, then immediately pin it
    ///    world-anchored so it stays put while the user looks around.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public class XRStartupFixer : MonoBehaviour
    {
        [Tooltip("The Coaching UI root GameObject (contains LazyFollow + FloatingCoachingUIGrab).")]
        [SerializeField]
        GameObject m_CoachingUI;

        bool m_WasActive;

        // ─── Phase 1: Before any XRI components run their Awake ──────────────
        void Awake()
        {
            if (m_CoachingUI == null)
            {
                var found = GameObject.Find("Coaching UI");
                if (found != null) m_CoachingUI = found;
            }

            if (m_CoachingUI == null) return;

            m_WasActive = m_CoachingUI.activeSelf;
            if (m_WasActive)
            {
                // Deactivate BEFORE CoachingUI's children run Awake.
                // This defers FloatingCoachingUIGrab.Awake(), LazyFollow.Awake(),
                // TrackedDeviceGraphicRaycaster.Awake() until SetActive(true) in Start.
                m_CoachingUI.SetActive(false);
            }
        }

        // ─── Phase 2: After all scene Awake() calls have finished ────────────
        void Start()
        {
            if (m_CoachingUI == null || !m_WasActive) return;

            // Activate so components initialize 
            m_CoachingUI.SetActive(true);

            // Phase 3: Setup explicit spawn and fix registration
            StartCoroutine(InitSpawnAndRegistration());
        }

        IEnumerator InitSpawnAndRegistration()
        {
            // Wait for camera to update its world position from the XR headset
            yield return null;
            yield return null;

            if (m_CoachingUI == null) yield break;

            var cam = Camera.main;
            if (cam != null)
            {
                // Match GoalManager default: slightly below eye line, comfortable distance in front of the camera.
                var spawnPos = cam.transform.TransformPoint(new Vector3(0f, -0.12f, 0.85f));
                m_CoachingUI.transform.position = spawnPos;

                var toCamera = cam.transform.position - m_CoachingUI.transform.position;
                var yawFacing = Vector3.ProjectOnPlane(toCamera, Vector3.up);
                if (yawFacing.sqrMagnitude > 0.000001f)
                {
                    m_CoachingUI.transform.rotation = Quaternion.LookRotation(yawFacing.normalized, Vector3.up) *
                        Quaternion.Euler(0f, 180f, 0f);
                }
            }

            var lf = m_CoachingUI.GetComponent<LazyFollow>();
            if (lf != null)
            {
                // Ensure it stays pinned in world space (doesn't follow head)
                lf.positionFollowMode = LazyFollow.PositionFollowMode.None;
                lf.rotationFollowMode = LazyFollow.RotationFollowMode.None;
            }

            StartCoroutine(FixGrabColliderRegistration());
        }

        // ─── Phase 4: Re-register XRGrabInteractable with the correct collider ─
        IEnumerator FixGrabColliderRegistration()
        {
            // Wait for FitColliderEndOfFrame
            // Frame +1: FloatingCoachingUIGrab.Start() → FitColliderEndOfFrame() runs,
            //           sizing the BoxCollider correctly from the canvas layout.
            // Frame +2: ContentSizeFitter layout settles (LateUpdate re-fits also run).
            yield return null;
            yield return null;

            if (m_CoachingUI == null) yield break;

            var grab = m_CoachingUI.GetComponent<XRGrabInteractable>();
            if (grab == null)
            {
                Debug.LogWarning("[XRStartupFixer] XRGrabInteractable not found on Coaching UI after two frames.");
                yield break;
            }

            var interactionManager = grab.interactionManager;
            if (interactionManager == null)
            {
                Debug.LogWarning("[XRStartupFixer] XRGrabInteractable has no interactionManager assigned.");
                yield break;
            }

            // Force XRI to re-register the interactable so the RayGrabStrip BoxCollider
            // (created after AddComponent registered the interactable with empty colliders)
            // gets properly added to m_ColliderToInteractableMap.
            interactionManager.UnregisterInteractable(grab as IXRInteractable);
            interactionManager.RegisterInteractable(grab as IXRInteractable);

            // Also ensure the Rigidbody is kinematic if not grabbed 
            // so gravity/forces don't drop the panel randomly
            var fcuGrab = m_CoachingUI.GetComponent<FloatingCoachingUIGrab>();
            var rb = m_CoachingUI.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            Debug.Log($"[XRStartupFixer] Re-registered XRGrabInteractable on '{m_CoachingUI.name}'. " +
                      $"Registered collider count: {grab.colliders.Count}");
        }
    }
}
