# sing-boot

Windows 系统托盘工具，用于在后台运行和管理 [mihomo](https://wiki.metacubex.one/) 或 [sing-box](https://github.com/SagerNet/sing-box)。

运行环境为 Windows x64 和 .NET Framework 4.8。

## 功能

- 托盘图标显示运行状态
- 左键点击或右键菜单控制启动/停止
- 开机自启动
- 自动识别同目录 mihomo 或 sing-box 部署
- 自动保存核心 stdout 到 `logs/` 目录

## 安装

1. 从 [Releases](https://github.com/hdrover/sing-boot/releases/latest) 下载 `sing-boot.exe`
2. 准备 mihomo 或 sing-box 核心文件
3. 按下面任一方式将核心和配置放在 `sing-boot.exe` 同目录
4. 运行 `sing-boot.exe`，托盘区会出现图标

### mihomo 部署

```
sing-boot.exe
mihomo-windows-amd64.exe
config.yaml
```

也支持 `mihomo.exe` 和 `config.yml`。启动命令为：

```bash
"mihomo-windows-amd64.exe" -d "<sing-boot.exe 所在目录>" -f "<配置文件完整路径>"
```

程序始终通过 `-f` 显式传递实际发现到的 `config.yaml` 或 `config.yml`，不会依赖 mihomo 的默认文件名。

### sing-box 部署

```
sing-boot.exe
sing-box.exe
config.json
```

sing-box 仍使用 `sing-box run -c stdin`，程序会读取并规范化同目录 `config.json` 后通过 stdin 传给核心。JSONC 支持注释和对象、数组内的尾随逗号；其他非法 JSON 会被拒绝，不会被自动改写。

## 配置

程序启动核心前会自动识别同目录文件：

- mihomo: `mihomo-windows-amd64.exe` 或 `mihomo.exe`，搭配 `config.yaml` 或 `config.yml`
- sing-box: `sing-box.exe`，搭配 `config.json`

如果两种完整布局同时存在，优先使用 mihomo。修改配置后需先停止再启动才能生效。

## 日志

核心进程的 stdout 会按天追加保存到程序同目录的 `logs/` 文件夹：

```text
logs/<core>-stdout-yyyy-MM-dd.log
```

例如 `logs/sing-box-stdout-2026-05-19.log` 或 `logs/mihomo-stdout-2026-05-19.log`。日志内容仅包含核心进程写出的 stdout。

## 开机自启

在右键菜单中勾选「Auto-start」即可启用。

启用后，如果退出时核心正在运行，下次开机会在可用的 IPv4 默认网关稳定后自动启动；如果退出前已手动停止，开机后仅启动托盘程序。

等待网络期间取消「Auto-start」会同时取消本次待执行的自动启动。

## 开发

需要 Windows、.NET 8 或更高版本 SDK，以及 .NET Framework 4.8 targeting pack。

```bash
dotnet restore src/SingBoot.sln
dotnet build src/SingBoot.sln -c Release
dotnet test src/SingBoot.sln -c Release
dotnet publish src/SingBoot/SingBoot.csproj -c Release
```

发布结果位于 `publish/`。运行时依赖的 YAML 解析组件及其第三方许可声明已嵌入 `sing-boot.exe`，部署时仍只需复制该 EXE。GitHub Actions 会在 push、pull request 和手工发布前执行 Release 构建与测试。

## 注意

- TUN 模式需要管理员权限，启动时会弹出 UAC 提示
- 崩溃或强制结束时，受管理的核心进程也会一同终止
- mihomo 配置请参考 [mihomo 文档](https://wiki.metacubex.one/)
- sing-box 配置请参考 [sing-box 官方文档](https://sing-box.sagernet.org/)
