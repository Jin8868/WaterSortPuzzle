using System;

namespace Game.UI
{
    /// <summary>一次确认弹窗的显示内容和按钮回调。</summary>
    public sealed class ConfirmUIData
    {
        public string Title { get; }
        public string Description { get; }
        public Action OnCancel { get; }
        public Action OnConfirm { get; }

        /// <summary>创建一组由确认弹窗显示的提示与操作。</summary>
        /// <param name="title">弹窗标题。</param>
        /// <param name="description">选择前需要告知玩家的信息。</param>
        /// <param name="onCancel">玩家点击取消并关闭弹窗后的操作，可为空。</param>
        /// <param name="onConfirm">玩家点击确认并关闭弹窗后的操作，可为空。</param>
        public ConfirmUIData(string title, string description, Action onCancel, Action onConfirm)
        {
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            OnCancel = onCancel;
            OnConfirm = onConfirm;
        }
    }
}
