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

        private const int LASTLEGACYFINITEID = 13; // 旧版最高通关编号只覆盖原有的有限关卡。
        private const int LEGACYENDLESSID = 14; // 旧版无尽最高分归属现在的无尽第一关。

        internal static int GetBestScore(int levelID)
        {
            string key = BESTSCOREKEY + "." + levelID;
            int legacyScore = levelID == LEGACYENDLESSID ? PlayerPrefs.GetInt(BESTSCOREKEY, 0) : 0;
            return PlayerPrefs.GetInt(key, legacyScore);
        }

        internal static bool IsCompleted(int levelID)
        {
            if (WaterSortLevelCatalog.Get(levelID).IsInfinity)
                return GetBestScore(levelID) > 0;

            // 旧存档只能还原原目录中的通关状态，新关卡使用各自的通关成绩。
            return PlayerPrefs.HasKey(BESTMOVESKEYPREFIX + levelID)
                || (levelID <= LASTLEGACYFINITEID && levelID <= PlayerPrefs.GetInt(PROGRESSKEY, 0));
        }

        internal static bool IsUnlocked(int levelID)
        {
            if (levelID < 1 || levelID > WaterSortLevelCatalog.Count)
                return false;

            int prerequisiteID = WaterSortLevelCatalog.Get(levelID).PrerequisiteID;
            return prerequisiteID == 0 || IsCompleted(prerequisiteID);
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

            // 每个关卡独立记录，挑战成绩不会提前解锁普通关。
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

            if (improvedMoves || improvedTime)
                PlayerPrefs.Save();
        }

        internal static void RecordBestScore(int levelID, int score)
        {
            if (!IsUnlocked(levelID) || score <= GetBestScore(levelID)) return;
            PlayerPrefs.SetInt(BESTSCOREKEY + "." + levelID, score);
            PlayerPrefs.Save();
        }
    }
}
