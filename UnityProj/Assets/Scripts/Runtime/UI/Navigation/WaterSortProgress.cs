using UnityEngine;

namespace Game.WaterSortPuzzle
{
    /// <summary>选关与结算共用的本地通关进度。</summary>
    internal static class WaterSortProgress
    {
        private const string PROGRESSKEY = "WaterSortPuzzle.CompletedLevel"; // 原有通关进度的存档键。
        private const string BESTSCOREKEY = "WaterSortPuzzle.BestScore"; // 原有无尽最高分的存档键。
        private const string BESTMOVESKEYPREFIX = "WaterSortPuzzle.BestMoves."; // 普通关最少步数的存档键前缀。
        private const string FASTESTSECONDSKEYPREFIX = "WaterSortPuzzle.FastestSeconds."; // 普通关最快用时的存档键前缀。

        internal static int CompletedLevel => PlayerPrefs.GetInt(PROGRESSKEY, 0);
        internal static int BestScore => PlayerPrefs.GetInt(BESTSCOREKEY, 0);

        internal static bool IsUnlocked(int levelID)
        {
            return levelID >= 1 && levelID <= WaterSortLevelCatalog.Count
                && WaterSortLevelCatalog.Get(levelID).PrerequisiteID <= CompletedLevel;
        }

        internal static bool TryGetLevelRecord(int levelID, out int bestMoveCount, out float fastestSeconds)
        {
            // 旧存档可能已有通关进度，但没有该关的成绩。
            bestMoveCount = PlayerPrefs.GetInt(BESTMOVESKEYPREFIX + levelID, -1);
            fastestSeconds = PlayerPrefs.GetFloat(FASTESTSECONDSKEYPREFIX + levelID, -1f);
            return bestMoveCount >= 0 && fastestSeconds >= 0f;
        }

        internal static void RecordCompletion(int levelID, int moveCount, float elapsedSeconds)
        {
            if (!IsUnlocked(levelID) || WaterSortLevelCatalog.Get(levelID).IsInfinity)
                return;

            // 首次通关推进解锁进度，重玩仍可单独刷新任意一项纪录。
            bool advanced = levelID > CompletedLevel;
            if (advanced)
                PlayerPrefs.SetInt(PROGRESSKEY, levelID);

            string bestMovesKey = BESTMOVESKEYPREFIX + levelID;
            string fastestSecondsKey = FASTESTSECONDSKEYPREFIX + levelID;
            int bestMoveCount = PlayerPrefs.GetInt(bestMovesKey, int.MaxValue);
            float fastestSeconds = PlayerPrefs.GetFloat(fastestSecondsKey, float.PositiveInfinity);
            bool improvedMoves = moveCount < bestMoveCount;
            bool improvedTime = elapsedSeconds < fastestSeconds;
            if (improvedMoves)
                PlayerPrefs.SetInt(bestMovesKey, moveCount);
            if (improvedTime)
                PlayerPrefs.SetFloat(fastestSecondsKey, elapsedSeconds);

            if (advanced || improvedMoves || improvedTime)
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
