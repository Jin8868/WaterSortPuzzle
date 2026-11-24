using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.WaterSortPuzzle;
using UnityEngine.UI;

namespace Game.UI
{
    public sealed partial class GamePlayUIView
    {
        private const int HINTSEARCHMILLISECONDS = 3000; // 每次提示求解的实际时间上限。

        private WaterSortSolution m_hintPlan; // 对应当前瓶子顺序的最短路径。
        private int m_hintIndex; // 路径中下一次尚未完成的倒水。
        private int m_hintRemaining; // 本轮剩余提示步数，-1 表示持续引导至通关。
        private int m_hintsUsed; // 本轮已完成的提示步数，重新求解时保留。
        private bool m_hintPending; // 等待瓶子归位后更新提示。
        private PourCommand m_guidedPour; // 按实际显示的指引发起、尚未提交的倒水。
        private CancellationTokenSource m_hintSearch; // 正在后台搜索或等待退出的任务。
        private string m_hintFeedback; // 当前局面的搜索失败说明。
        private Text m_hintLabel; // 现有提示按钮的文字节点。

        private int HintSteps => WaterSortLevelCatalog.Get(m_controller.LevelID).HintSteps;
        private bool CanStartHints =>
            CanPlay && m_controller.LevelID > 0 && !m_controller.IsInfinity
            && m_hintRemaining == 0 && m_hintSearch == null && HintSteps != 0;

        /// <summary>按关卡配置开启本轮倒水提示。</summary>
        public void StartHints()
        {
            if (!CanStartHints)
                return;

            m_hintRemaining = HintSteps;
            m_hintsUsed = 0;
            m_hintPending = true;
            m_hintFeedback = null;
            UpdateHintLabel("提示\n等待归位");
            RefreshLevelInfo();
            RefreshButtons();
        }

        private string GetHintLevelInfo()
        {
            if (m_hintFeedback != null)
                return m_levelLabel + "\n" + m_hintFeedback;
            return m_levelLabel;
        }

        private void UpdateHintLabel(string label)
        {
            if (m_hintLabel == null)
                m_hintLabel = BtnHint.GetComponentInChildren<Text>(true);
            if (m_hintLabel != null)
                m_hintLabel.text = label;
        }

        private void StopHints()
        {
            m_hintRemaining = 0;
            m_hintPending = false;
            m_hintPlan = null;
            m_hintIndex = 0;
            m_hintsUsed = 0;
            m_guidedPour = null;
            m_hintFeedback = null;
            ClearHintMarkers();
            UpdateHintLabel("提示");
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
            if (m_hintRemaining == 0)
            {
                m_hintPlan = null;
                ClearHintFeedback();
                return;
            }

            // 只追踪本次已经显示的指引。并行倒水会使后续路线失效。
            if (!m_hintPending && m_hintPlan != null)
            {
                var step = m_hintPlan.Steps[m_hintIndex];
                var bottles = m_controller.Bottles;
                if (bottles[step.SourceIndex] == command.Source && bottles[step.TargetIndex] == command.Target)
                    m_guidedPour = command;
            }

            if (m_guidedPour != command)
                m_hintPlan = null;

            ClearHintMarkers();
            m_hintPending = true;
            UpdateHintLabel("提示\n等待归位");
        }

        private void HandleHintPourFinished(PourCommand command)
        {
            if (m_guidedPour != command)
                return;

            // 仅已提交的引导步骤占用额度，随后等待瓶子归位。
            m_guidedPour = null;
            if (m_hintRemaining > 0)
                m_hintRemaining--;
            m_hintsUsed++;
            m_hintIndex++;
            if (m_hintPlan != null && m_hintIndex == m_hintPlan.Steps.Count)
                StopHints();
            else if (m_hintRemaining == 0)
            {
                m_hintPending = false;
                UpdateHintLabel("提示");
            }

            RefreshButtons();
        }

        private void HandleHintUndo(PourCommand command)
        {
            if (m_hintRemaining == 0)
            {
                m_hintPlan = null;
                ClearHintFeedback();
                return;
            }

            // 撤销未提交的引导命令时取消追踪，已消耗的额度保持原值。
            if (m_guidedPour == command)
                m_guidedPour = null;

            ClearHintMarkers();
            m_hintPlan = null;
            m_hintPending = true;
            UpdateHintLabel("提示\n等待归位");
        }

        private void ClearHintFeedback()
        {
            if (m_hintFeedback == null)
                return;

            m_hintFeedback = null;
            RefreshLevelInfo();
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

            // 本轮提示总数受当前路线长度和剩余额度共同限制。
            int remainingSteps = m_hintPlan.Steps.Count - m_hintIndex;
            if (m_hintRemaining > 0)
                remainingSteps = Math.Min(remainingSteps, m_hintRemaining);

            var step = m_hintPlan.Steps[m_hintIndex];
            m_cells[bottles[step.SourceIndex]].SetHint(true, isSource: true);
            m_cells[bottles[step.TargetIndex]].SetHint(true);
            UpdateHintLabel($"提示 {m_hintsUsed + 1}/{m_hintsUsed + remainingSteps}\n闪烁 → 常亮");
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

            // 分别记录局面失效和搜索超时，只有当前局面的超时才提示重试。
            using var search = new CancellationTokenSource();
            using var budget = new CancellationTokenSource(HINTSEARCHMILLISECONDS);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(search.Token, budget.Token);
            m_hintSearch = search;
            UpdateHintLabel("提示\n计算中");
            try
            {
                await UniTask.SwitchToThreadPool();
                WaterSortSolution plan = WaterSortSolver.Solve(snapshot, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                await UniTask.SwitchToMainThread();
                if (!search.IsCancellationRequested)
                {
                    if (plan.Steps.Count == 0)
                    {
                        StopHints();
                        if (plan.MinimumSteps < 0)
                            m_hintFeedback = "无解，请撤销或重置";
                    }
                    else
                    {
                        m_hintPlan = plan;
                        m_hintIndex = 0;
                        m_hintPending = true;
                    }
                    RefreshLevelInfo();
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                await UniTask.SwitchToMainThread();
                if (!search.IsCancellationRequested)
                {
                    StopHints();
                    m_hintFeedback = "提示超时，请重试";
                    RefreshLevelInfo();
                }
            }
            catch (Exception)
            {
                await UniTask.SwitchToMainThread();
                if (!search.IsCancellationRequested)
                {
                    StopHints();
                    RefreshLevelInfo();
                }
                throw;
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                m_hintSearch = null;
                if (m_controller != null)
                    RefreshButtons();
            }
        }
    }
}
