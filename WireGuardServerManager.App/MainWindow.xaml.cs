using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using QRCoder;
using WireGuardServerManager.Core;

namespace WireGuardServerManager.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<Server> _servers = new();
    private readonly ObservableCollection<DeviceRow> _devices = new();
    private readonly ObservableCollection<PeerRow> _peers = new();
    private readonly ObservableCollection<OnlineRow> _onlineDevices = new();
    private readonly ObservableCollection<HistoryRow> _history = new();
    private readonly ObservableCollection<TrafficRow> _traffic = new();
    private readonly ObservableCollection<PeerRow> _unknownPeers = new();
    private readonly ObservableCollection<AuditRow> _auditLogs = new();
    private readonly ObservableCollection<UserRow> _users = new();
    private readonly ObservableCollection<AccessRuleRow> _blacklist = new();
    private readonly ObservableCollection<AccessRuleRow> _whitelist = new();
    private readonly ObservableCollection<SecurityEventRow> _securityEvents = new();
    private readonly HashSet<Guid> _healthyServers = new();
    private readonly SqliteStore _store;
    private readonly SshWireGuardClient _ssh = new();
    private WireGuardKeyPair? _pendingKeyPair;
    private bool _startupSyncStarted;
    private Server? SelectedServer => ServerGrid.SelectedItem as Server ?? _servers.FirstOrDefault();

    public MainWindow()
    {
        InitializeComponent();
        SetActiveNavigation(DashboardNavButton);
        var dataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WireGuardServerManager", "manager.db");
        _store = new SqliteStore(dataPath);
        _store.Initialize();
        OnlineThresholdTextBox.Text = _store.GetSetting("OnlineThresholdSeconds") ?? "180";
        OfflineGraceTextBox.Text = _store.GetSetting("OfflineGracePeriodSeconds") ?? "60";
        var clashDirectory = _store.GetSetting("ClashConfigDirectory") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(clashDirectory))
        {
            var legacyConfigPath = _store.GetSetting("ClashConfigPath");
            if (!string.IsNullOrWhiteSpace(legacyConfigPath) && File.Exists(legacyConfigPath)) clashDirectory = Path.GetDirectoryName(legacyConfigPath) ?? string.Empty;
        }
        ClashConfigDirectoryTextBox.Text = clashDirectory;
        AutoSyncClashCheckBox.IsChecked = bool.TryParse(_store.GetSetting("AutoSyncClashPort"), out var autoSyncClash) && autoSyncClash;
        var language = _store.GetSetting("Language") ?? "zh-CN";
        LanguageComboBox.SelectedValue = language;
        foreach (var server in _store.LoadServers()) _servers.Add(server);
        foreach (var device in _store.LoadDevices()) _devices.Add(DeviceRow.From(device, _servers.FirstOrDefault(s => s.Id == device.ServerId)?.Name ?? "—"));
        ServerGrid.ItemsSource = _servers;
        ServerManagementGrid.ItemsSource = _servers;
        DeviceGrid.ItemsSource = _devices;
        PeerGrid.ItemsSource = _peers;
        OnlineGrid.ItemsSource = _onlineDevices;
        HistoryGrid.ItemsSource = _history;
        TrafficGrid.ItemsSource = _traffic;
        UnknownGrid.ItemsSource = _unknownPeers;
        AuditGrid.ItemsSource = _auditLogs;
        UserGrid.ItemsSource = _users;
        BlacklistGrid.ItemsSource = _blacklist;
        WhitelistGrid.ItemsSource = _whitelist;
        SecurityGrid.ItemsSource = _securityEvents;
        SecurityEventsGrid.ItemsSource = _securityEvents;
        DeviceServerComboBox.ItemsSource = _servers;
        DeviceServerComboBox.SelectedIndex = 0;
        MigrationTargetComboBox.ItemsSource = _servers;
        MigrationTargetComboBox.SelectedIndex = 1 < _servers.Count ? 1 : 0;
        DataContext = _servers;
        ServerCountText.Text = _servers.Count.ToString();
        UpdateSettingsDescriptions();
        ReloadDerivedData();
        ApplyLanguage(language);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_startupSyncStarted) return;
        _startupSyncStarted = true;
        var keyPath = _store.GetSetting("LastSshKeyPath");
        if (string.IsNullOrWhiteSpace(keyPath) || !File.Exists(keyPath))
        {
            StatusText.Text = "首次同步请选择 SSH 私钥；后续启动会在文件仍存在时自动对账。";
            return;
        }

        var failures = new List<string>();
        foreach (var server in _servers.ToArray())
        {
            var user = ResolveSshUser(server);
            var error = await SyncServerAsync(server, new SshConnectionOptions(server.Host, server.SshPort, user, keyPath), false);
            if (error is not null) failures.Add($"{server.Name}: {error}");
        }
        ReloadDerivedData(); UpdateDashboardCounts();
        StatusText.Text = failures.Count == 0 ? "启动自动对账完成。" : $"启动自动对账完成，但有 {failures.Count} 台服务器失败。";
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e) => ShowView(DashboardView, DashboardNavButton);
    private void Servers_Click(object sender, RoutedEventArgs e) => ShowView(ServersView, ServersNavButton);
    private void Peers_Click(object sender, RoutedEventArgs e) => ShowView(PeersView, PeersNavButton);
    private void Devices_Click(object sender, RoutedEventArgs e) => ShowView(DevicesView, DevicesNavButton);
    private void OnlineDevices_Click(object sender, RoutedEventArgs e) => ShowView(OnlineDevicesView, OnlineDevicesNavButton);
    private void History_Click(object sender, RoutedEventArgs e) => ShowView(HistoryView, HistoryNavButton);
    private void Traffic_Click(object sender, RoutedEventArgs e) => ShowView(TrafficView, TrafficNavButton);
    private void UnknownPeers_Click(object sender, RoutedEventArgs e) => ShowView(UnknownPeersView, UnknownPeersNavButton);
    private void SecurityEvents_Click(object sender, RoutedEventArgs e) { ReloadDerivedData(); ShowView(SecurityEventsView, SecurityEventsNavButton); }
    private void Settings_Click(object sender, RoutedEventArgs e) => ShowView(SettingsView, SettingsNavButton);
    private void AuditLogs_Click(object sender, RoutedEventArgs e)
    {
        ReloadDerivedData();
        ShowView(AuditLogsView, AuditLogsNavButton);
    }
    private void Users_Click(object sender, RoutedEventArgs e) { ReloadDerivedData(); ShowView(UsersView, UsersNavButton); }
    private void Blacklist_Click(object sender, RoutedEventArgs e) { ReloadDerivedData(); ShowView(BlacklistView, BlacklistNavButton); }
    private void Whitelist_Click(object sender, RoutedEventArgs e) { ReloadDerivedData(); ShowView(WhitelistView, WhitelistNavButton); }

    private void SecurityGrid_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SecurityGrid.SelectedItem is not SecurityEventRow row) return;
        ShowView(SecurityEventsView, SecurityEventsNavButton);
        SecurityEventsGrid.UpdateLayout();
        SecurityEventsGrid.SelectedItem = row;
        SecurityEventsGrid.ScrollIntoView(row);
        SecurityEventsGrid.Focus();
        StatusText.Text = $"已定位安全事件：{row.Type} · {row.OccurredAt}";
    }

    private void IgnoreUnknownPeer_Click(object sender, RoutedEventArgs e)
    {
        if (UnknownGrid.SelectedItem is not PeerRow row) { MessageBox.Show("请先选择 Unknown Peer。", "安全操作"); return; }
        var ignored = !row.IsIgnored;
        _store.SetPeerIgnored(row.ServerId, row.PublicKey, ignored);
        var replacement = row with { IsIgnored = ignored, Status = ignored ? "Ignored Peer" : "Unknown Peer" };
        ReplacePeer(row, replacement);
        ReloadDerivedData(); UpdateDashboardCounts();
        StatusText.Text = ignored ? "已忽略该 Peer 的重复提醒。" : "已恢复该 Peer 的安全提醒。";
    }

    private async void RemoveUnknownPeer_Click(object sender, RoutedEventArgs e)
    {
        if (UnknownGrid.SelectedItem is not PeerRow row) { MessageBox.Show("请先选择 Unknown Peer。", "安全操作"); return; }
        var server = _servers.FirstOrDefault(x => x.Id == row.ServerId);
        if (server is null) return;
        if (MessageBox.Show($"确定从 {server.Name} 移除这个 Peer？\n\nPublicKey：{row.PublicKey}\n\n此操作会备份并修改 VPS 配置。", "危险操作二次确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var options = ChooseSshOptions(server); if (options is null) return;
        try
        {
            StatusText.Text = $"正在从 {server.Name} 移除 Unknown Peer…";
            await _ssh.RemovePeerAsync(options, row.Interface, row.PublicKey);
            _peers.Remove(row); _unknownPeers.Remove(row);
            _store.AddAuditLog("UnknownPeerRemoved", $"Server={server.Name}; Interface={row.Interface}; PublicKey={row.PublicKey}");
            ReloadDerivedData(); UpdateDashboardCounts(); StatusText.Text = $"Unknown Peer 已移除：{row.PublicKey}";
        }
        catch (Exception ex) { MessageBox.Show(RedactForUi(ex.Message), "移除 Unknown Peer 失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ImportUnknownPeer_Click(object sender, RoutedEventArgs e)
    {
        if (UnknownGrid.SelectedItem is not PeerRow row) { MessageBox.Show("请先选择 Unknown Peer。", "安全操作"); return; }
        var server = _servers.FirstOrDefault(x => x.Id == row.ServerId); if (server is null) return;
        var vpnIp = row.AllowedIps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()?.Split('/')[0];
        var name = UnknownImportNameTextBox.Text.Trim(); var user = UnknownImportUserTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = $"Imported-{row.PublicKey[..Math.Min(8, row.PublicKey.Length)]}";
        var error = vpnIp is null ? "Unknown Peer 没有可识别的 VPN IP。" : DeviceValidation.Validate(name, row.PublicKey, vpnIp, server, _devices.Select(x => x.Device).ToArray());
        if (error is not null) { MessageBox.Show(error, "导入设备失败", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        try
        {
            var device = new Device(Guid.NewGuid(), server.Id, name, row.PublicKey, vpnIp!, user);
            _store.AddDevice(device); _devices.Add(DeviceRow.From(device, server.Name)); _unknownPeers.Remove(row);
            _store.AddAuditLog("UnknownPeerImported", $"Server={server.Name}; PublicKey={row.PublicKey}; Device={device.Id}");
            UnknownImportNameTextBox.Clear(); UnknownImportUserTextBox.Clear(); StatusText.Text = $"Unknown Peer 已导入设备：{name}。下次同步后完成匹配。";
        }
        catch (Exception ex) { MessageBox.Show(RedactForUi(ex.Message), "导入设备失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void TestServer_Click(object sender, RoutedEventArgs e)
    {
        var server = ServerManagementGrid.SelectedItem as Server ?? SelectedServer;
        if (server is null) return;
        var options = ChooseSshOptions(server); if (options is null) return;
        try
        {
            StatusText.Text = $"正在测试 {server.Name} 的 SSH…";
            var interfaces = await _ssh.ExecuteReadOnlyAsync(options, "wg show interfaces");
            _healthyServers.Add(server.Id); _store.AddAuditLog("SshTest", $"Server={server.Name}; Interfaces={interfaces.Replace(Environment.NewLine, ",", StringComparison.Ordinal)}"); UpdateDashboardCounts();
            MessageBox.Show($"SSH 连接成功。检测到 WireGuard Interface：{interfaces.Trim().Replace(Environment.NewLine, ", ", StringComparison.Ordinal)}", "SSH 测试", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText.Text = $"{server.Name} SSH 测试成功";
        }
        catch (Exception ex) { _healthyServers.Remove(server.Id); UpdateDashboardCounts(); StatusText.Text = "SSH 测试失败"; MessageBox.Show(RedactForUi(ex.Message), "SSH 测试失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void AddServer_Click(object sender, RoutedEventArgs e)
    {
        var name = NewServerNameTextBox.Text.Trim(); var host = NewServerHostTextBox.Text.Trim(); var sshUser = NewServerSshUserTextBox.Text.Trim(); var iface = NewServerInterfaceTextBox.Text.Trim(); var subnet = NewServerSubnetTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(sshUser) || string.IsNullOrWhiteSpace(iface)) { MessageBox.Show("名称、Host、SSH 用户、Interface 不能为空。", "服务器校验"); return; }
        if (!int.TryParse(NewServerSshPortTextBox.Text, out var sshPort) || sshPort is < 1 or > 65535 || !int.TryParse(NewServerWgPortTextBox.Text, out var wgPort) || wgPort is < 1 or > 65535) { MessageBox.Show("SSH/WireGuard 端口必须是 1-65535。", "服务器校验"); return; }
        if (!IsValidAddressOrCidr(subnet)) { MessageBox.Show("VPN 网段必须是有效 CIDR，例如 10.88.0.0/24。", "服务器校验"); return; }
        if (_servers.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || x.Host.Equals(host, StringComparison.OrdinalIgnoreCase))) { MessageBox.Show("服务器名称或 Host 已存在。", "服务器校验"); return; }
        var server = new Server(Guid.NewGuid(), name, host, sshPort, sshUser, iface, subnet, wgPort);
        try
        {
            _store.AddServer(server); _servers.Add(server); DeviceServerComboBox.Items.Refresh(); NewServerNameTextBox.Clear(); NewServerHostTextBox.Clear(); NewServerSubnetTextBox.Clear(); UpdateSettingsDescriptions(); StatusText.Text = $"服务器已添加：{name}";
        }
        catch (Exception ex) { MessageBox.Show(RedactForUi(ex.Message), "添加服务器失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ServerSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ServerManagementGrid.SelectedItem is not Server server) return;
        NewServerNameTextBox.Text = server.Name; NewServerHostTextBox.Text = server.Host; NewServerSshPortTextBox.Text = server.SshPort.ToString();
        NewServerSshUserTextBox.Text = server.SshUser; NewServerInterfaceTextBox.Text = server.WireGuardInterface; NewServerSubnetTextBox.Text = server.VpnSubnet ?? string.Empty; NewServerWgPortTextBox.Text = server.WireGuardPort.ToString();
    }

    private void UpdateServer_Click(object sender, RoutedEventArgs e)
    {
        if (ServerManagementGrid.SelectedItem is not Server current) { MessageBox.Show("请先选择服务器。", "服务器操作"); return; }
        var name = NewServerNameTextBox.Text.Trim(); var host = NewServerHostTextBox.Text.Trim(); var sshUser = NewServerSshUserTextBox.Text.Trim(); var iface = NewServerInterfaceTextBox.Text.Trim(); var subnet = NewServerSubnetTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(sshUser) || string.IsNullOrWhiteSpace(iface)) { MessageBox.Show("名称、Host、SSH 用户、Interface 不能为空。", "服务器校验"); return; }
        if (!int.TryParse(NewServerSshPortTextBox.Text, out var sshPort) || sshPort is < 1 or > 65535 || !int.TryParse(NewServerWgPortTextBox.Text, out var wgPort) || wgPort is < 1 or > 65535) { MessageBox.Show("SSH/WireGuard 端口必须是 1-65535。", "服务器校验"); return; }
        if (!IsValidAddressOrCidr(subnet)) { MessageBox.Show("VPN 网段必须是有效 CIDR，例如 10.88.0.0/24。", "服务器校验"); return; }
        if (_servers.Any(x => x.Id != current.Id && (x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || x.Host.Equals(host, StringComparison.OrdinalIgnoreCase)))) { MessageBox.Show("服务器名称或 Host 已存在。", "服务器校验"); return; }
        if (wgPort != current.WireGuardPort) { MessageBox.Show($"检测到 WireGuard 端口从 {current.WireGuardPort} 改为 {wgPort}。\n\n“保存资料”不会修改 VPS，也不会保存这个端口变化。请点击橙色的“切换 VPS 端口”。", "请使用端口切换功能", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var updated = current with { Name = name, Host = host, SshPort = sshPort, SshUser = sshUser, WireGuardInterface = iface, VpnSubnet = subnet };
        try
        {
            _store.UpdateServer(updated); var index = _servers.IndexOf(current); if (index >= 0) _servers[index] = updated;
            ReloadDevicesWithCurrentServerNames(); DeviceServerComboBox.Items.Refresh(); MigrationTargetComboBox.Items.Refresh();
            UpdateSettingsDescriptions();
            StatusText.Text = $"服务器已更新：{name}（WireGuard UDP {wgPort}）";
        }
        catch (Exception ex) { MessageBox.Show(RedactForUi(ex.Message), "修改服务器失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void ChangeWireGuardPort_Click(object sender, RoutedEventArgs e)
    {
        if (ServerManagementGrid.SelectedItem is not Server current) { MessageBox.Show("请先选择要切换端口的服务器。", "端口切换"); return; }
        if (!int.TryParse(NewServerWgPortTextBox.Text, out var newPort) || newPort is < 1 or > 65535) { MessageBox.Show("新端口必须是 1-65535。", "端口切换"); return; }
        var confirmation = $"服务器：{current.Name} ({current.Host})\nInterface：{current.WireGuardInterface}\n数据库当前端口：{current.WireGuardPort}\n目标端口：{newPort}\n\n程序会先放行并验证新端口，成功后删除旧 UFW 规则。所有手机和电脑的 Endpoint 端口随后都要改为 {newPort}。\n\n是否继续？";
        if (MessageBox.Show(confirmation, "确认切换 WireGuard 端口", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var options = ChooseSshOptions(current); if (options is null) return;

        try
        {
            StatusText.Text = $"正在安全切换 {current.Name} 的 WireGuard 端口到 {newPort}…";
            var result = await _ssh.ChangeWireGuardPortAsync(options, current.WireGuardInterface, newPort);
            var updated = current with { WireGuardPort = result.NewPort };
            _store.UpdateServer(updated);
            _store.AddAuditLog("WireGuardPortChanged", $"Server={current.Name}; Host={current.Host}; Interface={current.WireGuardInterface}; OldPort={result.OldPort}; NewPort={result.NewPort}; FirewallManaged={result.FirewallManaged}; Changed={result.Changed}");
            var index = _servers.IndexOf(current); if (index >= 0) _servers[index] = updated;
            ServerManagementGrid.SelectedItem = updated;
            NewServerWgPortTextBox.Text = result.NewPort.ToString();
            UpdateSettingsDescriptions();
            StatusText.Text = result.Changed ? $"{current.Name} 已从 UDP {result.OldPort} 切换到 {result.NewPort}。" : $"{current.Name} 已在使用 UDP {result.NewPort}，并已确认防火墙规则。";
            var firewallText = result.FirewallManaged ? "UFW 新端口已放行，旧端口规则已删除。" : "VPS 未安装 UFW；程序未修改主机防火墙。";
            var clashText = SyncClashConfigAfterPortChange(current, result.NewPort);
            MessageBox.Show($"端口切换成功。\n\n{firewallText}\n\n{clashText}\n\n其他客户端的 Endpoint 应为：\n{FormatEndpoint(current.Host, result.NewPort)}", "WireGuard 端口切换完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"{current.Name} 端口切换失败；数据库未修改。";
            MessageBox.Show(RedactForUi(ex.Message) + "\n\n程序已尝试恢复旧配置。请在 VPS 上确认 wg-quick 状态和当前监听端口。", "WireGuard 端口切换失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ReloadDevicesWithCurrentServerNames()
    {
        var devices = _store.LoadDevices(); _devices.Clear();
        foreach (var device in devices) _devices.Add(DeviceRow.From(device, _servers.FirstOrDefault(s => s.Id == device.ServerId)?.Name ?? "—"));
    }

    private void ShowView(UIElement view, System.Windows.Controls.Button? activeNavigation = null)
    {
        DashboardView.Visibility = Visibility.Collapsed;
        ServersView.Visibility = Visibility.Collapsed;
        PeersView.Visibility = Visibility.Collapsed;
        DevicesView.Visibility = Visibility.Collapsed;
        DeviceEditView.Visibility = Visibility.Collapsed;
        OnlineDevicesView.Visibility = Visibility.Collapsed;
        HistoryView.Visibility = Visibility.Collapsed;
        TrafficView.Visibility = Visibility.Collapsed;
        UnknownPeersView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        AuditLogsView.Visibility = Visibility.Collapsed;
        UsersView.Visibility = Visibility.Collapsed;
        BlacklistView.Visibility = Visibility.Collapsed;
        WhitelistView.Visibility = Visibility.Collapsed;
        SecurityEventsView.Visibility = Visibility.Collapsed;
        view.Visibility = Visibility.Visible;
        if (activeNavigation is not null) SetActiveNavigation(activeNavigation);
    }

    private void SetActiveNavigation(System.Windows.Controls.Button active)
    {
        var buttons = new[]
        {
            DashboardNavButton, ServersNavButton, UsersNavButton, DevicesNavButton,
            OnlineDevicesNavButton, PeersNavButton, HistoryNavButton, TrafficNavButton,
            UnknownPeersNavButton, SecurityEventsNavButton, BlacklistNavButton,
            WhitelistNavButton, SettingsNavButton, AuditLogsNavButton
        };

        foreach (var button in buttons)
        {
            var selected = ReferenceEquals(button, active);
            button.Background = selected ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) : Brushes.Transparent;
            button.Foreground = selected ? Brushes.White : new SolidColorBrush(Color.FromRgb(203, 213, 225));
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        var server = SelectedServer;
        if (server is null) return;
        var options = ChooseSshOptions(server);
        if (options is null) return;
        var error = await SyncServerAsync(server, options, true);
        if (error is not null) { MessageBox.Show(error, "SSH / WireGuard 同步失败", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        ShowView(PeersView, PeersNavButton);
    }

    private async void SyncAll_Click(object sender, RoutedEventArgs e)
    {
        if (_servers.Count == 0) return;
        var failures = new List<string>();
        _peers.Clear();
        foreach (var server in _servers.ToArray())
        {
            var options = ChooseSshOptions(server);
            if (options is null) { failures.Add($"{server.Name}: 未选择 SSH 私钥"); continue; }
            var error = await SyncServerAsync(server, options, false);
            if (error is not null) failures.Add($"{server.Name}: {error}");
        }
        ReloadDerivedData();
        UpdateDashboardCounts();
        StatusText.Text = failures.Count == 0 ? $"全部服务器同步完成：{_peers.Count} 个 Peer" : $"同步完成，但有 {failures.Count} 台服务器失败";
        if (failures.Count > 0) MessageBox.Show(string.Join(Environment.NewLine, failures), "部分服务器同步失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        ShowView(PeersView, PeersNavButton);
    }

    private async Task<string?> SyncServerAsync(Server server, SshConnectionOptions options, bool replaceServerPeers)
    {
        try
        {
            StatusText.Text = $"正在通过 SSH 同步 {server.Name}…";
            var dump = await _ssh.ExecuteReadOnlyAsync(options, "wg show all dump");
            var now = DateTimeOffset.UtcNow;
            var parsed = WireGuardDumpParser.Parse(dump, now, TimeSpan.FromSeconds(ParseOnlineThreshold()));
            var detectedInterface = ResolveInterface(server, parsed.Interfaces);
            var interfaceCidr = await _ssh.GetInterfaceIpv4CidrAsync(options, detectedInterface);
            var effectiveServer = ApplyDetectedServerRuntime(server, parsed, DeviceValidation.NormalizeIpv4Subnet(interfaceCidr));
            var reconciliation = new ReconciliationService().Reconcile(effectiveServer.Id, parsed, _devices.Select(x => x.Device).ToArray());
            foreach (var peer in reconciliation.Peers)
            {
                _store.RecordPeerSnapshot(effectiveServer.Id, peer, peer.MatchedDevice, now, TimeSpan.FromSeconds(ParseOfflineGracePeriod()), TimeSpan.FromSeconds(ParseOnlineThreshold()));
                if (peer.IsUnknown && !_store.IsPeerIgnored(effectiveServer.Id, peer.PublicKey))
                    _store.AddSecurityEvent(new SecurityEvent("UnknownPeer", $"服务器 {effectiveServer.Name} 发现未登记 Peer：{peer.PublicKey}", now, effectiveServer.Id, peer.PublicKey));
                else if (peer.MatchedDevice is { Enabled: false })
                    _store.AddSecurityEvent(new SecurityEvent("DisabledButActive", $"已禁用设备仍存在于服务器：{peer.PublicKey}", now, effectiveServer.Id, peer.PublicKey));
            }
            foreach (var missing in reconciliation.DatabaseOnly)
            {
                _store.CloseOpenSession(missing.Id, effectiveServer.Id, now);
                _store.AddSecurityEvent(new SecurityEvent("EnabledButMissing", $"数据库中的启用设备未出现在服务器：{missing.PublicKey}", now, effectiveServer.Id, missing.PublicKey));
            }
            foreach (var conflict in reconciliation.Conflicts) _store.AddSecurityEvent(new SecurityEvent("VpnIpConflict", conflict, now, effectiveServer.Id));
            _store.AddAuditLog("WireGuardSync", $"Server={effectiveServer.Name}; Interface={string.Join(",", parsed.Interfaces)}; ListenPort={effectiveServer.WireGuardPort}; Peers={parsed.Peers.Count}");
            if (replaceServerPeers) foreach (var old in _peers.Where(x => x.ServerId == effectiveServer.Id).ToArray()) _peers.Remove(old);
            foreach (var peer in reconciliation.Peers) _peers.Add(PeerRow.From(effectiveServer, peer, _store.IsPeerIgnored(effectiveServer.Id, peer.PublicKey)));
            _healthyServers.Add(effectiveServer.Id);
            UpdateDashboardCounts();
            return null;
        }
        catch (Exception ex)
        {
            foreach (var old in _peers.Where(x => x.ServerId == server.Id).ToArray()) _peers.Remove(old);
            _healthyServers.Remove(server.Id);
            _store.AddSecurityEvent(new SecurityEvent("ServerOfflineEvent", $"服务器 {server.Name} SSH/WireGuard 同步失败", DateTimeOffset.UtcNow, server.Id));
            UpdateDashboardCounts();
            return RedactForUi(ex.Message);
        }
    }

    private void AddDevice_Click(object sender, RoutedEventArgs e)
    {
        _pendingKeyPair = null;
        DeviceNameTextBox.Clear(); DeviceUserTextBox.Clear(); DevicePublicKeyTextBox.Clear();
        DeviceVpnIpTextBox.Text = string.Empty; DeviceSshUserTextBox.Text = "root"; DeviceServerComboBox.SelectedIndex = 0;
        ShowView(DeviceEditView, DevicesNavButton);
    }

    private void GenerateKeys_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _pendingKeyPair = WireGuardKeyGenerator.Generate();
            DevicePublicKeyTextBox.Text = _pendingKeyPair.PublicKey;
            StatusText.Text = "客户端密钥已生成；私钥仅保留在当前窗口内。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(RedactForUi(ex.Message), "生成 WireGuard 密钥失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void SaveDevice_Click(object sender, RoutedEventArgs e)
    {
        var server = DeviceServerComboBox.SelectedItem as Server;
        if (server is null) { MessageBox.Show("请选择服务器。", "设备校验", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (_pendingKeyPair is null)
        {
            MessageBox.Show("请先点击“生成官方密钥”。程序需要对应的私钥来导出客户端配置。", "设备校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var keyPair = _pendingKeyPair;
        var name = DeviceNameTextBox.Text.Trim(); var user = DeviceUserTextBox.Text.Trim(); var key = keyPair.PublicKey;
        if (string.IsNullOrWhiteSpace(name)) { MessageBox.Show("设备名称不能为空。", "设备校验", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var saveDialog = new SaveFileDialog
        {
            Title = "保存 WireGuard 客户端配置",
            Filter = "WireGuard 配置 (*.conf)|*.conf|所有文件 (*.*)|*.*",
            FileName = $"{SanitizeFileName(name)}-{server.Name}.conf",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (saveDialog.ShowDialog() != true) return;

        var sshUser = string.IsNullOrWhiteSpace(DeviceSshUserTextBox.Text) ? "root" : DeviceSshUserTextBox.Text.Trim();
        var sshKeyDialog = new OpenFileDialog { Title = "选择 SSH 私钥（只在内存中使用，不会写入数据库）", Filter = "SSH 私钥|*|所有文件|*.*", CheckFileExists = true };
        if (sshKeyDialog.ShowDialog() != true) return;
        _store.SetSetting("LastSshKeyPath", sshKeyDialog.FileName);

        var configPath = saveDialog.FileName;
        var qrPath = Path.ChangeExtension(configPath, ".png");
        var configCreated = false;
        var qrCreated = false;
        try
        {
            StatusText.Text = $"正在连接 {server.Name} 并添加 Peer…";
            var options = new SshConnectionOptions(server.Host, server.SshPort, sshUser, sshKeyDialog.FileName);
            var interfaceDump = await _ssh.ExecuteReadOnlyAsync(options, "wg show all dump");
            var parsed = WireGuardDumpParser.Parse(interfaceDump, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(ParseOnlineThreshold()));
            var wireGuardInterface = ResolveInterface(server, parsed.Interfaces);
            var interfaceCidr = await _ssh.GetInterfaceIpv4CidrAsync(options, wireGuardInterface);
            var effectiveServer = ApplyDetectedServerRuntime(server, parsed, DeviceValidation.NormalizeIpv4Subnet(interfaceCidr));
            var remoteAllowedIps = parsed.Peers.Select(x => x.AllowedIps).ToArray();
            var vpnIp = string.IsNullOrWhiteSpace(DeviceVpnIpTextBox.Text)
                ? DeviceValidation.FindNextIp(effectiveServer, _devices.Select(x => x.Device), remoteAllowedIps)
                : DeviceVpnIpTextBox.Text.Trim();
            if (vpnIp is null) throw new InvalidOperationException("该服务器的 VPN IP 地址池已用尽或网段未配置。");
            var validationError = DeviceValidation.Validate(name, key, vpnIp, effectiveServer, _devices.Select(x => x.Device).ToArray());
            if (validationError is not null) throw new InvalidOperationException(validationError);
            if (DeviceValidation.IsVpnIpReservedByAllowedIps(vpnIp, remoteAllowedIps)) throw new InvalidOperationException("该 VPN IP 已被服务器上的现有 Peer 占用。");
            var device = new Device(Guid.NewGuid(), effectiveServer.Id, name, key, vpnIp, user);
            wireGuardInterface = ResolveInterface(effectiveServer, parsed.Interfaces);
            var serverPublicKey = await _ssh.GetServerPublicKeyAsync(options, wireGuardInterface);
            var clientConfig = BuildClientConfiguration(keyPair.PrivateKey, vpnIp, serverPublicKey, effectiveServer.Host, effectiveServer.WireGuardPort);
            await _ssh.AddPeerAsync(options, wireGuardInterface, key, vpnIp);
            try
            {
                File.WriteAllText(configPath, clientConfig, new UTF8Encoding(false));
                configCreated = true;
                using var qrGenerator = new QRCodeGenerator();
                using var qrData = qrGenerator.CreateQrCode(clientConfig, QRCodeGenerator.ECCLevel.Q);
                var qrCode = new PngByteQRCode(qrData);
                File.WriteAllBytes(qrPath, qrCode.GetGraphic(10));
                qrCreated = true;
                _store.AddDevice(device);
            }
            catch
            {
                await _ssh.RemoveManagedPeerAsync(options, wireGuardInterface, key);
                if (configCreated) TryDelete(configPath);
                if (qrCreated) TryDelete(qrPath);
                throw;
            }
            _devices.Add(DeviceRow.From(device, effectiveServer.Name));
            _pendingKeyPair = null;
            StatusText.Text = $"设备已添加：{name} · {vpnIp} · 配置已导出";
            ShowView(DevicesView, DevicesNavButton);
        }
        catch (Exception ex)
        {
            if (configCreated) TryDelete(configPath);
            if (qrCreated) TryDelete(qrPath);
            StatusText.Text = "新增设备失败";
            MessageBox.Show(RedactForUi(ex.Message), "添加 WireGuard Peer 失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void DisableDevice_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceGrid.SelectedItem is not DeviceRow row) { MessageBox.Show("请先选择设备。", "设备操作"); return; }
        if (!row.Enabled) { MessageBox.Show("该设备已经是禁用状态。", "设备操作"); return; }
        var server = _servers.FirstOrDefault(x => x.Id == row.Device.ServerId);
        if (server is null) return;
        if (MessageBox.Show($"将从 {server.Name} 移除 Peer 并禁用“{row.Name}”？历史记录会保留。", "二次确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var options = ChooseSshOptions(server); if (options is null) return;
        try
        {
            StatusText.Text = $"正在从 {server.Name} 移除 Peer…";
            var wireGuardInterface = await DetectInterfaceAsync(options, server);
            await _ssh.RemoveManagedPeerAsync(options, wireGuardInterface, row.PublicKey);
            _store.SetDeviceEnabled(row.Device.Id, false);
            ReplaceDevice(row, row.Device with { Enabled = false });
            StatusText.Text = $"设备已禁用：{row.Name}（Peer 已移除，历史记录保留）";
        }
        catch (Exception ex) { MessageBox.Show(RedactForUi(ex.Message), "禁用设备失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void EnableDevice_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceGrid.SelectedItem is not DeviceRow row) { MessageBox.Show("请先选择设备。", "设备操作"); return; }
        if (row.Enabled) { MessageBox.Show("该设备已经是启用状态。", "设备操作"); return; }
        var server = _servers.FirstOrDefault(x => x.Id == row.Device.ServerId); if (server is null) return;
        var options = ChooseSshOptions(server); if (options is null) return;
        try
        {
            StatusText.Text = $"正在向 {server.Name} 恢复 Peer…";
            var wireGuardInterface = await DetectInterfaceAsync(options, server);
            await _ssh.AddPeerAsync(options, wireGuardInterface, row.PublicKey, row.VpnIp);
            _store.SetDeviceEnabled(row.Device.Id, true);
            ReplaceDevice(row, row.Device with { Enabled = true });
            StatusText.Text = $"设备已启用：{row.Name}";
        }
        catch (Exception ex) { MessageBox.Show(RedactForUi(ex.Message), "启用设备失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void MigrateDevice_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceGrid.SelectedItem is not DeviceRow row) { MessageBox.Show("请先选择设备。", "设备操作"); return; }
        if (!row.Enabled) { MessageBox.Show("请先启用设备，再进行迁移。", "设备操作"); return; }
        var source = _servers.FirstOrDefault(x => x.Id == row.Device.ServerId);
        var target = MigrationTargetComboBox.SelectedItem as Server;
        if (source is null || target is null || source.Id == target.Id) { MessageBox.Show("请选择与当前服务器不同的目标服务器。", "迁移校验"); return; }
        var targetIp = DeviceValidation.FindNextIp(target, _devices.Select(x => x.Device));
        if (targetIp is null) { MessageBox.Show("目标服务器的 VPN IP 地址池已用尽。", "迁移校验"); return; }
        if (MessageBox.Show($"将设备“{row.Name}”从 {source.Name}/{row.VpnIp} 迁移到 {target.Name}/{targetIp}？\n\n迁移后需要更新客户端配置中的 Endpoint 和 VPN IP。", "迁移二次确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var keyDialog = new OpenFileDialog { Title = "选择两台 VPS 共用的 SSH 私钥（只在内存中使用）", Filter = "SSH 私钥|*|所有文件|*.*", CheckFileExists = true };
        if (keyDialog.ShowDialog() != true) return;
        var sourceOptions = new SshConnectionOptions(source.Host, source.SshPort, ResolveSshUser(source), keyDialog.FileName);
        var targetOptions = new SshConnectionOptions(target.Host, target.SshPort, ResolveSshUser(target), keyDialog.FileName);
        string? sourceInterface = null, targetInterface = null;
        var targetAdded = false; var sourceRemoved = false;
        try
        {
            StatusText.Text = $"正在检查目标 {target.Name}…";
            targetInterface = await DetectInterfaceAsync(targetOptions, target);
            sourceInterface = await DetectInterfaceAsync(sourceOptions, source);
            await _ssh.AddPeerAsync(targetOptions, targetInterface, row.PublicKey, targetIp); targetAdded = true;
            await _ssh.RemoveManagedPeerAsync(sourceOptions, sourceInterface, row.PublicKey); sourceRemoved = true;
            try
            {
                _store.MoveDevice(row.Device.Id, source.Id, target.Id, row.VpnIp, targetIp);
            }
            catch
            {
                if (sourceRemoved) await _ssh.AddPeerAsync(sourceOptions, sourceInterface, row.PublicKey, row.VpnIp);
                if (targetAdded) await _ssh.RemoveManagedPeerAsync(targetOptions, targetInterface, row.PublicKey);
                throw;
            }
            ReplaceDevice(row, row.Device with { ServerId = target.Id, VpnIp = targetIp });
            _store.AddAuditLog("DeviceMigrationCompleted", $"Device={row.Device.Id}; From={source.Name}/{row.VpnIp}; To={target.Name}/{targetIp}");
            StatusText.Text = $"迁移完成：{row.Name} → {target.Name}/{targetIp}。请更新客户端配置。";
        }
        catch (Exception ex)
        {
            if (targetAdded && !sourceRemoved && targetInterface is not null)
            {
                try { await _ssh.RemoveManagedPeerAsync(targetOptions, targetInterface, row.PublicKey); } catch { }
            }
            MessageBox.Show(RedactForUi(ex.Message), "设备迁移失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task<string> DetectInterfaceAsync(SshConnectionOptions options, Server server)
    {
        var dump = await _ssh.ExecuteReadOnlyAsync(options, "wg show all dump");
        var detected = WireGuardDumpParser.Parse(dump, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(ParseOnlineThreshold())).Interfaces;
        return ResolveInterface(server, detected);
    }

    private async void DeleteDevice_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceGrid.SelectedItem is not DeviceRow row) { MessageBox.Show("请先选择设备。", "设备操作"); return; }
        if (MessageBox.Show($"确定软删除设备“{row.Name}”？历史记录不会删除。", "二次确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var server = _servers.FirstOrDefault(x => x.Id == row.Device.ServerId);
        if (server is null) return;
        var options = row.Enabled ? ChooseSshOptions(server) : null;
        if (row.Enabled && options is null) return;
        try
        {
            if (row.Enabled)
            {
                var wireGuardInterface = await DetectInterfaceAsync(options!, server);
                await _ssh.RemoveManagedPeerAsync(options!, wireGuardInterface, row.PublicKey);
            }
            _store.SoftDeleteDevice(row.Device.Id); _devices.Remove(row);
            StatusText.Text = $"设备已软删除：{row.Name}（Peer 已移除，历史记录保留）";
        }
        catch (Exception ex) { MessageBox.Show(RedactForUi(ex.Message), "删除设备失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void CancelDevice_Click(object sender, RoutedEventArgs e) => ShowView(DevicesView, DevicesNavButton);
    private void FeaturePlaceholder_Click(object sender, RoutedEventArgs e)
    {
        var button = (System.Windows.Controls.Button)sender;
        MessageBox.Show($"{button.Content} 页面将在后续阶段接入真实数据。", "第一阶段提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string RedactForUi(string message) => message.Replace("PrivateKey", "[REDACTED]", StringComparison.OrdinalIgnoreCase);

    private int ParseOnlineThreshold() => int.TryParse(OnlineThresholdTextBox.Text, out var value) && value is 60 or 120 or 180 or 300 or 600 ? value : 180;

    private int ParseOfflineGracePeriod() => int.TryParse(OfflineGraceTextBox.Text, out var value) && value is 0 or 60 or 120 or 300 ? value : 60;

    private void UpdateDashboardCounts()
    {
        ServerCountText.Text = _servers.Count.ToString();
        HealthyCountText.Text = _healthyServers.Count.ToString();
        OnlineCountText.Text = _peers.Count(x => x.Status == "Online").ToString();
        UnknownCountText.Text = _peers.Count(x => x.Status == "Unknown Peer").ToString();
        UserCountText.Text = _store.LoadUsers().Count(x => x.Enabled).ToString();
        DeviceCountText.Text = _devices.Count.ToString();
        var localNow = DateTimeOffset.Now;
        var localDayStart = new DateTimeOffset(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, localNow.Offset);
        var traffic = _store.LoadTrafficSince(localDayStart);
        TodayTrafficText.Text = FormatBytes(traffic.Rx + traffic.Tx);
        SecurityEventCountText.Text = _store.LoadSecurityEvents(1000).Count.ToString();
    }

    private static string FormatBytes(long bytes)
    {
        var value = (double)Math.Max(0, bytes); var units = new[] { "B", "KB", "MB", "GB", "TB" }; var index = 0;
        while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
        return index == 0 ? $"{value:0} B" : $"{value:0.##} {units[index]}";
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var clashDirectory = ClashConfigDirectoryTextBox.Text.Trim();
        var autoSyncClash = AutoSyncClashCheckBox.IsChecked == true;
        if (autoSyncClash && !Directory.Exists(clashDirectory))
        {
            MessageBox.Show("启用 Clash Verge 自动同步前，请选择一个存在的配置文件夹。", "Clash Verge 配置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var threshold = ParseOnlineThreshold();
        OnlineThresholdTextBox.Text = threshold.ToString();
        var grace = ParseOfflineGracePeriod();
        OfflineGraceTextBox.Text = grace.ToString();
        _store.SetSetting("OnlineThresholdSeconds", threshold.ToString());
        _store.SetSetting("OfflineGracePeriodSeconds", grace.ToString());
        _store.SetSetting("ClashConfigDirectory", clashDirectory);
        _store.SetSetting("AutoSyncClashPort", autoSyncClash.ToString());
        var language = LanguageComboBox.SelectedValue?.ToString() ?? "zh-CN";
        _store.SetSetting("Language", language);
        UpdateSettingsDescriptions();
        ApplyLanguage(language);
        StatusText.Text = language.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? $"Settings saved: online threshold {threshold}s, offline grace {grace}s, Clash integration {(autoSyncClash ? "enabled" : "disabled")}."
            : $"设置已保存：Online 阈值 {threshold} 秒，离线宽限 {grace} 秒，Clash 联动{(autoSyncClash ? "已启用" : "未启用")}。";
    }

    private void ApplyLanguage(string language)
    {
        LocalizationService.Apply(this, language);
        OnlineThresholdDescriptionText.Text = language.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? $"Devices with a handshake in the last {ParseOnlineThreshold()} seconds"
            : $"最近 {ParseOnlineThreshold()} 秒内完成握手的设备";
    }

    private void ChooseClashConfigDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择包含 Clash Verge 配置的文件夹",
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;
        ClashConfigDirectoryTextBox.Text = dialog.FolderName;
        AutoSyncClashCheckBox.IsChecked = true;
        StatusText.Text = "已选择 Clash 配置文件夹；点击“保存设置”后启用自动匹配。";
    }

    private string SyncClashConfigAfterPortChange(Server server, int newPort)
    {
        if (AutoSyncClashCheckBox.IsChecked != true) return "Clash Verge 自动同步未启用，配置文件未修改。";
        var directory = ClashConfigDirectoryTextBox.Text.Trim();
        try
        {
            var result = ClashConfigUpdater.UpdateWireGuardPortsInDirectory(directory, server.Host, newPort);
            if (result.MatchedNodes == 0)
            {
                _store.AddAuditLog("ClashPortSyncNoMatch", $"Server={server.Name}; Host={server.Host}; NewPort={newPort}; ScannedFiles={result.ScannedFiles}; Errors={result.Errors.Count}");
                return $"Clash Verge：扫描了 {result.ScannedFiles} 个 YAML，未找到 server 为 {server.Host} 的 WireGuard 节点，文件未修改。";
            }
            if (result.ChangedNodes == 0)
            {
                _store.AddAuditLog("ClashPortAlreadyCurrent", $"Server={server.Name}; Host={server.Host}; NewPort={newPort}; MatchedFiles={result.MatchedFiles}; MatchedNodes={result.MatchedNodes}; Errors={result.Errors.Count}");
                return $"Clash Verge：扫描了 {result.ScannedFiles} 个 YAML，在 {result.MatchedFiles} 个文件中匹配到 {result.MatchedNodes} 个节点，端口已经是 {newPort}。";
            }

            _store.AddAuditLog("ClashPortSynced", $"Server={server.Name}; Host={server.Host}; NewPort={newPort}; ScannedFiles={result.ScannedFiles}; MatchedFiles={result.MatchedFiles}; MatchedNodes={result.MatchedNodes}; ChangedNodes={result.ChangedNodes}; Backups={result.BackupPaths.Count}; Errors={result.Errors.Count}");
            var errorText = result.Errors.Count == 0 ? string.Empty : $"，另有 {result.Errors.Count} 个文件处理失败";
            return $"Clash Verge：扫描 {result.ScannedFiles} 个 YAML，在 {result.MatchedFiles} 个文件中更新 {result.ChangedNodes} 个 WireGuard 节点，生成 {result.BackupPaths.Count} 份备份{errorText}。请重新载入配置。";
        }
        catch (Exception ex)
        {
            var message = RedactForUi(ex.Message);
            _store.AddAuditLog("ClashPortSyncFailed", $"Server={server.Name}; Host={server.Host}; NewPort={newPort}; Error={message}");
            return $"Clash Verge 同步失败：{message}\nVPS 端口已成功修改，请手动更新 Clash 配置。";
        }
    }

    private void AddUser_Click(object sender, RoutedEventArgs e)
    {
        var name = NewUserTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) { MessageBox.Show("用户名称不能为空。", "用户校验"); return; }
        try
        {
            _store.AddUser(new UserRecord(Guid.NewGuid(), name, true, DateTimeOffset.UtcNow));
            NewUserTextBox.Clear(); ReloadDerivedData(); StatusText.Text = $"用户已添加：{name}";
        }
        catch (Exception ex) { MessageBox.Show(RedactForUi(ex.Message), "添加用户失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ToggleUser_Click(object sender, RoutedEventArgs e)
    {
        if (UserGrid.SelectedItem is not UserRow row) { MessageBox.Show("请先选择用户。", "用户操作"); return; }
        _store.SetUserEnabled(row.Id, !row.Enabled);
        ReloadDerivedData(); StatusText.Text = $"用户“{row.Name}”已{(!row.Enabled ? "启用" : "禁用")}。";
    }

    private void AddBlacklist_Click(object sender, RoutedEventArgs e) => AddAccessRule(NewBlacklistTextBox, "Blacklist");
    private void AddWhitelist_Click(object sender, RoutedEventArgs e) => AddAccessRule(NewWhitelistTextBox, "Whitelist");
    private void ToggleBlacklist_Click(object sender, RoutedEventArgs e) => ToggleAccessRule(BlacklistGrid, "Blacklist");
    private void ToggleWhitelist_Click(object sender, RoutedEventArgs e) => ToggleAccessRule(WhitelistGrid, "Whitelist");
    private void DeleteBlacklist_Click(object sender, RoutedEventArgs e) => DeleteAccessRule(BlacklistGrid, "Blacklist");
    private void DeleteWhitelist_Click(object sender, RoutedEventArgs e) => DeleteAccessRule(WhitelistGrid, "Whitelist");

    private void ToggleAccessRule(System.Windows.Controls.DataGrid grid, string type)
    {
        if (grid.SelectedItem is not AccessRuleRow row) { MessageBox.Show("请先选择规则。", "规则操作"); return; }
        _store.SetAccessRuleEnabled(row.Id, !row.Enabled);
        ReloadDerivedData(); StatusText.Text = $"{type} 规则已{(!row.Enabled ? "启用" : "禁用")}：{row.Value}";
    }

    private void DeleteAccessRule(System.Windows.Controls.DataGrid grid, string type)
    {
        if (grid.SelectedItem is not AccessRuleRow row) { MessageBox.Show("请先选择规则。", "规则操作"); return; }
        if (MessageBox.Show($"确定删除 {type} 规则“{row.Value}”？", "规则操作确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _store.DeleteAccessRule(row.Id); ReloadDerivedData(); StatusText.Text = $"{type} 规则已删除：{row.Value}";
    }

    private void AddAccessRule(System.Windows.Controls.TextBox input, string type)
    {
        var value = input.Text.Trim();
        if (!IsValidAddressOrCidr(value)) { MessageBox.Show("请输入有效 IPv4/IPv6 地址或 CIDR。", "规则校验"); return; }
        try
        {
            _store.AddAccessRule(new AccessRule(Guid.NewGuid(), type, value, "由管理员添加", true, DateTimeOffset.UtcNow));
            input.Clear(); ReloadDerivedData(); StatusText.Text = $"{type} 规则已添加：{value}";
        }
        catch (Exception ex) { MessageBox.Show(RedactForUi(ex.Message), "添加规则失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private static bool IsValidAddressOrCidr(string value)
    {
        if (IPAddress.TryParse(value, out _)) return true;
        var parts = value.Split('/');
        return parts.Length == 2 && IPAddress.TryParse(parts[0], out var address) && int.TryParse(parts[1], out var prefix) && prefix >= 0 && prefix <= (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128);
    }

    private Server ApplyDetectedServerRuntime(Server server, ParsedWireGuardDump parsed, string? detectedSubnet)
    {
        var detectedInterface = ResolveInterface(server, parsed.Interfaces);
        var detectedPort = parsed.ListenPorts.TryGetValue(detectedInterface, out var port) ? port : server.WireGuardPort;
        var effectiveSubnet = detectedSubnet ?? server.VpnSubnet;
        if (detectedInterface == server.WireGuardInterface && detectedPort == server.WireGuardPort && effectiveSubnet == server.VpnSubnet) return server;

        var updated = server with { WireGuardInterface = detectedInterface, WireGuardPort = detectedPort, VpnSubnet = effectiveSubnet };
        _store.UpdateServerRuntime(updated, server.WireGuardPort, server.WireGuardInterface, server.VpnSubnet);
        var managementSelected = ServerManagementGrid.SelectedItem is Server management && management.Id == server.Id;
        var dashboardSelected = ServerGrid.SelectedItem is Server dashboard && dashboard.Id == server.Id;
        var index = _servers.ToList().FindIndex(x => x.Id == server.Id);
        if (index >= 0) _servers[index] = updated;
        ServerGrid.Items.Refresh();
        ServerManagementGrid.Items.Refresh();
        if (managementSelected) ServerManagementGrid.SelectedItem = updated;
        if (dashboardSelected) ServerGrid.SelectedItem = updated;
        DeviceServerComboBox.Items.Refresh();
        MigrationTargetComboBox.Items.Refresh();
        UpdateSettingsDescriptions();
        return updated;
    }

    private string ResolveSshUser(Server server) => string.IsNullOrWhiteSpace(SshUserTextBox.Text) ? server.SshUser : SshUserTextBox.Text.Trim();

    private void UpdateSettingsDescriptions()
    {
        var english = (LanguageComboBox.SelectedValue?.ToString() ?? "zh-CN").StartsWith("en", StringComparison.OrdinalIgnoreCase);
        OnlineThresholdDescriptionText.Text = english
            ? $"Devices with a handshake in the last {ParseOnlineThreshold()} seconds"
            : $"最近 {ParseOnlineThreshold()} 秒内完成握手的设备";
        ConfiguredPortsText.Text = english
            ? (_servers.Count == 0 ? "No servers configured." : "Current records: " + string.Join(", ", _servers.Select(x => $"{x.Name} {x.WireGuardPort}")) + ". Synchronization corrects local records using the actual VPS listening ports.")
            : (_servers.Count == 0 ? "尚未配置服务器。" : "当前记录：" + string.Join("，", _servers.Select(x => $"{x.Name} {x.WireGuardPort}")) + "。同步时会自动用 VPS 实际监听端口校正本地记录。");
    }

    private SshConnectionOptions? ChooseSshOptions(Server server)
    {
        var dialog = new OpenFileDialog { Title = $"选择 {server.Name} 的 SSH 私钥（只在内存中使用）", Filter = "SSH 私钥|*|所有文件|*.*", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return null;
        _store.SetSetting("LastSshKeyPath", dialog.FileName);
        return new SshConnectionOptions(server.Host, server.SshPort, ResolveSshUser(server), dialog.FileName);
    }

    private void ReplaceDevice(DeviceRow row, Device device)
    {
        var index = _devices.IndexOf(row);
        if (index >= 0) _devices[index] = DeviceRow.From(device, row.ServerName);
    }

    private void ReplacePeer(PeerRow row, PeerRow replacement)
    {
        var index = _peers.IndexOf(row);
        if (index >= 0) _peers[index] = replacement;
        var unknownIndex = _unknownPeers.IndexOf(row);
        if (unknownIndex >= 0) _unknownPeers[unknownIndex] = replacement;
    }

    private void ReloadDerivedData()
    {
        _onlineDevices.Clear();
        foreach (var peer in _peers.Where(x => x.Status == "Online"))
            _onlineDevices.Add(OnlineRow.From(peer));
        _unknownPeers.Clear();
        foreach (var peer in _peers.Where(x => x.Status is "Unknown Peer" or "Ignored Peer")) _unknownPeers.Add(peer);
        _history.Clear();
        foreach (var session in _store.LoadConnectionSessions())
        {
            var device = _devices.FirstOrDefault(x => x.Device.Id == session.DeviceId);
            var server = _servers.FirstOrDefault(x => x.Id == session.ServerId);
            _history.Add(new HistoryRow(device?.Name ?? "—", server?.Name ?? "—", session.VpnIp, session.ConnectedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), session.DisconnectedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "在线", session.Rx, session.Tx));
        }
        _traffic.Clear();
        foreach (var total in _store.LoadTrafficTotals())
        {
            var device = total.DeviceId is null ? null : _devices.FirstOrDefault(x => x.Device.Id == total.DeviceId);
            var server = _servers.FirstOrDefault(x => x.Id == total.ServerId);
            _traffic.Add(new TrafficRow(device?.Name ?? total.PublicKey, server?.Name ?? "—", total.RxTotal, total.TxTotal, total.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")));
        }
        _auditLogs.Clear();
        foreach (var log in _store.LoadAuditLogs()) _auditLogs.Add(new AuditRow(log.OccurredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), log.Action, log.Details));
        _users.Clear();
        foreach (var user in _store.LoadUsers()) _users.Add(new UserRow(user.Id, user.Name, user.Enabled, user.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")));
        _blacklist.Clear();
        foreach (var rule in _store.LoadAccessRules("Blacklist")) _blacklist.Add(AccessRuleRow.From(rule));
        _whitelist.Clear();
        foreach (var rule in _store.LoadAccessRules("Whitelist")) _whitelist.Add(AccessRuleRow.From(rule));
        _securityEvents.Clear();
        foreach (var item in _store.LoadSecurityEvents(50)) _securityEvents.Add(SecurityEventRow.From(item));
        UpdateDashboardCounts();
    }

    private static string BuildClientConfiguration(string privateKey, string vpnIp, string serverPublicKey, string host, int port) => $"[Interface]\nPrivateKey = {privateKey}\nAddress = {vpnIp}/32\nDNS = 1.1.1.1\n\n[Peer]\nPublicKey = {serverPublicKey}\nEndpoint = {FormatEndpoint(host, port)}\nAllowedIPs = 0.0.0.0/0\nPersistentKeepalive = 25\n";

    private static string ResolveInterface(Server server, IReadOnlyList<string> detected)
    {
        if (detected.Contains(server.WireGuardInterface, StringComparer.Ordinal)) return server.WireGuardInterface;
        if (detected.Count == 1) return detected[0];
        throw new InvalidOperationException($"未找到配置的 WireGuard Interface“{server.WireGuardInterface}”；检测到：{string.Join(", ", detected)}。请在服务器设置中修正 Interface。");
    }

    private static string FormatEndpoint(string host, int port) => host.Contains(':') && !host.StartsWith("[", StringComparison.Ordinal) ? $"[{host}]:{port}" : $"{host}:{port}";

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(result) ? "wireguard-device" : result;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed record DeviceRow(Device Device, string ServerName)
    {
        public string Name => Device.Name; public string UserName => Device.UserName; public string PublicKey => Device.PublicKey; public string VpnIp => Device.VpnIp; public bool Enabled => Device.Enabled;
        public static DeviceRow From(Device device, string serverName) => new(device, serverName);
    }

    private sealed record OnlineRow(string DeviceName, string ServerName, string VpnIp, string PublicIp, string LatestHandshake)
    {
        public static OnlineRow From(PeerRow peer) => new(peer.MatchedDeviceName, peer.ServerName, peer.AllowedIps, peer.PublicIp, peer.LatestHandshake);
    }

    private sealed record HistoryRow(string DeviceName, string ServerName, string VpnIp, string ConnectedAt, string DisconnectedAt, long Rx, long Tx);

    private sealed record TrafficRow(string DeviceName, string ServerName, long RxTotal, long TxTotal, string UpdatedAt);

    private sealed record AuditRow(string OccurredAt, string Action, string Details);

    private sealed record UserRow(Guid Id, string Name, bool Enabled, string CreatedAt);

    private sealed record AccessRuleRow(Guid Id, string Value, string Description, bool Enabled)
    {
        public static AccessRuleRow From(AccessRule rule) => new(rule.Id, rule.Value, rule.Description, rule.Enabled);
    }

    private sealed record SecurityEventRow(string OccurredAt, string Type, string Description)
    {
        public static SecurityEventRow From(SecurityEvent item) => new(item.OccurredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), item.Type, item.Description);
    }

    private sealed record PeerRow(Guid ServerId, string ServerName, string Interface, string PublicKey, string AllowedIps, string Endpoint, string LatestHandshake, long TransferRx, long TransferTx, string Status, string MatchedDeviceName = "—", string PublicIp = "—", bool IsIgnored = false)
    {
        public static PeerRow From(Server server, WireGuardPeerSnapshot peer, bool ignored = false) => new(server.Id, server.Name, peer.Interface, peer.PublicKey, peer.AllowedIps, peer.Endpoint ?? "—", peer.LatestHandshake?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "—", peer.TransferRx, peer.TransferTx, peer.IsUnknown ? ignored ? "Ignored Peer" : "Unknown Peer" : peer.IsOnline ? "Online" : "Offline", peer.MatchedDevice?.Name ?? "—", peer.PublicIp ?? "—", ignored);
    }
}
