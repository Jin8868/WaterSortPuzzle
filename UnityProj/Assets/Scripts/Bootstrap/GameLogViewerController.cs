using AlloyFramework;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEventSystem = UnityEngine.EventSystems.EventSystem;

namespace Game
{
    /// <summary>
    /// 在业务 UI 初始化前提供独立的游戏内日志绘制。
    /// </summary>
    [DefaultExecutionOrder(-12000)]
    [RequireComponent(typeof(Reporter))]
    public sealed class GameLogViewerController : MonoBehaviour, IStartupLogViewer
    {
        private static GameLogViewerController m_instance; // 独立于业务资源系统的日志窗口实例。
        private const float INPUTCHECKINTERVAL = 0.25f; // 限制全场景输入与画布节点扫描频率。
        private readonly List<Canvas> m_hiddenCanvases = new List<Canvas>(); // 仅记录本窗口临时隐藏的画布。
        private Reporter m_reporter; // 当前对象上的第三方日志收集器。
        private UnityEventSystem m_ownedEventSystem; // 启动场景没有输入节点时使用的持久化回退。
        private float m_nextInputCheck; // 下一次允许扫描输入节点和新增画布的实时时刻。
        private bool m_wasVisible; // 上一帧已经应用到画布的窗口状态。

        /// <summary>日志窗口是否正在显示，供启动画布让出显示和输入。</summary>
        public static bool IsVisible => m_instance != null &&
            m_instance.m_reporter != null && m_instance.m_reporter.show;

        /// <summary>日志查看器是否已随主包安装。</summary>
        public static bool IsInstalled => m_instance != null;

        /// <summary>打开主包内置日志窗口，不依赖业务资源包初始化成功。</summary>
        public static void Show()
        {
            if (m_instance == null || !m_instance.isActiveAndEnabled) return;
            m_instance.ShowWindow();
        }

        /// <summary>关闭日志窗口并恢复临时隐藏的游戏画布。</summary>
        public static void Hide()
        {
            if (m_instance == null) return;
            m_instance.HideWindow();
        }

        bool IStartupLogViewer.IsVisible => IsVisible;

        void IStartupLogViewer.Show()
        {
            Show();
        }

        private void Awake()
        {
            // 重复进入启动场景时复用原收集器，不能让第二份实例重置第三方静态状态。
            if (m_instance != null && m_instance != this)
            {
                Destroy(gameObject);
                return;
            }
            m_instance = this;
            m_reporter = GetComponent<Reporter>();
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            if (m_instance != this) return;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Canvas.willRenderCanvases += OnWillRenderCanvases;
            m_nextInputCheck = 0f;
        }

        private void Start()
        {
            if (m_instance != this) return;
            EnsureInput();
        }

        private void OnDisable()
        {
            if (m_instance != this) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Canvas.willRenderCanvases -= OnWillRenderCanvases;
            HideWindow();
            if (m_ownedEventSystem != null) m_ownedEventSystem.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (m_instance != this) return;
            RestoreCanvases();
            m_instance = null;
        }

        private void Update()
        {
            if (m_instance != this) return;
            // 桌面和模拟器可直接使用快捷键，不再要求必须通过画圈识别。
            if (Input.GetKeyDown(KeyCode.F8)) ToggleWindow();
            if (Time.unscaledTime < m_nextInputCheck) return;
            m_nextInputCheck = Time.unscaledTime + INPUTCHECKINTERVAL;
            EnsureInput();
            if (IsVisible) HideOtherCanvases();
        }

        private void LateUpdate()
        {
            if (m_instance != this) return;
            // 兼容第三方画圈唤出与窗口内部关闭按钮，统一同步可见状态。
            SynchronizeVisibility();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            m_nextInputCheck = 0f;
            if (IsVisible) HideOtherCanvases();
        }

        private void ToggleWindow()
        {
            if (IsVisible) HideWindow();
            else ShowWindow();
        }

        private void ShowWindow()
        {
            if (m_reporter == null || !m_reporter.Initialized)
            {
                Debug.LogWarning("[GameLogViewer] 收到打开请求，但日志收集器尚未初始化。");
                return;
            }
            m_reporter.doShow();
            Debug.Log("[GameLogViewer] 已收到打开请求，日志窗口已展开。");
            SynchronizeVisibility();
        }

        private void HideWindow()
        {
            if (m_reporter != null && m_reporter.show)
            {
                m_reporter.show = false;
                gameObject.SendMessage("OnHideReporter", SendMessageOptions.DontRequireReceiver);
            }
            SynchronizeVisibility();
        }

        private void SynchronizeVisibility()
        {
            bool visible = IsVisible;
            if (visible == m_wasVisible) return;
            m_wasVisible = visible;
            if (visible) HideOtherCanvases();
            else RestoreCanvases();
        }

        private void HideOtherCanvases()
        {
            // GUI.depth 无法盖过 UGUI Overlay；只临时隐藏原本启用的画布，不切换对象或业务状态。
            Canvas[] canvases = FindObjectsOfType<Canvas>();
            foreach (Canvas canvas in canvases)
            {
                if (!canvas.enabled) continue;
                if (!m_hiddenCanvases.Contains(canvas)) m_hiddenCanvases.Add(canvas);
                canvas.enabled = false;
            }
        }

        private void OnWillRenderCanvases()
        {
            if (!IsVisible) return;
            // 业务脚本可能在本帧重新启用旧画布，在实际绘制前再次让出 IMGUI 层。
            foreach (Canvas canvas in m_hiddenCanvases)
            {
                if (canvas != null && canvas.enabled) canvas.enabled = false;
            }
        }

        private void RestoreCanvases()
        {
            // 原本关闭的画布不在列表中；场景销毁过的引用也不能重新启用。
            foreach (Canvas canvas in m_hiddenCanvases)
            {
                if (canvas != null) canvas.enabled = true;
            }
            m_hiddenCanvases.Clear();
        }

        private void EnsureInput()
        {
            // 优先使用游戏自己的输入节点，回退节点只在场景没有可用 EventSystem 时启用。
            UnityEventSystem current = UnityEventSystem.current;
            if (current != null && current != m_ownedEventSystem && current.isActiveAndEnabled)
            {
                if (m_ownedEventSystem != null) m_ownedEventSystem.gameObject.SetActive(false);
                return;
            }
            UnityEventSystem[] eventSystems = FindObjectsOfType<UnityEventSystem>();
            foreach (UnityEventSystem eventSystem in eventSystems)
            {
                if (eventSystem == m_ownedEventSystem || !eventSystem.isActiveAndEnabled) continue;
                if (m_ownedEventSystem != null) m_ownedEventSystem.gameObject.SetActive(false);
                UnityEventSystem.current = eventSystem;
                return;
            }
            if (m_ownedEventSystem == null)
            {
                var inputRoot = new GameObject("LogViewerInput",
                    typeof(UnityEventSystem), typeof(StandaloneInputModule));
                inputRoot.transform.SetParent(transform, false);
                m_ownedEventSystem = inputRoot.GetComponent<UnityEventSystem>();
            }
            m_ownedEventSystem.gameObject.SetActive(true);
            UnityEventSystem.current = m_ownedEventSystem;
        }

        private void OnGUI()
        {
            if (m_instance != this || m_reporter == null || !m_reporter.Initialized) return;
            // GUI.depth 只控制 IMGUI 顺序，UGUI 遮挡由窗口可见状态统一处理。
            int previousDepth = GUI.depth;
            GUI.depth = -10000;
            try
            {
                m_reporter.OnGUIDraw();
            }
            finally
            {
                GUI.depth = previousDepth;
                SynchronizeVisibility();
            }
        }
    }
}
