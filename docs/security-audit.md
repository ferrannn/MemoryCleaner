# MemoryCleaner 安全审计与加固记录

> 审计对象：MemoryCleaner **v1.36** 源码
> 审计时间：2026-08-01
> 加固版本：**v1.3.7**（本仓库当前代码）
> 审计范围：全部业务源码（17 个文件）+ Native 声明 + manifest + git 历史

---

## 一、结论

**未发现后门或恶意代码。**

程序仅有的外联是 GitHub Releases 检查/下载（`UpdateChecker`/`DownloadMirror`，全是公开域名）、注册表自启、以及清理功能本身，**无隐藏的命令回连、数据外传、加密字符串或混淆**。git 历史 13 次提交全部是正常功能迭代，无可疑"埋点"提交。

但**自动更新机制存在高危漏洞**——攻击者只需投递一个恶意 exe 替换 GitHub release 资产（攻破仓库 / 劫持第三方镜像 / DNS 污染任一环），用户点击"更新"或开机自检到"新版本"后，就会**以管理员权限执行该 exe**（程序 `highestAvailable` 运行，见 `app.manifest`）。

---

## 二、漏洞清单（审计时状态）

### 🔴 高危

#### 1. 自动更新 = 无签名校验的 RCE（`UpdateChecker.cs` / `TrayAppContext.cs`）

- 从 `api.github.com`（或镜像 `gh-api.p3terx.com`）拉最新版信息，**没有校验发布者身份**——只要 API 返回"版本号更高、带 .exe 资产"的 release 就信。
- 下载的 `.exe` **只校验大小**（`asset.Size`），不校验 SHA-256、不做 Authenticode 签名。
- 任何能投递该 release 的第三方镜像（代码明文列出 `gh-proxy.com`、`ghfast.top`、`ghproxy.net`、`gh.llkk.cc`）都能被换成恶意 exe。
- 下载的 exe 通过 `powershell.exe -ExecutionPolicy Bypass -WindowStyle Hidden` 启动的自替换脚本执行。
- manifest 是 `highestAvailable` → 整个链路 = **"HTTPS 上的任意文件下载 → 管理员权限执行"**，典型供应链攻击形态。

**修复**：见第三节「本次加固」。

#### 2. 更新脚本落 %TEMP% + 直接执行 = 本地提权 / 代码注入（`UpdateChecker.cs`）

```csharp
string ps1 = Path.Combine(Path.GetTempPath(), "mc_update.ps1");   // 固定文件名
await File.WriteAllTextAsync(ps1, ...);
Process.Start(new ProcessStartInfo {
    FileName = "powershell.exe",
    Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + ps1 + "\"", ... });
```

- 脚本写入 `%TEMP%\mc_update.ps1`，**文件名固定**。多用户系统里低权限用户可**预置同名文件**，管理员更新时 powershell 执行的就是攻击者的脚本。
- 更新会**静默创建** `exe.bak`（原 exe 备份）留在程序目录——可被利用的持久化点。

**修复**：彻底不落盘，见第三节。

### 🟠 中危

#### 3. 更新机制对路径的注入防护不完整（`UpdateChecker.cs`）

只拒绝单引号 `'`，但 PS1 里路径是 `'{path}'` 单引号包裹——**反引号、`$`、换行、`$( )`、双引号**都未被拦截，会直接进入脚本字符串，构成 PowerShell 注入。

**修复**：路径改环境变量传递，脚本体内零插值（见第三节）。

#### 4. 信任第三方加速镜像下载可执行文件

`DownloadMirror.cs` 让用户（或自动测速）选择第三方加速源下载**将要被执行的 exe**。这些源本身是不可信代理，结合漏洞 1 等于默认允许从不受信任的来源更新到管理员权限。

**修复**：校验文件只走 GitHub 直连，镜像仅加速 exe 载荷（见第三节）。

#### 5. 便携模式 = 低权限用户向管理员进程注入配置（`AppPaths.cs` + `TrayAppContext.cs`）

便携模式下配置写在 exe 同目录，`UpdateChecker` 会把**该目录里的 `*.new` 自动替换 exe**。若程序安装在人人可写的目录，低权限用户可预置 `config.json`（开启 `KillHighUsageProcesses` + 低阈值 → 管理员进程杀指定进程；或 `CheckUpdateOnStartup=true` 配合镜像链路执行恶意 exe）。

**状态**：**未在本轮修复**（见第五节「遗留项」）。

#### 6. 杀进程功能本身：进程名误杀 + 保护名单可绕过（`ProcessKiller.cs`）

- 按**进程名**匹配保护名单。恶意进程改名成 `explorer`/`svchost` 会被"保护"——保护逻辑反过来可被绕过。
- 阈值 8 GB、白名单默认空，配合漏洞 5 的配置注入，可让管理员进程结束任意进程。
- 正面：默认关闭、需确认对话框、UI 可预览——这部分做的是对的。

**状态**：**未在本轮修复**（见第五节）。

### 🟡 低危 / 健壮性

- **热键注册无输入校验**（`HotkeyWindow.cs`）：`HotkeyValue` 直接来自 config，恶意 config 可构造 `MOD_WIN` 等组合，注册系统级快捷键——DoS 骚扰，无提权。
- **下载目标符号链接**（`UpdateChecker.cs`）：`File.Create(currentExe + ".new")` 若不防符号链接，低权限用户可预置指向任意文件的链接，用下载内容覆盖任意文件。
- **`SystemCacheCleaner` 激进模式**：完整清空会释放系统文件缓存，对数据库/IDE 等有性能影响（温和模式是默认，此点已处理得当）。

---

## 三、本次加固（v1.3.7）

### 3.1 加固概览

| 漏洞 | 修复 |
|---|---|
| exe 只比大小，无内容校验 → 供应链 RCE | 每个 release 附带 `.sha256` 文件，SHA-256 强制校验（fail-closed） |
| 镜像源可篡改将被执行的 exe | `.sha256` 只走 GitHub 直连，绝不经镜像；exe 仍可镜像加速 |
| 脚本落 `%TEMP%` 固定名 → 本地提权 | 彻底不落脚本文件，`-EncodedCommand` + 环境变量传路径 |
| 路径含 `$`/反引号 → PowerShell 注入 | 路径全走环境变量，脚本体内零插值；单引号路径拦截移除 |

### 3.2 新数据流

```
TrayAppContext.CheckForUpdateAsync
  1. release = GetLatestReleaseAsync()                    （不变，API + API 镜像）
  2. IsNewer(release)                                     （不变）
  3. (exe, checksum) = PickUpdateAssets(release)          【新】exe 资产 + .sha256 资产
  4. expectedSha256 = FetchChecksumAsync(checksum, exe.Name)
       - 用 checksumAsset.DownloadUrl 原样直连（github.com release 资产）
       - TryParse 解析，校验文件名 == exe.Name
       - 任何失败 → null（fail-closed）
  5. null → 提示"无法获取官方 SHA-256 校验文件，已禁用自动更新" + 打开发布页；return
  6. 有值 → 现有"发现新版本"提示 → UpdateDownloadForm(exe, expectedSha256, …)
  7. 表单：选镜像 → 下载 → 边流式计算 SHA-256 → 比对 → -EncodedCommand 自替换
  8. ExitThread()                                         （不变）
```

**信任锚**：`checksumAsset.DownloadUrl`（GitHub release 资产直链）。`FetchChecksumAsync` 绝不调用 `mirror.Build`，被劫持的镜像前缀永远喂不进绑定哈希。镜像仅加速 exe 载荷本身。

### 3.3 改动文件

| 文件 | 改动 |
|---|---|
| `src/MemoryCleaner/Core/ChecksumVerifier.cs` | **新增**。纯逻辑：`IsValidSha256Hex` / `TryParse` / `ComputeSha256` / `ConstantTimeEquals`，全部 fail-closed |
| `src/MemoryCleaner/Core/UpdateChecker.cs` | 新增 `PickChecksumAsset` / `PickUpdateAssets` / `FetchChecksumAsync`；重写 `DownloadAndApplyAsync`（`expectedSha256` 参数、`IncrementalHash` 流式校验、`-EncodedCommand` + 环境变量自替换） |
| `src/MemoryCleaner/Core/DownloadMirror.cs` | 文档注释：镜像前缀仅用于 exe 载荷，校验和永远直连 |
| `src/MemoryCleaner/UI/UpdateDownloadForm.cs` | 构造函数接收校验和；null 时禁用下载 + 状态提示；`StartAsync` 加防御纵深 |
| `src/MemoryCleaner/TrayAppContext.cs` | `CheckForUpdateAsync` 按新数据流重写，fail-closed |
| `src/MemoryCleaner/MemoryCleaner.csproj` | 版本 1.3.6 → 1.3.7；新增 `InternalsVisibleTo` |
| `tests/MemoryCleaner.Core.Tests/` | **新增** xunit 测试项目（37 个用例，全绿） |
| `tools/make-sha256.ps1` | **新增**发布辅助脚本，生成 `.sha256` 侧车文件 |

### 3.4 fail-closed 语义（全部拒绝更新）

1. release 无 `.sha256` 资产
2. 校验文件拉取 HTTP 错误 / 超时 / 重定向到错误页
3. 内容空 / 损坏 / 仅 BOM
4. 无 64-hex 行 / 文件名不匹配 / 多个裸哈希
5. 下载后哈希 ≠ 期望值 → 删 `.new` + 明确报错
6. 入口校验 `expectedSha256` 非 64-hex → 不开始下载

### 3.5 审查中发现并修复的真实 bug

**`-WindowStyle Hidden` 命令行参数会让 `-EncodedCommand` 脚本静默不执行。**

- 实测（Win11 + Windows PowerShell 5.1，用临时 C# 程序逐字复刻生产启动代码）：`Arguments` 里带 `-WindowStyle Hidden` → 子进程退出码 -1、替换不执行、无任何输出；去掉该参数（保留 `CreateNoWindow` + `WindowStyle` 属性）→ 替换/备份/重启全部成功。
- 影响：旧实现下更新会**假成功**——用户看到"更新完成"但 exe 未替换。
- 已修复：`Arguments` 只保留 `-NoProfile -ExecutionPolicy Bypass -EncodedCommand {cmd}`，隐藏窗口靠 `CreateNoWindow=true` + `WindowStyle=Hidden` 属性。

---

## 四、验证

- ✅ `dotnet build MemoryCleaner.sln -c Release`：0 警告 0 错误
- ✅ `dotnet test tests/MemoryCleaner.Core.Tests`：37/37 全绿
  - `ChecksumVerifierTests`：TryParse 各种格式/失败、ComputeSha256 已知向量、ConstantTimeEquals
  - `UpdateAssetTests`：PickChecksumAsset / PickUpdateAssets 资产配对
- ✅ 自替换机制实测（临时 C# 程序复刻生产启动代码）：含空格 & 特殊字符路径下替换、备份、`.new` 清理全部通过
- ✅ `make-sha256.ps1` 生成的侧车文件哈希与 `Get-FileHash` 一致、格式为 `TryParse` 接受

---

## 五、遗留项（未在本轮修复，建议下一轮处理）

| # | 问题 | 风险 | 建议 |
|---|---|---|---|
| 1 | 便携模式 / 可写安装目录下，低权限用户可注入 `config.json` 或预置 `*.new` | 中 | 配置目录收紧 ACL；`File.Create` 防符号链接 |
| 2 | 杀进程按进程名匹配，保护名单可被同名恶意进程绕过 | 中 | 保护名单加 PID + 父进程校验；或退出"自动结束进程"功能 |
| 3 | 自替换后 `Start-Process` 重启失败被 `try{}catch{}` 吞掉，用户看到"更新完成"但程序没起来 | 低 | 重启失败时写日志或弹提示 |
| 4 | 热键 `HotkeyValue` 来自 config 无输入校验 | 低 | 钳制修饰键组合，禁止 `MOD_WIN` |

---

## 六、发布要求（重要）

从 v1.3.7 起，**每个 GitHub release 必须附带 `.sha256` 侧车文件**，否则所有客户端自动更新被冻结（fail-closed）。

```powershell
# 对每个要上传的产物执行一次
powershell -ExecutionPolicy Bypass -File .\tools\make-sha256.ps1 -Path .\publish-lean\MemoryCleaner.exe
```

把生成的 `MemoryCleaner.exe.sha256` 一起传到 GitHub Release。侧车文件格式：小写 64 位十六进制 + 双空格 + 文件名，ASCII 无 BOM 无换行——与 `ChecksumVerifier.TryParse` 接受格式一致。

---

## 七、风险 / UX 行为变化（诚实说明）

- **最大变化**：GitHub 直连不可达的网络（恰是当初加镜像服务的受众）将无法自动更新——校验和拿不到即拒绝，提示"请手动访问发布页下载"。这是接受的取舍：镜像不能成为完整性锚点。
- **旧客户端无感**：已发 v1.3.6 走旧的大小校验升级到 1.3.7，兼容。
- **启动自动检查（manual:false）**：有新版本但校验和取不到 → 静默跳过。fail-closed，UX 更合理。
- **延迟**：提示"发现新版本"前多一次小的直连请求（典型 <1s，GitHub 直连挂时最坏 15s——此时反正拒绝更新）。
- **路径边界**：含单引号/引号的路径从"拒绝"变为"允许"（环境变量安全携带）；不再写临时 `.ps1`；`.bak` 备份保留。
- **镜像被篡改**：现在得到清晰"SHA-256 校验不通过…换源重试"，镜像只做速度特性，永非信任边界。
