using System;
using System.Threading;
using AlloyFramework;
using Cysharp.Threading.Tasks;
using UnityEngine.Scripting;

namespace Game
{
    internal sealed class DefaultGameCodePreparation : IGameCodePreparation
    {
        private readonly GameCodePreparationResult m_result; // 供后续步骤读取的静态业务代码准备结果。

        /// <summary>根据主包启动配置创建静态业务代码准备服务。</summary>
        /// <param name="result">与后续业务步骤共享的准备结果。</param>
        /// <param name="runtimeAssemblyName">静态业务程序集名称。</param>
        /// <param name="installerTypeName">业务安装入口的完整类型名称。</param>
        /// <exception cref="ArgumentNullException">共享准备结果为空。</exception>
        /// <exception cref="InvalidOperationException">启动配置与主包中的业务入口不一致。</exception>
        [Preserve]
        public DefaultGameCodePreparation(
            GameCodePreparationResult result,
            string runtimeAssemblyName,
            string installerTypeName)
        {
            // 框架按统一构造签名传入配置，确保配置对应项目编译进主包的唯一安装入口。
            Type installerType = typeof(GameRuntimeInstaller);
            if (runtimeAssemblyName != installerType.Assembly.GetName().Name ||
                installerTypeName != installerType.FullName)
            {
                throw new InvalidOperationException("启动配置与主包内置的业务安装入口不一致。");
            }

            m_result = result ?? throw new ArgumentNullException(nameof(result));
        }

        /// <summary>从静态业务程序集中安装配置加载器。</summary>
        /// <param name="context">当前框架上下文。</param>
        /// <param name="cancellationToken">启动取消令牌。</param>
        /// <returns>业务安装完成的操作。</returns>
        public UniTask PrepareAsync(FrameworkContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 安装主包内置的业务代码，并向框架公开后续入口查找使用的程序集。
            GameRuntimeInstaller.Install();
            cancellationToken.ThrowIfCancellationRequested();
            m_result.RuntimeAssembly = typeof(GameRuntimeInstaller).Assembly;
            m_result.IsCompleted = true;
            return UniTask.CompletedTask;
        }
    }
}
