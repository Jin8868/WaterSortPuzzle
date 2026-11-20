namespace Game.WaterSortPuzzle
{
    public static class WaterSortSettings
    {
        /// <summary>无尽模式的初始剩余秒数。</summary>
        public const float DURATION = 120f;
        /// <summary>每次完成后延续连击的秒数。</summary>
        public const float COMBOWINDOW = 8f;
        /// <summary>单瓶基础得分，实际得分乘以当前连击数。</summary>
        public const int COMPLETIONSCORE = 100;
        /// <summary>无尽模式单瓶完成增加的秒数。</summary>
        public const float COMPLETIONTIME = 2f;
        /// <summary>提高隐藏瓶概率所需的分数间隔。</summary>
        public const int HIDDENSCORESTEP = 500;
        /// <summary>每个分数档增加的隐藏瓶百分比。</summary>
        public const int HIDDENPROBABILITYSTEP = 10;
        /// <summary>隐藏瓶概率百分比上限。</summary>
        public const int MAXHIDDENPROBABILITY = 60;

        /// <summary>按当前连击档位取得窗口时长。</summary>
        /// <param name="combo">本次完成后的连击数。</param>
        /// <returns>连击窗口秒数。</returns>
        public static float GetComboWindow(int combo)
        {
            if (combo >= 5)
                return 12f;
            return combo >= 3 ? 10f : COMBOWINDOW;
        }

        /// <summary>按当前连击档位取得无尽加时奖励。</summary>
        /// <param name="combo">本次完成后的连击数。</param>
        /// <returns>奖励秒数。</returns>
        public static float GetCompletionTime(int combo)
        {
            if (combo >= 5)
                return 4f;
            return combo >= 3 ? 3f : COMPLETIONTIME;
        }
    }
}
