# sing-boot

Windows 系统托盘工具，用于在后台运行和管理 [sing-box](https://github.com/SagerNet/sing-box)。

运行环境为 Windows x64 和 .NET Framework 4.8。

## 功能

- 托盘图标显示运行状态
- 左键点击或右键菜单控制启动/停止
- 开机自启动
- 自动识别同目录 sing-box 部署
- 支持在托盘右键菜单从远程地址拉取新配置、自动备份原 `config.json`，并在核心运行时自动重启
- 自动保存核心 stdout 和 stderr 到 `logs/` 目录

## 安装

1. 从 [Releases](https://github.com/hdrover/sing-boot/releases/latest) 下载 `sing-boot.exe`
2. 准备 sing-box 核心文件
3. 将核心和配置按下面的布局放在 `sing-boot.exe` 同目录
4. 运行 `sing-boot.exe`，托盘区会出现图标

### sing-box 部署

```
sing-boot.exe
sing-box.exe
config.json
sing-boot.json    # 可选：远程配置拉取设置
```

sing-box 仍使用 `sing-box run -c stdin`，程序会读取并规范化同目录 `config.json` 后通过 stdin 传给核心。JSONC 支持注释和对象、数组内的尾随逗号；其他非法 JSON 会被拒绝，不会被自动改写。

## 配置

程序启动核心前会自动识别同目录的 `sing-box.exe` 和 `config.json`。修改配置后需先停止再启动才能生效。

### 远程拉取配置（可选）

可在同目录创建 `sing-boot.json`（同样支持 JSONC 注释和尾随逗号）：

```json
{
  "configUrl": "https://raw.githubusercontent.com/SagerNet/sing-box/refs/heads/testing/test/config/vless-tls-client.json"
}
```

配置 `configUrl` 后，可在托盘右键菜单点击「Update config」手动拉取更新（单次请求，超时为 30 秒）：
1. 从 `configUrl` 拉取远程配置并校验是否为合法 JSON/JSONC 对象
2. 将原 `config.json` 备份为 `config.json.bak`，并将新配置保存为 `config.json`
3. 若当前 `sing-box` 正在运行，则自动重启核心使新配置生效；若当前处于停止状态，则仅备份并更新 `config.json`（拉取或校验失败时保留原 `config.json`，且不中断当前运行的 `sing-box`）

## 日志

日志会按天追加保存到程序同目录的 `logs/` 文件夹：

```text
logs/<core>-stdout-yyyy-MM-dd.log
logs/<core>-stderr-yyyy-MM-dd.log
logs/sing-boot-yyyy-MM-dd.log
```

`<core>-stdout` 文件包含核心进程写出的 stdout。`<core>-stderr` 文件保存核心的 stderr，每行附带接收时间，移除终端颜色控制符，并隐藏 HTTP/HTTPS URL 中的账号、路径和查询参数。即使 stderr 日志写入失败，程序也会继续读取核心输出，保留最近的错误摘要。

`sing-boot` 日志记录启动模式、自动恢复判定、默认网关等待、提权前的启动准备、手动配置更新结果，以及退出时是否保留恢复意图。下载失败会记录异常链、网络状态及 socket 错误码，隐藏 URL 中的账号、路径和查询参数，不记录完整代理配置。

## 开机自启

在右键菜单中勾选「Auto-start」即可启用。

启用后，成功接受核心启动请求会记住“应保持运行”的意图，下次开机会在可用的 IPv4 默认网关稳定后自动启动。只有明确点击 Stop、Quit 或取消「Auto-start」才会清除该意图；系统关机不会再根据关机瞬间的进程状态覆盖它。

等待网络期间取消「Auto-start」会同时取消本次待执行的自动启动。

## 开发

需要 Windows、.NET 8 或更高版本 SDK，以及 .NET Framework 4.8 targeting pack。

```bash
dotnet restore src/SingBoot.sln
dotnet build src/SingBoot.sln -c Release
dotnet test src/SingBoot.sln -c Release
dotnet publish src/SingBoot/SingBoot.csproj -c Release
```

发布结果位于 `publish/`。除系统提供的 .NET Framework 4.8 外，`sing-boot.exe` 无第三方运行时依赖，部署时只需复制该 EXE。GitHub Actions 会在 push、pull request 和手工发布前执行 Release 构建与测试。

## 注意

- TUN 模式需要管理员权限，启动时会弹出 UAC 提示
- 崩溃或强制结束时，受管理的核心进程也会一同终止
- sing-box 配置请参考 [sing-box 官方文档](https://sing-box.sagernet.org/)
