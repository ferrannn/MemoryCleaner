# MemoryCleaner

轻量级 **Windows 系统托盘** 内存清理工具：可自定义定时 / 阈值触发，支持多种清理方式，单文件运行、占用极低。

![dotnet](https://img.shields.io/badge/.NET-8.0-512BD4) ![platform](https://img.shields.io/badge/platform-Windows-0078D6) ![license](https://img.shields.io/badge/license-MIT-green)

## ✨ 功能

- **三种清理方式**（可自由组合）
  - 清空工作集（Working Set）— `EmptyWorkingSet`，安全、最常用
  - 清空系统缓存 / 待机列表（Standby / Modified List）— 需管理员
  - 结束高占用进程 — 默认关闭，强白名单保护
- **三种触发方式**（可任意组合）
  - 阈值触发：内存占用超过 X% 自动清理
  - 固定间隔：每隔 N 分钟清理
  - 定时点：每天 / 每周指定时间清理
- **系统托盘常驻**
  - 图标实时显示当前内存占用百分比（按占用率变色）
  - 右键菜单：立即清理 / 暂停 / 设置 / 开机自启 / 退出
  - 清理后气泡提示释放量
- **轻量化**
  - 零第三方依赖（纯 Win32 P/Invoke + 内置库）
  - 框架依赖单文件 **< 0.2 MB**，自身内存占用 < 50 MB
- **安全可靠**
  - 单实例运行、防重入、两次清理最小间隔
  - 系统关键进程绝不清理 / 结束
  - 配置 JSON 存于 `%AppData%\MemoryCleaner\config.json`，热重载

## 🚀 快速开始

### 方式一：下载 Release（推荐普通用户）

从 [Releases](../../releases) 下载：

| 文件 | 体积 | 说明 |
|---|---|---|
| `MemoryCleaner.exe`（自包含） | ~68 MB | **免安装**，双击即用，无需 .NET |
| `MemoryCleaner-fd.exe`（框架依赖） | ~0.2 MB | 体积极小，需先装 [.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0) |

> 💡 想启用“清理系统缓存”：右键 exe → **以管理员身份运行**。

### 方式二：自行编译

```bash
git clone https://github.com/<你的用户名>/MemoryCleaner.git
cd MemoryCleaner

# 自包含单文件（免安装）
dotnet publish src/MemoryCleaner -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o publish

# 框架依赖单文件（极小）
dotnet publish src/MemoryCleaner -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true -o publish-framework-dependent
```

## ⚙️ 配置

托盘图标 → 右键 → **设置**，或直接编辑 `%AppData%\MemoryCleaner\config.json`：

```jsonc
{
  "CleanWorkingSet": true,          // 清空工作集
  "CleanSystemCache": false,        // 清空系统缓存（需管理员）
  "KillHighUsageProcesses": false,  // 结束高占用进程（默认关闭）

  "ThresholdEnabled": true,         // 阈值触发
  "ThresholdPercent": 80,

  "IntervalEnabled": false,         // 固定间隔触发
  "IntervalMinutes": 30,

  "ScheduleEnabled": false,         // 定时点触发
  "DailyTimes": ["12:00"],
  "WeeklyEnabled": false,
  "WeeklyDay": 0,                   // 0=周日

  "KillThresholdMB": 2048,          // 单进程工作集阈值
  "ProcessWhitelist": [],           // 进程白名单（不含 .exe）

  "RunAtStartup": false,            // 开机自启
  "ShowNotification": true,         // 清理后通知
  "MinIntervalSeconds": 60          // 两次清理最小间隔
}
```

## 🛡️ 安全说明

- **“结束高占用进程”默认关闭**，开启后系统关键进程与你的白名单进程绝不会被结束。
- **“清理系统缓存”需要管理员权限**；非管理员运行时该选项自动灰置。
- 本工具只做**标准 Win32 内存管理调用**，不注入、不驱动、不修改他人进程内存数据。

## 🏗️ 项目结构

```
src/MemoryCleaner/
├─ Core/                # 清理器：工作集 / 系统缓存 / 高占用进程
├─ Scheduler/           # 调度引擎 + 三种触发器
├─ Config/              # 配置模型 / 持久化 / 开机自启
├─ UI/                  # 托盘 / 设置 / 关于 / 图标生成
├─ Native/              # Win32 P/Invoke 声明
└─ TrayAppContext.cs    # 托盘主逻辑
```

## 📄 许可

[MIT](LICENSE)
