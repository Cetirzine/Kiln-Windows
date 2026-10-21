# Windows 游戏安装与启动验收

2026-10-21，使用工作树中的启动代码及独立数据目录，未替换用户的 `D:\Kiln` 安装。当前修改尚未发布。

## 已修复

- Java 检查同时读取 stdout/stderr，限制诊断输出，8 秒内不能完成则结束自己的检查进程。基础 CDS 生成在后台执行，有超时并响应取消；关闭 CDS 时不再重复生成。
- 正确识别 x86 Java，不再显示为 x64；托管运行时判断要求完整目录边界。Windows `bin/server/classes.jsa` 能正确识别，避免每次启动都重新生成基础缓存。
- 新版 LWJGL 原生库只选择当前 Java 架构；旧版 `${arch}` 正确区分 32/64 位。此前会同时下载和加入其他架构的 Windows 原生库。
- 游戏状态等待真实 Windows 窗口，不再根据 `Setting user:` 等日志或经过 20 秒就报告运行成功。并发观察只触发一次窗口通知；已退出游戏不能被重新标记为运行中。UI 执行隐藏/最小化前再次检查游戏状态，防止排队的通知在退出后隐藏启动器。
- 日志的 stdout/stderr 写入加锁，避免并发写同一个流。
- Forge/NeoForge 安装处理器也改为后台并发读取双管道，使用 `ArgumentList` 保留特殊字符和空参数，响应取消并结束其子进程，设置 15 分钟上限；保留实际失败输出。原版文件准备和最终校验接入同一任务，下载及处理器阶段按九个阶段报告进度，元数据和安装器下载传递取消令牌。

## 当前实测

测试根目录：`artifacts/kiln-game-smoke-86f631f9307f4c409a8eb641354c985e`。所有运行时、缓存、账号数据路径及游戏目录都位于该标记目录内；游戏路径包含空格、中文、方括号和单引号。

- 从真实接口安装 Minecraft 1.21.1、支持库、3,888 个资源对象和 Mojang Windows x64 Java 21.0.7。游戏客户端 SHA-1 与 Mojang 元数据一致：`30c73b1c5da787909b2f73340419fdf13b9def88`。
- Vanilla 启用 CDS 的首次启动进入实际主菜单，加载纹理图集，保持运行 60 秒；窗口通知仅一次且回调时确有窗口。通过正常窗口关闭退出，返回值为 0，生成 19,988,480 bytes 的动态缓存，保存独立实例的游玩时间。报告：`Vanilla-first-cds-success.json`。
- 再次启动复用缓存；日志确认 `Mapped dynamic region`，窗口和图集约 8 秒内就绪，再保持 20 秒并正常关闭。报告：`Vanilla-warm-cds-success.json`，其中 27.98 秒包含保持运行的 20 秒。
- Fabric 0.19.5 和 Quilt 0.24.0 的真实 1.21.1 实例也完成实际窗口、图集加载、正常退出和 CDS 生成，所有十项检查通过，报告分别为 `Fabric-report.json`、`Quilt-report.json`。这两项与 Vanilla 使用同一份包含特殊字符的游戏路径。
- 166 项核心回归通过，含实际双管道背压、超时、取消和一个持续 21 秒、打印类似启动日志但从未创建窗口的子进程；后者不会误报运行成功。报告：`artifacts/parity-java-window-final.txt`。
- App 和 SelfTest 构建均无警告/错误；当前 App 的 149 项原生 UI 回归全部通过，未处理 UI 异常为零。报告：`artifacts/ui-java-window-preview.json`。
- 安装处理器修改在独立输出目录构建后，169 项核心回归通过；新增真实子进程检查双管道背压、空参数/引号/尾部反斜杠、工作目录、非零退出输出，以及取消后进程确已退出。报告：`artifacts/parity-forge-processor-preview.txt`。App 随后重新构建并通过全部 149 项 UI 检查（`artifacts/ui-forge-processor-preview.json`），未处理异常为零。加载器矩阵使用的是前一提交的安装处理器，不能把其结果当作新处理器修改的端到端验证。

## Forge 特殊路径失败尚未解决

Forge 1.21.1-52.1.20 的真实安装处理器全部运行完成，但游戏在创建窗口前退出（返回值 1），因此不能标记 Forge 通过。栈为 `ZipFileSystemProvider.uriToPath → Path.of → ModuleJarMetadata`，输入 URI 中游戏目录的方括号未转义，报 `Illegal character in path`。客户端和 `securemodules-2.2.24.jar` 的 SHA-1 均与官方元数据一致；没有为通过测试而改用不含特殊字符的目录。

Forge 官方曾记录并修复相关 [ZipPath 方括号问题 #10755](https://github.com/MinecraftForge/MinecraftForge/issues/10755)，但本次所用的官方 2.2.24 仍在上述模块元数据路径触发异常，需要继续定位兼容方案。当前卷的 `GetShortPathName` 返回原路径，不能依赖 8.3 短路径处理。报告：`Forge-report.json`，原始日志 `Logs/Launch-20261021-122247-82b850c4bf354c41a11fedf31e7e3c5d.log`。NeoForge 尚未开始，矩阵遇到失败已终止。

## CDS 首次启动性能仍需处理

最初 120 秒超时并不能证明死锁。进一步测得：此 Windows Java 21.0.7 仅以客户端 JAR 为 classpath 运行 `-version`，首次创建动态缓存耗时 133.88 秒后正常退出；移除客户端 JAR 则正常快速完成。客户端清单约 3.19 MB。抽取清单生成独立测试 JAR 后，64 KiB 约 0.21 秒、256 KiB 约 1.35 秒、1 MiB 超过 12 秒。

这与 OpenJDK 21 中清单清理调用反复移动剩余字符串的实现一致，属于源码与计时支持的判断，未获得 JVM 原生线程栈来直接证明。参考 [ClassLoaderExt::read_manifest](https://github.com/openjdk/jdk21u/blob/jdk-21.0.7-ga/src/hotspot/share/classfile/classLoaderExt.cpp) 和 [StringUtils::replace_no_expand](https://github.com/openjdk/jdk21u/blob/jdk-21.0.7-ga/src/hotspot/share/utilities/stringUtils.cpp)。隔离验收已延长首次缓存的等待上限，验证功能实际可用；未删除/关闭 CDS，也未修改带签名的游戏客户端。生产端首次缓存的等待体验和速度仍未解决，不能将此轮描述为已消除所有启动延迟。

其余加载器、完整整合包、真实微软登录/皮肤，以及全页面 1:1 对照仍需继续验收；不能据 Vanilla 或 UI 回归宣称总目标完成。
