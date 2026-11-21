using System;
using System.Collections.Generic;
using System.Threading;
using AlloyFramework;
using AlloyFramework.UI;
using Cysharp.Threading.Tasks;
using Game.WaterSortPuzzle;
using UnityEngine;

namespace Game.UI
{
    public sealed partial class GamePlayUIView
    {
        private const float REFERENCEWIDTH = 1760f;
        private const float REFERENCEHEIGHT = 840f;

        private readonly Dictionary<BottleData, BottleCell> m_cells = new Dictionary<BottleData, BottleCell>(); // 瓶子映射。
        private readonly List<BottleCell> m_liveCells = new List<BottleCell>(); // 包括正在滑出的实例。
        private readonly Stack<BottleCell> m_bottlePool = new Stack<BottleCell>(); // 回收后的瓶子。
        private IAssetHandle<GameObject> m_bottlePrefabHandle; // 与瓶子实例共用页面生命周期的预制体句柄。
        private WaterSortPuzzleController m_controller; // 当前打开期间的游戏会话。
        private ConfirmationRequest m_confirmationRequest; // 当前关卡正在等待的确认请求。
        private string m_levelLabel; // 普通关卡标题。
        private bool m_playing; // 是否已载入关卡。
        private bool m_navigating; // 是否正在返回选关页。
        private int m_navigationVersion; // 避免旧的跳转完成后影响复用页面。
        private int m_lastReverse = int.MinValue; // 上次显示的撤销次数。
        private int m_lastTimerSecond = -1; // 上次显示的无尽倒计时秒数。

        private bool CanPlay => m_playing && !m_navigating && m_confirmationRequest == null && !m_controller.IsStopped;

        internal async UniTask PrepareGameplayAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (m_bottlePrefabHandle != null)
                return;

            // 页面关闭后可能复用，瓶子实例和源资源一同保留至页面销毁。
            IAssetHandle<GameObject> handle = await WaterSortPuzzleResources.LoadBottlePrefabAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                m_bottlePrefabHandle = handle;
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        internal void OpenSession()
        {
            if (m_controller != null)
                return;

            m_controller = new WaterSortPuzzleController();
            m_controller.LevelLoaded += HandleLevelLoaded;
            m_controller.BottleReplaced += HandleBottleReplaced;
            m_controller.Completed += HandleCompleted;
            m_controller.BeginPour += HandleHintPourStarted;
            m_controller.FinishPour += HandleHintPourFinished;
            m_controller.SyncData += HandleHintUndo;
            m_controller.Stopped += StopHints;
            GoResult.SetActive(false);
            BtnClose.onClick.AddListener(HandleResultClose);
            BtnNext.onClick.AddListener(HandleNextLevel);
            BtnBack.onClick.AddListener(HandleBack);
            BtnReset.onClick.AddListener(HandleReset);
            BtnRetrun.onClick.AddListener(HandleReverse);
            BtnHint.onClick.AddListener(StartHints);
            RefreshButtons();
        }

        internal void EnterLevel(int levelID)
        {
            if (m_controller == null)
                throw new InvalidOperationException("倒水页面尚未打开。");

            if (!WaterSortProgress.IsUnlocked(levelID))
                throw new ArgumentOutOfRangeException(nameof(levelID), "关卡不存在或尚未解锁。");

            m_playing = true;
            m_controller.LoadLevel(levelID);
        }

        internal void CloseSession()
        {
            if (m_controller == null)
                return;

            // 先终止命令，再回收视图和解除本局的事件订阅。
            m_playing = false;
            m_navigationVersion++;
            m_navigating = false;
            InvalidateConfirmation();
            m_controller.Stop();
            ReleaseAll();
            RefreshButtons();
            m_controller.LevelLoaded -= HandleLevelLoaded;
            m_controller.BottleReplaced -= HandleBottleReplaced;
            m_controller.Completed -= HandleCompleted;
            m_controller.BeginPour -= HandleHintPourStarted;
            m_controller.FinishPour -= HandleHintPourFinished;
            m_controller.SyncData -= HandleHintUndo;
            m_controller.Stopped -= StopHints;
            GoResult.SetActive(false);
            BtnClose.onClick.RemoveListener(HandleResultClose);
            BtnNext.onClick.RemoveListener(HandleNextLevel);
            BtnBack.onClick.RemoveListener(HandleBack);
            BtnReset.onClick.RemoveListener(HandleReset);
            BtnRetrun.onClick.RemoveListener(HandleReverse);
            BtnHint.onClick.RemoveListener(StartHints);
            m_controller = null;
        }

        private void Update()
        {
            if (!m_playing)
                return;

            float deltaTime = Time.unscaledDeltaTime;
            m_controller.Tick(deltaTime);
            if (m_controller.IsInfinity && !m_controller.IsStopped)
            {
                int second = Mathf.CeilToInt(m_controller.RemainingTime);
                if (second != m_lastTimerSecond)
                    RefreshLevelInfo();
            }

            // 外部关闭或刷新可能没有按钮回调；正常关闭期间保留等待中的选择。
            ConfirmationRequest request = m_confirmationRequest;
            UIHandle<ConfirmUIController> handle = request?.Handle;
            if (handle != null && (!handle.IsValid ||
                (handle.State != UIState.Closing && !handle.Controller.IsShowing(request.Data))))
                m_confirmationRequest = null;

            RefreshButtons();
        }

        private void LateUpdate()
        {
            RefreshPendingHint();
            // 等所有瓶子更新完倾角和接水动画，再按本帧液面连接水流。
            foreach (BottleCell source in m_liveCells)
            {
                if (source.Data.State == EBottleState.Pouring
                    && m_cells.TryGetValue(source.PourTarget, out BottleCell target))
                    source.RefreshStream(target);
            }
        }

        private BottleCell RentBottle(BottleData bottle)
        {
            BottleCell cell = m_bottlePool.Count > 0 ? m_bottlePool.Pop() : CreateBottle();
            cell.gameObject.SetActive(true);
            cell.Bind(m_controller, bottle);
            cell.Released += HandleReleased;
            m_liveCells.Add(cell);
            m_cells[bottle] = cell;
            return cell;
        }

        private BottleCell CreateBottle()
        {
            // 瓶身和图层来自现有预制体，BottleCell 只负责瓶子的交互和动画。
            GameObject instance = Instantiate(m_bottlePrefabHandle.Asset, GoBottle.transform, false);
            if (instance.TryGetComponent(out BottleCell cell))
                return cell;

            return instance.AddComponent<BottleCell>();
        }

        private void HandleReleased(BottleCell cell)
        {
            if (!m_liveCells.Remove(cell))
                return;

            // 滑出的旧瓶只能移除自己的数据映射。
            if (cell.Data != null && m_cells.TryGetValue(cell.Data, out BottleCell mapped) && mapped == cell)
                m_cells.Remove(cell.Data);
            cell.Released -= HandleReleased;
            cell.Unbind();
            cell.gameObject.SetActive(false);
            m_bottlePool.Push(cell);
        }

        private void ReleaseAll()
        {
            while (m_liveCells.Count > 0)
                HandleReleased(m_liveCells[m_liveCells.Count - 1]);
            m_cells.Clear();
        }

        private void HandleLevelLoaded()
        {
            InvalidateConfirmation();
            GoResult.SetActive(false);
            ReleaseAll();

            // 按现有棋盘容器的可用尺寸调整瓶子数据，保留容器在预制体中的布局。
            RectTransform board = (RectTransform)GoBottle.transform;
            Vector2 size = board.rect.size;
            float boardScale = Mathf.Min(size.x / REFERENCEWIDTH, size.y / REFERENCEHEIGHT);
            if (boardScale <= 0f)
                boardScale = 1f;

            IReadOnlyList<BottleData> bottles = m_controller.Bottles;
            WaterSortLayout.Apply(bottles);
            float centerY = (bottles[0].LocalPos.y + bottles[bottles.Count - 1].LocalPos.y) * 0.5f;
            for (int index = 0; index < bottles.Count; index++)
            {
                BottleData bottle = bottles[index];
                bottle.LocalPos = (bottle.LocalPos - Vector2.up * centerY) * boardScale;
                bottle.LocalScale *= boardScale;
                RentBottle(bottle).PlaySlideIn(true);
            }

            LevelDefinition level = WaterSortLevelCatalog.Get(m_controller.LevelID);
            m_levelLabel = "关卡 " + level.Name;
            m_lastReverse = int.MinValue;
            RefreshLevelInfo();
            RefreshButtons();
        }

        private void HandleBottleReplaced(BottleData previous, BottleData replacement)
        {
            // 撤销可能发生在旧瓶滑出途中，先清理相同数据的旧视图。
            for (int index = m_liveCells.Count - 1; index >= 0; index--)
            {
                BottleCell cell = m_liveCells[index];
                if (cell.Data == replacement)
                    HandleReleased(cell);
            }

            if (m_cells.TryGetValue(previous, out BottleCell outgoing))
            {
                m_cells.Remove(previous);
                outgoing.PlaySlideOut(false);
            }
            RentBottle(replacement).PlaySlideIn(false);
        }

        private void RefreshButtons()
        {
            bool canPlay = CanPlay;
            BtnBack.interactable = canPlay;
            BtnReset.interactable = canPlay;
            BtnRetrun.interactable = canPlay && m_controller.CanReverse;
            BtnHint.interactable = canPlay && !m_controller.IsInfinity && !m_hintsEnabled;
            BtnClose.interactable = m_playing && !m_navigating;
            BtnNext.interactable = BtnClose.interactable;
            if (m_lastReverse != m_controller.ReverseRemaining)
            {
                m_lastReverse = m_controller.ReverseRemaining;
                TxtTiems.text = m_lastReverse < 0 ? "∞" : m_lastReverse.ToString();
            }
        }

        private void RefreshLevelInfo()
        {
            if (m_controller.IsStopped)
            {
                TxtLevelInfo.text = m_controller.IsInfinity ? m_controller.Score + "分" : "关卡完成";
            }
            else if (m_controller.IsInfinity)
            {
                m_lastTimerSecond = Mathf.CeilToInt(m_controller.RemainingTime);
                TxtLevelInfo.text = m_lastTimerSecond + " 秒";
            }
            else
            {
                TxtLevelInfo.text = GetHintLevelInfo();
            }
        }

        private void HandleReverse()
        {
            if (!CanPlay)
                return;

            m_controller.Reverse();
            RefreshLevelInfo();
            RefreshButtons();
        }

        private void HandleReset()
        {
            if (CanPlay)
                OpenConfirmationAsync("重置关卡", "要重新开始当前关卡吗？", m_controller.Reset).Forget();
        }

        private void HandleBack()
        {
            if (!CanPlay)
                return;

            string description = m_controller.IsInfinity ? "要结束挑战并结算当前得分吗？" : "要返回选关界面吗？";
            OpenConfirmationAsync("返回", description, HandleConfirmedBack).Forget();
        }

        private void HandleConfirmedBack()
        {
            if (m_controller.IsInfinity)
            {
                SettleInfinity();
                ShowResult();
                return;
            }

            ReturnToSelectionAsync().Forget();
        }

        private async UniTask OpenConfirmationAsync(string title, string description, Action onConfirm)
        {
            // 在异步加载前占用请求，并为每次请求绑定独立的回调身份。
            using var loading = new CancellationTokenSource();
            var request = new ConfirmationRequest(this, title, description, onConfirm, loading);
            m_confirmationRequest = request;
            RefreshButtons();
            try
            {
                UIHandle<ConfirmUIController> handle =
                    await UIManager.Instance.OpenAsync(GameUI.ConfirmUI, request.Data, loading.Token);
                if (ReferenceEquals(m_confirmationRequest, request))
                    request.Handle = handle;
                else if (handle.IsValid && handle.Controller.IsShowing(request.Data))
                    await handle.CloseAsync();
            }
            catch (OperationCanceledException)
            {
                // 关卡结束或界面关闭后，取消仍在加载的弹窗。
            }
            finally
            {
                request.Loading = null;
                if (request.Handle == null && ReferenceEquals(m_confirmationRequest, request))
                {
                    m_confirmationRequest = null;
                    RefreshButtons();
                }
            }
        }

        private void FinishConfirmation(ConfirmationRequest request, Action action)
        {
            // 弹窗关闭后才收到选择，关卡切换、结算或关页都会先清除旧请求。
            if (!ReferenceEquals(m_confirmationRequest, request))
                return;

            m_confirmationRequest = null;
            RefreshButtons();
            if (CanPlay)
                action?.Invoke();
        }

        private void InvalidateConfirmation()
        {
            ConfirmationRequest request = m_confirmationRequest;
            if (request == null)
                return;

            // 先阻止旧回调，再取消加载或按句柄关闭仍显示此请求的弹窗。
            m_confirmationRequest = null;
            request.Loading?.Cancel();
            UIHandle<ConfirmUIController> handle = request.Handle;
            if (handle != null && handle.IsValid && handle.Controller.IsShowing(request.Data))
                handle.CloseAsync().Forget();
        }

        private void HandleCompleted()
        {
            InvalidateConfirmation();
            if (m_controller.IsInfinity)
                SettleInfinity();
            else
                WaterSortProgress.RecordCompletion(m_controller.LevelID);

            ShowResult();
        }

        private void ShowResult()
        {
            // 结算时冻结三个数值，同一场游戏内重试的时间计入关卡耗时。
            int seconds = (int)m_controller.ElapsedSeconds;
            TxtUseTime.text = $"{seconds / 60:00}:{seconds % 60:00}";
            TxtBottleNum.text = m_controller.SortedBottleCount.ToString();
            TxtRestNum.text = m_controller.ResetCount.ToString();
            BtnNext.gameObject.SetActive(CanEnterNextLevel());
            GoResult.SetActive(true);
            RefreshLevelInfo();
            RefreshButtons();
        }

        private bool CanEnterNextLevel()
        {
            int nextID = m_controller.LevelID + 1;
            return m_controller.IsCompleted && !m_controller.IsInfinity && WaterSortProgress.IsUnlocked(nextID)
                && !WaterSortLevelCatalog.Get(nextID).IsInfinity;
        }

        private void HandleResultClose()
        {
            if (GoResult.activeSelf && !m_navigating) ReturnToSelectionAsync().Forget();
        }

        private void HandleNextLevel()
        {
            if (GoResult.activeSelf && !m_navigating && CanEnterNextLevel()) EnterLevel(m_controller.LevelID + 1);
        }

        private void SettleInfinity()
        {
            if (!m_controller.IsStopped)
                m_controller.Stop();
            WaterSortProgress.RecordBestScore(m_controller.Score);
        }

        private async UniTask ReturnToSelectionAsync()
        {
            // 选关页加载期间阻止重复操作，失败后仍可通过返回按钮重试。
            int version = m_navigationVersion;
            m_navigating = true;
            RefreshButtons();
            try
            {
                await WaterSortNavigation.OpenSelectionAsync();
            }
            finally
            {
                if (m_navigationVersion == version)
                {
                    m_navigating = false;
                    RefreshLevelInfo();
                    RefreshButtons();
                }
            }
        }

        private void OnDestroy()
        {
            CloseSession();
            m_bottlePrefabHandle?.Dispose();
            m_bottlePrefabHandle = null;
        }

        private sealed class ConfirmationRequest
        {
            private readonly GamePlayUIView m_owner; // 发起请求的玩法页。
            private readonly Action m_onConfirm; // 确认且仍属于当前关卡时执行的操作。

            internal ConfirmationRequest(
                GamePlayUIView owner,
                string title,
                string description,
                Action onConfirm,
                CancellationTokenSource loading)
            {
                m_owner = owner;
                m_onConfirm = onConfirm;
                Loading = loading;
                Data = new ConfirmUIData(title, description, HandleCancel, HandleConfirm);
            }

            internal ConfirmUIData Data { get; }
            internal CancellationTokenSource Loading { get; set; }
            internal UIHandle<ConfirmUIController> Handle { get; set; }

            private void HandleCancel()
            {
                m_owner.FinishConfirmation(this, null);
            }

            private void HandleConfirm()
            {
                m_owner.FinishConfirmation(this, m_onConfirm);
            }
        }
    }
}
