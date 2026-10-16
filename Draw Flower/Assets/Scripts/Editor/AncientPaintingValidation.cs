using System.Collections.Generic;
using System.Text;
using DrawFlower.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace DrawFlower.EditorTools
{
    /// <summary>
    /// 古画教学项目的「配置体检」。
    /// 向导窗口和构建前校验都调用这里，保证两边口径一致。
    /// </summary>
    public static class AncientPaintingValidation
    {
        public const string PrefabName = "ChooseInkPanel";
        public const string YesResourcePath = "UI/yes";
        public const string NoResourcePath = "UI/no";
        public const string PaletteObjectName = "Palette";
        public const int ExpectedInkCount = 5;

        public class Issue
        {
            public bool IsError;
            public string Message;
            public Object Context;

            public override string ToString() => (IsError ? "❌ " : "⚠️ ") + Message;
        }

        public static Issue Err(string message, Object context = null)
            => new Issue { IsError = true, Message = message, Context = context };

        public static Issue Warn(string message, Object context = null)
            => new Issue { IsError = false, Message = message, Context = context };

        // ── 扫描 ────────────────────────────────────────────────────

        /// <summary>在 root 下找 Palette，再取它的直接子物体里所有带 Button 的，顺序即从左到右。</summary>
        public static List<Button> FindInkButtons(GameObject root, string paletteName = PaletteObjectName)
        {
            var result = new List<Button>();
            if (root == null) return result;

            var palette = FindByName(root.transform, paletteName);
            Transform container = palette != null ? palette : root.transform;

            for (int i = 0; i < container.childCount; i++)
            {
                var b = container.GetChild(i).GetComponent<Button>();
                if (b != null) result.Add(b);
            }

            // 兜底：Palette 下没按钮，就在整棵子树里找名字以 Ink 开头的按钮
            if (result.Count == 0)
            {
                foreach (var b in root.GetComponentsInChildren<Button>(true))
                {
                    if (b.gameObject.name.StartsWith("Ink", System.StringComparison.OrdinalIgnoreCase))
                        result.Add(b);
                }
                result.Sort((a, c) => a.transform.GetSiblingIndex().CompareTo(c.transform.GetSiblingIndex()));
            }

            return result;
        }

        public static Transform FindByName(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name.Equals(name, System.StringComparison.OrdinalIgnoreCase)) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = FindByName(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        public static string FindPrefabPath(string prefabName = PrefabName)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab " + prefabName, new[] { "Assets" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(p)
                        .Equals(prefabName, System.StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            return null;
        }

        // ── 体检 ────────────────────────────────────────────────────

        public static List<Issue> ValidateAll()
        {
            var list = new List<Issue>();
            CheckFeedbackSprites(list);
            CheckPrefabSource(list);
            CheckSceneSteps(list);
            return list;
        }

        private static void CheckFeedbackSprites(List<Issue> list)
        {
            CheckSprite(list, YesResourcePath);
            CheckSprite(list, NoResourcePath);

            for (int i = 0; i < ExpectedInkCount; i++)
                CheckSprite(list, "UI/ink_" + i, required: false);
        }

        private static void CheckSprite(List<Issue> list, string resourcePath, bool required = true)
        {
            string assetPath = "Assets/Resources/" + resourcePath + ".png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);

            if (tex == null)
            {
                if (required)
                    list.Add(Err($"缺图片：{assetPath}（脚本按 Resources 路径 \"{resourcePath}\" 读取，文件名/位置必须完全一致）"));
                return;
            }

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null || importer.textureType != TextureImporterType.Sprite)
            {
                list.Add(Err($"{assetPath} 的 Texture Type 不是 Sprite，" +
                             "Resources.Load<Sprite> 会返回 null、反馈图就生成不出来。", tex));
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<Sprite>(assetPath) == null)
                list.Add(Err($"{assetPath} 没有生成 Sprite 子资源（Sprite Mode 需为 Single）。", tex));
        }

        private static void CheckPrefabSource(List<Issue> list)
        {
            string path = FindPrefabPath();
            if (path == null)
            {
                list.Add(Warn($"Assets 下没找到名为 {PrefabName} 的面板预制体。"));
                return;
            }

            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(path);
            }
            catch (System.Exception e)
            {
                list.Add(Err($"读取 {path} 失败：{e.Message}"));
                return;
            }

            try
            {
                var buttons = FindInkButtons(root);
                if (buttons.Count == 0)
                    list.Add(Err($"{path} 里没找到墨色按钮：{PaletteObjectName} 下需要有带 Button 的子物体。"));
                else if (buttons.Count != ExpectedInkCount)
                    list.Add(Warn($"{path} 里扫到 {buttons.Count} 个墨色按钮，正常应为 {ExpectedInkCount} 个。"));

                var step = root.GetComponentInChildren<InkColorStep>(true);
                if (step == null)
                {
                    list.Add(Warn($"{path} 还没挂 InkColorStep。点向导里的「一键配置」即可自动创建并完成设置。"));
                }
                else
                {
                    var so = new SerializedObject(step);
                    ValidateStepSerialized(so, step, path, buttons.Count, list);
                }
            }
            finally
            {
                if (root != null) PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void CheckSceneSteps(List<Issue> list)
        {
            var steps = UnityEngine.Object.FindObjectsOfType<InkColorStep>(true);
            if (steps == null || steps.Length == 0)
            {
                list.Add(Warn("当前打开的场景里没有 InkColorStep。如果场景里也没有 " +
                              $"{PrefabName} 预制体实例，运行游戏时不会有任何面板和逻辑。"));
                return;
            }

            foreach (var step in steps)
            {
                var so = new SerializedObject(step);
                ValidateStepSerialized(so, step, "场景", -1, list);
            }
        }

        private static void ValidateStepSerialized(SerializedObject so, InkColorStep step,
                                                   string where, int scannedButtonCount, List<Issue> list)
        {
            var arr = so.FindProperty("inkButtons");
            int configured = arr != null ? arr.arraySize : 0;

            if (configured == 0)
            {
                list.Add(Warn($"「{step.name}」({where}) 的 Ink Buttons 为空，运行时会自动扫描 Palette。" +
                              "建议点向导里的「一键配置」把引用写死，避免 Hierarchy 变动后错位。"));
            }
            else
            {
                for (int i = 0; i < configured; i++)
                {
                    var el = arr.GetArrayElementAtIndex(i);
                    if (el.objectReferenceValue == null)
                        list.Add(Err($"「{step.name}」({where}) 第 {i} 个墨色按钮引用为空（Missing）。", step));
                }
                if (configured != ExpectedInkCount)
                    list.Add(Warn($"「{step.name}」({where}) 配了 {configured} 个按钮，正常应为 {ExpectedInkCount} 个。", step));
            }

            int count = configured > 0 ? configured : scannedButtonCount;
            var ci = so.FindProperty("correctIndex");
            if (ci != null && count > 0 && (ci.intValue < 0 || ci.intValue >= count))
            {
                list.Add(Err($"「{step.name}」({where}) Correct Index = {ci.intValue} 超出范围 0~{count - 1}，" +
                             "点任何按钮都会判为「错」。", step));
            }

            var yes = so.FindProperty("yesResourcePath");
            var no = so.FindProperty("noResourcePath");
            if (yes != null && string.IsNullOrEmpty(yes.stringValue))
                list.Add(Err($"「{step.name}」({where}) Yes Resource Path 为空。", step));
            if (no != null && string.IsNullOrEmpty(no.stringValue))
                list.Add(Err($"「{step.name}」({where}) No Resource Path 为空。", step));
        }

        public static string Describe(List<Issue> issues)
        {
            int errors = 0, warnings = 0;
            foreach (var i in issues)
            {
                if (i.IsError) errors++;
                else warnings++;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"共 {errors} 个错误、{warnings} 个提示");
            if (errors == 0 && warnings == 0) sb.AppendLine("✅ 配置完好，可以直接运行。");
            foreach (var i in issues) sb.AppendLine(i.ToString());
            return sb.ToString();
        }
    }
}
