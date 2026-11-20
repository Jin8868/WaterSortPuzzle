using System;
using AlloyFramework.UI;

namespace Game.UI
{
    public sealed class GamePlayUIController : UIController<GamePlayUIView>
    {
        /// <summary>在已打开的倒水页面进入指定关卡。</summary>
        /// <param name="levelID">要进入的已解锁关卡编号。</param>
        /// <exception cref="InvalidOperationException">倒水页面尚未打开或已关闭。</exception>
        /// <exception cref="ArgumentOutOfRangeException">关卡不存在或尚未解锁。</exception>
        public void EnterLevel(int levelID)
        {
            View.EnterLevel(levelID);
        }

        protected override void OnCreate()
        {
            View.InitializeGameplay();
        }

        protected override void OnOpen()
        {
            View.OpenSession();
        }

        protected override void OnClose()
        {
            View.CloseSession();
        }

        protected override void OnDispose()
        {
            View.CloseSession();
        }
    }
}
