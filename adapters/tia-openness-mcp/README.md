# TIA 上游 MCP 薄适配层 / Upstream TIA MCP adapter

此候选通过公开 MCP stdio 协议调用原版 `TIA_Portal_Openness_MCP`，不引用其内部 C# 类型，不修改其源码。Node SDK 处理协议，`upstream.js` 处理本适配器的子进程和版本约束，`contract.js` 集中处理结果/契约兼容。原有 TIA-Guard Worker、软件 CPU 和 Factory I/O 后端保持独立。

**当前范围：P0 协议发现与无连接诊断。** 版本清单中的 `accepted` 和 `rollback` 仍为空；本候选不替换已发行适配器，不提供工程连接、读写、编译、HMI 或 PLC 控制。上游工具数量不等于本适配器开放的能力。

## 本轮可调用的 MCP

| 工具 | 行为 |
|---|---|
| `upstream_status` | 只读本适配器配置和会话状态，不启动引擎 |
| `upstream_capabilities` | 验证原版文件，启动专属引擎，检查公开 MCP 工具契约 |
| `upstream_state` | 读 `GetState`，必须明确未连接 TIA |
| `upstream_environment` | 固定调用 `Doctor(fix=false)`，前后核对未连接 |
| `upstream_update_check` | 只查上游公开稳定版元数据；不下载、执行或提升版本 |
| `upstream_close` | 结束本适配器拥有的引擎，确认进程退出；会话不可自动重连 |

没有任意工具转发、命令、参数、HTTP 端口或 GUI 后备入口。调用者不能通过 `fix=true` 或工具名绕过此范围。下一阶段的自有工程接口需独立实现和验收。

## 本机准备

需要 Node.js >=22、Python >=3.11（仅准备/测试）、TIA V21 与 Openness/.NET Framework 4.8。通用测试不需要 Siemens 软件。安装 Node 依赖使用锁文件；此候选不修改全局 MCP 配置。

1. 从 `upstream.lock.json` 的官方 `assetUrl` 下载对应 ZIP 到本机专用目录。
2. 在本目录运行 `npm ci --ignore-scripts --no-audit --no-fund`。
3. 用校验和固定的 ZIP 准备一个全新目录，例如：

```powershell
python prepare.py --archive E:\PLC\cache\upstream.zip --output E:\PLC\upstream\candidate --tia-location 'D:\Siemens\Portal V21'
```

准备过程核对官方 ZIP 的 SHA-256、版本清单指定的 62 个运行文件及内容，拒绝路径逃逸、重复/已有输出，不执行程序。生成机器专用的 `candidate.config.json`；原始文件保持不变。`TG_TIA_UPSTREAM_CONFIG` 指向该文件的绝对路径。

每次启动仍核验全部程序文件。上游会生成 `TiaMcpServer.startup.log`，此唯一普通日志文件由版本清单明确放行，不参与程序文件指纹；未知文件、链接及 DLL/EXE 改动继续拒绝。保留日志用于排障，不能通过删除日志或重解压来冒充冷启动复验。

```powershell
$env:TG_TIA_UPSTREAM_CONFIG = 'E:\PLC\upstream\candidate.config.json'
node probe.js
node native-smoke.js
```

`probe.js` 是运维诊断入口，`native-smoke.js` 通过实际 MCP 客户端连接本适配器，再调用真实原版引擎；两者都只覆盖 P0。标准输出为 JSON，错误返回非零退出码。原始结果、路径与日志留在本机 `.local/`，不得直接收入公开仓库。

## MCP 配置示例

将以下模板的两个绝对路径换成本机值。配置只作为示例，不自动写入任何客户端。

```json
{
  "mcpServers": {
    "tia-guard-upstream": {
      "command": "node",
      "args": ["E:/PLC/tia-guard/adapters/tia-openness-mcp/index.js"],
      "env": { "TG_TIA_UPSTREAM_CONFIG": "E:/PLC/upstream/candidate.config.json" }
    }
  }
}
```

正常顺序：`upstream_status` → `upstream_capabilities` → `upstream_state` / `upstream_environment` → `upstream_close`。一个适配器会话复用一个引擎子进程。引擎以 `--read-only --profile full --tia-major-version 21` 启动；`full` 只是发现范围，不是写权限。任何已连接/未知连接状态、文件变化、工具契约变化或超时都会终止本会话；先排查，再显式重启，不重放操作。

退出前调用 `upstream_close` 取得清理回执。本轮覆盖正常关闭和可处理的超时/失败；宿主被强制终止时的系统级子进程托管尚未验收，不将其作为无人值守运行保证。

## 持续升级

```powershell
node probe.js --check-update
```

更新检查比对新 tag、资产来源和 GitHub 提供的 SHA-256。同 tag 的资产替换也会报错。它只返回候选元数据，不改版本清单或本地配置。

新版本在新目录准备，更新 tag/sourceCommit/资产身份及经验证的 runtime/协议身份；先跑云端契约测试，再跑本机原版 P0 和拟声明的工程/运行关卡。全部通过后单独评审默认版本提升。旧版工程写入不能通过切回旧引擎自动撤销。

设计与关卡见[总方案](../../docs/UPSTREAM-MCP-INTEGRATION.md)。真实验收与测试计划见 [TEST.md](tests/TEST.md)。

## English

This candidate integrates an unchanged upstream engine through public MCP stdio, with pinned release/source/artifact/runtime and protocol identities. It exposes six bounded discovery/diagnostic/lifecycle/update tools only. There is no arbitrary forwarding or engineering connection, edit, compile, HMI or PLC control. P0 protocol evidence does not qualify those operations.

Prepare an official checksum-verified archive with `prepare.py`, point `TG_TIA_UPSTREAM_CONFIG` to its absolute config path, and start `node index.js`. Keep raw receipts local. Use `node probe.js --check-update` to discover newer stable releases without downloading or promotion. Qualify candidates separately and retain a genuinely verified rollback version; none exists at first onboarding.

## Sources and licenses

- [TIA_Portal_Openness_MCP](https://github.com/bulaofen0036-coder/TIA_Portal_Openness_MCP), MIT, version identity pinned in [upstream.lock.json](upstream.lock.json). Source and vendor DLLs are not copied into this adapter.
- [Official TypeScript MCP SDK](https://github.com/modelcontextprotocol/typescript-sdk), Apache-2.0 for the pinned client/server 2.3.1 packages, installed through pinned npm dependencies.
- [MCP stdio transport](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports); protocol comes from the SDK rather than a new parser.
