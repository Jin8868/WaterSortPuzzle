using System;
using System.IO;
using System.Threading;
using AlloyFramework;
using AlloyFramework.Audio;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Game.WaterSortPuzzle
{
    /// <summary>管理背景音乐与音效设置，并在本地独立保存。</summary>
    internal sealed class WaterSortAudioSettingsModel : ModelBase
    {
        private const string FILENAME = "WaterSortAudioSettings.json"; // 本地音频设置文件名。
        private const int SAVEVERSION = 1; // 当前音频设置文件结构版本。

        private AudioSettingsData m_settings = new AudioSettingsData(); // 当前生效的音频设置。
        private bool m_isLoaded; // 是否已完成首次加载。
        private bool m_isDirty; // 当前设置是否有未保存的改动。

        internal float BGMVolume => m_settings.BGMVolume;

        internal float SFXVolume => m_settings.SFXVolume;

        internal bool BGMEnabled => m_settings.BGMEnabled;

        internal bool SFXEnabled => m_settings.SFXEnabled;

        internal async UniTask LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!m_isLoaded)
            {
                // 等待文件读取完成后才发布设置，避免取消或并发加载覆盖当前改动。
                AudioSettingsData savedSettings = await ReadSettingsAsync();
                cancellationToken.ThrowIfCancellationRequested();
                if (!m_isLoaded && !m_isDirty && savedSettings != null)
                    m_settings = savedSettings;
                m_isLoaded = true;
            }

            // 音频后端就绪后，将存档中的开关和音量换算为实际播放音量。
            ApplyBGMVolume();
            ApplySFXVolume();
        }

        internal void SetBGMVolume(float volume)
        {
            if (!IsValidVolume(volume) || BGMVolume == volume)
                return;

            m_settings.BGMVolume = volume;
            m_isDirty = true;
            ApplyBGMVolume();
        }

        internal void SetSFXVolume(float volume)
        {
            if (!IsValidVolume(volume) || SFXVolume == volume)
                return;

            m_settings.SFXVolume = volume;
            m_isDirty = true;
            ApplySFXVolume();
        }

        internal void SetBGMEnabled(bool enabled)
        {
            if (BGMEnabled == enabled)
                return;

            m_settings.BGMEnabled = enabled;
            m_isDirty = true;
            ApplyBGMVolume();
        }

        internal void SetSFXEnabled(bool enabled)
        {
            if (SFXEnabled == enabled)
                return;

            m_settings.SFXEnabled = enabled;
            m_isDirty = true;
            ApplySFXVolume();
        }

        internal void Save()
        {
            if (!m_isDirty)
                return;

            try
            {
                // 在同一目录完成写入后替换正式文件，保留上次成功保存的内容直至提交。
                string directory = Application.persistentDataPath;
                string path = Path.Combine(directory, FILENAME);
                string temporaryPath = path + ".tmp";
                m_settings.Version = SAVEVERSION;
                Directory.CreateDirectory(directory);
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(m_settings));
                if (File.Exists(path))
                    File.Replace(temporaryPath, path, null);
                else
                    File.Move(temporaryPath, path);

                m_isDirty = false;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
                || exception is NotSupportedException)
            {
                // 保留待保存状态，下次关闭设置或暂停应用时可重试。
                Debug.LogWarning($"无法保存音频设置：{exception.Message}");
            }
        }

        private static async UniTask<AudioSettingsData> ReadSettingsAsync()
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, FILENAME);
                string json;
                using (var reader = new StreamReader(path))
                {
                    json = await reader.ReadToEndAsync();
                }

                // 从默认值覆盖读取，兼容缺少单个音频字段的设置文件。
                var settings = new AudioSettingsData();
                JsonUtility.FromJsonOverwrite(json, settings);
                if (settings.Version != SAVEVERSION || !IsValidVolume(settings.BGMVolume)
                    || !IsValidVolume(settings.SFXVolume))
                    throw new InvalidDataException("音频设置的版本或音量无效。");

                return settings;
            }
            catch (Exception exception) when (exception is FileNotFoundException
                || exception is DirectoryNotFoundException)
            {
                return null;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
                || exception is ArgumentException || exception is InvalidDataException)
            {
                Debug.LogWarning($"无法读取音频设置，将使用默认值：{exception.Message}");
                return null;
            }
        }

        private void ApplyBGMVolume()
        {
            AudioManager audio = AudioManager.Instance;
            if (audio != null && audio.IsReady)
                audio.SetBGMVolume(BGMEnabled ? BGMVolume : 0f);
        }

        private void ApplySFXVolume()
        {
            AudioManager audio = AudioManager.Instance;
            if (audio != null && audio.IsReady)
                audio.SetSFXVolume(SFXEnabled ? SFXVolume : 0f);
        }

        private static bool IsValidVolume(float volume)
        {
            return volume >= 0f && volume <= 1f;
        }

        [Serializable]
        private sealed class AudioSettingsData
        {
            /// <summary>设置文件结构版本。</summary>
            public int Version;

            /// <summary>背景音乐开启时使用的音量。</summary>
            public float BGMVolume = 1f;

            /// <summary>音效开启时使用的音量。</summary>
            public float SFXVolume = 1f;

            /// <summary>是否开启背景音乐。</summary>
            public bool BGMEnabled = true;

            /// <summary>是否开启音效。</summary>
            public bool SFXEnabled = true;
        }
    }
}
