using System;
using AlloyFramework.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public sealed class SettingsUIView : UIView
    {
        [SerializeField]
        private Button m_closeButton; // 关闭设置弹窗的按钮。

        [SerializeField]
        private Slider m_bgmSlider; // 背景音乐音量滑条。

        [SerializeField]
        private Slider m_sfxSlider; // 游戏音效音量滑条。

        [SerializeField]
        private Toggle m_bgmToggle; // 背景音乐开关。

        [SerializeField]
        private Toggle m_sfxToggle; // 游戏音效开关。

        [SerializeField]
        private Text m_bgmVolumeLabel; // 背景音乐音量百分比。

        [SerializeField]
        private Text m_sfxVolumeLabel; // 游戏音效音量百分比。

        [SerializeField]
        private Text m_bgmStateLabel; // 背景音乐开关状态。

        [SerializeField]
        private Text m_sfxStateLabel; // 游戏音效开关状态。

        internal event Action ApplicationSuspended;

        internal Button CloseButton => m_closeButton;
        internal Slider BGMSlider => m_bgmSlider;
        internal Slider SFXSlider => m_sfxSlider;
        internal Toggle BGMToggle => m_bgmToggle;
        internal Toggle SFXToggle => m_sfxToggle;
        internal Text BGMVolumeLabel => m_bgmVolumeLabel;
        internal Text SFXVolumeLabel => m_sfxVolumeLabel;
        internal Text BGMStateLabel => m_bgmStateLabel;
        internal Text SFXStateLabel => m_sfxStateLabel;

        private void OnApplicationPause(bool paused)
        {
            if (paused && isActiveAndEnabled)
                ApplicationSuspended?.Invoke();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && isActiveAndEnabled)
                ApplicationSuspended?.Invoke();
        }
    }
}
