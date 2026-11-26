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
        private int m_lastTimerSecond = -1; // 上次显示的倒计时秒数。
        private RectTransform m_board; // 承载全部瓶子的棋盘节点。
        private float m_boardAnchorY; // 预制体中棋盘区域相对锚点的原始高度。
        private Rect m_boardLayoutBounds; // 相对静止瓶组中心的完整动画范围。

        private bool CanPlay => m_playing && !m_navigating && m_confirmationRequest == null && !m_controller.IsStopped;

        internal async UniTask PrepareGameplayAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (m_audioScope == null) await PrepareAudioAsync(cancellationToken);
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
            m_controller.TimedOut += HandleTimedOut;
            m_controller.BeginPour += HandleHintPourStarted;
            m_controller.FinishPour += HandleHintPourFinished;
            m_controller.SyncData += HandleHintUndo;
            m_controller.Stopped += StopHints;
            BtnBack.onClick.AddListener(HandleBack);
            BtnReset.onClick.AddListener(HandleReset);
            BtnRetrun.onClick.AddListener(HandleReverse);
            BtnHint.onClick.AddListener(StartHints);
            RefreshButtons();
        }

        internal void EnterLevel(int levelID)
        {
            if (!WaterSortProgress.IsUnlocked(levelID))
                throw new ArgumentOutOfRangeException(nameof(levelID), "关卡不存在或尚未解锁。");

            StartLevel(levelID);
        }

        internal void StartLevel(int levelID)
        {
            if (m_controller == null)
                throw new InvalidOperationException("倒水页面尚未打开。");

            m_playing = true;
            m_controller.LoadLevel(levelID);
        }

        internal void CloseSession()
        {
            if (m_controller == null)
                return;

            // 先终止命令，再回收视图和解除本局的事件订阅。
            m_playing = false;
            StopGameplayAudio();
            m_navigationVersion++;
            m_navigating = false;
            InvalidateConfirmation();
            InvalidateResult();
            m_controller.Stop();
            ReleaseAll();
            RefreshButtons();
            m_controller.LevelLoaded -= HandleLevelLoaded;
            m_controller.BottleReplaced -= HandleBottleReplaced;
            m_controller.Completed -= HandleCompleted;
            m_controller.TimedOut -= HandleTimedOut;
            m_controller.BeginPour -= HandleHintPourStarted;
            m_controller.FinishPour -= HandleHintPourFinished;
            m_controller.SyncData -= HandleHintUndo;
            m_controller.Stopped -= StopHints;
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
            if (!m_controller.IsStopped)
            {
                int second = Mathf.CeilToInt(m_controller.RemainingTime);
                UpdateCountdownAudio(second);
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
            if (m_playing)
                FitBoard();

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
            StartGameplayAudio();
            InvalidateConfirmation();
            InvalidateResult();
            ReleaseAll();

            IReadOnlyList<BottleData> bottles = m_controller.Bottles;
            if (m_board == null)
            {
                m_board = (RectTransform)GoBottle.transform;
                m_boardAnchorY = m_board.anchoredPosition.y;
            }

            // 按当前安全区选择排列，动画期间只缩放棋盘，不移动其他瓶子的原位。
            RectTransform safeArea = (RectTransform)m_board.parent;
            float centerY = safeArea.rect.center.y - (m_board.localPosition.y - m_board.anchoredPosition.y);
            Rect available = m_board.rect;
            m_boardLayoutBounds = WaterSortLayout.Apply(bottles, available.width,
                m_boardAnchorY + available.yMax - centerY, centerY - m_boardAnchorY - available.yMin);
            foreach (BottleData bottle in bottles)
            {
                RentBottle(bottle).PlaySlideIn(true);
            }
            FitBoard();

            LevelDefinition level = WaterSortLevelCatalog.Get(m_controller.LevelID);
            m_levelLabel = "关卡 " + level.Name;
            m_lastReverse = int.MinValue;
            RefreshLevelInfo();
            RefreshButtons();
        }

        private void FitBoard()
        {
            if (m_board == null)
                return;

            Rect available = m_board.rect;
            if (available.width <= 0f || available.height <= 0f)
                return;

            // 从安全区中心开始布局，只使用关卡的静态边界，避免棋盘跟随倒水动画移动。
            RectTransform safeArea = (RectTransform)m_board.parent;
            Vector2 position = m_board.anchoredPosition;
            float anchorY = m_board.localPosition.y - position.y;
            float centerY = safeArea.rect.center.y - anchorY;
            float bottom = m_boardAnchorY + available.yMin;
            float top = m_boardAnchorY + available.yMax;
            // 固定静止瓶组的中心，分别用中心到上下边缘的净空限制缩放。
            float scale = Mathf.Min(1f, available.width / m_boardLayoutBounds.width);
            scale = Mathf.Min(scale, (top - centerY) / m_boardLayoutBounds.yMax);
            scale = Mathf.Clamp01(Mathf.Min(scale, (centerY - bottom) / -m_boardLayoutBounds.yMin));
            if (!Mathf.Approximately(m_board.localScale.x, scale))
                m_board.localScale = new Vector3(scale, scale, 1f);

            position.y = centerY;
            if (!Mathf.Approximately(m_board.anchoredPosition.y, position.y))
                m_board.anchoredPosition = position;
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
            BtnBack.interactable = canPlay || CanLeaveWithoutResult;
            BtnReset.interactable = canPlay;
            BtnRetrun.interactable = canPlay && m_controller.CanReverse;
            BtnHint.interactable = CanStartHints;
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
                string result = m_controller.IsInfinity ? m_controller.Score + " 分" : GetResultTitle();
                TxtLevelInfo.text = $"<size=54><b>{result}</b></size>";
            }
            else
            {
                // 大号数字占据独立一行，最后五秒的颜色与逐秒提示同步。
                m_lastTimerSecond = Mathf.Max(0, Mathf.CeilToInt(m_controller.RemainingTime));
                string timer = $"{m_lastTimerSecond / 60:00}:{m_lastTimerSecond % 60:00}";
                string timerColor = m_lastTimerSecond <= COUNTDOWNSECONDS ? "#FF9081" : "#FFD786";
                string timerLine = $"<size=64><b><color={timerColor}>{timer}</color></b></size>";
                string levelInfo = m_controller.IsInfinity ? "无尽模式" : GetHintLevelInfo();
                TxtLevelInfo.text = timerLine + "\n" + levelInfo;
            }
        }

        private string GetResultTitle()
        {
            if (m_controller.IsTimedOut)
                return "超时失败";
            return m_controller.IsInfinity ? "挑战结束" : "关卡完成";
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
            if (CanLeaveWithoutResult)
            {
                ReturnToSelectionAsync().Forget();
                return;
            }

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
                ShowResultAsync().Forget();
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
                request.Opened.TrySetResult();
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
            DismissConfirmationAsync().Forget();
        }

        private async UniTask DismissConfirmationAsync()
        {
            ConfirmationRequest request = m_confirmationRequest;
            if (request == null)
                return;

            // 先阻止旧回调，再取消加载或按句柄关闭仍显示此请求的弹窗。
            m_confirmationRequest = null;
            request.Loading?.Cancel();
            await request.Opened.Task;
            UIHandle<ConfirmUIController> handle = request.Handle;
            if (handle != null && handle.IsValid
                && (handle.State == UIState.Closing || handle.Controller.IsShowing(request.Data)))
                await handle.CloseAsync();
        }

        private void HandleCompleted()
        {
            if (m_controller.IsInfinity)
                SettleInfinity();
            else
                WaterSortProgress.RecordCompletion(
                    m_controller.LevelID,
                    m_controller.PourStepCount,
                    (float)m_controller.ElapsedSeconds);

            ShowResultAsync().Forget();
        }

        private void HandleTimedOut()
        {
            if (m_controller.IsInfinity)
                WaterSortProgress.RecordBestScore(m_controller.Score);
            ShowResultAsync().Forget();
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
            m_audioScope?.Dispose();
            m_audioScope = null;
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
            internal UniTaskCompletionSource Opened { get; } = new UniTaskCompletionSource();
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
