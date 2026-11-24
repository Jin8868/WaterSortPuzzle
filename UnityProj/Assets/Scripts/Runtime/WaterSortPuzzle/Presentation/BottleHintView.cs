using UnityEngine;
using UnityEngine.UI;

namespace Game.WaterSortPuzzle
{
    /// <summary>用闪烁和常亮的瓶身描边区分倒出瓶与接水瓶。</summary>
    [RequireComponent(typeof(Image))]
    public sealed class BottleHintView : MonoBehaviour
    {
        private const float FADESECONDS = 0.45f; // 单次呼吸渐变的时长。
        private Image m_marker; // 预制体已有的瓶身描边。
        private bool m_isSource; // 倒出瓶使用动态描边，接水瓶保持常亮。
        private float m_elapsed; // 当前提示的闪烁进度。

        internal void Show(bool isSource)
        {
            if (m_marker == null) m_marker = GetComponent<Image>();
            m_isSource = isSource;
            m_elapsed = 0f;
            m_marker.color = isSource ? new Color32(255, 211, 117, 255) : new Color32(111, 243, 218, 255);
            gameObject.SetActive(true);
            m_marker.canvasRenderer.SetAlpha(1f);
        }

        private void Update()
        {
            if (!m_isSource) return;
            m_elapsed = Mathf.Repeat(m_elapsed + Time.unscaledDeltaTime, 2f * FADESECONDS);
            m_marker.canvasRenderer.SetAlpha(Mathf.Lerp(1f, 0.12f, Mathf.PingPong(m_elapsed / FADESECONDS, 1f)));
        }
    }
}
