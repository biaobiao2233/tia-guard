# #43 从零 SCL 工程集成验证 — 2026-10-12

状态：**限定范围的离线工程流程 PASS，候选未合并、未发布。**

产品源码绑定 `5aceb1e6a5192fe127bfe75906e5b8316b674d7f`，基线 main 为 `542db2f33770d145ac351e891019501bf5335b1f`。后续包装提交只更新文档、Gateway Skill 和本记录。Session 为 `integrate-43-20261012-0030`；执行资源分配和最终交接以 [#43](https://github.com/biaobiao2233/tia-guard/issues/43) 的 CLAIM/RESULT/RELEASE 为准。

完整结果的 SHA-256、输入身份、对象/变量清单、编译统计、二进制身份、工程目录摘要和清理读数见[机器可读回执](project-generation-integration-20261012.json)。其中 HTTP 状态来自实际 `Invoke-WebRequest.StatusCode` 的已完成终端调用读数，由协调者记录；工程响应 JSON 本身不含 HTTP 状态。原始报告、工程目录和回读 XML 保存在本轮专属 `E:\AGENT\Workspace\.agent-tmp\tia43-run`，未提交 Siemens DLL、工程二进制、token 或许可证。

## 已接通的产品路径

`init → 编辑 manifest/SCL → CLI 或 Gateway/MCP preview/build → 新建 WithoutUI 工程 → 依赖导入 → 完整编译诊断 → 独立回读 Compare → 关闭 → 发布新 .ap21`

公开生成输入支持 SCL。CLI 是 `build-generation`，HTTP 是 `/api/project-generation/preview` 和 `/build`，MCP 是 `preview_project_generation` 和 `build_project_generation`。失败报告可供 Agent 修源码后重新 preview/build；与原有 canonical LAD 往返接口并存。

签名来源为 A `072c14f24fff1dbce712827886c1ba23e17cf5d5`，完整编译报告绑定 C `be8453e79914457901b01eb62420d1699833e492`；四个候选工作包及原 PR 的历史结果不被此回执改写。

## 当前候选检查

| 检查 | 结果 |
| --- | --- |
| 既有契约 / Gateway Host | 152 / 76 PASS |
| Generation Spec / Engine / Verify / Integration | 84 / 80 / 63 / 30 PASS |
| Compilation | 52 PASS |
| 合计 | **537/537 PASS** |
| Schema | 既有三类 PASS；generation 3 正例、34 反例 PASS |
| V21 SDK Release 构建 | CLI、Openness、Worker、Host、GUI、generation Smoke，全部 0 警告 / 0 错误 |
| Smoke 自测 | 适配器绑定、编译未知结果和缺服务拒绝检查 PASS；自测不启动 TIA |
| 独立技术审查 | 对 `5aceb1e` **PASS**；独立 Agent 审查不等于另一 GitHub 账户批准 |

新 suites 和 generation schema 已加入 Windows CI。具体远端运行结果由集成 PR checks 记录。

## 真实 V21 检查

全部使用本轮自建 WithoutUI 实例、独占 scratch 和新输出目录。CPU 精确 profile 为 `OrderNumber:6ES7 212-1AE40-0XB0/V4.7`，回读 API 为 `21.0.0.0`。编译与回读均来自真实博图，独立期望来自冻结原始源字节。

| 用例 | 实际结果 |
| --- | --- |
| CLI 最小 Main/OB1 | 新工程发布；编译 0/0；回读 PASS |
| CLI 多块电机控制 | UDT、FB、全局 DB、实例 DB、OB1 和 5 个 IO 变量；编译 0/0；回读 PASS |
| CLI Real 返回 FC | FC＋OB1 调用＋Real 结果变量；编译 0/0；回读 PASS |
| HTTP Void FC | preview/build，HTTP 200、`published-verified`；编译 0/0；回读 PASS |
| HTTP 错误源码 | `Tag "MissingSignal" not defined.`；HTTP 422、compile 阶段、1 错误 / 0 警告、诊断完整；无输出工程 |
| HTTP 根据报错修复 | 补上 Bool `%M0.0` 声明，重新 preview；HTTP 200；编译 0/0；回读 PASS |
| HTTP 预览后输入变更 | HTTP 400，明确要求重新 preview；创建前拒绝、无输出 |
| HTTP MCP | initialize、工具发现和 `preview_project_generation` 真调用成功；仅 preview，不创建工程 |

HTTP 的实际测试使用本轮隔离 `127.0.0.1:18843 --allow-write`，没有修改 GUI 管理的默认端口。默认 GUI 单次批准和 MCP 工程失败结构的控制契约由 Host 自动化测试覆盖；此记录不声称完成真实 GUI 批准点击或真实 MCP build 的单独运行验收。

## 审查发现与修复

初始 `94e0250` 虽然能真实创建并编译最小/电机工程，但独立审查发现 XML 投影漏了实例 DB 的保留/访问属性以及命名 UDT 的展开字段；旧 PASS 不作为最终完整验证资格。

`scl-source-v2` 现在逐项验证已实证的默认 metadata，拒绝未知节点/属性/起始值，并从独立冻结 UDT 声明展开字段。复审通过完整 `D.Compare` 验算：实例保留/访问属性变化、隐藏成员或起始值变为 UNKNOWN；UDT 子字段类型变化变为 FAIL。所有公开 Builder 发布入口也必须传验证适配器，缺失时在 staging/TIA 之前拒绝。FC 对应 `Return/RET_VAL`，Void 与有返回值均纳入，Section 顺序规范化而成员顺序保留；两个真实 FC 用例均已通过。

创建时不能删除 V21 默认变量表，因此先把它改名为输入声明的第一张表；无用户表时采用预先声明的空 `_TiaGuard_Default` 并验证。导入前冻结/hash 检查保留原字节，只给临时副本添加 UTF-8 BOM。源和回读只规范化已实证的 Main 默认标题、局部变量 `#` 前缀、空声明段及格式/注释；代码 token、字面量、调用和用户属性仍比较。

本轮曾因博图 Openness 访问确认等待超时而在 create 阶段失败，无输出；用户要求继续后重新测试取得上述最终记录。没有由 Agent 改用户组、权限或永久白名单。自动审批服务曾因额度不足无法执行一次后续检查；恢复后该检查和其余实际验证已完成，不作为工程失败或安全拒绝。

## 资源与剩余目标

最终复核：自建 Host/Worker 已退出，TIA 会话已 Dispose，18843 已释放，`g-*` 构建 staging 为 0；有效工程和 readback 证据保留。原项目、原 checkout、其他 Agent 的 worktree、main 和公开 Release 未修改。

本次证明从源码创建、编译和工程回读一致性。PLCSIM 控制行为尚未验收，真实 PLC 下载/RUN/STOP/force/在线写入未提供。已只读确认本机存在 `D:\Siemens\PLCSIM\PLCSIM_V21` 和 `PLCSIMADV`；安装存在不等于型号/API/许可证或仿真行为资格。

下一阶段应为电机示例建立仅连仿真器的流程，测试启动保持、停止优先、故障闭锁、复位和再启动；必须独立分配仿真实例，并在任何下载/写入前证明目标是该实例。#43 的 LAD 生成、计时计数器、额外模块/CPU 和 HMI 仍待逐项扩展。#41 原有 GUI/Gateway patch 资格仍未开始/未接受，本次创建路径不替代它。
