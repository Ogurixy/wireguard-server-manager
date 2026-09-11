# WireGuard Server Manager User Guide

> Current version: Beta 0.9.0 test release. Test it on a non-critical server and keep a backup of `/etc/wireguard/` before production use.

WireGuard Server Manager is a Windows 10/11 desktop application for managing WireGuard VPS servers over SSH. It shows real peer state, matches devices by public key, infers online status, tracks traffic and history, detects unknown peers, and safely changes WireGuard listening ports.

## Download

Download `WireGuardServerManager-Windows-x64.zip` and its `.sha256` file from the [Beta 0.9.0 release](https://github.com/Ogurixy/wireguard-server-manager/releases/tag/beta-0.9.0). Extract the archive and run `WireGuardServerManager.exe`.

The local database is stored in `%LOCALAPPDATA%\WireGuardServerManager\manager.db`, so replacing the executable does not remove your data.

## Add a server

1. Open **Servers**.
2. Enter a display name, public host, SSH port/user, WireGuard interface, VPN subnet, and current WireGuard UDP port.
3. Add the server, select it, and test SSH with your private key.
4. Use **Sync selected** or **Sync all** to read `wg show all dump`.

The SSH private key is not stored in SQLite. Never publish SSH keys, VPS passwords, client configurations, or WireGuard private keys.

## Change the WireGuard port

Select a server, enter the new UDP port, and use the orange **Change VPS port** button. The app allows the new firewall rule, backs up and updates the WireGuard configuration, restarts and verifies the interface, then removes the old UFW rule. Update every client endpoint after the change.

## Clash Verge folder synchronization

In **Settings**, select the folder containing Clash YAML files and enable automatic synchronization. After a successful VPS port change, the app recursively scans `.yaml` and `.yml` files, matches every `type: wireguard` node whose `server` equals the VPS host, creates per-file backups, and updates matching ports. Reload the configuration in Clash Verge afterward.

## Devices and peers

Device identity is based on the WireGuard public key. Creating a device uses official WireGuard tools, allocates a free VPN IP, adds the peer over SSH, and exports a client configuration and QR code. Disable and soft-delete operations preserve history. Migration adds and verifies the target peer before removing the source peer.

## Language

Open **Settings → Interface language**, choose **Simplified Chinese** or **English**, and select **Save settings**. The choice is stored locally and restored on startup.

## Build from source

```powershell
git clone https://github.com/Ogurixy/wireguard-server-manager.git
cd wireguard-server-manager
dotnet restore WireGuardServerManager.slnx
dotnet build WireGuardServerManager.slnx --configuration Release
dotnet test WireGuardServerManager.Tests/WireGuardServerManager.Tests.csproj --configuration Release
```

See the [Chinese guide](USER_GUIDE.zh-CN.md) for the complete workflow and troubleshooting checklist.
