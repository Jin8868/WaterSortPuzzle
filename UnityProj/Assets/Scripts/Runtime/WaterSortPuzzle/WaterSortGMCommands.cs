using System;
using System.Globalization;
using AlloyFramework;
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

        [Preserve]
        [GMCommand("普通模式：一键通关到第 N 关")]
        private void CompleteNormalLevels(string input)
        {
            if (!int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
            {
                Debug.LogWarning("请在 GM 输入框中填写普通关卡序号，例如 13。");
                return;
            }

            CompleteNormalLevelsAsync(number).Forget(Debug.LogException);
        }

        private async UniTask CompleteNormalLevelsAsync(int number)
        {
            // 以普通模式的显示顺序批量修改存档，保留已有纪录和挑战进度。
            try
            {
                ModelManager.Instance.Get<WaterSortProgressModel>().CompleteNormalLevelsThrough(number);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                Debug.LogWarning(exception.Message);
                return;
            }

            await RefreshAfterGrantAsync();
            Debug.Log($"已标记普通第 1 至 {number} 关通关。");
        }

        [Preserve]
        [GMCommand("无尽模式：解锁到第 N 关")]
        private void UnlockEndlessLevels(string input)
        {
            if (!int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
            {
                Debug.LogWarning("请在 GM 输入框中填写无尽关卡序号。");
                return;
            }

            UnlockLevelsAsync(number).Forget(Debug.LogException);
        }

        [Preserve]
        [GMCommand("一键解锁所有关卡")]
        private void UnlockAllLevels()
        {
            UnlockLevelsAsync(null).Forget(Debug.LogException);
        }

        private async UniTask UnlockLevelsAsync(int? endlessNumber)
        {
            try
            {
                ModelManager.Instance.Get<WaterSortProgressModel>().UnlockLevelsForGM(endlessNumber);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                Debug.LogWarning(exception.Message);
                return;
            }

            await RefreshAfterGrantAsync();
            Debug.Log(endlessNumber.HasValue ? $"已解锁无尽第 1 至 {endlessNumber} 关。" : "已解锁所有关卡。");
        }

        private static async UniTask RefreshAfterGrantAsync()
        {
            UIManager manager = UIManager.Instance;
            if (manager.IsOpen(GameUI.LevelSelectUI))
                await manager.OpenAsync(GameUI.LevelSelectUI);
            await manager.CloseAsync("GMPanelUI");
        }

        [Preserve]
        [GMCommand("清空本地存档")]
        private void ConfirmDeleteSave()
        {
            ConfirmDeleteSaveAsync().Forget(Debug.LogException);
        }

        private async UniTask ConfirmDeleteSaveAsync()
        {
            UIManager manager = UIManager.Instance;
            await manager.CloseAsync("GMPanelUI");
            if (manager.IsOpen(GameUI.ConfirmUI))
            {
                Debug.LogWarning("请先处理当前的确认弹窗，再清空存档。");
                return;
            }

            var data = new ConfirmUIData("清空本地存档",
                "将结束当前对局，删除全部通关进度、个人纪录和无尽最高分，返回选关。确定清空吗？",
                null, DeleteSave);
            await manager.OpenAsync(GameUI.ConfirmUI, data);
        }

        private void DeleteSave()
        {
            DeleteSaveAsync().Forget(Debug.LogException);
        }

        private async UniTask DeleteSaveAsync()
        {
            // 等游戏页退出，防止在途结算把刚刚清空的成绩重新写回。
            await WaterSortNavigation.OpenSelectionAsync();
            UIManager manager = UIManager.Instance;
            if (manager.IsOpen(GameUI.GamePlayUI) || !manager.IsOpen(GameUI.LevelSelectUI))
            {
                Debug.LogWarning("界面正在切换，请在选关界面重新执行清空存档。");
                return;
            }

            ModelManager.Instance.Get<WaterSortProgressModel>().DeleteSave();
            await manager.OpenAsync(GameUI.LevelSelectUI);
            Debug.Log("本地存档已清空。");
        }
    }
}
