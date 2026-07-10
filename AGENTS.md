# AGENTS.md - sing-boot 项目指南

> 本地运行路径: `c:\sing-box\`

## 项目概述

sing-boot 是一个 Windows 系统托盘工具，用于在后台运行和控制 [mihomo](https://wiki.metacubex.one/) 或 [sing-box](https://github.com/SagerNet/sing-box) 代理核心。

**技术栈**: C# / .NET Framework 4.8 / Windows Forms（使用 .NET 8 或更高版本 SDK 构建）

**核心功能**:
- 托盘图标显示运行状态
- 左键点击或右键菜单切换当前核心启动/停止
- 通过 Windows 注册表管理开机自启
- 自动发现 mihomo 或 sing-box 布局
- sing-box 配置通过 stdin 管道传递给 `sing-box run -c stdin`
- mihomo 配置通过 `-f` 显式传递
- 支持 JSONC 配置文件（允许注释和尾随逗号）
- 自动保存核心 stdout 日志

---

## 项目结构

```
sing-boot/
├── src/
│   ├── SingBoot.sln              # Visual Studio 解决方案
│   ├── SingBoot/                 # 主项目目录
│   │   ├── SingBoot.csproj       # 项目配置文件
│   │   ├── Program.cs            # 程序入口点
│   │   ├── SingBootApp.cs        # 应用主控制器
│   │   ├── CoreSupervisor.cs     # 核心进程管理器
│   │   ├── CoreProfile.cs        # 核心发现与启动参数
│   │   ├── CoreConfig.cs         # JSON/YAML 配置加载与 TUN 检测
│   │   ├── MainForm.cs           # 托盘 UI 窗体
│   │   ├── JsonHelper.cs         # JSONC 规范化工具
│   │   ├── NetworkReadiness.cs   # 开机自启网络就绪检查
│   │   ├── AutoStart.cs          # 开机自启管理
│   │   ├── SingleInstance.cs     # 单实例锁
│   │   ├── PrivilegeHelper.cs    # 权限提升辅助
│   │   └── EmbeddedAssemblyResolver.cs # 嵌入依赖加载
│   └── SingBoot.Tests/           # MSTest 回归测试
├── publish/                      # 发布输出目录
├── .github/workflows/            # CI 与发布工作流
├── icon.ico                      # 运行中图标
├── icon_disabled.ico             # 已停止图标
├── THIRD-PARTY-NOTICES.txt       # 嵌入依赖许可声明
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
- `Config` - 当前所选核心的配置信息
- `State` - 当前核心状态
- `RequiresElevation` - 是否需要管理员权限（检测 TUN inbound）

**核心方法**:
- `Start()` - 启动 sing-box
- `Stop()` - 停止 sing-box
- `PrepareForStart()` - 启动前检查（冲突进程、权限提升）
- `Shutdown()` - 优雅关闭

### CoreSupervisor.cs - 进程管理器

**职责**: 管理所选核心子进程的完整生命周期

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

### CoreProfile.cs / CoreConfig.cs - 核心发现与配置

**职责**: 发现 mihomo/sing-box 布局，加载配置并判断是否需要管理员权限

**功能**:
- mihomo: 支持 `config.yaml` / `config.yml`，使用完整 YAML 解析器处理锚点、合并键和流式结构
- sing-box: 读取 JSONC 配置 `config.json`，规范化后通过 stdin 传给核心
- 检测 mihomo 或 sing-box 是否启用 TUN（需要管理员权限）

### JsonHelper.cs - JSONC 规范化

**职责**: 将 JSONC 转换为标准 JSON

**处理内容**:
- 单行注释 `// ...`
- 多行注释 `/* ... */`
- 尾随逗号（在 `}` 或 `]` 前的逗号会被移除）

**实现**: 状态机移除注释并保留词法边界，只删除对象或数组闭合符前的尾随逗号；其他非法 JSON 会被拒绝

### AutoStart.cs - 开机自启

**注册表路径**:
- 启动项: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\SingBoot`
- 状态存储: `HKCU\Software\SingBoot\ResumeCoreOnAutoStart`

**逻辑**:
- 启用时写入注册表启动项
- 记录退出时当前核心是否在运行
- 下次开机自动启动时恢复之前的状态

### SingleInstance.cs - 单实例锁

**职责**: 确保同时只有一个实例运行

**技术**:
- 使用全局命名 Mutex (`Global\SingBoot_SingleInstance_Mutex`)
- 设置宽松的安全描述符，允许不同权限级别的进程共享同一互斥锁

### PrivilegeHelper.cs - 权限管理

**职责**: 检测和请求管理员权限

**场景**: 当配置启用 TUN 时需要管理员权限

**流程**:
1. 检测当前是否以管理员运行
2. 如需提升，使用 `runas` 动词重新启动
3. 通过 `--handoff-start` 参数传递启动意图

---

## 构建与发布

### 环境要求

- [.NET 8 或更高版本 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- .NET Framework 4.8 targeting pack

### 开发构建

```bash
dotnet build src/SingBoot.sln -v minimal
dotnet test src/SingBoot.sln -c Release
```

### 发布构建

```bash
dotnet publish src/SingBoot/SingBoot.csproj -c Release
```

**发布配置** (Release 模式自动应用):
- 目标框架: `net48`
- 目标平台: `x64`
- 依赖 .NET Framework 4.8（非自包含）
- YamlDotNet 作为资源嵌入，部署产物仍为单个 `sing-boot.exe`
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
4. 在 `src/SingBoot.Tests/` 添加对应回归测试
5. 运行 `dotnet build src/SingBoot.sln` 和 `dotnet test src/SingBoot.sln` 确认通过

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

当前版本不支持热更新配置。修改配置文件后需要:
1. 停止当前核心
2. 重新启动

---

## 常见问题

**Q: 托盘图标不显示?**
A: 检查 Windows 资源管理器是否正常运行，尝试重启资源管理器。

**Q: 启动后立即失败?**
A: 检查当前核心的配置文件是否有效，托盘错误提示会显示核心返回的摘要信息。

**Q: 开机自启不工作?**
A: 确保在右键菜单中勾选了 "Auto-start"，检查注册表中是否存在启动项。

**Q: TUN 模式无法启动?**
A: TUN 模式需要管理员权限，点击启动时会弹出 UAC 提示。

---

## CI/CD

仓库包含 GitHub Actions 工作流：push 和 pull request 会执行 Release 构建及测试；手工发布也会先测试，再构建 `net48` 版本并将单文件 `sing-boot.exe` 发布到 GitHub Release。

---

## 相关链接

- [sing-box 官方文档](https://sing-box.sagernet.org/)
- [sing-box GitHub](https://github.com/SagerNet/sing-box)
- [mihomo 文档](https://wiki.metacubex.one/)
- [.NET 8 文档](https://learn.microsoft.com/dotnet/)
