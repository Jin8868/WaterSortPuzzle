using System.Collections.Generic;
using AlloyFramework.UI;
using Game.WaterSortPuzzle;

namespace Game.UI
{
    public sealed class LevelSelectUIController : UIController<LevelSelectUIView>
    {
        private readonly List<LevelItemData> m_levelItems = new List<LevelItemData>(); // 当前模式的列表数据。
        private bool m_bound; // 首次绑定后复用列表和可见项。
        private bool m_challenge; // 当前显示的关卡模式。

        protected override void OnCreate()
        {
            View.NormalModeButton.onClick.AddListener(ShowNormalMode);
            View.ChallengeModeButton.onClick.AddListener(ShowChallengeMode);
        }

        protected override void OnInitData(UIEmptyData data)
        {
            View.ArrangeGrid();
            Display(WaterSortNavigation.ChallengeSelected);
        }

        protected override void OnRefresh(UIEmptyData data)
        {
            Display(WaterSortNavigation.ChallengeSelected);
        }

        protected override void OnResume()
        {
            Display(WaterSortNavigation.ChallengeSelected);
        }

        protected override void OnDispose()
        {
            View.NormalModeButton.onClick.RemoveListener(ShowNormalMode);
            View.ChallengeModeButton.onClick.RemoveListener(ShowChallengeMode);
        }

        private void ShowNormalMode()
        {
            Display(false);
        }

        private void ShowChallengeMode()
        {
            Display(true);
        }

        private void Display(bool challenge)
        {
            bool rebind = !m_bound || m_challenge != challenge;
            m_challenge = challenge;
            WaterSortNavigation.ChallengeSelected = challenge;
            View.DisplayMode(challenge);

            // 保留目录编号和解锁条件，不以筛选后的位置作为关卡编号。
            m_levelItems.Clear();
            int completedLevel = WaterSortProgress.CompletedLevel;
            int bestScore = WaterSortProgress.BestScore;
            for (int levelID = 1; levelID <= WaterSortLevelCatalog.Count; levelID++)
            {
                LevelDefinition level = WaterSortLevelCatalog.Get(levelID);
                if (level.IsChallenge != challenge)
                    continue;

                bool unlocked = WaterSortProgress.IsUnlocked(level.ID);
                WaterSortProgress.TryGetLevelRecord(level.ID, out int bestMoveCount, out float fastestSeconds);
                m_levelItems.Add(new LevelItemData(level, unlocked, completedLevel, bestScore,
                    bestMoveCount, fastestSeconds));
            }

            // 切换模式从第一张卡开始，返回同一模式时保留滚动位置并刷新纪录。
            if (rebind)
            {
                View.ListLevel.BindData(m_levelItems);
                m_bound = true;
            }
            else
            {
                View.ListLevel.Refresh();
            }
        }
    }
}
