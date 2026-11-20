using System.Collections.Generic;
using UnityEngine;

namespace Game.WaterSortPuzzle
{
    public enum EBottleState
    {
        Idle, Moving, Pouring, BeingPour, Returning, SlideIn, SlideOut, Deactive, Stop, CompleteAnim
    }

    public enum EPourCommandState
    {
        Started, Received, Finished, Reversed
    }

    public sealed class BottleData
    {
        /// <summary>每瓶的最大层数。</summary>
        public const int CAPACITY = 4;
        /// <summary>从瓶底到瓶口排列的颜色。</summary>
        public readonly List<int> Colors = new List<int>(CAPACITY);
        /// <summary>从瓶底开始隐藏的层数。</summary>
        public int InvisibleCount;
        /// <summary>尚未插入目标数据的预占层数。</summary>
        public int LockCount;
        /// <summary>预占层的颜色。</summary>
        public int LockColor;
        /// <summary>当前表现状态。</summary>
        public EBottleState State;
        /// <summary>布局原点。</summary>
        public Vector2 LocalPos;
        /// <summary>布局缩放。</summary>
        public float LocalScale = 1f;

        public int TopColor => Colors.Count == 0 ? 0 : Colors[Colors.Count - 1];
        public int EffectiveColor => LockCount > 0 ? LockColor : TopColor;

        /// <summary>判断瓶子是否为空。</summary>
        /// <returns>没有水层时返回真。</returns>
        public bool IsEmpty() => Colors.Count == 0;

        /// <summary>判断实际层与预占层是否达到容量。</summary>
        /// <returns>没有剩余容量时返回真。</returns>
        public bool IsFull() => Colors.Count + LockCount >= CAPACITY;

        /// <summary>判断所有可见水层是否构成完成瓶。</summary>
        /// <returns>四层同色且无隐藏或预占时返回真。</returns>
        public bool IsFinish()
        {
            if (InvisibleCount != 0 || LockCount != 0 || Colors.Count != CAPACITY || TopColor == 0)
                return false;
            for (int index = 1; index < Colors.Count; index++)
            {
                if (Colors[index] != Colors[0])
                    return false;
            }
            return true;
        }
    }
}
