# TIA AI Gateway

**中文** | [English](TIA-AI-BRIDGE.en.md)

[首页](https://github.com/biaobiao2233/tia-guard) · [快速开始](GETTING-STARTED.md)

## 当前集成状态

当前源码包括 GUI 自动启动的本机 API Gateway、AI Engineering v2 上下文、结构化 patch preview/apply、逐次 GUI 确认，以及隔离打包的 worker。精确版本检查见[集成验证记录](https://github.com/biaobiao2233/tia-guard/blob/main/docs/verification/publication-20261001.md)。下文的旧版变量及发布接口仍支持，但不代表全部 v2 API。

当前 JSON patch 为 `upsert_tag` 和 `replace_output_condition`。先查询 `/capabilities` 与 `/openapi.json`。默认 HTTP apply 需要 GUI 对相应 preview 进行确认；默认 MCP 工具只读。显式 headless `--allow-write` 跳过 GUI 确认，但仍保留单次 token 和离线副本验证。

源码公开、二进制发行和真实 TIA 工程验收是独立状态。

## 本机 Agent 使用流程

打开 TIA-Guard 后，本机 Gateway 自动在 `127.0.0.1:18761` 就绪。普通用户不需要复制地址、选择传输方式或手工启动 Bridge。

配套 Skill 先读 capabilities，再读派生 AI Engineering 视图。真正写入前，窗口只确认这一次修改。原始工程不会保存；成功修改只落在临时离线副本，并通过 Compile、round-trip Verify 与 AI 语义检查。

本机 Cursor、Codex 等桌面 Agent 可以连接。纯云端聊天页面没有到本机的通道，Skill 不会自动建立网络连接。

## 设计与架构

Bridge 在外部 AI Agent 与本机 TIA Portal V21 之间提供供应商中立的工程接口。两个层面保持分离：

- **AI 可读视图**：从验证过的 `tia-source/` 确定性派生的工程上下文。
- **TIA AI Bridge**：对本机 TIA 工程会话的显式访问。

v2 将已绑定离线工程导出到 Bridge 拥有的临时 canonical workspace，复用 AI Engineering v2 renderer，不新增第二套 LAD parser。`ai/` 是派生视图。结构化 patch 是命令，依据视图验证、预览、绑定单次 token，只应用到临时离线副本；失败则恢复该副本。`verifyVerdict=pass` 表示 RoundTripVerifier 接受了由导出源重建的工程。Patch JSON 不会变成构建事实来源。

两层只通过临时 canonical export 交汇，不能替代权威 round-trip source。

设计研究参考了两个 MIT 许可项目：
- `Czarnak/tia-portal-mcp`：持久 net48 Openness worker、精确工程绑定、目标不明时拒绝操作及写入保护。
- `bulaofen0036-coder/TIA_Portal_Openness_MCP`：广泛 TIA 能力、stdio/HTTP、声明式流程、LAD/SCL、硬件/HMI 和 VCI/Git。

TIA-Guard 未整仓复制它们，使用自己的契约和范围边界。

调用链：外部 AI / MCP / HTTP → .NET 8 `tia-guard-bridge` host → 持久 worker protocol → x64 .NET Framework 4.8 `TiaGuard.Bridge.Worker` → `TiaGuard.Openness` → TIA V21。

Host 不加载 Siemens 程序集。发行包将 net48 worker 放在独立 `worker/`，避免探测到 self-contained .NET 8 host 的运行时程序集。开发构建允许旁置 worker 作为回退。

默认只读，支持工程发现、精确绑定、元数据及有限快照。旧版变量/发布写工具需显式 `--allow-write`，且只作用于临时离线副本：根变量 upsert 先在内存进行，发布到新目录另需独立 preview。发布先编译，用 SaveAs 写全新目录，再以新的临时副本重新打开并核对工程 `contentId`。

任何模式都不提供 PLC 下载、启停、force、在线变量写入、Safety、附着用户工程的写入/覆盖保存、归档或原位置 SaveAs。

## 精确工程绑定

- 恰好一个 V21 进程有已打开工程时，`connect_project` 可省略 PID。
- 多个候选时必须传精确进程 ID。
- 绑定后拒绝切换 PID，直到显式 disconnect。
- 工程身份变化会使会话失效，不自动猜测。
- 离线检查仅打开临时副本，输入 `.ap21` 不直接打开。

## MCP stdio

```powershell
tia-guard-bridge.exe --transport stdio
```

基础工具：`bridge_doctor`、`list_open_projects`、`connect_project`、`open_offline_project`、`disconnect_project`、`get_bridge_state`、`get_project`、`get_project_snapshot`。

AI v2 查询工具：`get_ai_project_context`、`get_program_graph`、`get_network`、`where_used`、`refresh_ai_context`。

写模式额外注册 `preview_tag_upsert` / `apply_tag_upsert`、`preview_patch` / `apply_patch`、`preview_publish_modified_copy` / `apply_publish_modified_copy`。默认只读模式不注册写工具。

## 本机 HTTP

```powershell
tia-guard-bridge.exe --transport http --port 18761
```

只绑定 loopback：`http://127.0.0.1:18761`。Streamable HTTP MCP 端点为 `/mcp`；普通 JSON API 可供不支持 MCP 的 Agent 使用。

| 接口 | 用途 |
| --- | --- |
| `GET /health`, `GET /capabilities`, `GET /openapi.json` | 服务与能力发现 |
| `GET /api/v1/gateway/status` | GUI/Gateway/确认状态 |
| `GET /api/v1/projects`, `POST /api/v1/connect` | 发现并精确绑定工程 |
| `POST /api/v1/open-offline`, `POST /api/v1/disconnect` | 离线副本与断开 |
| `GET /api/v1/state`, `project`, `project/snapshot` | 会话与工程查询 |
| `GET /api/v1/ai/context`, `program-graph`, `network`, `where-used` | AI 工程查询 |
| `POST /api/v1/ai/patches/preview`, `apply` | v2 结构化 patch |
| `POST /api/v1/tags/preview-upsert`, `apply-upsert` | 旧版变量操作；需写模式 |
| `POST /api/v1/project/preview-publish`, `apply-publish` | 旧版副本发布；需写模式 |

## 受控写入契约

变量请求 → 预览当前根变量状态 → 将精确绑定、请求和状态计算 hash → 随机单次 token（10 分钟有效）→ 同一请求与 token 申请 apply → Siemens 修改之前消耗 token → 修改临时副本内存 → 回读并精确核对。

Token 重放、请求不同、工程绑定不同或预览后变量状态变化均被拒绝。第一次 apply 尝试即消耗 token，包括找到 token 后验证失败的情况。

变量操作只接受根变量表名和精确变量名，拒绝路径；可新增变量或更新类型、逻辑地址。此旧版操作不保存临时工程，断开/关闭即丢弃修改。

**发布修改副本**另需 token：要求完整快照，绑定精确工程/请求/content 状态，保留工程名，SaveAs 前编译，拒绝改名发布及已有目标目录；SaveAs 后原绑定失效，通过新的临时副本重新打开 `.ap21` 并核对确定性 `contentId`。需要另一份副本时选择新的父目录。附着用户工程的写入权限仍属于后续范围，在线 PLC 控制与工程编辑分离。

v2 patch 成功需编译、确定性导出、重建、round-trip Verify 与 AI 语义检查，且只保存临时离线副本。失败恢复副本；恢复失败明确报告 `rollback_failed`。原工程不会保存。
