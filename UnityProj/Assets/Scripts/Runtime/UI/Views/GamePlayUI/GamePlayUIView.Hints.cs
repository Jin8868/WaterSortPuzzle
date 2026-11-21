using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.WaterSortPuzzle;

namespace Game.UI
{
    public sealed partial class GamePlayUIView
    {
        private WaterSortSolution m_hintPlan; // 对应当前瓶子顺序的最短路径。
        private int m_hintIndex; // 路径中下一次尚未完成的倒水。
        private bool m_hintPending; // 等待瓶子归位后更新提示。
        private CancellationTokenSource m_hintSearch; // 正在后台搜索或等待退出的任务。
        private bool m_hintsEnabled; // 玩家是否为本次挑战开启连续提示。

        /// <summary>从当前关卡的稳定局面开始连续显示最短路径提示。</summary>
        public void StartHints()
        {
            if (!CanPlay || m_controller.IsInfinity || m_hintsEnabled)
                return;

            m_hintsEnabled = true;
            m_hintPending = true;
            RefreshLevelInfo();
            RefreshButtons();
        }

        private string GetHintLevelInfo()
        {
            if (!m_hintsEnabled)
                return m_levelLabel;
            if (m_hintPlan == null)
                return m_levelLabel + "\n正在计算最短路线";
            if (m_hintPlan.MinimumSteps < 0)
                return m_levelLabel + " · 无解\n可撤销或重置";
            if (m_hintIndex >= m_hintPlan.Steps.Count)
                return m_levelLabel;

            // 描边标记两瓶，文字说明倒水方向。玩家可见编号从 1 开始。
            var step = m_hintPlan.Steps[m_hintIndex];
            return $"{m_levelLabel} · 剩 {m_hintPlan.MinimumSteps - m_hintIndex} 步\n" +
                $"{step.SourceIndex + 1} 号瓶 → {step.TargetIndex + 1} 号瓶";
        }

        private void StopHints()
        {
            m_hintsEnabled = false;
            m_hintPending = false;
            m_hintPlan = null;
            m_hintIndex = 0;
            ClearHintMarkers();
        }

        private void ClearHintMarkers()
        {
            // 旧搜索退出前保留令牌，连续点击时最多运行一个搜索。
            m_hintSearch?.Cancel();
            foreach (BottleCell cell in m_liveCells)
                cell.SetHint(false);
        }

        private void HandleHintPourStarted(PourCommand command)
        {
            if (!m_hintsEnabled)
                return;

            ClearHintMarkers();
            m_hintPending = true;
        }

        private void HandleHintPourFinished(PourCommand command)
        {
            if (m_hintPlan == null || m_hintIndex >= m_hintPlan.Steps.Count)
                return;

            // 确认实际提交的源瓶和目标瓶后才消费一步，偏离路线时重新求解。
            var step = m_hintPlan.Steps[m_hintIndex];
            var bottles = m_controller.Bottles;
            if (bottles[step.SourceIndex] == command.Source && bottles[step.TargetIndex] == command.Target)
                m_hintIndex++;
            else
                m_hintPlan = null;
            RefreshLevelInfo();
        }

        private void HandleHintUndo(PourCommand command)
        {
            if (!m_hintsEnabled)
                return;

            ClearHintMarkers();
            m_hintPlan = null;
            m_hintIndex = 0;
            m_hintPending = true;
        }

        private void RefreshPendingHint()
        {
            if (!m_hintPending || m_hintSearch != null)
                return;

            // 等待所有并行倒水提交且瓶子恢复可选，避免读取接水与源瓶扣层之间的瞬态。
            var bottles = m_controller.Bottles;
            for (int index = 0; index < bottles.Count; index++)
            {
                if (bottles[index].State != EBottleState.Idle)
                    return;
            }

            m_hintPending = false;
            if (m_hintPlan == null)
            {
                SearchHintsAsync(bottles).Forget();
                return;
            }

            if (m_hintIndex < m_hintPlan.Steps.Count)
            {
                var step = m_hintPlan.Steps[m_hintIndex];
                m_cells[bottles[step.SourceIndex]].SetHint(true);
                m_cells[bottles[step.TargetIndex]].SetHint(true);
            }
        }

        private async UniTask SearchHintsAsync(IReadOnlyList<BottleData> bottles)
        {
            // 只在主线程读取活动瓶子，后台只访问本次搜索的副本。
            var snapshot = new List<BottleData>(bottles.Count);
            foreach (BottleData bottle in bottles)
            {
                var copy = new BottleData { InvisibleCount = bottle.InvisibleCount };
                copy.Colors.AddRange(bottle.Colors);
                snapshot.Add(copy);
            }

            using var search = new CancellationTokenSource();
            m_hintSearch = search;
            try
            {
                await UniTask.SwitchToThreadPool();
                WaterSortSolution plan = WaterSortSolver.Solve(snapshot, search.Token);
                await UniTask.SwitchToMainThread();
                if (!search.IsCancellationRequested)
                {
                    m_hintPlan = plan;
                    m_hintIndex = 0;
                    m_hintPending = true;
                    RefreshLevelInfo();
                }
            }
            catch (OperationCanceledException) when (search.IsCancellationRequested)
            {
                // 倒水、撤销或离开关卡时，旧结果由新局面取代。
            }
            catch (Exception)
            {
                await UniTask.SwitchToMainThread();
                if (!search.IsCancellationRequested)
                {
                    m_hintsEnabled = false;
                    RefreshLevelInfo();
                    RefreshButtons();
                }
                throw;
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                m_hintSearch = null;
            }
        }
    }
}
