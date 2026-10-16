using System.Collections.Generic;
using DrawFlower.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DrawFlower.EditorTools
{
    /// <summary>
    /// 步骤 ① 选墨色 的配置向导。
    ///
    /// 解决的问题：正确答案、5 个按钮的引用、反馈图路径这些事以前要手工在 Inspector 里点十几次，
    /// 换个预制体就得重来一遍。这里一次点完，并且顺手告诉你哪里配错了。
    /// </summary>
    public class InkColorStepWizard : EditorWindow
    {
        private const string MenuPath = "Tools/古画教学/① 墨色步骤 · 配置向导";

        // 目标
        private GameObject _target;

        // 答案
        private int _correctIndex = 0;

        // 反馈图
        private string _yesPath = AncientPaintingValidation.YesResourcePath;
        private string _noPath = AncientPaintingValidation.NoResourcePath;
        private Vector2 _feedbackSize = new Vector2(108f, 96f);
        private Vector2 _feedbackOffset = Vector2.zero;
        private bool _randomRotation = true;

        // 淡出
        private float _fadeDelay = 0.45f;
        private float _fadeDuration = 0.7f;
        private float _fadeStagger = 0.09f;
        private bool _fadeAllAtOnce = false;
        private bool _deactivateWhenDone = true;

        private List<Button> _buttons = new List<Button>();
        private List<AncientPaintingValidation.Issue> _issues;
        private Vector2 _scroll;
        private string _targetSignature = "";
        private GUIStyle _thumbStyle;

        [MenuItem(MenuPath, false, 1)]
        public static void Open()
        {
            var w = GetWindow<InkColorStepWizard>(true, "墨色步骤向导", true);
            w.minSize = new Vector2(430f, 560f);
            w.AutoLocate();
            w.Show();
        }

        // ── 目标定位 ────────────────────────────────────────────────

        private void AutoLocate()
        {
            string path = AncientPaintingValidation.FindPrefabPath();
            if (path != null)
            {
                _target = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            else
            {
                var inScene = Object.FindObjectOfType<InkColorStep>(true);
                if (inScene != null) _target = inScene.gameObject;
            }
            Refresh();
        }

        private void Refresh()
        {
            _buttons = AncientPaintingValidation.FindInkButtons(_target);
            _targetSignature = _target == null ? "" : _target.name + "/" + _buttons.Count;
            PrefillFromTarget();
        }

        /// <summary>把目标上已有的设置读回窗口，避免每次打开都从头填。</summary>
        private void PrefillFromTarget()
        {
            if (_target == null) return;

            var step = _target.GetComponentInChildren<InkColorStep>(true);
            if (step == null) return;

            var so = new SerializedObject(step);
            var ci = so.FindProperty("correctIndex");
            if (ci != null) _correctIndex = ci.intValue;
            var yp = so.FindProperty("yesResourcePath");
            if (yp != null && !string.IsNullOrEmpty(yp.stringValue)) _yesPath = yp.stringValue;
            var np = so.FindProperty("noResourcePath");
            if (np != null && !string.IsNullOrEmpty(np.stringValue)) _noPath = np.stringValue;
            var fs = so.FindProperty("feedbackSize");
            if (fs != null) _feedbackSize = fs.vector2Value;
            var fo = so.FindProperty("feedbackOffset");
            if (fo != null) _feedbackOffset = fo.vector2Value;
            var rr = so.FindProperty("randomRotation");
            if (rr != null) _randomRotation = rr.boolValue;
            var fd = so.FindProperty("fadeDelay");
            if (fd != null) _fadeDelay = fd.floatValue;
            var fu = so.FindProperty("fadeDuration");
            if (fu != null) _fadeDuration = fu.floatValue;
            var st = so.FindProperty("fadeStagger");
            if (st != null) _fadeStagger = st.floatValue;
            var fa = so.FindProperty("fadeAllAtOnce");
            if (fa != null) _fadeAllAtOnce = fa.boolValue;
            var dw = so.FindProperty("deactivateWhenDone");
            if (dw != null) _deactivateWhenDone = dw.boolValue;
        }

        // ── 界面 ────────────────────────────────────────────────────

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("古画教学 · 步骤 ①  选墨色", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "点错 → 按钮上永久留下 no.png\n" +
                "点对 → 按钮上出现 yes.png，随后 5 个墨色按钮依次淡出消失",
                MessageType.Info);

            DrawTargetSection();
            DrawButtonSection();
            DrawParamsSection();
            DrawActionsSection();
            DrawIssueSection();

            EditorGUILayout.EndScrollView();
        }

        private void DrawTargetSection()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("① 目标面板", EditorStyles.boldLabel);

            // ── 第一要紧的事：场景里有没有能跑的实例？ ──
            // 没放进去的话光改预制体没用，运行起来空空如也。这里给一条最明显的提示。
            var sceneRoot = SceneSetupTools.FindExistingTutorialRoot();
            if (sceneRoot == null)
            {
                EditorGUILayout.HelpBox(
                    "❗ 当前场景里没找到任何教学面板。预制体改得再好，运行起来也是空白。\n" +
                    "先点下面的按钮把它放进 SampleScene。",
                    MessageType.Warning);

                if (GUILayout.Button("📥 把 TutorialCanvas 放进 SampleScene（一键就位）",
                    GUILayout.Height(34)))
                {
                    SceneSetupTools.EnsureTutorialCanvasInScene();
                    return; // 等用户点完，下一帧重新走 OnGUI
                }

                EditorGUILayout.Space(6);
            }
            else
            {
                // 场景里有根了，但 Palette 上可能还是没挂 InkColorStep —— 这是点了没反应最常见的原因
                var palette = SceneSetupTools.FindPalette(sceneRoot);
                bool hasStep = palette != null && palette.GetComponent<InkColorStep>() != null;
                if (!hasStep)
                {
                    EditorGUILayout.HelpBox(
                        "⚠️ 场景里的 Palette 上没有 InkColorStep。这就是「Hierarchy 看上去都齐，但点了没反应」的原因。\n" +
                        "下面两个按钮随便挑一个，都会先补挂脚本再继续配置。",
                        MessageType.Warning);
                    if (GUILayout.Button("🔧 修复场景里的墨色步骤（菜单 ③ 也可）",
                        GUILayout.Height(28)))
                    {
                        SceneSetupTools.FixInkStepMenu();
                        return;
                    }
                    EditorGUILayout.Space(4);
                }
            }

            EditorGUI.BeginChangeCheck();
            _target = (GameObject)EditorGUILayout.ObjectField("面板 / 预制体", _target, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck()) Refresh();

            if (_target != null && _targetSignature != _target.name + "/" + _buttons.Count) Refresh();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("自动定位 TutorialCanvas")) AutoLocate();
                using (new EditorGUI.DisabledScope(_target == null))
                {
                    if (GUILayout.Button("重新放进场景"))
                    {
                        SceneSetupTools.EnsureTutorialCanvasInScene();
                    }
                }
                if (GUILayout.Button("诊断")) SceneSetupTools.Diagnose();
            }

            if (_target != null)
            {
                bool isAsset = PrefabUtility.IsPartOfPrefabAsset(_target);
                EditorGUILayout.LabelField("修改方式",
                    isAsset ? "预制体资源（改动对所有未来的实例生效）" : "场景物体（仅当前场景）",
                    EditorStyles.miniLabel);
                if (isAsset)
                {
                    EditorGUILayout.HelpBox(
                        "你正在改预制体本身。这一改对 SampleScene 里已经放进去的实例也会生效（除非实例上覆盖了配置）。",
                        MessageType.None);
                }
            }
        }

        private void DrawButtonSection()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("② 墨色按钮（从左到右）", EditorStyles.boldLabel);

            if (_target == null)
            {
                EditorGUILayout.HelpBox("先指定面板。", MessageType.Warning);
                return;
            }

            if (_buttons.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "没扫到按钮。需要 TutorialCanvas/Palette 下有 5 个带 Button 组件的子物体。",
                    MessageType.Error);
                return;
            }

            if (_thumbStyle == null)
            {
                _thumbStyle = new GUIStyle(GUI.skin.box) { margin = new RectOffset(2, 6, 2, 2) };
            }

            for (int i = 0; i < _buttons.Count; i++)
            {
                var btn = _buttons[i];
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    bool isCorrect = _correctIndex == i;
                    if (GUILayout.Toggle(isCorrect, "", GUILayout.Width(16)) != isCorrect)
                        _correctIndex = i;

                    var tex = LoadTexture("UI/ink_" + i);
                    if (tex != null)
                        GUILayout.Label(tex, _thumbStyle, GUILayout.Width(38), GUILayout.Height(36));
                    else
                        GUILayout.Box("?", GUILayout.Width(38), GUILayout.Height(36));

                    GUILayout.Label($"Ink{i}", GUILayout.Width(46));

                    if (btn != null)
                    {
                        GUILayout.Label(btn.name, EditorStyles.miniLabel);
                        if (isCorrect) GUILayout.Label("← 正确答案", EditorStyles.miniBoldLabel);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("选中", GUILayout.Width(46)))
                        {
                            Selection.activeGameObject = btn.gameObject;
                            EditorGUIUtility.PingObject(btn.gameObject);
                        }
                    }
                    else
                    {
                        EditorGUILayout.LabelField("Missing", EditorStyles.miniLabel);
                    }
                }
            }

            EditorGUILayout.LabelField("正确答案索引", _correctIndex.ToString(), EditorStyles.miniLabel);
        }

        private void DrawParamsSection()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("③ 反馈图与淡出参数", EditorStyles.boldLabel);

            _yesPath = EditorGUILayout.TextField("对图 Resources 路径", _yesPath);
            _noPath = EditorGUILayout.TextField("错图 Resources 路径", _noPath);
            _feedbackSize = EditorGUILayout.Vector2Field("反馈图外框尺寸", _feedbackSize);
            _feedbackOffset = EditorGUILayout.Vector2Field("反馈图位置偏移（相对按钮中心）", _feedbackOffset);
            _randomRotation = EditorGUILayout.Toggle("随机小角度旋转", _randomRotation);

            EditorGUILayout.Space(2);
            _fadeDelay = EditorGUILayout.Slider("点对后停顿 (秒)", _fadeDelay, 0f, 2f);
            _fadeDuration = EditorGUILayout.Slider("单个按钮淡出时长", _fadeDuration, 0.05f, 3f);
            using (new EditorGUI.DisabledScope(_fadeAllAtOnce))
            {
                _fadeStagger = EditorGUILayout.Slider("相邻按钮错开时间", _fadeStagger, 0f, 0.5f);
            }
            _fadeAllAtOnce = EditorGUILayout.Toggle("5 个按钮同时淡出", _fadeAllAtOnce);
            _deactivateWhenDone = EditorGUILayout.Toggle("淡出后隐藏按钮物体", _deactivateWhenDone);
        }

        private void DrawActionsSection()
        {
            EditorGUILayout.Space(8);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_target == null || _buttons.Count == 0))
                {
                    if (GUILayout.Button("一键配置", GUILayout.Height(30))) Apply();
                }
                if (GUILayout.Button("校验", GUILayout.Height(30), GUILayout.Width(80)))
                    _issues = AncientPaintingValidation.ValidateAll();
            }

            EditorGUILayout.Space(4);
        }

        private void DrawIssueSection()
        {
            if (_issues == null) return;

            EditorGUILayout.LabelField("体检结果", EditorStyles.boldLabel);
            int errors = 0;
            foreach (var i in _issues) if (i.IsError) errors++;

            EditorGUILayout.HelpBox(
                errors == 0 ? "没有发现错误。" : $"发现 {errors} 个错误，运行前必须修掉。",
                errors == 0 ? MessageType.Info : MessageType.Error);

            foreach (var issue in _issues)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(issue.ToString(), EditorStyles.wordWrappedMiniLabel);
                    if (issue.Context != null && GUILayout.Button("定位", GUILayout.Width(46)))
                    {
                        Selection.activeObject = issue.Context;
                        EditorGUIUtility.PingObject(issue.Context);
                    }
                }
            }
        }

        // ── 写配置 ──────────────────────────────────────────────────

        private void Apply()
        {
            if (_target == null) return;

            bool editingAsset = PrefabUtility.IsPartOfPrefabAsset(_target);

            if (editingAsset)
            {
                string path = AssetDatabase.GetAssetPath(_target);
                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    ApplyTo(contents, assetContext: true);
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[墨色向导] 已把配置写入预制体 {path}");
            }
            else
            {
                Undo.RegisterFullObjectHierarchyUndo(_target, "配置墨色步骤");
                ApplyTo(_target, assetContext: false);
                if (_target.scene.IsValid()) EditorSceneManager.MarkSceneDirty(_target.scene);
                Debug.Log($"[墨色向导] 已把配置写入场景物体 {_target.name}");
            }

            Refresh();
            _issues = AncientPaintingValidation.ValidateAll();
            Repaint();
        }

        private void ApplyTo(GameObject root, bool assetContext)
        {
            var buttons = AncientPaintingValidation.FindInkButtons(root);

            // 挂在 Palette 上：这个步骤的职责就是管这一排按钮
            var palette = AncientPaintingValidation.FindByName(root.transform, AncientPaintingValidation.PaletteObjectName);
            GameObject host = palette != null ? palette.gameObject : root;

            var step = host.GetComponent<InkColorStep>();
            if (step == null)
                step = assetContext ? host.AddComponent<InkColorStep>() : Undo.AddComponent<InkColorStep>(host);

            var so = new SerializedObject(step);

            SetArray(so.FindProperty("inkButtons"), buttons);
            so.FindProperty("buttonContainerName").stringValue = AncientPaintingValidation.PaletteObjectName;
            so.FindProperty("correctIndex").intValue = Mathf.Clamp(_correctIndex, 0, Mathf.Max(0, buttons.Count - 1));
            so.FindProperty("yesResourcePath").stringValue = _yesPath;
            so.FindProperty("noResourcePath").stringValue = _noPath;
            so.FindProperty("feedbackSize").vector2Value = _feedbackSize;
            so.FindProperty("feedbackOffset").vector2Value = _feedbackOffset;
            so.FindProperty("randomRotation").boolValue = _randomRotation;
            so.FindProperty("fadeDelay").floatValue = _fadeDelay;
            so.FindProperty("fadeDuration").floatValue = _fadeDuration;
            so.FindProperty("fadeStagger").floatValue = _fadeStagger;
            so.FindProperty("fadeAllAtOnce").boolValue = _fadeAllAtOnce;
            so.FindProperty("deactivateWhenDone").boolValue = _deactivateWhenDone;

            if (!assetContext) Undo.RecordObject(step, "配置墨色步骤");
            so.ApplyModifiedProperties();
        }

        private static void SetArray(SerializedProperty prop, List<Button> items)
        {
            if (prop == null) return;
            prop.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        // 老的 InstantiateIntoScene 已挪到 SceneSetupTools，这里只保留小工具
        // ── 小工具 ──────────────────────────────────────────────────

        private static Texture2D LoadTexture(string resourcePath)
            => AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/" + resourcePath + ".png");
    }
}
