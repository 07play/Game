using UnityEngine;
using UnityEngine.Events;

namespace DrawFlower.Gameplay
{
    /// <summary>
    /// 「教学流程的总指挥」 —— 顺序执行多个教学步骤，按完成事件切面板。
    ///
    /// 用法（在场景里随便一个空 GameObject 上挂这个脚本）：
    ///   1. 把每一步所在的 GameObject 拖进 steps[] 数组（默认含 InkColorStep + LeafPaintStep 两段）
    ///   2. 每一步的 GameObject 上挂对应的 StepBase 子类
    ///   3. 进入 Play 即可：第 0 步 autoStart 自动开；后续由 onStepCompleted 接力
    ///
    /// 同一个物体上挂的步骤会被自动管理：开始时只激活当前步所在物体，其余 SetActive(false)。
    /// </summary>
    [AddComponentMenu("古画教学/流程 · GameFlow 总指挥")]
    public class GameFlow : MonoBehaviour
    {
        [System.Serializable]
        public class FlowStep
        {
            [Tooltip("本步骤所在 GameObject。开始时只有第 0 步是激活的。")]
            public GameObject container;

            [Tooltip("挂在该 GameObject 上的步骤脚本（可以自动从 container 找，无需手填）。")]
            public StepBase step;

            [Tooltip("进入本步骤前的开场动画（可选）。")]
            public UnityEvent onEnter;

            [Tooltip("本步骤结束时的收场动画（可选）。下一段的 SetActive(true) 会在它之后。")]
            public UnityEvent onExit;
        }

        [Header("① 步骤顺序（按数组下标 0 → 1 → 2 ... 顺序执行）")]
        [SerializeField] private FlowStep[] steps = new FlowStep[0];

        [Header("② 调试")]
        [SerializeField, Tooltip("勾上：开始时跳到指定步骤（用于测试）")]
        private bool useDebugStart = false;

        [SerializeField, Min(0)] private int debugStartIndex = 0;

        [Header("③ 全部完成")]
        [Tooltip("所有步骤都完成后触发，可挂一个「完成画面」或者重新开始游戏。")]
        public UnityEvent onAllCompleted = new UnityEvent();

        private int _currentIndex = -1;

        private void Start()
        {
            if (steps == null || steps.Length == 0) return;

            // 自动补全 step 引用
            for (int i = 0; i < steps.Length; i++)
            {
                var s = steps[i];
                if (s == null) continue;
                if (s.container == null) continue;
                if (s.step == null) s.step = s.container.GetComponentInChildren<StepBase>(true);
            }

            int startAt = useDebugStart ? Mathf.Clamp(debugStartIndex, 0, steps.Length - 1) : 0;
            EnterStep(startAt);
        }

        /// <summary>从外部跳到某一步（教学测试时用）。</summary>
        public void JumpTo(int index)
        {
            if (index < 0 || index >= steps.Length) return;
            // 重置已完成的步骤
            for (int i = 0; i < steps.Length; i++)
            {
                var s = steps[i];
                if (s != null && s.step != null && s.step.IsCompleted) s.step.Reset();
            }
            EnterStep(index);
        }

        private void EnterStep(int index)
        {
            if (index < 0 || index >= steps.Length) return;
            _currentIndex = index;
            var s = steps[index];
            if (s == null) return;

            // 其它步骤所在的物体全部隐藏
            for (int i = 0; i < steps.Length; i++)
            {
                var other = steps[i];
                if (other == null || other.container == null) continue;
                other.container.SetActive(i == index);
            }

            // 开场动画（如有）
            s.onEnter?.Invoke();

            if (s.step != null) s.step.Begin();
        }

        private void Update()
        {
            if (steps == null) return;
            if (_currentIndex < 0 || _currentIndex >= steps.Length) return;

            var cur = steps[_currentIndex];
            if (cur == null || cur.step == null) return;

            // 每帧轮询：步骤完成就推进
            if (cur.step.IsCompleted)
            {
                cur.onExit?.Invoke();

                int next = _currentIndex + 1;
                if (next >= steps.Length)
                {
                    // 全部完成
                    if (_currentIndex != -1)
                    {
                        onAllCompleted?.Invoke();
                        _currentIndex = -1;
                    }
                    return;
                }
                EnterStep(next);
            }
        }

        // 方便 GameFlow 在 Inspector 里手动指过来
        public int CurrentIndex => _currentIndex;
        public FlowStep GetStep(int i) => (i >= 0 && i < steps.Length) ? steps[i] : null;
    }
}
