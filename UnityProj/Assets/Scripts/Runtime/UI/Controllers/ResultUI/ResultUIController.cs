using System;
using AlloyFramework.UI;
using Cysharp.Threading.Tasks;

namespace Game.UI
{
    public sealed class ResultUIController : UIController<ResultUIView, ResultUIData>
    {
        private bool m_acceptingChoice; // 当前结算是否允许提交按钮操作。

        internal bool Owns(ResultUIData data)
        {
            return ReferenceEquals(Data, data);
        }

        protected override void OnCreate()
        {
            View.BtnClose.onClick.AddListener(HandleClose);
            View.BtnNext.onClick.AddListener(HandleNext);
        }

        protected override void OnInitData(ResultUIData data)
        {
            Display(data);
        }

        protected override void OnRefresh(ResultUIData data)
        {
            Display(data);
        }

        protected override void OnClose()
        {
            m_acceptingChoice = false;
        }

        protected override void OnDispose()
        {
            m_acceptingChoice = false;
            View.BtnClose.onClick.RemoveListener(HandleClose);
            View.BtnNext.onClick.RemoveListener(HandleNext);
        }

        private void Display(ResultUIData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            // 使用结算时冻结的数值，弹窗加载期间关卡数据可以独立变化。
            int seconds = data.ElapsedSeconds;
            View.TxtResultTitle.text = data.Title;
            View.TxtUseTime.text = $"{seconds / 60:00}:{seconds % 60:00}";
            View.TxtBottleNum.text = data.SortedBottleCount.ToString();
            View.TxtStepNum.text = data.PourStepCount.ToString();
            View.BtnNext.gameObject.SetActive(data.HasNextLevel);
            SetChoiceEnabled(true);
        }

        private void SetChoiceEnabled(bool enabled)
        {
            m_acceptingChoice = enabled;
            View.BtnClose.interactable = enabled;
            View.BtnNext.interactable = enabled;
        }

        private void HandleClose()
        {
            if (m_acceptingChoice && State == UIState.Active)
                SubmitAsync(Data.ReturnToSelection).Forget();
        }

        private void HandleNext()
        {
            if (m_acceptingChoice && State == UIState.Active && Data.HasNextLevel)
                SubmitAsync(Data.EnterNextLevel).Forget();
        }

        private async UniTask SubmitAsync(Func<UniTask> action)
        {
            // 目标页加载期间保留弹窗，导航失败时允许在同一份结算上重试。
            ResultUIData data = Data;
            SetChoiceEnabled(false);
            try
            {
                await action();
            }
            finally
            {
                if (Owns(data) && (State == UIState.Active || State == UIState.Paused))
                    SetChoiceEnabled(true);
            }
        }
    }
}
