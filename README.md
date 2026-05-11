# sing-boot

Windows 系统托盘工具，用于在后台运行和管理 [mihomo](https://wiki.metacubex.one/) 或 [sing-box](https://github.com/SagerNet/sing-box)。

## 功能

- 托盘图标显示运行状态
- 左键点击或右键菜单控制启动/停止
- 开机自启动
- 自动识别同目录 mihomo 或 sing-box 部署

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
"mihomo-windows-amd64.exe" -d "<sing-boot.exe 所在目录>"
```

### sing-box 部署

```
sing-boot.exe
sing-box.exe
config.json
```

sing-box 仍使用 `sing-box run -c stdin`，程序会读取并规范化同目录 `config.json` 后通过 stdin 传给核心。

## 配置

程序启动核心前会自动识别同目录文件：

- mihomo: `mihomo-windows-amd64.exe` 或 `mihomo.exe`，搭配 `config.yaml` 或 `config.yml`
- sing-box: `sing-box.exe`，搭配 `config.json`

如果两种完整布局同时存在，优先使用 mihomo。修改配置后需先停止再启动才能生效。

## 开机自启

在右键菜单中勾选「Auto-start」即可启用。

启用后，如果退出时核心正在运行，下次开机也会自动启动；如果退出前已手动停止，开机后仅启动托盘程序。

## 注意

- TUN 模式需要管理员权限，启动时会弹出 UAC 提示
- 崩溃或强制结束时，受管理的核心进程也会一同终止
- mihomo 配置请参考 [mihomo 文档](https://wiki.metacubex.one/)
- sing-box 配置请参考 [sing-box 官方文档](https://sing-box.sagernet.org/)
