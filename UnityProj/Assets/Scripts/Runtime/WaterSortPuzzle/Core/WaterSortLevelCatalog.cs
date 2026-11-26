using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using AlloyFramework;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace Game.WaterSortPuzzle
{
    [Serializable]
    public sealed class BottleDefinition
    {
        /// <summary>从瓶底到瓶口排列的颜色编号。</summary>
        public int[] Colors;
        /// <summary>从瓶底开始隐藏的层数。</summary>
        public int InvisibleCount;
    }

    [Serializable]
    public sealed class LevelDefinition
    {
        /// <summary>从一开始的关卡编号。</summary>
        public int ID;
        /// <summary>在选关和游戏界面显示的名称。</summary>
        public string Name;
        /// <summary>解锁所需的已完成关卡编号；零表示直接解锁。</summary>
        public int PrerequisiteID;
        /// <summary>每局的撤销次数；负一表示不限次数。</summary>
        public int ReverseCount = GameDefine.DEFAULTREVERSECOUNT;
        /// <summary>关卡时限（秒）。</summary>
        public int MaxDurationSeconds;
        /// <summary>每次提示最多引导的步数，-1 表示全部，0 表示禁用。</summary>
        public int HintSteps = 3;
        /// <summary>初始局面的最少倒水步数，-1 表示尚未配置或无尽模式。</summary>
        public int OptimalSteps = -1;
        /// <summary>是否应用反色规则。</summary>
        public bool ReverseColor;
        /// <summary>是否将有限关卡列入挑战模式。</summary>
        public bool ChallengeMode;
        /// <summary>是否使用无尽模式规则。</summary>
        public bool IsInfinity;
        /// <summary>按游戏布局顺序排列的初始瓶子。</summary>
        public BottleDefinition[] Bottles;

        /// <summary>特殊配置关和无尽关归入选关页的挑战模式。</summary>
        public bool IsChallenge => ChallengeMode || IsInfinity;

        /// <summary>从关卡配置创建独立的瓶子数据。</summary>
        /// <returns>按配置顺序排列的瓶子。</returns>
        public List<BottleData> CreateBottles()
        {
            var bottles = new List<BottleData>(Bottles.Length);
            foreach (BottleDefinition definition in Bottles)
            {
                var bottle = new BottleData { InvisibleCount = definition.InvisibleCount };
                bottle.Colors.AddRange(definition.Colors);
                bottles.Add(bottle);
            }

            return bottles;
        }
    }

    public static class WaterSortLevelCatalog
    {
        private const string LEVELDIRECTORY = "Assets/Res/Config/WaterSortLevels/"; // 包清单中的关卡资源目录。
        private const int MAXCOLORID = 7; // 调色板中有效的最大颜色编号。
        private const int INFINITYGROUPSIZE = 3; // 无尽模式每组参与生成的瓶子数。

        private static LevelDefinition[] m_levels = Array.Empty<LevelDefinition>(); // 已验证的关卡目录。

        public static int Count => m_levels.Length;

        /// <summary>从默认资源包加载整个关卡目录。</summary>
        /// <param name="cancellationToken">用于取消加载的令牌。</param>
        /// <returns>关卡目录就绪的任务。</returns>
        /// <exception cref="InvalidOperationException">默认资源包尚未初始化。</exception>
        /// <exception cref="InvalidDataException">没有找到关卡，或者其中一份配置无效。</exception>
        public static async UniTask LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string packageName = ResourceSettings.DefaultPackageName;
            ResourcePackage package = YooAssets.TryGetPackage(packageName);
            if (package == null)
            {
                throw new InvalidOperationException($"关卡资源包尚未初始化：{packageName}。");
            }

            // 从包清单发现关卡资源，新增文件随包发布后即可被加载。
            var levels = new List<LevelDefinition>();
            foreach (AssetInfo asset in package.GetAllAssetInfos())
            {
                if (!asset.AssetPath.StartsWith(LEVELDIRECTORY, StringComparison.Ordinal)
                    || !asset.AssetPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using (IAssetHandle<TextAsset> handle = await ResourceManager.Instance.LoadAssetAsync<TextAsset>(
                    asset.Address, packageName, cancellationToken: cancellationToken))
                {
                    try
                    {
                        levels.Add(Parse(handle.Asset.text));
                    }
                    catch (InvalidDataException exception)
                    {
                        throw new InvalidDataException($"关卡文件无效：{asset.AssetPath}。{exception.Message}", exception);
                    }
                }
            }

            // 按编号确认目录完整，全部验证成功后再替换运行时目录。
            if (levels.Count == 0)
            {
                throw new InvalidDataException($"关卡目录中没有 JSON 资源：{LEVELDIRECTORY}。");
            }

            levels.Sort(CompareLevels);
            for (int index = 0; index < levels.Count; index++)
            {
                if (levels[index].ID != index + 1)
                {
                    throw new InvalidDataException($"关卡编号须从 1 起连续且唯一，预期 {index + 1}。");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            m_levels = levels.ToArray();
        }

        /// <summary>清空已加载的关卡目录。</summary>
        public static void Unload()
        {
            m_levels = Array.Empty<LevelDefinition>();
        }

        /// <summary>读取并验证单个关卡的 JSON 配置。</summary>
        /// <param name="json">关卡文件的 JSON 文本。</param>
        /// <returns>通过配置校验的关卡。</returns>
        /// <exception cref="InvalidDataException">JSON 格式或关卡内容无效。</exception>
        public static LevelDefinition Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidDataException("关卡 JSON 为空。");
            }

            var level = new LevelDefinition();
            try
            {
                JsonUtility.FromJsonOverwrite(json, level);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("关卡 JSON 格式无效。", exception);
            }

            // 前置关只能指向更早的关卡，随目录校验排除缺失引用和循环解锁。
            if (level.ID <= 0 || string.IsNullOrWhiteSpace(level.Name)
                || level.PrerequisiteID < 0 || level.PrerequisiteID >= level.ID
                || level.ReverseCount < -1 || level.MaxDurationSeconds <= 0 || level.HintSteps < -1
                || level.OptimalSteps < -1 || level.Bottles == null || level.Bottles.Length < 2)
            {
                throw new InvalidDataException("关卡编号、名称、前置关卡、撤销次数、限时、提示步数或瓶子配置无效。");
            }

            ValidateBottles(level);
            return level;
        }

        /// <summary>获取指定编号的关卡配置。</summary>
        /// <param name="levelID">从一开始的关卡编号。</param>
        /// <returns>对应关卡配置。</returns>
        /// <exception cref="ArgumentOutOfRangeException">关卡编号超出范围。</exception>
        public static LevelDefinition Get(int levelID)
        {
            if (levelID < 1 || levelID > m_levels.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(levelID));
            }

            return m_levels[levelID - 1];
        }

        private static void ValidateBottles(LevelDefinition level)
        {
            int emptyCount = 0;
            int filledCount = 0;
            for (int index = 0; index < level.Bottles.Length; index++)
            {
                BottleDefinition bottle = level.Bottles[index];
                if (bottle?.Colors == null || bottle.Colors.Length > BottleData.CAPACITY
                    || bottle.InvisibleCount < 0
                    || bottle.InvisibleCount > Math.Max(0, bottle.Colors.Length - 1))
                {
                    throw new InvalidDataException($"第 {index + 1} 个瓶子的层数或隐藏层数无效。");
                }

                if (bottle.Colors.Length == 0)
                {
                    emptyCount++;
                    continue;
                }

                filledCount++;
                if (level.IsInfinity && bottle.Colors.Length != BottleData.CAPACITY)
                {
                    throw new InvalidDataException($"无尽模式第 {index + 1} 个非空瓶必须达到容量。");
                }

                foreach (int color in bottle.Colors)
                {
                    if (color < (level.IsInfinity ? 0 : 1) || color > MAXCOLORID)
                    {
                        throw new InvalidDataException($"第 {index + 1} 个瓶子的颜色编号 {color} 无效。");
                    }
                }
            }

            // 无尽生成器每组三瓶填充，留出至少三个空瓶来整理一组。
            if (filledCount == 0
                || (level.IsInfinity && (emptyCount < INFINITYGROUPSIZE || filledCount % INFINITYGROUPSIZE != 0)))
            {
                throw new InvalidDataException("关卡的非空瓶数量或无尽模板的分组无效。");
            }
        }

        private static int CompareLevels(LevelDefinition left, LevelDefinition right)
        {
            return left.ID.CompareTo(right.ID);
        }
    }
}
