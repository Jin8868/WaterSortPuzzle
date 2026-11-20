using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.WaterSortPuzzle;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Game.UI
{
    public sealed partial class GamePlayUIView
    {
        private readonly Dictionary<BottleData, BottleCell> m_cells = new Dictionary<BottleData, BottleCell>(); // 瓶子映射。
        private readonly List<BottleCell> m_liveCells = new List<BottleCell>(); // 包括正在滑出的实例。
        private readonly Stack<BottleCell> m_bottlePool = new Stack<BottleCell>(); // 回收后的瓶子。
        private readonly Dictionary<BottleCell, Image> m_streams = new Dictionary<BottleCell, Image>(); // 活跃水流。
        private readonly Stack<Image> m_streamPool = new Stack<Image>(); // 回收后的水流。
        private readonly List<Image> m_comboDots = new List<Image>(); // 连击窗口点阵。
        private WaterSortPuzzleController m_controller; // 当前打开期间的游戏会话。
        private WaterSortPuzzleAssets m_assets; // 原始预制引用集。
        private Font m_font; // 界面共享动态字体。
        private RectTransform m_safeArea; // 预制体中的安全区布局根节点。
        private RectTransform m_boardHost; // 预制体中用于摆放瓶子的节点。
        private RectTransform m_playfield; // 缩放后的瓶子和水流坐标空间。
        private RectTransform m_board; // 瓶子布局根节点。
        private RectTransform m_streamRoot; // 水流层。
        private RectTransform m_poolRoot; // 隐藏池层。
        private RectTransform m_hud; // 局内操作面板。
        private RectTransform m_modal; // 模态遮挡层。
        private Text m_title; // 当前关卡标题。
        private Text m_score; // 延迟滚动的分数。
        private Text m_timer; // 倒计时文本。
        private Text m_modalText; // 确认及结算说明。
        private Text m_confirmLabel; // 确认按钮标题。
        private Text m_bonus; // 加时提示。
        private Image m_timeBar; // 倒计时条。
        private Button m_cancelButton; // 对话框取消按钮。
        private UnityAction m_confirmAction; // 当前确认动作的命名方法引用。
        private RectTransform m_dialogPanel; // 以局内参考尺寸适配的对话框内容。
        private AudioListener m_audioListener; // 场景未提供监听器时临时启用的监听器。
        private Button m_resetButton; // 重置按钮。
        private Button m_helpButton; // 玩法说明按钮。
        private Button m_confirmButton; // 对话框确认按钮。
        private AudioSource m_audioSource; // 最后十秒的倒计时音效。
        private AudioClip m_tickClip; // 程序生成的短提示音。
        private bool m_initialized; // 防止重复创建界面。
        private bool m_playing; // 是否处于关卡中。
        private bool m_sessionOpen; // 框架当前是否已打开本页。
        private int m_navigationVersion; // 避免旧的跳转完成后影响复用页面。
        private bool m_navigating; // 是否正在返回选关页。
        private Vector2 m_lastSafeSize; // 上次用于局内布局的安全区尺寸。
        private float m_displayScore; // 当前显示分数。
        private float m_scoreDelay; // 加分后保留旧分数半秒。
        private float m_bonusRemaining; // 加时提示剩余显示时间。
        private float m_lastRemainingTime; // 用于识别新增时间。
        private int m_lastScore = -1; // 上次已显示的整数分数。
        private int m_lastCombo = -1; // 上次已显示的连击数。
        private int m_lastReverse = int.MinValue; // 上次显示的撤销次数。
        private int m_lastTickSecond = -1; // 避免一秒内重复提示。

        internal void InitializeGameplay()
        {
            if (m_initialized)
                return;

            // 在框架页面首次创建时准备局内资源，关闭后保留节点和实例池。
            m_assets = Resources.Load<WaterSortPuzzleAssets>("WaterSortPuzzle/WaterSortPuzzleAssets");
            if (m_assets == null || m_assets.BottlePrefab == null)
                throw new InvalidOperationException("倒水游戏缺少 WaterSortPuzzleAssets 或瓶子预制引用。");

            m_font = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", "Arial" }, 28);
            BuildUI();
            BuildTickAudio();
            m_initialized = true;

            for (int index = 0; index < 3; index++)
                m_bottlePool.Push(CreateBottle());
        }

        internal void OpenSession()
        {
            if (m_sessionOpen)
                return;

            m_controller = new WaterSortPuzzleController();
            m_sessionOpen = true;
            BtnRetrun.interactable = false;
            m_controller.LevelLoaded += HandleLevelLoaded;
            m_controller.BottleReplaced += HandleBottleReplaced;
            m_controller.ScoreChanged += HandleScoreChanged;
            m_controller.TimeChanged += HandleTimeChanged;
            m_controller.Completed += HandleCompleted;
            BtnBack.onClick.AddListener(HandleBack);
            BtnRetrun.onClick.AddListener(HandleReverse);
            m_resetButton.onClick.AddListener(HandleReset);
            m_helpButton.onClick.AddListener(HandleHelp);
            m_confirmButton.onClick.AddListener(HandleConfirm);
            m_cancelButton.onClick.AddListener(CloseModal);
        }

        internal void EnterLevel(int levelID)
        {
            if (!m_sessionOpen)
                throw new InvalidOperationException("倒水页面尚未打开。");

            if (!WaterSortProgress.IsUnlocked(levelID))
                throw new ArgumentOutOfRangeException(nameof(levelID), "关卡不存在或尚未解锁。");

            // 场景没有监听器时保持原试玩版的倒水声与倒计时提示。
            AudioListener sceneListener = FindObjectOfType<AudioListener>();
            if (m_audioListener == null && sceneListener == null)
                m_audioListener = gameObject.AddComponent<AudioListener>();
            if (m_audioListener != null)
                m_audioListener.enabled = sceneListener == null || sceneListener == m_audioListener;

            m_playing = true;
            CloseModal();
            m_hud.gameObject.SetActive(true);
            FitSafeArea();
            m_controller.LoadLevel(levelID);
        }

        internal void CloseSession()
        {
            if (!m_sessionOpen)
                return;

            // 先终止命令和音效，再回收视图、解除所有本局监听。
            m_sessionOpen = false;
            m_playing = false;
            m_navigationVersion++;
            m_navigating = false;
            BtnRetrun.interactable = false;
            m_controller.Stop();
            ReleaseAll();
            CloseModal();
            m_hud.gameObject.SetActive(false);
            m_audioSource.Stop();
            if (m_audioListener != null)
                m_audioListener.enabled = false;

            m_controller.LevelLoaded -= HandleLevelLoaded;
            m_controller.BottleReplaced -= HandleBottleReplaced;
            m_controller.ScoreChanged -= HandleScoreChanged;
            m_controller.TimeChanged -= HandleTimeChanged;
            m_controller.Completed -= HandleCompleted;
            m_controller = null;
            BtnBack.onClick.RemoveListener(HandleBack);
            BtnRetrun.onClick.RemoveListener(HandleReverse);
            m_resetButton.onClick.RemoveListener(HandleReset);
            m_helpButton.onClick.RemoveListener(HandleHelp);
            m_confirmButton.onClick.RemoveListener(HandleConfirm);
            m_cancelButton.onClick.RemoveListener(CloseModal);
        }

        private void Update()
        {
            if (!m_playing)
                return;
            m_controller.Tick(Time.unscaledDeltaTime);
            m_scoreDelay -= Time.unscaledDeltaTime;
            if (m_scoreDelay <= 0f)
                m_displayScore = Mathf.MoveTowards(m_displayScore, m_controller.Score,
                    Mathf.Max(200f, Mathf.Abs(m_controller.Score - m_displayScore) * 8f) * Time.unscaledDeltaTime);
            int score = Mathf.RoundToInt(m_displayScore);
            if (score != m_lastScore || m_lastCombo != m_controller.Combo)
            {
                m_lastScore = score;
                m_lastCombo = m_controller.Combo;
                m_score.text = "得分 " + score + "   连击 ×" + m_lastCombo;
            }
            m_bonusRemaining -= Time.unscaledDeltaTime;
            m_bonus.gameObject.SetActive(m_bonusRemaining > 0f);
            m_bonus.rectTransform.anchoredPosition = new Vector2(640f, 330f + (1.2f - m_bonusRemaining) * 35f);
            RefreshHUD();
        }

        private void LateUpdate()
        {
            if (!m_playing)
                return;

            FitSafeArea();
            // 每帧跟随旋转后的瓶口，水流始终沿屏幕竖直方向。
            foreach (KeyValuePair<BottleCell, Image> entry in m_streams)
            {
                BottleCell cell = entry.Key;
                RectTransform stream = entry.Value.rectTransform;
                Vector3 start = m_streamRoot.InverseTransformPoint(cell.WaterFallHangPoint.position);
                float length = 100f * cell.Data.LocalScale;
                if (cell.PourTarget != null && m_cells.TryGetValue(cell.PourTarget, out BottleCell target))
                {
                    Vector3 end = m_streamRoot.InverseTransformPoint(target.WaterFallHangPoint.position);
                    length = Mathf.Max(5f, start.y - end.y);
                }
                stream.anchoredPosition = start;
                stream.sizeDelta = new Vector2(10f * cell.Data.LocalScale, length);
            }
        }

        private void BuildUI()
        {
            // 瓶子、返回和撤销沿用预制体节点，补充的显示层由同一个页面管理。
            m_boardHost = (RectTransform)GoBottle.transform;
            m_safeArea = (RectTransform)m_boardHost.parent;
            ConfigureBackground();
            m_playfield = CreateRect("Playfield", m_boardHost, Vector2.zero, Vector2.zero);
            m_board = CreateRect("Bottles", m_playfield, Vector2.zero, Vector2.zero);
            m_streamRoot = CreateRect("Streams", m_playfield, Vector2.zero, Vector2.zero);
            m_poolRoot = CreateRect("Pool", m_safeArea, Vector2.zero, Vector2.zero);
            m_poolRoot.gameObject.SetActive(false);
            ConfigurePrefabLabels();
            BuildHUD();
            BuildModal();
            m_hud.gameObject.SetActive(false);
        }

        private void ConfigureBackground()
        {
            // 预制体已有全屏背景节点，沿用原倒水界面的底色补齐图像。
            Transform backgroundRoot = transform.Find("Background");
            Image background = backgroundRoot.GetComponent<Image>();
            if (background == null)
                background = backgroundRoot.gameObject.AddComponent<Image>();
            background.color = new Color(0.055f, 0.09f, 0.14f);
            background.raycastTarget = true;
        }

        private void ConfigurePrefabLabels()
        {
            // 使用局内共享字体，确保预制体上的中文标题和无限次数符号可显示。
            BtnBack.GetComponentInChildren<Text>().font = m_font;
            BtnRetrun.GetComponentInChildren<Text>().font = m_font;
            TxtTiems.font = m_font;
        }

        private void BuildHUD()
        {
            m_hud = CreateRect("HUD", m_safeArea, new Vector2(1920f, 1080f), Vector2.zero);
            m_title = CreateText("", m_hud, new Vector2(-640f, -492f), new Vector2(500f, 70f), 36);
            m_score = CreateText("", m_hud, new Vector2(0f, -492f), new Vector2(700f, 70f), 30);
            m_timer = CreateText("", m_hud, new Vector2(700f, -492f), new Vector2(300f, 70f), 32);
            m_timeBar = CreateImage("TimeBar", m_hud, new Color(0.35f, 0.8f, 0.75f));
            m_timeBar.rectTransform.anchoredPosition = new Vector2(0f, -442f);
            m_timeBar.rectTransform.sizeDelta = new Vector2(1650f, 6f);
            m_bonus = CreateText("+2 秒", m_hud, new Vector2(640f, 330f), new Vector2(230f, 70f), 32);
            m_bonus.gameObject.SetActive(false);

            // 按照命中连击窗口的剩余比例点亮底部点阵。
            for (int index = 0; index < 10; index++)
            {
                Image dot = CreateImage("ComboDot", m_hud, Color.white);
                dot.rectTransform.anchoredPosition = new Vector2((index - 4.5f) * 23f, -453f);
                dot.rectTransform.sizeDelta = new Vector2(15f, 5f);
                m_comboDots.Add(dot);
            }

            m_resetButton = CreateButton("重置", m_hud, new Vector2(-140f, 462f), new Vector2(235f, 76f));
            m_helpButton = CreateButton("玩法说明", m_hud, new Vector2(140f, 462f), new Vector2(235f, 76f));
        }

        private void FitSafeArea()
        {
            // 保持原瓶子和水流的局部坐标比例，并让内容避开安全区边缘。
            Vector2 safeSize = m_safeArea.rect.size;
            if (safeSize == m_lastSafeSize || safeSize.x <= 0f || safeSize.y <= 0f)
                return;

            m_lastSafeSize = safeSize;
            float scale = Mathf.Min(safeSize.x / 1920f, safeSize.y / 1080f);
            Vector3 scaled = new Vector3(scale, scale, 1f);
            m_playfield.localScale = scaled;
            m_playfield.anchoredPosition = -m_boardHost.anchoredPosition;
            m_hud.localScale = scaled;
            m_dialogPanel.localScale = scaled;
        }

        private void BuildModal()
        {
            Image shade = CreateImage("Dialog", transform, new Color(0f, 0f, 0f, 0.8f));
            m_modal = shade.rectTransform;
            Stretch(m_modal);
            shade.raycastTarget = true;
            Image panel = CreateImage("Panel", m_modal, new Color(0.1f, 0.17f, 0.23f));
            m_dialogPanel = panel.rectTransform;
            m_dialogPanel.sizeDelta = new Vector2(1100f, 570f);
            m_modalText = CreateText("", panel.transform, new Vector2(0f, 70f), new Vector2(980f, 350f), 32);
            m_confirmButton = CreateButton("确定", panel.transform, new Vector2(230f, -190f),
                new Vector2(310f, 80f));
            m_confirmLabel = m_confirmButton.GetComponentInChildren<Text>();
            m_cancelButton = CreateButton("取消", panel.transform, new Vector2(-230f, -190f),
                new Vector2(310f, 80f));
            m_modal.gameObject.SetActive(false);
        }

        private BottleCell CreateBottle()
        {
            GameObject instance = Instantiate(m_assets.BottlePrefab, m_poolRoot);
            BottleCell cell = instance.GetComponent<BottleCell>();
            if (cell == null)
                cell = instance.AddComponent<BottleCell>();
            instance.SetActive(false);
            return cell;
        }

        private BottleCell RentBottle(BottleData bottle)
        {
            BottleCell cell = m_bottlePool.Count > 0 ? m_bottlePool.Pop() : CreateBottle();
            cell.transform.SetParent(m_board, false);
            cell.gameObject.SetActive(true);
            cell.Bind(m_controller, bottle);
            cell.WaterFallChanged += HandleWaterFall;
            cell.Released += HandleReleased;
            m_liveCells.Add(cell);
            m_cells[bottle] = cell;
            return cell;
        }

        private void HandleReleased(BottleCell cell)
        {
            if (!m_liveCells.Remove(cell))
                return;

            // 回收滑出的旧瓶时，仅移除仍指向该实例的数据映射。
            if (cell.Data != null && m_cells.TryGetValue(cell.Data, out BottleCell mapped) && mapped == cell)
                m_cells.Remove(cell.Data);
            HandleWaterFall(cell, false);
            cell.WaterFallChanged -= HandleWaterFall;
            cell.Released -= HandleReleased;
            cell.Unbind();
            cell.transform.SetParent(m_poolRoot, false);
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
            ReleaseAll();
            // 三行模板向上让出底部状态栏，两行模板为顶排选中抬升留出空间。
            Vector2 center = new Vector2(0f, m_controller.Bottles.Count > 24 ? 135f : 72f);
            m_board.anchoredPosition = center;
            m_streamRoot.anchoredPosition = center;
            WaterSortLayout.Apply(m_controller.Bottles);
            for (int index = 0; index < m_controller.Bottles.Count; index++)
                RentBottle(m_controller.Bottles[index]).PlaySlideIn(true);
            m_displayScore = 0f;
            m_scoreDelay = 0f;
            m_lastScore = -1;
            m_score.text = "得分 0   连击 ×0";
            m_lastRemainingTime = m_controller.RemainingTime;
            m_lastTickSecond = -1;
            m_bonusRemaining = 0f;
            m_bonus.gameObject.SetActive(false);
            LevelDefinition level = WaterSortLevelCatalog.Get(m_controller.LevelID);
            m_title.text = level.IsInfinity ? "无尽挑战" : "关卡 " + level.Name;
            RefreshHUD();
        }

        private void HandleBottleReplaced(BottleData previous, BottleData replacement)
        {
            // 撤销可能发生在旧瓶滑出途中；先清理相同数据的旧视图，避免双重事件订阅。
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

        private void HandleWaterFall(BottleCell cell, bool visible)
        {
            if (!visible)
            {
                if (!m_streams.TryGetValue(cell, out Image existing))
                    return;

                m_streams.Remove(cell);
                existing.gameObject.SetActive(false);
                m_streamPool.Push(existing);
                return;
            }
            if (m_streams.ContainsKey(cell))
                return;

            Image stream = m_streamPool.Count > 0 ? m_streamPool.Pop() : CreateImage("WaterStream", m_streamRoot,
                Color.white);
            stream.rectTransform.pivot = new Vector2(0.5f, 1f);
            stream.color = cell.PourColor;
            stream.gameObject.SetActive(true);
            m_streams.Add(cell, stream);
        }

        private void HandleScoreChanged()
        {
            // 撤销立即回退显示，增加分数保留半秒后再追上。
            if (m_controller.Score < m_displayScore)
                m_displayScore = m_controller.Score;
            m_scoreDelay = 0.5f;
        }

        private void HandleTimeChanged()
        {
            float increase = m_controller.RemainingTime - m_lastRemainingTime;
            if (m_playing && m_controller.IsInfinity && increase > 0.5f)
            {
                m_bonus.text = "+" + Mathf.RoundToInt(increase) + " 秒";
                m_bonusRemaining = 1.2f;
            }
            m_lastRemainingTime = m_controller.RemainingTime;
        }

        private void RefreshHUD()
        {
            if (m_lastReverse != m_controller.ReverseRemaining)
            {
                m_lastReverse = m_controller.ReverseRemaining;
                TxtTiems.text = m_lastReverse < 0 ? "∞" : m_lastReverse.ToString();
            }
            BtnRetrun.interactable = m_controller.CanReverse;
            int second = Mathf.CeilToInt(m_controller.RemainingTime);
            if (m_lastTickSecond != second)
                m_timer.text = m_controller.IsInfinity ? second + " 秒" : "";
            m_timeBar.gameObject.SetActive(m_controller.IsInfinity);
            m_timeBar.rectTransform.sizeDelta = new Vector2(
                1650f * Mathf.Clamp01(m_controller.RemainingTime / WaterSortSettings.DURATION), 6f);
            m_timeBar.color = m_controller.RemainingTime <= 10f ? new Color(1f, 0.35f, 0.3f)
                : new Color(0.35f, 0.8f, 0.75f);
            float comboProgress = m_controller.ComboRemainingTime / m_controller.ComboWindow;
            for (int index = 0; index < m_comboDots.Count; index++)
                m_comboDots[index].color = index < Mathf.CeilToInt(comboProgress * m_comboDots.Count)
                    ? new Color(1f, 0.8f, 0.4f) : new Color(0.2f, 0.3f, 0.36f);
            if (m_controller.IsInfinity && second > 0 && second <= 10 && second != m_lastTickSecond)
                m_audioSource.PlayOneShot(m_tickClip);
            m_lastTickSecond = second;
        }

        private void HandleReverse()
        {
            m_controller.Reverse();
            RefreshHUD();
        }

        private void HandleReset()
        {
            ShowDialog("重新开始本关？\n当前进度和分数将被清除。", "重新开始", ConfirmReset, true);
        }

        private void ConfirmReset()
        {
            m_controller.Reset();
        }

        private void HandleBack()
        {
            if (m_controller.IsInfinity)
                ShowDialog("结束无尽挑战并按当前得分结算？\n得分：" + m_controller.Score, "结算", SettleInfinity, true);
            else
                ShowDialog("返回关卡列表？\n本局进度不会保存。", "返回", RequestSelection, true);
        }

        private void HandleHelp()
        {
            ShowDialog("点击源瓶，再点击空瓶或顶层同色的瓶子。\n每瓶最多四层，只能倒出顶端连续同色水。\n"
                + "四层同色即完成；问号层随上方水倒出逐层揭晓。\n撤销也可中断正在进行的倒水。\n"
                + "无尽模式完成一瓶加时并换瓶，确认框打开时仍会计时。", "知道了", null, false);
        }

        private void HandleCompleted()
        {
            if (m_controller.IsInfinity)
            {
                SettleInfinity();
                return;
            }
            WaterSortProgress.RecordCompletion(m_controller.LevelID);
            if (m_controller.LevelID < WaterSortLevelCatalog.Count - 1)
                ShowDialog("关卡完成！\n得分：" + m_controller.Score, "下一关", ContinueAfterWin, false);
            else
                ShowDialog("关卡完成！\n得分：" + m_controller.Score, "返回列表", RequestSelection, false);
        }

        private void ContinueAfterWin()
        {
            EnterLevel(m_controller.LevelID + 1);
        }

        private void SettleInfinity()
        {
            m_controller.Stop();
            WaterSortProgress.RecordBestScore(m_controller.Score);
            int best = WaterSortProgress.BestScore;
            ShowDialog("挑战结束\n本次得分：" + m_controller.Score + "\n最高纪录：" + best,
                "返回列表", RequestSelection, false);
        }

        private void RequestSelection()
        {
            if (!m_navigating)
                ReturnToSelectionAsync().Forget();
        }

        private async UniTask ReturnToSelectionAsync()
        {
            // 保留确认框直到切页成功，目标加载失败时可以再次确认。
            int version = m_navigationVersion;
            m_navigating = true;
            try
            {
                await WaterSortNavigation.OpenSelectionAsync();
            }
            finally
            {
                if (m_navigationVersion == version)
                    m_navigating = false;
            }
        }

        private void ShowDialog(string message, string confirmText, UnityAction action, bool canCancel)
        {
            m_modalText.text = message;
            m_confirmLabel.text = confirmText;
            m_confirmAction = action;
            m_cancelButton.gameObject.SetActive(canCancel);
            RectTransform confirm = (RectTransform)m_confirmButton.transform;
            confirm.anchoredPosition = new Vector2(canCancel ? 230f : 0f, -190f);
            m_modal.gameObject.SetActive(true);
            m_modal.SetAsLastSibling();
        }

        private void HandleConfirm()
        {
            UnityAction action = m_confirmAction;
            if (action != RequestSelection)
                CloseModal();
            action?.Invoke();
        }

        private void CloseModal()
        {
            m_confirmAction = null;
            m_modal.gameObject.SetActive(false);
        }

        private void BuildTickAudio()
        {
            m_audioSource = gameObject.AddComponent<AudioSource>();
            m_audioSource.playOnAwake = false;
            m_audioSource.volume = 0.15f;
            float[] samples = new float[2205];
            for (int index = 0; index < samples.Length; index++)
                samples[index] = Mathf.Sin(index * 2f * Mathf.PI * 880f / 22050f)
                    * (1f - index / (float)samples.Length);
            m_tickClip = AudioClip.Create("WaterSortCountdown", samples.Length, 1, 22050, false);
            m_tickClip.SetData(samples, 0);
        }

        private RectTransform CreateRect(string objectName, Transform parent, Vector2 size, Vector2 position)
        {
            GameObject instance = new GameObject(objectName, typeof(RectTransform));
            RectTransform rect = instance.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        private Image CreateImage(string objectName, Transform parent, Color color)
        {
            RectTransform rect = CreateRect(objectName, parent, Vector2.zero, Vector2.zero);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Text CreateText(string content, Transform parent, Vector2 position, Vector2 size, int fontSize)
        {
            RectTransform rect = CreateRect("Label", parent, size, position);
            Text label = rect.gameObject.AddComponent<Text>();
            label.font = m_font;
            label.text = content;
            label.fontSize = fontSize;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(0.9f, 0.95f, 1f);
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            return label;
        }

        private Button CreateButton(string title, Transform parent, Vector2 position, Vector2 size)
        {
            Image image = CreateImage("Button", parent, new Color(0.15f, 0.29f, 0.38f));
            image.rectTransform.anchoredPosition = position;
            image.rectTransform.sizeDelta = size;
            image.raycastTarget = true;
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.7f, 0.95f, 1f);
            colors.disabledColor = new Color(0.32f, 0.36f, 0.4f);
            button.colors = colors;
            CreateText(title, image.transform, Vector2.zero, size - new Vector2(20f, 10f), 30);
            return button;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void OnDestroy()
        {
            CloseSession();
            if (m_tickClip != null)
                Destroy(m_tickClip);
            if (m_font != null)
                Destroy(m_font);
        }
    }
}
