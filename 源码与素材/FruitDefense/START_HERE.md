> 最新更新：三档难度已下调为 0.5.1，当前敌人数值、出怪量与验证以[难度调整说明](09_项目交接文档/12_整体难度下调0.5.1.md)为准。

> 工作目录已更新到 **0.5 章节增益功能版**：3章9关、清波三选一、同章加成继承和防线评分。当前规则与试玩说明以[0.5说明](09_项目交接文档/11_章节增益与防线评分0.5.md)为准。下文保留前版背景，旧交付ZIP尚未更新。

# 从这里开始 / START HERE

这是“保卫水果”0.3手机竖屏原型的完整项目交接，Godot .NET + C#。系统未知的云电脑请先识别OS、CPU和图形能力，不要直接运行.exe。

首先阅读 [交接总览](09_项目交接文档/00_交接总览与文档导航.md)、[云电脑启动指南](09_项目交接文档/01_新设备与云电脑启动指南.md)；给AI接手时复制 [完整交接指令](09_项目交接文档/07_给云端AI的完整交接指令.md)。

Windows完整离线包：保持offline-tools与FruitDefense同级，双击初始化开发环境.cmd，再检查开发环境.cmd，然后启动保卫水果.cmd。

Linux/macOS或通用包：需要匹配的工具，按启动指南执行setup.py --download，之后project.sh build和test。有显示环境时再play/test-ui。

This is a source-and-assets handoff, not a mobile installer. Detect OS/architecture/display first. Use tools/Project.ps1 on Windows, tools/setup.py and tools/project.sh on Linux/macOS. Headless clouds can build and test rules; graphical checks need a display. Read the Chinese handoff documents before changing gameplay.

当前已有完整玩法原型、38项原机检查、全部现有素材与提示词。正式美术尚未接入，主页面为流程占位，手机安装包、技能、多关卡尚未完成。打包文件校验见MANIFEST.sha256与SOURCE_SNAPSHOT.json。
