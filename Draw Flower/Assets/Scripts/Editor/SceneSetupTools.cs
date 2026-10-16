using System.Collections.Generic;
using System.Linq;
using DrawFlower.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DrawFlower.EditorTools
{
    /// <summary>
    /// 「把教学面板放进当前场景」 —— 这是新手最容易卡住的一步。
    ///
    /// 为什么单做一份：向导既会改预制体也会改场景实例，需要同一个「确保场景里有可运行实例」的工具，
    /// 否则向导配了半天，运行起来场景里空空如也，让人怀疑程序没生效。
    ///
    /// 使用方式：
    ///   - 顶部菜单 Tools → 古画教学 → 0⃣ 把教学面板放进 SampleScene
    ///   - 或 InkColorStepWizard 顶部那个大按钮
    /// </summary>
    public static class SceneSetupTools
    {
        public const string MenuPath = "Tools/古画教学/0⃣ 把教学面板放进 SampleScene";
        public const string FixInkStepPath = "Tools/古画教学/③ 修复场景里的墨色步骤（挂脚本+写引用）";
        public const string DiagnosePath = "Tools/古画教学/④ 把场景诊断打印到 Console";
        public const string LeavePanelPath = "Tools/古画教学/⑤ 把叶子面板放进 SampleScene";
        public const string GameFlowPath = "Tools/古画教学/⑥ 创建/修复 GameFlow 流程控制器";
        public const string DiagnoseLeafPath = "Tools/古画教学/⑦ 把叶子涂色诊断打印到 Console";
        public const string VeinAreasPath = "Tools/古画教学/⑧ 按 lightImage 位置给脉络加碰撞盒";

        public const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
        private const string LeavePrefabName = "LeavePanel";

        /// <summary>
        /// 确保当前打开的 SampleScene 里有一个挂好 InkColorStep 的教学面板实例。
        /// 调用一次就够了；再点也只是选中已经放好的对象，不会重复创建。
        /// </summary>
        [MenuItem(MenuPath, false, 0)]
        public static void EnsureTutorialCanvasInScene()
        {
            // 1. 把 SampleScene 切换为当前打开的场景（用户可能停在别的场景里）
            var active = SceneManager.GetActiveScene();
            if (active.path != SampleScenePath)
            {
                if (!System.IO.File.Exists(SampleScenePath))
                {
                    EditorUtility.DisplayDialog("古画教学",
                        $"项目里找不到 {SampleScenePath}，\n先在 Unity 里至少保存过一次这个场景再试。", "好");
                    return;
                }

                bool ok = EditorUtility.DisplayDialog("古画教学",
                    "当前打开的不是 SampleScene，要先把 " + SampleScenePath + " 打开再放教学面板。\n继续吗？",
                    "打开并继续", "取消");
                if (!ok) return;

                if (active.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;

                EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
            }

            // 2. 看场景里是不是已经有了能跑的实例
            var existing = FindExistingTutorialRoot();
            if (existing != null)
            {
                Selection.activeGameObject = existing;
                EditorGUIUtility.PingObject(existing);
                EnsureInkStepAttached(existing, force: true);
                EditorUtility.DisplayDialog("古画教学",
                    "SampleScene 里已经有一个教学面板实例，无需重复放入。\n" +
                    "已选中「" + existing.name + "」。\n\n" +
                    "Palette 上的 InkColorStep 已检查并补齐，按钮引用已重新写好。\n" +
                    "▶ Play 应该可以响应了。",
                    "好");
                return;
            }

            // 3. 实例化预制体
            string prefabPath = AncientPaintingValidation.FindPrefabPath();
            if (prefabPath == null)
            {
                EditorUtility.DisplayDialog("古画教学",
                    "在 Assets 下找不到名为 " + AncientPaintingValidation.PrefabName + " 的预制体。\n" +
                    "这一步要求 UI 面板已经做好。", "好");
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("古画教学",
                    $"找到了 {prefabPath} 但加载失败，重新打开 Unity 再试。", "好");
                return;
            }

            var scene = SceneManager.GetActiveScene();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            if (instance == null)
            {
                EditorUtility.DisplayDialog("古画教学", "实例化失败，重试或重新打开 Unity。", "好");
                return;
            }

            Undo.RegisterCreatedObjectUndo(instance, "放入教学面板");

            // 4. 立刻挂上 InkColorStep + 写好引用，再保存
            EnsureInkStepAttached(instance, force: true);

            // 4.5 关键：场景里必须要有 EventSystem，否则所有 UI 按钮的点击根本没路由 → "按了没反应"
            EnsureEventSystemInScene(scene);

            // 5. 落盘
            Selection.activeGameObject = instance;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, SampleScenePath);

            EditorUtility.DisplayDialog("古画教学",
                $"已经完成：\n" +
                $"  1. 打开 SampleScene\n" +
                $"  2. 把 {prefab.name} 实例化进场景\n" +
                $"  3. Palette 上挂好 InkColorStep，按钮引用和默认值都写好\n" +
                $"  4. 场景已保存\n\n" +
                $"现在点 Unity 顶部的 ▶ Play 就能玩。",
                "好");
        }

        /// <summary>
        /// 【修复菜单】不管场景里现在什么状态：把 InkColorStep 强挂到 Palette 上，按钮引用扫一遍。
        /// 解决"Hierarchy 看上去都齐，但点墨色没反应"的漏油情况。
        /// </summary>
        [MenuItem(FixInkStepPath, false, 3)]
        public static void FixInkStepMenu()
        {
            var root = FindExistingTutorialRoot();
            if (root == null)
            {
                EditorUtility.DisplayDialog("古画教学",
                    "场景里还没找到教学面板实例。请先点菜单 0⃣。", "好");
                return;
            }
            EnsureInkStepAttached(root, force: true);

            var scene = root.scene;
            EnsureEventSystemInScene(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            if (scene.path == SampleScenePath)
                EditorSceneManager.SaveScene(scene, SampleScenePath);

            var palette = FindPalette(root);
            Selection.activeGameObject = palette != null ? palette.gameObject : root;
            EditorGUIUtility.PingObject(Selection.activeGameObject);

            EditorUtility.DisplayDialog("古画教学",
                "已挂好 InkColorStep 并写入按钮引用、默认值、效果参数。\n\n" +
                "已确保场景里有 EventSystem（没有它的话所有 UI 点击都不会响应）。\n" +
                "正确答案已经设为「Ink3」（按 Hierarchy 顺序第 4 个，从左数）。\n\n" +
                "▶ Play 一下试试。", "好");
        }

        /// <summary>
        /// 把 InkColorStep 挂到 Palette 上并写好全部默认值 / 按钮引用。
        /// 这函数被多处复用：放进场景时 / 用户叫"修复"时 / 体检里发现 InkColorStep 缺了时。
        /// </summary>
        /// <param name="root">教学面板根（含 Palette 子物体的那一个）</param>
        /// <param name="force">true=无视 Palette 上是否已挂，挂新一份；false=已有就跳过</param>
        public static void EnsureInkStepAttached(GameObject root, bool force)
        {
            if (root == null) return;
            var palette = FindPalette(root);
            if (palette == null)
            {
                Debug.LogWarning("[SceneSetupTools] 找不到 Palette 子物体，跳过挂脚本。", root);
                return;
            }

            var host = palette.gameObject;
            var step = host.GetComponent<InkColorStep>();
            if (step == null)
            {
                step = Undo.AddComponent<InkColorStep>(host);
                Debug.Log("[SceneSetupTools] 已给 " + host.name + " 挂上 InkColorStep。", host);
            }
            else if (!force)
            {
                return;
            }

            // 把按钮按名字里的数字排序（Ink0 < Ink1 < ... < Ink4），而不是按 Hierarchy 顺序。
            // 否则用户在 Hierarchy 里拖动过按钮、顺序乱了之后，correctIndex 会对不上正确答案。
            var orderedChildren = Enumerable.Range(0, palette.childCount)
                .Select(i => palette.GetChild(i))
                .Where(t => t.GetComponent<Button>() != null)
                .OrderBy(t =>
                {
                    var m = System.Text.RegularExpressions.Regex.Match(t.name, @"(\d+)");
                    return m.Success ? int.Parse(m.Value) : int.MaxValue;
                })
                .ToList();

            var so = new SerializedObject(step);
            var arr = so.FindProperty("inkButtons");
            arr.arraySize = orderedChildren.Count;
            for (int i = 0; i < orderedChildren.Count; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = orderedChildren[i].GetComponent<Button>();

            so.FindProperty("buttonContainerName").stringValue = AncientPaintingValidation.PaletteObjectName;

            // 默认正确答案是 Ink3（按名字排序后 index=3）。用户在 Inspector 里随时可以改。
            so.FindProperty("correctIndex").intValue = 3;

            so.ApplyModifiedProperties();

            // 如果是 PrefabInstance，把改动也写进 override，免得 LoadScene 后丢
            if (PrefabUtility.IsPartOfPrefabInstance(host))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(step);
            }
        }

        /// <summary>
        /// 【诊断】把场景里 TutorialCanvas / Palette / InkColorStep 的详细情况写到 Console。
        /// 不知道哪里坏了？先点这个，再回头粘控制台输出给我。
        /// </summary>
        [MenuItem(DiagnosePath, false, 4)]
        public static void Diagnose()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== 古画教学 · 场景诊断 ===");

            var scene = SceneManager.GetActiveScene();
            sb.AppendLine($"当前场景: name='{scene.name}' path='{scene.path}' rootCount={scene.rootCount}");

            var root = FindExistingTutorialRoot();
            if (root == null)
            {
                sb.AppendLine("❌ 场景里没有含 Palette 子物体的根 —— 请先点菜单 0⃣ 把面板放进 SampleScene。");
                Debug.LogError(sb.ToString());
                return;
            }

            sb.AppendLine($"找到教学根: {root.name} (fileID={root.GetInstanceID()})");

            bool inPrefab = PrefabUtility.IsPartOfPrefabInstance(root);
            sb.AppendLine($"是否 PrefabInstance: {inPrefab} (如果为 false，运行起来任何 Editor 下做的修改都不会跟着预制体同步)");

            var palette = FindPalette(root);
            if (palette == null)
            {
                sb.AppendLine("❌ 根里有但没找到名为 " + AncientPaintingValidation.PaletteObjectName + " 的子物体！");
            }
            else
            {
                sb.AppendLine($"Palette: {palette.gameObject.name}, 直接子物体 {palette.childCount} 个");

                var step = palette.GetComponent<InkColorStep>();
                if (step == null)
                {
                    sb.AppendLine("❌ Palette 上没有 InkColorStep 组件 —— 这就是你点了没反应的原因之一。请点菜单 ③ 修复。");
                }
                else
                {
                    sb.AppendLine($"InkColorStep: 已挂（fileID={step.GetInstanceID()}）");
                    var so = new SerializedObject(step);
                    var arr = so.FindProperty("inkButtons");
                    int configured = arr != null ? arr.arraySize : 0;
                    sb.AppendLine($"  InkButtons: 配置了 {configured} 个");
                    for (int i = 0; i < configured; i++)
                    {
                        var el = arr.GetArrayElementAtIndex(i);
                        var b = el.objectReferenceValue as Button;
                        sb.AppendLine($"    [{i}] {(b == null ? "(Missing)" : b.gameObject.name)}");
                    }
                    var ci = so.FindProperty("correctIndex");
                    sb.AppendLine($"  CorrectIndex={ci?.intValue}");
                    var yp = so.FindProperty("yesResourcePath").stringValue;
                    var np = so.FindProperty("noResourcePath").stringValue;
                    sb.AppendLine($"  YesResourcePath='{yp}' NoResourcePath='{np}'");
                }

                var hlg = palette.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                sb.AppendLine($"Palette 上 HorizontalLayoutGroup: {(hlg != null ? "有" : "无")}");
            }

            sb.AppendLine();
            sb.AppendLine("--- EventSystem（缺了它所有 UI 点击都没反应） ---");
            var existingEs = FindExistingEventSystem(scene);
            if (existingEs != null)
            {
                sb.AppendLine($"✓ EventSystem: {existingEs.name}");
                var im = existingEs.GetComponent<UnityEngine.EventSystems.BaseInputModule>();
                sb.AppendLine($"  InputModule: {(im != null ? im.GetType().Name : "❌ 无！")}");
            }
            else
            {
                sb.AppendLine("❌ 场景里没有 EventSystem —— 这是你点击墨色按钮没反应的真正原因。");
                sb.AppendLine("   请点菜单 ③ 修复场景，它会顺手帮你建一个。");
            }

            sb.AppendLine();
            sb.AppendLine("--- Ink* 按钮 ---");
            foreach (Transform t in palette)
            {
                var b = t.GetComponent<Button>();
                var img = t.GetComponent<Image>();
                sb.AppendLine($"  {t.name}  Button={(b != null ? "有" : "无")} Image={(img != null ? "有" : "无")}");
            }

            sb.AppendLine();
            sb.AppendLine("--- Resources/UI 图片 ---");
            for (int i = 0; i < AncientPaintingValidation.ExpectedInkCount; i++)
                sb.AppendLine($"  UI/ink_{i}.png 存在? {System.IO.File.Exists("Assets/Resources/UI/ink_" + i + ".png")}");
            sb.AppendLine($"  UI/yes.png 存在? {System.IO.File.Exists("Assets/Resources/UI/yes.png")}");
            sb.AppendLine($"  UI/no.png 存在? {System.IO.File.Exists("Assets/Resources/UI/no.png")}");

            Debug.Log(sb.ToString());
            EditorUtility.DisplayDialog("古画教学 · 诊断",
                "诊断信息已写到 Console（Window → General → Console）。\n\n" +
                "粘贴给我，我直接看问题在哪。", "好");
        }

        /// <summary>
        /// 【关键】场景里必须有一个 EventSystem + InputModule，否则所有 UI 按钮的点击都没路由，
        /// 画面上能看到按钮但点了完全没反应。这是新手最常踩的坑。
        /// 已经在的话什么也不做；缺了则 create EventSystem + StandaloneInputModule + 保存。
        /// </summary>
        public static GameObject EnsureEventSystemInScene(Scene scene)
        {
            if (!scene.IsValid()) return null;

            var existing = FindExistingEventSystem(scene);
            if (existing != null) return existing;

            var go = new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.EventSystems.StandaloneInputModule));
            SceneManager.MoveGameObjectToScene(go, scene);
            Undo.RegisterCreatedObjectUndo(go, "创建 EventSystem");
            Debug.Log("[SceneSetupTools] 已创建 EventSystem。", go);
            return go;
        }

        public static GameObject FindExistingEventSystem(Scene scene)
        {
            if (!scene.IsValid()) return null;
            foreach (var root in scene.GetRootGameObjects())
            {
                var es = root.GetComponent<UnityEngine.EventSystems.EventSystem>();
                if (es != null) return root;
            }
            return null;
        }

        /// <summary>
        /// 在当前打开的场景里找已经存在的教学面板根：含 Palette 子物体的物体。
        /// </summary>
        public static GameObject FindExistingTutorialRoot()
        {
            return FindExistingTutorialRoot(SceneManager.GetActiveScene());
        }

        public static GameObject FindExistingTutorialRoot(Scene scene)
        {
            if (!scene.IsValid()) return null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (FindPalette(root) != null) return root;
            }
            return null;
        }

        public static Transform FindPalette(GameObject root)
        {
            if (root == null) return null;
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == AncientPaintingValidation.PaletteObjectName);
        }

        // ── 叶子面板 LeavePanel ─────────────────────────────────────

        /// <summary>
        /// 在场景里找第一片叶子面板（LeavePanel1）的实例。
        /// </summary>
        public static GameObject FindLeavePanelRoot()
        {
            return FindLeaveRoot("LeavePanel1");
        }

        /// <summary>
        /// 按面板名字找「叶子面板」目标：优先场景实例（名字匹配），找不到就返回预制体资产。
        /// 这样配置工具既能改场景里正在运行的那个，也能直接改预制体。
        /// </summary>
        public static GameObject FindLeaveRoot(string name)
        {
            var all = Object.FindObjectsOfType<LeafPaintStep>(true);
            foreach (var lp in all)
            {
                if (lp.name == name || lp.name.Contains(name)) return lp.gameObject;
            }
            string path = FindLeavePrefabPath(name);
            if (path != null) return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return null;
        }

        /// <summary>找叶子面板预制体（精确名优先，如 LeavePanel1 / LeavePanel2）。</summary>
        public static string FindLeavePrefabPath(string nameFilter = "LeavePanel1")
        {
            // 1) 精确匹配文件名
            string[] all = AssetDatabase.FindAssets("t:Prefab");
            foreach (var g in all)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == nameFilter) return p;
            }
            // 2) 名字模糊（FindAssets 的 name 过滤）
            var guids = AssetDatabase.FindAssets("t:Prefab " + nameFilter);
            if (guids.Length > 0) return AssetDatabase.GUIDToAssetPath(guids[0]);
            // 3) 兜底：含 leave 的预制体
            foreach (var g in all)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (p.ToLower().Contains("leave")) return p;
            }
            return null;
        }

        [MenuItem(LeavePanelPath, false, 5)]
        public static void EnsureLeavePanelInScene()
        {
            // 切场景
            var active = SceneManager.GetActiveScene();
            if (active.path != SampleScenePath)
            {
                if (!System.IO.File.Exists(SampleScenePath))
                {
                    EditorUtility.DisplayDialog("古画教学",
                        $"项目里找不到 {SampleScenePath}，\n先在 Unity 里至少保存过一次这个场景再试。", "好");
                    return;
                }
                bool ok = EditorUtility.DisplayDialog("古画教学",
                    "当前打开的不是 SampleScene，要先把 " + SampleScenePath + " 打开再放叶子面板。\n继续吗？",
                    "打开并继续", "取消");
                if (!ok) return;
                if (active.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
            }

            var existing = FindLeavePanelRoot();
            if (existing != null)
            {
                Selection.activeGameObject = existing;
                EditorGUIUtility.PingObject(existing);
                EditorUtility.DisplayDialog("古画教学",
                    "场景里已经有 LeavePanel 实例，无需重复放入。\n" +
                    "已选中「" + existing.name + "」。\n" +
                    "现在用 ⑨ 向导配置 8 个脉络槽位。", "好");
                return;
            }

            string prefabPath = FindLeavePrefabPath();
            if (prefabPath == null)
            {
                EditorUtility.DisplayDialog("古画教学",
                    "在 Assets 下找不到名为 " + LeavePrefabName + " 的预制体。\n" +
                    "请先在 Unity 里把这个预制体建好，结构是：根 Canvas + 一个挂着 LeafPaintStep 的子物体 + 8 个带 Collider2D 的脉络 area。", "好");
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("古画教学",
                    $"找到了 {prefabPath} 但加载失败，重新打开 Unity 再试。", "好");
                return;
            }

            var scene = SceneManager.GetActiveScene();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            if (instance == null)
            {
                EditorUtility.DisplayDialog("古画教学", "实例化失败，重试或重新打开 Unity。", "好");
                return;
            }
            Undo.RegisterCreatedObjectUndo(instance, "放入叶子面板");

            // 顺手挂脚本（如果预制体没挂）
            var step = instance.GetComponentInChildren<LeafPaintStep>(true);
            if (step == null)
            {
                step = Undo.AddComponent<LeafPaintStep>(instance);
            }

            EnsureEventSystemInScene(scene);

            Selection.activeGameObject = instance;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, SampleScenePath);

            EditorUtility.DisplayDialog("古画教学",
                $"已经完成：\n" +
                $"  1. 打开 SampleScene\n" +
                $"  2. 把 {prefab.name} 实例化进场景\n" +
                $"  3. LeafPaintStep 已挂上\n" +
                $"  4. 场景已保存\n\n" +
                $"下一步：用 ⑨ 配置向导把 8 个脉络的 area / 图片 / next 按钮填好。", "好");
        }

        // ── GameFlow 控制器 ────────────────────────────────────────

        [MenuItem(GameFlowPath, false, 6)]
        public static void EnsureGameFlowInScene()
        {
            var active = SceneManager.GetActiveScene();
            if (active.path != SampleScenePath)
            {
                if (!System.IO.File.Exists(SampleScenePath))
                {
                    EditorUtility.DisplayDialog("古画教学", "请先打开 " + SampleScenePath + "。", "好");
                    return;
                }
                bool ok = EditorUtility.DisplayDialog("古画教学",
                    "当前打开的不是 SampleScene，要先把 " + SampleScenePath + " 打开再建 GameFlow。\n继续吗？",
                    "打开并继续", "取消");
                if (!ok) return;
                if (active.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
            }

            var existing = Object.FindObjectOfType<GameFlow>(true);
            if (existing != null)
            {
                // 重新写 steps 引用
                AutoWireGameFlow(existing);
                Selection.activeGameObject = existing.gameObject;
                EditorSceneManager.MarkSceneDirty(active);
                EditorSceneManager.SaveScene(active, SampleScenePath);
                EditorUtility.DisplayDialog("古画教学",
                    "GameFlow 已存在。已自动重写 steps 引用：\n" +
                    "  [0] 墨色步骤（InkColorStep 所在物体）\n" +
                    "  [1] 第一片叶子（LeavePanel1）\n" +
                    "  [2] 第二片叶子（LeavePanel2，若已放进场景）\n\n" +
                    "你也可以在 Inspector 里手动调整顺序。", "好");
                return;
            }

            // 新建
            var go = new GameObject("GameFlow");
            Undo.RegisterCreatedObjectUndo(go, "创建 GameFlow");
            SceneManager.MoveGameObjectToScene(go, active);
            var flow = Undo.AddComponent<GameFlow>(go);
            AutoWireGameFlow(flow);

            EditorSceneManager.MarkSceneDirty(active);
            EditorSceneManager.SaveScene(active, SampleScenePath);

            Selection.activeGameObject = go;
            EditorUtility.DisplayDialog("古画教学",
                "已创建 GameFlow 并自动写好步骤引用。\n" +
                "▶ Play 即可按顺序：① 选墨色 → ② 第一片叶子 → ③ 第二片叶子。", "好");
        }

        private static void AutoWireGameFlow(GameFlow flow)
        {
            if (flow == null) return;
            var inkRoot = FindExistingTutorialRoot();
            // GameFlow 只需要「场景里的实例」（要 SetActive 切换显示），不能用预制体资产
            var leaf1Root = FindLeaveRootInScene("LeavePanel1");
            var leaf2Root = FindLeaveRootInScene("LeavePanel2");

            var stepsField = typeof(GameFlow).GetField("steps",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (stepsField == null) return;

            var listType = typeof(GameFlow).GetNestedType("FlowStep");
            int count = 1 + (leaf1Root != null ? 1 : 0) + (leaf2Root != null ? 1 : 0);   // 墨色 + 叶子1 + 叶子2
            var array = System.Array.CreateInstance(listType, count);

            // step 0：墨色
            var s0 = System.Activator.CreateInstance(listType);
            listType.GetField("container").SetValue(s0, inkRoot);
            listType.GetField("step").SetValue(s0, inkRoot != null ? inkRoot.GetComponentInChildren<StepBase>(true) : null);
            array.SetValue(s0, 0);

            // step 1：第一片叶子
            var s1 = System.Activator.CreateInstance(listType);
            listType.GetField("container").SetValue(s1, leaf1Root);
            listType.GetField("step").SetValue(s1, leaf1Root != null ? leaf1Root.GetComponentInChildren<StepBase>(true) : null);
            array.SetValue(s1, 1);

            // step 2：第二片叶子
            if (leaf2Root != null)
            {
                var s2 = System.Activator.CreateInstance(listType);
                listType.GetField("container").SetValue(s2, leaf2Root);
                listType.GetField("step").SetValue(s2, leaf2Root.GetComponentInChildren<StepBase>(true));
                array.SetValue(s2, 2);
            }

            stepsField.SetValue(flow, array);
            EditorUtility.SetDirty(flow);
        }

        /// <summary>只在场景里找名字匹配的叶子面板实例（不 fallback 到预制体资产）。</summary>
        public static GameObject FindLeaveRootInScene(string name)
        {
            foreach (var lp in Object.FindObjectsOfType<LeafPaintStep>(true))
            {
                if (lp.name == name || lp.name.Contains(name)) return lp.gameObject;
            }
            return null;
        }

        // ── 叶子涂色诊断 ──────────────────────────────────────────

        [MenuItem(DiagnoseLeafPath, false, 7)]
        public static void DiagnoseLeafPanel()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== 古画教学 · 叶子涂色 诊断 ===");

            var root = FindLeavePanelRoot();
            if (root == null)
            {
                sb.AppendLine("❌ 场景里没找到 LeafPaintStep。请先点菜单 ⑤ 把叶子面板放进 SampleScene。");
                Debug.LogError(sb.ToString());
                EditorUtility.DisplayDialog("古画教学 · 诊断", "诊断信息已写到 Console。\n没有找到 LeafPaintStep。", "好");
                return;
            }
            sb.AppendLine($"找到叶子面板根: {root.name}");

            var step = root.GetComponentInChildren<LeafPaintStep>(true);
            sb.AppendLine($"LeafPaintStep: {(step != null ? "已挂" : "❌ 未挂")}");
            if (step == null) { Debug.LogError(sb.ToString()); return; }

            var so = new SerializedObject(step);
            var arr = so.FindProperty("veins");
            sb.AppendLine($"Vein 槽位数: {arr.arraySize}");
            int withArea = 0, withLight = 0, withDeep = 0;
            for (int i = 0; i < arr.arraySize; i++)
            {
                var el = arr.GetArrayElementAtIndex(i);
                var id = el.FindPropertyRelative("id")?.stringValue ?? "?";
                var area = el.FindPropertyRelative("area")?.objectReferenceValue;
                var lightImg = el.FindPropertyRelative("lightImage")?.objectReferenceValue;
                var deepImg = el.FindPropertyRelative("deepImage")?.objectReferenceValue;
                if (area != null) withArea++;
                if (lightImg != null) withLight++;
                if (deepImg != null) withDeep++;
                sb.AppendLine($"  [{i}] id='{id}' area={(area != null ? "✓" : "✗")} light={(lightImg != null ? "✓" : "✗")} deep={(deepImg != null ? "✓" : "✗")}");
            }
            sb.AppendLine($"  统计: area {withArea}/{arr.arraySize}, light {withLight}/{arr.arraySize}, deep {withDeep}/{arr.arraySize}");

            var nb = so.FindProperty("nextButton")?.objectReferenceValue;
            sb.AppendLine($"Next 按钮: {(nb != null ? "已配" : "❌ 未配")}");

            // GameFlow
            var flow = Object.FindObjectOfType<GameFlow>(true);
            if (flow == null)
                sb.AppendLine("⚠️ 场景里没有 GameFlow —— 流程不会自动切换。请点菜单 ⑥ 创建。");
            else
                sb.AppendLine($"GameFlow: 已建，CurrentIndex={flow.CurrentIndex}");

            Debug.Log(sb.ToString());
            EditorUtility.DisplayDialog("古画教学 · 诊断",
                "诊断信息已写到 Console。\n" +
                (withArea < arr.arraySize ? "❌ 有脉络没配 area 碰撞盒。\n" : "✓ 脉络 area 齐全。\n") +
                (withLight < arr.arraySize ? "❌ 有脉络没配 lightImage。\n" : "✓ lightImage 齐全。\n") +
                (nb == null ? "❌ Next 按钮未配。\n" : "✓ Next 按钮已配。\n"),
                "好");
        }

        // ── 脉络 area 碰撞盒（按 lightImage 位置）────────────────────

        [MenuItem(VeinAreasPath, false, 8)]
        public static void EnsureVeinAreasMenu()
        {
            var root = FindLeavePanelRoot();
            GameObject target;
            if (root != null)
            {
                target = root;
            }
            else
            {
                // 场景里没实例，改去操作预制体资产
                string p = FindLeavePrefabPath();
                if (p == null)
                {
                    EditorUtility.DisplayDialog("古画教学",
                        "场景里没有 LeavePanel，也没有 LeavePanel.prefab。\n先把面板预制体建好再试。", "好");
                    return;
                }
                target = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            }
            EnsureVeinAreas(target);
        }

        /// <summary>
        /// 在 leaveRoot 下，给名字匹配某种规律的 GameObject（默认 1-1~1-8）旁边各建一个
        /// 「Area_&lt;同名&gt;」兄弟节点，挂 BoxCollider2D，RectTransform 和 Collider 完全照抄源图。
        ///
        /// 为什么单独建兄弟节点而不是把 collider 挂到 1-X 自己身上：
        /// lightImage 初始会 SetActive(false)，而挂在失活 GameObject 上的 Collider2D
        /// 不参与 Physics2D.OverlapPoint 查询 —— 点了没反应。兄弟节点始终激活，
        /// 所以浅涂之前、之后、整个生命周期都能命中。
        /// </summary>
        public static (int created, int updated, int missing) EnsureVeinAreas(GameObject leaveRoot, bool showDialog = true, string onlyPrefix = null, bool skipExisting = false)
        {
            if (leaveRoot == null) return (0, 0, 0);

            int created = 0, updated = 0, missing = 0;
            foreach (var imgTf in leaveRoot.GetComponentsInChildren<RectTransform>(true))
            {
                if (imgTf == null) continue;
                string id = imgTf.gameObject.name;

                // 默认匹配 1-1 这种带连字符的脉络 id；用户也可以手填 area，工具只补缺的部分
                if (!LooksLikeVeinId(id)) continue;

                // 只处理指定前缀（多层共用碰撞体时，只给第一层生成）
                if (onlyPrefix != null && !id.StartsWith(onlyPrefix + "-")) continue;

                var parent = imgTf.parent;
                if (parent == null) continue;

                string areaName = "Area_" + id;

                Transform existing = parent.Find(areaName);

                // skipExisting：用户手动调过碰撞盒位置后，别再覆盖回去 —— 已存在就直接跳过
                if (skipExisting && existing != null) continue;

                GameObject areaGo;
                RectTransform areaRect;
                bool isNew = false;

                if (existing != null)
                {
                    areaGo = existing.gameObject;
                    areaRect = areaGo.GetComponent<RectTransform>();
                    if (areaRect == null) areaRect = Undo.AddComponent<RectTransform>(areaGo);
                }
                else
                {
                    areaGo = new GameObject(areaName, typeof(RectTransform));
                    Undo.RegisterCreatedObjectUndo(areaGo, "Create area " + areaName);
                    areaRect = areaGo.GetComponent<RectTransform>();
                    isNew = true;
                }

                // ★ 必须先挂到父物体下，再抄 RectTransform 字段。
                //   反过来的话，SetTransformParent 默认「保持世界坐标」，会反向改写 anchoredPosition，
                //   把我们刚抄好的坐标搅成远离子物体的垃圾值（曾经出现 Area 全部飞到画面左下角）。
                if (isNew)
                {
                    Undo.SetTransformParent(areaRect, parent, "放置 area 到画布");
                }

                Undo.RecordObject(areaRect, "同步 area rect");
                areaRect.anchorMin = imgTf.anchorMin;
                areaRect.anchorMax = imgTf.anchorMax;
                areaRect.pivot = imgTf.pivot;
                areaRect.sizeDelta = imgTf.sizeDelta;
                areaRect.anchoredPosition = imgTf.anchoredPosition;
                areaRect.localScale = imgTf.localScale;
                areaRect.localRotation = imgTf.localRotation;

                // BoxCollider2D
                var bc = areaGo.GetComponent<BoxCollider2D>();
                if (bc == null)
                {
                    bc = Undo.AddComponent<BoxCollider2D>(areaGo);
                }
                Undo.RecordObject(bc, "同步 area collider");
                // 用图片 alpha 包围盒（非透明像素范围）设置碰撞体，去掉四周透明边距，
                // 让 BoxCollider2D 更贴近花瓣花萼的形状。读不到 alpha 就退回整个 RectTransform。
                var bounds = GetAlphaBounds(imgTf);
                bc.offset = bounds.center;
                bc.size = bounds.size;
                bc.isTrigger = false;
                bc.usedByEffector = false;
                bc.enabled = true;

                // 确保 active（即使 1-X 是失活的，area 也得保持激活，Physics 查询才能命中）
                if (!areaGo.activeSelf) Undo.RecordObject(areaGo, "激活 area");
                areaGo.SetActive(true);

                if (isNew) created++; else updated++;
            }
            missing = 0;

            // 保存：预制体 / 场景实例走两条不同的路
            bool isPrefabAsset = PrefabUtility.IsPartOfPrefabAsset(leaveRoot);
            if (isPrefabAsset)
            {
                EditorUtility.SetDirty(leaveRoot);
                AssetDatabase.SaveAssetIfDirty(leaveRoot);
            }
            else
            {
                EditorSceneManager.MarkSceneDirty(leaveRoot.scene);
                if (leaveRoot.scene.path == SampleScenePath)
                    EditorSceneManager.SaveScene(leaveRoot.scene, SampleScenePath);
            }

            Debug.Log($"[脉络 area] 创建 {created} 个、更新 {updated} 个（目标根：{leaveRoot.name}, 类型：{(isPrefabAsset ? "预制体资产" : "场景实例")}）。");
            if (showDialog)
                EditorUtility.DisplayDialog("古画教学",
                    $"脉络 area 已就位：\n  新建 {created} 个 Area_*，\n  更新 {updated} 个 Area_*。\n\n" +
                    "以后 1-X 图位置改了，重新点这个菜单即可同步。", "好");

            return (created, updated, missing);
        }

        /// <summary>判断一个 GameObject 名字是不是脉络 id（默认支持「1-1」/「1-2」/「2-3」这种）。</summary>
        public static bool LooksLikeVeinId(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.StartsWith("Area_")) return false;        // 别把已经建好的 area 又当成 id
            int dash = name.IndexOf('-');
            if (dash <= 0 || dash >= name.Length - 1) return false;
            int leaf, vein;
            return int.TryParse(name.Substring(0, dash), out leaf)
                && int.TryParse(name.Substring(dash + 1), out vein);
        }

        /// <summary>
        /// 读图片的 alpha 通道，算出非透明像素的包围盒，映射到 RectTransform local 坐标。
        /// 用于让 BoxCollider2D「去透明边距」，更贴近花瓣花萼形状。读不到 alpha 就退回整个 rect。
        /// 假设：pivot=0.5、preserveAspect=false（sprite 拉伸填满 sizeDelta）。
        /// </summary>
        private static Rect GetAlphaBounds(RectTransform imgTf)
        {
            var img = imgTf.GetComponent<Image>();
            if (img == null || img.sprite == null) return imgTf.rect;

            string path = AssetDatabase.GetAssetPath(img.sprite);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
                return imgTf.rect;

            try
            {
                byte[] data = System.IO.File.ReadAllBytes(path);
                var tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tmp.LoadImage(data)) { Object.DestroyImmediate(tmp); return imgTf.rect; }

                int w = tmp.width, h = tmp.height;
                Color[] px = tmp.GetPixels();
                int minx = w, miny = h, maxx = -1, maxy = -1;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        if (px[y * w + x].a > 0.03f)
                        {
                            if (x < minx) minx = x;
                            if (x > maxx) maxx = x;
                            if (y < miny) miny = y;
                            if (y > maxy) maxy = y;
                        }
                    }
                }
                Object.DestroyImmediate(tmp);
                if (maxx < 0) return imgTf.rect;   // 全透明，退回 rect

                float Sx = imgTf.rect.width;
                float Sy = imgTf.rect.height;
                float cx = ((minx + maxx) / 2f / w - 0.5f) * Sx;
                float cy = ((miny + maxy) / 2f / h - 0.5f) * Sy;
                float sw = (maxx - minx + 1f) / w * Sx;
                float sh = (maxy - miny + 1f) / h * Sy;
                return new Rect(cx - sw / 2f, cy - sh / 2f, sw, sh);
            }
            catch
            {
                return imgTf.rect;
            }
        }

        // ── 叶子涂色 · 一键全自动配置（不依赖 id，按名字数字排序对齐）────────────────

        public const string AutoConfigLeafPath = "Tools/古画教学/⑩ 叶子涂色 · 一键全自动配置";

        [MenuItem(AutoConfigLeafPath, false, 10)]
        public static void AutoConfigureLeafMenu()
        {
            GameObject target = FindLeavePanelRoot();
            if (target == null)
            {
                string p = FindLeavePrefabPath();
                if (p == null)
                {
                    EditorUtility.DisplayDialog("古画教学", "找不到 LeavePanel（场景里没有、也没有预制体）。\n先做面板或点菜单 ⑤ 放进场景。", "好");
                    return;
                }
                target = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            }
            AutoConfigureLeafStep(target);
        }

        // ── 第二片叶子 LeavePanel2 · 一键就位 ──────────────────────────

        public const string SetupLeaf2Path = "Tools/古画教学/⑪ 配置第二片叶子（放场景+碰撞盒+脚本）";

        [MenuItem(SetupLeaf2Path, false, 11)]
        public static void SetupLeavePanel2()
        {
            var active = SceneManager.GetActiveScene();
            if (active.path != SampleScenePath)
            {
                if (!System.IO.File.Exists(SampleScenePath))
                {
                    EditorUtility.DisplayDialog("古画教学", "请先打开 " + SampleScenePath + "。", "好");
                    return;
                }
                if (!EditorUtility.DisplayDialog("古画教学",
                    "当前不是 SampleScene，要先打开它再配置第二片叶子。\n继续吗？", "打开并继续", "取消"))
                    return;
                if (active.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
            }

            // 找 LeavePanel2 场景实例，没有就实例化预制体
            GameObject leaf2 = null;
            foreach (var lp in Object.FindObjectsOfType<LeafPaintStep>(true))
            {
                if (lp.name == "LeavePanel2" || lp.name.Contains("LeavePanel2")) { leaf2 = lp.gameObject; break; }
            }
            if (leaf2 == null)
            {
                string p2 = FindLeavePrefabPath("LeavePanel2");
                if (p2 == null)
                {
                    EditorUtility.DisplayDialog("古画教学",
                        "找不到 LeavePanel2 预制体。\n先把第二片叶子的面板做成名为 LeavePanel2 的预制体。", "好");
                    return;
                }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p2);
                var scene = SceneManager.GetActiveScene();
                leaf2 = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                Undo.RegisterCreatedObjectUndo(leaf2, "放入 LeavePanel2");
            }

            // 生成碰撞盒（BoxCollider2D 按图片 RectTransform 精确对齐，贴合叶脉）+ 配脚本（单遍）
            EnsureVeinAreas(leaf2, showDialog: false);
            AutoConfigureLeafStep(leaf2, singlePass: true, showDialog: false);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), SampleScenePath);

            Selection.activeGameObject = leaf2;
            EditorGUIUtility.PingObject(leaf2);
            EditorUtility.DisplayDialog("古画教学",
                "第二片叶子 LeavePanel2 已就位：\n" +
                "  1. 已放进场景\n" +
                "  2. 已生成 Area_2-1~2-6 碰撞盒（贴近叶脉）\n" +
                "  3. 已配 6 个脉络 + next（单遍模式）\n\n" +
                "接着跑菜单 ⑥ 更新 GameFlow，即可接上流程。", "好");
        }

        // ── 一键装配完整流程 ──────────────────────────────────────

        public const string AssembleFlowPath = "Tools/古画教学/⑫ 一键装配完整流程（墨色+叶子1+叶子2）";

        [MenuItem(AssembleFlowPath, false, 12)]
        public static void AssembleFullFlow()
        {
            var active = SceneManager.GetActiveScene();
            if (active.path != SampleScenePath)
            {
                if (!System.IO.File.Exists(SampleScenePath))
                {
                    EditorUtility.DisplayDialog("古画教学", "请先打开 " + SampleScenePath + "。", "好");
                    return;
                }
                if (!EditorUtility.DisplayDialog("古画教学",
                    "当前不是 SampleScene，要先打开它再装配完整流程。\n继续吗？", "打开并继续", "取消"))
                    return;
                if (active.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
                active = SceneManager.GetActiveScene();
            }

            var sb = new System.Text.StringBuilder();

            // 1. 墨色面板
            GameObject inkRoot = FindExistingTutorialRoot();
            if (inkRoot == null)
            {
                string inkPath = AncientPaintingValidation.FindPrefabPath();
                if (inkPath != null)
                {
                    inkRoot = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(inkPath), active);
                    Undo.RegisterCreatedObjectUndo(inkRoot, "放入墨色面板");
                }
            }
            if (inkRoot != null) { EnsureInkStepAttached(inkRoot, force: true); sb.AppendLine("  墨色面板 ✓（InkColorStep 已挂，正确答案 Ink3）"); }
            else sb.AppendLine("  ❌ 墨色面板：找不到 ChooseInkPanel 预制体");

            // 2. 第一片叶子
            GameObject leaf1 = FindLeaveRootInScene("LeavePanel1");
            if (leaf1 == null)
            {
                string p1 = FindLeavePrefabPath("LeavePanel1");
                if (p1 != null)
                {
                    leaf1 = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(p1), active);
                    Undo.RegisterCreatedObjectUndo(leaf1, "放入 LeavePanel1");
                }
            }
            if (leaf1 != null)
            {
                EnsureVeinAreas(leaf1, showDialog: false);
                AutoConfigureLeafStep(leaf1, singlePass: false, showDialog: false);
                sb.AppendLine("  第一片叶子 ✓（8 脉络 + 碰撞盒，两遍）");
            }
            else sb.AppendLine("  ❌ 第一片叶子：找不到 LeavePanel1 预制体");

            // 3. 第二片叶子
            GameObject leaf2 = FindLeaveRootInScene("LeavePanel2");
            if (leaf2 == null)
            {
                string p2 = FindLeavePrefabPath("LeavePanel2");
                if (p2 != null)
                {
                    leaf2 = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(p2), active);
                    Undo.RegisterCreatedObjectUndo(leaf2, "放入 LeavePanel2");
                }
            }
            if (leaf2 != null)
            {
                EnsureVeinAreas(leaf2, showDialog: false);
                AutoConfigureLeafStep(leaf2, singlePass: true, showDialog: false);
                sb.AppendLine("  第二片叶子 ✓（6 脉络 + 碰撞盒，单遍）");
            }
            else sb.AppendLine("  ❌ 第二片叶子：找不到 LeavePanel2 预制体");

            // 4. EventSystem（没有它，所有 UI 点击都不路由）
            EnsureEventSystemInScene(active);

            // 5. GameFlow 三步
            var flow = Object.FindObjectOfType<GameFlow>(true);
            if (flow == null)
            {
                var go = new GameObject("GameFlow");
                SceneManager.MoveGameObjectToScene(go, active);
                flow = Undo.AddComponent<GameFlow>(go);
            }
            AutoWireGameFlow(flow);
            sb.AppendLine("  GameFlow ✓（三步：墨色 → 叶子1 → 叶子2）");

            // 6. 保存
            EditorSceneManager.MarkSceneDirty(active);
            EditorSceneManager.SaveScene(active, SampleScenePath);

            EditorUtility.DisplayDialog("古画教学 · 完整流程已装配",
                "装配结果：\n" + sb.ToString() + "\n▶ Play 即可从头玩：选墨色 → 第一片叶子（浅+深）→ 第二片叶子（一遍）。", "好");
        }

        // ── 第三片（花瓣花萼 LeavePanel3）· 单独配置，暂不接流程 ──────────

        public const string SetupLeaf3Path = "Tools/古画教学/⑬ 单独配置花瓣花萼（放场景+碰撞盒+脚本）";

        [MenuItem(SetupLeaf3Path, false, 13)]
        public static void SetupLeavePanel3()
        {
            var active = SceneManager.GetActiveScene();
            if (active.path != SampleScenePath)
            {
                if (!System.IO.File.Exists(SampleScenePath))
                {
                    EditorUtility.DisplayDialog("古画教学", "请先打开 " + SampleScenePath + "。", "好");
                    return;
                }
                if (!EditorUtility.DisplayDialog("古画教学",
                    "当前不是 SampleScene，要先打开它再配置花瓣花萼。\n继续吗？", "打开并继续", "取消"))
                    return;
                if (active.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
            }

            // 找 LeavePanel3 场景实例，没有就实例化预制体
            GameObject leaf3 = null;
            foreach (var lp in Object.FindObjectsOfType<LeafPaintStep>(true))
            {
                if (lp.name == "LeavePanel3" || lp.name.Contains("LeavePanel3")) { leaf3 = lp.gameObject; break; }
            }
            if (leaf3 == null)
            {
                string p3 = FindLeavePrefabPath("LeavePanel3");
                if (p3 == null)
                {
                    EditorUtility.DisplayDialog("古画教学",
                        "找不到 LeavePanel3 预制体。\n先把花瓣花萼的面板做成名为 LeavePanel3 的预制体。", "好");
                    return;
                }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p3);
                var scene = SceneManager.GetActiveScene();
                leaf3 = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                Undo.RegisterCreatedObjectUndo(leaf3, "放入 LeavePanel3");
            }

            // 生成碰撞盒（alpha 包围盒去透明边距，更贴近花瓣花萼）+ 配脚本（单遍第一层）
            var r = EnsureVeinAreas(leaf3, showDialog: false);
            AutoConfigureLeafStep(leaf3, singlePass: true, showDialog: false);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), SampleScenePath);

            Selection.activeGameObject = leaf3;
            EditorGUIUtility.PingObject(leaf3);
            EditorUtility.DisplayDialog("古画教学",
                "花瓣花萼 LeavePanel3 已就位：\n" +
                $"  1. 已放进场景\n" +
                $"  2. 已生成 Area_3-X 碰撞盒 {r.created + r.updated} 个（按图片透明轮廓去边距）\n" +
                "  3. 已配脉络 + next（单遍第一层）\n\n" +
                "说明：这一步还没接进 GameFlow 流程，先单独测。\n" +
                "想直接看它，可临时把 GameFlow 的 Debug Start Index 设到对应位置；\n" +
                "等这一步做好了，我再帮你加进完整流程。", "好");
        }

        /// <summary>
        /// 把叶子的 N 个脉络一次性配齐：area（Area_* 碰撞盒）+ lightImage（1-N 图片）+ next 按钮。
        /// 全部按名字里的数字排序对齐，不依赖任何手填 id —— 彻底避开「id 对不上 / 漏配某一个」的问题。
        /// </summary>
        public static void AutoConfigureLeafStep(GameObject leaveRoot, bool singlePass = false, bool showDialog = true)
        {
            if (leaveRoot == null) { EditorUtility.DisplayDialog("古画教学", "目标 LeavePanel 为空。", "好"); return; }
            var step = leaveRoot.GetComponentInChildren<LeafPaintStep>(true);
            if (step == null) { EditorUtility.DisplayDialog("古画教学", "目标上没有 LeafPaintStep 组件。", "好"); return; }

            // 1. 脉络图片：名字形如 "1-1"（数字-数字），排除 Area_
            var imgs = leaveRoot.GetComponentsInChildren<RectTransform>(true)
                .Select(t => t.gameObject)
                .Where(g => IsVeinImageName(g.name))
                .OrderBy(g => VeinSortKey(g.name))
                .ToList();

            // 2. Area 碰撞盒：名字形如 "Area_1-1"
            var areas = leaveRoot.GetComponentsInChildren<Collider2D>(true)
                .Where(c => c.name.StartsWith("Area_") && IsVeinImageName(c.name.Substring(5)))
                .OrderBy(c => VeinSortKey(c.name.Substring(5)))
                .ToList();

            // 3. next 按钮：名字含 next（不区分大小写）
            var nextBtn = leaveRoot.GetComponentsInChildren<Button>(true)
                .FirstOrDefault(b => b.name.ToLower().Contains("next"));

            // 4. 成品图：名字含 final（不区分大小写）
            var finalImg = leaveRoot.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name.ToLower().Contains("final"));

            int cnt = Mathf.Min(imgs.Count, areas.Count);
            var so = new SerializedObject(step);
            var arr = so.FindProperty("veins");
            arr.arraySize = cnt;
            for (int i = 0; i < cnt; i++)
            {
                var el = arr.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("id").stringValue = imgs[i].name;
                el.FindPropertyRelative("area").objectReferenceValue = areas[i];
                el.FindPropertyRelative("lightImage").objectReferenceValue = imgs[i];
                el.FindPropertyRelative("deepImage").objectReferenceValue = null;
            }
            if (nextBtn != null)
                so.FindProperty("nextButton").objectReferenceValue = nextBtn;
            if (finalImg != null)
                so.FindProperty("finalImage").objectReferenceValue = finalImg.gameObject;
            var spProp = so.FindProperty("singlePass");
            if (spProp != null) spProp.boolValue = singlePass;
            so.ApplyModifiedProperties();

            // 落盘（资产 / 实例两条路）
            string where;
            if (PrefabUtility.IsPartOfPrefabAsset(step))
            {
                EditorUtility.SetDirty(step.gameObject);
                AssetDatabase.SaveAssets();
                where = "预制体资产（已保存）";
            }
            else
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(step);
                EditorSceneManager.MarkSceneDirty(step.gameObject.scene);
                if (step.gameObject.scene.path == SampleScenePath)
                    EditorSceneManager.SaveScene(step.gameObject.scene, SampleScenePath);
                where = "场景实例（已保存）";
            }

            if (showDialog)
                EditorUtility.DisplayDialog("古画教学",
                    $"叶子涂色已全自动配齐：\n  脉络图片 {imgs.Count} 个\n  Area 碰撞盒 {areas.Count} 个\n  已对齐写入 {cnt} 个槽位\n  next 按钮：{(nextBtn != null ? nextBtn.name : "❌ 没找到")}\n  成品图：{(finalImg != null ? finalImg.name : "❌ 没找到")}\n\n目标：{where}", "好");
        }

        private static bool IsVeinImageName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.StartsWith("Area_")) return false;
            int dash = name.IndexOf('-');
            if (dash <= 0 || dash >= name.Length - 1) return false;
            return int.TryParse(name.Substring(0, dash), out _)
                && int.TryParse(name.Substring(dash + 1), out _);
        }

        private static (int leaf, int vein) VeinSortKey(string name)
        {
            int dash = name.IndexOf('-');
            if (dash <= 0) return (0, 0);
            int.TryParse(name.Substring(0, dash), out int leaf);
            int.TryParse(name.Substring(dash + 1), out int vein);
            return (leaf, vein);
        }

        // ── 花瓣花萼 · 多层涂色配置（3/4/5/6 层）────────────────────

        public const string SetupMultiLayerPath = "Tools/古画教学/⑭ 配置花瓣多层（3/4/5/6 层递进涂色）";

        [MenuItem(SetupMultiLayerPath, false, 14)]
        public static void SetupMultiLayerMenu()
        {
            string p3 = FindLeavePrefabPath("LeavePanel3");
            if (p3 == null)
            {
                EditorUtility.DisplayDialog("古画教学",
                    "找不到 LeavePanel3 预制体。\n先把花瓣花萼面板做成名为 LeavePanel3 的预制体。", "好");
                return;
            }

            // ★ 必须用 LoadPrefabContents 编辑预制体：直接改 LoadAssetAtPath 拿到的对象，
            //   新增的子物体（Area 碰撞盒）不会真正写进 .prefab 文件 —— 这就是之前碰撞盒"消失"的根因。
            string summary;
            var contents = PrefabUtility.LoadPrefabContents(p3);
            try
            {
                summary = ConfigureMultiLayerCore(contents);
                PrefabUtility.SaveAsPrefabAsset(contents, p3);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[花瓣多层] 配置失败：{e}\n{e.StackTrace}");
                EditorUtility.DisplayDialog("古画教学", "配置失败：" + e.Message + "\n详见 Console。", "好");
                return;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            // 重新实例化场景里的 LeavePanel3（继承最新预制体），并清理飞出去的旧碰撞盒
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "LeavePanel3") Undo.DestroyObjectImmediate(root);
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p3);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(inst, "重新放入 LeavePanel3");

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith("Area_")) Undo.DestroyObjectImmediate(root);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (scene.path == SampleScenePath)
                EditorSceneManager.SaveScene(scene, SampleScenePath);

            EditorUtility.DisplayDialog("古画教学 · 花瓣多层已配齐",
                summary + "\n\n已重新放入场景。\n▶ Play 测：3 层涂完 → next 变灰切 4 层 → 4 层拖拽出图 → … → 6 层。", "好");
        }

        /// <summary>
        /// 在（PrefabUtility.LoadPrefabContents 打开的）预制体内容上配置花瓣多层（共用第一层碰撞体），
        /// 返回给弹窗展示的 summary。此方法只做修改，不负责保存 —— 由调用方 SaveAsPrefabAsset 落盘。
        /// </summary>
        private static string ConfigureMultiLayerCore(GameObject leaveRoot)
        {
            // 1. 识别花瓣层前缀（3~6），树枝前缀单独是 7（树枝走 LeafPaintStep 两遍，不混进多层）
            var prefixes = leaveRoot.GetComponentsInChildren<RectTransform>(true)
                .Select(t => t.name)
                .Where(IsVeinImageName)
                .Select(n => n.Substring(0, n.IndexOf('-')))
                .Where(p => int.TryParse(p, out int v) && v >= 3 && v <= 6)
                .Distinct()
                .OrderBy(p => { int.TryParse(p, out int v); return v; })
                .ToList();

            if (prefixes.Count == 0)
                throw new System.InvalidOperationException("没扫到任何「数字-数字」命名的脉络图片");
            string firstPrefix = prefixes[0];

            // 2. 生成/更新所有层碰撞盒 → 删多余碰撞盒：保留 Area_3-X（花瓣基础）和 Area_7-1（树枝，7-1/7-2 共用），
            //    删 4/5/6（复用 3）和 7-2（复用 7-1）。绝不能「先删再重建」—— Undo 删除延迟生效会导致碰撞盒全消失。
            EnsureVeinAreas(leaveRoot, showDialog: false, skipExisting: true);
            {
                var keep = new List<string>();
                foreach (var t in leaveRoot.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("Area_" + firstPrefix + "-") || t.name == "Area_7-1")
                        keep.Add(t.name);
                var removeList = leaveRoot.GetComponentsInChildren<Transform>(true)
                    .Where(t => t.name.StartsWith("Area_") && IsVeinImageName(t.name.Substring(5)))
                    .Where(t => !keep.Contains(t.name))
                    .ToList();
                foreach (var t in removeList) Undo.DestroyObjectImmediate(t.gameObject);
            }

            // 2.5 删掉挂在面板根上的旧 LeafPaintStep（之前单层配的）—— 根上要挂 MultiLayerPaintStep，
            //     StepBase 同物体只能挂一个。树枝的 LeafPaintStep 在 BranchHost 子物体上，不受影响。
            var rootLeaf = leaveRoot.GetComponent<LeafPaintStep>();
            if (rootLeaf != null) Undo.DestroyObjectImmediate(rootLeaf);

            // 3. 挂 MultiLayerPaintStep
            var step = leaveRoot.GetComponentInChildren<MultiLayerPaintStep>(true);
            if (step == null) step = Undo.AddComponent<MultiLayerPaintStep>(leaveRoot);

            // 4. 收集第一层图片（含 RectTransform），用于后续层按位置复用碰撞盒
            var firstImgs = leaveRoot.GetComponentsInChildren<RectTransform>(true)
                .Where(t => IsVeinImageName(t.name) && t.name.StartsWith(firstPrefix + "-"))
                .OrderBy(t => VeinSortKey(t.name))
                .ToList();

            Collider2D FirstAreaOf(RectTransform imgTf)
            {
                if (imgTf == null || imgTf.parent == null) return null;
                var a = imgTf.parent.Find("Area_" + imgTf.name);
                return a != null ? a.GetComponent<Collider2D>() : null;
            }

            // 5. 按层配 veins
            var so = new SerializedObject(step);
            var layersProp = so.FindProperty("layers");
            layersProp.arraySize = prefixes.Count;

            var summary = new System.Text.StringBuilder();
            summary.AppendLine($"识别到 {prefixes.Count} 层（共用第一层碰撞盒）：");
            for (int L = 0; L < prefixes.Count; L++)
            {
                string prefix = prefixes[L];
                var layerProp = layersProp.GetArrayElementAtIndex(L);
                layerProp.FindPropertyRelative("layerId").stringValue = prefix;

                var imgs = leaveRoot.GetComponentsInChildren<RectTransform>(true)
                    .Where(t => IsVeinImageName(t.name) && t.name.StartsWith(prefix + "-"))
                    .OrderBy(t => VeinSortKey(t.name))
                    .ToList();

                var veinsProp = layerProp.FindPropertyRelative("veins");
                veinsProp.arraySize = imgs.Count;
                for (int i = 0; i < imgs.Count; i++)
                {
                    var img = imgs[i];
                    var el = veinsProp.GetArrayElementAtIndex(i);
                    el.FindPropertyRelative("id").stringValue = img.name;
                    el.FindPropertyRelative("lightImage").objectReferenceValue = img.gameObject;
                    el.FindPropertyRelative("deepImage").objectReferenceValue = null;

                    if (L == 0)
                    {
                        el.FindPropertyRelative("area").objectReferenceValue = FirstAreaOf(img);
                    }
                    else
                    {
                        var nearest = firstImgs
                            .OrderBy(f => ((Vector2)f.anchoredPosition - (Vector2)img.anchoredPosition).sqrMagnitude)
                            .FirstOrDefault();
                        el.FindPropertyRelative("area").objectReferenceValue = nearest != null ? FirstAreaOf(nearest) : null;
                    }
                }
                summary.AppendLine($"  第 {prefix} 层：{imgs.Count} 个区域" + (L == 0 ? "（拥有碰撞盒）" : "（复用第一层碰撞盒）"));
            }

            // 6. 树枝 LeafPaintStep（两遍：7-1 浅涂 → 7-2 加深，共用 Area_7-1 碰撞盒）。
            //    ★ 必须挂到独立子物体 BranchHost —— StepBase 带 DisallowMultipleComponent，
            //      和花瓣的 MultiLayerPaintStep 不能挂在同一个 GameObject 上。
            var branch = leaveRoot.GetComponentsInChildren<LeafPaintStep>(true).FirstOrDefault();
            if (branch == null)
            {
                var hostTf = leaveRoot.transform.Find("BranchHost");
                GameObject host = hostTf != null ? hostTf.gameObject : null;
                if (host == null)
                {
                    host = new GameObject("BranchHost", typeof(RectTransform));
                    var hr = host.GetComponent<RectTransform>();
                    hr.anchorMin = Vector2.zero;
                    hr.anchorMax = Vector2.one;
                    hr.offsetMin = Vector2.zero;
                    hr.offsetMax = Vector2.zero;
                    Undo.SetTransformParent(host.transform, leaveRoot.transform, "树枝宿主");
                }
                branch = Undo.AddComponent<LeafPaintStep>(host);
            }
            var area7_1 = leaveRoot.GetComponentsInChildren<Collider2D>(true).FirstOrDefault(c => c.name == "Area_7-1");
            var img7_1 = leaveRoot.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t => t.name == "7-1");
            var img7_2 = leaveRoot.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t => t.name == "7-2");
            var bso = new SerializedObject(branch);
            var bveins = bso.FindProperty("veins");
            bveins.arraySize = 1;
            var bel = bveins.GetArrayElementAtIndex(0);
            bel.FindPropertyRelative("id").stringValue = "7";
            bel.FindPropertyRelative("area").objectReferenceValue = area7_1;
            bel.FindPropertyRelative("lightImage").objectReferenceValue = img7_1 != null ? img7_1.gameObject : null;
            bel.FindPropertyRelative("deepImage").objectReferenceValue = img7_2 != null ? img7_2.gameObject : null;
            bso.FindProperty("singlePass").boolValue = false;   // 两遍
            bso.FindProperty("autoStart").boolValue = false;    // 由花瓣完成时 Begin
            bso.ApplyModifiedProperties();
            summary.AppendLine("  树枝：7-1 浅涂 → 7-2 加深（共用 Area_7-1 碰撞盒）");

            // 7. next 按钮（花瓣和树枝共用同一个）
            var nextBtn = leaveRoot.GetComponentsInChildren<Button>(true)
                .FirstOrDefault(b => b.name.ToLower().Contains("next"));
            if (nextBtn != null)
            {
                so.FindProperty("nextButton").objectReferenceValue = nextBtn;
                bso.FindProperty("nextButton").objectReferenceValue = nextBtn;
            }

            // 8. 高亮成品图引用 + 树枝衔接
            var flowerHig = leaveRoot.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name.ToLower().Contains("flower_hig") || t.name.ToLower().Contains("flowerhig"));
            var shuzhiHig = leaveRoot.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name.ToLower().Contains("shuzhi_hig") || t.name.ToLower().Contains("shuzhihig"));
            so.FindProperty("flowerHighlight").objectReferenceValue = flowerHig != null ? flowerHig.gameObject : null;
            so.FindProperty("branchHighlight").objectReferenceValue = shuzhiHig != null ? shuzhiHig.gameObject : null;
            so.FindProperty("branchStep").objectReferenceValue = branch;

            so.ApplyModifiedProperties();
            bso.ApplyModifiedProperties();
            summary.Append("next 按钮：" + (nextBtn != null ? nextBtn.name : "❌ 没找到"));
            return summary.ToString();
        }

        // ── 花蕊 LeavePanel4 · 配置（替换式涂色）─────────────────────

        public const string SetupStamenPath = "Tools/古画教学/⑮ 配置花蕊（LeavePanel4）";

        [MenuItem(SetupStamenPath, false, 15)]
        public static void SetupStamenMenu()
        {
            string p4 = FindLeavePrefabPath("LeavePanel4");
            if (p4 == null)
            {
                EditorUtility.DisplayDialog("古画教学", "找不到 LeavePanel4 预制体。\n先把花蕊面板做成 LeavePanel4 预制体。", "好");
                return;
            }

            // 1. 配置花蕊预制体（LoadPrefabContents 保证写进 .prefab）
            string summary;
            var contents = PrefabUtility.LoadPrefabContents(p4);
            try
            {
                summary = ConfigureStamenCore(contents);
                PrefabUtility.SaveAsPrefabAsset(contents, p4);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[花蕊] 配置失败：{e}\n{e.StackTrace}");
                EditorUtility.DisplayDialog("古画教学", "配置失败：" + e.Message + "\n详见 Console。", "好");
                return;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            // 2. 重新实例化场景里的 LeavePanel4（初始失活，等树枝完成才激活）
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "LeavePanel4") Undo.DestroyObjectImmediate(root);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p4);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(inst, "放入 LeavePanel4");
            inst.SetActive(false);

            // 3. 在场景里配树枝 LeafPaintStep 的 nextPanel / selfPanel 引用（面板切换）
            GameObject lp3 = null, lp4 = null;
            foreach (var r in scene.GetRootGameObjects())
            {
                if (r.name == "LeavePanel3") lp3 = r;
                if (r.name == "LeavePanel4") lp4 = r;
            }
            LeafPaintStep branchInScene = null;
            foreach (var lp in Object.FindObjectsOfType<LeafPaintStep>(true))
                if (lp.gameObject.name == "BranchHost") branchInScene = lp;

            if (branchInScene != null && lp3 != null && lp4 != null)
            {
                var bso = new SerializedObject(branchInScene);
                bso.FindProperty("nextPanel").objectReferenceValue = lp4;
                bso.FindProperty("selfPanel").objectReferenceValue = lp3;
                bso.ApplyModifiedProperties();
                PrefabUtility.RecordPrefabInstancePropertyModifications(branchInScene);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (scene.path == SampleScenePath)
                EditorSceneManager.SaveScene(scene, SampleScenePath);

            EditorUtility.DisplayDialog("古画教学 · 花蕊已配齐",
                summary + "\n\nLeavePanel4 已放入场景（初始失活）。\n树枝（7-1→7-2）完成后自动切到花蕊。", "好");
        }

        /// <summary>在 LeavePanel4 预制体内容上配置花蕊，返回 summary。</summary>
        private static string ConfigureStamenCore(GameObject root)
        {
            var sb = new System.Text.StringBuilder();

            // 0. 先删掉根上已有的步骤脚本（StepBase 带 DisallowMultipleComponent，
            //    根上残留的 LeafPaintStep 等会让 StamenPaintStep 挂不上去 → SerializedObject(null) 报错）
            foreach (var old in root.GetComponents<StepBase>())
                Object.DestroyImmediate(old, true);

            // 挂 StamenPaintStep
            var step = root.GetComponentInChildren<StamenPaintStep>(true);
            if (step == null) step = Undo.AddComponent<StamenPaintStep>(root);
            if (step == null)
                throw new System.InvalidOperationException(
                    "StamenPaintStep 挂载失败（根物体上可能还有其他 StepBase 脚本或组件缺失），配置中止。");

            var so = new SerializedObject(step);
            var slotsProp = so.FindProperty("slots");
            slotsProp.arraySize = 5;
            for (int i = 0; i < 5; i++)
            {
                int n = i + 1;
                var el = slotsProp.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("id").stringValue = n.ToString();
                var area = root.GetComponentsInChildren<Collider2D>(true).FirstOrDefault(c => c.name == "Area_8-" + n);
                el.FindPropertyRelative("area").objectReferenceValue = area;
                var init = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n.ToString());
                el.FindPropertyRelative("initialImage").objectReferenceValue = init != null ? init.gameObject : null;
                var res = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "8-" + n);
                el.FindPropertyRelative("resultImage").objectReferenceValue = res != null ? res.gameObject : null;
                // 激活碰撞盒（Area_8-N 预制体里是失活的，碰撞盒要激活才能响应）
                if (area != null && !area.gameObject.activeSelf)
                    area.gameObject.SetActive(true);
            }

            var final = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "8-6");
            so.FindProperty("finalImage").objectReferenceValue = final != null ? final.gameObject : null;

            var nextBtn = root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name.ToLower().Contains("next"));
            if (nextBtn != null) so.FindProperty("nextButton").objectReferenceValue = nextBtn;

            so.ApplyModifiedProperties();
            sb.AppendLine("花蕊：5 个区域（1~5 初始 → 拖拽换 8-1~8-5，全完成后成品 8-6）");
            sb.Append("next 按钮：" + (nextBtn != null ? nextBtn.name : "❌ 没找到"));
            return sb.ToString();
        }

        /// <summary>删除 leaveRoot 下所有「非 keepPrefix 层」的 Area_* 碰撞盒节点（多层共用碰撞体时清理多余的）。</summary>
        private static void RemoveAreasExcept(GameObject root, string keepPrefix)
        {
            if (root == null) return;
            var toRemove = root.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith("Area_") && IsVeinImageName(t.name.Substring(5)))
                .Where(t => !t.name.StartsWith("Area_" + keepPrefix + "-"))
                .ToList();
            foreach (var t in toRemove)
                Undo.DestroyObjectImmediate(t.gameObject);
        }

        // ── LeavePanel5 · 渐变过渡（9-1 → 9-2）─────────────────────

        public const string SetupTransitionPath = "Tools/古画教学/⑯ 配置渐变过渡（LeavePanel5）";

        [MenuItem(SetupTransitionPath, false, 16)]
        public static void SetupTransitionMenu()
        {
            string p5 = FindLeavePrefabPath("LeavePanel5");
            if (p5 == null)
            {
                EditorUtility.DisplayDialog("古画教学", "找不到 LeavePanel5 预制体。\n先把渐变过渡面板做成名为 LeavePanel5 的预制体。", "好");
                return;
            }

            // 1. 配置 LeavePanel5 预制体（LoadPrefabContents 保证写进 .prefab）
            string summary;
            var contents = PrefabUtility.LoadPrefabContents(p5);
            try
            {
                summary = ConfigureTransitionCore(contents);
                PrefabUtility.SaveAsPrefabAsset(contents, p5);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[渐变过渡] 配置失败：{e}\n{e.StackTrace}");
                EditorUtility.DisplayDialog("古画教学", "配置失败：" + e.Message + "\n详见 Console。", "好");
                return;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            // 2. 重新实例化场景里的 LeavePanel5（初始失活，等花蕊完成才激活）
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "LeavePanel5") Undo.DestroyObjectImmediate(root);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p5);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(inst, "放入 LeavePanel5");
            inst.SetActive(false);

            // 3. 在场景里配花蕊 StamenPaintStep 的 nextPanel / selfPanel（花蕊完成后切到 LeavePanel5）
            GameObject lp4 = null, lp5 = null;
            foreach (var r in scene.GetRootGameObjects())
            {
                if (r.name == "LeavePanel4") lp4 = r;
                if (r.name == "LeavePanel5") lp5 = r;
            }
            StamenPaintStep stamenInScene = null;
            foreach (var s in Object.FindObjectsOfType<StamenPaintStep>(true))
                if (s.gameObject.name == "LeavePanel4") stamenInScene = s;

            if (stamenInScene != null && lp4 != null && lp5 != null)
            {
                var sso = new SerializedObject(stamenInScene);
                sso.FindProperty("nextPanel").objectReferenceValue = lp5;
                sso.FindProperty("selfPanel").objectReferenceValue = lp4;
                sso.ApplyModifiedProperties();
                PrefabUtility.RecordPrefabInstancePropertyModifications(stamenInScene);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (scene.path == SampleScenePath)
                EditorSceneManager.SaveScene(scene, SampleScenePath);

            EditorUtility.DisplayDialog("古画教学 · 渐变过渡已配齐",
                summary + "\n\nLeavePanel5 已放入场景（初始失活）。\n花蕊完成后自动切到 LeavePanel5，9-1 淡出 → 9-2 出现。", "好");
        }

        /// <summary>在 LeavePanel5 预制体内容上配置渐变过渡，返回 summary。</summary>
        private static string ConfigureTransitionCore(GameObject root)
        {
            var sb = new System.Text.StringBuilder();

            // 0. 清掉根上已有的步骤脚本（避免 DisallowMultipleComponent 冲突）
            foreach (var old in root.GetComponents<StepBase>())
                Object.DestroyImmediate(old, true);

            var step = root.GetComponentInChildren<FadeTransitionStep>(true);
            if (step == null) step = Undo.AddComponent<FadeTransitionStep>(root);
            if (step == null)
                throw new System.InvalidOperationException("FadeTransitionStep 挂载失败，配置中止。");

            var so = new SerializedObject(step);
            var from = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "9-1");
            var to = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "9-2");
            so.FindProperty("fromImage").objectReferenceValue = from != null ? from.gameObject : null;
            so.FindProperty("toImage").objectReferenceValue = to != null ? to.gameObject : null;

            var nextBtn = root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name.ToLower().Contains("next"));
            if (nextBtn != null) so.FindProperty("nextButton").objectReferenceValue = nextBtn;

            so.ApplyModifiedProperties();

            sb.AppendLine("渐变过渡：9-1（淡出）→ 9-2（淡入）");
            sb.Append("next 按钮：" + (nextBtn != null ? nextBtn.name : "❌ 没找到"));
            return sb.ToString();
        }

        // ── 一键串联全部面板 + 配文案引用 ─────────────────────────

        public const string SetupChainPath = "Tools/古画教学/⑰ 一键串联全部面板 + 配文案";

        [MenuItem(SetupChainPath, false, 17)]
        public static void SetupChainMenu()
        {
            var scene = SceneManager.GetActiveScene();
            var sb = new System.Text.StringBuilder();

            GameObject ink = FindSceneRoot("ChooseInkPanel");
            GameObject l1 = FindSceneRoot("LeavePanel1");
            GameObject l2 = FindSceneRoot("LeavePanel2");
            GameObject l3 = FindSceneRoot("LeavePanel3");
            GameObject l4 = FindSceneRoot("LeavePanel4");
            GameObject l5 = FindSceneRoot("LeavePanel5");

            var inkStep = ink != null ? ink.GetComponentInChildren<InkColorStep>(true) : null;
            var l1Step = l1 != null ? l1.GetComponentInChildren<LeafPaintStep>(true) : null;
            var l2Step = l2 != null ? l2.GetComponentInChildren<LeafPaintStep>(true) : null;
            var l3Petals = l3 != null ? l3.GetComponentInChildren<MultiLayerPaintStep>(true) : null;

            LeafPaintStep branchStep = null;
            if (l3 != null)
            {
                var bh = l3.transform.Find("BranchHost");
                if (bh != null) branchStep = bh.GetComponent<LeafPaintStep>();
            }
            var l4Step = l4 != null ? l4.GetComponentInChildren<StamenPaintStep>(true) : null;
            var l5Step = l5 != null ? l5.GetComponentInChildren<FadeTransitionStep>(true) : null;

            // 串联：完成 → 激活下一面板
            SetLink(inkStep, l1, ink);       sb.AppendLine((inkStep != null ? "✓" : "✗") + " 墨色 → 叶子1");
            SetLink(l1Step, l2, l1);          sb.AppendLine((l1Step != null ? "✓" : "✗") + " 叶子1 → 叶子2");
            SetLink(l2Step, l3, l2);          sb.AppendLine((l2Step != null ? "✓" : "✗") + " 叶子2 → 花瓣");

            // 花瓣 → 树枝（同面板内部，MultiLayerPaintStep 完成后 Begin branchStep）
            if (l3Petals != null && branchStep != null)
            {
                var mso = new SerializedObject(l3Petals);
                mso.FindProperty("branchStep").objectReferenceValue = branchStep;
                mso.ApplyModifiedProperties();
                MarkDirty(l3Petals);
            }
            sb.AppendLine((l3Petals != null && branchStep != null ? "✓" : "✗") + " 花瓣 → 树枝");

            SetLink(branchStep, l4, l3);      sb.AppendLine((branchStep != null ? "✓" : "✗") + " 树枝 → 花蕊");
            SetLink(l4Step, l5, l4);          sb.AppendLine((l4Step != null ? "✓" : "✗") + " 花蕊 → 过渡");
            sb.AppendLine((l5Step != null ? "✓" : "✗") + " 过渡（最后一步）");

            // autoStart：只有墨色自动开始，其余等上一步 Begin
            SetAutoStart(inkStep, true);
            SetAutoStart(l1Step, false);
            SetAutoStart(l2Step, false);
            SetAutoStart(l3Petals, false);
            SetAutoStart(branchStep, false);
            SetAutoStart(l4Step, false);
            SetAutoStart(l5Step, false);

            // 配文案引用（Prompt / Progress 的 Text）
            SetPromptRefs(inkStep, ink);
            SetPromptRefs(l1Step, l1);
            SetPromptRefs(l2Step, l2);
            SetPromptRefs(l3Petals, l3);
            SetPromptRefs(branchStep, l3);
            SetPromptRefs(l4Step, l4);
            SetPromptRefs(l5Step, l5);

            EditorSceneManager.MarkSceneDirty(scene);
            if (scene.path == SampleScenePath)
                EditorSceneManager.SaveScene(scene, SampleScenePath);

            EditorUtility.DisplayDialog("古画教学 · 串联完成",
                "面板串联结果：\n" + sb.ToString() +
                "\n✗ 表示对应面板/脚本没找到——先点对应菜单把面板放进场景再跑本菜单。\n\n" +
                "提示文案：在每个步骤脚本的「提示文案」栏里按阶段填（见说明文档）。", "好");
        }

        private static GameObject FindSceneRoot(string name)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == name) return root;
            return null;
        }

        private static void SetLink(StepBase step, GameObject nextPanel, GameObject selfPanel)
        {
            if (step == null) return;
            var so = new SerializedObject(step);
            so.FindProperty("nextPanel").objectReferenceValue = nextPanel;
            so.FindProperty("selfPanel").objectReferenceValue = selfPanel;
            so.ApplyModifiedProperties();
            MarkDirty(step);
        }

        private static void SetAutoStart(StepBase step, bool value)
        {
            if (step == null) return;
            var so = new SerializedObject(step);
            var p = so.FindProperty("autoStart");
            if (p != null) p.boolValue = value;
            so.ApplyModifiedProperties();
            MarkDirty(step);
        }

        private static void SetPromptRefs(StepBase step, GameObject panelRoot)
        {
            if (step == null || panelRoot == null) return;
            var so = new SerializedObject(step);
            so.FindProperty("promptText").objectReferenceValue = FindText(panelRoot, "Prompt");
            so.FindProperty("progressText").objectReferenceValue = FindText(panelRoot, "Progress");
            so.ApplyModifiedProperties();
            MarkDirty(step);
        }

        private static Text FindText(GameObject root, string name)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Text>(true))
                if (t.name == name) return t;
            return null;
        }

        private static void MarkDirty(StepBase step)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(step))
                PrefabUtility.RecordPrefabInstancePropertyModifications(step);
            else
                EditorUtility.SetDirty(step.gameObject);
        }

        // ── 写入提示文案（分阶段，写进预制体资产，持久）──────────

        public const string WritePromptsPath = "Tools/古画教学/⑱ 写入提示文案";

        [MenuItem(WritePromptsPath, false, 18)]
        public static void WritePromptsMenu()
        {
            var sb = new System.Text.StringBuilder();
            int ok = 0;

            ok += WritePrompt("ChooseInkPanel", r => r.GetComponentInChildren<InkColorStep>(true),
                new[] { "请选择淡墨", "请选择淡墨", "请选择淡墨" }, sb);

            ok += WritePrompt("LeavePanel1", r => r.GetComponentInChildren<LeafPaintStep>(true),
                new[] { "选对啦，请给中间的叶片分染淡墨吧", "选对啦，请给中间的叶片分染淡墨吧", "现在进行第二次分染，加深颜色", "现在进行第二次分染，加深颜色" }, sb);

            ok += WritePrompt("LeavePanel2", r => r.GetComponentInChildren<LeafPaintStep>(true),
                new[] { "现在请进行局部分染加深（请分染高亮位置）", "现在请进行局部分染加深（请分染高亮位置）" }, sb);

            ok += WritePrompt("LeavePanel3（花瓣）", r => r.GetComponentInChildren<MultiLayerPaintStep>(true),
                new[] { "请用浅墨浅浅染出花瓣和花萼的薄厚关系", "请用胭脂对花瓣进行第二次分染", "请用朱磦均匀的对花瓣罩染", "请再用朱磦对花瓣进行第二次罩染", "请再用朱磦对花瓣进行第三遍罩染" }, sb);

            ok += WritePrompt("LeavePanel3（树枝）", r =>
            {
                var bh = r.transform.Find("BranchHost");
                return bh != null ? bh.GetComponent<LeafPaintStep>() : null;
            }, new[] { "请用淡墨将树枝皴擦一下", "请用淡墨将树枝皴擦一下", "请用淡墨将树枝罩染一下", "请用淡墨将树枝罩染一下" }, sb);

            ok += WritePrompt("LeavePanel4", r => r.GetComponentInChildren<StamenPaintStep>(true),
                new[] { "请顺着虚线方向画出花蕊，并用笔尖点缀花芯", "请顺着虚线方向画出花蕊，并用笔尖点缀花芯" }, sb);

            ok += WritePrompt("LeavePanel5", r => r.GetComponentInChildren<FadeTransitionStep>(true),
                new[] { "恭喜完成！", "恭喜完成！" }, sb);

            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("古画教学 · 文案已写入",
                $"成功写入 {ok} 处文案。\n\n{sb}", "好");
        }

        private static int WritePrompt(string label, System.Func<GameObject, StepBase> findStep, string[] texts, System.Text.StringBuilder sb)
        {
            string prefabName = label.Split('（')[0].Split('/')[0].Trim();
            string p = FindLeavePrefabPath(prefabName);
            if (p == null)
            {
                sb.AppendLine($"✗ {label}：找不到预制体");
                return 0;
            }
            var contents = PrefabUtility.LoadPrefabContents(p);
            try
            {
                var step = findStep(contents);
                if (step == null)
                {
                    sb.AppendLine($"✗ {label}：找不到步骤脚本");
                    return 0;
                }
                var so = new SerializedObject(step);
                var arr = so.FindProperty("promptStages");
                arr.arraySize = texts.Length;
                for (int i = 0; i < texts.Length; i++)
                    arr.GetArrayElementAtIndex(i).stringValue = texts[i];
                so.ApplyModifiedProperties();
                PrefabUtility.SaveAsPrefabAsset(contents, p);
                sb.AppendLine($"✓ {label}：{texts.Length} 条文案");
                return 1;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
    }
}
