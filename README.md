# WireGuard Server Manager

一个面向自建 WireGuard VPS 的 Windows 10/11 管理工具。它通过 SSH 读取服务器真实状态，并提供 Peer、设备、流量、连接历史、安全事件和监听端口管理能力。软件界面支持简体中文和 English。

> 当前为 **Beta 0.9.0 测试版**。请先在非关键服务器上验证，并在执行端口切换、Peer 删除或迁移前保留服务器配置备份。

[English](docs/USER_GUIDE.en-US.md) · [中文完整教程](docs/USER_GUIDE.zh-CN.md) · [下载与校验](docs/DOWNLOADS.md)

> 本项目是独立的开源管理工具，与 WireGuard LLC 无隶属或背书关系。请仅管理你拥有或明确获准管理的服务器。

## 下载

- [下载 Beta 0.9.0 Windows x64](https://github.com/Ogurixy/wireguard-server-manager/releases/tag/beta-0.9.0) — 下载 `WireGuardServerManager-Windows-x64.zip`
- [查看全部版本与更新说明](../../releases)
- [下载源代码 ZIP](https://github.com/Ogurixy/wireguard-server-manager/archive/refs/heads/main.zip)

发布页提供 Windows x64 免安装压缩包和 SHA-256 校验文件。本仓库不发布 Android 客户端。

## 主要功能

- 管理多台 WireGuard VPS，并为每台服务器保存独立接口、VPN 网段和监听端口
- 通过 SSH 自动检测 WireGuard Interface，解析 `wg show all dump`
- 以 Public Key 为设备长期身份，匹配服务器 Peer 与本地设备
- 判断在线/离线状态，识别 Unknown Peer、IP 冲突和配置差异
- 新增、禁用、启用、软删除和迁移设备
- 自动分配未占用 VPN IP，导出客户端配置与二维码
- 记录连接历史、长期流量、计数器重置和审计日志
- 安全切换 WireGuard UDP 监听端口：开放新端口、备份、修改、重启验证，再清理旧规则
- 可关联 Clash Verge YAML；VPS 端口切换成功后自动备份并同步所有同服务器 WireGuard 节点
- 简体中文与 English 界面，可在 Settings 页面保存切换
- Windows 10/11 x64 免安装桌面管理端

## 安全设计

- SSH 私钥不写入 Windows SQLite 数据库；Android 端只保存在应用私有目录
- UI、日志和数据库不显示或保存服务器 WireGuard PrivateKey
- 修改服务器配置前备份到 `/etc/wireguard/backups/`
- 删除 Peer 前二次确认；数据库使用软删除并保留历史
- 不提供任意 Shell 输入框，服务器操作使用内置的受控命令
- 端口切换或迁移失败时优先回滚，避免服务器或设备失联

仓库和安装包不包含任何 VPS 地址、密码、SSH 私钥、WireGuard 私钥或客户端配置。

## 快速开始

### Windows

1. 从 [Beta 0.9.0 Release](https://github.com/Ogurixy/wireguard-server-manager/releases/tag/beta-0.9.0) 下载 Windows x64 压缩包并解压。
2. 运行 `WireGuardServerManager.exe`。
3. 在 **Servers** 页面添加自己的 VPS。
4. 选择 SSH 私钥并测试连接。
5. 同步服务器，确认自动检测到的接口和监听端口。

### 联动 Clash Verge

1. 打开 **Settings**，点击“选择文件夹”，选择存放 Clash Verge 代理配置的目录。
2. 勾选“VPS 端口切换成功后自动匹配并同步文件夹内所有 Clash 配置”，点击“保存设置”。
3. 回到 **Servers**，选择服务器并使用“切换 VPS 端口”。
4. VPS 修改和验证成功后，程序会递归扫描目录内所有 `.yaml/.yml`，在 `proxies` 中寻找 `type: wireguard` 且 `server` 与 VPS 地址相同的节点，并更新全部对应 `port`。
5. 每个被修改的配置都会在原目录生成带 `.wg-manager-*.bak` 后缀的备份。随后在 Clash Verge 中重新载入配置。

如果 YAML 来自在线订阅，订阅刷新可能覆盖本地修改；此时应联动一份由你维护的本地配置。

如需在 Windows 端生成 WireGuard 客户端密钥，请安装[官方 WireGuard 客户端](https://www.wireguard.com/install/)，让程序能够调用官方 `wg.exe`。

## 服务器要求

- Linux VPS 已安装 WireGuard 与 `wg-quick`
- SSH 账户可以执行所需的 `wg`、`wg-quick`、`systemctl` 和配置备份操作
- 推荐创建专用 `wg-manager` 用户并只授予必要命令，避免 `NOPASSWD: ALL`
- 使用 UFW 时，程序会按安全顺序维护 WireGuard UDP 端口规则

## 项目结构

```text
WireGuardServerManager.App/       Windows WPF 管理端
WireGuardServerManager.Core/      WireGuard、SSH、解析与数据核心
WireGuardServerManager.Tests/     自动化测试
```

## 从源码构建

要求：.NET 10 SDK 和 Windows 10/11。

```powershell
dotnet restore WireGuardServerManager.slnx
dotnet build WireGuardServerManager.slnx --configuration Release
dotnet test WireGuardServerManager.Tests/WireGuardServerManager.Tests.csproj --configuration Release
```

发布 Windows x64：

```powershell
dotnet publish WireGuardServerManager.App/WireGuardServerManager.App.csproj `
  --configuration Release --runtime win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## 参与贡献

欢迎提交 Issue 与 Pull Request。请先阅读 [CONTRIBUTING.md](CONTRIBUTING.md)。安全问题请按照 [SECURITY.md](SECURITY.md) 私下报告，不要在公开 Issue 中粘贴密钥或完整配置。

## 许可证

本项目使用 [MIT License](LICENSE)。
