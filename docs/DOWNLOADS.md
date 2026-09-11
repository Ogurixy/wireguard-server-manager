# 下载、校验与源码 / Downloads, verification, and source

> 当前下载为 Beta 0.9.0 测试版 / The current download is the Beta 0.9.0 test release.

## Windows 成品 / Windows binary

- Beta 0.9.0: <https://github.com/Ogurixy/wireguard-server-manager/releases/tag/beta-0.9.0>
- 文件 / File: `WireGuardServerManager-Windows-x64.zip`
- 校验 / Checksum: `WireGuardServerManager-Windows-x64.zip.sha256`

PowerShell 校验：

```powershell
Get-FileHash .\WireGuardServerManager-Windows-x64.zip -Algorithm SHA256
Get-Content .\WireGuardServerManager-Windows-x64.zip.sha256
```

两处哈希值必须一致。The two SHA-256 values must match.

## Git 源码 / Git source

```powershell
git clone https://github.com/Ogurixy/wireguard-server-manager.git
```

源代码 ZIP / Source ZIP:

<https://github.com/Ogurixy/wireguard-server-manager/archive/refs/heads/main.zip>

## 范围 / Scope

本仓库和 Release 仅提供 Windows x64 管理端，不包含 Android 应用、自定义软件图标、VPS 凭据、SSH 私钥或 WireGuard 私钥。

This repository and its releases contain only the Windows x64 manager. They do not include an Android application, custom application artwork, VPS credentials, SSH private keys, or WireGuard private keys.
