using System;
using AlloyFramework.UI;
using Cysharp.Threading.Tasks;

namespace Game.UI
{
    public sealed class ConfirmUIController : UIController<ConfirmUIView, ConfirmUIData>
    {
        private bool m_acceptingChoice; // 当前弹窗是否仍可提交选择。

        /// <summary>判断弹窗是否仍在等待指定请求的选择。</summary>
        /// <param name="data">打开弹窗时传入的数据实例。</param>
        /// <returns>弹窗仍显示这次请求时返回 true。</returns>
        public bool IsShowing(ConfirmUIData data)
        {
            return m_acceptingChoice && ReferenceEquals(Data, data);
        }

        protected override void OnCreate()
        {
            View.BtnCancel.onClick.AddListener(HandleCancel);
            View.BtnConfirm.onClick.AddListener(HandleConfirm);
        }

        protected override void OnInitData(ConfirmUIData data)
        {
            Display(data);
        }

        protected override void OnRefresh(ConfirmUIData data)
        {
            Display(data);
        }

        protected override void OnClose()
        {
            m_acceptingChoice = false;
        }

        protected override void OnDispose()
        {
            View.BtnCancel.onClick.RemoveListener(HandleCancel);
            View.BtnConfirm.onClick.RemoveListener(HandleConfirm);
        }

        private void Display(ConfirmUIData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            View.TxtTitle.text = data.Title;
            View.TxtDesc.text = data.Description;
            m_acceptingChoice = true;
        }

        private void HandleCancel()
        {
            if (m_acceptingChoice)
                CompleteAsync(Data.OnCancel).Forget();
        }

        private void HandleConfirm()
        {
            if (m_acceptingChoice)
                CompleteAsync(Data.OnConfirm).Forget();
        }

        private async UniTask CompleteAsync(Action callback)
        {
            // 先锁定本次选择，等弹窗关闭后再允许业务回调跳转或打开另一个弹窗。
            m_acceptingChoice = false;
            await CloseAsync();
            callback?.Invoke();
        }
    }
}
