using AlloyFramework.UI;
using Cysharp.Threading.Tasks;
using Game.WaterSortPuzzle;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    public sealed class LevelItem : UIListItem
    {
        private static readonly Color m_standardBackground = new Color32(36, 70, 90, 255); // 普通关卡底色。
        private static readonly Color m_challengeBackground = new Color32(101, 59, 52, 255); // 挑战关卡底色。
        private static readonly Color m_lockedBackground = new Color32(26, 51, 66, 255); // 锁定关卡底色。
        private static readonly Color m_standardAccent = new Color32(84, 194, 186, 255); // 普通关卡装饰色。
        private static readonly Color m_challengeAccent = new Color32(255, 156, 108, 255); // 挑战关卡装饰色。
        private static readonly Color m_standardStatus = new Color32(143, 237, 217, 255); // 普通关卡状态文字色。
        private static readonly Color m_challengeStatus = new Color32(255, 198, 125, 255); // 挑战关卡状态文字色。

        [SerializeField] private Button m_button; // 复用项上的唯一点击入口。
        [SerializeField] private Image m_background; // 卡片底色和按钮过渡的目标。
        [SerializeField] private Image m_accent; // 卡片顶部装饰条。
        [SerializeField] private GameObject m_content; // 解锁后显示的文案、记录和装饰。
        [SerializeField] private Text m_title; // 关卡编号或无尽挑战名称。
        [SerializeField] private Text m_status; // 关卡进度或模式标识。
        [SerializeField] private Text m_playLabel; // 可玩卡片上的操作提示。
        [SerializeField] private Text m_primaryLabel; // 普通关步数或无尽最高分标题。
        [SerializeField] private Text m_primaryValue; // 普通关步数或无尽最高分。
        [SerializeField] private GameObject m_timeSection; // 普通关最快用时区域。
        [SerializeField] private Text m_timeValue; // 普通关最快用时。
        [SerializeField] private Text m_lockedLevelID; // 锁定卡片显示的关卡名称。
        [SerializeField] private GameObject m_lockedMask; // 锁定时显示的黑色半透明遮罩和锁图标。

        protected override void OnInitialize()
        {
            // 每个池实例只注册一次，点击时读取当前绑定的关卡。
            m_button.onClick.AddListener(HandleClick);
        }

        protected override void OnBind()
        {
            LevelItemData level = (LevelItemData)Data;

            // 锁定卡片使用统一底色，显示编号和锁定状态。
            bool isUnlocked = level.IsUnlocked;
            m_content.SetActive(isUnlocked);
            m_lockedMask.SetActive(!isUnlocked);
            m_button.interactable = isUnlocked;
            if (!isUnlocked)
            {
                m_background.color = m_lockedBackground;
                m_lockedLevelID.text = level.DisplayName;
                return;
            }

            // 每次重绑覆盖颜色、文案和版式，避免共用实例时残留上一关的信息。
            m_background.color = level.IsChallenge ? m_challengeBackground : m_standardBackground;
            m_accent.color = level.IsChallenge ? m_challengeAccent : m_standardAccent;
            Color textAccent = level.IsChallenge ? m_challengeStatus : m_standardStatus;
            m_status.color = textAccent;
            m_playLabel.color = textAccent;
            m_title.text = level.DisplayName;
            m_status.text = level.StatusText;
            m_primaryLabel.text = level.IsInfinity ? "最高分" : "最少步数 / 最优步数";
            m_primaryValue.text = level.PrimaryValue;
            m_timeSection.SetActive(!level.IsInfinity);
            m_timeValue.text = level.FastestTime;
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
