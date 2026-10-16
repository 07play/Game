using System.Collections;
using UnityEngine;

namespace DrawFlower.UI
{
    /// <summary>
    /// 轻量 UI 补间工具（不依赖 DOTween / 协程库）。
    /// 所有等待都可以选择使用「不受 timeScale 影响」的时间，避免游戏暂停时 UI 动画卡住。
    /// </summary>
    public static class UITween
    {
        /// <summary>拿到（没有就补一个）CanvasGroup。淡入淡出统一走 CanvasGroup，比逐个改 Image.color 更安全。</summary>
        public static CanvasGroup EnsureCanvasGroup(GameObject go)
        {
            if (go == null) return null;
            var cg = go.GetComponent<CanvasGroup>();
            if (cg == null) cg = go.AddComponent<CanvasGroup>();
            return cg;
        }

        /// <summary>把 CanvasGroup 立即可见、可交互。</summary>
        public static void SetVisible(CanvasGroup cg, bool visible)
        {
            if (cg == null) return;
            cg.alpha = visible ? 1f : 0f;
            cg.interactable = visible;
            cg.blocksRaycasts = visible;
        }

        /// <summary>等待若干秒。unscaled = true 时不受 Time.timeScale 影响。</summary>
        public static IEnumerator Wait(float seconds, bool unscaled = true)
        {
            if (seconds <= 0f) yield break;
            if (!unscaled)
            {
                yield return new WaitForSeconds(seconds);
                yield break;
            }

            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        /// <summary>CanvasGroup 透明度补间。</summary>
        public static IEnumerator Fade(CanvasGroup cg, float from, float to, float duration,
                                       float delay = 0f, bool unscaled = true)
        {
            if (cg == null) yield break;
            if (delay > 0f) yield return Wait(delay, unscaled);

            cg.alpha = from;
            if (duration <= 0f)
            {
                cg.alpha = to;
                yield break;
            }

            float t = 0f;
            while (t < duration)
            {
                t += unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
                cg.alpha = Mathf.LerpUnclamped(from, to, EaseInOutCubic(Mathf.Clamp01(t / duration)));
                yield return null;
            }
            cg.alpha = to;
        }

        /// <summary>「啪」地弹出：缩放 + 淡入。用于对错反馈图。</summary>
        public static IEnumerator PopIn(RectTransform rt, CanvasGroup cg, float fromScale,
                                        float duration, float delay = 0f, bool unscaled = true)
        {
            if (rt == null) yield break;
            if (delay > 0f) yield return Wait(delay, unscaled);

            Vector3 baseScale = rt.localScale;
            rt.localScale = baseScale * fromScale;
            if (cg != null) cg.alpha = 0f;

            if (duration <= 0f)
            {
                rt.localScale = baseScale;
                if (cg != null) cg.alpha = 1f;
                yield break;
            }

            float t = 0f;
            while (t < duration)
            {
                t += unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                rt.localScale = baseScale * Mathf.LerpUnclamped(fromScale, 1f, EaseOutBack(k));
                if (cg != null) cg.alpha = Mathf.Clamp01(k * 2.5f);
                yield return null;
            }

            rt.localScale = baseScale;
            if (cg != null) cg.alpha = 1f;
        }

        public static float EaseInOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
        }

        public static float EaseOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - Mathf.Pow(1f - t, 3f);
        }

        /// <summary>末端会轻微冲过头再回弹，配合缩放使用效果更好。</summary>
        public static float EaseOutBack(float t, float overshoot = 1.70158f)
        {
            t = Mathf.Clamp01(t);
            float c3 = overshoot + 1f;
            float p = t - 1f;
            return 1f + c3 * p * p * p + overshoot * p * p;
        }
    }
}
