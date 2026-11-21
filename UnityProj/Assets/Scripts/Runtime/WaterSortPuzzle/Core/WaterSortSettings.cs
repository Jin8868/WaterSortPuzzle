namespace Game.WaterSortPuzzle
{
    public static class WaterSortSettings
    {
        /// <summary>无尽模式的初始剩余秒数。</summary>
        public const float DURATION = 120f;
        /// <summary>每完成一瓶获得的固定分数。</summary>
        public const int COMPLETIONSCORE = 100;
        /// <summary>无尽模式单瓶完成增加的秒数。</summary>
        public const float COMPLETIONTIME = 2f;
        /// <summary>提高隐藏瓶概率所需的分数间隔。</summary>
        public const int HIDDENSCORESTEP = 500;
        /// <summary>每个分数档增加的隐藏瓶百分比。</summary>
        public const int HIDDENPROBABILITYSTEP = 10;
        /// <summary>隐藏瓶概率百分比上限。</summary>
        public const int MAXHIDDENPROBABILITY = 60;

    }
}
