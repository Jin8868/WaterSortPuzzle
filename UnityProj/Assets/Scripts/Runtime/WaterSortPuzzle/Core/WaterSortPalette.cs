using UnityEngine;

namespace Game.WaterSortPuzzle
{
    public static class WaterSortPalette
    {
        private static readonly Color[] m_bodyColors =
        {
            Color.clear, FromRGB(0x508baa), FromRGB(0xaa3838), FromRGB(0x6b4e9d),
            FromRGB(0x53a183), FromRGB(0x232322), FromRGB(0xcba552), FromRGB(0xdedede)
        }; // 原项目七种水体颜色。
        private static readonly Color[] m_surfaceColors =
        {
            Color.clear, FromRGB(0x7ac6ee), FromRGB(0xdb7676), FromRGB(0x9f79e1),
            FromRGB(0x70ceaa), FromRGB(0x232322), FromRGB(0xffd57a), FromRGB(0xffffff)
        }; // 原项目七种液面颜色。

        /// <summary>取得水体颜色。</summary>
        /// <param name="colorID">颜色编号。</param>
        /// <returns>无效编号返回透明色。</returns>
        public static Color GetBody(int colorID)
        {
            return colorID > 0 && colorID < m_bodyColors.Length ? m_bodyColors[colorID] : Color.clear;
        }

        /// <summary>取得液面颜色。</summary>
        /// <param name="colorID">颜色编号。</param>
        /// <returns>无效编号返回透明色。</returns>
        public static Color GetSurface(int colorID)
        {
            return colorID > 0 && colorID < m_surfaceColors.Length ? m_surfaceColors[colorID] : Color.clear;
        }

        private static Color FromRGB(int rgb)
        {
            return new Color((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f, 1f);
        }
    }
}
