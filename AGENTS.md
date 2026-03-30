# AGENTS.md - sing-boot 项目指南

> 本地运行路径: `c:\sing-box\`

## 项目概述

sing-boot 是一个 Windows 系统托盘工具，用于在后台运行和控制 [sing-box](https://github.com/SagerNet/sing-box) 代理核心。

**技术栈**: C# / .NET 8 / Windows Forms

**核心功能**:
- 托盘图标显示运行状态
- 左键点击或右键菜单切换 sing-box 启动/停止
- 通过 Windows 注册表管理开机自启
- 配置通过 stdin 管道传递给 `sing-box run -c stdin`
- 支持 JSONC 配置文件（允许注释和尾随逗号）

---

## 项目结构

```
sing-boot/
├── src/
│   ├── SingBoot.sln              # Visual Studio 解决方案
│   └── SingBoot/                 # 主项目目录
│       ├── SingBoot.csproj       # 项目配置文件
│       ├── Program.cs            # 程序入口点
│       ├── SingBootApp.cs        # 应用主控制器
│       ├── CoreSupervisor.cs     # sing-box 进程管理器
│       ├── MainForm.cs           # 托盘 UI 窗体
│       ├── SingBoxConfig.cs      # JSONC 配置加载
│       ├── JsonHelper.cs         # JSONC 规范化工具
│       ├── AutoStart.cs          # 开机自启管理
│       ├── SingleInstance.cs     # 单实例锁
│       └── PrivilegeHelper.cs    # 权限提升辅助
├── publish/                      # 发布输出目录
├── config.json                   # sing-box 示例配置
├── icon.ico                      # 运行中图标
├── icon_disabled.ico             # 已停止图标
└── README.md                     # 项目说明文档
```

---

## 核心模块说明

### Program.cs - 程序入口

**职责**: 应用启动、单实例检查、初始化主窗体

**启动模式**:
- `Normal` - 普通启动
- `--auto-start` - 开机自启启动
- `--handoff-start` - 权限提升后移交启动

**关键流程**:
1. 解析命令行参数确定启动模式
2. 获取单实例互斥锁（跨权限级别共享）
3. 创建 `SingBootApp` 实例
4. 启动 Windows Forms 消息循环

### SingBootApp.cs - 应用控制器

**职责**: 协调各组件、管理生命周期、暴露操作接口

**核心属性**:
- `Config` - sing-box 配置内容
- `State` - 当前核心状态
- `RequiresElevation` - 是否需要管理员权限（检测 TUN inbound）

**核心方法**:
- `Start()` - 启动 sing-box
- `Stop()` - 停止 sing-box
- `PrepareForStart()` - 启动前检查（冲突进程、权限提升）
- `Shutdown()` - 优雅关闭

### CoreSupervisor.cs - 进程管理器

**职责**: 管理 sing-box 子进程的完整生命周期

**技术要点**:
- 使用 Windows Job Object 确保父进程退出时子进程也被终止
- 通过 stdin 管道传递配置: `sing-box run -c stdin`
- 捕获 stderr 输出用于错误诊断
- 优雅关闭: 先发送 Ctrl+C，超时后强制终止

**状态机**:
```
Stopped → Starting → Running → Stopping → Stopped
                ↓                      ↑
              Failed ←─────────────────┘
```

**Windows API 调用**:
- `CreateProcess` - 创建进程
- `CreateJobObject` / `AssignProcessToJobObject` - Job Object 管理
- `CreatePipe` - 管道创建
- `GenerateConsoleCtrlEvent` - 发送 Ctrl+C 信号

### MainForm.cs - 托盘界面

**职责**: 系统托盘图标、上下文菜单、用户交互

**UI 元素**:
- 托盘图标（运行中/已停止两种状态）
- 右键菜单: Start/Stop、Auto-start、Quit
- 气泡通知（错误提示）

**事件处理**:
- 左键点击: 切换启动/停止
- 会话结束事件: 保存恢复状态

### SingBoxConfig.cs - 代理配置

**职责**: 加载和规范化 sing-box 配置

**功能**:
- 读取程序同目录下的 JSONC 配置文件 `config.json`
- 调用 `JsonHelper.NormalizeJson()` 去除注释和尾随逗号
- 检测是否包含 TUN inbound（需要管理员权限）

### JsonHelper.cs - JSONC 规范化

**职责**: 将 JSONC 转换为标准 JSON

**处理内容**:
- 单行注释 `// ...`
- 多行注释 `/* ... */`
- 尾随逗号（在 `}` 或 `]` 前的逗号会被移除）

**实现**: 状态机解析器，逐字符处理

### AutoStart.cs - 开机自启

**注册表路径**:
- 启动项: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\SingBoot`
- 状态存储: `HKCU\Software\SingBoot\ResumeCoreOnAutoStart`

**逻辑**:
- 启用时写入注册表启动项
- 记录退出时 sing-box 是否在运行
- 下次开机自动启动时恢复之前的状态

### SingleInstance.cs - 单实例锁

**职责**: 确保同时只有一个实例运行

**技术**:
- 使用全局命名 Mutex (`Global\SingBoot_SingleInstance_Mutex`)
- 设置宽松的安全描述符，允许不同权限级别的进程共享同一互斥锁

### PrivilegeHelper.cs - 权限管理

**职责**: 检测和请求管理员权限

**场景**: 当配置包含 TUN inbound 时需要管理员权限

**流程**:
1. 检测当前是否以管理员运行
2. 如需提升，使用 `runas` 动词重新启动
3. 通过 `--handoff-start` 参数传递启动意图

---

## 构建与发布

### 环境要求

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### 开发构建

```bash
dotnet build src/SingBoot/SingBoot.csproj -v minimal
```

### 发布构建

```bash
dotnet publish src/SingBoot/SingBoot.csproj -c Release
```

**发布配置** (Release 模式自动应用):
- 目标平台: `win-x64`
- 自包含: 是
- 单文件: 是
- 压缩: 是
- 输出目录: `publish/`

---

## 本地部署说明

### 文件放置

```
c:\sing-box\
├── sing-box.exe          # sing-box 核心
├── sing-boot.exe         # 本程序
└── config.json           # sing-box 配置
```

### 首次运行

1. 确保已下载 [sing-box](https://github.com/SagerNet/sing-box/releases) Windows 版本
2. 编辑 `config.json` 配置代理服务器
3. 运行 `sing-boot.exe`，托盘区会出现图标
4. 左键点击或右键选择 "Start" 启动代理

---

## 开发指南

### 添加新功能

1. 在 `src/SingBoot/` 下添加新的 `.cs` 文件
2. 遵循现有代码风格
3. 使用 nullable 引用类型
4. 运行 `dotnet build` 确认编译通过

### 调试

```bash
# 在 Visual Studio 或 VS Code 中打开解决方案
# 设置断点后按 F5 启动调试

# 或使用命令行
dotnet run --project src/SingBoot/SingBoot.csproj
```

## 注意事项

### 进程管理

- sing-box 进程通过 Job Object 与 sing-boot 绑定
- sing-boot 崩溃或被强制结束时，sing-box 也会被系统终止
- 正常退出时优先发送 Ctrl+C 信号优雅关闭

### 权限提升

- TUN 模式需要管理员权限
- 权限提升会启动新的提升进程，原进程退出
- 使用 `--handoff-start` 参数在进程间传递启动意图

### 配置热更新

当前版本不支持热更新配置。修改 `config.json` 后需要:
1. 停止 sing-box
2. 重新启动

---

## 常见问题

**Q: 托盘图标不显示?**
A: 检查 Windows 资源管理器是否正常运行，尝试重启资源管理器。

**Q: 启动后立即失败?**
A: 检查 `config.json` 是否有效，托盘错误提示会显示 sing-box 返回的摘要信息。

**Q: 开机自启不工作?**
A: 确保在右键菜单中勾选了 "Auto-start"，检查注册表中是否存在启动项。

**Q: TUN 模式无法启动?**
A: TUN 模式需要管理员权限，点击启动时会弹出 UAC 提示。

---

## CI/CD

仓库包含 GitHub Actions 工作流，自动构建 `net48` Release 版本并将 `sing-boot.exe` 发布到 GitHub Release。

---

## 相关链接

- [sing-box 官方文档](https://sing-box.sagernet.org/)
- [sing-box GitHub](https://github.com/SagerNet/sing-box)
- [.NET 8 文档](https://learn.microsoft.com/dotnet/)
