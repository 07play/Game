using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DrawFlower.EditorTools
{
    /// <summary>
    /// 导入约束：Assets/Resources/UI 下的图片必须是「Sprite / Single / 关 Mipmap」。
    ///
    /// 为什么需要它：美术往这个文件夹里丢一张新图，如果忘了改 Texture Type，
    /// Resources.Load&lt;Sprite&gt; 会静默返回 null，游戏里就是「点错了什么都不出现」，
    /// 而且要等打包后在真机上才发现。这里在导入那一刻就把它掰正。
    /// </summary>
    public class UISpriteImportEnforcer : AssetPostprocessor
    {
        private const string UI_ROOT = "Assets/Resources/UI/";
        private const int MAX_SIZE = 1024;

        void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.StartsWith(UI_ROOT, System.StringComparison.OrdinalIgnoreCase)) return;

            var importer = assetImporter as TextureImporter;
            if (importer == null) return;

            // 首次导入 = 用我们的默认值，不啰嗦；之后再被改回来才提醒
            bool firstImport = importer.importSettingsMissing;
            var fixes = new List<string>();

            void Fix(bool needFix, System.Action apply, string label)
            {
                if (!needFix) return;
                apply();
                if (!firstImport) fixes.Add(label);
            }

            Fix(importer.textureType != TextureImporterType.Sprite,
                () => importer.textureType = TextureImporterType.Sprite, "Texture Type → Sprite");

            Fix(importer.spriteImportMode != SpriteImportMode.Single,
                () => importer.spriteImportMode = SpriteImportMode.Single, "Sprite Mode → Single");

            Fix(importer.mipmapEnabled,
                () => importer.mipmapEnabled = false, "Mip Maps → Off");

            Fix(!importer.alphaIsTransparency,
                () => importer.alphaIsTransparency = true, "Alpha Is Transparency → On");

            Fix(!importer.sRGBTexture,
                () => importer.sRGBTexture = true, "sRGB → On");

            Fix(importer.wrapMode != TextureWrapMode.Clamp,
                () => importer.wrapMode = TextureWrapMode.Clamp, "Wrap Mode → Clamp");

            Fix(importer.maxTextureSize > MAX_SIZE,
                () => importer.maxTextureSize = MAX_SIZE, $"Max Size → {MAX_SIZE}");

            // Android 走 ASTC，避免默认 RGBA32 白白吃掉手机内存
            var android = importer.GetPlatformTextureSettings("Android");
            if (!android.overridden || android.format != TextureImporterFormat.ASTC_6x6)
            {
                android.overridden = true;
                android.format = TextureImporterFormat.ASTC_6x6;
                importer.SetPlatformTextureSettings(android);
                if (!firstImport) fixes.Add("Android → ASTC 6x6");
            }

            if (fixes.Count > 0)
            {
                Debug.LogWarning(
                    $"[UI导入规范] 已自动修正 {path}：{string.Join("、", fixes)}。" +
                    "这一栏的设置是被统一接管的，别单独改回来。", importer);
            }
        }
    }

    /// <summary>
    /// 构建前校验：有错直接打断构建，而不是打完包到真机上才发现。
    /// </summary>
    public class AncientPaintingBuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var issues = AncientPaintingValidation.ValidateAll();
            var errors = issues.Where(i => i.IsError).ToList();

            if (errors.Count > 0)
            {
                var sb = new StringBuilder("古画教学 · 构建前校验失败：\n");
                foreach (var e in errors) sb.AppendLine("  · " + e.Message);
                sb.AppendLine("\n（菜单 Tools/古画教学/① 墨色步骤 · 配置向导 里可以一键修）");
                throw new BuildFailedException(sb.ToString());
            }

            int warnings = issues.Count - errors.Count;
            Debug.Log($"[古画教学] 构建前校验通过。提示 {warnings} 条，无阻断性错误。");
        }
    }

    /// <summary>
    /// 手动 / CI 入口。CI 里可以这样跑：
    /// Unity.exe -batchmode -quit -projectPath &lt;项目&gt; -executeMethod DrawFlower.EditorTools.AncientPaintingCLI.RunValidationBatch
    /// </summary>
    public static class AncientPaintingCLI
    {
        [MenuItem("Tools/古画教学/② 运行完整校验", false, 2)]
        public static void RunValidationMenu()
        {
            var issues = AncientPaintingValidation.ValidateAll();

            foreach (var i in issues)
            {
                if (i.IsError) Debug.LogError(i.ToString(), i.Context);
                else Debug.LogWarning(i.ToString(), i.Context);
            }

            int errors = issues.Count(i => i.IsError);
            EditorUtility.DisplayDialog(
                errors == 0 ? "古画教学 · 配置体检：通过" : "古画教学 · 配置体检：有问题",
                AncientPaintingValidation.Describe(issues),
                "好");
        }

        /// <summary>给 -batchmode 用：有错误就以非 0 退出码结束，让 CI 挂掉。</summary>
        public static void RunValidationBatch()
        {
            var issues = AncientPaintingValidation.ValidateAll();
            int errors = 0;

            foreach (var i in issues)
            {
                if (i.IsError)
                {
                    errors++;
                    Debug.LogError("[构建校验] " + i.Message);
                }
                else
                {
                    Debug.LogWarning("[构建校验] " + i.Message);
                }
            }

            Debug.Log($"=== 古画教学配置校验完成：{errors} 个错误，{issues.Count - errors} 个提示 ===");
            EditorApplication.Exit(errors == 0 ? 0 : 1);
        }
    }
}
