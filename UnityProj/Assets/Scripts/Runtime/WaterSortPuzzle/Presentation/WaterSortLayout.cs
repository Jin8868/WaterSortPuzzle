using System.Collections.Generic;
using UnityEngine;

namespace Game.WaterSortPuzzle
{
    /// <summary>按配置模板安排瓶子位置。</summary>
    public static class WaterSortLayout
    {
        /// <summary>计算所有瓶子的原位与布局缩放。</summary>
        /// <param name="bottles">当前关卡中的瓶子。</param>
        public static void Apply(IReadOnlyList<BottleData> bottles)
        {
            // 余数分配给靠前的行，保持每行独立居中。
            int count = bottles.Count;
            int rows = count <= 8 ? 1 : count <= 24 ? 2 : 3;
            float spaceX = count <= 8 ? 230f : count <= 16 ? 207f : count <= 24 ? 154f : 157f;
            float spaceY = count <= 8 ? 0f : count <= 16 ? 428f : count <= 24 ? 377f : 294f;
            float offsetY = count <= 8 ? -65f : count <= 16 ? 120f : count <= 24 ? 63f : 133f;
            float scale = count <= 8 ? 1f : count <= 16 ? 0.9f : count <= 24 ? 0.72f : 0.62f;
            int index = 0;
            for (int row = 0; row < rows; row++)
            {
                int rowCount = count / rows + (row < count % rows ? 1 : 0);
                for (int column = 0; column < rowCount; column++)
                {
                    BottleData bottle = bottles[index++];
                    bottle.LocalPos = new Vector2((column - (rowCount - 1) * 0.5f) * spaceX,
                        -row * spaceY + offsetY);
                    bottle.LocalScale = scale;
                }
            }
        }
    }
}
