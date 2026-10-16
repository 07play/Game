using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DrawFlower.Gameplay
{
    /// <summary>
    /// 步骤 ② 叶子涂色（两遍：浅涂 + 加深）。
    ///
    /// 规则：
    ///   · 玩家「长按 + 拖拽」光标进入一个未涂色的脉络区域 → 该区域对应的 lightImage / deepImage 激活。
    ///   · 一次按压只染一个脉络，松开后再按才能染下一个（不实现"一笔扫过几个"）。
    ///   · 8 个脉络都涂完 → 下一步按钮从灰变绿可点。
    ///   · 第一次全部涂完 → 自动切到「加深」模式，重置脉络、按钮再变灰，再来一遍。
    ///   · 第二次也全部涂完 → 抛 onStepCompleted（继承自 StepBase）。
    ///
    /// 为什么不用「点击」而是「长按+拖」：古画教学面对的是小学生，"涂一笔"的手感要像真画笔，
    /// 长按 + 拖拽避免误触（手指在屏幕上随便碰一下不会触发）。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("古画教学/步骤 02 · 叶子涂色（浅涂+加深）")]
    public class LeafPaintStep : StepBase
    {
        public enum Mode { Light = 0, Deep = 1 }

        [Header("① 脉络区域（按你设计的 1-1~1-8 填进去）")]
        [SerializeField]
        [Tooltip("8 个脉络，每个含 area / lightImage / deepImage。先填 area 拖入碰撞盒，再分别拖入两遍要激活的图片。")]
        private List<VeinSlot> veins = new List<VeinSlot>();

        [Header("② 当前涂色模式")]
        [SerializeField] private Mode currentMode = Mode.Light;

        [Header("③ 单遍模式")]
        [Tooltip("勾上：涂完一遍直接结束，不进入「加深」第二遍（第二片叶子用）。不勾：浅涂+加深两遍（第一片叶子用）。")]
        [SerializeField] private bool singlePass = false;

        [Header("④ 高亮（当前正在涂的叶子）")]
        [Tooltip("第一片叶子轮廓的高亮图，可选；加深模式时如果你想高亮消失，把这个拖空即可。")]
        [SerializeField] private GameObject highlightOverlay;

        [Header("⑤ 下一步按钮（涂完前不可点）")]
        [SerializeField] private Button nextButton;

        [Header("⑥ 成品图（整片叶子完成图）")]
        [Tooltip("第一片叶子涂完后的成品图（如 1-final）。浅涂完成点 next 时激活；加深完成点 next 时克隆一份叠在相同位置。")]
        [SerializeField] private GameObject finalImage;

        [Tooltip("按钮在「可点」状态下的颜色（绿色调，默认 = 0.4,0.85,0.4）")]
        [SerializeField] private Color nextButtonEnabledColor = new Color(0.40f, 0.85f, 0.40f, 1f);

        [Tooltip("按钮在「不可点」状态下的颜色（灰色调，默认 = 0.7,0.7,0.7,0.5）")]
        [SerializeField] private Color nextButtonDisabledColor = new Color(0.7f, 0.7f, 0.7f, 0.5f);

        [Tooltip("可点状态下的图片（可选，留空则只用颜色区分）")]
        [SerializeField] private Sprite nextButtonEnabledSprite;

        [Tooltip("不可点状态下的图片（可选，留空则只用颜色区分）")]
        [SerializeField] private Sprite nextButtonDisabledSprite;

        [Header("⑦ 输入判定（长按 + 拖拽）")]
        [SerializeField, Tooltip("按住多久才认为是真的「长按」（秒）。在这之前松开不算。")]
        private float longPressDuration = 0.15f;

        [SerializeField, Tooltip("按下后必须移动多少像素才认为是真的「拖拽」。")]
        private float dragThresholdPx = 6f;

        [Header("⑧ 反馈（可选）")]
        [SerializeField, Tooltip("涂对一个脉络时的音效，可选。")]
        private AudioClip veinDoneClip;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("音效音量")]
        private float veinDoneVolume = 0.6f;

        [Header("⑨ 事件")]
        public UnityEvent onVeinColored = new UnityEvent();
        public UnityEvent onAllLightDone = new UnityEvent();
        public UnityEvent onAllDeepDone = new UnityEvent();
        public UnityEvent<string> onModeChanged = new UnityEvent<string>();

        // ── 状态 ──
        private bool _isPressing;
        private float _pressStartTime;
        private Vector2 _pressStartPos;
        private int _pressedSlotIndex = -1;        // 当前这次按压已经涂了哪个槽；-1 表示还没涂
        private bool _longPressMet;               // 这次按压是否已经达到了 longPressDuration
        private bool _nextButtonWired;            // 防止重复 AddListener
        private readonly List<GameObject> _deepClones = new List<GameObject>();  // 加深模式下克隆出来的叠加层

        public Mode CurrentMode => currentMode;
        public int DoneCount
        {
            get
            {
                int n = 0;
                foreach (var v in veins) if (v != null && v.IsDone(currentMode == Mode.Deep)) n++;
                return n;
            }
        }
        public int TotalCount => veins == null ? 0 : veins.Count;
        public IReadOnlyList<VeinSlot> Veins => veins;

        // ── StepBase ──

        protected override void OnBegin()
        {
            // next 按钮如果在预制体里是失活的（常见做法：先隐藏等涂完再出现），这里把它点亮。
            // 状态（灰/绿）由 ApplyNextButton 管，这里只管"看得见"。
            if (nextButton != null && !nextButton.gameObject.activeSelf)
                nextButton.gameObject.SetActive(true);

            // 浅涂从空白开始：成品图先藏起来。加深模式不碰它，让浅涂完成时亮起的成品图保持显示。
            if (currentMode == Mode.Light && finalImage != null)
                finalImage.SetActive(false);

            ResetVeinsForCurrentMode();
            ApplyNextButton(false);
            UpdateHighlight();
            onModeChanged.Invoke(currentMode.ToString());
            WireNextButtonIfNeeded();

            // 提示 + 进度：浅涂阶段显示第 0 条，加深阶段显示第 2 条
            SetPrompt(currentMode == Mode.Deep ? 2 : 0);
            UpdateProgress(0, TotalCount);
        }

        protected override void OnReset()
        {
            ResetVeinsForCurrentMode();
            ApplyNextButton(false);
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
            if (_isPressing) return;     // 防止 Unity 偶发的双 Down
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

            var slot = veins[idx];
            if (slot == null) return;
            if (slot.IsDone(currentMode == Mode.Deep)) return;   // 已涂过

            ColorVein(idx);
            _pressedSlotIndex = idx;
        }

        private void OnPressEnd()
        {
            _isPressing = false;
            _longPressMet = false;
            _pressedSlotIndex = -1;
        }

        private Canvas _rootCanvas;   // 缓存所属根 Canvas，避免每帧 GetComponentInParent

        private int FindVeinUnderCursor()
        {
            // 关键：不能直接 Camera.main.ScreenToWorldPoint。
            //   Screen Space - Overlay 的 Canvas 不归任何相机渲染，它的子物体（含我们的 Collider2D）
            //   所在的"世界"就是 Canvas 自身的世界空间；Camera.main 的世界和它对不上。
            //   正确做法：把鼠标屏幕坐标先换算到 Canvas 局部空间，再变回 Canvas 世界空间，
            //   这样无论 Overlay / Screen Space - Camera / World Space 都成立。
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
            var slot = veins[idx];
            bool isDeep = currentMode == Mode.Deep;
            slot.SetDone(isDeep, true);

            var img = isDeep ? slot.deepImage : slot.lightImage;
            if (img != null) img.SetActive(true);

            // 加深模式：如果没配 deepImage，就克隆第一遍的图叠上去，两层叠加看起来像"加深"。
            // 以后美工给了专门的深色 deep 图，只要在向导里把 deepImage 配上，这里就走上面的分支。
            if (isDeep && slot.deepImage == null && slot.lightImage != null)
            {
                CloneLightAsDeep(slot);
            }

            onVeinColored.Invoke();
            if (veinDoneClip != null) AudioSource.PlayClipAtPoint(veinDoneClip, Camera.main != null ? Camera.main.transform.position : Vector3.zero, veinDoneVolume);

            UpdateProgress(DoneCount, TotalCount);

            if (IsAllDoneInCurrentMode())
            {
                if (!isDeep) OnLightAllDone();
                else         OnDeepAllDone();
            }
        }

        private bool IsAllDoneInCurrentMode()
        {
            bool isDeep = currentMode == Mode.Deep;
            for (int i = 0; i < veins.Count; i++)
            {
                var v = veins[i];
                if (v == null) continue;
                if (!v.IsDone(isDeep)) return false;
            }
            return veins.Count > 0;
        }

        private void OnLightAllDone()
        {
            onAllLightDone.Invoke();
            ApplyNextButton(true);
            SetPrompt(1);   // 浅涂完成：提示「点下一步」/「点下一步加深」
        }

        private void OnDeepAllDone()
        {
            onAllDeepDone.Invoke();
            // 第二遍也涂完，让玩家点 Next 进入下一步骤（不要再自动 CompleteStep，否则玩家失去掌控感）
            ApplyNextButton(true);
            SetPrompt(3);   // 加深完成：提示「点下一步」
        }

        /// <summary>下一步按钮被点时，由 GameFlow 调来进入「加深」模式。</summary>
        public void EnterDeepenMode()
        {
            currentMode = Mode.Deep;
            Reset();
            Begin();
        }

        public void EnterLightMode()
        {
            currentMode = Mode.Light;
            Reset();
            Begin();
        }

        // ── 工具 ──

        private void ResetVeinsForCurrentMode()
        {
            if (veins == null) return;
            bool isDeep = currentMode == Mode.Deep;

            // 加深模式重置时，先清掉上一轮克隆出来的叠加层（它们只在运行时存在）
            if (isDeep) ClearDeepClones();

            for (int i = 0; i < veins.Count; i++)
            {
                var v = veins[i];
                if (v == null) continue;
                v.SetDone(isDeep, false);
                // 浅涂：隐藏 lightImage；加深：隐藏 deepImage（若有）。浅层的结果在加深阶段保留不动。
                var img = isDeep ? v.deepImage : v.lightImage;
                if (img != null) img.SetActive(false);
            }
        }

        /// <summary>克隆第一遍的图作为加深叠加层，叠在 lightImage 上面。</summary>
        private void CloneLightAsDeep(VeinSlot slot)
        {
            var src = slot.lightImage;
            var clone = Instantiate(src, src.transform.parent);
            clone.name = src.name + "_deep";
            clone.SetActive(true);
            // 让克隆紧贴在源图之上（siblingIndex 越大渲染越靠上）
            int si = src.transform.GetSiblingIndex() + 1;
            clone.transform.SetSiblingIndex(si);
            _deepClones.Add(clone);
        }

        private void ClearDeepClones()
        {
            foreach (var c in _deepClones)
            {
                if (c != null) Destroy(c);
            }
            _deepClones.Clear();
        }

        private void UpdateHighlight()
        {
            if (highlightOverlay == null) return;
            // 默认两遍都显示。如果你想加深时高亮消失，把 highlight 拖到 null 即可；或者在 Inspector 里写个 mode 开关。
            highlightOverlay.SetActive(true);
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
            // 让 Next 按钮按一下就进入「加深」模式。注意：
            //   - 浅涂全完后 ApplyNextButton(true) 让按钮可点；
            //   - 加深全完后会自动 CompleteStep()，这一步不会等按钮被点（也不该等），
            //     所以这里只在 Light 模式下响应。
            nextButton.onClick.AddListener(OnNextButtonClicked);
            _nextButtonWired = true;
        }

        private void OnNextButtonClicked()
        {
            if (currentMode == Mode.Light && IsAllDoneInCurrentMode())
            {
                if (finalImage != null && !finalImage.activeSelf)
                    finalImage.SetActive(true);

                if (singlePass)
                {
                    // 单遍模式（第二片叶子）：涂完一遍直接结束本步骤
                    SwitchPanelIfNeeded();
                    CompleteStep();
                }
                else
                {
                    // 两遍模式（第一片叶子）：进入加深
                    EnterDeepenMode();
                }
            }
            else if (currentMode == Mode.Deep && IsAllDoneInCurrentMode())
            {
                // 加深完成：克隆一份成品图叠在相同位置，让整片叶子"更深"，然后结束本步骤
                CloneFinalAsDeep();
                SwitchPanelIfNeeded();
                CompleteStep();
            }
            // 其它情况（按钮被强行启用、或一半中途被点）一律忽略。
        }

        /// <summary>克隆成品图，叠在 1-final 相同位置，作为加深后的最终层。</summary>
        private void CloneFinalAsDeep()
        {
            if (finalImage == null) return;
            var clone = Instantiate(finalImage, finalImage.transform.parent);
            clone.name = finalImage.name + "_deep";
            clone.SetActive(true);
            int si = finalImage.transform.GetSiblingIndex() + 1;
            clone.transform.SetSiblingIndex(si);
        }
    }
}
