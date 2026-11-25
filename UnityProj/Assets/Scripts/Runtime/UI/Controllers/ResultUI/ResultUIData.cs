using System;
using Cysharp.Threading.Tasks;

namespace Game.UI
{
    /// <summary>保存一局结束时的结算数值与操作。</summary>
    public sealed class ResultUIData
    {
        public string Title { get; }
        public int ElapsedSeconds { get; }
        public int SortedBottleCount { get; }
        public int PourStepCount { get; }
        public bool HasNextLevel { get; }
        internal Func<UniTask> ReturnToSelection { get; }
        internal Func<UniTask> EnterNextLevel { get; }

        internal ResultUIData(
            string title,
            int elapsedSeconds,
            int sortedBottleCount,
            int pourStepCount,
            bool hasNextLevel,
            Func<UniTask> returnToSelection,
            Func<UniTask> enterNextLevel)
        {
            Title = title;
            ElapsedSeconds = elapsedSeconds;
            SortedBottleCount = sortedBottleCount;
            PourStepCount = pourStepCount;
            HasNextLevel = hasNextLevel;
            ReturnToSelection = returnToSelection;
            EnterNextLevel = enterNextLevel;
        }
    }
}
