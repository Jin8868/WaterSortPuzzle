using AlloyFramework.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public sealed partial class LevelSelectUIView : UIView
    {
        private const float MINIMUMCARDWIDTH = 230f; // 保持关卡卡片的记录和状态文字有足够空间。
        private const int MAXIMUMCOLUMNS = 3; // 横屏宽面板最多显示三列。

        [SerializeField] private Button m_normalModeButton; // 普通关卡页签。
        [SerializeField] private Button m_challengeModeButton; // 挑战关卡页签。
        [SerializeField] private Image m_panelBackground; // 随模式切换色调的列表背景。
        [SerializeField] private Text m_heading; // 选关标题或挑战玩法说明。
        private Vector2 m_safeAreaSize; // 上次布局使用的安全区尺寸。

        // <alloy-generated-bindings>
        // 此区块由 AlloyFramework UI 生成器维护，请勿手动修改。
        [UnityEngine.SerializeField, AlloyFramework.UI.UIBindingReference]
        private AlloyFramework.UI.UIInfiniteList m_listLevel;

        public AlloyFramework.UI.UIInfiniteList ListLevel => m_listLevel;
        // </alloy-generated-bindings>

        // <alloy-generated-animation-keys>
        // 此区块由 AlloyFramework UI 生成器维护，请勿手动修改。
        public static class AnimationKeys
        {
        }
        // </alloy-generated-animation-keys>

        internal Button NormalModeButton => m_normalModeButton;
        internal Button ChallengeModeButton => m_challengeModeButton;

        internal void DisplayMode(bool challenge)
        {
            // 面板和页签使用同一套色调，锁定关卡的状态不会遮住挑战玩法说明。
            m_panelBackground.color = challenge
                ? new Color(0.23f, 0.076f, 0.069f, 0.96f)
                : new Color(0.035f, 0.105f, 0.15f, 0.9f);
            m_heading.color = challenge
                ? new Color(1f, 0.78f, 0.49f)
                : new Color(0.84f, 0.91f, 0.98f);
            m_heading.text = challenge ? "限时解谜 · 无尽冲分" : "选择关卡";
            m_normalModeButton.targetGraphic.color = challenge
                ? new Color(0.29f, 0.15f, 0.16f)
                : new Color(0.12f, 0.41f, 0.52f);
            m_challengeModeButton.targetGraphic.color = challenge
                ? new Color(0.68f, 0.24f, 0.1f)
                : new Color(0.08f, 0.19f, 0.25f);
        }

        internal void ArrangeGrid()
        {
            if (ListLevel == null)
                return;

            // 按安全区对称留边，横竖屏切换时面板保持居中。
            RectTransform panel = (RectTransform)m_panelBackground.transform;
            m_safeAreaSize = ((RectTransform)panel.parent).rect.size;
            bool landscape = m_safeAreaSize.x >= m_safeAreaSize.y;
            Vector2 margin = landscape ? new Vector2(0.06f, 0.045f) : new Vector2(0.03f, 0.045f);
            panel.anchorMin = margin;
            panel.anchorMax = Vector2.one - margin;

            // 用列表自身的边距和间距计算列数，确保新增列后卡片仍有最小可读宽度。
            float spacing = ListLevel.CrossAxisSpacing;
            float availableWidth = panel.rect.width - ListLevel.Padding.horizontal + spacing;
            int columns = Mathf.FloorToInt(availableWidth / (MINIMUMCARDWIDTH + spacing));
            ListLevel.CrossAxisCount = Mathf.Clamp(columns, 1, MAXIMUMCOLUMNS);
        }

        private void LateUpdate()
        {
            // 安全区可能在根节点尺寸不变时更新。
            RectTransform safeArea = (RectTransform)m_panelBackground.transform.parent;
            if (safeArea.rect.size != m_safeAreaSize)
                ArrangeGrid();
        }
    }
}
