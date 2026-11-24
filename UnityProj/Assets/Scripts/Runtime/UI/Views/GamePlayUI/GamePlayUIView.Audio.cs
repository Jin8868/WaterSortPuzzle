using System;
using System.Threading;
using AlloyFramework.Audio;
using Cysharp.Threading.Tasks;
using Game.WaterSortPuzzle;
using UnityEngine;

namespace Game.UI
{
    public sealed partial class GamePlayUIView
    {
        private const string MUSICEVENT = "Play_WaterSortBGM"; // 由 Wwise 素材设置循环。
        private const string TICKEVENT = "Play_WaterSortCountdown"; // 最后五秒的短促提示。
        private const int COUNTDOWNSECONDS = 5; // 开始逐秒提示的剩余秒数。
        private AudioScope m_audioScope; // 页面持有的音频资源租约和播放请求。
        private long m_musicPlayID; // 本局连续播放的背景音乐。
        private long m_tickPlayID; // 切关时停止上一局尚未结束的倒数声。
        private int m_lastAudioSecond; // 加时后从新的剩余时间继续判断整秒边界。

        private async UniTask PrepareAudioAsync(CancellationToken cancellationToken)
        {
            var audio = AudioManager.Instance;
            var scope = audio.CreateScope("WaterSortGameplay");
            try
            {
                // 在玩法页接受操作前准备事件，避免短音效因首次加载而延迟响起。
                await audio.PreloadAudioAsync(MUSICEVENT, scope, cancellationToken);
                await audio.PreloadAudioAsync(TICKEVENT, scope, cancellationToken);
                await audio.PreloadAudioAsync(BottleCell.POUREVENT, scope, cancellationToken);
                m_audioScope = scope;
            }
            catch (OperationCanceledException)
            {
                scope.Dispose();
                throw;
            }
            catch (Exception exception)
            {
                scope.Dispose();
                Debug.LogWarning($"玩法音频资源加载失败：{exception.Message}");
            }
        }

        private void StartGameplayAudio()
        {
            if (m_audioScope == null) return;
            var audio = AudioManager.Instance;
            if (m_musicPlayID == 0)
            {
                var options = new AudioPlayOptions { Scope = m_audioScope, Volume = 0.48f };
                m_musicPlayID = audio.PlayAudio(MUSICEVENT, options);
            }

            // 重置时延续背景音乐，倒数以本轮时间为准。
            if (m_tickPlayID != 0) audio.StopAudio(m_tickPlayID);
            m_lastAudioSecond = int.MaxValue;
            UpdateCountdownAudio(Mathf.CeilToInt(m_controller.RemainingTime));
        }

        private void UpdateCountdownAudio(int seconds)
        {
            if (seconds == m_lastAudioSecond || m_audioScope == null) return;
            if (seconds < m_lastAudioSecond && seconds > 0 && seconds <= COUNTDOWNSECONDS)
            {
                var options = new AudioPlayOptions { Scope = m_audioScope, Volume = 0.8f };
                m_tickPlayID = AudioManager.Instance.PlayAudio(TICKEVENT, options);
            }
            m_lastAudioSecond = seconds;
        }

        private void StopGameplayAudio()
        {
            if (m_musicPlayID != 0) AudioManager.Instance.StopAudio(m_musicPlayID);
            if (m_tickPlayID != 0) AudioManager.Instance.StopAudio(m_tickPlayID);
            m_musicPlayID = 0;
            m_tickPlayID = 0;
        }
    }
}
