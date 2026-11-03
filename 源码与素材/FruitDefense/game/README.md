> 工作目录已更新到 **0.5 章节增益功能版**：3章9关、清波三选一、同章加成继承和防线评分。当前规则与试玩说明以[0.5说明](../09_项目交接文档/11_章节增益与防线评分0.5.md)为准。下文保留前版背景，旧交付ZIP尚未更新。

# Godot保卫水果原型工程

已确定路线：Godot的.NET版本 + C#，最终目标为手机竖屏游戏，目前使用Windows预览和调试。

## 当前状态

本目录包含可运行的720×1280竖屏原型：3类射手、1类敌人、3波进攻、触屏/鼠标拉弓、精简战斗信息、头像入口、小暂停菜单、设置、主页流程与胜负。正式主页样式和手机安装包还在后续阶段。

从项目根目录双击[启动保卫水果.cmd](../启动保卫水果.cmd)试玩，或使用[打开Godot工程.cmd](../打开Godot工程.cmd)进入编辑器。操作见[试玩说明](../原型试玩说明.md)，代码结构见[源码阅读说明](../源码阅读说明.md)。

2026年10月14日环境准备结果：

- Godot 4.7.2 .NET编辑器：`D:\GodotDev\godot\Godot_v4.7.2-stable_mono_win64`。
- .NET SDK 10.0.402：`D:\GodotDev\dotnet`。
- 双击启动入口：`D:\GodotDev\启动Godot.cmd`。
- 工具说明：`D:\GodotDev\工具使用说明.md`。
- 独立验证项目：`D:\GodotDev\verification\CSharpSmoke`，使用Godot.NET.Sdk 4.7.2与net10.0。
- 下载包通过官方校验，最小C#项目编译为0个警告、0个错误，Godot成功执行C#脚本。
- 导出模板将在正式打包阶段按固定的Godot版本准备。

工具验证记录位于 `D:\GodotDev\logs` 和 `D:\GodotDev\verification\verification-result.json`。本版检查规则、触屏、多指隔离、菜单与竖屏布局，结果见[验证报告](../06_预览与测试/试玩记录/原型验证报告.md)。游戏规则见[玩法说明](../02_需求与参考/保卫水果_玩法与原型说明.md)。

## 素材位置

- `assets/backgrounds`：实际加载的背景、地图图块及场景装饰。
- `assets/characters`：己方射手、怪物、Boss、守护目标及角色部件。
- `assets/animations`：动画序列帧、动画图集等图片资源。
- `assets/projectiles`：子弹、飞行物等资源。
- `assets/effects`：技能、命中、爆炸、状态和环境特效图片。
- `assets/ui`：按钮、卡牌、图标、面板和界面装饰。
- `assets/audio`：游戏使用的音乐和音效。
- `assets/fonts`：游戏使用的字体。

## 场景位置

当前实际入口为 `scenes/main/Main.tscn`，角色与弹药暂由C#类和代码绘制组织。下面其他目录为正式素材与场景拆分预留。

- `scenes/main`：主入口和场景切换。
- `scenes/world`：战场、道路和建造位置。
- `scenes/characters/heroes`：己方射手场景。
- `scenes/characters/enemies`：怪物与Boss场景。
- `scenes/projectiles`：普通、狙击与霰弹的弹药场景。
- `scenes/defense`：隔离网和水晶场景。
- `scenes/skills`：技能效果场景。
- `scenes/ui`：英雄头像、菜单与结算场景。

## C#源码位置

- `src/core`：游戏入口、战场控制和公共规则。
- `src/entities`：射手、怪物、弹药和角色状态。
- `src/input`：拉弓瞄准、发射与角色切换。
- `src/defense`：隔离网受损和核心生命规则。
- `src/combat`：攻击、伤害、状态和主动技能。
- `src/levels`：路线、波次、关卡与胜负流程。
- `src/presentation`：动画、特效与音频的连接代码。
- `src/ui`：玩家界面与交互代码。

核心规则使用普通C#类，Godot入口连接输入和表现。`BattleWorld`统一推进战斗，`IAttackBehavior`有普通、贯穿、溅射三个实现，防线和核心使用独立状态类。`src/levels`仍为后续多关卡扩展预留，当前波次逻辑位于`BattleWorld`及配置文件。

## 配置和引擎资源

- `config`：角色数值、关卡和波次等规则配置。
- `resources/materials`：材质和着色器。
- `resources/animations`：引擎动画资源。
- `resources/particles`：粒子效果资源。

`../04_处理完成` 保存检查通过的素材成品；本目录的素材是适合运行时加载的导出版本。原始图片仍保留在 `../01_你提供的素材`。

官方参考：[Godot C#环境要求](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html)。
