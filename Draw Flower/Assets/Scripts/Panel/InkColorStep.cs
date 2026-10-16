using System.Collections;
using System.Collections.Generic;
using DrawFlower.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DrawFlower.Gameplay
{
    /// <summary>
    /// 步骤 ① 选墨色。
    ///
    /// 规则：
    ///   · 点错墨色 → 在该按钮上生成 no.png，**永久保留**（不会被自动清掉），可累积标记多个错误按钮。
    ///   · 点对墨色 → 在该按钮上生成 yes.png，然后 5 个墨色按钮依次淡出消失，本步骤结束。
    ///
    /// 放在 Palette（或 Canvas 根）上都行，按钮会自动扫描，不需要手工到 Inspector 里连 onClick。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("古画教学/步骤 01 · 选墨色")]
    public class InkColorStep : StepBase
    {
        [Header("① 墨色按钮（从左到右，留空自动扫描）")]
        [SerializeField]
        [Tooltip("按 Hierarchy 顺序 = 从左到右。留空时脚本会自动在下面这个名字的物体下找按钮。")]
        private Button[] inkButtons = new Button[0];

        [SerializeField]
        [Tooltip("自动扫描用的容器名（TutorialCanvas/Palette）")]
        private string buttonContainerName = "Palette";

        [Header("② 正确答案")]
        [SerializeField, Range(0, 4)]
        [Tooltip("0 = 最左边的墨色，4 = 最右边的墨色")]
        private int correctIndex = 0;

        [Header("③ 对错反馈图（Resources 内路径，不带扩展名）")]
        [SerializeField] private string yesResourcePath = "UI/yes";
        [SerializeField] private string noResourcePath = "UI/no";

        [SerializeField]
        [Tooltip("反馈图的最大外框尺寸（等比缩放，不会拉变形）")]
        private Vector2 feedbackSize = new Vector2(108f, 96f);

        [SerializeField] private Vector2 feedbackOffset = Vector2.zero;

        [SerializeField]
        [Tooltip("给反馈图一点随机旋转，看起来像随手贴上去的印章")]
        private bool randomRotation = true;

        [SerializeField] private Vector2 rotationRange = new Vector2(-18f, 18f);
        [SerializeField] private float popDuration = 0.18f;
        [SerializeField] private float popFromScale = 1.35f;

        [Header("④ 淡出消失")]
        [SerializeField] private float fadeDelay = 0.45f;
        [SerializeField] private float fadeDuration = 0.7f;

        [SerializeField]
        [Tooltip("相邻按钮之间错开的时间，越大越像「一个接一个淡去」")]
        private float fadeStagger = 0.09f;

        [SerializeField]
        [Tooltip("勾选：5 个按钮同时淡出（忽略错开时间）")]
        private bool fadeAllAtOnce = false;

        [SerializeField]
        [Tooltip("淡出结束后是否把按钮 SetActive(false)")]
        private bool deactivateWhenDone = true;

        [Header("⑤ 规则")]
        [SerializeField]
        [Tooltip("同一个错误按钮是否允许重复点击、叠加多个 no 图")]
        private bool allowRepeatWrongClick = false;

        [Header("⑥ 事件（可在 Inspector 里挂后续逻辑）")]
        public UnityEvent<int> onInkClicked = new UnityEvent<int>();
        public UnityEvent<int> onCorrectAnswer = new UnityEvent<int>();
        public UnityEvent<int> onWrongAnswer = new UnityEvent<int>();
        public UnityEvent onFadedOut = new UnityEvent();

        /// <summary>点错的总次数（可用于做「点错 3 次后给提示」之类的逻辑）</summary>
        public int WrongCount { get; private set; }

        public int CorrectIndex => correctIndex;

        /// <summary>实际参与判定的按钮列表（从左到右），运行时只读</summary>
        public IReadOnlyList<Button> Buttons => _buttons;

        private Button[] _buttons;
        private Sprite _yesSprite;
        private Sprite _noSprite;
        private readonly HashSet<int> _markedWrong = new HashSet<int>();
        private bool _resolved;
        private bool _finished;

        // ─────────────────────────────────────────────────────────────

        protected override void OnBegin()
        {
            if (!_resolved) Resolve();
            if (_buttons == null || _buttons.Length == 0) return;

            SetPrompt(0);

            _yesSprite = LoadSprite(yesResourcePath, "yes");
            _noSprite = LoadSprite(noResourcePath, "no");

            for (int i = 0; i < _buttons.Length; i++)
            {
                var btn = _buttons[i];
                if (btn == null) continue;

                // 重置状态，保证这一步可以被重复播放（例如重新开始本关）
                btn.interactable = true;
                UITween.SetVisible(UITween.EnsureCanvasGroup(btn.gameObject), true);

                int index = i;
                btn.onClick.AddListener(() => HandleClick(index));
            }
        }

        /// <summary>确定按钮列表。优先用 Inspector 里配好的，否则自动扫描。</summary>
        private void Resolve()
        {
            _resolved = true;

            if (inkButtons != null && inkButtons.Length > 0)
            {
                _buttons = (Button[])inkButtons.Clone();
            }
            else
            {
                Transform container = FindContainer();
                if (container == null)
                {
                    Debug.LogError(
                        $"[InkColorStep] 找不到墨色按钮。请把 5 个按钮拖到 Ink Buttons，" +
                        $"或确认面板里有名为 \"{buttonContainerName}\" 的物体。", this);
                    _buttons = new Button[0];
                    return;
                }

                var list = new List<Button>();
                for (int i = 0; i < container.childCount; i++)
                {
                    var b = container.GetChild(i).GetComponent<Button>();
                    if (b != null) list.Add(b);
                }
                _buttons = list.ToArray();
            }

            for (int i = 0; i < _buttons.Length; i++)
            {
                if (_buttons[i] == null)
                    Debug.LogError($"[InkColorStep] 第 {i} 个墨色按钮是空的（Missing），请在 Inspector 里补上。", this);
            }

            if (_buttons.Length != 5)
            {
                Debug.LogWarning(
                    $"[InkColorStep] 扫描到 {_buttons.Length} 个墨色按钮（正常是 5 个）。" +
                    "请检查 Palette 下的顺序是否和「从左到右」一致。", this);
            }

            if (_buttons.Length > 0 && (correctIndex < 0 || correctIndex >= _buttons.Length))
            {
                Debug.LogError(
                    $"[InkColorStep] Correct Index = {correctIndex} 超出范围 0~{_buttons.Length - 1}，已临时修正为 0。" +
                    "请到 Inspector 重新设置正确答案。", this);
                correctIndex = 0;
            }
        }

        private Transform FindContainer()
        {
            if (!string.IsNullOrEmpty(buttonContainerName))
            {
                var t = FindByName(transform, buttonContainerName);
                if (t != null) return t;
            }

            // 退路 1：脚本自己就挂在 Palette 上
            if (GetComponent<Button>() != null) return transform;

            // 退路 2：自己的直接子物体里有按钮
            for (int i = 0; i < transform.childCount; i++)
            {
                if (transform.GetChild(i).GetComponent<Button>() != null) return transform;
            }
            return null;
        }

        private static Transform FindByName(Transform root, string name)
        {
            if (root.name.Equals(name, System.StringComparison.OrdinalIgnoreCase)) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = FindByName(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        // ─────────────────────────────────────────────────────────────

        private void HandleClick(int index)
        {
            if (_finished || _buttons == null || index < 0 || index >= _buttons.Length) return;

            onInkClicked.Invoke(index);

            // ── 选对 ──────────────────────────────────────────────
            if (index == correctIndex)
            {
                _finished = true;                       // 立刻上锁，防止淡出期间再点出别的反馈图
                SpawnFeedback(_buttons[index].transform, _yesSprite, "Feedback_Yes");
                onCorrectAnswer.Invoke(index);
                SetPrompt(2);
                StartCoroutine(FinishRoutine());
                return;
            }

            // ── 选错 ──────────────────────────────────────────────
            WrongCount++;
            onWrongAnswer.Invoke(index);
            SetPrompt(1);

            bool alreadyMarked = _markedWrong.Contains(index);
            if (!allowRepeatWrongClick && alreadyMarked) return;

            _markedWrong.Add(index);
            SpawnFeedback(_buttons[index].transform, _noSprite, "Feedback_No_" + index);
        }

        /// <summary>在按钮上生成反馈图。生成的是普通子物体，除了本步骤结束，不会有任何东西清掉它。</summary>
        private void SpawnFeedback(Transform parent, Sprite sprite, string goName)
        {
            if (parent == null) return;

            if (sprite == null)
            {
                Debug.LogError($"[InkColorStep] 反馈图缺失，无法生成 {goName}。请检查 Resources 路径和图片的 Texture Type。", this);
                return;
            }

            var go = new GameObject(goName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);

            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = feedbackOffset;
            rt.sizeDelta = feedbackSize;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.Euler(0f, 0f,
                randomRotation ? Random.Range(rotationRange.x, rotationRange.y) : 0f);
            rt.SetAsLastSibling();   // 画在按钮图片上面

            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;    // 等比缩放，不拉变形
            img.raycastTarget = false;    // 关键：反馈图不能挡住按钮自己的点击

            var cg = go.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;

            StartCoroutine(UITween.PopIn(rt, cg, popFromScale, popDuration));
        }

        /// <summary>点对之后：禁止再点 → 等一拍 → 5 个按钮依次淡出 → 收尾。</summary>
        private IEnumerator FinishRoutine()
        {
            if (_buttons != null)
            {
                foreach (var b in _buttons)
                    if (b != null) b.interactable = false;
            }

            yield return UITween.Wait(fadeDelay);

            float total = 0f;
            if (_buttons != null)
            {
                for (int i = 0; i < _buttons.Length; i++)
                {
                    var b = _buttons[i];
                    if (b == null) continue;

                    var cg = UITween.EnsureCanvasGroup(b.gameObject);
                    cg.interactable = false;
                    cg.blocksRaycasts = false;

                    float delay = fadeAllAtOnce ? 0f : i * fadeStagger;
                    StartCoroutine(UITween.Fade(cg, cg.alpha, 0f, fadeDuration, delay));
                    total = Mathf.Max(total, delay + fadeDuration);
                }
            }

            yield return UITween.Wait(total);

            if (deactivateWhenDone && _buttons != null)
            {
                foreach (var b in _buttons)
                {
                    // 别把自己所在的物体关掉，否则协程后段的回调会丢
                    if (b != null && b.gameObject != gameObject) b.gameObject.SetActive(false);
                }
            }

            onFadedOut.Invoke();
            SwitchPanelIfNeeded();
            CompleteStep();
        }

        private Sprite LoadSprite(string path, string label)
        {
            if (string.IsNullOrEmpty(path)) return null;

            var s = Resources.Load<Sprite>(path);
            if (s == null)
            {
                Debug.LogError(
                    $"[InkColorStep] 读不到 Resources/{path}（{label} 图）。请确认：" +
                    "① 文件在 Assets/Resources/ 下；② Texture Type 是 Sprite (2D and UI)。", this);
            }
            return s;
        }
    }
}
