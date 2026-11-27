using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Game.WaterSortPuzzle.Editor
{
    /// <summary>创建和编辑倒水游戏的独立关卡配置。</summary>
    internal sealed class WaterSortLevelEditorWindow : EditorWindow
    {
        private const string LEVELDIRECTORY = "Assets/Res/Config/WaterSortLevels"; // 关卡资源目录。
        private const int DEFAULTDURATION = 300; // 新关卡的累计耗时上限。
        private const int DEFAULTHINTSTEPS = 3; // 新关卡每次提示的默认步数。
        private const int MAXCOLORCOUNT = 7; // 普通关卡可用的颜色数量。

        [SerializeReference] private LevelDefinition m_level; // 当前编辑的关卡草稿。
        [SerializeField] private string m_loadedPath; // 当前草稿对应的原始文件路径。
        [SerializeField] private string m_loadedJson; // 加载时的文件内容，用于防止覆盖外部修改。
        [SerializeField] private int m_colorCount = 4; // 生成局面的颜色数量。
        private string[] m_levelPaths = Array.Empty<string>(); // 当前目录中可选的关卡文件。
        private Vector2 m_scroll; // 瓶子编辑区滚动位置。
        private readonly List<BottleData> m_previewLayout = new List<BottleData>(); // 与玩法共用的预览布局坐标。
        private string m_status; // 最近一次操作的反馈。
        private MessageType m_statusType; // 最近一次反馈的级别。
        private CancellationTokenSource m_search; // 当前后台求解，窗口关闭时取消。

        [MenuItem("★AlloyFramework★/倒水游戏/关卡编辑器")]
        private static void Open()
        {
            GetWindow<WaterSortLevelEditorWindow>("倒水关卡编辑器");
        }

        [OnOpenAsset]
        private static bool OpenLevelFile(int instanceID, int line)
        {
            string path = AssetDatabase.GetAssetPath(instanceID);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetDirectoryName(path)?.Replace('\\', '/'), LEVELDIRECTORY,
                    StringComparison.Ordinal))
                return false;

            var window = GetWindow<WaterSortLevelEditorWindow>("倒水关卡编辑器");
            if (window.m_loadedPath != path && window.CanReplaceDraft())
                window.LoadLevel(path);
            return true;
        }

        private void OnEnable()
        {
            saveChangesMessage = "当前倒水关卡有未保存的修改。";
            RefreshLevels();
            if (m_level == null && m_levelPaths.Length > 0)
                LoadLevel(m_levelPaths[0]);
        }

        /// <summary>保存当前草稿，并在保存成功后清除未保存状态。</summary>
        public override void SaveChanges()
        {
            if (TrySave())
                base.SaveChanges();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (m_level != null)
            {
                DrawPreview();
                m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
                EditorGUI.BeginChangeCheck();
                DrawLevel();
                EditorGUI.BeginChangeCheck();
                DrawBottles();
                if (EditorGUI.EndChangeCheck())
                    m_level.OptimalSteps = -1;
                if (EditorGUI.EndChangeCheck())
                {
                    hasUnsavedChanges = true;
                    Repaint();
                }
                EditorGUILayout.EndScrollView();
                DrawOptimalSteps();
            }

            if (!string.IsNullOrEmpty(m_status))
                EditorGUILayout.HelpBox(m_status, m_statusType);
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string[] options = new string[m_levelPaths.Length + 1];
                options[0] = "选择关卡文件";
                for (int index = 0; index < m_levelPaths.Length; index++)
                    options[index + 1] = Path.GetFileNameWithoutExtension(m_levelPaths[index]);

                int selectedIndex = Array.IndexOf(m_levelPaths, m_loadedPath) + 1;
                int nextIndex = EditorGUILayout.Popup(selectedIndex, options);
                if (nextIndex > 0 && nextIndex != selectedIndex)
                {
                    string nextPath = m_levelPaths[nextIndex - 1];
                    if (CanReplaceDraft())
                        LoadLevel(nextPath);
                }

                if (GUILayout.Button("刷新", GUILayout.Width(55)))
                    RefreshLevels();

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(m_loadedPath)))
                {
                    if (GUILayout.Button("重载", GUILayout.Width(55)) && CanReplaceDraft())
                        LoadLevel(m_loadedPath);
                }

                if (GUILayout.Button("新建", GUILayout.Width(55)) && CanReplaceDraft())
                    CreateLevel();

                using (new EditorGUI.DisabledScope(m_level == null))
                {
                    if (GUILayout.Button("保存", GUILayout.Width(55)))
                        SaveChanges();
                }
            }
        }

        private void DrawLevel()
        {
            EditorGUILayout.LabelField("关卡设置", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!string.IsNullOrEmpty(m_loadedPath)))
                m_level.ID = EditorGUILayout.IntField("关卡编号", m_level.ID);

            m_level.Name = EditorGUILayout.TextField("名称", m_level.Name);
            m_level.PrerequisiteID = EditorGUILayout.IntField("前置关卡编号", m_level.PrerequisiteID);
            m_level.ReverseCount = EditorGUILayout.IntField("撤销次数（-1 不限）", m_level.ReverseCount);
            m_level.HintSteps = EditorGUILayout.IntField("提示步数（-1 全部，0 禁用）", m_level.HintSteps);
            m_level.MaxDurationSeconds = EditorGUILayout.IntField("限时秒数", m_level.MaxDurationSeconds);
            EditorGUI.BeginChangeCheck();
            m_level.ReverseColor = EditorGUILayout.Toggle("反转颜色", m_level.ReverseColor);
            m_level.IsInfinity = EditorGUILayout.Toggle("无尽模式", m_level.IsInfinity);
            if (EditorGUI.EndChangeCheck())
                m_level.OptimalSteps = -1;

            if (m_level.IsInfinity)
                m_level.InfinityStartSeconds = EditorGUILayout.IntField("开局倒计时（秒）", m_level.InfinityStartSeconds);

            // 分类只影响选关入口，无尽关始终从挑战模式进入。
            using (new EditorGUI.DisabledScope(m_level.IsInfinity))
            {
                bool challenge = EditorGUILayout.Toggle("归入挑战模式", m_level.IsChallenge);
                if (!m_level.IsInfinity)
                    m_level.ChallengeMode = challenge;
            }

            using (new EditorGUI.DisabledScope(m_level.IsInfinity))
            using (new EditorGUILayout.HorizontalScope())
            {
                // 生成参数属于窗口，不属于关卡草稿。
                bool draftChanged = GUI.changed;
                m_colorCount = EditorGUILayout.IntSlider("生成颜色数", m_colorCount, 2, MAXCOLORCOUNT);
                GUI.changed = draftChanged;
                if (GUILayout.Button("生成局面", GUILayout.Width(85))
                    && EditorUtility.DisplayDialog("生成局面", "生成的瓶子将替换当前草稿的瓶子。", "生成", "取消"))
                    GenerateBottles();
            }
        }

        private void DrawOptimalSteps()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string steps = m_level.OptimalSteps >= 0 ? m_level.OptimalSteps.ToString() : "未计算";
                EditorGUILayout.LabelField("最优步数", m_level.IsInfinity ? "无尽模式不适用" : steps);
                using (new EditorGUI.DisabledScope(m_level.IsInfinity || m_search != null))
                {
                    if (GUILayout.Button("计算并保存最优步数", GUILayout.Width(150)))
                        CalculateAndSaveOptimalSteps();
                }
            }
        }

        private void OnDisable()
        {
            // 后台任务自行释放令牌；关闭或重开窗口后丢弃旧结果。
            m_search?.Cancel();
            m_search = null;
        }

        private async void CalculateAndSaveOptimalSteps()
        {
            LevelDefinition draft = m_level;
            using (var search = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                m_search = search;
                try
                {
                    // 在主线程验证草稿，求解器仅接收独立的瓶子快照。
                    string snapshot = JsonUtility.ToJson(draft);
                    List<BottleData> bottles = WaterSortLevelCatalog.Parse(snapshot).CreateBottles();
                    SetStatus("正在计算当前局面的最优步数…", MessageType.Info);
                    WaterSortSolution solution = await Task.Run(() => WaterSortSolver.Solve(bottles, search.Token));
                    if (m_search != search)
                        return;

                    search.Token.ThrowIfCancellationRequested();
                    if (m_level != draft || JsonUtility.ToJson(draft) != snapshot)
                    {
                        SetStatus("草稿已切换或修改，请重新计算。", MessageType.Info);
                        return;
                    }

                    if (solution.MinimumSteps < 0)
                    {
                        SetStatus("当前局面无解。", MessageType.Warning);
                        return;
                    }

                    // 复用现有保存入口，保留外部修改检测和原子替换。
                    draft.OptimalSteps = solution.MinimumSteps;
                    hasUnsavedChanges = true;
                    if (TrySave())
                        SetStatus($"已保存 {Path.GetFileName(m_loadedPath)}，最优步数为 {draft.OptimalSteps}。", MessageType.Info);
                }
                catch (OperationCanceledException)
                {
                    if (m_search == search)
                        SetStatus("求解超过 30 秒，未写入结果。", MessageType.Warning);
                }
                catch (Exception exception)
                {
                    if (m_search == search)
                        SetStatus($"求解失败：{exception.Message}", MessageType.Error);
                }
                finally
                {
                    if (m_search == search)
                    {
                        m_search = null;
                        Repaint();
                    }
                }
            }
        }

        private void DrawPreview()
        {
            int count = m_level.Bottles.Length;
            EditorGUILayout.LabelField(m_level.IsInfinity ? "水瓶预览 · 灰色水层在开局随机生成" : "水瓶预览",
                EditorStyles.boldLabel);
            float height = Mathf.Clamp(position.height * 0.30f, 100f, 300f);
            Rect area = GUILayoutUtility.GetRect(1f, height, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint)
                return;

            EditorGUI.DrawRect(area, new Color(0.12f, 0.14f, 0.17f));
            if (count == 0)
                return;

            // 排列只随瓶数变化，水层直接读取当前草稿，编辑时无需重新导入 JSON。
            if (m_previewLayout.Count != count)
            {
                m_previewLayout.Clear();
                for (int index = 0; index < count; index++)
                    m_previewLayout.Add(new BottleData());
                WaterSortLayout.Apply(m_previewLayout);
            }

            float scale = m_previewLayout[0].LocalScale;
            Vector2 size = new Vector2(132f, 380f) * scale;
            Vector2 first = m_previewLayout[0].LocalPos;
            Vector2 last = m_previewLayout[count - 1].LocalPos;
            // 标签占用行间的一小段空间，预览始终以第一行（最宽行）为宽度基准。
            float width = size.x + Mathf.Abs(first.x) * 2f;
            float boardHeight = size.y + first.y - last.y;
            float zoom = Mathf.Min((area.width - 16f) / width, (area.height - 28f) / boardHeight);
            if (zoom <= 0f)
                return;
            Vector2 center = area.center - Vector2.up * 7f;
            size *= zoom;
            for (int index = 0; index < count; index++)
            {
                Vector2 point = m_previewLayout[index].LocalPos;
                point = center + new Vector2(point.x, (first.y + last.y) * 0.5f - point.y) * zoom;
                DrawPreviewBottle(new Rect(point - size * 0.5f, size), m_level.Bottles[index], index);
            }
        }

        private void DrawPreviewBottle(Rect bounds, BottleDefinition bottle, int index)
        {
            float neckWidth = bounds.width * 0.55f;
            float neckHeight = bounds.height * 0.09f;
            EditorGUI.DrawRect(new Rect(bounds.center.x - neckWidth / 2f, bounds.y, neckWidth, neckHeight),
                Color.gray);
            Rect body = new Rect(bounds.x, bounds.y + neckHeight, bounds.width, bounds.height - neckHeight);
            EditorGUI.DrawRect(body, Color.gray);
            Rect inside = new Rect(body.x + 1, body.y + 1, Mathf.Max(1, body.width - 2), body.height - 2);
            EditorGUI.DrawRect(inside, new Color(0.20f, 0.23f, 0.27f));

            // 配置索引从瓶底递增，空层保留背景；问号只遮住配置中尚未揭晓的层。
            float layerHeight = inside.height * 0.94f / BottleData.CAPACITY;
            for (int layer = 0; layer < bottle.Colors.Length; layer++)
            {
                bool hidden = layer < bottle.InvisibleCount;
                Color color = WaterSortPalette.GetBody(bottle.Colors[layer]);
                if (hidden)
                    color = new Color(0.22f, 0.25f, 0.30f);
                else if (m_level.IsInfinity)
                    color = Color.gray;
                else if (color.a == 0f)
                    color = Color.magenta;
                Rect stripe = new Rect(inside.x, inside.yMax - (layer + 1) * layerHeight,
                    inside.width, Mathf.Max(1f, layerHeight - 0.5f));
                EditorGUI.DrawRect(stripe, color);
                if (hidden)
                    GUI.Label(stripe, "?", EditorStyles.centeredGreyMiniLabel);
            }

            GUI.Label(new Rect(bounds.center.x - 12f, bounds.yMax, 24f, 15f), (index + 1).ToString(),
                EditorStyles.centeredGreyMiniLabel);
        }

        private void DrawBottles()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("瓶子（颜色由底到顶）", EditorStyles.boldLabel);
            for (int index = 0; index < m_level.Bottles.Length; index++)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    BottleDefinition bottle = m_level.Bottles[index];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField($"瓶子 {index + 1}");
                        if (GUILayout.Button("删除", GUILayout.Width(55)))
                        {
                            RemoveBottle(index);
                            break;
                        }
                    }

                    int count = EditorGUILayout.IntSlider("水层数量", bottle.Colors.Length, 0, BottleData.CAPACITY);
                    if (count != bottle.Colors.Length)
                    {
                        // 保留未删除的水层，为添加的水层提供可直接使用的颜色。
                        int previousCount = bottle.Colors.Length;
                        Array.Resize(ref bottle.Colors, count);
                        for (int layer = previousCount; layer < count; layer++)
                            bottle.Colors[layer] = m_level.IsInfinity ? 0 : 1;

                        bottle.InvisibleCount = Math.Min(bottle.InvisibleCount, Math.Max(0, count - 1));
                    }

                    for (int layer = 0; layer < count; layer++)
                        bottle.Colors[layer] = EditorGUILayout.IntField($"颜色 {layer + 1}", bottle.Colors[layer]);

                    bottle.InvisibleCount = EditorGUILayout.IntSlider(
                        "隐藏层数", bottle.InvisibleCount, 0, Math.Max(0, count - 1));
                }
            }

            if (GUILayout.Button("添加空瓶"))
            {
                int index = m_level.Bottles.Length;
                Array.Resize(ref m_level.Bottles, index + 1);
                m_level.Bottles[index] = new BottleDefinition { Colors = Array.Empty<int>() };
                m_level.OptimalSteps = -1;
                hasUnsavedChanges = true;
            }
        }

        private void RemoveBottle(int index)
        {
            BottleDefinition[] bottles = m_level.Bottles;
            Array.Copy(bottles, index + 1, bottles, index, bottles.Length - index - 1);
            Array.Resize(ref m_level.Bottles, bottles.Length - 1);
            m_level.OptimalSteps = -1;
            hasUnsavedChanges = true;
        }

        private void RefreshLevels()
        {
            string directory = GetAbsolutePath(LEVELDIRECTORY);
            m_levelPaths = Directory.Exists(directory)
                ? Directory.GetFiles(directory, "Level*.json") : Array.Empty<string>();

            for (int index = 0; index < m_levelPaths.Length; index++)
                m_levelPaths[index] = $"{LEVELDIRECTORY}/{Path.GetFileName(m_levelPaths[index])}";
            Array.Sort(m_levelPaths, StringComparer.Ordinal);
        }

        private bool CanReplaceDraft()
        {
            if (!hasUnsavedChanges)
                return true;

            int choice = EditorUtility.DisplayDialogComplex("未保存的关卡", saveChangesMessage, "保存", "取消", "放弃");
            return choice == 2 || (choice == 0 && TrySave());
        }

        private void CreateLevel()
        {
            int nextID = GetNextAvailableID();
            m_level = new LevelDefinition
            {
                ID = nextID,
                Name = nextID.ToString("D2"),
                ReverseCount = GameDefine.DEFAULTREVERSECOUNT,
                HintSteps = DEFAULTHINTSTEPS,
                MaxDurationSeconds = DEFAULTDURATION
            };
            m_loadedPath = null;
            m_loadedJson = null;
            GenerateBottles();
        }

        private void LoadLevel(string path)
        {
            try
            {
                // 读取与验证都成功后再替换当前草稿。
                string json = File.ReadAllText(GetAbsolutePath(path));
                LevelDefinition level = WaterSortLevelCatalog.Parse(json);
                if (!string.Equals(path, GetLevelPath(level.ID), StringComparison.Ordinal))
                    throw new InvalidDataException("关卡编号必须与文件名一致。");

                m_level = level;
                m_loadedPath = path;
                m_loadedJson = json;
                hasUnsavedChanges = false;
                m_status = null;
            }
            catch (Exception exception)
            {
                SetStatus($"读取失败：{exception.Message}", MessageType.Error);
            }
        }

        private bool TrySave()
        {
            if (m_level == null)
                return false;

            try
            {
                // 使用运行时入口验证即将写入的同一份 JSON。
                string json = JsonUtility.ToJson(m_level, true) + "\n";
                WaterSortLevelCatalog.Parse(json);
                string path = GetLevelPath(m_level.ID);
                string absolutePath = GetAbsolutePath(path);
                bool alreadyLoaded = string.Equals(path, m_loadedPath, StringComparison.Ordinal);
                if (alreadyLoaded && (!File.Exists(absolutePath) || File.ReadAllText(absolutePath) != m_loadedJson))
                    throw new IOException("文件已被外部修改，请重新打开后再编辑。");
                if (!alreadyLoaded && File.Exists(absolutePath))
                    throw new IOException("该编号已有文件，请打开已有文件或更换当前草稿的编号。");

                // 运行时按连续编号加载，新增配置须补齐目录中的首个空缺。
                if (!alreadyLoaded)
                {
                    int nextID = GetNextAvailableID();
                    if (m_level.ID != nextID)
                        throw new InvalidDataException($"新关卡编号应为 {nextID}。");
                }

                // 写完临时文件后再替换目标，避免写入失败破坏已有配置。
                Directory.CreateDirectory(GetAbsolutePath(LEVELDIRECTORY));
                string temporaryPath = $"{absolutePath}.{Guid.NewGuid():N}.tmp";
                try
                {
                    File.WriteAllText(temporaryPath, json);
                    if (alreadyLoaded)
                        File.Replace(temporaryPath, absolutePath, null);
                    else
                        File.Move(temporaryPath, absolutePath);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }

                m_loadedPath = path;
                m_loadedJson = json;
                hasUnsavedChanges = false;
                AssetDatabase.ImportAsset(path);
                RefreshLevels();
                SetStatus($"已保存 {Path.GetFileName(path)}", MessageType.Info);
                return true;
            }
            catch (Exception exception)
            {
                SetStatus($"保存失败：{exception.Message}", MessageType.Error);
                return false;
            }
        }

        private void GenerateBottles()
        {
            int[] colors = new int[m_colorCount];
            for (int index = 0; index < colors.Length; index++)
                colors[index] = index + 1;

            for (int index = colors.Length - 1; index > 0; index--)
            {
                int otherIndex = UnityEngine.Random.Range(0, index + 1);
                int color = colors[index];
                colors[index] = colors[otherIndex];
                colors[otherIndex] = color;
            }

            // 路径可逆：借助空瓶取出第一个顶层，再按环顺序归还每个顶层。
            BottleDefinition[] bottles = new BottleDefinition[colors.Length + 1];
            for (int index = 0; index < colors.Length; index++)
            {
                int[] layers = new int[BottleData.CAPACITY];
                for (int layer = 0; layer < layers.Length - 1; layer++)
                    layers[layer] = colors[index];

                layers[layers.Length - 1] = colors[(index + 1) % colors.Length];
                bottles[index] = new BottleDefinition { Colors = layers };
            }

            bottles[bottles.Length - 1] = new BottleDefinition { Colors = Array.Empty<int>() };
            m_level.Bottles = bottles;
            m_level.OptimalSteps = -1;
            hasUnsavedChanges = true;
            m_status = null;
        }

        private static string GetLevelPath(int id)
        {
            return $"{LEVELDIRECTORY}/Level{id:D3}.json";
        }

        private static int GetNextAvailableID()
        {
            int id = 1;
            while (File.Exists(GetAbsolutePath(GetLevelPath(id))))
                id++;
            return id;
        }

        private static string GetAbsolutePath(string assetPath)
        {
            return Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
        }

        private void SetStatus(string status, MessageType type)
        {
            m_status = status;
            m_statusType = type;
        }
    }
}
