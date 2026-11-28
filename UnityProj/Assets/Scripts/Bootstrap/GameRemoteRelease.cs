using System;
using System.Collections.Generic;
using System.Threading;
using AlloyFramework;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Game
{
    internal static class GameRemoteRelease
    {
        private static GameHotUpdateSettings m_settings; // 启动前读取的资源发布配置。
        private static GameReleaseManifest m_release; // 本次启动固定的发布描述。
        private static string m_channelURL; // 当前平台和渠道的发布根地址。

        internal static void Configure(GameHotUpdateSettings settings)
        {
            // 显式清理设置，支持关闭域重载后的重复编辑器运行。
            m_settings = settings;
            m_release = null;
            m_channelURL = null;
            ResourceSettings.StartupPreparation = null;
            ResourceSettings.PackageVersionProvider = null;
            ResourceSettings.RemoteUrlProvider = null;
#if UNITY_EDITOR
            bool host = settings.ResourceMode == EGameResourceMode.HotUpdate && settings.EditorHostMode;
            ResourceSettings.PlayModeOverride = host ? ResourcePlayMode.Host : ResourcePlayMode.EditorSimulate;
#else
            bool host = settings.ResourceMode == EGameResourceMode.HotUpdate;
            ResourceSettings.PlayModeOverride = host ? ResourcePlayMode.Host : ResourcePlayMode.Offline;
#endif
            if (host)
            {
                ResourceSettings.StartupPreparation = PrepareAsync;
                ResourceSettings.PackageVersionProvider = GetPackageVersion;
                ResourceSettings.RemoteUrlProvider = GetRemoteURL;
            }
        }

        private static async UniTask PrepareAsync(CancellationToken cancellationToken)
        {
            // 发布指针按 YooAsset 构建目标和渠道定位，本次启动使用指针指定的固定发布。
            string platform = GetPlatform();
            m_channelURL = m_settings.ServerURL + "/" + platform + "/" + m_settings.Channel;
            string url = m_channelURL + "/release.json";
            StartupLoading.ReportOperation(EStartupLoadingStage.CheckingRelease, "检查资源版本");
            for (int attempt = 0; attempt <= m_settings.RequestRetryCount; attempt++)
            {
                // 每次重试独立请求；取消时释放请求并终止网络传输。
                using (UnityWebRequest request = UnityWebRequest.Get(url))
                {
                    request.timeout = m_settings.RequestTimeoutSeconds;
                    UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                    try
                    {
                        while (!operation.isDone)
                        {
                            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                        }
                        cancellationToken.ThrowIfCancellationRequested();
                        if (request.result == UnityWebRequest.Result.Success)
                        {
                            GameReleaseManifest release = JsonUtility.FromJson<GameReleaseManifest>(
                                request.downloadHandler.text);
                            ValidateRelease(release, platform);
                            m_release = release;
                            Debug.Log($"[YooAsset] 已固定资源发布 {release.ReleaseID}，平台={platform}");
                            return;
                        }
                        if (attempt == m_settings.RequestRetryCount)
                        {
                            throw new InvalidOperationException($"发布信息请求失败：{url}，{request.error}");
                        }
                    }
                    finally
                    {
                        if (!operation.isDone)
                        {
                            request.Abort();
                        }
                    }
                }
                await UniTask.Delay(1000, cancellationToken: cancellationToken);
            }
        }

        private static string GetPlatform()
        {
            // 编辑器模拟当前构建目标；Player 使用发布工具对应的目标名称。
#if UNITY_EDITOR
            return UnityEditor.EditorUserBuildSettings.activeBuildTarget.ToString();
#elif UNITY_ANDROID
            return "Android";
#elif UNITY_STANDALONE_WIN
            return "StandaloneWindows64";
#else
            throw new InvalidOperationException("当前平台尚未配置远端资源发布。");
#endif
        }

        private static void ValidateRelease(GameReleaseManifest release, string platform)
        {
            if (release == null || release.Platform != platform ||
                !GameHotUpdateSettings.IsPathSegment(release.ReleaseID) || release.Packages == null)
            {
                throw new InvalidOperationException("远端发布缺失、格式无效或平台与当前主包不匹配。");
            }

            // 确保包名和版本可用于发布路径，并校验启动依赖的资源包齐备。
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (GameReleasePackage package in release.Packages)
            {
                if (package == null || !GameHotUpdateSettings.IsPathSegment(package.Name) ||
                    !GameHotUpdateSettings.IsPathSegment(package.Version) || !names.Add(package.Name))
                {
                    throw new InvalidOperationException("远端发布包含非法包名、版本或重复包。");
                }
            }
            if (!names.Contains(ResourceSettings.DefaultPackageName) || !names.Contains("AudioPackage"))
            {
                throw new InvalidOperationException("远端发布缺少当前主包必需的资源包。");
            }
        }

        private static string GetPackageVersion(string packageName)
        {
            if (m_release != null)
            {
                foreach (GameReleasePackage package in m_release.Packages)
                {
                    if (package.Name == packageName)
                    {
                        return package.Version;
                    }
                }
            }
            throw new InvalidOperationException($"本次发布没有指定资源包版本：{packageName}");
        }

        private static string GetRemoteURL(string packageName, string fileName)
        {
            GetPackageVersion(packageName);
            return m_channelURL + "/releases/" + m_release.ReleaseID + "/" + packageName + "/" +
                Uri.EscapeDataString(fileName);
        }
    }
}
