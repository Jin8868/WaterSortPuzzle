using Game.WaterSortPuzzle;

namespace Game
{
    internal sealed class LevelItemData
    {
        internal LevelItemData(LevelDefinition level, bool unlocked, int completedLevel, int bestScore)
        {
            LevelID = level.ID;
            IsInfinity = level.IsInfinity;
            IsReverseColor = level.ReverseColor;
            IsUnlocked = unlocked;
            IsCompleted = !IsInfinity && LevelID <= completedLevel;
            DisplayName = IsInfinity ? "Endless" : level.Name;
            StatusText = GetStatusText(bestScore);
        }

        internal int LevelID { get; }
        internal string DisplayName { get; }
        internal string StatusText { get; }
        internal bool IsUnlocked { get; }
        internal bool IsCompleted { get; }
        internal bool IsInfinity { get; }
        internal bool IsReverseColor { get; }

        private string GetStatusText(int bestScore)
        {
            if (!IsUnlocked)
            {
                return "Locked";
            }

            if (IsInfinity)
            {
                return $"Best: {bestScore}";
            }

            if (IsCompleted)
            {
                return "Cleared";
            }

            return IsReverseColor ? "Reverse" : "Level";
        }
    }
}
