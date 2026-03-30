# sing-boot

Windows 系统托盘工具，用于在后台运行和管理 [sing-box](https://github.com/SagerNet/sing-box)。

## 功能

- 托盘图标显示运行状态
- 左键点击或右键菜单控制启动/停止
- 开机自启动

## 安装

1. 从 [Releases](https://github.com/hdrover/sing-boot/releases/latest) 下载 `sing-boot.exe`
2. 从 [sing-box Releases](https://github.com/SagerNet/sing-box/releases) 下载 Windows amd64 版本
3. 将 `sing-boot.exe`、`sing-box.exe` 和 `config.json` 放在同一目录
4. 运行 `sing-boot.exe`，托盘区会出现图标

## 配置

程序读取同目录下的 `config.json`。修改配置后需先停止再启动才能生效。

## 开机自启

在右键菜单中勾选「Auto-start」即可启用。

启用后，如果退出时 sing-box 正在运行，下次开机也会自动启动；如果退出前已手动停止，开机后仅启动托盘程序。

## 注意

- TUN 模式需要管理员权限，启动时会弹出 UAC 提示
- 崩溃或强制结束时，sing-box 也会一同终止
- 更多配置选项请参考 [sing-box 官方文档](https://sing-box.sagernet.org/)
