using System.Globalization;
using AlloyFramework.GM;
using AlloyFramework.UI;
using Cysharp.Threading.Tasks;
using Game.UI;
using Game.WaterSortPuzzle;
using UnityEngine;
using UnityEngine.Scripting;

namespace Game
{
    // 框架从业务入口获取注册契约，命令由独立对象提供。
    public sealed partial class GameEntry : IGMProvider
    {
        /// <summary>在编辑器和开发构建中启用 GM。</summary>
        public bool IsGMEnabled => Application.isEditor || Debug.isDebugBuild;

        /// <summary>注册本轮倒水游戏的 GM 命令。</summary>
        /// <param name="registry">框架提供的命令注册器。</param>
        public void RegisterCommands(GMRegistry registry)
        {
            registry.Register(new WaterSortGMCommands());
        }
    }
}

namespace Game.WaterSortPuzzle
{
    /// <summary>集中声明倒水游戏的 GM 命令并处理面板输入。</summary>
    internal sealed class WaterSortGMCommands
    {
        [Preserve]
        [GMCommand("输出关卡目录信息")]
        private void LogLevelCatalog()
        {
            Debug.Log($"倒水游戏已加载 {WaterSortLevelCatalog.Count} 个关卡。");
        }

        [Preserve]
        [GMCommand("跳转关卡（关卡 ID）")]
        private void JumpToLevel(string input)
        {
            // 只把已载入目录中的关卡编号交给导航层。
            int levelCount = WaterSortLevelCatalog.Count;
            if (!int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out int levelID)
                || levelID < 1 || levelID > levelCount)
            {
                Debug.LogWarning($"请输入 1 到 {levelCount} 之间的关卡 ID。");
                return;
            }

            JumpToLevelAsync(levelID).Forget(Debug.LogException);
        }

        private async UniTask JumpToLevelAsync(int levelID)
        {
            if (await WaterSortNavigation.TryStartLevelAsync(levelID))
            {
                // 切关成功后收起上一局的确认弹窗和本次命令面板。
                await UIManager.Instance.CloseAsync(GameUI.ConfirmUI);
                await UIManager.Instance.CloseAsync("GMPanelUI");
            }
        }
    }
}
