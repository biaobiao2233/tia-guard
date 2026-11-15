# 上游 MCP 接入与升级

方向确认：2026-11-14。适用于当前和未来的第三方工程引擎接入。这是实现约束和分阶段验收方案，不是完整博途/HMI/PLCSIM 能力已经交付的声明。

## 目标与分层

Agent 优先使用成熟 MCP 能力；CLI 保留环境诊断、批处理、回归入口。第三方引擎以独立进程运行，TIA-Guard 通过公开 MCP 协议调用，不复制引擎实现，不要求新增 HTTP Gateway。

| 层 | 责任 | 不应承担 |
|---|---|---|
| 第三方工程引擎 | Openness、工程对象、厂商编译/HMI 接口 | TIA-Guard 的验收判定和软件 CPU 归属规则 |
| 薄适配层 | 进程生命周期、MCP 协议、版本与能力检测、参数/结果兼容、准确目标约束 | 再实现一套编译器或厂商对象模型 |
| TIA-Guard 验证/仿真 | 独立回读、证据、专用软件 CPU、Factory I/O 工艺验收 | 借上游工具数或连接成功提高支持等级 |

工程写入保持一个所属明确的通道。同一工程不能同时由旧 Worker 与上游引擎写入。只读查询、编译、工程修改、下载、运行命令分别授权和验收；不能把上游全部工具透传为默认可用能力。

## 上游选择

首个候选：[bulaofen0036-coder/TIA_Portal_Openness_MCP](https://github.com/bulaofen0036-coder/TIA_Portal_Openness_MCP)，MIT。2026-11-14 查到稳定版 [v2.7.11](https://github.com/bulaofen0036-coder/TIA_Portal_Openness_MCP/releases/tag/v2.7.11)，包含服务端 `--read-only`。它仍允许连接、打开/关闭工程及导出，因此只读开关并不替代本项目的实例归属和文件路径约束。`Doctor` 必须明确 `fix=false`。

默认采用原版资产/源码。准确 tag、提交和资产校验值由适配器的版本清单维护，不散落硬编码。需要改上游时只保留小补丁和原因、对应版本、测试及移除条件，争取上游接收后删除补丁。

其他上游（如 Czarnak/tia-portal-mcp）按同样规则评估。MCP 协议相同，不表示工程工具名称和参数相同；兼容逻辑集中在各自适配器，不强迫所有平台共用厂商对象模型。

## 本轮范围与关卡

| 阶段 | 可执行范围 | 必须取得的证据 |
|---|---|---|
| P0 | 原版引擎启动/停止、MCP 初始化、工具发现、无连接的状态与诊断 | 正确版本/文件身份、真实协议结果、无新增 TIA 会话、清理结果；单元测试另列 |
| P1 | 显式创建的独立 TIA 会话和测试工程读取、导出 | 精确工程路径/归属、实际块/变量、目标与导出路径限制、保存关闭重开回读 |
| P2 | 自有副本新建/修改/编译、HMI 工程能力 | 错误源码确实失败、修复后真实编译、独立回读、异常和退出清理 |
| P3 | 对接已有软件 CPU/Factory I/O 后端 | 新建归属、完整下载/RUN、真实传感器和输出、停止/复位/异常测试 |

先完成 P0，再按依赖推进。P0 通过不能标为 P1/P2/P3 通过。未接入或受阻的操作不在默认工具列表出现；不得以 mock、GUI 包装或输出预期值代替真实结果。现有产线暂停记录和其他对话实例继续受保护。

## 升级与回退

1. **发现候选**：只查询上游公开元数据，不自动执行下载程序、修改用户配置或替换引擎。记录新 tag、准确源提交、资产 URL/校验值及变更说明。
2. **隔离准备**：候选和已验证版本分目录。校验资产及入口文件身份，不覆盖正在使用的版本。仅记录模板配置，机器路径和日志留本机。
3. **兼容检查**：比较支持的 MCP 初始化版本、工具 schema 和结果结构。必需字段删除、类型/默认值变化、未知错误或不完整结果应拒绝提升；新工具默认不获授权。新增可选参数仍需行为检查。
4. **真实验证**：在本轮自有资源上执行与拟声明范围匹配的关卡。离线测试可在 CI 运行；厂商原生检查在具备软件和许可的本机运行。
5. **显式提升**：提交版本清单、准确证据和回退版本。用户级 MCP 配置和发布包升级是独立动作，不由发现候选自动触发。
6. **回退**：停止且仅停止适配器拥有的引擎进程，重新选用旧版本。工程文件回退依赖事先保存的副本，不能假设旧引擎能撤销新引擎的工程写入。

版本状态至少区分 `candidate`、`protocol-verified`、`engineering-verified` 和 `runtime-verified`。回退版本的存在不等于已验证；首次接入如果尚无历史已验证版，必须明确为空。恢复绑定需要新会话，不复用旧 PID 或句柄。

## English

Prefer MCP for agent access and retain CLI for diagnostics, batch work and regression. Run third-party engineering engines in independent processes, unchanged by default. Integrate through public MCP contracts; keep compatibility in a thin adapter and retain independent verification/simulation in TIA-Guard. Serialize every write channel to the same owned project.

The first candidate is the MIT-licensed TIA_Portal_Openness_MCP. Its `--read-only` flag still allows connection, project lifecycle and export; the adapter must restrict those effects separately and call Doctor with `fix=false`.

Qualify stages separately: P0 real protocol/discovery without starting TIA; P1 owned-project reads/exports; P2 real creation/edit/compile/HMI readback; P3 software-CPU and Factory I/O execution. Mocks and protocol success do not qualify engineering or runtime behavior.

Keep exact release/source/artifact identities in one version manifest. Discover upgrades without replacing running engines. Stage candidates separately, check protocol/schema/result/error drift, run native acceptance for the intended scope, then explicitly promote and retain a verified rollback version. No prior rollback version must be represented as absent. Engine rollback does not undo project writes; keep project copies and rebind with fresh ownership. Track necessary upstream patches separately, with an exit condition.
