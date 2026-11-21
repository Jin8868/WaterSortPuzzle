using UnityEngine;

namespace Game.WaterSortPuzzle
{
    /// <summary>控制瓶子提示图像的淡入淡出。</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BottleHintView : MonoBehaviour
    {
        private const float FADESECONDS = 0.4f; // 从透明到完全可见的时间。
        private CanvasRenderer m_renderer; // 提示节点上已有的图像渲染器。
        private float m_elapsed; // 本次提示已经播放的时间。

        private void OnEnable()
        {
            if (m_renderer == null)
                m_renderer = GetComponent<CanvasRenderer>();

            m_elapsed = 0f;
            m_renderer.SetAlpha(0f);
        }

        private void Update()
        {
            m_elapsed = Mathf.Repeat(m_elapsed + Time.unscaledDeltaTime, 2f * FADESECONDS);
            m_renderer.SetAlpha(Mathf.PingPong(m_elapsed / FADESECONDS, 1f));
        }
    }
}
