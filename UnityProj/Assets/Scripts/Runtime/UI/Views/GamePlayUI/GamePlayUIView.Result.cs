using System;
using System.Threading;
using AlloyFramework.UI;
using Cysharp.Threading.Tasks;
using Game.WaterSortPuzzle;

namespace Game.UI
{
    public sealed partial class GamePlayUIView
    {
        private ResultRequest m_resultRequest; // 当前关卡拥有的结算请求。
        private UniTaskCompletionSource m_resultClosing; // 旧结算退出时通知等待中的打开或关页流程。

        private bool CanLeaveWithoutResult => m_playing && !m_navigating && m_controller.IsStopped
            && (m_resultRequest == null || m_resultRequest.Handle?.IsValid == false);

        internal UniTask WaitForResultCloseAsync()
        {
            return m_resultClosing?.Task ?? UniTask.CompletedTask;
        }

        private async UniTask ShowResultAsync()
        {
            StopGameplayAudio();

            // 先冻结结果与归属，再等确认弹窗和上一份结算退出。
            using var loading = new CancellationTokenSource();
            var request = new ResultRequest(this, GetNextLevelID(), loading);
            m_resultRequest = request;
            UniTask previousClose = WaitForResultCloseAsync();
            RefreshLevelInfo();
            RefreshButtons();
            try
            {
                await DismissConfirmationAsync();
                await previousClose.AttachExternalCancellation(loading.Token);
                loading.Token.ThrowIfCancellationRequested();
                request.Handle = await UIManager.Instance.OpenAsync(GameUI.ResultUI, request.Data, loading.Token);
            }
            catch (OperationCanceledException) when (loading.IsCancellationRequested)
            {
                // 切换关卡或关页后，当前结算失效。
            }
            finally
            {
                request.Loading = null;
                if (request.Handle == null && ReferenceEquals(m_resultRequest, request))
                {
                    m_resultRequest = null;
                    RefreshButtons();
                }

                request.Opened.TrySetResult();
            }
        }

        private int GetNextLevelID()
        {
            if (!m_controller.IsCompleted || m_controller.IsInfinity)
                return 0;

            // 下一关留在当前模式，无尽挑战由选关入口单独进入。
            bool challenge = WaterSortLevelCatalog.Get(m_controller.LevelID).IsChallenge;
            for (int levelID = m_controller.LevelID + 1; levelID <= WaterSortLevelCatalog.Count; levelID++)
            {
                LevelDefinition next = WaterSortLevelCatalog.Get(levelID);
                if (!next.IsInfinity && next.IsChallenge == challenge && WaterSortProgress.IsUnlocked(levelID))
                    return levelID;
            }
            return 0;
        }

        private void InvalidateResult()
        {
            ResultRequest request = m_resultRequest;
            if (request == null)
                return;

            // 立即撤销按钮操作，等在途打开退出后只关闭属于这次请求的界面。
            m_resultRequest = null;
            UniTask previousClose = WaitForResultCloseAsync();
            var closing = new UniTaskCompletionSource();
            m_resultClosing = closing;
            request.Loading?.Cancel();
            CloseResultAsync(request, previousClose, closing).Forget();
        }

        private async UniTask CloseResultAsync(
            ResultRequest request,
            UniTask previousClose,
            UniTaskCompletionSource closing)
        {
            try
            {
                await previousClose;
                await request.Opened.Task;
                UIHandle<ResultUIController> handle = request.Handle;
                if (UIManager.Instance != null && handle != null && handle.IsValid
                    && handle.Controller.Owns(request.Data))
                    await handle.CloseAsync();
            }
            finally
            {
                if (ReferenceEquals(m_resultClosing, closing))
                    m_resultClosing = null;

                closing.TrySetResult();
            }
        }

        private sealed class ResultRequest
        {
            private readonly GamePlayUIView m_owner; // 发起结算的玩法页。
            private readonly int m_nextLevelID; // 普通关成功通关后可进入的后续关卡。

            internal ResultRequest(GamePlayUIView owner, int nextLevelID, CancellationTokenSource loading)
            {
                m_owner = owner;
                m_nextLevelID = nextLevelID;
                Loading = loading;
                Data = new ResultUIData(
                    owner.GetResultTitle(),
                    (int)owner.m_controller.ElapsedSeconds,
                    owner.m_controller.SortedBottleCount,
                    owner.m_controller.PourStepCount,
                    nextLevelID > 0,
                    ReturnToSelectionAsync,
                    EnterNextLevelAsync);
            }

            internal ResultUIData Data { get; }
            internal UniTaskCompletionSource Opened { get; } = new UniTaskCompletionSource();
            internal CancellationTokenSource Loading { get; set; }
            internal UIHandle<ResultUIController> Handle { get; set; }

            private bool CanSubmit => ReferenceEquals(m_owner.m_resultRequest, this)
                && m_owner.m_playing && !m_owner.m_navigating;

            private UniTask ReturnToSelectionAsync()
            {
                return CanSubmit ? m_owner.ReturnToSelectionAsync() : UniTask.CompletedTask;
            }

            private UniTask EnterNextLevelAsync()
            {
                if (CanSubmit && m_nextLevelID > 0)
                    m_owner.EnterLevel(m_nextLevelID);

                return UniTask.CompletedTask;
            }
        }
    }
}
