using System;
using System.Collections.Generic;

namespace Game.WaterSortPuzzle
{
    public sealed class LevelDefinition
    {
        private readonly string m_configuration; // 原始瓶子配置。

        public int ID { get; }
        public string Name { get; }
        public int PrerequisiteID { get; }
        public int ReverseCount { get; }
        public bool ReverseColor => ID == 13;
        public bool IsInfinity => ID == 14;

        internal LevelDefinition(
            int id, string name, string configuration, int prerequisiteID,
            int reverseCount = GameDefine.DEFAULTREVERSECOUNT)
        {
            ID = id;
            Name = name;
            m_configuration = configuration;
            ReverseCount = reverseCount;
            PrerequisiteID = prerequisiteID;
        }

        /// <summary>从关卡配置创建独立的瓶子数据。</summary>
        /// <returns>按配置顺序排列的瓶子。</returns>
        public List<BottleData> CreateBottles()
        {
            List<BottleData> bottles = new List<BottleData>();
            string[] definitions = m_configuration.Split('|');
            for (int index = 0; index < definitions.Length; index++)
            {
                string definition = definitions[index];
                BottleData bottle = new BottleData();
                int start = definition.IndexOf('[') + 1;
                string content = definition.Substring(start, definition.IndexOf(']') - start);
                if (content.Length > 0)
                {
                    string[] colors = content.Split(',');
                    for (int layer = 0; layer < colors.Length; layer++)
                        bottle.Colors.Add(int.Parse(colors[layer]));
                }
                bottle.InvisibleCount = definition[0] == '1' ? Math.Max(0, bottle.Colors.Count - 1) : 0;
                bottles.Add(bottle);
            }
            return bottles;
        }
    }

    public static class WaterSortLevelCatalog
    {
        private static readonly LevelDefinition[] m_levels = CreateLevels(); // 原项目十四条关卡配置。

        public static int Count => m_levels.Length;

        /// <summary>获取指定编号的关卡配置。</summary>
        /// <param name="levelID">从一开始的关卡编号。</param>
        /// <returns>对应关卡配置。</returns>
        /// <exception cref="ArgumentOutOfRangeException">关卡编号超出范围。</exception>
        public static LevelDefinition Get(int levelID)
        {
            if (levelID < 1 || levelID > m_levels.Length)
                throw new ArgumentOutOfRangeException(nameof(levelID));
            return m_levels[levelID - 1];
        }

        private static LevelDefinition[] CreateLevels()
        {
            return new LevelDefinition[]
            {
                new LevelDefinition(1, "01",
                    "0:[1,2,1,2]|" +
                    "0:[2,1,2,1]|" +
                    "0:[]",
                    prerequisiteID: 0),
                new LevelDefinition(2, "02",
                    "0:[1,2,3,1]|" +
                    "0:[2,3,1,2]|" +
                    "0:[3,1,2,3]|" +
                    "0:[]",
                    prerequisiteID: 1),
                new LevelDefinition(3, "03",
                    "0:[1,2,1,3]|" +
                    "0:[2,3,2,4]|" +
                    "0:[3,4,3,1]|" +
                    "0:[4,1,4,2]|" +
                    "0:[]",
                    prerequisiteID: 2),
                new LevelDefinition(4, "04",
                    "0:[1,2,3,4]|" +
                    "0:[1,2,4,3]|" +
                    "0:[2,1,3,4]|" +
                    "0:[2,1,4,3]|" +
                    "0:[]|" +
                    "0:[]",
                    prerequisiteID: 3),
                new LevelDefinition(5, "05",
                    "0:[7,1,1,6]|" +
                    "0:[1,7,3,2]|" +
                    "0:[7,6,3,2]|" +
                    "0:[3,6,3,6]|" +
                    "0:[2,2,7,1]|" +
                    "0:[]|" +
                    "0:[]",
                    prerequisiteID: 4),
                new LevelDefinition(6, "06",
                    "0:[7,6,1,6]|" +
                    "0:[6,7,1,1]|" +
                    "0:[1,7,3,3]|" +
                    "0:[1,3,3,2]|" +
                    "0:[1,2,1,6]|" +
                    "1:[2,7,2,1]|" +
                    "0:[]|" +
                    "0:[]",
                    prerequisiteID: 5, reverseCount: 5),
                new LevelDefinition(7, "07",
                    "0:[6,4,3,6]|" +
                    "0:[1,5,2,7]|" +
                    "0:[2,7,4,1]|" +
                    "0:[7,6,3,3]|" +
                    "0:[5,1,1,1]|" +
                    "0:[2,4,5,7]|" +
                    "0:[6,4,3,2]|" +
                    "1:[5,1,1,1]|" +
                    "0:[]|" +
                    "0:[]",
                    prerequisiteID: 6, reverseCount: 5),
                new LevelDefinition(8, "08",
                    "0:[1,2,1,3]|" +
                    "0:[2,1,2,4]|" +
                    "0:[3,4,3,1]|" +
                    "0:[4,3,4,2]|" +
                    "0:[1,3,2,4]|" +
                    "0:[2,4,1,3]|" +
                    "0:[3,1,4,2]|" +
                    "1:[4,2,3,1]|" +
                    "0:[]",
                    prerequisiteID: 7, reverseCount: 5),
                new LevelDefinition(9, "09",
                    "0:[1,2,3,4]|" +
                    "0:[1,2,4,3]|" +
                    "0:[1,3,2,4]|" +
                    "0:[1,3,4,2]|" +
                    "0:[2,1,3,4]|" +
                    "0:[2,1,4,3]|" +
                    "0:[2,3,1,4]|" +
                    "1:[2,3,4,1]|" +
                    "0:[]|" +
                    "0:[]",
                    prerequisiteID: 8, reverseCount: 5),
                new LevelDefinition(10, "10",
                    "0:[1,2,3,5]|" +
                    "0:[1,2,4,3]|" +
                    "0:[1,3,4,5]|" +
                    "0:[1,4,2,5]|" +
                    "0:[2,1,5,3]|" +
                    "0:[2,3,1,4]|" +
                    "0:[2,4,5,1]|" +
                    "1:[3,2,4,5]|" +
                    "1:[3,5,1,2]|" +
                    "1:[4,3,5,4]|" +
                    "0:[]|" +
                    "0:[]",
                    prerequisiteID: 9, reverseCount: 8),
                new LevelDefinition(11, "11",
                    "0:[1,2,3,4]|" +
                    "0:[1,2,5,6]|" +
                    "0:[1,3,5,7]|" +
                    "0:[1,4,6,7]|" +
                    "0:[2,3,6,7]|" +
                    "0:[2,4,5,7]|" +
                    "0:[2,5,6,3]|" +
                    "0:[3,4,5,6]|" +
                    "0:[3,7,2,4]|" +
                    "0:[4,1,7,5]|" +
                    "1:[5,1,6,2]|" +
                    "1:[6,3,1,7]|" +
                    "1:[7,4,2,6]|" +
                    "1:[4,5,3,1]|" +
                    "0:[]|" +
                    "0:[]",
                    prerequisiteID: 10, reverseCount: 8),
                new LevelDefinition(12, "12",
                    "0:[1,2,3,4]|" +
                    "0:[1,2,5,6]|" +
                    "0:[1,3,5,7]|" +
                    "0:[1,4,6,2]|" +
                    "0:[1,2,6,3]|" +
                    "0:[1,5,4,7]|" +
                    "0:[2,1,3,5]|" +
                    "0:[2,3,6,2]|" +
                    "0:[2,4,5,1]|" +
                    "0:[2,6,7,3]|" +
                    "1:[3,1,6,5]|" +
                    "1:[1,2,4,6]|" +
                    "1:[4,1,2,5]|" +
                    "1:[4,3,1,6]|" +
                    "1:[5,2,7,4]|" +
                    "0:[]|" +
                    "0:[]",
                    prerequisiteID: 11, reverseCount: 8),
                new LevelDefinition(13, "99",
                    "0:[1,1,2,3]|" +
                    "0:[1,1,4,5]|" +
                    "0:[2,2,3,4]|" +
                    "0:[2,2,5,6]|" +
                    "0:[3,3,4,6]|" +
                    "0:[3,3,5,7]|" +
                    "0:[4,4,1,7]|" +
                    "0:[5,5,1,6]|" +
                    "0:[6,6,2,7]|" +
                    "0:[7,7,2,3]|" +
                    "0:[1,2,4,7]|" +
                    "0:[1,3,5,6]|" +
                    "0:[1,4,6,7]|" +
                    "0:[1,4,5,7]|" +
                    "0:[1,1,5,6]|" +
                    "0:[]",
                    prerequisiteID: 12, reverseCount: 10),
                new LevelDefinition(14, "无尽", CreateInfinityTemplate(), prerequisiteID: 12, reverseCount: -1)
            };
        }

        private static string CreateInfinityTemplate()
        {
            string[] definitions = new string[33];
            for (int index = 0; index < definitions.Length; index++)
                definitions[index] = index >= 15 && index < 18 ? "0:[]" : "0:[0,0,0,0]";
            return string.Join("|", definitions);
        }
    }
}
