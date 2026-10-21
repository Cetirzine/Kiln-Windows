# SwiftUI → Windows 对照与验收

基准：本工作区 `kiln-launcher/Sources`，2026-10-20。目标 Windows x64。

状态：正在实施，未达到完整验收。源码中有对应方法不代表已通过真实运行验证。
本轮追加验证：85 项核心回归、43 项原生 UI 检查通过；Modrinth 与 CurseForge 公共代理的搜索、版本、下载、校验通过，实际 Mod 导出引用链接和 SHA-1/SHA-512 清单通过。Java、账号面板、关于页、开始页、导出与导入弹窗已在窗口中检查；三语言 / 三档窗口检查覆盖工作流弹窗内边界、底部同排按钮、实例卡片及 Mod 搜索控件的实际边缘，不代表其余页面的全量截图验收，不以编译通过替代。
macOS / Apple Silicon / ARM64 专属优化不移植：Metal、Rosetta、LWJGL arm64 替换、GLFW macOS 补丁、Dock 与 QoS；通用 JVM/CDS/AOT/下载优化保留。

## 已确认的问题与处理

| 原版依据 | 旧 Windows 问题 | 当前处理 / 待验收 |
|---|---|---|
| MainView.swift / Theme.swift | 通用 NavigationView 替换 76pt 侧栏，缺点阵、边框与硬阴影 | 已重写侧栏，点阵控件、卡片组件；仍需全页截图验收 |
| KilnApp.swift | 1040×680 被当作物理像素，高 DPI 裁切 | 已按 DPI 换算；依用户要求提供 920×620 / 1040×680 / 1200×800 三档，不再自由调整尺寸；待各档截图验收 |
| MainView.swift / 设置语言 | 保存语言却不刷新侧栏 | 统一重建导航和页面，保留设置标签；待三语言交互测试 |
| Theme.swift | WinUI 默认按钮、开关、选择框和对话框 | 正在统一自定义模板；用户要求全部应用内弹窗及控件一致 |
| HomePage.swift | 场景固定高度，操作区在场景外，物品栏格子错误 | 重写 HUD、九格、最新游玩排序、账号入口、绿色启动进度按钮 |
| HomePage.swift | 主页无官方更新卡片 | 已补 Mojang 更新卡片；网络内容已在窗口中出现 |
| HomePage.swift | 固定账号被忽略、停止按钮同时触发启动 | 已修主页账号选择、运行中栏与独立停止入口 |
| MainView.swift / RunningGamesBar | 缺完整日志和诊断入口 | 补日志级别颜色、自动滚动、复制、打开文件、诊断包；修复 5000 行后不再刷新；存档/协议/主页统一启动任务与运行记录，待真实启动验收 |
| InstancePages.swift | 所有版本挤在下拉框，缺分组列表 | 正在改为分类列表、名称筛选、文件夹选择、上下文菜单 |
| InstanceSetupPage / InstanceContent | 卡片布局简陋，文件夹固定四列，资源和截图操作占位过大 | 按原版恢复自适应带图标文件夹及诊断导出/删除入口；依最新要求，管理区使用三列等宽等高网格，删除固定右下角。存档带图标及标题栏导入操作，资源/光影使用紧凑条目和图标操作，截图为 16:9 自适应缩略图。三语言、三档窗口原生边界检查通过，概览和资源卡片已实机查看 |
| ModManager | 简陋行、无搜索、重复插入异步结果、无逐项更新 | 新 ModManager：过滤、来源版本、图标、逐项/全部更新、文件监视；待验证 |
| DownloadPage.swift | Mod 下载藏在下拉框，资源入口缺失，重复父控件错误 | 正在改为游戏/Mod/整合包/资源包/光影/任务标签；新资源搜索页 |
| InstallPanel | 没有原版左版本列表、右 320pt 安装面板 | 已恢复图标列表、已安装标记、分段筛选、320pt 面板、加载器及附加组件；禁止重复点击安装，待截图与各加载器安装验收 |
| ResourceSearchPage | 没有来源/版本/加载器筛选、分页、详情版本选择 | 已实现；Curse.tools HTTPS /v1 替代停服的 MCIM；搜索→版本→下载→SHA1 与指纹识别已实测，见 CURSEFORGE-NETWORK.md |
| 必需前置 Mod | 旧实现错误被吞掉，可能报告成功但缺前置 | 改为显示依赖并选择一起安装；还需验证解析失败的显式反馈 |
| ImportUI.swift | 缺原版多选导入弹窗、来源与已导入标记 | 已补自定义 sheet、多选、拖放、文件夹/压缩包统一选择、来源/警告/已导入标记及批量重名计划；临时来源与 UI 回归通过，真实启动器联机安装/复制仍需验收 |
| ModpackUI.swift | 缺手动自动收集、元数据及导出文件选择 | 已补导出名称/版本/简介、逐项选择、个人数据默认不选、大小/目录标记；安装确认显示格式/游戏/加载器/文件数并实时校验名称；手动下载按两秒任务循环收集、显示等待数量和完成状态。原生 UI 与实际生成 mrpack、真实 Mod 链接导出通过；完整远端整合包安装仍需验收 |
| AccountUI.swift / HomePage.swift | 微软皮肤/披风、皮肤库无入口，离线改名/顺序/重新登录缺失 | 已补 UI 入口与服务接线，微软账号实际登录/皮肤上传/改名仍需真实账号环境验收 |
| ProfileSheet | 改名可用性检查存在过期响应、对话框嵌套风险 | 已加代次检查、可用状态控制按钮与自定义弹窗；待交互验收 |
| SetupPage.swift | 缺五标签、Java 管理、额外参数等；控件列不齐 | 已重写五标签与缺失设置，统一 230pt 名称列 / 320pt 控件；待复测 |
| SystemIntegration.swift | 只有任务栏闪烁，缺真实通知、任务进度、最近版本菜单 | 待补 Windows 等价行为 |
| FolderWatcher.swift | 原版外部文件变更刷新；Windows 无统一实现 | 已给版本列表/Mod 管理增加监视，其他页面待补 |
| PCL2（Windows 附加功能） | 旧自检直接写入 Pcl2TakenOver 导致弹窗跳过 | 已新增实际 UI 确认标记及设置手动入口，待弹窗验收 |
| 发布体积 | v0.2.10 ZIP 93,733,936 bytes，捆入 ONNX/DirectML/AI/Search | 已改为 WinUI 必要依赖；自包含预览 ZIP 约 68.9 MB，减少约 26%；未删运行时。程序名及产品元数据统一为 Kiln，待独立运行/自动更新验收 |

## 全量源文件覆盖清单

2026-10-21 修复与验证：

- Java 行不再给没有移除按钮的项目预留 76pt 空栏；选中背景铺满行宽，对勾右对齐，移除按钮单独占用实际宽度。
- 依最新要求移除窗口按钮白色底，激活/未激活均透明；普通页面随浅深色主题改变图标颜色，开始页每 250ms 采样按钮下方实际场景像素亮度，深色用白图标、浅色用黑图标，阈值回滞避免动画高光导致闪烁。
- 自定义弹窗改为独立对象，直接挂在主窗口最上层，避免 ContentDialog 与 Popup 重复持有内容、弹出独立漂浮窗口。关闭操作排到当前输入事件之后再移除视觉树；禁用底层控件并阻止主页启动快捷键，焦点只还给仍已加载的控件。
- 账号弹出面板关闭完成后再打开登录/皮肤弹窗，弹窗统一使用主窗口 XamlRoot。购买链接单行显示，登录按钮用网格对齐；为当前有效账号提供直接更换皮肤入口。
- Microsoft 设备代码、轮询、Xbox/XSTS/Minecraft/档案查询均支持取消；设备代码 25 秒、后续档案阶段合计 90 秒超时，显示当前阶段及等待秒数。真实微软账号完成验证仍待用户环境验收，不宣称已验证登录成功。
- 搜索框移除系统模板的默认内边距，修复内层高度超出边框；原生布局实测占位文字中心 14.313pt、输入编辑区中心 14pt，外框高度 28pt。
- 开始页取消外层 ScrollViewer，标准窗口中场景、启动操作与四张新闻均可见，无页面滚动条。关于页恢复原版应用信息、致谢列表和法律卡片，链接按钮同列对齐；省略 Apple 专属 Metal 项。
- 侧栏任务入口在安装时切换为原版的 34×3pt 进度条与运行任务数量，250ms 刷新，任务结束恢复图标；覆盖并行任务数及 50% 进度显示的原生 UI 回归。
- 版本概览恢复版本信息 / 文件夹 / 管理卡片及 Java 标记、启动、快捷方式和启动链接入口。资源包/光影卡片标题显示数量、标题栏操作、文件图标/大小/时间和启用提示；截图恢复自适应多列画廊，支持 PNG/JPG/JPEG。Mod 管理搜索栏、操作栏、列表使用同一宽度，原生布局实测均为 893.333pt。
- `Kiln.exe --ui-smoke-test <报告绝对路径>` 在应用 UI 线程运行合成弹窗及临时实例检查，不登录、不更改账号。原生窗口连续 100 次取消/安装确认/重开通过，无残留 Popup、遮罩或内容父节点，窗口仍响应；报告写入指定 JSON 文件。检查结束显示临时实例概览，关闭窗口时清理临时实例。

0.2.12w 追加修复：

- 按 `ModpackUI.swift` / `ImportUI.swift` 恢复 sheet 的 17pt 标题、22pt 内边距和 480 / 520 / 560pt 宽度；自定义复选框、左侧文件选择操作与右侧确认按钮，三个工作流在三种语言、三档尺寸下均检查弹窗边界与按钮同排。
- 实际窗口发现通用 FlowRow 用于弹窗自动宽度列时会把按钮换行到边框外，已改用水平 StackPanel。验收从“在窗口内”收紧为“在弹窗本身内”。
- 手动收集由弹窗的显示/关闭生命周期显式启停，不等待 XAML Loaded 帧事件；后台 IO 放到工作线程。原生测试实际投放两个临时下载文件，先自动收集一个，再完成剩余文件，哈希不符的文件保持原位。
- Windows 下载目录读取用户 Shell 配置，支持移动过的 Downloads；不会创建或修改系统配置。已拒绝驱动器路径、ADS、父目录等不安全整合包路径，导出取消保留已有目标文件，导出排除启动器账号文件。
- 批量导入按 Windows 忽略大小写规则预留名称，跳过不支持的项目；修复 `CON.txt`、控制字符等来源名称可能导致的无限循环，取消时清理自己的临时解压目录。来源目录不删除。
- 网络回归使用真实 Sodium Extra 文件，导出后确认一条 Modrinth 下载引用、完整 SHA-1/SHA-512 和没有重复 JAR；CurseForge 搜索、版本、下载、指纹识别均再次通过。
- 整合包下载缓存按来源/版本复用。下载结束后再开始安装任务，避免同一个安装同时计为两个运行任务；确认窗口取消与无效文件失败分别返回结果，不把它们误报为安装成功。原生 UI 检查调用实际安装确认入口验证取消时不创建任务，无效整合包报告失败。
- Mod 本地管理的搜索框、筛选、等宽操作按钮及列表两端对齐；资源下载页的搜索框/安装目标或文件安装按钮与结果视口两端对齐。原生检查使用实际控件坐标，不只比较父网格宽度。
- 卡片按原版调整标题栏留白、深色操作控件、14pt 卡片间距及硬阴影，恢复概览诊断包/删除入口；缩略图按 480px 解码，不为图库加载完整尺寸原图。
- 未添加包依赖。自包含发布目录为 `publish/Kiln-v0.2.12w-grid/`，最终程序在不指定 DOTNET_ROOT 的新进程中执行原生检查；安装包与便携 ZIP 一并提供 SHA-256 校验文件。

核心按目录逐个对照：Auth（Accounts、EmbeddedClientID、MinecraftServices、Skins、SkinServer、Yggdrasil）；Community（CurseForge、Modrinth、ModIndex、ModNames、嵌入名表）；Foundation（JSON、Localization、Log、Settings、Net、Job、Updater、Zip）；Game（Remote、Instance、Installer、ForgeInstaller、ForgeAddOns、Import、InstanceManager、Modpack）；Java；Launch（Launcher、CrashAnalyzer、Diagnostics）。

方法名差异已建立初筛清单；`DependentsOf`、`ProjectsByIds`、`ParseVersion` 等属于改名，不能误报成缺失。AppleSilicon、GLFWPatch、MetalRenderer 的文件缺失属于明确的平台排除项。

UI 源文件：MainView、Theme、HomePage、DownloadPage、InstancePages、InstanceContent、AccountUI、SetupPage、AboutPage、ImportUI、ModpackUI、IconPicker、BlockIcon、BlockTextures、DotScene/Classic/Drops/Versions、LogoMark。App 层：KilnApp、Intents、SystemIntegration、FolderWatcher、Snapshot、SelfTest、Main。

## 必须完成的验证

- 从 Swift 自检移植独立回归测试，在临时目录运行；禁止调用旧默认自检，因为它会迁移真实 PCL2 配置。
- 离线 UUID、规则、Maven 路径、参数分割、NeoForge 版本、log4j 分段解析、崩溃分析、版本比较、依赖保护、复制重命名、路径安全、旧 JSON 兼容、皮肤验证。
- 实际启动：原版、Fabric、Quilt、Forge、NeoForge；离线皮肤与微软/外置账号涉及账号环境，逐项记录可验证边界。
- 窗口 920×620 / 1040×680 / 1200×800、缩放、浅深色、三种语言、长标题、空数据、网络失败；所有页面及弹窗均检查边距/裁切/可达操作。
- 自包含发布包启动，检查必要 DLL、体积与哈希；不得用删除运行时或漏功能伪装缩小。
