using System;
using System.Collections.Generic;

namespace Game.WaterSortPuzzle
{
    public sealed class WaterSortPuzzleController
    {
        private readonly List<BottleData> m_bottles = new List<BottleData>(); // 当前关卡的瓶子。
        private readonly List<PourCommand> m_commands = new List<PourCommand>(); // 按开始顺序保存的撤销栈。
        private readonly HashSet<PourCommand> m_activeCommands = new HashSet<PourCommand>(); // 尚未通知结束的命令。
        private readonly Dictionary<BottleData, int> m_incomingCounts = new Dictionary<BottleData, int>(); // 接水计数。
        private readonly Dictionary<BottleData, int> m_outgoingCounts = new Dictionary<BottleData, int>(); // 倒水计数。
        private readonly InfinityBottleGenerator m_generator = new InfinityBottleGenerator(); // 无尽颜色生成器。
        private readonly List<PourCommand> m_comboAwards = new List<PourCommand>(); // 当前连击按得分顺序记录。
        // 完成动画令牌，用于过滤撤销后的晚完成回调。
        private readonly Dictionary<BottleData, PourCommand> m_completionTokens =
            new Dictionary<BottleData, PourCommand>();
        private BottleData m_selectedBottle; // 当前选中源瓶。
        private int m_comboID; // 区分过期后重新开始的连击代次。
        private double m_elapsedTime; // 当前关卡已经过的实时时间。
        private bool m_resetting; // 阻止重置期间事件重新发起命令。

        public event Action<PourCommand> BeginPour;
        public event Action<PourCommand> DoPour;
        public event Action<PourCommand> FinishPour;
        public event Action<PourCommand> InterruptPour;
        public event Action<PourCommand> SyncData;
        public event Action<BottleData> BottleSelectChange;
        public event Action<BottleData> BottleCompleted;
        public event Action<BottleData, BottleData> BottleReplaced;
        public event Action ScoreChanged;
        public event Action TimeChanged;
        public event Action LevelLoaded;
        public event Action Stopped;
        public event Action Completed;

        public IReadOnlyList<BottleData> Bottles => m_bottles;
        public BottleData SelectedBottle => m_selectedBottle;
        public int LevelID { get; private set; }
        public bool IsInfinity { get; private set; }
        public int Score { get; private set; }
        public int Combo { get; private set; }
        public float RemainingTime { get; private set; }
        public float ComboRemainingTime { get; private set; }
        public float ComboWindow { get; private set; } = WaterSortSettings.COMBOWINDOW;
        public int ReverseRemaining { get; private set; }
        public bool CanReverse => !IsStopped && !m_resetting && ReverseRemaining != 0 && m_commands.Count > 0;
        public bool IsStopped { get; private set; }
        public bool IsCompleted { get; private set; }

        /// <summary>加载配置关卡并使旧命令失效。</summary>
        /// <param name="levelID">一到十四的关卡编号。</param>
        /// <exception cref="ArgumentOutOfRangeException">关卡编号超出范围。</exception>
        public void LoadLevel(int levelID)
        {
            LevelDefinition level = WaterSortLevelCatalog.Get(levelID);
            m_resetting = true;
            Stop();
            m_commands.Clear();
            m_activeCommands.Clear();
            m_incomingCounts.Clear();
            m_outgoingCounts.Clear();
            m_completionTokens.Clear();
            m_comboAwards.Clear();
            m_bottles.Clear();
            m_bottles.AddRange(level.CreateBottles());
            LevelID = levelID;
            IsInfinity = level.IsInfinity;
            if (IsInfinity)
                m_generator.FillInfinityTemplate(m_bottles);
            Score = 0;
            Combo = 0;
            m_comboID++;
            m_elapsedTime = 0d;
            RemainingTime = IsInfinity ? WaterSortSettings.DURATION : 0f;
            ComboRemainingTime = 0f;
            ComboWindow = WaterSortSettings.COMBOWINDOW;
            ReverseRemaining = level.ReverseCount;
            IsStopped = false;
            IsCompleted = false;
            SetSelection(null);
            m_resetting = false;
            LevelLoaded?.Invoke();
            ScoreChanged?.Invoke();
            TimeChanged?.Invoke();
        }

        /// <summary>重新加载当前关卡。</summary>
        public void Reset()
        {
            if (LevelID != 0)
                LoadLevel(LevelID);
        }

        /// <summary>按选瓶规则切换选择或发起倒水。</summary>
        /// <param name="bottle">被点击的瓶子。</param>
        /// <returns>输入被处理时返回真。</returns>
        public bool TrySelect(BottleData bottle)
        {
            if (IsStopped || m_resetting || bottle == null || !m_bottles.Contains(bottle))
                return false;
            if (bottle == m_selectedBottle || bottle.IsFinish())
            {
                SetSelection(null);
                return true;
            }
            if (m_selectedBottle != null && TryPour(m_selectedBottle, bottle))
                return true;
            SetSelection(CanSource(bottle) ? bottle : null);
            return true;
        }

        /// <summary>预占目标容量并广播开始搬移。</summary>
        /// <param name="source">源瓶。</param>
        /// <param name="target">目标瓶。</param>
        /// <returns>命令创建成功时返回真。</returns>
        public bool TryPour(BottleData source, BottleData target)
        {
            if (IsStopped || m_resetting || source == null || target == null || source == target
                || !m_bottles.Contains(source) || !m_bottles.Contains(target) || !CanSource(source)
                || target.IsFull() || HasOutgoing(target)
                || (target.State != EBottleState.Idle && target.State != EBottleState.BeingPour)
                || (target.EffectiveColor != 0 && target.EffectiveColor != source.TopColor))
                return false;

            // 只允许顶端连续可见层参与本次搬移。
            int count = 0;
            int available = BottleData.CAPACITY - target.Colors.Count - target.LockCount;
            for (int index = source.Colors.Count - 1; index >= source.InvisibleCount && count < available; index--)
            {
                if (source.Colors[index] != source.TopColor)
                    break;
                count++;
            }
            if (count == 0)
                return false;
            PourCommand command = new PourCommand(this, source, target, count);
            m_commands.Add(command);
            m_activeCommands.Add(command);
            IncrementCount(m_incomingCounts, target);
            IncrementCount(m_outgoingCounts, source);
            SetSelection(null);
            BeginPour?.Invoke(command);
            return true;
        }

        /// <summary>由源瓶移动到位后请求目标接水。</summary>
        /// <param name="command">本次倒水命令。</param>
        public void NotifyMovingFinished(PourCommand command)
        {
            if (CanAdvance(command) && command.State == EPourCommandState.Started)
                DoPour?.Invoke(command);
        }

        /// <summary>由源瓶提交搬水后广播倒水结束。</summary>
        /// <param name="command">已经提交的命令。</param>
        public void NotifyPourFinished(PourCommand command)
        {
            if (!CanAdvance(command) || command.State != EPourCommandState.Finished)
                return;
            RemoveActive(command);
            if (command.Target.IsFinish() && !HasIncoming(command.Target))
                m_completionTokens[command.Target] = command;
            FinishPour?.Invoke(command);
            if (m_completionTokens.TryGetValue(command.Target, out PourCommand token) && token == command)
                BottleCompleted?.Invoke(command.Target);
            CheckCompleted();
        }

        /// <summary>使用动画进入时的命令令牌提交完成，拒绝撤销后的过期回调。</summary>
        /// <param name="bottle">完成动画对应的瓶子。</param>
        /// <param name="command">进入完成动画时的命令。</param>
        public void NotifyBottleComplete(BottleData bottle, PourCommand command)
        {
            if (IsStopped || bottle == null || !m_bottles.Contains(bottle) || !bottle.IsFinish()
                || HasIncoming(bottle) || command == null
                || !m_completionTokens.TryGetValue(bottle, out PourCommand token) || token != command)
                return;
            // 动画由最后结束的命令触发，奖励归属最后开始的命令，确保按开始顺序撤销时一并扣回。
            PourCommand owner = FindCompletionOwner(bottle);
            if (owner == null || owner.CompletionAwarded)
                return;

            // 奖励绑定命令，撤销及过期回调都可按所有权验证。
            owner.CompletionAwarded = true;
            m_completionTokens.Remove(bottle);
            if (Combo == 0)
                m_comboID++;
            owner.ComboID = m_comboID;
            Combo++;
            ComboWindow = WaterSortSettings.GetComboWindow(Combo);
            ComboRemainingTime = ComboWindow;
            owner.ComboWindow = ComboWindow;
            owner.ComboDeadline = m_elapsedTime + ComboWindow;
            m_comboAwards.Add(owner);
            owner.AwardScore = WaterSortSettings.COMPLETIONSCORE * Combo;
            Score += owner.AwardScore;
            if (IsInfinity)
            {
                owner.AwardTime = WaterSortSettings.GetCompletionTime(Combo);
                RemainingTime += owner.AwardTime;
                BottleData replacement = m_generator.CreateReplacement(m_bottles, Score, m_activeCommands);
                replacement.LocalPos = bottle.LocalPos;
                replacement.LocalScale = bottle.LocalScale;
                owner.Replacement = replacement;
                m_bottles[m_bottles.IndexOf(bottle)] = replacement;
                BottleReplaced?.Invoke(bottle, replacement);
            }
            ScoreChanged?.Invoke();
            TimeChanged?.Invoke();
            CheckCompleted();
        }

        /// <summary>撤销最后一条命令及其完成奖励。</summary>
        /// <returns>成功撤销时返回真。</returns>
        public bool Reverse()
        {
            if (!CanReverse)
                return false;
            PourCommand command = m_commands[m_commands.Count - 1];
            m_commands.RemoveAt(m_commands.Count - 1);
            // 回滚前从活动索引移除，历史命令只用于撤销和少量完成归属查询。
            RemoveActive(command);
            m_completionTokens.Remove(command.Target);
            SetSelection(null);

            // 先回滚水层，再将状态已经恢复的旧瓶交给视图。
            command.Reverse();
            if (command.Replacement != null)
            {
                int replacementIndex = m_bottles.IndexOf(command.Replacement);
                if (replacementIndex >= 0)
                {
                    command.Target.State = EBottleState.Idle;
                    m_bottles[replacementIndex] = command.Target;
                    BottleReplaced?.Invoke(command.Replacement, command.Target);
                }
            }
            if (command.CompletionAwarded)
            {
                Score -= command.AwardScore;
                // 按得分顺序恢复最近一次有效奖励的原到期时间，保留并行命令的奖励。
                if (command.ComboID == m_comboID && m_comboAwards.Remove(command))
                    UpdateComboWindow();
                RemainingTime = Math.Max(0f, RemainingTime - command.AwardTime);
            }
            if (ReverseRemaining > 0)
                ReverseRemaining--;
            InterruptPour?.Invoke(command);
            SyncData?.Invoke(command);
            ScoreChanged?.Invoke();
            TimeChanged?.Invoke();
            return true;
        }

        /// <summary>更新限时和连击窗口。</summary>
        /// <param name="deltaTime">本帧经过的秒数。</param>
        public void Tick(float deltaTime)
        {
            if (IsStopped || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                return;
            m_elapsedTime += deltaTime;
            int previousCombo = Combo;
            UpdateComboWindow();
            if (previousCombo != Combo)
                ScoreChanged?.Invoke();
            if (IsInfinity)
            {
                RemainingTime = Math.Max(0f, RemainingTime - deltaTime);
                if (RemainingTime == 0f)
                    Complete();
            }
            TimeChanged?.Invoke();
        }

        /// <summary>冻结当前所有瓶子和后续动画数据提交。</summary>
        public void Stop()
        {
            IsStopped = true;
            SetSelection(null);
            Stopped?.Invoke();
        }

        internal bool CanAdvance(PourCommand command)
        {
            return !IsStopped && !m_resetting && command != null && m_activeCommands.Contains(command)
                && command.State != EPourCommandState.Reversed;
        }

        private bool CanSource(BottleData bottle)
        {
            return !bottle.IsEmpty() && !bottle.IsFinish() && !HasIncoming(bottle) && !HasOutgoing(bottle)
                && bottle.State == EBottleState.Idle;
        }

        private bool HasOutgoing(BottleData bottle)
        {
            return m_outgoingCounts.ContainsKey(bottle);
        }

        private bool HasIncoming(BottleData bottle)
        {
            return m_incomingCounts.ContainsKey(bottle);
        }

        private static void IncrementCount(Dictionary<BottleData, int> counts, BottleData bottle)
        {
            counts.TryGetValue(bottle, out int count);
            counts[bottle] = count + 1;
        }

        private static void DecrementCount(Dictionary<BottleData, int> counts, BottleData bottle)
        {
            if (counts[bottle] == 1)
                counts.Remove(bottle);
            else
                counts[bottle]--;
        }

        private void RemoveActive(PourCommand command)
        {
            if (!m_activeCommands.Remove(command))
                return;
            DecrementCount(m_incomingCounts, command.Target);
            DecrementCount(m_outgoingCounts, command.Source);
        }

        private void UpdateComboWindow()
        {
            if (m_comboAwards.Count > 0)
            {
                PourCommand lastAward = m_comboAwards[m_comboAwards.Count - 1];
                double remainingTime = lastAward.ComboDeadline - m_elapsedTime;
                if (remainingTime > 0d)
                {
                    Combo = m_comboAwards.Count;
                    ComboWindow = lastAward.ComboWindow;
                    ComboRemainingTime = (float)remainingTime;
                    return;
                }
            }
            m_comboAwards.Clear();
            Combo = 0;
            ComboWindow = WaterSortSettings.COMBOWINDOW;
            ComboRemainingTime = 0f;
        }

        private PourCommand FindCompletionOwner(BottleData bottle)
        {
            for (int index = m_commands.Count - 1; index >= 0; index--)
            {
                PourCommand command = m_commands[index];
                if (command.Target == bottle && command.State == EPourCommandState.Finished)
                    return command;
            }
            return null;
        }

        private void SetSelection(BottleData bottle)
        {
            m_selectedBottle = bottle;
            BottleSelectChange?.Invoke(bottle);
        }

        private void CheckCompleted()
        {
            if (IsInfinity || IsStopped || m_activeCommands.Count != 0 || m_completionTokens.Count != 0)
                return;
            for (int index = 0; index < m_bottles.Count; index++)
            {
                BottleData bottle = m_bottles[index];
                if (!bottle.IsEmpty() && !bottle.IsFinish())
                    return;
            }
            Complete();
        }

        private void Complete()
        {
            if (IsCompleted)
                return;
            IsCompleted = true;
            Stop();
            Completed?.Invoke();
        }
    }
}
