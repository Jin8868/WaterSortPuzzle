using AlloyFramework.UI;
using Game.WaterSortPuzzle;

namespace Game.UI
{
    public sealed class LevelSelectUIController : UIController<LevelSelectUIView>
    {
        private LevelItemData[] m_levelItems; // 列表持有的关卡显示数据。

        protected override void OnInitData(UIEmptyData data)
        {
            if (m_levelItems != null)
            {
                RefreshLevels();
                return;
            }

            // 按目录顺序绑定正式关卡，目录末尾同时提供无尽挑战入口。
            LevelItemData[] levels = new LevelItemData[WaterSortLevelCatalog.Count];
            FillLevelItems(levels);
            View.ListLevel.BindData(levels);
            m_levelItems = levels;
        }

        protected override void OnRefresh(UIEmptyData data)
        {
            RefreshLevels();
        }

        protected override void OnResume()
        {
            RefreshLevels();
        }

        private void RefreshLevels()
        {
            // 列表保留集合引用，更新数据后重新绑定可见项并保留滚动位置。
            FillLevelItems(m_levelItems);
            View.ListLevel.Refresh();
        }

        private static void FillLevelItems(LevelItemData[] levels)
        {
            int completedLevel = WaterSortProgress.CompletedLevel;
            int bestScore = WaterSortProgress.BestScore;
            for (int index = 0; index < levels.Length; index++)
            {
                LevelDefinition level = WaterSortLevelCatalog.Get(index + 1);
                bool unlocked = WaterSortProgress.IsUnlocked(level.ID);
                levels[index] = new LevelItemData(level, unlocked, completedLevel, bestScore);
            }
        }
    }
}
