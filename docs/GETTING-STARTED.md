# 快速开始

**中文** | [English](GETTING-STARTED.en.md)

[返回首页](https://github.com/biaobiao2233/tia-guard) · [AI Gateway](TIA-AI-BRIDGE.md)

## 下载与前提

从 [Releases](https://github.com/biaobiao2233/tia-guard/releases) 下载 `tia-guard-v0.1.0-prealpha.1-windows-x64.zip`。这是实验性预览版，适合在自己的离线工程副本上试用；请先阅读同一发行页的验证范围。

需要 Windows x64、.NET Framework 4.8、已安装 TIA Portal V21 与 Openness、Git for Windows，以及当前登录令牌有效的 **Siemens TIA Openness** 组权限。Gateway 已附带 .NET 8 运行时。软件不附带 Siemens DLL、许可证或 TIA 安装程序。

1. 解压整个 ZIP 到短路径，例如 `C:\TiaGuard`。不要仅拖出 EXE。
2. 在该目录运行 `.\tia-guard.exe doctor`，查看本机前提。
3. 双击 `TiaGuard.exe`。GUI 当前为中文；本说明提供语言切换。
4. 初次执行 Openness 操作时，如出现 TIA 官方访问确认，请核对程序后按 Siemens 的访问流程处理。

可用 PowerShell 检查下载文件的 SHA-256，并与发行页的 `SHA256SUMS.txt` 比较：

```powershell
Get-FileHash .\tia-guard-v0.1.0-prealpha.1-windows-x64.zip -Algorithm SHA256
```

## GitHub → TIA

输入包含 TIA-Guard canonical source 的 Git 仓库 URL → clone/pull → 选择工程 → 选择新的输出目录 → 重建、编译及受支持语义校验。普通只有 `.ap21` 的仓库不能替代 canonical source。

支持 `tia-projects/<工程槽位>/tia-source/` 多工程布局和旧的 `tia-source/` 单工程布局。输出目录应全新，原工程不会被覆盖。语义 PASS 不代表二进制文件必须相同。

## TIA → GitHub

选择自己的 `.ap21` → 输入有写权限的 Git 仓库 URL → 选择工程槽位或新增槽位 → 导出并验证 → commit/push。程序复用系统 Git Credential Manager 或 SSH；先确保普通 Git 能访问目标仓库。

一次发布只更新所选槽位的 `tia-source/`。派生 `ai/` 需要单独执行 `ai-view`，当前 GUI 不会自动提交它。公开工程前自行检查名称、注释和 XML 中的信息。

## AI Agent

GUI 会启动 `127.0.0.1:18761` 的本机 Gateway。配套 `skill.zip` 中的 `tia-guard-gateway/SKILL.md` 可用于本机 Agent；安装方式以所用 Agent 的 Skill 功能为准。Skill 本身不会建立云端到本机的网络通道。

Agent 先读取 capabilities 与 OpenAPI。HTTP 工程修改需要精确 preview、GUI 单次确认及后续验证。详见 [Gateway 指南](TIA-AI-BRIDGE.md)。

## 常见问题

| 现象 | 处理 |
| --- | --- |
| Openness 权限不足 | 检查组成员资格；新增权限后注销并重新登录，再运行 doctor |
| TIA / PublicAPI 未找到 | 确认 V21 与 Openness 正确安装；不要从他处复制 Siemens DLL 到软件目录 |
| Git 认证失败 | 先使用普通 Git 完成认证并核对目标仓库权限 |
| 端口占用 | 检查是否已有 TIA-Guard/Gateway 实例；不要擅自结束其他程序 |
| Verify 为 blocked | 阅读诊断；不支持内容或不完整捕获不能视为通过 |
| TIA 路径错误 | 使用较短安装/输出目录，并查看具体诊断 |

## 当前范围

仅覆盖已验证的 V21/S7-1200 单 PLC、Main OB1 有限 LAD 与根变量表子集。不支持在线 PLC 操作。历史真实 TIA 证据对应其注明版本，最新集成 GUI/Gateway 的完整工程流程仍需单独验收。请勿将预览版当作稳定生产工具。
