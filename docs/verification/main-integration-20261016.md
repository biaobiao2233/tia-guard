# TIA 工程生成与 PLCSIM 阶段成果整合

日期：2026-10-16，Asia/Singapore。此记录描述提交合并前的组合验证；实际合并状态以 GitHub PR 和 main 的提交为准。

## 准确来源

- main 起点：`ed6a7bfe04d0fc90c718d4eda12335e4681d47c1`，已包含 TwinCAT PR #62。
- 待整合成果：`16871b4631899824c1b65bda9c1755f58fca3f44`，`feat/43-plcsim-automation`。
- 该成果已包含从零工程候选 `ad2f399de5af386175ddf35d015cbe83ed20ce9c`（PR #52），因此在同一次整合中保留其祖先关系，无需另行复制或重写生成代码。
- 在独立 `integrate/43-main-20261016` worktree 中执行实际合并，无文本冲突。除使用入口状态说明和本记录外，TIA 产品代码、测试、打包脚本、Skill 和契约 CI 与 `16871b4` 完全相同。
- TwinCAT 适配器、示例、契约说明和 `.github/workflows/twincat3.yml` 与 main 起点完全相同；README 中 TwinCAT 的能力、许可前提及限制保留。

## 组合版本验证

| 本地检查 | 结果 |
| --- | --- |
| Contracts | 152/152 |
| Bridge Host | 175/175 |
| Compilation | 52/52 |
| Generation Spec / Engine / Verify / Integration | 84/80/63/30，全通过 |
| 合计 | **636/636** |
| 两组契约/schema 脚本 | PASS |
| Worker、Host、CLI、GUI、Openness Smoke Release 构建 | 0 错误、0 警告 |
| Smoke self-test、Git 差异格式检查 | PASS |

本地原始结果位于任务记录 `merge-main-20261016/test-results`、`test-summary.json` 和 `smoke.json`。PR CI 另行验证推送后的准确候选。TwinCAT 文件没有变化，本次未重新启动 XAE/ADS/NC 或补发其运行验收。没有新增 Openness 调用变更；既有 V21 运行证据仍限于各记录中的准确源码与环境，组合构建测试不替代工程软件的运行验收。

## 合并后的能力边界

从零工程入口提供受支持的 SCL → 新建 V21 工程 → 完整编译 → 独立回读验证 → 输出新 `.ap21`，具体范围见[生成说明](../PROJECT-GENERATION.md)。

PLCSIM 已验证原生 HTTP/MCP 实例生命周期、电源控制、SIM 表地址配置、临时副本硬件/软件编译准备和桌面应用包身份引起的 DLL 搜索兼容修复。完整下载、RUN/STOP、扫描及实际 I/O 仍未验收，保持默认关闭的实验功能；`runtimeQualified=false`、现有实例/真实 PLC 排除和独立安全配置 opt-in 保留。失败下载报告及已知 I/O 权限前提见[PLCSIM 验证记录](plcsim-native-product-20261016.md)。

本次仅整合源码，不生成或发布 Release，不修改软件许可、凭据、系统权限、用户工程或运行中的 PLC。
