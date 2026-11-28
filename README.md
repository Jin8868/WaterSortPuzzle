# WaterSortPuzzle

Unity 版本：2022.3.17f1c1。使用 Unity Hub 打开本仓库中的 UnityProj。

## 本地依赖

将 AlloyFramework 仓库放在 WaterSortPuzzle 的同级目录。Unity 通过 `Packages/manifest.json` 中的本地路径引用框架。

## Wwise SDK

使用 Wwise Unity Integration 2025.1.11.4331（SDK 2025.1.11 Build 9262）。
SDK 保留 Assets/Wwise 导入布局，不转换为 UPM 包，以兼容现有编辑器路径。
第三方 SDK 目录已被 Git 忽略；项目的 ScriptableObjects、Resources、WwiseSettings.xml
及其 .meta 文件继续跟踪。恢复 SDK 时请关闭 Unity。

从官方安装或已有模板取得相同版本的 Assets/Wwise 目录后，在仓库根目录运行：

    powershell -ExecutionPolicy Bypass -File .\Tools\RestoreWwise.ps1 -SourceWwisePath ..\game-template\UnityProj\Assets\Wwise

也可以将 SourceWwisePath 指向其他相同版本的 SDK 目录。
脚本仅恢复第三方目录，不覆盖本项目的初始化设置。
Wwise 生成的 SoundBanks 和本地测试音频工程仍按 .gitignore 排除，需另行生成或准备。

## Git 提交范围

提交业务代码、资源及 .meta、Packages、ProjectSettings，以及配置生成所需的 DesignData 和 Tools。
不提交 Unity 缓存、第三方 Wwise SDK 或模板的历史提交。
模板远程保留为 upstream，仅供参考；本项目使用独立的初始提交，不直接合并模板历史。
