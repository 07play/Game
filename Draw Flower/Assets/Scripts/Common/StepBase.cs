using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DrawFlower.Gameplay
{
    /// <summary>
    /// 教学流程里「一个步骤」的基类。
    /// 古画教学是分步进行的（① 选墨色 → ② 点拖线稿显图层 → …），
    /// 每个步骤继承本类，完成时抛 onStepCompleted，由流程管理器把控制权交给下一步。
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class StepBase : MonoBehaviour
    {
        [Header("流程控制")]
        [Tooltip("勾选：进入场景后自动开始本步骤。取消：由上一个步骤或流程管理器调用 Begin()。")]
        [SerializeField] protected bool autoStart = true;

        [Tooltip("本步骤完成时触发（例如把下一步 SetActive(true) 或调用它的 Begin()）")]
        public UnityEvent onStepCompleted = new UnityEvent();

        [Header("面板切换（完成时切到下一步）")]
        [Tooltip("本步骤完成后要激活的下一个面板")]
        [SerializeField] protected GameObject nextPanel;

        [Tooltip("本步骤所在面板，完成后失活")]
        [SerializeField] protected GameObject selfPanel;

        [Header("提示文案（分阶段）")]
        [Tooltip("对话框提示文字（DialogBox/Prompt 的 Text）")]
        [SerializeField] protected Text promptText;

        [Tooltip("进度文字（Progress 的 Text）")]
        [SerializeField] protected Text progressText;

        [Tooltip("分阶段的提示文案，按阶段顺序填，脚本在关键节点自动切换")]
        [SerializeField] protected string[] promptStages = new string[0];

        [Tooltip("进度文案格式，{0}=已涂 {1}=总数，如「进度：{0}/{1}」")]
        [SerializeField] protected string progressFormat = "进度：{0}/{1}";

        /// <summary>正在执行中</summary>
        public bool IsRunning { get; private set; }

        /// <summary>已完成（不会再重复执行）</summary>
        public bool IsCompleted { get; private set; }

        protected virtual void Start()
        {
            if (autoStart && !IsRunning && !IsCompleted) Begin();
        }

        /// <summary>
        /// 强制重置：把本步骤恢复到「未开始」状态，并允许重新 Begin。
        /// 同一物体在同一教学流程里做"两遍"（例如浅涂 + 加深）时使用。
        /// </summary>
        public void Reset()
        {
            IsRunning = false;
            IsCompleted = false;
            OnReset();
        }

        /// <summary>子类可以重写，做状态归零。</summary>
        protected virtual void OnReset() { }

        /// <summary>开始本步骤。重复调用会被忽略，可安全地被外部多次触发。</summary>
        public void Begin()
        {
            if (IsRunning || IsCompleted) return;
            IsRunning = true;
            OnBegin();
        }

        /// <summary>子类在这里做初始化：找引用、绑事件、播开场动画。</summary>
        protected abstract void OnBegin();

        /// <summary>子类在条件满足时调用，标记本步骤结束。</summary>
        protected void CompleteStep()
        {
            if (IsCompleted) return;
            IsCompleted = true;
            IsRunning = false;
            OnCompleted();
            onStepCompleted.Invoke();
        }

        protected virtual void OnCompleted() { }

        // ── 面板切换 ──────────────────────────────────────────────

        /// <summary>完成时调用：激活下一个面板并 Begin 它的主步骤脚本，再隐藏自己所在面板。</summary>
        protected void SwitchPanelIfNeeded()
        {
            if (nextPanel != null)
            {
                nextPanel.SetActive(true);
                // 主步骤脚本一般挂在面板根（或其直接子树）上；树枝等辅助脚本挂在更深子物体，取「最近」的那个即可
                var nextStep = nextPanel.GetComponentInChildren<StepBase>(true);
                if (nextStep != null) nextStep.Begin();
            }
            if (selfPanel != null) selfPanel.SetActive(false);
        }

        // ── 提示文案 ──────────────────────────────────────────────

        /// <summary>显示第 stageIndex 条阶段提示（越界或为空则忽略，不覆盖已有文案）。</summary>
        protected void SetPrompt(int stageIndex)
        {
            if (promptText == null) return;
            if (promptStages == null) return;
            if (stageIndex < 0 || stageIndex >= promptStages.Length) return;
            if (string.IsNullOrEmpty(promptStages[stageIndex])) return;
            promptText.text = promptStages[stageIndex];
        }

        /// <summary>直接设置提示文字。</summary>
        protected void SetPromptText(string text)
        {
            if (promptText != null && !string.IsNullOrEmpty(text)) promptText.text = text;
        }

        /// <summary>更新进度文字，如「进度：3/8」。</summary>
        protected void UpdateProgress(int done, int total)
        {
            if (progressText == null) return;
            progressText.text = string.Format(progressFormat, done, total);
        }
    }
}
