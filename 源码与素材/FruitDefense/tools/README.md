# 工具说明

Project.ps1：Windows环境检测、离线/联网初始化、编译、试玩、编辑器、测试与无窗口导入。
project.sh：Linux/macOS入口；test仅规则，test-ui需要图形环境。
setup.py：Python 3.9+，根据平台清单下载/验证/解压官方工具；默认不联网，--download明确允许。
platform-manifest.json：固定版本的官方URL和SHA512。
packages：Godot C#所需NuGet原始包，NuGet.Config相对引用。
external-tools.example.json：环境变量的示例，不会自动读取。
pack_project.py：重新制作通用源码包和校验清单，排除本机生成缓存。
verify_project.py：按MANIFEST.sha256验证解压后的项目文件。

Windows入口的本机D盘查找只为兼容原设备；本地tools或显式环境变量优先。严格迁移验证可以设置FRUITDEFENSE_PORTABLE_ONLY=1。工具与缓存仅使用项目内部目录，不更改全局PATH。

工具许可证保留在官方原始包及nupkg内部。项目本身未在此自动指定开源许可证，候选美术来源与使用范围见交接文档。
