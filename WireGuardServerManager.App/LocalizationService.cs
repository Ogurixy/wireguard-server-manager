using System.Windows;
using System.Windows.Controls;

namespace WireGuardServerManager.App;

public static class LocalizationService
{
    private static readonly (string Zh, string En)[] Pairs =
    [
        ("简体中文", "Simplified Chinese"), ("界面语言", "Interface language"),
        ("▦   仪表板", "▦   Dashboard"), ("▣   服务器", "▣   Servers"),
        ("♙   用户", "♙   Users"), ("◉   设备", "◉   Devices"),
        ("↔   在线设备", "↔   Online Devices"), ("◈   WireGuard Peer", "◈   WireGuard Peers"),
        ("◷   连接历史", "◷   Connection History"), ("▤   流量", "▤   Traffic"),
        ("⚠   未知 Peer", "⚠   Unknown Peers"), ("⚠   安全事件", "⚠   Security Events"),
        ("⊘   黑名单", "⊘   Blacklist"), ("⊙   白名单", "⊙   Whitelist"),
        ("⚙   设置", "⚙   Settings"), ("▥   审计日志", "▥   Audit Logs"),
        ("仪表板", "Dashboard"), ("服务器", "Servers"), ("用户", "Users"),
        ("设备", "Devices"), ("在线设备", "Online Devices"),
        ("连接历史", "Connection History"), ("流量", "Traffic"),
        ("未知 Peer", "Unknown Peers"), ("安全事件", "Security Events"),
        ("黑名单", "Blacklist"), ("白名单", "Whitelist"), ("设置", "Settings"),
        ("审计日志", "Audit Logs"), ("健康服务器", "Healthy Servers"),
        ("今日流量", "Today Traffic"),
        ("第一阶段 · WireGuard only", "WireGuard management"),
        ("WireGuard 服务器运行概览", "WireGuard server overview"), ("SSH 用户", "SSH user"),
        ("SSH 同步选中", "Sync selected"), ("同步全部", "Sync all"),
        ("服务器", "Server"), ("用户", "User"), ("设备", "Device"),
        ("地址", "Address"), ("名称", "Name"), ("类型", "Type"), ("详情", "Details"),
        ("时间", "Time"), ("状态", "Status"), ("操作", "Action"),
        ("已配置服务器", "Configured servers"), ("等待 SSH 同步", "Waiting for SSH sync"),
        ("最近握手在线", "Online by recent handshake"), ("安全事件待处理", "Security events pending"),
        ("启用用户", "Enabled users"), ("未软删除设备", "Active device records"),
        ("收发合计", "RX + TX total"), ("最近记录", "Recent records"),
        ("最近安全事件（点击定位）", "Recent security events (click to locate)"),
        ("点击一条记录，打开安全事件明细并定位到该记录", "Click a record to open and locate its security event"),
        ("管理 WireGuard VPS 与 SSH 连接参数（SSH 私钥只在同步时选择，不写入数据库）", "Manage WireGuard VPS and SSH settings (SSH private keys are selected only for sync and are not stored in the database)"),
        ("添加或编辑服务器", "Add or edit server"), ("公网 IP/域名", "Public IP/domain"),
        ("SSH 端口", "SSH port"), ("VPN 网段", "VPN subnet"),
        ("VPN 网段，例如 10.88.0.0/24", "VPN subnet, e.g. 10.88.0.0/24"),
        ("目标 WireGuard UDP 端口", "Target WireGuard UDP port"), ("添加", "Add"),
        ("保存资料（不改端口）", "Save details (do not change port)"),
        ("只保存名称、Host、SSH 参数、Interface 和 VPN 网段", "Save only name, host, SSH settings, interface and VPN subnet"),
        ("测试选中 SSH", "Test selected SSH"), ("切换 VPS 端口", "Change VPS port"),
        ("先放行新端口并验证，再删除旧端口规则", "Allow and verify the new port before removing the old rule"),
        ("WireGuard 端口只能通过橙色“切换 VPS 端口”修改。安全顺序：放行新 UDP 端口 → 备份并修改配置 → 重启并验证 → 删除旧 UFW 端口。成功后所有客户端 Endpoint 也必须改为新端口。", "Change the WireGuard port only with the orange button. Safe order: allow new UDP port → back up and edit configuration → restart and verify → remove old UFW rule. Update every client endpoint after success."),
        ("服务器真实状态 · 只读同步", "Live server state · read-only sync"),
        ("设备身份以 WireGuard PublicKey 为核心", "WireGuard public key is the device identity"),
        ("新增设备", "Add device"), ("禁用选中", "Disable selected"), ("启用选中", "Enable selected"),
        ("迁移选中", "Migrate selected"), ("软删除选中", "Soft-delete selected"),
        ("设备名称", "Device name"), ("用户名称", "User name"), ("所属服务器", "Server"),
        ("SSH 用户（通常为 root）", "SSH user (usually root)"), ("WireGuard 客户端密钥", "WireGuard client keys"),
        ("生成官方密钥", "Generate official keys"), ("VPN IP（可留空自动分配）", "VPN IP (leave blank for automatic allocation)"),
        ("添加 Peer 并导出配置", "Add peer and export configuration"), ("取消", "Cancel"),
        ("生成客户端密钥后，程序会通过 SSH 添加 Peer，并导出可导入手机或电脑的客户端配置。服务器私钥不会读取或保存。", "After generating client keys, the app adds the peer over SSH and exports a client configuration. The server private key is never read or stored."),
        ("最近 180 秒内完成握手的设备", "Devices with a handshake in the last 180 seconds"),
        ("公网 IP", "Public IP"), ("最近握手", "Latest handshake"),
        ("根据 WireGuard 最近握手推断的连接记录", "Connection records inferred from recent WireGuard handshakes"),
        ("开始", "Started"), ("结束", "Ended"),
        ("本地累计流量；检测到计数器重置时不会产生负数", "Locally accumulated traffic; counter resets never produce negative usage"),
        ("设备/PublicKey", "Device/PublicKey"), ("RX 累计", "Total RX"), ("TX 累计", "Total TX"), ("更新时间", "Updated"),
        ("服务器存在但数据库没有匹配设备的 PublicKey；忽略后仍会显示，但不再重复生成安全事件", "A public key exists on the server but has no matching local device. Ignored peers remain visible without generating repeated security events."),
        ("导入后的设备名称", "Imported device name"), ("导入为设备", "Import as device"),
        ("忽略/恢复提醒", "Ignore/restore alert"), ("二次确认后移除", "Remove after confirmation"),
        ("WireGuard 状态判定、Clash Verge 联动和已配置服务器", "WireGuard state detection, Clash Verge integration and configured servers"),
        ("Online 阈值（秒，仅允许 60/120/180/300/600）", "Online threshold (seconds: 60/120/180/300/600)"),
        ("Offline Grace Period（秒，允许 0/60/120/300；用于短暂抖动）", "Offline grace period (seconds: 0/60/120/300)"),
        ("Clash Verge 配置联动", "Clash Verge configuration integration"),
        ("选择 Clash 配置文件夹。VPS 端口切换并验证成功后，程序会递归扫描其中所有 YAML，按服务器地址匹配 WireGuard 节点，逐文件备份并更新全部对应端口。", "Choose a Clash configuration folder. After a verified VPS port change, the app recursively scans YAML files, matches WireGuard nodes by server address, backs up each file and updates all matching ports."),
        ("选择文件夹", "Choose folder"), ("包含 Clash Verge YAML 配置的文件夹", "Folder containing Clash Verge YAML configurations"),
        ("VPS 端口切换成功后自动匹配并同步文件夹内所有 Clash 配置", "Automatically match and update every Clash configuration after a successful VPS port change"),
        ("提示：如果 Clash Verge 正在运行，文件更新后可能需要在 Clash Verge 中重新载入该配置。订阅自动更新也可能覆盖本地修改。", "Tip: Reload the configuration in Clash Verge after files change. Subscription updates may overwrite local changes."),
        ("保存设置", "Save settings"), ("当前端口将在启动时载入。", "Current ports are loaded at startup."),
        ("安全事件完整记录；从 Dashboard 单击事件后会自动定位到对应记录", "Complete security event history; click a Dashboard event to locate it here"),
        ("管理员操作记录；不记录服务器私钥或客户端私钥", "Administrator audit trail; server and client private keys are never recorded"),
        ("设备归属用户；禁用用户不会删除设备历史", "Device owners; disabling a user does not remove device history"),
        ("添加用户", "Add user"), ("启用/禁用", "Enable/disable"), ("创建时间", "Created"),
        ("记录需要阻止的公网 IP 或 CIDR；应用前需明确选择目标服务器", "Record public IPs or CIDRs to block; explicitly select a target server before applying"),
        ("记录允许的公网 IP 或 CIDR；应用前需明确选择目标服务器", "Record allowed public IPs or CIDRs; explicitly select a target server before applying"),
        ("地址/CIDR", "Address/CIDR"), ("添加黑名单", "Add to blacklist"), ("添加白名单", "Add to whitelist"),
        ("启用", "Enabled"), ("删除", "Delete"), ("说明", "Notes")
    ];

    public static string Translate(string value, string language)
    {
        var english = language.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        foreach (var pair in Pairs)
        {
            if (value == pair.Zh || value == pair.En) return english ? pair.En : pair.Zh;
        }
        return value;
    }

    public static void Apply(DependencyObject root, string language)
    {
        ApplyElement(root, language);
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) Apply(child, language);
    }

    private static void ApplyElement(DependencyObject element, string language)
    {
        if (element is TextBlock text) text.Text = Translate(text.Text, language);
        if (element is ContentControl content && content.Content is string value) content.Content = Translate(value, language);
        if (element is FrameworkElement framework && framework.ToolTip is string tip) framework.ToolTip = Translate(tip, language);
        if (element is DataGrid grid)
        {
            foreach (var column in grid.Columns)
            {
                if (column.Header is string header) column.Header = Translate(header, language);
            }
        }
    }
}
