using AlloyFramework.UI;
using Cysharp.Threading.Tasks;
using Game.WaterSortPuzzle;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    public sealed class LevelItem : UIListItem
    {
        private Image m_completedBackground; // 已通关样式及按钮点中区域。
        private Image m_incompleteBackground; // 未通关样式及按钮点中区域。
        private Text m_levelText; // 关卡编号或无尽挑战名称。
        private Text m_statusText; // 当前关卡状态或无尽最高分。
        private Button m_button; // 复用项上的唯一点击入口。

        protected override void OnInitialize()
        {
            // 复用现有美术节点，并将两个背景都设为按钮的点中区域。
            m_completedBackground = transform.Find("go_pass").GetComponent<Image>();
            m_incompleteBackground = transform.Find("go_Unpass").GetComponent<Image>();
            m_levelText = transform.Find("txt_Level").GetComponent<Text>();
            m_statusText = transform.Find("Level").GetComponent<Text>();
            m_completedBackground.raycastTarget = true;
            m_incompleteBackground.raycastTarget = true;

            // 每个池实例只注册一次，点击时读取当前绑定的关卡。
            m_button = gameObject.AddComponent<Button>();
            m_button.onClick.AddListener(HandleClick);
        }

        protected override void OnBind()
        {
            LevelItemData level = (LevelItemData)Data;

            // 每次绑定都覆盖显示和交互状态，避免滚动复用时沿用旧关卡的样式。
            m_completedBackground.gameObject.SetActive(level.IsCompleted);
            m_incompleteBackground.gameObject.SetActive(!level.IsCompleted);
            m_button.targetGraphic = level.IsCompleted ? m_completedBackground : m_incompleteBackground;
            m_button.interactable = level.IsUnlocked;
            m_levelText.color = level.IsCompleted ? Color.black : Color.white;
            m_statusText.color = m_levelText.color;
            m_levelText.text = level.DisplayName;
            m_statusText.text = level.StatusText;
        }

        protected override void OnUnbind()
        {
            m_button.interactable = false;
        }

        protected override void OnDispose()
        {
            if (m_button != null)
            {
                m_button.onClick.RemoveListener(HandleClick);
            }
        }

        private void HandleClick()
        {
            if (Data is LevelItemData level && level.IsUnlocked)
            {
                WaterSortNavigation.OpenLevelAsync(level.LevelID).Forget();
            }
        }
    }
}
