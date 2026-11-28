using AlloyFramework;
using AlloyFramework.UI;
using Cysharp.Threading.Tasks;
using Game.WaterSortPuzzle;
using UnityEngine;

namespace Game.UI
{
    public sealed class SettingsUIController : UIController<SettingsUIView>
    {
        private WaterSortAudioSettingsModel m_settings; // 当前使用的音频设置模型。

        protected override void OnCreate()
        {
            // 统一在创建和销毁时管理监听，刷新界面时不会重复注册。
            m_settings = ModelManager.Instance.Get<WaterSortAudioSettingsModel>();
            View.ApplicationSuspended += SaveSettings;
            View.CloseButton.onClick.AddListener(HandleClose);
            View.BGMSlider.onValueChanged.AddListener(HandleBGMVolumeChanged);
            View.SFXSlider.onValueChanged.AddListener(HandleSFXVolumeChanged);
            View.BGMToggle.onValueChanged.AddListener(HandleBGMEnabledChanged);
            View.SFXToggle.onValueChanged.AddListener(HandleSFXEnabledChanged);
        }

        protected override void OnInitData(UIEmptyData data)
        {
            DisplaySettings();
        }

        protected override void OnRefresh(UIEmptyData data)
        {
            DisplaySettings();
        }

        protected override void OnClose()
        {
            // 拖动时立即改变音量，在退出设置时统一写入存档。
            SaveSettings();
        }

        protected override void OnDispose()
        {
            View.ApplicationSuspended -= SaveSettings;
            View.CloseButton.onClick.RemoveListener(HandleClose);
            View.BGMSlider.onValueChanged.RemoveListener(HandleBGMVolumeChanged);
            View.SFXSlider.onValueChanged.RemoveListener(HandleSFXVolumeChanged);
            View.BGMToggle.onValueChanged.RemoveListener(HandleBGMEnabledChanged);
            View.SFXToggle.onValueChanged.RemoveListener(HandleSFXEnabledChanged);
        }

        private void DisplaySettings()
        {
            // 从已加载的设置恢复控件，避免初始化触发写入。
            View.BGMSlider.SetValueWithoutNotify(m_settings.BGMVolume);
            View.SFXSlider.SetValueWithoutNotify(m_settings.SFXVolume);
            View.BGMToggle.SetIsOnWithoutNotify(m_settings.BGMEnabled);
            View.SFXToggle.SetIsOnWithoutNotify(m_settings.SFXEnabled);

            // 静音状态下仍显示已保存的音量，方便开启前调整。
            View.BGMVolumeLabel.text = FormatVolume(m_settings.BGMVolume);
            View.SFXVolumeLabel.text = FormatVolume(m_settings.SFXVolume);
            View.BGMStateLabel.text = FormatState(m_settings.BGMEnabled);
            View.SFXStateLabel.text = FormatState(m_settings.SFXEnabled);
        }

        private void HandleBGMVolumeChanged(float volume)
        {
            m_settings.SetBGMVolume(volume);
            View.BGMVolumeLabel.text = FormatVolume(m_settings.BGMVolume);
        }

        private void HandleSFXVolumeChanged(float volume)
        {
            m_settings.SetSFXVolume(volume);
            View.SFXVolumeLabel.text = FormatVolume(m_settings.SFXVolume);
        }

        private void HandleBGMEnabledChanged(bool enabled)
        {
            m_settings.SetBGMEnabled(enabled);
            View.BGMStateLabel.text = FormatState(m_settings.BGMEnabled);
            SaveSettings();
        }

        private void HandleSFXEnabledChanged(bool enabled)
        {
            m_settings.SetSFXEnabled(enabled);
            View.SFXStateLabel.text = FormatState(m_settings.SFXEnabled);
            SaveSettings();
        }

        private void SaveSettings()
        {
            m_settings.Save();
        }

        private void HandleClose()
        {
            CloseAsync().Forget();
        }

        private static string FormatVolume(float volume)
        {
            return $"{Mathf.RoundToInt(volume * 100f)}%";
        }

        private static string FormatState(bool enabled)
        {
            return enabled ? "开启" : "关闭";
        }
    }
}
