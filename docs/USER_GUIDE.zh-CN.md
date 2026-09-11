# WireGuard Server Manager 中文教程

> 当前版本：Beta 0.9.0 测试版。用于重要服务器前请先测试，并保留 `/etc/wireguard/` 配置备份。

## 1. 下载和启动

1. 打开 [Beta 0.9.0 Release](https://github.com/Ogurixy/wireguard-server-manager/releases/tag/beta-0.9.0)。
2. 下载 `WireGuardServerManager-Windows-x64.zip` 和对应的 `.sha256` 文件。
3. 解压到一个可写文件夹，运行 `WireGuardServerManager.exe`。
4. Windows SmartScreen 首次提示时，先核对下载地址和 SHA-256，再选择“更多信息 → 仍要运行”。

程序是免安装版。数据库保存在当前 Windows 用户的 `%LOCALAPPDATA%\WireGuardServerManager\manager.db`，更新程序不会覆盖数据库。

## 2. 服务器准备

VPS 需要安装 WireGuard、`wg-quick` 和 OpenSSH Server。推荐创建专用的 `wg-manager` 用户，并只授予 WireGuard 管理所需的 sudo 命令。不要使用 `NOPASSWD: ALL`。

程序不会把 SSH 私钥写入 SQLite。请妥善保管私钥，不要将私钥、VPS 密码或客户端配置提交到 GitHub。

## 3. 添加 VPS

进入 **Servers**：

1. 填写名称、公网 IP 或域名、SSH 端口、SSH 用户、WireGuard Interface、VPN 网段和当前 WireGuard UDP 端口。
2. Interface 通常为 `wg0`，但程序不强制写死。
3. 点击“添加”。
4. 在列表中选中服务器，点击“测试选中 SSH”，选择 SSH 私钥。
5. SSH 成功后返回 Dashboard，使用“SSH 同步选中”或“同步全部”。

## 4. 修改 WireGuard 端口

1. 在 **Servers** 选中服务器。
2. 在端口框填写 1–65535 范围内的新 UDP 端口。
3. 点击橙色“切换 VPS 端口”。
4. 程序按顺序开放新 UFW 规则、备份配置、修改监听端口、重启并验证 WireGuard，最后清理旧端口规则。
5. 成功后必须同步修改所有客户端的 `Endpoint` 端口。

不要使用“保存资料”直接改端口；它只修改普通服务器资料。

## 5. Clash Verge 文件夹自动同步

1. 打开 **Settings**。
2. 点击“选择文件夹”，选择保存 Clash YAML 的目录。
3. 勾选自动同步并保存设置。
4. 以后切换 VPS 端口成功时，程序会递归扫描所有 `.yaml` 和 `.yml`。
5. 只匹配 `type: wireguard` 且 `server` 与 VPS 地址一致的节点，然后更新其 `port`。
6. 每个修改过的文件旁都会生成 `.wg-manager-*.bak` 备份。
7. 回到 Clash Verge 重新载入配置。

在线订阅刷新可能覆盖本地 YAML，请使用自己维护的本地配置或在刷新后重新同步。

## 6. 设备和 Peer

在 **Devices** 创建设备时，程序调用 WireGuard 官方工具生成密钥，分配未占用 VPN IP，通过 SSH 添加 Peer，并导出客户端配置和二维码。PublicKey 是设备的长期身份；公网 IP 和 Endpoint 不是设备唯一标识。

- 禁用：从运行中的 WireGuard 移除 Peer，但保留数据库和历史。
- 启用：根据数据库记录重新添加 Peer。
- 软删除：保留审计、连接和流量历史。
- 迁移：先在目标服务器添加并验证，再从源服务器删除，降低完全失联风险。

## 7. 状态和安全事件

WireGuard 没有传统连接会话。程序默认把最近 180 秒有握手的 Peer 判定为在线，并使用离线宽限期减少网络抖动造成的反复上下线。

服务器中存在、但无法按 PublicKey 匹配本地设备的 Peer 会显示为 **Unknown Peer**。删除前请核对 PublicKey、Allowed IP、Endpoint 和流量；程序不会自动删除未知 Peer。

## 8. 中英文切换

进入 **Settings → 界面语言**，选择“简体中文”或“English”，点击“保存设置”。选择会写入本地设置，并在以后启动时自动应用。

## 9. 故障排查

- `Permission denied (publickey)`：确认 SSH 用户、私钥和服务器上的 `authorized_keys`。
- 端口切换失败：确认 sudo 权限、UFW、`/etc/wireguard/<interface>.conf` 和 `systemctl status wg-quick@<interface>`。
- Peer 有发送但无接收：检查公网 UDP 端口、防火墙、Endpoint、服务端监听和最近握手。
- Clash 未更新：确认选择的是包含 YAML 的文件夹，节点类型是 `wireguard`，且 `server` 与 VPS Host 完全一致。
- 服务器 PrivateKey 不应出现在截图、日志或 Issue 中。

## 10. 从 Git 获取源码

```powershell
git clone https://github.com/Ogurixy/wireguard-server-manager.git
cd wireguard-server-manager
dotnet restore WireGuardServerManager.slnx
dotnet build WireGuardServerManager.slnx --configuration Release
dotnet test WireGuardServerManager.Tests/WireGuardServerManager.Tests.csproj --configuration Release
```
