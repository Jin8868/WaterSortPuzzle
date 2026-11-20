using UnityEngine;

namespace Game.WaterSortPuzzle
{
    /// <summary>选关与结算共用的本地通关进度。</summary>
    internal static class WaterSortProgress
    {
        private const string PROGRESSKEY = "WaterSortPuzzle.CompletedLevel"; // 原有通关进度的存档键。
        private const string BESTSCOREKEY = "WaterSortPuzzle.BestScore"; // 原有无尽最高分的存档键。

        internal static int CompletedLevel => PlayerPrefs.GetInt(PROGRESSKEY, 0);
        internal static int BestScore => PlayerPrefs.GetInt(BESTSCOREKEY, 0);

        internal static bool IsUnlocked(int levelID)
        {
            return levelID >= 1 && levelID <= WaterSortLevelCatalog.Count
                && WaterSortLevelCatalog.Get(levelID).PrerequisiteID <= CompletedLevel;
        }

        internal static void RecordCompletion(int levelID)
        {
            // 重玩已通关关卡时保留最高进度，无尽只记录分数。
            if (levelID <= CompletedLevel || !IsUnlocked(levelID) || WaterSortLevelCatalog.Get(levelID).IsInfinity)
                return;
            PlayerPrefs.SetInt(PROGRESSKEY, levelID);
            PlayerPrefs.Save();
        }

        internal static void RecordBestScore(int score)
        {
            if (score <= BestScore) return;
            PlayerPrefs.SetInt(BESTSCOREKEY, score);
            PlayerPrefs.Save();
        }
    }
}
