using System;
using System.Collections.Generic;
using System.Threading;

namespace Game.WaterSortPuzzle
{
    /// <summary>最短解中的一次倒水。</summary>
    public readonly struct WaterSortStep
    {
        internal WaterSortStep(int sourceIndex, int targetIndex)
        {
            SourceIndex = sourceIndex;
            TargetIndex = targetIndex;
        }

        public int SourceIndex { get; }
        public int TargetIndex { get; }
    }

    /// <summary>从指定瓶子顺序出发的求解结果。</summary>
    public sealed class WaterSortSolution
    {
        internal WaterSortSolution(int minimumSteps, WaterSortStep[] steps)
        {
            MinimumSteps = minimumSteps;
            Steps = Array.AsReadOnly(steps);
        }

        public int MinimumSteps { get; }
        public IReadOnlyList<WaterSortStep> Steps { get; }
    }

    /// <summary>在稳定的有限关卡局面上查找最少倒水次数。</summary>
    public static class WaterSortSolver
    {
        private const int COLORBITS = 15; // 每层用于保存非零颜色编号的位数。
        private const ulong COLORMASK = (1UL << COLORBITS) - 1; // 单层颜色掩码。
        private const int HIDDENSHIFT = BottleData.CAPACITY * COLORBITS; // 隐藏层数所在位。
        private const int CACHESIZE = 100000; // 超过缓存容量时，改用有界内存的精确搜索。
        private static readonly WaterSortSolution m_unsolvable =
            new WaterSortSolution(-1, Array.Empty<WaterSortStep>()); // 无解时没有可执行步骤。

        /// <summary>按顺序执行合法倒水，求出当前局面的最短解。</summary>
        /// <param name="bottles">按实际瓶子顺序排列的稳定局面快照。</param>
        /// <param name="cancellationToken">用于取消本次搜索的令牌。</param>
        /// <returns>最少倒水次数和对应的零基瓶子索引。无解时次数为 -1。</returns>
        /// <exception cref="ArgumentNullException">瓶子列表为空引用。</exception>
        /// <exception cref="ArgumentException">瓶子快照包含无效的层数、颜色或预占。</exception>
        /// <exception cref="OperationCanceledException">本次求解已取消。</exception>
        public static WaterSortSolution Solve(
            IReadOnlyList<BottleData> bottles,
            CancellationToken cancellationToken = default)
        {
            if (bottles == null)
                throw new ArgumentNullException(nameof(bottles));

            cancellationToken.ThrowIfCancellationRequested();
            var colors = new Dictionary<int, int>();
            var totals = new List<int> { 0 };
            var initial = new ulong[bottles.Count];
            int layerCount = 0;
            for (int index = 0; index < bottles.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BottleData bottle = bottles[index];
                if (bottle == null || bottle.Colors.Count > BottleData.CAPACITY || bottle.LockCount != 0
                    || bottle.InvisibleCount < 0 || bottle.InvisibleCount > Math.Max(0, bottle.Colors.Count - 1))
                    throw new ArgumentException("求解需要稳定的瓶子数据。", nameof(bottles));

                // 重新编号以支持稀疏颜色 ID，零保留给空水层。
                ulong packed = (ulong)bottle.InvisibleCount << HIDDENSHIFT;
                for (int layer = 0; layer < bottle.Colors.Count; layer++)
                {
                    int color = bottle.Colors[layer];
                    if (color == 0)
                        throw new ArgumentException("水层颜色不能为零。", nameof(bottles));

                    if (!colors.TryGetValue(color, out int code))
                    {
                        code = totals.Count;
                        if ((ulong)code > COLORMASK)
                            throw new ArgumentException("颜色种类超出求解器支持范围。", nameof(bottles));

                        colors.Add(color, code);
                        totals.Add(0);
                    }

                    totals[code]++;
                    packed |= (ulong)code << (layer * COLORBITS);
                }

                initial[index] = packed;
                layerCount += bottle.Colors.Count;
            }

            // 完成后每种颜色必须恰好分到若干个满瓶。
            foreach (int total in totals)
            {
                if (total % BottleData.CAPACITY != 0)
                    return m_unsolvable;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var search = new Search(initial.Length, layerCount / BottleData.CAPACITY, totals, cancellationToken);
            WaterSortSolution solution = search.SearchShortest(initial, out int nextBound);
            return solution ?? search.SearchByDepth(initial, nextBound);
        }

        private static int Count(ulong bottle)
        {
            int count = 0;
            while (count < BottleData.CAPACITY && GetColor(bottle, count) != 0)
                count++;
            return count;
        }

        private static ulong GetColor(ulong bottle, int layer)
        {
            return (bottle >> (layer * COLORBITS)) & COLORMASK;
        }

        private static int GetHidden(ulong bottle)
        {
            return (int)(bottle >> HIDDENSHIFT);
        }

        private static int GetVisibleRun(ulong bottle, int count, ulong color)
        {
            int hidden = GetHidden(bottle);
            int layer = count - 1;
            while (layer >= hidden && GetColor(bottle, layer) == color)
                layer--;
            return count - layer - 1;
        }

        private static ulong RemoveLayers(ulong bottle, int count)
        {
            ulong layers = bottle & ((1UL << (count * COLORBITS)) - 1);
            int hidden = Math.Min(GetHidden(bottle), Math.Max(0, count - 1));
            return layers | ((ulong)hidden << HIDDENSHIFT);
        }

        private sealed class Search
        {
            private readonly int m_goalSegments; // 目标局面中的同色连续段数。
            private readonly int[] m_goalBottomCounts; // 完成时各颜色应占据的瓶底数量。
            private readonly int[] m_bottomDeficits; // 复用的各色瓶底缺口缓冲区。
            private readonly CancellationToken m_cancellationToken; // 当前求解的取消令牌。
            private readonly ulong[] m_sorted; // 复用的瓶子排序缓冲区。
            private readonly char[] m_keyBuffer; // 复用的状态键编码缓冲区。
            // 当前搜索轮次中各等价局面的最少已知步数。
            private readonly Dictionary<string, int> m_distances = new Dictionary<string, int>();
            private readonly HashSet<string> m_currentKeys = new HashSet<string>(); // 深度搜索的路径内判重。
            private readonly List<WaterSortStep> m_currentSteps = new List<WaterSortStep>(); // 当前深度优先路径。

            internal Search(
                int bottleCount, int goalSegments, List<int> totals, CancellationToken cancellationToken)
            {
                m_goalSegments = goalSegments;
                m_goalBottomCounts = new int[totals.Count];
                for (int index = 1; index < totals.Count; index++)
                    m_goalBottomCounts[index] = totals[index] / BottleData.CAPACITY;
                m_bottomDeficits = new int[totals.Count];
                m_cancellationToken = cancellationToken;
                m_sorted = new ulong[bottleCount];
                m_keyBuffer = new char[bottleCount * 4];
            }

            internal WaterSortSolution SearchShortest(ulong[] initial, out int nextBound)
            {
                var open = new MinHeap();
                string key = CreateKey(initial);
                m_distances.Add(key, 0);
                open.Push(new Node(initial, key, null, Estimate(initial)));

                while (open.Count > 0)
                {
                    m_cancellationToken.ThrowIfCancellationRequested();
                    Node current = open.Pop();
                    if (m_distances[current.Key] != current.Distance)
                        continue;

                    if (current.Estimate == 0)
                    {
                        nextBound = 0;
                        return CreateSolution(current);
                    }

                    // 当前最低估价仍是全局下界，可用它作为深度搜索的首个阈值。
                    if (m_distances.Count >= CACHESIZE)
                    {
                        nextBound = current.Priority;
                        return null;
                    }

                    foreach (WaterSortStep step in EnumerateSteps(current.Bottles))
                    {
                        ulong[] next = (ulong[])current.Bottles.Clone();
                        ApplyStep(next, step);
                        string nextKey = CreateKey(next);
                        int distance = current.Distance + 1;
                        if (m_distances.TryGetValue(nextKey, out int known) && known <= distance)
                            continue;

                        m_distances[nextKey] = distance;
                        var path = new Path(current.Path, step, distance);
                        open.Push(new Node(next, nextKey, path, Estimate(next)));
                    }
                }

                nextBound = 0;
                return m_unsolvable;
            }

            internal WaterSortSolution SearchByDepth(ulong[] bottles, int bound)
            {
                // 每轮枚举阈值以内的局面，再跳到本轮遇到的最小超限值。
                while (bound != int.MaxValue)
                {
                    m_distances.Clear();
                    int nextBound = int.MaxValue;
                    if (VisitDepth(bottles, 0, bound, ref nextBound))
                        return new WaterSortSolution(m_currentSteps.Count, m_currentSteps.ToArray());

                    bound = nextBound;
                }

                return m_unsolvable;
            }

            private bool VisitDepth(ulong[] bottles, int distance, int bound, ref int nextBound)
            {
                m_cancellationToken.ThrowIfCancellationRequested();
                string key = CreateKey(bottles);
                if (m_currentKeys.Contains(key))
                    return false;

                bool knownState = m_distances.TryGetValue(key, out int known);
                if (knownState && known <= distance)
                    return false;

                int estimate = Estimate(bottles);
                int priority = distance + estimate;
                if (priority > bound)
                {
                    nextBound = Math.Min(nextBound, priority);
                    return false;
                }

                if (estimate == 0)
                    return true;

                if (m_distances.Count < CACHESIZE || knownState)
                    m_distances[key] = distance;

                m_currentKeys.Add(key);
                foreach (WaterSortStep step in EnumerateSteps(bottles))
                {
                    // 仅保存本次改变的两瓶，回溯后恢复索引对应的局面。
                    ulong source = bottles[step.SourceIndex];
                    ulong target = bottles[step.TargetIndex];
                    ApplyStep(bottles, step);
                    m_currentSteps.Add(step);
                    if (VisitDepth(bottles, distance + 1, bound, ref nextBound))
                        return true;

                    m_currentSteps.RemoveAt(m_currentSteps.Count - 1);
                    bottles[step.SourceIndex] = source;
                    bottles[step.TargetIndex] = target;
                }

                m_currentKeys.Remove(key);
                return false;
            }

            private string CreateKey(ulong[] bottles)
            {
                // 排序仅用于识别等价局面，搜索路径始终保留实际的瓶子位置。
                Array.Copy(bottles, m_sorted, bottles.Length);
                Array.Sort(m_sorted);
                for (int index = 0; index < m_sorted.Length; index++)
                {
                    ulong bottle = m_sorted[index];
                    int offset = index * 4;
                    m_keyBuffer[offset] = (char)bottle;
                    m_keyBuffer[offset + 1] = (char)(bottle >> 16);
                    m_keyBuffer[offset + 2] = (char)(bottle >> 32);
                    m_keyBuffer[offset + 3] = (char)(bottle >> 48);
                }

                return new string(m_keyBuffer);
            }

            private int Estimate(ulong[] bottles)
            {
                int segments = 0;
                int deficits = m_goalSegments;
                Array.Copy(m_goalBottomCounts, m_bottomDeficits, m_goalBottomCounts.Length);
                foreach (ulong bottle in bottles)
                {
                    int bottom = (int)GetColor(bottle, 0);
                    if (m_bottomDeficits[bottom] > 0)
                    {
                        m_bottomDeficits[bottom]--;
                        deficits--;
                    }

                    // 每层隐藏颜色单独贡献一段。一次倒水至多通过合并减少一段。
                    int hidden = GetHidden(bottle);
                    segments += hidden;
                    ulong previous = 0;
                    for (int layer = hidden; layer < BottleData.CAPACITY; layer++)
                    {
                        ulong color = GetColor(bottle, layer);
                        if (color == 0)
                            break;

                        if (color != previous)
                            segments++;
                        previous = color;
                    }
                }

                // 补上一个瓶底缺口必须往空瓶倒水，这种倒水无法同时合并两段。
                return segments - m_goalSegments + deficits;
            }

            private static IReadOnlyList<WaterSortStep> EnumerateSteps(ulong[] bottles)
            {
                var steps = new List<WaterSortStep>();
                for (int sourceIndex = 0; sourceIndex < bottles.Length; sourceIndex++)
                {
                    ulong source = bottles[sourceIndex];
                    int count = Count(source);
                    if (count == 0)
                        continue;

                    ulong color = GetColor(source, count - 1);
                    int run = GetVisibleRun(source, count, color);
                    if (run == BottleData.CAPACITY)
                        continue;

                    bool emptySeen = false;
                    for (int targetIndex = 0; targetIndex < bottles.Length; targetIndex++)
                    {
                        if (sourceIndex == targetIndex)
                            continue;

                        ulong target = bottles[targetIndex];
                        int targetCount = Count(target);
                        if (targetCount == 0)
                        {
                            // 整瓶平移和选择另一个空瓶都只产生位置对称的局面。
                            if (emptySeen || run == count)
                                continue;
                            emptySeen = true;
                        }
                        else if (targetCount == BottleData.CAPACITY || GetColor(target, targetCount - 1) != color)
                        {
                            continue;
                        }

                        steps.Add(new WaterSortStep(sourceIndex, targetIndex));
                    }
                }

                return steps;
            }

            private static void ApplyStep(ulong[] bottles, WaterSortStep step)
            {
                ulong source = bottles[step.SourceIndex];
                ulong target = bottles[step.TargetIndex];
                int sourceCount = Count(source);
                int targetCount = Count(target);
                ulong color = GetColor(source, sourceCount - 1);
                int run = GetVisibleRun(source, sourceCount, color);
                int moved = Math.Min(run, BottleData.CAPACITY - targetCount);

                // 源瓶的最后一层可见水倒完后，揭开紧邻瓶口的下一层。
                bottles[step.SourceIndex] = RemoveLayers(source, sourceCount - moved);
                for (int layer = targetCount; layer < targetCount + moved; layer++)
                    target |= color << (layer * COLORBITS);
                bottles[step.TargetIndex] = target;
            }

            private static WaterSortSolution CreateSolution(Node goal)
            {
                var steps = new WaterSortStep[goal.Distance];
                for (Path path = goal.Path; path != null; path = path.Previous)
                    steps[path.Distance - 1] = path.Step;
                return new WaterSortSolution(steps.Length, steps);
            }
        }

        private sealed class Path
        {
            internal Path(Path previous, WaterSortStep step, int distance)
            {
                Previous = previous;
                Step = step;
                Distance = distance;
            }

            internal Path Previous { get; }
            internal WaterSortStep Step { get; }
            internal int Distance { get; }
        }

        private sealed class Node
        {
            internal Node(ulong[] bottles, string key, Path path, int estimate)
            {
                Bottles = bottles;
                Key = key;
                Path = path;
                Estimate = estimate;
            }

            internal ulong[] Bottles { get; }
            internal string Key { get; }
            internal Path Path { get; }
            internal int Estimate { get; }
            internal int Distance => Path?.Distance ?? 0;
            internal int Priority => Distance + Estimate;
        }

        private sealed class MinHeap
        {
            private readonly List<Node> m_items = new List<Node>(); // 按估计总步数排列的候选局面。

            internal int Count => m_items.Count;

            internal void Push(Node node)
            {
                int index = m_items.Count;
                m_items.Add(node);
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (!ComesBefore(node, m_items[parent]))
                        break;

                    m_items[index] = m_items[parent];
                    index = parent;
                }

                m_items[index] = node;
            }

            internal Node Pop()
            {
                Node first = m_items[0];
                int lastIndex = m_items.Count - 1;
                Node last = m_items[lastIndex];
                m_items.RemoveAt(lastIndex);
                if (lastIndex == 0)
                    return first;

                int index = 0;
                int child = 1;
                while (child < m_items.Count)
                {
                    int right = child + 1;
                    if (right < m_items.Count && ComesBefore(m_items[right], m_items[child]))
                        child = right;
                    if (!ComesBefore(m_items[child], last))
                        break;

                    m_items[index] = m_items[child];
                    index = child;
                    child = index * 2 + 1;
                }

                m_items[index] = last;
                return first;
            }

            private static bool ComesBefore(Node left, Node right)
            {
                return left.Priority < right.Priority
                    || (left.Priority == right.Priority && left.Estimate < right.Estimate);
            }
        }
    }
}
