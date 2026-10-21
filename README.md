<img src="https://raw.githubusercontent.com/Cetirzine/Kiln-Windows/main/assets/logo.svg" width="128" alt="Kiln">

# Kiln for Windows

<p align="center">
  <img src="https://img.shields.io/badge/C%23-12-blueviolet?logo=csharp&logoColor=white" alt="C#">
  <img src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white" alt=".NET 10">
  <img src="https://img.shields.io/badge/WinUI_3-Windows_10_1809+-0078D4?logo=windows&logoColor=white" alt="WinUI 3">
  <a href="https://github.com/Cetirzine/Kiln-Windows/releases/latest"><img src="https://img.shields.io/github/v/release/Cetirzine/Kiln-Windows?logo=github&label=release" alt="Release"></a>
  <a href="https://github.com/Cetirzine/Kiln-Windows/releases"><img src="https://img.shields.io/github/downloads/Cetirzine/Kiln-Windows/total?logo=github&label=downloads" alt="Downloads"></a>
  <a href="https://github.com/Cetirzine/Kiln-Windows/stargazers"><img src="https://img.shields.io/github/stars/Cetirzine/Kiln-Windows?logo=github" alt="Stars"></a>
  <a href="LICENSE.md"><img src="https://img.shields.io/badge/license-PolyForm_Strict_1.0.0-blue" alt="License"></a>
</p>

为 Windows 打造的 Minecraft: Java Edition 启动器，WinUI 3 原生实现。是 [Kiln（macOS 版）](https://github.com/Cetirzine/kiln-launcher)的姊妹项目，核心逻辑逐文件对照移植，数据格式完全互通。

## 特点

**一键接管 PCL2**

- 首次启动自动在本机查找 PCL2（Plain Craft Launcher 2），提示「已检测到您当前环境有PCL2，是否继承其配置？」
- 确认后继承游戏目录、版本隔离、内存、Java、窗口尺寸、启动参数等全部设置；各版本独立设置（`versions/<名称>/PCL/Setup.ini`）迁移为 Kiln 的 `.kiln/setup.json`
- 不移动、不修改 PCL2 的任何文件，两个启动器可以共用同一个游戏目录

**安装与管理**

- 原版、Fabric、Quilt、Forge、NeoForge 安装（Forge / NeoForge 运行安装器 processors，思路参考 [HMCL](https://github.com/HMCL-dev/HMCL)）
- 整合包：安装 Modrinth（.mrpack）与 CurseForge 整合包，导出为 .mrpack；作者禁止第三方下载的文件会自动尝试从 Modrinth 获取同一文件
- 从其他启动器导入：PCL2、Prism Launcher、MultiMC / PolyMC（实例文件夹或导出的 .zip）、CurseForge App 与官方启动器
- 版本重命名、复制，记录每个版本的游戏时长；存档导入导出，一键直接进入存档（1.20 及以上）

**账号**

- 离线账号（与服务端一致的离线 UUID）、微软账号（设备代码流登录，内置已过审的 Client ID）、外置登录（LittleSkin 等 authlib-injector 皮肤站，不保存密码）；多账号切换，每个版本可固定使用某个账号
- 离线账号可使用本地皮肤或同名正版玩家的皮肤（内置本地皮肤服务，自动签名材质属性）

**性能**

- 按 Java 版本调优的 G1 参数（可选分代 ZGC），初始堆为最大堆的一半
- Java 25+：AOT 缓存（JEP 483/514/515）；Java 19~24：动态 CDS 归档；Java 24+：紧凑对象头
- 快速校验：文件未变化时跳过 SHA-1 计算
- 默认使用官方源下载，BMCLAPI 镜像作为备用（可在设置中切换）；失败的文件会降低并发自动重试

**社区资源**

- Modrinth 与 CurseForge（Curse.tools 公共代理、MCIM 备用，可填写官方 API 密钥）的 Mod、资源包、光影搜索与下载，安装时自动补全前置
- 已安装的 Mod 自动识别（SHA-1 / CurseForge 指纹）并检查更新，可一键更新
- Mod 中文名：英文名后显示中文名，可用中文、拼音或拼音首字母搜索（如 `jxdl` → 机械动力）

**工具**

- 游戏日志（解析 log4j XML）、崩溃原因分析、导出诊断包（登录令牌自动隐藏）、导出启动脚本（.bat）
- 自动检查新版本，确认后下载、校验并替换自身

**Windows 原生**

- WinUI 3 自定义控件、点阵背景，跟随系统浅色 / 深色外观；透明窗口按钮随背景明暗切换黑白图标
- 系统通知、深色模式、高分屏 DPI
- 简体中文 / 繁體中文 / English

## 从 macOS 版移植说明

核心（`Kiln.Core`）与 macOS 版的 `LauncherCore` 逐文件对照移植，行为一致：

- 版本 JSON 规则判定以 `windows` 为目标系统，natives 选择 `natives-windows`
- 版本数据（`.kiln/setup.json`、版本布局、整合包格式）与 macOS 版完全一致，目录可在两平台间互换
- macOS 专属优化（Metal 渲染、Apple Silicon 的 LWJGL arm64 替换、Rosetta 2）不适用于 Windows，未包含
- 自更新从本仓库的 Release 获取 zip 资产，SHA-256 校验后由 PowerShell 脚本替换自身

## 补充 Mod 中文名

中文名表沿用 [macOS 版仓库](https://github.com/Cetirzine/kiln-launcher)的 [`assets/mod-names-zh.json`](https://github.com/Cetirzine/kiln-launcher/blob/main/assets/mod-names-zh.json)，欢迎在那里提交 PR。

## 构建

需要 .NET 10 SDK。

```powershell
dotnet build            # 或 dotnet build -c Release
dotnet run --project Kiln.App
```

### 自检与端到端测试

```powershell
dotnet run --project Kiln.SelfTest                # PCL2 检测 / 接管 / 实例扫描 / Java 搜索
dotnet run --project Kiln.SelfTest -- --launch-test 26.2 <游戏目录>   # 安装并启动游戏（会短暂打开游戏窗口）
```

## 微软登录

Kiln 内置的 Client ID 已通过微软的 Minecraft API 审核，下载即可直接登录正版账号，不需要任何设置。

自行构建的分支如果想换用自己的 Azure 应用：在构建时设置环境变量 `KILN_MS_CLIENT_ID=<你的 ID>`，或在 设置 → 下载 中填写。注意：新注册的 Azure 应用需要先向微软申请 Minecraft API 访问权限，否则登录会在最后一步返回 403（详见 https://aka.ms/AppRegInfo ）。

## 许可

[PolyForm Strict 1.0.0](LICENSE.md)。个人使用与分发未修改的原始发布包不受限制；分发修改后的版本需要另行授权。
