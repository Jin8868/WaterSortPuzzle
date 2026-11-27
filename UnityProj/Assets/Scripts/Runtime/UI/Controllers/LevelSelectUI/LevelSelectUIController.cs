using System.Collections.Generic;
using AlloyFramework;
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
            WaterSortProgressModel progress = ModelManager.Instance.Get<WaterSortProgressModel>();
            m_levelItems.Clear();
            for (int levelID = 1; levelID <= WaterSortLevelCatalog.Count; levelID++)
            {
                LevelDefinition level = WaterSortLevelCatalog.Get(levelID);
                if (level.IsChallenge != challenge)
                    continue;

                bool unlocked = progress.IsUnlocked(level.ID);
                progress.TryGetLevelRecord(level.ID, out int bestMoveCount, out float fastestSeconds);
                bool completed = progress.IsCompleted(levelID);
                int bestScore = level.IsInfinity ? progress.GetBestScore(levelID) : 0;
                m_levelItems.Add(new LevelItemData(level, unlocked, completed, bestScore,
                    bestMoveCount, fastestSeconds));
            }

            // 挑战页从无尽第一关开始，再列出保留的独立限时挑战。
            if (challenge)
                m_levelItems.Sort(CompareChallenges);

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

        private static int CompareChallenges(LevelItemData left, LevelItemData right)
        {
            int group = right.IsInfinity.CompareTo(left.IsInfinity);
            return group != 0 ? group : left.LevelID.CompareTo(right.LevelID);
        }
    }
}
