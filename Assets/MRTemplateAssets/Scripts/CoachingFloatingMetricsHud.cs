using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;

namespace UnityEngine.XR.Templates.MR
{
    /// <summary>
    /// Runtime metrics strip between the coaching modal and the bottom action buttons.
    /// </summary>
    static class CoachingFloatingMetricsHud
    {
        public const string MetricsRootName = "FloatingMetricsHud";

        public static GameObject EnsureRoot(
            RectTransform coachingCardRoot,
            TextMeshProUGUI fontSource,
            out RectTransform rootRt,
            out TextMeshProUGUI bodyTmp)
        {
            rootRt = null;
            bodyTmp = null;

            if (coachingCardRoot == null)
                return null;

            var existing = coachingCardRoot.Find(MetricsRootName);
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
                rootRt = go.GetComponent<RectTransform>();
                bodyTmp = go.GetComponentInChildren<TextMeshProUGUI>(true);
                EnsureMetricsHudChrome(go);
                return go;
            }

            go = new GameObject(MetricsRootName, typeof(RectTransform), typeof(CanvasRenderer));
            rootRt = go.GetComponent<RectTransform>();
            go.transform.SetParent(coachingCardRoot, false);

            EnsureMetricsHudChrome(go);

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.35f);
            bg.raycastTarget = false;

            var textGo = new GameObject("MetricsText", typeof(RectTransform), typeof(CanvasRenderer));
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(10f, 8f);
            textRt.offsetMax = new Vector2(-10f, -8f);

            bodyTmp = textGo.AddComponent<TextMeshProUGUI>();
            bodyTmp.textWrappingMode = TextWrappingModes.Normal;
            bodyTmp.overflowMode = TextOverflowModes.Overflow;
            bodyTmp.alignment = TextAlignmentOptions.TopLeft;
            bodyTmp.fontSize = 14f;
            bodyTmp.richText = false;
            bodyTmp.color = Color.white;
            bodyTmp.raycastTarget = false;
            if (fontSource != null && fontSource.font != null)
            {
                bodyTmp.font = fontSource.font;
                bodyTmp.fontSharedMaterial = fontSource.fontSharedMaterial;
            }
            else if (TMP_Settings.defaultFontAsset != null)
                bodyTmp.font = TMP_Settings.defaultFontAsset;

            return go;
        }

        static void EnsureMetricsHudChrome(GameObject root)
        {
            if (!root.TryGetComponent<LayoutElement>(out var le))
                le = root.AddComponent<LayoutElement>();
            le.ignoreLayout = true;

            if (!root.TryGetComponent<CanvasGroup>(out var cg))
                cg = root.AddComponent<CanvasGroup>();
            cg.interactable = false;
            cg.blocksRaycasts = false;
        }

        /// <summary>
        /// Fills the vertical band between the modal bottom and the top of the action buttons (in card local space).
        /// </summary>
        public static void LayoutBetweenModalAndActions(
            RectTransform cardRoot,
            RectTransform metricsRoot,
            RectTransform modalRt,
            IReadOnlyList<RectTransform> visibleActionRects,
            float gap,
            float minHeight,
            float maxHeight)
        {
            if (cardRoot == null || metricsRoot == null)
                return;

            var corners = new Vector3[4];
            var buttonTopY = float.MinValue;
            var hasButton = false;
            for (var i = 0; i < visibleActionRects.Count; i++)
            {
                var rt = visibleActionRects[i];
                if (rt == null || !rt.gameObject.activeInHierarchy)
                    continue;

                rt.GetWorldCorners(corners);
                for (var j = 0; j < 4; j++)
                {
                    var ly = cardRoot.InverseTransformPoint(corners[j]).y;
                    if (!hasButton || ly > buttonTopY)
                    {
                        buttonTopY = ly;
                        hasButton = true;
                    }
                }
            }

            if (!hasButton)
                buttonTopY = cardRoot.rect.yMin + 24f;

            var modalBottomY = cardRoot.rect.yMax - 24f;
            if (modalRt != null && modalRt.gameObject.activeInHierarchy)
            {
                modalRt.GetWorldCorners(corners);
                var minY = float.MaxValue;
                for (var j = 0; j < 4; j++)
                    minY = Mathf.Min(minY, cardRoot.InverseTransformPoint(corners[j]).y);
                modalBottomY = minY;
            }

            var ph = cardRoot.rect.height;
            if (ph < 0.001f)
                ph = 1f;
            var py = cardRoot.rect.yMin;

            var bandBottom = buttonTopY + gap;
            var bandTop = modalBottomY - gap;
            if (bandTop <= bandBottom + 4f)
            {
                // Modal / button bounds can invert when layout is odd — use a stable middle band.
                bandBottom = py + ph * 0.22f;
                bandTop = py + ph * 0.48f;
            }

            var height = bandTop - bandBottom;
            if (height < minHeight)
            {
                height = Mathf.Min(maxHeight, minHeight);
                bandTop = bandBottom + height;
            }
            else
                height = Mathf.Clamp(height, minHeight, maxHeight);
            var anchorBot = (bandBottom - py) / ph;
            var anchorTop = (bandTop - py) / ph;
            anchorBot = Mathf.Clamp01(anchorBot);
            anchorTop = Mathf.Clamp01(Mathf.Max(anchorTop, anchorBot + 0.06f));

            // Full horizontal stretch with side padding so TMP always gets a real width (VLG + stretch children can collapse to 0).
            metricsRoot.anchorMin = new Vector2(0f, anchorBot);
            metricsRoot.anchorMax = new Vector2(1f, anchorTop);
            metricsRoot.pivot = new Vector2(0.5f, 0.5f);
            const float padX = 12f;
            metricsRoot.offsetMin = new Vector2(padX, 0f);
            metricsRoot.offsetMax = new Vector2(-padX, 0f);
            metricsRoot.anchoredPosition = Vector2.zero;

            LayoutRebuilder.ForceRebuildLayoutImmediate(cardRoot);
            LayoutRebuilder.ForceRebuildLayoutImmediate(metricsRoot);
        }
    }
}
