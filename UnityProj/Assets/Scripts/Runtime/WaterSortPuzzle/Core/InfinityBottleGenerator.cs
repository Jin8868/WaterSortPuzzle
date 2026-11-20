using System;
using System.Collections.Generic;

namespace Game.WaterSortPuzzle
{
    public sealed class InfinityBottleGenerator
    {
        private readonly Random m_random = new Random(); // 独立随机源。

        internal void FillInfinityTemplate(IReadOnlyList<BottleData> bottles)
        {
            List<BottleData> filledBottles = new List<BottleData>();
            for (int index = 0; index < bottles.Count; index++)
            {
                if (!bottles[index].IsEmpty())
                    filledBottles.Add(bottles[index]);
            }
            Shuffle(filledBottles);

            // 模板有三个空瓶；每组三瓶只含三种颜色，整理完会腾出三个空瓶给下一组。
            List<int> group = new List<int>(3 * BottleData.CAPACITY);
            List<int> palette = new List<int> { 1, 2, 3, 4, 5, 6, 7 };
            for (int index = 0; index < filledBottles.Count; index += 3)
            {
                group.Clear();
                Shuffle(palette);
                for (int colorIndex = 0; colorIndex < 3; colorIndex++)
                {
                    for (int layer = 0; layer < BottleData.CAPACITY; layer++)
                        group.Add(palette[colorIndex]);
                }
                do
                {
                    Shuffle(group);
                    for (int bottleIndex = 0; bottleIndex < 3; bottleIndex++)
                    {
                        BottleData bottle = filledBottles[index + bottleIndex];
                        for (int layer = 0; layer < BottleData.CAPACITY; layer++)
                            bottle.Colors[layer] = group[bottleIndex * BottleData.CAPACITY + layer];
                    }
                }
                while (filledBottles[index].IsFinish()
                    || filledBottles[index + 1].IsFinish()
                    || filledBottles[index + 2].IsFinish());
            }
        }

        /// <summary>补齐模板中的占位层，保持颜色组数。</summary>
        /// <param name="bottles">无尽开局模板。</param>
        public void Fill(IReadOnlyList<BottleData> bottles)
        {
            int[] counts = CountColors(bottles, false);
            List<int> colors = new List<int>();
            List<int> existingColors = new List<int>();
            for (int color = 1; color <= 7; color++)
            {
                if (counts[color] > 0)
                    existingColors.Add(color);
            }
            int zeroCount = 0;
            for (int index = 0; index < bottles.Count; index++)
            {
                for (int layer = 0; layer < bottles[index].Colors.Count; layer++)
                {
                    if (bottles[index].Colors[layer] == 0)
                        zeroCount++;
                }
            }

            // 先补余数缺口，再补完整四层组，避免制造孤立颜色。
            for (int missing = 1; missing < BottleData.CAPACITY; missing++)
            {
                for (int color = 1; color <= 7; color++)
                {
                    if ((BottleData.CAPACITY - counts[color] % BottleData.CAPACITY) % BottleData.CAPACITY != missing)
                        continue;
                    for (int layer = 0; layer < missing && colors.Count < zeroCount; layer++)
                        colors.Add(color);
                }
            }
            while (colors.Count < zeroCount)
            {
                int color = existingColors.Count > 0
                    ? existingColors[m_random.Next(existingColors.Count)] : m_random.Next(1, 8);
                for (int layer = 0; layer < BottleData.CAPACITY && colors.Count < zeroCount; layer++)
                    colors.Add(color);
            }
            Shuffle(colors);
            int position = 0;
            for (int index = 0; index < bottles.Count; index++)
            {
                for (int layer = 0; layer < bottles[index].Colors.Count; layer++)
                {
                    if (bottles[index].Colors[layer] == 0)
                        bottles[index].Colors[layer] = colors[position++];
                }
            }

            // 优先以跨瓶交换消除完成瓶，保留每种颜色的层数。
            for (int index = 0; index < bottles.Count; index++)
            {
                BottleData bottle = bottles[index];
                if (!bottle.IsFinish())
                    continue;
                bool exchanged = false;
                for (int otherIndex = 0; otherIndex < bottles.Count && !exchanged; otherIndex++)
                {
                    BottleData other = bottles[otherIndex];
                    if (other == bottle)
                        continue;
                    for (int layer = 0; layer < other.Colors.Count; layer++)
                    {
                        if (other.Colors[layer] == bottle.TopColor
                            || WouldCompleteAfterSwap(other, layer, bottle.TopColor))
                            continue;
                        int savedColor = bottle.Colors[0];
                        bottle.Colors[0] = other.Colors[layer];
                        other.Colors[layer] = savedColor;
                        exchanged = true;
                        break;
                    }
                }
                if (!exchanged)
                    bottle.Colors[0] = bottle.TopColor % 7 + 1;
            }
        }

        /// <summary>按场上孤儿权重生成替换瓶。</summary>
        /// <param name="bottles">当前场上的瓶子。</param>
        /// <param name="score">当前分数，用于隐藏层概率。</param>
        /// <param name="activeCommands">活动命令；接水后未提交的源层会从统计中扣除。</param>
        /// <returns>四层非完成状态的新瓶。</returns>
        public BottleData CreateReplacement(
            IReadOnlyList<BottleData> bottles, int score, IEnumerable<PourCommand> activeCommands = null)
        {
            int[] counts = CountColors(bottles, true);
            if (activeCommands != null)
            {
                // 以接水为搬移时点，扣除仍留在源里的副本。若接水瓶已满，同样排除已搬入的层。
                foreach (PourCommand command in activeCommands)
                {
                    if (command.State == EPourCommandState.Received)
                        counts[command.Color] -= command.ColorCount;
                }
            }
            int[] weights = new int[8];
            for (int color = 1; color <= 7; color++)
                weights[color] = Math.Max(2, counts[color] % BottleData.CAPACITY);
            BottleData result = new BottleData();
            for (int layer = 0; layer < BottleData.CAPACITY; layer++)
            {
                int total = 0;
                for (int color = 1; color <= 7; color++)
                    total += weights[color];
                int draw = m_random.Next(total);
                for (int color = 1; color <= 7; color++)
                {
                    draw -= weights[color];
                    if (draw < 0)
                    {
                        result.Colors.Add(color);
                        weights[color]--;
                        break;
                    }
                }
            }
            if (result.IsFinish())
                result.Colors[0] = result.TopColor % 7 + 1;
            int hiddenProbability = Math.Min(WaterSortSettings.MAXHIDDENPROBABILITY,
                score / WaterSortSettings.HIDDENSCORESTEP * WaterSortSettings.HIDDENPROBABILITYSTEP);
            result.InvisibleCount = m_random.Next(100) < hiddenProbability ? 3 : 0;
            return result;
        }

        private void Shuffle<T>(List<T> items)
        {
            for (int index = items.Count - 1; index > 0; index--)
            {
                int otherIndex = m_random.Next(index + 1);
                T savedItem = items[index];
                items[index] = items[otherIndex];
                items[otherIndex] = savedItem;
            }
        }

        private static bool WouldCompleteAfterSwap(BottleData bottle, int swappedLayer, int color)
        {
            if (bottle.Colors.Count != BottleData.CAPACITY || bottle.InvisibleCount != 0)
                return false;
            for (int layer = 0; layer < bottle.Colors.Count; layer++)
            {
                if (layer != swappedLayer && bottle.Colors[layer] != color)
                    return false;
            }
            return true;
        }
        private static int[] CountColors(IReadOnlyList<BottleData> bottles, bool excludeCompleted)
        {
            int[] counts = new int[8];
            for (int index = 0; index < bottles.Count; index++)
            {
                BottleData bottle = bottles[index];
                if (excludeCompleted && bottle.IsFinish())
                    continue;
                for (int layer = 0; layer < bottle.Colors.Count; layer++)
                {
                    int color = bottle.Colors[layer];
                    if (color >= 1 && color <= 7)
                        counts[color]++;
                }
            }
            return counts;
        }
    }
}
