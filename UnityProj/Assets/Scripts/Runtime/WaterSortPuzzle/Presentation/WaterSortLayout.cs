using System.Collections.Generic;
using UnityEngine;

namespace Game.WaterSortPuzzle
{
    /// <summary>为居中的瓶阵选择行数、间距和动画边界。</summary>
    public static class WaterSortLayout
    {
        private const float SIDEREACH = 380f; // 倾倒时所需的左右空间。
        private const float UPPERREACH = 536f; // 倾倒时所需的上方空间。
        private const float LOWERREACH = 220f; // 旋转时所需的下方空间。
        private const float ROWSPACING = 460f; // 原大瓶身高 380，行间留 80。
        private const float COLUMNSPACING = 232f; // 原大瓶身宽 132，列间至少留 100。

        /// <summary>按游戏参考画布排列预览瓶组。</summary>
        /// <param name="bottles">当前关卡的瓶子。</param>
        public static void Apply(IReadOnlyList<BottleData> bottles)
        {
            Apply(bottles, 1824f, 536f, 396f);
        }

        /// <summary>按可用空间选择瓶身显示比例最大的居中排列。</summary>
        /// <param name="bottles">当前关卡的瓶子。</param>
        /// <param name="width">棋盘可用宽度。</param>
        /// <param name="above">中心到顶部的距离。</param>
        /// <param name="below">中心到底部操作栏的距离。</param>
        /// <returns>以瓶阵中心为原点的完整动画边界。</returns>
        public static Rect Apply(IReadOnlyList<BottleData> bottles, float width, float above, float below)
        {
            // 布局尚未就绪时沿用预览尺寸，后续棋盘适配仍按真实窗口缩放。
            if (width <= 0f || above <= 0f || below <= 0f)
                return Apply(bottles, 1824f, 536f, 396f);

            int count = bottles.Count;
            int bestRows = 1;
            float bestScale = 0f;
            for (int rows = 1; rows <= count; rows++)
            {
                float halfHeight = (rows - 1) * ROWSPACING * 0.5f;
                float heightScale = Mathf.Min(above / (halfHeight + UPPERREACH), below / (halfHeight + LOWERREACH));
                if (heightScale <= bestScale)
                    break;

                int columns = Mathf.CeilToInt((float)count / rows);
                float rowWidth = (columns - 1) * COLUMNSPACING + 2f * SIDEREACH;
                float scale = Mathf.Min(1f, Mathf.Min(width / rowWidth, heightScale));
                if (scale > bestScale)
                {
                    bestScale = scale;
                    bestRows = rows;
                }
            }

            // 将富余横向空间分给列间距，不为额外留白牺牲已经选出的瓶身大小。
            int longestRow = Mathf.CeilToInt((float)count / bestRows);
            float spacing = COLUMNSPACING;
            if (longestRow > 1 && count > 8)
                spacing = Mathf.Clamp((width / bestScale - 2f * SIDEREACH) / (longestRow - 1), COLUMNSPACING, 280f);

            int index = 0;
            for (int row = 0; row < bestRows; row++)
            {
                int rowCount = count / bestRows + (row < count % bestRows ? 1 : 0);
                for (int column = 0; column < rowCount; column++)
                {
                    BottleData bottle = bottles[index++];
                    bottle.LocalPos = new Vector2((column - (rowCount - 1) * 0.5f) * spacing,
                        ((bestRows - 1) * 0.5f - row) * ROWSPACING);
                    bottle.LocalScale = 1f;
                }
            }

            float halfWidth = (longestRow - 1) * spacing * 0.5f + SIDEREACH;
            float rowExtent = (bestRows - 1) * ROWSPACING * 0.5f;
            return Rect.MinMaxRect(-halfWidth, -rowExtent - LOWERREACH, halfWidth, rowExtent + UPPERREACH);
        }
    }
}
