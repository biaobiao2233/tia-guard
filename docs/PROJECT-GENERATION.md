# 从零创建 PLC 工程

此入口将 `project-generation.json` 和 SCL 源文件转换成一个新的 TIA Portal V21 工程，不需要已有 `.ap21` 或二进制模板。CLI、HTTP 和 MCP 调用同一个工作流：检查并冻结输入 → 创建 CPU → 按依赖导入 → 编译 → 独立回读比较 → 关闭工程后发布。失败返回阶段和诊断，便于 Agent 修改源码并重新调用。

此功能通过仓库源码提供；已发布预览包不自动包含它。真实环境和具体源码身份见[集成验证记录](verification/project-generation-integration-20261012.md)。

## 环境和输入

需要 Windows、TIA Portal V21、对应 Openness SDK 和当前用户的 Openness 访问权限。首次连接或可执行文件变化时，博图可能弹出 Openness 访问确认；需要用户允许本次访问，等待超时会失败。项目不会自动更改用户组或权限。源码构建示例：

```powershell
dotnet build src/TiaGuard.Cli -c Release -p:TiaPortalPublicApi="D:\Siemens\Portal V21\PublicAPI\V21\net48"
dotnet build src/TiaGuard.Bridge.Host -c Release -p:TiaPortalPublicApi="D:\Siemens\Portal V21\PublicAPI\V21\net48"
```

输入遵循[生成契约](contracts/project-generation-v1.md)，所有源文件都必须在 manifest 中声明。当前 profile 是单 PLC、`OrderNumber:6ES7 212-1AE40-0XB0/V4.7`，支持 SCL 的 Main/OB1、FC（包括 Void 和有返回值）、FB、全局 DB、实例 DB、UDT 和根 PLC 变量表。编号由 manifest 的 artifact ID 确定，Main 为 OB1，其余块从 100 开始按种类分配。

输入、输出、scratch 使用三个互不嵌套的绝对目录。输出必须不存在；scratch 必须已存在且专属当前任务。源文件始终保持原样，TIA 所需的 UTF-8 BOM 只写到临时导入副本。

## CLI

```powershell
# 在新的目录生成 manifest 和 Main.scl，然后编辑源码与声明。
tia-guard.exe init E:\PLC\input
New-Item -ItemType Directory -Path E:\PLC\scratch

tia-guard.exe build-generation E:\PLC\input `
  --output E:\PLC\new-project `
  --scratch E:\PLC\scratch `
  --api "D:\Siemens\Portal V21\PublicAPI\V21\net48"
```

`--api` 可省略以使用默认安装路径。参数顺序为输入、`--output`、`--scratch`、可选 `--api`。成功返回 JSON、退出码 0 和新 `.ap21` 路径；失败退出码 5，检查 `Phase`、`Failures`、`Compile.FullResult`、`Verification.Findings` 及 `CleanupFailures`。

现成的[多块电机示例](../examples/project-generation-v1/programs/scl-multiblock/project-generation.json)包含 UDT、FB、全局 DB、实例 DB、OB1 和 5 个 IO 变量；[FC 返回示例](../examples/project-generation-v1/specs/fc-return/project-generation.json)包含有返回值的 FC 和 OB1 调用。示例文件中的上游版权和许可证继续适用。

## HTTP 和 MCP

GUI 管理的 Gateway 默认要求一次确认；独立 headless 写模式用 `--allow-write`。两种模式都必须先 preview，后 build。创建前断开 Gateway 已绑定的工程。

- HTTP：`POST /api/project-generation/preview`、`POST /api/project-generation/build`。
- MCP：`preview_project_generation`、`build_project_generation`；stdio 默认仅提供 preview，启用写模式后提供 build。

请求字段为 `sourceDirectory`、`outputDirectory`、`ownedScratchRoot`、可选 `publicApiDirectory`。build 还需要 preview 返回的 `safetyToken`。Token 绑定输入全部字节、请求路径和 Worker 身份，只能使用一次；源码修改后必须重新 preview。

HTTP 完整成功返回 200；创建工作流失败返回 422 和完整报告，包括部分 preflight 失败，具体看 `Phase`。Gateway 入口的请求、token 或会话校验拒绝返回 400。MCP 的工程失败设置 `isError=true`。Agent 可读取诊断、修正源码、申请新的 preview，然后构建到另一个新目录。完整请求示例见[Gateway 指南](TIA-AI-BRIDGE.md#从零创建离线-plc-工程)。

## 验证和当前边界

编译必须成功且完整收集诊断。回读比较独立冻结输入中的工程/CPU 身份、完整对象集合、编号、全部 SCL 非注释 token、接口、UDT 展开字段、实例目标、变量类型和地址。`scl-source-v2` 仅接受已实证的 V21 默认标题、本地变量 `#` 前缀、空声明段和接口默认属性；未知 XML 节点、访问属性、保留属性或起始值阻止完整 PASS。

当前初版未资格验证 RETAIN、显式声明起始值、复杂存储属性、任意系统库类型、额外硬件模块和其他 OB 类；部分输入在 preflight 拒绝，其他未知回读内容返回 UNKNOWN。同一源文件生成超过 99 个同类块时可能遇到临时自动编号碰撞，当前会安全失败。LAD/SimaticML 不属于此 SCL 新建入口，应使用原有往返流程的受支持子集。

成功证明工程可创建、编译并与输入一致。PLC 扫描周期中的控制行为需要单独的 PLCSIM/现场测试；此入口没有下载、RUN/STOP、force 或在线写入功能。它也没有为原有 GUI patch 流程的 Issue #41 补发验收。
