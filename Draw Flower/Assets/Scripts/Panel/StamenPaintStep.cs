using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DrawFlower.Gameplay
{
    /// <summary>花蕊涂色里的一个区域：初始图（花丝）拖拽后隐藏，替换成成品图（圆点）。</summary>
    [System.Serializable]
    public class StamenSlot
    {
        [Tooltip("仅显示用，如 1 / 2 / 3")]
        public string id = "";

        [Tooltip("判定区域（碰撞盒），拖到这里触发替换")]
        public Collider2D area;

        [Tooltip("初始图（花丝，如 1），一开始显示")]
        public GameObject initialImage;

        [Tooltip("替换图（圆点，如 8-1），拖拽后显示")]
        public GameObject resultImage;

        [System.NonSerialized] public bool done;
    }

    /// <summary>
    /// 步骤 ④ 花蕊涂色（替换式）：
    ///   · 初始 1~5（花丝）都显示。
    ///   · 玩家「长按 + 拖拽」到某个区域 → 该区域的初始图（花丝）隐藏，替换图（8-X 圆点）显示。
    ///   · 5 个都替换完 → 成品图（8-6）激活、下一步按钮变绿可点。
    ///   · 点下一步 → 抛 onStepCompleted。
    ///
    /// 输入检测与 LeafPaintStep / MultiLayerPaintStep 一致（Overlay Canvas 坐标换算 + OverlapPoint）。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("古画教学/步骤 04 · 花蕊涂色（替换式）")]
    public class StamenPaintStep : StepBase
    {
        [Header("① 花蕊区域（5 个：area + 初始图 + 替换图）")]
        [SerializeField] private List<StamenSlot> slots = new List<StamenSlot>();

        [Header("② 成品图（全部完成后延迟激活，如 8-6）")]
        [SerializeField] private GameObject finalImage;

        [Tooltip("全部涂完后，延迟多久再激活成品图（秒）")]
        [SerializeField] private float finalDelay = 1f;

        [Header("③ 下一步按钮（涂完前不可点）")]
        [SerializeField] private Button nextButton;

        [Tooltip("按钮「可点」颜色（绿），默认 0.4,0.85,0.4")]
        [SerializeField] private Color nextButtonEnabledColor = new Color(0.40f, 0.85f, 0.40f, 1f);

        [Tooltip("按钮「不可点」颜色（灰），默认 0.7,0.7,0.7,0.5")]
        [SerializeField] private Color nextButtonDisabledColor = new Color(0.7f, 0.7f, 0.7f, 0.5f);

        [Tooltip("可点状态图（可选）")]
        [SerializeField] private Sprite nextButtonEnabledSprite;

        [Tooltip("不可点状态图（可选）")]
        [SerializeField] private Sprite nextButtonDisabledSprite;

        [Header("④ 输入判定（长按 + 拖拽）")]
        [SerializeField, Tooltip("按住多久才算「长按」（秒）")]
        private float longPressDuration = 0.15f;

        [SerializeField, Tooltip("按下后移动多少像素才算「拖拽」")]
        private float dragThresholdPx = 6f;

        [Header("⑤ 事件")]
        public UnityEvent onStamenColored = new UnityEvent();
        public UnityEvent onAllStamenDone = new UnityEvent();

        // ── 状态 ──
        private bool _isPressing;
        private float _pressStartTime;
        private Vector2 _pressStartPos;
        private int _pressedSlotIndex = -1;
        private bool _longPressMet;
        private bool _nextButtonWired;
        private Canvas _rootCanvas;
        private Coroutine _finalCoroutine;

        public int DoneCount
        {
            get { int n = 0; foreach (var s in slots) if (s != null && s.done) n++; return n; }
        }

        // ── StepBase ──

        protected override void OnBegin()
        {
            if (nextButton != null && !nextButton.gameObject.activeSelf)
                nextButton.gameObject.SetActive(true);

            // 重置：初始图（花丝）显示，替换图（圆点）隐藏，成品图隐藏
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s == null) continue;
                s.done = false;
                if (s.initialImage != null) s.initialImage.SetActive(true);
                if (s.resultImage != null) s.resultImage.SetActive(false);
            }
            if (finalImage != null) finalImage.SetActive(false);
            if (_finalCoroutine != null) { StopCoroutine(_finalCoroutine); _finalCoroutine = null; }

            ApplyNextButton(false);
            WireNextButtonIfNeeded();

            SetPrompt(0);
            UpdateProgress(0, slots.Count);
        }

        protected override void OnReset()
        {
            OnBegin();
        }

        private void Update()
        {
            if (!IsRunning) return;
            if (Input.GetMouseButtonDown(0)) OnPressBegin();
            if (Input.GetMouseButton(0))    OnPressHold();
            if (Input.GetMouseButtonUp(0))  OnPressEnd();
        }

        // ── 输入 ──

        private void OnPressBegin()
        {
            if (_isPressing) return;
            _isPressing = true;
            _longPressMet = false;
            _pressedSlotIndex = -1;
            _pressStartTime = Time.unscaledTime;
            _pressStartPos = Input.mousePosition;
        }

        private void OnPressHold()
        {
            if (!_isPressing) return;

            if (!_longPressMet)
            {
                float held = Time.unscaledTime - _pressStartTime;
                float moved = Vector2.Distance(_pressStartPos, Input.mousePosition);
                if (held >= longPressDuration && moved >= dragThresholdPx) _longPressMet = true;
            }

            if (!_longPressMet) return;
            if (_pressedSlotIndex >= 0) return;

            int idx = FindSlotUnderCursor();
            if (idx < 0) return;

            var slot = slots[idx];
            if (slot == null || slot.done) return;

            ColorStamen(idx);
            _pressedSlotIndex = idx;
        }

        private void OnPressEnd()
        {
            _isPressing = false;
            _longPressMet = false;
            _pressedSlotIndex = -1;
        }

        private int FindSlotUnderCursor()
        {
            if (_rootCanvas == null)
            {
                var own = GetComponentInParent<Canvas>();
                _rootCanvas = own != null ? own.rootCanvas : null;
            }
            if (_rootCanvas == null) return -1;

            Camera cam = null;
            if (_rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                cam = _rootCanvas.worldCamera != null ? _rootCanvas.worldCamera : Camera.main;

            var canvasRT = (RectTransform)_rootCanvas.transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRT, (Vector2)Input.mousePosition, cam, out Vector2 local))
                return -1;

            Vector2 probe = canvasRT.TransformPoint(local);

            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s == null || s.area == null) continue;
                if (s.area.OverlapPoint(probe)) return i;
            }
            return -1;
        }

        // ── 涂色（替换式） ──

        private void ColorStamen(int idx)
        {
            var slot = slots[idx];
            slot.done = true;
            if (slot.initialImage != null) slot.initialImage.SetActive(false);
            if (slot.resultImage != null) slot.resultImage.SetActive(true);

            onStamenColored.Invoke();
            UpdateProgress(DoneCount, slots.Count);

            if (IsAllDone())
            {
                onAllStamenDone.Invoke();
                SetPrompt(1);   // 全涂完：提示「点下一步」
                // 全部涂完后，延迟 finalDelay 秒再激活成品图 + 点亮按钮
                if (_finalCoroutine != null) StopCoroutine(_finalCoroutine);
                _finalCoroutine = StartCoroutine(ActivateFinalAfterDelay());
            }
        }

        private IEnumerator ActivateFinalAfterDelay()
        {
            yield return new WaitForSecondsRealtime(finalDelay);
            if (finalImage != null) finalImage.SetActive(true);
            ApplyNextButton(true);
            _finalCoroutine = null;
        }

        private bool IsAllDone()
        {
            if (slots.Count == 0) return false;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s == null) continue;
                if (!s.done) return false;
            }
            return true;
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
            if (!IsAllDone()) return;
            SwitchPanelIfNeeded();
            CompleteStep();
        }
    }
}
