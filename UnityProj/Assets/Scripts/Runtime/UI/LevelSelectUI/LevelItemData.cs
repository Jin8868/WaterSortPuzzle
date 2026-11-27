using System;
using Game.WaterSortPuzzle;

namespace Game
{
    internal sealed class LevelItemData
    {
        internal LevelItemData(
            LevelDefinition level,
            bool unlocked,
            bool completed,
            int bestScore,
            int bestMoveCount,
            float fastestSeconds)
        {
            LevelID = level.ID;
            IsInfinity = level.IsInfinity;
            IsChallenge = level.IsChallenge;
            IsUnlocked = unlocked;
            DisplayName = level.DisplayName;
            StatusText = unlocked ? GetStatusText(level, completed) : string.Empty;
            PrimaryValue = IsInfinity ? bestScore.ToString() : FormatMoves(bestMoveCount, level.OptimalSteps);
            FastestTime = IsInfinity ? string.Empty : FormatTime(fastestSeconds);
        }

        internal int LevelID { get; }
        internal string DisplayName { get; }
        internal string StatusText { get; }
        internal string PrimaryValue { get; }
        internal string FastestTime { get; }
        internal bool IsUnlocked { get; }
        internal bool IsInfinity { get; }
        internal bool IsChallenge { get; }

        private static string GetStatusText(LevelDefinition level, bool completed)
        {
            if (level.IsInfinity)
            {
                return "无尽冲分";
            }

            if (completed)
            {
                return level.ReverseColor ? "反色 · 已通关" : "已通关";
            }

            if (level.IsChallenge)
            {
                return $"限时 {level.MaxDurationSeconds / 60}:{level.MaxDurationSeconds % 60:00}";
            }

            return "待通关";
        }

        private static string FormatMoves(int bestMoveCount, int optimalSteps)
        {
            string best = bestMoveCount < 0 ? "—" : bestMoveCount.ToString();
            string optimal = optimalSteps < 0 ? "—" : optimalSteps.ToString();
            return $"{best} / {optimal}";
        }

        private static string FormatTime(float fastestSeconds)
        {
            if (fastestSeconds < 0)
            {
                return "—";
            }

            // 保留百分秒，并按总分钟显示时长，避免较长记录跨小时回卷。
            TimeSpan duration = TimeSpan.FromSeconds(fastestSeconds);
            return $"{(int)duration.TotalMinutes}:{duration.Seconds:00}.{duration.Milliseconds / 10:00}";
        }
    }
}
