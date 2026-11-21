namespace Game.WaterSortPuzzle
{
    public sealed class PourCommand
    {
        private readonly WaterSortPuzzleController m_controller; // 命令所属控制器。
        private readonly int m_originalInvisibleCount; // 撤销时恢复的隐藏层数。
        internal BottleData Replacement; // 本次完成后替换的新瓶。
        internal int AwardScore; // 本命令实际获得的分数。
        internal float AwardTime; // 本命令增加的剩余时间。
        internal bool CompletionAwarded; // 防止重复完成回调。

        public BottleData Source { get; }
        public BottleData Target { get; }
        public int Color { get; }
        public int ColorCount { get; }
        public EPourCommandState State { get; private set; }

        internal PourCommand(WaterSortPuzzleController controller, BottleData source, BottleData target, int count)
        {
            m_controller = controller;
            m_originalInvisibleCount = source.InvisibleCount;
            Source = source;
            Target = target;
            Color = source.TopColor;
            ColorCount = count;
            State = EPourCommandState.Started;
            target.LockCount += count;
            target.LockColor = Color;
        }

        /// <summary>由目标瓶在开始接水时插入水层并消耗预占。</summary>
        /// <returns>首次成功接水时返回真。</returns>
        public bool Receive()
        {
            if (State != EPourCommandState.Started || !m_controller.CanAdvance(this))
                return false;

            // 预占与实际层互换，容量始终只计算一次。
            Target.LockCount -= ColorCount;
            for (int index = 0; index < ColorCount; index++)
                Target.Colors.Add(Color);
            if (Target.LockCount == 0)
                Target.LockColor = 0;
            State = EPourCommandState.Received;
            return true;
        }

        /// <summary>由源瓶在液面动画结束时移除源层并揭晓下一层。</summary>
        /// <returns>首次成功提交时返回真。</returns>
        public bool Commit()
        {
            if (State != EPourCommandState.Received || !m_controller.CanAdvance(this))
                return false;

            // 仅在视图确认倒完后改变源瓶，隐藏区顶端揭晓一层。
            Source.Colors.RemoveRange(Source.Colors.Count - ColorCount, ColorCount);
            Source.InvisibleCount = System.Math.Min(Source.InvisibleCount,
                System.Math.Max(0, Source.Colors.Count - 1));
            State = EPourCommandState.Finished;
            return true;
        }

        internal void Reverse()
        {
            // 同色并行目标只回退本命令的层数，其他命令的预占保持有效。
            if (State == EPourCommandState.Started)
            {
                Target.LockCount -= ColorCount;
            }
            else
            {
                Target.Colors.RemoveRange(Target.Colors.Count - ColorCount, ColorCount);
                if (State == EPourCommandState.Finished)
                {
                    for (int index = 0; index < ColorCount; index++)
                        Source.Colors.Add(Color);
                    Source.InvisibleCount = m_originalInvisibleCount;
                }
            }
            if (Target.LockCount == 0)
                Target.LockColor = 0;
            State = EPourCommandState.Reversed;
        }
    }
}
