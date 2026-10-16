using System;
using UnityEngine;

namespace DrawFlower.Gameplay
{
    /// <summary>
    /// 一片叶子的一个脉络区域。
    /// 设计成 [Serializable] 的纯数据类，方便在 Inspector 里直接配 8 个槽位。
    /// </summary>
    [Serializable]
    public class VeinSlot
    {
        [Tooltip("仅用于显示/日志/体检。例：1-1 表示第一片叶子的第 1 个脉络。")]
        public string id = "";

        [Tooltip("这个脉络的判定区域（BoxCollider2D）。脚本会判鼠标光标是否落在它上面。")]
        public Collider2D area;

        [Header("两遍涂色对应的图片")]
        [Tooltip("第一遍（浅涂）显示的图片，初始 inactive。")]
        public GameObject lightImage;

        [Tooltip("第二遍（加深）显示的图片，叠加在 lightImage 上面。留空表示这一遍不画这个脉络。")]
        public GameObject deepImage;

        // 运行期状态（不进 Inspector）
        [NonSerialized] public bool isLightDone;
        [NonSerialized] public bool isDeepDone;

        public bool IsDone(bool isDeep)
        {
            return isDeep ? isDeepDone : isLightDone;
        }

        public void SetDone(bool isDeep, bool done)
        {
            if (isDeep) isDeepDone = done; else isLightDone = done;
        }
    }
}
