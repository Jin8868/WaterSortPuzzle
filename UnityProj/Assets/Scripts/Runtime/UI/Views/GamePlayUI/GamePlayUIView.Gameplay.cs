using System;
using System.Collections.Generic;
using System.Threading;
using AlloyFramework;
using Cysharp.Threading.Tasks;
using Game.WaterSortPuzzle;
using UnityEngine;

namespace Game.UI
{
    public sealed partial class GamePlayUIView
    {
        private const float CONFIRMATIONSECONDS = 3f;
        private const float REFERENCEWIDTH = 1760f;
        private const float REFERENCEHEIGHT = 840f;

        private readonly Dictionary<BottleData, BottleCell> m_cells = new Dictionary<BottleData, BottleCell>(); // 瓶子映射。
        private readonly List<BottleCell> m_liveCells = new List<BottleCell>(); // 包括正在滑出的实例。
        private readonly Stack<BottleCell> m_bottlePool = new Stack<BottleCell>(); // 回收后的瓶子。
        private IAssetHandle<GameObject> m_bottlePrefabHandle; // 与瓶子实例共用页面生命周期的预制体句柄。
        private WaterSortPuzzleController m_controller; // 当前打开期间的游戏会话。
        private EConfirmation m_confirmation; // 等待再次点击的操作。
        private float m_confirmationRemaining; // 二次点击的确认窗口。
        private string m_levelLabel; // 普通关卡标题。
        private bool m_playing; // 是否已载入关卡。
        private bool m_navigating; // 是否正在返回选关页。
        private int m_navigationVersion; // 避免旧的跳转完成后影响复用页面。
        private int m_lastReverse = int.MinValue; // 上次显示的撤销次数。
        private int m_lastTimerSecond = -1; // 上次显示的无尽倒计时秒数。

        private enum EConfirmation
        {
            None,
            Reset,
            Back
        }

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
            GoResult.SetActive(false);
            BtnClose.onClick.AddListener(HandleResultClose);
            BtnNext.onClick.AddListener(HandleNextLevel);
            BtnBack.onClick.AddListener(HandleBack);
            BtnReset.onClick.AddListener(HandleReset);
            BtnRetrun.onClick.AddListener(HandleReverse);
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
            m_controller.Stop();
            ReleaseAll();
            ResetConfirmation();
            RefreshButtons();
            m_controller.LevelLoaded -= HandleLevelLoaded;
            m_controller.BottleReplaced -= HandleBottleReplaced;
            m_controller.Completed -= HandleCompleted;
            GoResult.SetActive(false);
            BtnClose.onClick.RemoveListener(HandleResultClose);
            BtnNext.onClick.RemoveListener(HandleNextLevel);
            BtnBack.onClick.RemoveListener(HandleBack);
            BtnReset.onClick.RemoveListener(HandleReset);
            BtnRetrun.onClick.RemoveListener(HandleReverse);
            m_controller = null;
        }

        private void Update()
        {
            if (!m_playing)
                return;

            float deltaTime = Time.unscaledDeltaTime;
            m_controller.Tick(deltaTime);
            if (m_confirmation != EConfirmation.None)
            {
                m_confirmationRemaining -= deltaTime;
                if (m_confirmationRemaining <= 0f)
                {
                    ResetConfirmation();
                    RefreshLevelInfo();
                }
            }
            else if (m_controller.IsInfinity && !m_controller.IsStopped)
            {
                int second = Mathf.CeilToInt(m_controller.RemainingTime);
                if (second != m_lastTimerSecond)
                    RefreshLevelInfo();
            }

            RefreshButtons();
        }

        private void LateUpdate()
        {
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
            GoResult.SetActive(false);
            ReleaseAll();
            ResetConfirmation();

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
            bool canPlay = m_playing && !m_navigating && !m_controller.IsStopped;
            BtnBack.interactable = canPlay;
            BtnReset.interactable = canPlay;
            BtnRetrun.interactable = canPlay && m_controller.CanReverse;
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
                TxtLevelInfo.text = m_levelLabel;
            }
        }

        private void HandleReverse()
        {
            ResetConfirmation();
            m_controller.Reverse();
            RefreshLevelInfo();
            RefreshButtons();
        }

        private void HandleReset()
        {
            if (Confirm(EConfirmation.Reset, "再按重置"))
                m_controller.Reset();
        }

        private void HandleBack()
        {
            if (!m_controller.IsStopped)
            {
                if (!Confirm(EConfirmation.Back, "再按返回"))
                    return;

                if (m_controller.IsInfinity)
                {
                    SettleInfinity();
                    ShowResult();
                    return;
                }
            }

            ReturnToSelectionAsync().Forget();
        }

        private bool Confirm(EConfirmation confirmation, string prompt)
        {
            if (m_confirmation == confirmation)
            {
                ResetConfirmation();
                return true;
            }

            m_confirmation = confirmation;
            m_confirmationRemaining = CONFIRMATIONSECONDS;
            TxtLevelInfo.text = prompt;
            return false;
        }

        private void ResetConfirmation()
        {
            m_confirmation = EConfirmation.None;
            m_confirmationRemaining = 0f;
        }

        private void HandleCompleted()
        {
            ResetConfirmation();
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
    }
}
