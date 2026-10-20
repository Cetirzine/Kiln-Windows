# CurseForge 接入验证

2026-10-20，在本机 Windows 网络环境实测；状态仅代表测试时刻。

| 服务 | 实测结果 | 当前处理 |
|---|---|---|
| `https://api.curse.tools/v1` | 搜索、项目/文件批量 POST、文件列表、指纹匹配均成功 | 未配置官方密钥时的首选公共代理 |
| `https://api.curse.tools/v1/cf` | 旧路径返回 302，Location 指向 HTTP `/v1` | 不再使用；直接访问新 HTTPS 路径 |
| `https://mod.mcimirror.top/curseforge` | 503，页面正文明确显示“MCIM 暂时停止服务” | 保留备用；不向镜像发送官方密钥 |
| `https://cfproxy.fly.dev` | 搜索、详情、文件接口 TLS 连接失败 | 未加入默认来源 |
| `https://api.curseforge.com` | 无密钥返回 403；无 Kiln 自有有效密钥 | 用户配置自己的密钥后走官方接口，提供连接测试；有效密钥流程尚未实测 |

## 实际链路

`Kiln.SelfTest --network-smoke` 通过：

- Modrinth：空查询、sodium、Sodium Extra 搜索。
- Modrinth：版本查询，下载 `sodium-extra-fabric-0.9.4+mc26.2.jar`（484469 bytes），SHA-1 校验。
- CurseForge：JEI 搜索（Minecraft 1.12.2 / Forge），返回 30 项且包含项目 238222。
- CurseForge：项目批量查询、文件批量查询（整合包清单解析所需接口）。
- CurseForge：下载 `jei_1.12.2-4.22.0.1037.jar`（715519 bytes），SHA-1 校验。
- CurseForge：根据所下载文件的 MurmurHash2 指纹成功识别同一文件。
- 下载全部位于独立临时目录，验证后清理；未安装 Mod、未改账号。

## 官方下载认证

官方 2026-06-10 公告要求 CDN 下载使用 `x-api-key`：
https://blog.curseforge.com/introducing-api-key-authentication-for-curseforge-file-downloads/

下载引擎已补请求头，覆盖 Mod 下载、更新和整合包文件。重定向逐跳重新构造请求，密钥仅发送到允许的 HTTPS CurseForge CDN 主机，不发送给公共代理、其他 CDN 或 HTTP 地址。65 项回归检查全部通过，包含 7 项主机和认证头边界检查。

公共服务可用性与作者的第三方分发设置会影响可下载范围；作者禁止第三方分发的文件继续提供网站入口，不伪造下载地址。
