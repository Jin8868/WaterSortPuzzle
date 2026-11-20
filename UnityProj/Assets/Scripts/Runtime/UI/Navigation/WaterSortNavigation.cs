using System.Threading;
using AlloyFramework.UI;
using Cysharp.Threading.Tasks;
using Game.UI;

namespace Game.WaterSortPuzzle
{
    /// <summary>通过框架打开选关页与游戏页，并协调页面交接。</summary>
    internal static class WaterSortNavigation
    {
        private static CancellationTokenSource m_lifetime; // 当前业务会话的导航生命周期。
        private static UniTaskCompletionSource m_transition; // 阻止重复点击并等待在途切换退出。

        internal static async UniTask StartAsync(CancellationToken cancellationToken)
        {
            m_lifetime = new CancellationTokenSource();
            using (var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_lifetime.Token))
            {
                await SwitchAsync(0, startup.Token);
            }
        }

        internal static UniTask OpenSelectionAsync()
        {
            return m_lifetime == null ? UniTask.CompletedTask : SwitchAsync(0, m_lifetime.Token);
        }

        internal static UniTask OpenLevelAsync(int levelID)
        {
            if (m_lifetime == null || !WaterSortProgress.IsUnlocked(levelID)) return UniTask.CompletedTask;
            return SwitchAsync(levelID, m_lifetime.Token);
        }

        private static async UniTask SwitchAsync(int levelID, CancellationToken cancellationToken)
        {
            if (m_transition != null || cancellationToken.IsCancellationRequested) return;
            var transition = new UniTaskCompletionSource();
            m_transition = transition;
            try
            {
                // 目标页加载完成后才交接，失败时来源页仍可操作。
                UIManager manager = UIManager.Instance;
                if (levelID == 0)
                {
                    await manager.OpenAsync(GameUI.LevelSelectUI, cancellationToken);
                    if (manager.IsOpen(GameUI.GamePlayUI)) await manager.CloseAsync(GameUI.GamePlayUI);
                }
                else
                {
                    var handle = await manager.OpenAsync(GameUI.GamePlayUI, cancellationToken);
                    try
                    {
                        // 生成的界面定义没有数据参数，通过实际打开的实例接收关卡编号。
                        handle.Controller.EnterLevel(levelID);
                    }
                    catch
                    {
                        await handle.CloseAsync();
                        throw;
                    }
                    if (manager.IsOpen(GameUI.LevelSelectUI)) await manager.CloseAsync(GameUI.LevelSelectUI);
                }
            }
            finally
            {
                m_transition = null;
                transition.TrySetResult();
            }
        }

        internal static async UniTask ShutdownAsync()
        {
            if (m_lifetime == null) return;
            var lifetime = m_lifetime;
            m_lifetime = null;
            try
            {
                // 等待打开流程取消或关闭完成，再让两个页面释放各自的监听与资源。
                var transition = m_transition;
                lifetime.Cancel();
                if (transition != null) await transition.Task;
                UIManager manager = UIManager.Instance;
                if (manager == null) return;
                if (manager.IsOpen(GameUI.GamePlayUI)) await manager.CloseAsync(GameUI.GamePlayUI);
                if (manager.IsOpen(GameUI.LevelSelectUI)) await manager.CloseAsync(GameUI.LevelSelectUI);
            }
            finally
            {
                lifetime.Dispose();
            }
        }
    }
}
