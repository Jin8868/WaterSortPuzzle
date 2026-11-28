using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;

namespace Game.Editor
{
    /// <summary>从本项目的 Player 中排除未使用的共享协议插件。</summary>
    public sealed class GamePlayerAssemblyFilter : IFilterBuildAssemblies
    {
        private const string PROTOBUFASSEMBLY = "Google.Protobuf.dll"; // 共享包中的协议插件文件名。

        /// <summary>使用 Unity 默认的构建回调顺序。</summary>
        public int callbackOrder => 0;

        /// <summary>筛选将进入 Player 的托管程序集。</summary>
        /// <param name="buildOptions">本次 Player 构建的选项。</param>
        /// <param name="assemblies">Unity 收集的程序集路径。</param>
        /// <returns>排除协议插件后的程序集路径。</returns>
        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
        {
            // 共享包的导入配置由其他项目共用，只在当前项目的 Player 构建中筛除该文件。
            var playerAssemblies = new List<string>(assemblies.Length);
            foreach (string assembly in assemblies)
            {
                if (!string.Equals(Path.GetFileName(assembly), PROTOBUFASSEMBLY, StringComparison.OrdinalIgnoreCase))
                {
                    playerAssemblies.Add(assembly);
                }
            }
            return playerAssemblies.Count == assemblies.Length ? assemblies : playerAssemblies.ToArray();
        }
    }
}
