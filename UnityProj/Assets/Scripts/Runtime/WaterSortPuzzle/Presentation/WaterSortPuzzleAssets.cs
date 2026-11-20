using UnityEngine;

namespace Game.WaterSortPuzzle
{
    /// <summary>引用项目原有瓶子预制，保证运行时与美术资源共用同一份资产。</summary>
    public sealed class WaterSortPuzzleAssets : ScriptableObject
    {
        [SerializeField] private GameObject m_bottlePrefab; // 原始瓶子预制引用。

        public GameObject BottlePrefab => m_bottlePrefab;
    }
}
