using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using DrawFlower.UI;

namespace DrawFlower.Gameplay
{
    /// <summary>
    /// 步骤 ⑤ 渐变过渡（如 LeavePanel5：9-1 → 9-2）：
    ///   · 进入后 fromImage（9-1）显示、toImage（9-2）隐藏。
    ///   · 自动交叉淡化：9-1 渐渐淡出失活，9-2 渐渐淡入出现。
    ///   · 播完后下一步按钮由灰变绿，等学生点下一步才结束本步骤。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("古画教学/步骤 05 · 渐变过渡（淡出/淡入）")]
    public class FadeTransitionStep : StepBase
    {
        [Header("① 过渡图")]
        [Tooltip("一开始显示的图（如 9-1），会渐渐淡出")]
        [SerializeField] private GameObject fromImage;

        [Tooltip("淡出后出现的图（如 9-2），会渐渐淡入")]
        [SerializeField] private GameObject toImage;

        [Header("② 淡入淡出参数")]
        [Tooltip("淡出/淡入各用时（秒），默认 1")]
        [SerializeField] private float fadeDuration = 1f;

        [Tooltip("进入后先停多久再开始淡化（秒），默认 0.3")]
        [SerializeField] private float fadeDelay = 0.3f;

        [Header("③ 下一步按钮（淡化播完前不可点）")]
        [SerializeField] private Button nextButton;

        [Tooltip("按钮「可点」颜色（绿）")]
        [SerializeField] private Color nextButtonEnabledColor = new Color(0.40f, 0.85f, 0.40f, 1f);

        [Tooltip("按钮「不可点」颜色（灰）")]
        [SerializeField] private Color nextButtonDisabledColor = new Color(0.7f, 0.7f, 0.7f, 0.5f);

        [Tooltip("可点状态图（可选）")]
        [SerializeField] private Sprite nextButtonEnabledSprite;

        [Tooltip("不可点状态图（可选）")]
        [SerializeField] private Sprite nextButtonDisabledSprite;

        [Header("④ 事件")]
        public UnityEvent onTransitionDone = new UnityEvent();

        // ── 状态 ──
        private CanvasGroup _fromCg;
        private CanvasGroup _toCg;
        private Coroutine _coroutine;
        private bool _nextButtonWired;

        // ── StepBase ──

        protected override void OnBegin()
        {
            if (nextButton != null && !nextButton.gameObject.activeSelf)
                nextButton.gameObject.SetActive(true);

            _fromCg = UITween.EnsureCanvasGroup(fromImage);
            _toCg = UITween.EnsureCanvasGroup(toImage);

            if (fromImage != null) fromImage.SetActive(true);
            if (toImage != null) toImage.SetActive(true);

            UITween.SetVisible(_fromCg, true);   // 9-1 可见
            UITween.SetVisible(_toCg, false);    // 9-2 隐藏（alpha=0，不挡射线）

            ApplyNextButton(false);
            WireNextButtonIfNeeded();
            SetPrompt(0);

            if (_coroutine != null) StopCoroutine(_coroutine);
            _coroutine = StartCoroutine(PlayTransition());
        }

        protected override void OnReset()
        {
            OnBegin();
        }

        // ── 交叉淡化 ──

        private IEnumerator PlayTransition()
        {
            if (fadeDelay > 0f)
                yield return UITween.Wait(fadeDelay, true);

            if (fromImage != null) fromImage.SetActive(true);
            if (toImage != null) toImage.SetActive(true);

            if (fadeDuration <= 0f)
            {
                // 立即切换
                if (_fromCg != null) _fromCg.alpha = 0f;
                if (_toCg != null) _toCg.alpha = 1f;
            }
            else
            {
                float t = 0f;
                while (t < fadeDuration)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / fadeDuration);
                    float e = UITween.EaseInOutCubic(k);
                    if (_fromCg != null) _fromCg.alpha = Mathf.Lerp(1f, 0f, e);
                    if (_toCg != null) _toCg.alpha = Mathf.Lerp(0f, 1f, e);
                    yield return null;
                }
                if (_fromCg != null) _fromCg.alpha = 0f;
                if (_toCg != null) _toCg.alpha = 1f;
            }

            if (fromImage != null) fromImage.SetActive(false);

            onTransitionDone.Invoke();
            ApplyNextButton(true);
            SetPrompt(1);
            _coroutine = null;
        }

        // ── 工具 ──

        private void ApplyNextButton(bool enabled)
        {
            if (nextButton == null) return;
            nextButton.interactable = enabled;
            var img = nextButton.GetComponent<Image>();
            if (img != null)
            {
                if (enabled && nextButtonEnabledSprite != null) img.sprite = nextButtonEnabledSprite;
                if (!enabled && nextButtonDisabledSprite != null) img.sprite = nextButtonDisabledSprite;
                img.color = enabled ? nextButtonEnabledColor : nextButtonDisabledColor;
            }
        }

        private void WireNextButtonIfNeeded()
        {
            if (_nextButtonWired) return;
            if (nextButton == null) return;
            nextButton.onClick.AddListener(OnNextButtonClicked);
            _nextButtonWired = true;
        }

        private void OnNextButtonClicked()
        {
            CompleteStep();
        }
    }
}
