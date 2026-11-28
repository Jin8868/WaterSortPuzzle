using AlloyFramework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using YooAsset.Editor;
using BuildReport = UnityEditor.Build.Reporting.BuildReport;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Game.Editor
{
    /// <summary>构建安装包与 YooAsset 资源补丁，并将资源发布到本机目录。</summary>
    public static class GameBuildPipeline
    {
        private static bool m_running; // 防止同一编辑器并发执行构建。
        private static string m_currentStep; // 当前执行步骤，用于定位失败阶段。
        private static Stopwatch m_stepTimer; // 当前步骤的实际耗时。

        private sealed class BuiltPackage
        {
            /// <summary>步骤输出目录。</summary>
            public string Directory;
            /// <summary>资源包版本。</summary>
            public string Version;
        }

        [MenuItem("★AlloyFramework★/出包/一键完整出包")]
        private static void BuildMainPackage()
        {
            Run(false);
        }

        [MenuItem("★AlloyFramework★/出包/一键生成资源热更新补丁")]
        private static void BuildPatch()
        {
            Run(true);
        }

        [MenuItem("★AlloyFramework★/出包/构建配置/配置 Windows 与 Android 安装包")]
        private static void ConfigurePlayer()
        {
            CheckEditorState();
            ConfigurePlayer(GameHotUpdateSettings.Load());
        }

        private static void Run(bool patch)
        {
            CheckEditorState();
            m_running = true;
            var buildTimer = Stopwatch.StartNew();
            BeginStep("构建输入检查", patch ? "热更新补丁" : "完整主包");
            try
            {
                AssetDatabase.SaveAssets();
                BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
                if (target != BuildTarget.StandaloneWindows64 && target != BuildTarget.Android)
                {
                    throw new BuildFailedException("一键出包只支持 Windows x64 和 Android。");
                }
                if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
                {
                    throw new BuildFailedException($"缺少 {target} 平台模块，请通过 Unity Hub 安装。");
                }
                if (!patch && target == BuildTarget.Android && EditorUserBuildSettings.exportAsGoogleAndroidProject)
                {
                    throw new BuildFailedException("一键出包需要关闭 Export Project，直接生成 APK 或 AAB。");
                }
                GameHotUpdateSettings settings = GameHotUpdateSettings.Load();
                EndStep("完成", $"平台={target}，资源模式={settings.ResourceMode}");
                if (patch)
                {
                    GeneratePatch(target, settings.Channel);
                }
                else
                {
                    GenerateMain(target, settings);
                }
                Debug.Log($"[Build][全部完成] 总耗时={buildTimer.Elapsed.TotalSeconds:F1}秒");
            }
            catch (Exception exception)
            {
                EndStep("失败", exception.Message);
                Debug.LogError($"[Build][构建失败] 总耗时={buildTimer.Elapsed.TotalSeconds:F1}秒");
                Debug.LogException(exception);
                throw;
            }
            finally
            {
                m_running = false;
                m_currentStep = null;
                m_stepTimer = null;
                EditorUtility.ClearProgressBar();
            }
        }

        private static void GenerateMain(BuildTarget target, GameHotUpdateSettings settings)
        {
            ConfigurePlayer(settings);

            // 主包将本次资源包写入 StreamingAssets，供离线启动使用。
            var packages = new List<BuiltPackage>();
            packages.Add(BuildPackage("AudioPackage", true, "1/3"));
            packages.Add(BuildPackage("DefaultPackage", true, "2/3"));

            BeginStep("3/3 Unity Player", "重新构建完整安装包");
            RemoveLegacyBuiltinVideoPackage();
            string playerDirectory = Path.Combine("BuildArtifacts/Players", target.ToString());
            string playerPath = Path.Combine(playerDirectory, target == BuildTarget.Android ?
                "game-template" + (EditorUserBuildSettings.buildAppBundle ? ".aab" : ".apk") : "game-template.exe");
            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                {
                    scenes.Add(scene.path);
                }
            }
            if (scenes.Count == 0)
            {
                throw new BuildFailedException("Build Settings 没有启用的启动场景。");
            }

            // 收集内置资源后构建 Player，成功后才更新服务器发布指针。
            Directory.CreateDirectory(playerDirectory);
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                target = target,
                locationPathName = playerPath,
                options = GetPlayerOptions()
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                throw new BuildFailedException($"Player 构建失败：{report.summary.result}");
            }
            EndStep("完成", $"输出={Path.GetFullPath(playerPath)}");

            // 新主包成功后，发布对应的完整资源集合。
            BeginStep("发布主包资源", "复制本次构建资源并更新服务器版本指针");
            string releaseID = GetReleaseID();
            string published = Publish(target, settings.Channel, releaseID, packages);
            EndStep("完成", $"版本={releaseID}，输出={Path.GetFullPath(published)}");
            Debug.Log($"[Build] 一键完整出包完成：{Path.GetFullPath(playerPath)}");
            EditorUtility.RevealInFinder(Path.GetFullPath(playerPath));
        }

        private static void GeneratePatch(BuildTarget target, string channel)
        {
            // 补丁只输出发布资源包，保留已有安装包和 StreamingAssets。
            var packages = new List<BuiltPackage>();
            packages.Add(BuildPackage("AudioPackage", false, "1/2"));
            packages.Add(BuildPackage("DefaultPackage", false, "2/2"));

            BeginStep("发布资源热更新补丁", "复制发布文件并更新服务器版本指针");
            string releaseID = GetReleaseID();
            string published = Publish(target, channel, releaseID, packages);
            EndStep("完成", $"版本={releaseID}，输出={Path.GetFullPath(published)}");
        }

        private static string GetReleaseID()
        {
            return DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        }

        private static void RemoveLegacyBuiltinVideoPackage()
        {
            // 清除升级前生成的内置包，避免旧视频随新安装包发布。
            string packagePath = Path.Combine(AssetBundleBuilderHelper.GetStreamingAssetsRoot(), "VideoPackage")
                .Replace('\\', '/');
            if (Directory.Exists(packagePath) && !AssetDatabase.DeleteAsset(packagePath))
            {
                throw new BuildFailedException("无法清理旧视频内置包：" + packagePath);
            }
        }

        private static BuiltPackage BuildPackage(string packageName, bool buildin, string phase)
        {
            BeginStep($"{phase} {packageName}", buildin ? "构建资源包并复制内置资源" : "构建补丁资源包");
            string version = GetReleaseID();
            string directory = GamePackageBuildService.Build(packageName, version, buildin);
            EndStep("完成", $"版本={version}，输出={Path.GetFullPath(directory)}");
            return new BuiltPackage { Directory = directory, Version = version };
        }

        private static void BeginStep(string step, string detail)
        {
            m_currentStep = step;
            m_stepTimer = Stopwatch.StartNew();
            Debug.Log($"[Build][开始] {step} | {detail}");
        }

        private static void EndStep(string status, string detail)
        {
            double seconds = m_stepTimer == null ? 0 : m_stepTimer.Elapsed.TotalSeconds;
            string message = $"[Build][{status}] {m_currentStep} | 耗时={seconds:F1}秒 | {detail}";
            if (status == "失败")
            {
                Debug.LogError(message);
            }
            else
            {
                Debug.Log(message);
            }
            m_stepTimer?.Stop();
        }

        private static string Publish(BuildTarget target, string channel, string releaseID, List<BuiltPackage> packages)
        {
            string root = Path.Combine("BuildArtifacts/LocalCDN", target.ToString(), channel);
            string destination = Path.Combine(root, "releases", releaseID);
            var manifest = new GameReleaseManifest
            {
                Platform = target.ToString(),
                ReleaseID = releaseID,
                Packages = new GameReleasePackage[packages.Count]
            };
            for (int index = 0; index < packages.Count; index++)
            {
                BuiltPackage package = packages[index];
                string packageName = new DirectoryInfo(package.Directory).Parent.Name;
                manifest.Packages[index] = new GameReleasePackage { Name = packageName, Version = package.Version };
            }
            string json = JsonUtility.ToJson(manifest, true);
            if (Directory.Exists(destination))
            {
                // 已有版本目录必须完全匹配，不能覆盖旧发布。
                if (!File.Exists(Path.Combine(destination, "release-manifest.json")) ||
                    File.ReadAllText(Path.Combine(destination, "release-manifest.json")) != json)
                {
                    throw new BuildFailedException("发布目录已存在且内容不同，拒绝覆盖不可变版本。");
                }
                for (int index = 0; index < packages.Count; index++)
                {
                    VerifyCopy(packages[index].Directory, Path.Combine(destination, manifest.Packages[index].Name));
                }
            }
            else
            {
                string staging = Path.Combine(root, "staging", releaseID);
                if (Directory.Exists(staging))
                {
                    throw new BuildFailedException("存在同名未完成发布，请检查 staging 目录。");
                }
                Directory.CreateDirectory(staging);
                for (int index = 0; index < packages.Count; index++)
                {
                    string copy = Path.Combine(staging, manifest.Packages[index].Name);
                    CopyDirectory(packages[index].Directory, copy);
                    VerifyCopy(packages[index].Directory, copy);
                }
                File.WriteAllText(Path.Combine(staging, "release-manifest.json"), json);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                Directory.Move(staging, destination);
            }
            // 完整发布目录提交成功后，原子替换小指针；启动时只读取一次该指针。
            string pointer = Path.Combine(root, "release.json");
            string pending = pointer + ".pending";
            File.WriteAllText(pending, json);
            if (File.Exists(pointer))
            {
                File.Replace(pending, pointer, null);
            }
            else
            {
                File.Move(pending, pointer);
            }
            return destination;
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(source.TrimEnd('/', '\\').Length).TrimStart('/', '\\');
                string target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, false);
            }
        }

        private static void VerifyCopy(string source, string destination)
        {
            string[] files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
            if (!Directory.Exists(destination) || files.Length !=
                Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Length)
            {
                throw new BuildFailedException("发布文件集合不完整，保留原发布指针。");
            }
            // 检查复制后的文件集合和长度，避免再次完整读取资源文件计算哈希。
            foreach (string file in files)
            {
                string relative = file.Substring(source.TrimEnd('/', '\\').Length).TrimStart('/', '\\');
                string copy = Path.Combine(destination, relative);
                if (!File.Exists(copy) || new FileInfo(file).Length != new FileInfo(copy).Length)
                {
                    throw new BuildFailedException($"发布文件校验失败：{relative}");
                }
            }
        }

        private static BuildOptions GetPlayerOptions()
        {
            BuildOptions options = BuildOptions.None;
            if (EditorUserBuildSettings.development)
            {
                options |= BuildOptions.Development;
            }
            if (EditorUserBuildSettings.allowDebugging && EditorUserBuildSettings.development)
            {
                options |= BuildOptions.AllowDebugging;
            }
            if (EditorUserBuildSettings.connectProfiler)
            {
                options |= BuildOptions.ConnectWithProfiler;
            }
            return options;
        }

        private static void CheckEditorState()
        {
            if (m_running || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                throw new BuildFailedException("请退出运行模式并等待脚本编译完成，不能并发构建。");
            }
        }

        private static void ConfigurePlayer(GameHotUpdateSettings settings)
        {
            // 保持 Windows 和 Android 的现有安装包目标及 API 环境。
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Standalone, ApiCompatibilityLevel.NET_Unity_4_8);
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Android, ApiCompatibilityLevel.NET_Unity_4_8);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;

            if (settings.ResourceMode == EGameResourceMode.HotUpdate &&
                settings.ServerURL.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                // 使用本地 HTTP 服务时，允许 Player 访问明文资源地址。
                PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[Build] Windows x64 和 Android ARM64 安装包配置完成。");
        }
    }
}
