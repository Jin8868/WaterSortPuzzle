using AlloyFramework.UI;

namespace Game.UI
{
    public sealed partial class GamePlayUIView : UIView
    {
        // <alloy-generated-bindings>
        // 此区块由 AlloyFramework UI 生成器维护，请勿手动修改。
        [UnityEngine.SerializeField, AlloyFramework.UI.UIBindingReference]
        private UnityEngine.UI.Button m_btnBack;

        public UnityEngine.UI.Button BtnBack => m_btnBack;

        [UnityEngine.SerializeField, AlloyFramework.UI.UIBindingReference]
        private UnityEngine.UI.Button m_btnRetrun;

        public UnityEngine.UI.Button BtnRetrun => m_btnRetrun;

        [UnityEngine.SerializeField, AlloyFramework.UI.UIBindingReference]
        private UnityEngine.UI.Text m_txtTiems;

        public UnityEngine.UI.Text TxtTiems => m_txtTiems;

        [UnityEngine.SerializeField, AlloyFramework.UI.UIBindingReference]
        private UnityEngine.GameObject m_goBottle;

        public UnityEngine.GameObject GoBottle => m_goBottle;
        // </alloy-generated-bindings>

        // <alloy-generated-animation-keys>
        // 此区块由 AlloyFramework UI 生成器维护，请勿手动修改。
        public static class AnimationKeys
        {
        }
        // </alloy-generated-animation-keys>
    }
}
