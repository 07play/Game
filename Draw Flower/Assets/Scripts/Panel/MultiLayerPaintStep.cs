using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DrawFlower.Gameplay
{
    /// <summary>
    /// 多层涂色里的一「层」：例如花瓣花萼的第 1 层是 3-1~3-11，第 2 层是 4-1~4-7……
    /// 每层有自己的一组脉络（area + lightImage），涂完一层后切到下一层。
    /// </summary>
    [System.Serializable]
    public class PaintLayer
    {
        [Tooltip("层名，仅显示用，如 3 / 4 / 5 / 6")]
        public string layerId = "";

        [Tooltip("这一层的脉络区域（area = 碰撞盒，lightImage = 拖拽后要显示的图）")]
        public List<VeinSlot> veins = new List<VeinSlot>();
    }

    /// <summary>
    /// 步骤 ③ 多层涂色（花瓣花萼）：同一面板上依次涂 N 层。
    ///
    /// 规则：
    ///   · 玩家「长按 + 拖拽」进入当前层某个未涂色的区域 → 该区域 lightImage 激活。
    ///   · 当前层全部涂完 → 下一步按钮从灰变绿可点。
    ///   · 点下一步 → 切到下一层（上一层的图保留叠加，达到"越涂越深"的效果）。
    ///   · 最后一层涂完 → 点下一步 → 抛 onStepCompleted（继承自 StepBase）。
    ///
    /// 输入检测与 LeafPaintStep 一致（Overlay Canvas 坐标换算 + Collider2D.OverlapPoint）。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("古画教学/步骤 03 · 多层涂色（花瓣花萼）")]
    public class MultiLayerPaintStep : StepBase
    {
        [Header("① 多层（每层一组脉络，按 3 → 4 → 5 → 6 顺序）")]
        [SerializeField]
        [Tooltip("每层含一组脉络。第 0 层先涂，涂完点 next 进第 1 层，依此类推。")]
        private List<PaintLayer> layers = new List<PaintLayer>();

        [Header("② 下一步按钮（每层涂完前不可点）")]
        [SerializeField] private Button nextButton;

        [Tooltip("按钮在「可点」状态下的颜色（绿色调，默认 = 0.4,0.85,0.4）")]
        [SerializeField] private Color nextButtonEnabledColor = new Color(0.40f, 0.85f, 0.40f, 1f);

        [Tooltip("按钮在「不可点」状态下的颜色（灰色调，默认 = 0.7,0.7,0.7,0.5）")]
        [SerializeField] private Color nextButtonDisabledColor = new Color(0.7f, 0.7f, 0.7f, 0.5f);

        [Tooltip("可点状态下的图片（可选，留空则只用颜色区分）")]
        [SerializeField] private Sprite nextButtonEnabledSprite;

        [Tooltip("不可点状态下的图片（可选，留空则只用颜色区分）")]
        [SerializeField] private Sprite nextButtonDisabledSprite;

        [Header("③ 输入判定（长按 + 拖拽）")]
        [SerializeField, Tooltip("按住多久才认为是真的「长按」（秒）。")]
        private float longPressDuration = 0.15f;

        [SerializeField, Tooltip("按下后必须移动多少像素才认为是真的「拖拽」。")]
        private float dragThresholdPx = 6f;

        [Header("④ 反馈（可选）")]
        [SerializeField, Tooltip("涂对一个区域时的音效，可选。")]
        private AudioClip veinDoneClip;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("音效音量")]
        private float veinDoneVolume = 0.6f;

        [Header("⑤ 事件")]
        public UnityEvent onVeinColored = new UnityEvent();
        public UnityEvent<int> onLayerCompleted = new UnityEvent<int>();
        public UnityEvent onAllLayersDone = new UnityEvent();

        [Header("⑥ 高亮切换与树枝衔接（花瓣涂完 → 树枝两遍）")]
        [SerializeField, Tooltip("花瓣阶段的高亮图（如 flower_hig），涂 3~6 层期间保持激活。")]
        private GameObject flowerHighlight;

        [SerializeField, Tooltip("树枝阶段的高亮图（如 shuzhi_hig），6 层涂完点 next 时激活。")]
        private GameObject branchHighlight;

        [SerializeField, Tooltip("树枝两遍涂色脚本（LeafPaintStep：浅涂 7-1 → 加深 8-1，7/8 共用碰撞体）。6 层涂完点 next 时自动 Begin。")]
        private LeafPaintStep branchStep;

        // ── 状态 ──
        private int _currentLayer = -1;
        private bool _isPressing;
        private float _pressStartTime;
        private Vector2 _pressStartPos;
        private int _pressedSlotIndex = -1;
        private bool _longPressMet;
        private bool _nextButtonWired;
        private Canvas _rootCanvas;

        public int CurrentLayer => _currentLayer;
        public int LayerCount => layers == null ? 0 : layers.Count;

        public PaintLayer GetLayer(int i) => (i >= 0 && i < LayerCount) ? layers[i] : null;

        // ── StepBase ──

        protected override void OnBegin()
        {
            if (nextButton != null && !nextButton.gameObject.activeSelf)
                nextButton.gameObject.SetActive(true);

            // 花瓣阶段高亮：flower_hig 亮、shuzhi_hig 灭
            if (flowerHighlight != null) flowerHighlight.SetActive(true);
            if (branchHighlight != null) branchHighlight.SetActive(false);

            // 从空白开始：所有层所有图都先隐藏
            ResetAllLayers();
            _currentLayer = 0;
            ResetLayerVeins(0);
            ApplyNextButton(false);
            WireNextButtonIfNeeded();

            // 提示 + 进度：第 0 层（花瓣第 1 遍）
            SetPrompt(0);
            UpdateProgress(0, layers.Count > 0 ? layers[0].veins.Count : 0);
        }

        protected override void OnReset()
        {
            ResetAllLayers();
            _currentLayer = 0;
            ResetLayerVeins(0);
            ApplyNextButton(false);
        }

        private void Update()
        {
            if (!IsRunning) return;
            if (_currentLayer < 0 || _currentLayer >= LayerCount) return;

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
            if (_pressedSlotIndex >= 0) return;   // 一次按压只染一个

            int idx = FindVeinUnderCursor();
            if (idx < 0) return;

            var layer = layers[_currentLayer];
            var slot = layer.veins[idx];
            if (slot == null) return;
            if (slot.IsDone(false)) return;   // 已涂过

            ColorVein(idx);
            _pressedSlotIndex = idx;
        }

        private void OnPressEnd()
        {
            _isPressing = false;
            _longPressMet = false;
            _pressedSlotIndex = -1;
        }

        private int FindVeinUnderCursor()
        {
            // Overlay Canvas 不能 Camera.main.ScreenToWorldPoint，改用 Canvas 局部空间换算
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

            var veins = layers[_currentLayer].veins;
            for (int i = 0; i < veins.Count; i++)
            {
                var v = veins[i];
                if (v == null || v.area == null) continue;
                if (v.area.OverlapPoint(probe)) return i;
            }
            return -1;
        }

        // ── 涂色 ──

        private void ColorVein(int idx)
        {
            var slot = layers[_currentLayer].veins[idx];
            slot.SetDone(false, true);
            if (slot.lightImage != null) slot.lightImage.SetActive(true);

            onVeinColored.Invoke();
            if (veinDoneClip != null)
                AudioSource.PlayClipAtPoint(veinDoneClip, Camera.main != null ? Camera.main.transform.position : Vector3.zero, veinDoneVolume);

            UpdateProgress(CurrentLayerDoneCount(), layers[_currentLayer].veins.Count);

            if (IsLayerDone(layers[_currentLayer]))
            {
                onLayerCompleted.Invoke(_currentLayer);
                ApplyNextButton(true);
            }
        }

        private int CurrentLayerDoneCount()
        {
            int n = 0;
            var layer = layers[_currentLayer];
            if (layer == null) return 0;
            for (int i = 0; i < layer.veins.Count; i++)
            {
                var v = layer.veins[i];
                if (v != null && v.IsDone(false)) n++;
            }
            return n;
        }

        private bool IsLayerDone(PaintLayer layer)
        {
            if (layer == null || layer.veins.Count == 0) return false;
            for (int i = 0; i < layer.veins.Count; i++)
            {
                var v = layer.veins[i];
                if (v == null) continue;
                if (!v.IsDone(false)) return false;
            }
            return true;
        }

        // ── 工具 ──

        private void ResetAllLayers()
        {
            if (layers == null) return;
            for (int L = 0; L < layers.Count; L++)
            {
                var layer = layers[L];
                if (layer == null) continue;
                for (int i = 0; i < layer.veins.Count; i++)
                {
                    var v = layer.veins[i];
                    if (v == null) continue;
                    v.SetDone(false, false);
                    if (v.lightImage != null) v.lightImage.SetActive(false);
                }
            }
        }

        /// <summary>重置某层的脉络为「未涂」状态（隐藏该层所有图）。之前层保持不动（叠加）。</summary>
        private void ResetLayerVeins(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= LayerCount) return;
            var layer = layers[layerIndex];
            if (layer == null) return;
            for (int i = 0; i < layer.veins.Count; i++)
            {
                var v = layer.veins[i];
                if (v == null) continue;
                v.SetDone(false, false);
                if (v.lightImage != null) v.lightImage.SetActive(false);
            }
        }

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
            if (_currentLayer < 0 || _currentLayer >= LayerCount) return;
            if (!IsLayerDone(layers[_currentLayer])) return;   // 没涂完，忽略

            if (_currentLayer >= LayerCount - 1)
            {
                // 最后一层也涂完 → 切换高亮（花瓣→树枝）并开始树枝两遍，然后结束本步骤
                onAllLayersDone.Invoke();
                if (flowerHighlight != null) flowerHighlight.SetActive(false);
                if (branchHighlight != null) branchHighlight.SetActive(true);
                if (branchStep != null) branchStep.Begin();
                CompleteStep();
            }
            else
            {
                // 切到下一层（上一层的图保留叠加）
                _currentLayer++;
                ResetLayerVeins(_currentLayer);
                ApplyNextButton(false);
                SetPrompt(_currentLayer);
                UpdateProgress(0, layers[_currentLayer].veins.Count);
            }
        }
    }
}
