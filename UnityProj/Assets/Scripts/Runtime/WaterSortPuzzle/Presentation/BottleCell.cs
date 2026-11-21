using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.WaterSortPuzzle
{
    /// <summary>驱动单个瓶子的交互、水层和局部动画。</summary>
    public sealed class BottleCell : MonoBehaviour, IPointerClickHandler
    {
        private const float FILLRATE = 8f; // 每秒变化的水层数。
        private const float TILTRATE = 600f; // 每秒可转过的角度。
        private const float TRAVELTIME = 0.24f; // 飞向目标及归位的秒数。
        private const float SLIDETIME = 0.32f; // 无尽换瓶的滑动秒数。
        private const float COMPLETETIME = 0.44f; // 完成封瓶的秒数。
        private const float MOUTHHEIGHT = 171f; // 瓶口相对瓶身中心的高度。
        private const float MOUTHEDGE = 25f; // 瓶口边缘相对中心的宽度。
        private const float STREAMGAP = 84f; // 倒水时两瓶瓶口的垂直距离。
        private static readonly Vector2 m_streamStartOffset = new Vector2(6f, 4f); // 棋盘坐标中的出水口微调量。
        private const float OFFSCREEN = 2600f; // 屏外换瓶起点及终点的横坐标幅度。

        private readonly HashSet<PourCommand> m_waiting = new HashSet<PourCommand>(); // 已预占但尚未接水。
        private readonly HashSet<PourCommand> m_receiving = new HashSet<PourCommand>(); // 已插层且正在接水。
        private WaterSortPuzzleController m_controller; // 当前会话。
        private BottleLiquidView m_liquid; // 瓶内液体渲染。
        private RectTransform m_rect; // 瓶子布局及旋转根节点。
        private RectTransform m_cap; // 完成时落下的瓶盖。
        private Vector2 m_capHome; // 瓶盖原始位置。
        private GameObject m_selection; // 瓶子外圈选中高亮。
        private AudioSource m_pourAudio; // 当前瓶的独立倒水声。
        private AudioClip m_pourClip; // 循环的程序水声。
        private PourCommand m_outgoing; // 当前源瓶命令。
        private PourCommand m_completion; // 当前完成动画的令牌。
        private Vector2 m_animationStart; // 移动或滑动的起点。
        private float m_animationAngle; // 移动或回正的起始角度。
        private float m_elapsed; // 当前阶段经过的时间。
        private float m_angle; // 瓶身的实际旋转角度。
        private float m_direction; // 倾倒朝向。
        private float m_fill; // 当前显示的实数层数。
        private float m_finalFill; // 倒完后的显示层数。
        private int m_revealCount; // 揭晓动画开始前的隐藏层数。
        private bool m_streamVisible; // 当前源瓶是否正在显示瓶间水流。

        public BottleData Data { get; private set; }
        public BottleData PourTarget => m_outgoing?.Target;
        public Color PourColor => WaterSortPalette.GetBody(m_outgoing?.Color ?? 0);
        public Vector3 WaterFallPosition => m_rect.TransformPoint(GetMouthOffset())
            + m_rect.parent.TransformVector(m_streamStartOffset * m_rect.localScale.x);

        public event Action<BottleCell, bool> WaterFallChanged;
        public event Action<BottleCell> Released;

        /// <summary>将池中的视图绑定到指定瓶子。</summary>
        /// <param name="controller">所属关卡控制器。</param>
        /// <param name="bottle">本实例显示的瓶子。</param>
        public void Bind(WaterSortPuzzleController controller, BottleData bottle)
        {
            Unbind();
            InitializeVisuals();
            m_controller = controller;
            Data = bottle;
            Data.State = EBottleState.Idle;
            ApplyHome();
            SyncLiquid();
            SetCap(bottle.IsFinish());

            // 全局事件使用命令和瓶子引用过滤，回收时成对解绑。
            controller.BeginPour += HandleBeginPour;
            controller.DoPour += HandleDoPour;
            controller.FinishPour += HandleFinishPour;
            controller.InterruptPour += HandleInterruptPour;
            controller.SyncData += HandleSyncData;
            controller.BottleSelectChange += HandleSelection;
            controller.Stopped += HandleStopped;
        }

        /// <summary>解除会话绑定并复位瞬时效果。</summary>
        public void Unbind()
        {
            if (m_controller == null)
                return;

            m_controller.BeginPour -= HandleBeginPour;
            m_controller.DoPour -= HandleDoPour;
            m_controller.FinishPour -= HandleFinishPour;
            m_controller.InterruptPour -= HandleInterruptPour;
            m_controller.SyncData -= HandleSyncData;
            m_controller.BottleSelectChange -= HandleSelection;
            m_controller.Stopped -= HandleStopped;
            ClearPourEffects();
            m_waiting.Clear();
            m_receiving.Clear();
            m_outgoing = null;
            m_completion = null;
            m_revealCount = 0;
            Data = null;
            m_controller = null;
        }

        /// <summary>从左侧滑入布局位置。</summary>
        /// <param name="skipAnimation">是否直接摆放到目标位置。</param>
        public void PlaySlideIn(bool skipAnimation)
        {
            if (skipAnimation)
            {
                ApplyHome();
                Data.State = EBottleState.Idle;
                return;
            }

            m_elapsed = 0f;
            m_animationStart = new Vector2(-OFFSCREEN, Data.LocalPos.y);
            m_rect.anchoredPosition = m_animationStart;
            Data.State = EBottleState.SlideIn;
        }

        /// <summary>向右侧滑出并通知宿主回收。</summary>
        /// <param name="skipAnimation">是否立即回收。</param>
        public void PlaySlideOut(bool skipAnimation)
        {
            if (skipAnimation)
            {
                Release();
                return;
            }

            ClearPourEffects();
            m_elapsed = 0f;
            m_animationStart = m_rect.anchoredPosition;
            m_completion = null;
            Data.State = EBottleState.SlideOut;
        }

        /// <summary>将瓶子点击转交给关卡选择规则。</summary>
        /// <param name="eventData">指针点击数据。</param>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && Data != null
                && (Data.State == EBottleState.Idle || Data.State == EBottleState.BeingPour))
                m_controller.TrySelect(Data);
        }

        private void InitializeVisuals()
        {
            if (m_rect != null)
                return;

            // 沿用瓶身、选中外圈和瓶盖，现有瓶身图承担点击检测。
            m_rect = (RectTransform)transform;
            m_liquid = GetComponentInChildren<BottleLiquidView>(true);
            m_selection = transform.Find("go_select").gameObject;
            m_cap = (RectTransform)transform.Find("go_top");
            m_capHome = m_cap.anchoredPosition;
            transform.Find("go_bottle").GetComponent<Image>().raycastTarget = true;
            SetAngle(0f);
        }

        private void Update()
        {
            if (Data == null)
                return;

            float deltaTime = Time.unscaledDeltaTime;
            switch (Data.State)
            {
                case EBottleState.Idle:
                    UpdateSelection(deltaTime);
                    break;
                case EBottleState.Moving:
                    UpdateMoving(deltaTime);
                    break;
                case EBottleState.Pouring:
                    UpdatePouring(deltaTime);
                    break;
                case EBottleState.BeingPour:
                    UpdateReceiving(deltaTime);
                    break;
                case EBottleState.Returning:
                    UpdateReturning(deltaTime);
                    break;
                case EBottleState.SlideIn:
                case EBottleState.SlideOut:
                    UpdateSliding(deltaTime);
                    break;
                case EBottleState.CompleteAnim:
                    UpdateCompletion(deltaTime);
                    break;
            }
        }

        private void OnDestroy()
        {
            Unbind();
            if (m_pourClip != null)
                Destroy(m_pourClip);
        }

        private void HandleSelection(BottleData selected)
        {
            m_selection.SetActive(selected == Data && Data.State == EBottleState.Idle);
        }

        private void HandleBeginPour(PourCommand command)
        {
            if (command.Source == Data)
            {
                m_outgoing = command;
                m_direction = Data.LocalPos.x <= command.Target.LocalPos.x ? -1f : 1f;
                m_animationStart = m_rect.anchoredPosition;
                m_elapsed = 0f;
                m_animationAngle = m_angle;
                m_finalFill = Data.Colors.Count - command.ColorCount;
                m_revealCount = Data.InvisibleCount;
                m_rect.SetAsLastSibling();
                Data.State = EBottleState.Moving;
            }

            if (command.Target == Data)
            {
                m_waiting.Add(command);
                Data.State = EBottleState.BeingPour;
            }
        }

        private void HandleDoPour(PourCommand command)
        {
            // 订阅顺序可变化。源瓶先收到广播时也进入动画，由目标瓶负责实际插层。
            if (command == m_outgoing && Data.State == EBottleState.Moving)
            {
                Data.State = EBottleState.Pouring;
                m_liquid.SetHidden(0, 0f);
                m_liquid.SetWave(true);
                m_liquid.SetHighlight(false);
                PlayPourSound();
                SetStream(true);
            }

            if (command.Target == Data && m_waiting.Contains(command) && command.Receive())
            {
                m_waiting.Remove(command);
                m_receiving.Add(command);
                m_liquid.SetBubbles(true);
                m_liquid.RefreshColors(Data);
                m_liquid.SetWave(true);
                m_liquid.SetHighlight(false);
            }
        }

        private void HandleFinishPour(PourCommand command)
        {
            if (command.Target != Data || !m_receiving.Remove(command) || UpdateReceivingEffects())
                return;

            SetFill(Data.Colors.Count);
            if (!Data.IsFinish())
            {
                Data.State = EBottleState.Idle;
                return;
            }

            // 仅最后结束接水的命令持有完成动画令牌。
            m_completion = command;
            m_elapsed = 0f;
            Data.State = EBottleState.CompleteAnim;
            SetCap(true);
            m_cap.anchoredPosition = m_capHome + Vector2.up * 75f;
        }

        private void HandleInterruptPour(PourCommand command)
        {
            if (command == m_outgoing || (command.Source == Data && Data.State == EBottleState.Returning))
            {
                ClearPourEffects();
                m_outgoing = null;
                m_revealCount = 0;
                ApplyHome();
                Data.State = EBottleState.Idle;
            }

            if (command.Target != Data)
                return;

            // 等待中的预占无需改液面，只有已经接水的命令参与上升速率。
            bool wasReceiving = m_receiving.Remove(command);
            bool wasWaiting = m_waiting.Remove(command);
            if (wasReceiving || !wasWaiting)
            {
                int settled = Data.Colors.Count;
                foreach (PourCommand receiving in m_receiving)
                    settled -= receiving.ColorCount;
                SetFill(Mathf.Clamp(m_fill - command.ColorCount, settled, Data.Colors.Count));
            }
            if (Data.State == EBottleState.BeingPour && !UpdateReceivingEffects())
                Data.State = EBottleState.Idle;
            if (Data.State == EBottleState.CompleteAnim && (command == m_completion || !Data.IsFinish()))
            {
                m_completion = null;
                Data.State = EBottleState.Idle;
                ApplyHome();
            }

            SetCap(Data.IsFinish());
        }

        private void HandleSyncData(PourCommand command)
        {
            if (command.Source != Data && command.Target != Data)
                return;

            // 活跃接水保留正在上升的液面，等待中其余预占也保持原样。
            m_liquid.RefreshColors(Data);
            if (m_receiving.Count == 0)
                SetFill(Data.Colors.Count);
            else
                SetFill(Mathf.Min(m_fill, Data.Colors.Count));
            if (command.Source == Data)
                m_revealCount = 0;
        }

        private void HandleStopped()
        {
            ClearPourEffects();
            Data.State = EBottleState.Stop;
        }

        private void UpdateSelection(float deltaTime)
        {
            Vector2 target = Data.LocalPos;
            if (m_controller.SelectedBottle == Data)
                target.y += 30f;
            m_rect.anchoredPosition = Vector2.MoveTowards(m_rect.anchoredPosition, target, 400f * deltaTime);
        }

        private void UpdateMoving(float deltaTime)
        {
            float progress = Advance(deltaTime, TRAVELTIME);
            SetAngle(Mathf.Lerp(m_animationAngle, m_direction * GetOverflowAngle(m_fill), progress));
            m_rect.anchoredPosition = Vector2.Lerp(m_animationStart, GetPourPosition(), progress);
            if (progress >= 1f)
                m_controller.NotifyMovingFinished(m_outgoing);
        }

        private void UpdatePouring(float deltaTime)
        {
            if (m_outgoing.State != EPourCommandState.Received)
                return;

            SetFill(Mathf.MoveTowards(m_fill, m_finalFill, FILLRATE * deltaTime));
            float targetAngle = m_direction * GetOverflowAngle(m_fill);
            SetAngle(Mathf.MoveTowards(m_angle, targetAngle, TILTRATE * deltaTime));
            m_rect.anchoredPosition = GetPourPosition();
            if (m_fill != m_finalFill || m_angle != targetAngle || !m_outgoing.Commit())
                return;

            // 先切换到回程，再广播结束，以允许同步回调替换目标瓶。
            PourCommand finished = m_outgoing;
            ClearPourEffects();
            m_outgoing = null;
            m_liquid.RefreshColors(Data);
            m_liquid.SetHidden(m_revealCount, 0f);
            m_animationStart = m_rect.anchoredPosition;
            m_animationAngle = m_angle;
            m_elapsed = 0f;
            Data.State = EBottleState.Returning;
            m_controller.NotifyPourFinished(finished);
        }

        private void UpdateReceiving(float deltaTime)
        {
            if (m_receiving.Count == 0)
                return;

            SetFill(Mathf.MoveTowards(m_fill, Data.Colors.Count, FILLRATE * m_receiving.Count * deltaTime));
        }

        private bool UpdateReceivingEffects()
        {
            bool isReceiving = m_receiving.Count > 0;
            m_liquid.SetWave(isReceiving);
            m_liquid.SetBubbles(isReceiving);
            m_liquid.SetHighlight(!isReceiving);
            if (!isReceiving)
            {
                SetFill(Data.Colors.Count);
            }
            return isReceiving || m_waiting.Count > 0;
        }

        private void UpdateReturning(float deltaTime)
        {
            float progress = Advance(deltaTime, TRAVELTIME);
            SetAngle(Mathf.Lerp(m_animationAngle, 0f, progress));
            m_rect.anchoredPosition = Vector2.Lerp(m_animationStart, Data.LocalPos, progress);
            if (m_revealCount > Data.InvisibleCount)
                m_liquid.SetHidden(m_revealCount, progress);
            if (progress < 1f)
                return;

            m_liquid.SetHidden(Data.InvisibleCount, 0f);
            m_revealCount = 0;
            Data.State = EBottleState.Idle;
        }

        private void UpdateSliding(float deltaTime)
        {
            bool isLeaving = Data.State == EBottleState.SlideOut;
            Vector2 end = isLeaving ? new Vector2(OFFSCREEN, Data.LocalPos.y) : Data.LocalPos;
            float progress = Advance(deltaTime, SLIDETIME);
            m_rect.anchoredPosition = Vector2.Lerp(m_animationStart, end, progress);
            if (progress < 1f)
                return;

            if (isLeaving)
                Release();
            else
                Data.State = EBottleState.Idle;
        }

        private void UpdateCompletion(float deltaTime)
        {
            float progress = Advance(deltaTime, COMPLETETIME);
            float scale = Data.LocalScale * (1f + 0.09f * Mathf.Sin(Mathf.PI * progress));
            m_rect.localScale = Vector3.one * scale;
            m_cap.anchoredPosition = Vector2.Lerp(m_capHome + Vector2.up * 75f, m_capHome, progress);
            if (progress < 1f)
                return;

            PourCommand completed = m_completion;
            m_completion = null;
            Data.State = EBottleState.Idle;
            m_controller.NotifyBottleComplete(Data, completed);
        }

        private float Advance(float deltaTime, float duration)
        {
            m_elapsed += deltaTime;
            float progress = Mathf.Clamp01(m_elapsed / duration);
            return Mathf.SmoothStep(0f, 1f, progress);
        }

        private static float GetOverflowAngle(float fill)
        {
            // 解瓶口下沿旋转后的 Y 坐标等于水平液面时的角度。
            const float NECKHEIGHT = 0.45f;
            const float NECKHALFWIDTH = MOUTHEDGE / 380f;
            float surface = -NECKHEIGHT + 0.845f * fill / BottleData.CAPACITY;
            float radius = Mathf.Sqrt(NECKHEIGHT * NECKHEIGHT + NECKHALFWIDTH * NECKHALFWIDTH);
            float angle = Mathf.Acos(Mathf.Clamp(surface / radius, -1f, 1f))
                - Mathf.Atan2(NECKHALFWIDTH, NECKHEIGHT);
            return Mathf.Min(140f, angle * Mathf.Rad2Deg);
        }

        private Vector2 GetPourPosition()
        {
            // 逆推瓶身位置，让旋转中的出水点固定在目标瓶口正上方。
            BottleData target = m_outgoing.Target;
            Vector2 mouthOffset = m_rect.localRotation * GetMouthOffset();
            Vector2 destination = target.LocalPos + Vector2.up * (MOUTHHEIGHT + STREAMGAP) * target.LocalScale;
            return destination - mouthOffset * Data.LocalScale;
        }

        private void SetAngle(float angle)
        {
            m_angle = angle;
            m_rect.localRotation = Quaternion.Euler(0f, 0f, angle);
            m_liquid.SetRotation(angle);
        }

        private Vector3 GetMouthOffset()
        {
            float edge = m_angle == 0f ? 0f : -Mathf.Sign(m_angle) * MOUTHEDGE;
            return new Vector3(edge, MOUTHHEIGHT, 0f);
        }

        private void ApplyHome()
        {
            m_rect.anchoredPosition = Data.LocalPos;
            m_rect.localScale = Vector3.one * Data.LocalScale;
            SetAngle(0f);
            m_selection.SetActive(false);
        }

        private void SetCap(bool visible)
        {
            m_cap.anchoredPosition = m_capHome;
            m_cap.gameObject.SetActive(visible);
        }

        private void SyncLiquid()
        {
            m_liquid.RefreshColors(Data);
            m_liquid.SetHidden(Data.InvisibleCount, 0f);
            m_liquid.SetHighlight(true);
            SetFill(Data.Colors.Count);
        }

        private void SetFill(float fill)
        {
            m_fill = fill;
            m_liquid.SetFill(fill);
        }

        private void Release()
        {
            Data.State = EBottleState.Deactive;
            Released?.Invoke(this);
        }

        private void SetStream(bool visible)
        {
            if (m_streamVisible == visible)
                return;

            m_streamVisible = visible;
            if (!visible && m_liquid != null)
                m_liquid.ClearStream();
            WaterFallChanged?.Invoke(this, visible);
        }

        internal void RefreshStream(BottleCell target)
        {
            Vector3 surface = target.m_liquid.GetReceivingSurface(target.m_fill);
            m_liquid.SetStream(WaterFallPosition, surface, PourColor);
        }

        private void ClearPourEffects()
        {
            SetStream(false);
            if (m_liquid != null)
            {
                m_liquid.SetWave(false);
                m_liquid.SetBubbles(false);
                m_liquid.SetHighlight(true);
            }
            if (m_pourAudio != null)
                m_pourAudio.Stop();
        }

        private void PlayPourSound()
        {
            if (m_pourAudio == null)
            {
                m_pourAudio = gameObject.AddComponent<AudioSource>();
                m_pourAudio.playOnAwake = false;
                m_pourAudio.loop = true;
                m_pourAudio.volume = 0.2f;
                m_pourClip = CreatePourSound();
                m_pourAudio.clip = m_pourClip;
            }

            m_pourAudio.Play();
        }

        private static AudioClip CreatePourSound()
        {
            const int SAMPLERATE = 22050;
            float[] samples = new float[SAMPLERATE / 2];
            System.Random noise = new System.Random(1023);
            float filtered = 0f;

            // 用低通噪声和短周期脉动合成水流，首尾衰减避免循环接缝爆音。
            for (int index = 0; index < samples.Length; index++)
            {
                float progress = (float)index / (samples.Length - 1);
                filtered = Mathf.Lerp(filtered, (float)noise.NextDouble() * 2f - 1f, 0.16f);
                float pulse = 0.75f + 0.25f * Mathf.Sin(progress * 18f * Mathf.PI);
                float envelope = Mathf.Clamp01(Mathf.Min(progress, 1f - progress) * 60f);
                samples[index] = filtered * pulse * envelope;
            }

            AudioClip clip = AudioClip.Create("WaterSortPour", samples.Length, 1, SAMPLERATE, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
