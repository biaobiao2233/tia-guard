# 按软件安装适配包 / Adapter packages

一个 Release 对应同一个源码版本，每个工程软件单独下载、单独配置 MCP。无需额外安装公共核心；安装两个适配器不会让 TIA Gateway 代理 TwinCAT。本文适用于 `v0.2.0-alpha.1` 起的包；旧预览包请看对应版本说明。

One release, separate packages for each engineering application. Configure each MCP separately. Engineering applications and official licenses are prerequisites, not bundled components. Existing command/config names remain unchanged.

## 下载与完整性 / Download and integrity

从 [Releases](https://github.com/biaobiao2233/tia-guard/releases) 下载所需 ZIP、`SHA256SUMS.txt` 和 `release-manifest.json`。对照 SHA-256 后完整解压到专用短目录，例如 `E:\Tools\TIA-Guard\tia`、`E:\Tools\TIA-Guard\twincat`；不要把两个包解压到同一目录，也不要只复制 EXE。

```powershell
Get-FileHash .\tia-guard-tia-v0.2.0-alpha.1-windows-x64.zip -Algorithm SHA256
Get-FileHash .\tia-guard-twincat-v0.2.0-alpha.1-windows-x64.zip -Algorithm SHA256
```

`release-manifest.json` 记录准确源码提交、源码树和各个资产的大小/哈希。包版本为 `0.2.0-alpha.1`；现有 CLI、MCP 和 API 的内部版本可能仍报告 `0.1.0`，不能用内部协议版本代替包身份。

## TIA Portal

推荐 `tia-guard-tia-v<版本>-windows-x64.zip`，包含 GUI、CLI 和 `bridge/`。只需要 AI 接入时可改用 `tia-guard-bridge-v<版本>-windows-x64.zip`，无需两份都下载。

前提 / Requirements:

- Windows x64、.NET Framework 4.8、TIA Portal V21 和 Openness。
- 当前登录令牌具有有效 Openness 权限；工具不自动修改组或访问许可。博图出现官方访问确认时由用户处理。
- Git 仅在使用 Git 工作流时需要；从零创建工程和 MCP 不要求 Git。
- Bridge 自带 .NET 8 运行时。不包含 Siemens DLL 或工程软件安装程序。

完整包可先运行 `.\tia-guard.exe doctor`。双击 `TiaGuard.exe` 使用 GUI，或直接配置独立 MCP：

```json
{
  "mcpServers": {
    "tia-guard": {
      "command": "E:/Tools/TIA-Guard/tia/bridge/tia-guard-bridge.exe",
      "args": ["--transport", "stdio"]
    }
  }
}
```

默认只读。需要创建工程或受控修改时，在同一 `args` 中显式增加 `--allow-write`；仍须遵循 preview → 单次 token → 执行和验证。独立 Bridge 包的 EXE 位于包根目录，修改 `command` 即可。配置位置按 AI 客户端自己的说明填写；本项目不会替用户修改全局 AI 配置。

完整包的 CLI：

```powershell
.\tia-guard.exe init E:\PLC\input
New-Item -ItemType Directory -Path E:\PLC\scratch
.\tia-guard.exe build-generation E:\PLC\input --output E:\PLC\new-project --scratch E:\PLC\scratch
```

输入可编辑，输出目录必须全新，scratch 独占。完整对象范围、非默认安装路径和诊断见[工程生成](https://github.com/biaobiao2233/tia-guard/blob/main/docs/PROJECT-GENERATION.md)。原有 Git 功能见 [GUI 入门](https://github.com/biaobiao2233/tia-guard/blob/main/docs/GETTING-STARTED.md)。PLCSIM 实验代码已包含但默认关闭；完整下载 / RUN / I/O 未验收，不能把安装成功视为仿真通过。

## TwinCAT 3

下载 `tia-guard-twincat-v<版本>-windows-x64.zip`。包内已有生产 Node 依赖和具备原生绑定的 Worker，不需要 `npm install` 或重新构建本项目。

前提 / Requirements:

- Windows x64、Node.js ≥22、**.NET 8 Windows Desktop Runtime x64**。
- 已安装 TwinCAT XAE；实际验证版本为 3.1.4026.27 / `TwinCAT OS (x64)`，其他版本另行验证。
- 本机 XAE 的 DTE public assemblies，以及从本机官方 `TCatSysManager` type library 生成的 `TCatSysManagerLib.dll` interop 元数据。
- 仿真另需官方 Usermode Runtime、有效许可；NC/PTP 还需对应模块及 Tc2_MC2 3.3.75.0。只进行离线工程操作时无需开启 Runtime。

### 一次性生成本机 interop

已有本机 interop 文件可直接使用。否则，从本机 TwinCAT 安装目录或已注册 type library 的路径找到 `TCatSysManager` 类型库文件（`.tlb` 或包含该类型库的官方模块），在 **64 位 Windows PowerShell 5.1** 中执行：

```powershell
powershell.exe -NoProfile -File E:\Tools\TIA-Guard\twincat\adapters\twincat3\scripts\New-LocalInterop.ps1 `
  -TypeLibraryPath "<本机官方 TCatSysManager 类型库的完整路径>" `
  -OutputDirectory E:\PLC\tc3-interop
```

输出目录必须不存在。脚本仅导入类型元数据，生成本机 `TCatSysManagerLib.dll` / 依赖；不注册 COM、不启动 IDE、不修改路由或许可。此步骤使用 Windows PowerShell 自带 .NET Framework，不需要另装编译 SDK。不要从其他用户工程或发行包复制厂商 DLL。将下方 `TC3_INTEROP` 指向实际生成文件。

### MCP 配置

先创建本适配器独占的工作目录及其 scratch：

```powershell
New-Item -ItemType Directory -Path E:\PLC\tc3-owned\scratch
```

MCP 配置示例（路径和 build 必须符合自己的安装）：

```json
{
  "mcpServers": {
    "twincat3": {
      "command": "C:/Program Files/nodejs/node.exe",
      "args": ["E:/Tools/TIA-Guard/twincat/adapters/twincat3/mcp/index.js"],
      "env": {
        "TC3_WORKER_COMMAND": "E:/Tools/TIA-Guard/twincat/worker/TwinCat3.Worker.exe",
        "TC3_WORKER_ARGS": "[]",
        "TC3_WORKSPACE": "E:/PLC/tc3-owned",
        "TC3_SCRATCH": "E:/PLC/tc3-owned/scratch",
        "TC3_INTEROP": "E:/PLC/tc3-interop/TCatSysManagerLib.dll",
        "TC3_BUILD": "4026.27",
        "TC3_OFFLINE_WRITES": "0",
        "TC3_RUNTIME_WRITES": "0"
      }
    }
  }
}
```

第一次调用 `doctor_twincat`，核对环境、后端 capability 和不可用原因；工具能列出并不表示本机编译或仿真已验证。Doctor 不会启动 XAE 或 Runtime。

需要生成工程时，明确把 `TC3_OFFLINE_WRITES` 改为 `1` 并重新启动该 MCP；仿真还需另设 `TC3_RUNTIME_WRITES=1`，不能仅靠离线写开关进入运行。不要设置固定 `TC3_PIPE` / `TC3_IDENTITY`，它们由 MCP 每会话自动生成。输入及全新的输出目录必须遵循独占工作目录规则；示例可从包内 `examples/twincat3-generation/` 复制。

详见包内 `adapters/twincat3/docs/MCP.md`、`WORKER.md` 和 NC 示例。退出前调用 `dispose_twincat_session` 并确认清理成功。工具不会接管用户 IDE、既有 Runtime 或物理 PLC；官方许可验证码由用户本人处理。

## 从源码打包 / Build packages

在具备官方开发环境的 Windows x64 构建机上：

```powershell
.\scripts\package-windows.ps1 -Version 0.2.0-alpha.1 -OutputDirectory E:\Builds\tia-preview
.\scripts\package-twincat.ps1 -Version 0.2.0-alpha.1 -OutputDirectory E:\Builds\tc3-preview `
  -TwinCatInteropAssemblyPath E:\PLC\tc3-interop\TCatSysManagerLib.dll `
  -TwinCatVsPublicAssemblies "C:\Program Files\Beckhoff\TcXaeShell\Common7\IDE\PublicAssemblies"
```

旧 Windows 打包脚本仍生成 `tia-guard-v<版本>-windows-x64.zip`；发布时将其复制为明确标识软件的 `tia-guard-tia-v<版本>-windows-x64.zip`，内部命令名称保持兼容。TwinCAT 打包必须提供真实 native 绑定，拒绝发布只能运行 mock/纯逻辑的 Worker。

## 已知边界 / Limits

这是预览发行版。TIA 与 TwinCAT 的软件版本、项目对象和仿真能力不同；不提供跨厂商工程转换，不承诺 GUI 所有功能，不操作真实设备。完整能力与准确证据见[发布说明](releases/v0.2.0-alpha.1.md)。CODESYS 尚未发布。
