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
    /// 步骤 ② 叶子涂色 的配置向导。
    ///
    /// 一次给 LeafPaintStep 写好：
    ///   - 8 个 VeinSlot（id / area / lightImage / deepImage）
    ///   - 高亮 overlay
    ///   - next 按钮（含 enabled/disabled 颜色）
    ///   - 长按 + 拖拽参数
    /// </summary>
    public class LeafPaintStepWizard : EditorWindow
    {
        private const string MenuPath = "Tools/古画教学/⑨ 叶子涂色步骤 · 配置向导";

        // 目标
        private GameObject _leaveRoot;          // LeavePanel 预制体或场景实例
        private LeafPaintStep _step;            // 在 LeaveRoot 上找到的脚本
        private List<VeinSlot> _draftVeins = new List<VeinSlot>();   // 向导里的临时编辑槽
        private Collider2D[] _areaChips;         // 按名字 1-1~1-8 自动收集的候选碰撞盒
        private GameObject _nextButtonGo;
        private GameObject _finalImageGo;

        [MenuItem(MenuPath, false, 9)]
        public static void Open()
        {
            var w = GetWindow<LeafPaintStepWizard>(true, "叶子涂色向导", true);
            w.minSize = new Vector2(460f, 600f);
            w.AutoLocate();
            w.Show();
        }

        private void AutoLocate()
        {
            string path = FindLeavePrefabPath();
            if (path != null)
            {
                _leaveRoot = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            else
            {
                // 场景里搜一个挂了 LeafPaintStep 的物体
                var inScene = Object.FindObjectOfType<LeafPaintStep>(true);
                if (inScene != null) _leaveRoot = inScene.gameObject;
            }
            Reload();
        }

        private static string FindLeavePrefabPath()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab LeavePanel");
            if (guids.Length > 0) return AssetDatabase.GUIDToAssetPath(guids[0]);
            // 容错：找含 LeavePanel 字样的
            guids = AssetDatabase.FindAssets("t:Prefab");
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (p.ToLower().Contains("leave")) return p;
            }
            return null;
        }

        private void Reload()
        {
            _step = _leaveRoot != null ? _leaveRoot.GetComponentInChildren<LeafPaintStep>(true) : null;

            if (_step != null)
            {
                var so = new SerializedObject(_step);
                var arr = so.FindProperty("veins");
                _draftVeins.Clear();
                if (arr != null)
                {
                    for (int i = 0; i < arr.arraySize; i++)
                    {
                        var el = arr.GetArrayElementAtIndex(i);
                        _draftVeins.Add(new VeinSlot
                        {
                            id = el.FindPropertyRelative("id")?.stringValue ?? (i + 1).ToString(),
                            area = el.FindPropertyRelative("area")?.objectReferenceValue as Collider2D,
                            lightImage = el.FindPropertyRelative("lightImage")?.objectReferenceValue as GameObject,
                            deepImage = el.FindPropertyRelative("deepImage")?.objectReferenceValue as GameObject,
                        });
                    }
                }
                if (_draftVeins.Count == 0)
                {
                    for (int i = 0; i < 8; i++)
                        _draftVeins.Add(new VeinSlot { id = (i + 1).ToString() });
                }

                var btnProp = so.FindProperty("nextButton");
                _nextButtonGo = btnProp != null ? btnProp.objectReferenceValue as GameObject : null;

                var finProp = so.FindProperty("finalImage");
                _finalImageGo = finProp != null ? finProp.objectReferenceValue as GameObject : null;
            }
            else
            {
                _draftVeins.Clear();
                for (int i = 0; i < 8; i++) _draftVeins.Add(new VeinSlot { id = (i + 1).ToString() });
                _nextButtonGo = null;
                _finalImageGo = null;
            }

            _areaChips = _leaveRoot != null
                ? _leaveRoot.GetComponentsInChildren<Collider2D>(true)
                : new Collider2D[0];
        }

        // ── GUI ──

        private void OnGUI()
        {
            EditorGUILayout.LabelField("古画教学 · 步骤 ② 叶子涂色", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "8 个脉络分别有 area（碰撞盒）和 lightImage / deepImage（两遍要激活的图片）。\n" +
                "填好点「写入脚本」会直接落到脚本上。",
                MessageType.Info);

            DrawTargetSection();
            DrawVeinSection();
            DrawNextButtonSection();
            DrawActions();
        }

        private void DrawTargetSection()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("① 目标面板", EditorStyles.boldLabel);

            // 场景里没有 LeavePanel 时的引导
            var sceneLeave = FindLeaveInScene();
            if (sceneLeave == null)
            {
                EditorGUILayout.HelpBox(
                    "❗ 当前场景里没找到 LeavePanel 实例。\n" +
                    "先用菜单 ⑤ 把叶子面板放进 SampleScene。",
                    MessageType.Warning);
                if (GUILayout.Button("📥 把 LeavePanel 放进 SampleScene（一键就位）", GUILayout.Height(30)))
                {
                    SceneSetupTools.EnsureLeavePanelInScene();
                    return;
                }
            }

            EditorGUI.BeginChangeCheck();
            _leaveRoot = (GameObject)EditorGUILayout.ObjectField("LeavePanel / 预制体", _leaveRoot, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck()) Reload();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("自动定位 LeavePanel")) AutoLocate();
                if (GUILayout.Button("诊断")) SceneSetupTools.DiagnoseLeafPanel();
            }

            if (_leaveRoot != null)
            {
                bool isAsset = PrefabUtility.IsPartOfPrefabAsset(_leaveRoot);
                EditorGUILayout.LabelField("修改方式",
                    isAsset ? "预制体资源（对所有未来实例生效）" : "场景物体（仅当前场景）",
                    EditorStyles.miniLabel);
            }
        }

        private void DrawVeinSection()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("② 8 个脉络槽位", EditorStyles.boldLabel);

            if (_leaveRoot == null)
            {
                EditorGUILayout.HelpBox("先指定 LeavePanel。", MessageType.Warning);
                return;
            }

            if (_areaChips != null && _areaChips.Length > 0)
            {
                EditorGUILayout.LabelField($"在 {_leaveRoot.name} 下扫到 {_areaChips.Length} 个 Collider2D（候选 area）", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "没扫到任何 Collider2D。\n" +
                    "点下面的按钮给每个脉络图片（默认名字 1-1~1-8）旁边建一个 Area_<id> 兄弟节点，\n" +
                    "BoxCollider2D 完全照抄源图的 RectTransform，能覆盖脉络形状。\n" +
                    "为什么不做成「在图片上加 collider」：image 初始 SetActive(false)，失活 GameObject 上的 collider 不响应 Physics2D 查询。",
                    MessageType.Warning);
                if (GUILayout.Button("🆕 按 lightImage 位置创建 area 碰撞盒（推荐）", GUILayout.Height(28)))
                {
                    SceneSetupTools.EnsureVeinAreas(_leaveRoot);
                    Reload();
                }
            }

            // 一键按 id 找 area 的批量按钮
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("按 id 1-1~1-8 批量配 area"))
                {
                    AutoAssignAreasById();
                }
                if (GUILayout.Button("按 id 批量配 lightImage"))
                {
                    AutoAssignImagesById(isDeep: false);
                }
                if (GUILayout.Button("按 id 批量配 deepImage"))
                {
                    AutoAssignImagesById(isDeep: true);
                }
            }

            // ★ 一键全自动：不依赖 id，按名字数字排序对齐 area + lightImage + next，直接写盘。
            //    专治「某一个槽位漏配 / id 对不上」这类问题。
            if (GUILayout.Button("⚡ 一键全自动配齐（area + light + next，直接保存）", GUILayout.Height(30)))
            {
                SceneSetupTools.AutoConfigureLeafStep(_leaveRoot);
                Reload();
            }

            for (int i = 0; i < _draftVeins.Count; i++)
            {
                var s = _draftVeins[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        s.id = EditorGUILayout.TextField("ID", s.id, GUILayout.Width(140));
                        if (GUILayout.Button("按名找 area", GUILayout.Width(86)))
                            s.area = FindColliderByName(s.id);
                        if (GUILayout.Button("按名找 light", GUILayout.Width(86)))
                            s.lightImage = FindGameObjectByName(s.id);
                        if (GUILayout.Button("按名找 deep", GUILayout.Width(86)))
                            s.deepImage = FindGameObjectByName(s.id);
                    }

                    s.area = (Collider2D)EditorGUILayout.ObjectField("Area (Collider2D)", s.area, typeof(Collider2D), true);
                    s.lightImage = (GameObject)EditorGUILayout.ObjectField("Light Image (第一遍显示)", s.lightImage, typeof(GameObject), true);
                    s.deepImage = (GameObject)EditorGUILayout.ObjectField("Deep Image (第二遍叠加)", s.deepImage, typeof(GameObject), true);
                }
            }
        }

        private void DrawNextButtonSection()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("③ 下一步按钮 & 成品图", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _nextButtonGo = (GameObject)EditorGUILayout.ObjectField("Next 按钮物体", _nextButtonGo, typeof(GameObject), true);
                if (GUILayout.Button("🔍 自动找", GUILayout.Width(70)))
                {
                    var hit = _leaveRoot != null
                        ? _leaveRoot.GetComponentsInChildren<Button>(true)
                            .FirstOrDefault(b => b.name.ToLower().Contains("next"))
                        : null;
                    if (hit != null)
                    {
                        _nextButtonGo = hit.gameObject;
                        Debug.Log($"[叶子涂色向导] 自动找到 Next 按钮：{hit.name}");
                    }
                    else
                        Debug.LogWarning("[叶子涂色向导] 没找到名字含 next 的 Button。");
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                _finalImageGo = (GameObject)EditorGUILayout.ObjectField("成品图（1-final）", _finalImageGo, typeof(GameObject), true);
                if (GUILayout.Button("🔍 自动找", GUILayout.Width(70)))
                {
                    var hit = _leaveRoot != null
                        ? _leaveRoot.GetComponentsInChildren<Transform>(true)
                            .FirstOrDefault(t => t.name.ToLower().Contains("final"))
                        : null;
                    if (hit != null)
                    {
                        _finalImageGo = hit.gameObject;
                        Debug.Log($"[叶子涂色向导] 自动找到成品图：{hit.name}");
                    }
                    else
                        Debug.LogWarning("[叶子涂色向导] 没找到名字含 final 的物体。");
                }
            }
        }

        private void DrawActions()
        {
            EditorGUILayout.Space(8);
            using (new EditorGUI.DisabledScope(_leaveRoot == null || _step == null))
            {
                if (GUILayout.Button("写入脚本（保存全部 8 个脉络）", GUILayout.Height(30)))
                {
                    Apply();
                }
            }
            if (GUILayout.Button("校验（体检）", GUILayout.Height(24), GUILayout.Width(100)))
            {
                SceneSetupTools.DiagnoseLeafPanel();
            }
        }

        private Collider2D FindColliderByName(string id)
        {
            if (_areaChips == null) return null;
            string n = id == null ? "" : id.Trim();
            // 1) 精确 Area_<id>
            var hit = _areaChips.FirstOrDefault(c => c != null && c.name == "Area_" + n);
            if (hit != null) return hit;
            // 2) id 是 "1" 而 area 叫 "Area_1-1"
            hit = _areaChips.FirstOrDefault(c => c != null && c.name == "Area_1-" + n);
            if (hit != null) return hit;
            // 3) 兜底：名字含 Area_ 且含 id（- 后面的数字对得上）
            hit = _areaChips.FirstOrDefault(c =>
                c != null && c.name.StartsWith("Area_") && c.name.EndsWith("-" + n));
            return hit;
        }

        private GameObject FindGameObjectByName(string name)
        {
            if (_leaveRoot == null) return null;
            string id = name == null ? "" : name.Trim();
            var all = _leaveRoot.GetComponentsInChildren<Transform>(true);

            // 1) 精确匹配，且排除 Area_ 兄弟节点
            var exact = all.FirstOrDefault(t => t.name == id && !t.name.StartsWith("Area_"));
            if (exact != null) return exact.gameObject;

            // 2) id 是 "1" 而图片叫 "1-1"（当前项目就是这种命名）
            var withLeaf = all.FirstOrDefault(t => t.name == "1-" + id && !t.name.StartsWith("Area_"));
            if (withLeaf != null) return withLeaf.gameObject;

            // 3) 兜底：名字以 "-"+id 结尾、含连字符、非 Area_ 的图片
            var suffix = all.FirstOrDefault(t =>
                !t.name.StartsWith("Area_") && t.name.Contains("-") && t.name.EndsWith("-" + id));
            return suffix != null ? suffix.gameObject : null;
        }

        private void AutoAssignAreasById()
        {
            int n = 0;
            for (int i = 0; i < _draftVeins.Count; i++)
            {
                var s = _draftVeins[i];
                if (s == null) continue;
                if (s.area != null) continue;
                var hit = FindColliderByName(s.id);
                if (hit != null) { s.area = hit; n++; }
            }
            Debug.Log($"[叶子涂色向导] 按 id 匹配到 {n} 个 area。");
            Repaint();
        }

        private void AutoAssignImagesById(bool isDeep)
        {
            int n = 0;
            for (int i = 0; i < _draftVeins.Count; i++)
            {
                var s = _draftVeins[i];
                if (s == null) continue;
                if (!isDeep && s.lightImage != null) continue;
                if (isDeep && s.deepImage != null) continue;
                var go = FindGameObjectByName(s.id);
                if (go == null) continue;
                if (isDeep) s.deepImage = go; else s.lightImage = go;
                // 把 id 一并修正成图片的真实名字（如 "1" → "1-1"），后续 area/图片匹配和 Inspector 都更清楚
                s.id = go.name;
                n++;
            }
            Debug.Log($"[叶子涂色向导] 按 id 匹配到 {n} 个 {(isDeep ? "deepImage" : "lightImage")}。");
            Repaint();
        }

        // ── 写入 ──

        private void Apply()
        {
            if (_step == null) return;
            var so = new SerializedObject(_step);
            var arr = so.FindProperty("veins");
            arr.arraySize = _draftVeins.Count;
            for (int i = 0; i < _draftVeins.Count; i++)
            {
                var s = _draftVeins[i];
                var el = arr.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("id").stringValue = s.id;
                el.FindPropertyRelative("area").objectReferenceValue = s.area;
                el.FindPropertyRelative("lightImage").objectReferenceValue = s.lightImage;
                el.FindPropertyRelative("deepImage").objectReferenceValue = s.deepImage;
            }
            if (_nextButtonGo != null)
            {
                var nb = so.FindProperty("nextButton");
                if (nb != null) nb.objectReferenceValue = _nextButtonGo.GetComponent<Button>();
            }
            var fin = so.FindProperty("finalImage");
            if (fin != null) fin.objectReferenceValue = _finalImageGo;
            so.ApplyModifiedProperties();

            // ★ 关键：把改动真正落到磁盘。之前只 ApplyModifiedProperties + MarkSceneDirty，
            //   对「预制体资产」这个目标，MarkSceneDirty 是无效的 —— 没 SetDirty + SaveAssets，
            //   一 Play（域重载）改动就全没了，看起来就像"没保存"。
            int lights = _draftVeins.Count(v => v != null && v.lightImage != null);
            int areas = _draftVeins.Count(v => v != null && v.area != null);

            if (PrefabUtility.IsPartOfPrefabAsset(_step))
            {
                EditorUtility.SetDirty(_step.gameObject);
                AssetDatabase.SaveAssets();
                Debug.Log($"[叶子涂色向导] 已写入预制体资产：area {areas}、lightImage {lights}。");
                EditorUtility.DisplayDialog("古画教学",
                    $"已写入预制体资产并保存：\n  area：{areas} / {_draftVeins.Count}\n  lightImage：{lights} / {_draftVeins.Count}\n\n" +
                    "现在 ▶ Play 就能生效。", "好");
            }
            else
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(_step);
                EditorSceneManager.MarkSceneDirty(_step.gameObject.scene);
                if (_step.gameObject.scene.path == SceneSetupTools.SampleScenePath)
                    EditorSceneManager.SaveScene(_step.gameObject.scene, SceneSetupTools.SampleScenePath);
                Debug.Log($"[叶子涂色向导] 已写入场景实例：area {areas}、lightImage {lights}。");
                EditorUtility.DisplayDialog("古画教学",
                    $"已写入场景实例：\n  area：{areas} / {_draftVeins.Count}\n  lightImage：{lights} / {_draftVeins.Count}\n\n" +
                    "场景已保存，▶ Play 即可。", "好");
            }

            Repaint();
        }

        private GameObject FindLeaveInScene()
        {
            var lp = Object.FindObjectOfType<LeafPaintStep>(true);
            return lp != null ? lp.gameObject : null;
        }
    }
}
