$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$script:ConfigPath = Join-Path $PSScriptRoot "appsettings.json"
$script:Switches = New-Object System.Collections.ArrayList
$script:SelectedIndex = -1
$script:IsLoadingSwitch = $false

function Show-Error {
    param([string]$Message)
    [System.Windows.Forms.MessageBox]::Show(
        $Message,
        "配置错误",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
}

function Show-Info {
    param([string]$Message)
    [System.Windows.Forms.MessageBox]::Show(
        $Message,
        "提示",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
}

function New-DefaultSwitch {
    return [ordered]@{
        Name = "新交换机"
        Host = ""
        Port = 161
        Community = ""
        Version = "V2C"
        TimeoutMs = 5000
        MaxRepetitions = 10
        TextEncoding = ""
        IncludeNamePrefixes = @("GigabitEthernet", "Ten-GigabitEthernet", "FortyGigE", "HundredGigE", "Bridge-Aggregation")
        IncludeInterfaceIndexes = @()
        ExcludeInterfaceIndexes = @()
    }
}

function New-DefaultConfig {
    return [ordered]@{
        Logging = [ordered]@{
            LogLevel = [ordered]@{
                Default = "Information"
                "Microsoft.Hosting.Lifetime" = "Information"
            }
            EventLog = [ordered]@{
                LogLevel = [ordered]@{
                    Default = "Information"
                }
            }
        }
        Monitor = [ordered]@{
            PollIntervalSeconds = 10
            AlertOnFirstPoll = $false
            AlertDeviceErrors = $true
            AlertDeviceRecovery = $true
            RetryCount = 2
            RetryDelayMs = 1000
            SnmpTextEncoding = "GB18030"
            StateFile = "state/port-state.json"
            Firewall = [ordered]@{
                EnsureSnmpOutboundRule = $true
                RuleName = "H3CSwitchPortMonitor SNMP Outbound"
            }
            Feishu = [ordered]@{
                WebhookUrl = ""
                Secret = ""
            }
            Switches = @()
        }
    }
}

function Get-Prop {
    param($Object, [string]$Name, $Default)
    if ($null -eq $Object) { return $Default }
    $prop = $Object.PSObject.Properties[$Name]
    if ($null -eq $prop -or $null -eq $prop.Value) { return $Default }
    return $prop.Value
}

function To-StringArray {
    param($Value)
    if ($null -eq $Value) { return @() }
    if ($Value -is [string]) {
        if ([string]::IsNullOrWhiteSpace($Value)) { return @() }
        return @($Value)
    }

    $items = @()
    foreach ($item in $Value) {
        if ($null -ne $item -and -not [string]::IsNullOrWhiteSpace([string]$item)) {
            $items += [string]$item
        }
    }
    return $items
}

function To-IntArray {
    param($Value)
    if ($null -eq $Value) { return @() }
    $items = @()
    foreach ($item in $Value) {
        $text = [string]$item
        if ([string]::IsNullOrWhiteSpace($text)) { continue }
        $parsed = 0
        if ([int]::TryParse($text, [ref]$parsed)) {
            $items += $parsed
        }
    }
    return $items
}

function Split-TextList {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return @() }
    return @(
        $Text -split "[`r`n,;，；]+" |
            ForEach-Object { $_.Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
}

function Split-IntList {
    param([string]$Text, [string]$FieldName)
    $items = @()
    foreach ($item in (Split-TextList $Text)) {
        $parsed = 0
        if (-not [int]::TryParse($item, [ref]$parsed)) {
            throw "$FieldName 里包含非整数：$item"
        }
        $items += $parsed
    }
    return $items
}

function Get-IntFromTextBox {
    param(
        [System.Windows.Forms.TextBox]$TextBox,
        [string]$FieldName,
        [int]$Minimum
    )
    $value = 0
    if (-not [int]::TryParse($TextBox.Text.Trim(), [ref]$value)) {
        throw "$FieldName 必须是整数。"
    }
    if ($value -lt $Minimum) {
        throw "$FieldName 不能小于 $Minimum。"
    }
    return $value
}

function Convert-SwitchFromJson {
    param($Item)
    return [ordered]@{
        Name = [string](Get-Prop $Item "Name" "")
        Host = [string](Get-Prop $Item "Host" "")
        Port = [int](Get-Prop $Item "Port" 161)
        Community = [string](Get-Prop $Item "Community" "")
        Version = [string](Get-Prop $Item "Version" "V2C")
        TimeoutMs = [int](Get-Prop $Item "TimeoutMs" 5000)
        MaxRepetitions = [int](Get-Prop $Item "MaxRepetitions" 10)
        TextEncoding = [string](Get-Prop $Item "TextEncoding" "")
        IncludeNamePrefixes = @(To-StringArray (Get-Prop $Item "IncludeNamePrefixes" @()))
        IncludeInterfaceIndexes = @(To-IntArray (Get-Prop $Item "IncludeInterfaceIndexes" @()))
        ExcludeInterfaceIndexes = @(To-IntArray (Get-Prop $Item "ExcludeInterfaceIndexes" @()))
    }
}

function Load-Config {
    $script:Switches.Clear()

    if (-not (Test-Path $script:ConfigPath)) {
        $script:Config = New-DefaultConfig
        [void]$script:Switches.Add((New-DefaultSwitch))
        return
    }

    try {
        $raw = Get-Content $script:ConfigPath -Raw -Encoding UTF8
        $loaded = $raw | ConvertFrom-Json
    }
    catch {
        throw "读取 appsettings.json 失败：$($_.Exception.Message)"
    }

    $defaults = New-DefaultConfig
    $monitor = Get-Prop $loaded "Monitor" $null
    $feishu = Get-Prop $monitor "Feishu" $null
    $firewall = Get-Prop $monitor "Firewall" $null

    $script:Config = $defaults
    $script:Config.Monitor.PollIntervalSeconds = [int](Get-Prop $monitor "PollIntervalSeconds" 10)
    $script:Config.Monitor.AlertOnFirstPoll = [bool](Get-Prop $monitor "AlertOnFirstPoll" $false)
    $script:Config.Monitor.AlertDeviceErrors = [bool](Get-Prop $monitor "AlertDeviceErrors" $true)
    $script:Config.Monitor.AlertDeviceRecovery = [bool](Get-Prop $monitor "AlertDeviceRecovery" $true)
    $script:Config.Monitor.RetryCount = [int](Get-Prop $monitor "RetryCount" 2)
    $script:Config.Monitor.RetryDelayMs = [int](Get-Prop $monitor "RetryDelayMs" 1000)
    $script:Config.Monitor.SnmpTextEncoding = [string](Get-Prop $monitor "SnmpTextEncoding" "GB18030")
    $script:Config.Monitor.StateFile = [string](Get-Prop $monitor "StateFile" "state/port-state.json")
    $script:Config.Monitor.Firewall.EnsureSnmpOutboundRule = [bool](Get-Prop $firewall "EnsureSnmpOutboundRule" $true)
    $script:Config.Monitor.Firewall.RuleName = [string](Get-Prop $firewall "RuleName" "H3CSwitchPortMonitor SNMP Outbound")
    $script:Config.Monitor.Feishu.WebhookUrl = [string](Get-Prop $feishu "WebhookUrl" "")
    $script:Config.Monitor.Feishu.Secret = [string](Get-Prop $feishu "Secret" "")

    $switchItems = Get-Prop $monitor "Switches" @()
    foreach ($item in $switchItems) {
        [void]$script:Switches.Add((Convert-SwitchFromJson $item))
    }
    if ($script:Switches.Count -eq 0) {
        [void]$script:Switches.Add((New-DefaultSwitch))
    }
}

function New-Label {
    param([string]$Text, [int]$X, [int]$Y, [int]$Width = 110)
    $label = New-Object System.Windows.Forms.Label
    $label.Text = $Text
    $label.SetBounds($X, $Y + 4, $Width, 22)
    return $label
}

function New-TextBox {
    param([int]$X, [int]$Y, [int]$Width = 220)
    $box = New-Object System.Windows.Forms.TextBox
    $box.SetBounds($X, $Y, $Width, 24)
    return $box
}

function New-MultiTextBox {
    param([int]$X, [int]$Y, [int]$Width, [int]$Height)
    $box = New-Object System.Windows.Forms.TextBox
    $box.Multiline = $true
    $box.ScrollBars = "Vertical"
    $box.SetBounds($X, $Y, $Width, $Height)
    return $box
}

function Refresh-SwitchList {
    $script:IsLoadingSwitch = $true
    try {
        $listSwitches.Items.Clear()
        for ($i = 0; $i -lt $script:Switches.Count; $i++) {
            $item = $script:Switches[$i]
            $name = [string]$item.Name
            if ([string]::IsNullOrWhiteSpace($name)) { $name = "未命名交换机" }
            $hostText = [string]$item.Host
            if ([string]::IsNullOrWhiteSpace($hostText)) { $hostText = "未填写IP" }
            [void]$listSwitches.Items.Add("$name ($hostText)")
        }

        if ($script:Switches.Count -gt 0) {
            if ($script:SelectedIndex -lt 0 -or $script:SelectedIndex -ge $script:Switches.Count) {
                $script:SelectedIndex = 0
            }
            $listSwitches.SelectedIndex = $script:SelectedIndex
        }
    }
    finally {
        $script:IsLoadingSwitch = $false
    }
}

function Load-SwitchToFields {
    param([int]$Index)
    if ($Index -lt 0 -or $Index -ge $script:Switches.Count) { return }
    $script:IsLoadingSwitch = $true
    try {
        $item = $script:Switches[$Index]
        $txtName.Text = [string]$item.Name
        $txtHost.Text = [string]$item.Host
        $txtPort.Text = [string]$item.Port
        $txtCommunity.Text = [string]$item.Community
        $cmbVersion.Text = [string]$item.Version
        $txtTimeout.Text = [string]$item.TimeoutMs
        $txtMaxRepetitions.Text = [string]$item.MaxRepetitions
        $txtTextEncoding.Text = [string]$item.TextEncoding
        $txtPrefixes.Text = ([string[]]$item.IncludeNamePrefixes) -join [Environment]::NewLine
        $txtIncludeIndexes.Text = ([int[]]$item.IncludeInterfaceIndexes) -join [Environment]::NewLine
        $txtExcludeIndexes.Text = ([int[]]$item.ExcludeInterfaceIndexes) -join [Environment]::NewLine
    }
    finally {
        $script:IsLoadingSwitch = $false
    }
}

function Save-CurrentSwitchFromFields {
    if ($script:SelectedIndex -lt 0 -or $script:SelectedIndex -ge $script:Switches.Count) { return }

    $port = Get-IntFromTextBox $txtPort "SNMP 端口" 1
    $timeout = Get-IntFromTextBox $txtTimeout "超时时间" 1000
    $maxRepetitions = Get-IntFromTextBox $txtMaxRepetitions "MaxRepetitions" 1

    if ([string]::IsNullOrWhiteSpace($txtHost.Text)) {
        throw "交换机 IP 或域名不能为空。"
    }
    if ([string]::IsNullOrWhiteSpace($txtCommunity.Text)) {
        throw "SNMP Community 不能为空。"
    }

    $script:Switches[$script:SelectedIndex] = [ordered]@{
        Name = $txtName.Text.Trim()
        Host = $txtHost.Text.Trim()
        Port = $port
        Community = $txtCommunity.Text.Trim()
        Version = $cmbVersion.Text.Trim()
        TimeoutMs = $timeout
        MaxRepetitions = $maxRepetitions
        TextEncoding = $txtTextEncoding.Text.Trim()
        IncludeNamePrefixes = @(Split-TextList $txtPrefixes.Text)
        IncludeInterfaceIndexes = @(Split-IntList $txtIncludeIndexes.Text "IncludeInterfaceIndexes")
        ExcludeInterfaceIndexes = @(Split-IntList $txtExcludeIndexes.Text "ExcludeInterfaceIndexes")
    }
}

function Load-GlobalFields {
    $monitor = $script:Config.Monitor
    $txtWebhook.Text = [string]$monitor.Feishu.WebhookUrl
    $txtSecret.Text = [string]$monitor.Feishu.Secret
    $txtPollInterval.Text = [string]$monitor.PollIntervalSeconds
    $txtRetryCount.Text = [string]$monitor.RetryCount
    $txtRetryDelay.Text = [string]$monitor.RetryDelayMs
    $txtSnmpTextEncoding.Text = [string]$monitor.SnmpTextEncoding
    $chkFirewall.Checked = [bool]$monitor.Firewall.EnsureSnmpOutboundRule
}

function Save-GlobalFields {
    $script:Config.Monitor.PollIntervalSeconds = Get-IntFromTextBox $txtPollInterval "轮询间隔" 1
    $script:Config.Monitor.RetryCount = Get-IntFromTextBox $txtRetryCount "重试次数" 0
    $script:Config.Monitor.RetryDelayMs = Get-IntFromTextBox $txtRetryDelay "重试间隔" 0
    $script:Config.Monitor.SnmpTextEncoding = $txtSnmpTextEncoding.Text.Trim()
    if ([string]::IsNullOrWhiteSpace($script:Config.Monitor.SnmpTextEncoding)) {
        $script:Config.Monitor.SnmpTextEncoding = "GB18030"
    }
    $script:Config.Monitor.Firewall.EnsureSnmpOutboundRule = [bool]$chkFirewall.Checked
    $script:Config.Monitor.Feishu.WebhookUrl = $txtWebhook.Text.Trim()
    $script:Config.Monitor.Feishu.Secret = $txtSecret.Text.Trim()

    if ([string]::IsNullOrWhiteSpace($script:Config.Monitor.Feishu.WebhookUrl)) {
        throw "飞书机器人 WebhookUrl 不能为空。"
    }
}

function Build-ConfigForSave {
    Save-GlobalFields
    Save-CurrentSwitchFromFields
    $switchArray = @()
    foreach ($switch in $script:Switches) {
        $switchArray += $switch
    }
    $script:Config.Monitor.Switches = $switchArray
    return $script:Config
}

function Save-ConfigFile {
    $config = Build-ConfigForSave
    $backupPath = $script:ConfigPath + ".bak"
    if (Test-Path $script:ConfigPath) {
        Copy-Item $script:ConfigPath $backupPath -Force
    }
    $json = $config | ConvertTo-Json -Depth 20
    [System.IO.File]::WriteAllText($script:ConfigPath, $json, [System.Text.Encoding]::UTF8)
}

try {
    Load-Config
}
catch {
    Show-Error $_.Exception.Message
    Start-Process notepad.exe $script:ConfigPath
    exit 1
}

$form = New-Object System.Windows.Forms.Form
$form.Text = "H3C 交换机端口监控 - 配置工具"
$form.StartPosition = "CenterScreen"
$form.Size = New-Object System.Drawing.Size -ArgumentList 980, 720
$form.MinimumSize = New-Object System.Drawing.Size -ArgumentList 960, 680
$form.Font = New-Object System.Drawing.Font -ArgumentList "Microsoft YaHei UI", 9

$tabs = New-Object System.Windows.Forms.TabControl
$tabs.Dock = "Fill"
$form.Controls.Add($tabs)

$tabGlobal = New-Object System.Windows.Forms.TabPage
$tabGlobal.Text = "基础配置"
$tabs.TabPages.Add($tabGlobal)

$tabSwitches = New-Object System.Windows.Forms.TabPage
$tabSwitches.Text = "交换机"
$tabs.TabPages.Add($tabSwitches)

$txtWebhook = New-TextBox 150 25 730
$txtSecret = New-TextBox 150 65 360
$txtPollInterval = New-TextBox 150 115 100
$txtRetryCount = New-TextBox 150 155 100
$txtRetryDelay = New-TextBox 150 195 100
$txtSnmpTextEncoding = New-TextBox 150 235 120
$chkFirewall = New-Object System.Windows.Forms.CheckBox
$chkFirewall.Text = "自动创建出站 UDP SNMP 防火墙规则"
$chkFirewall.SetBounds(150, 275, 360, 26)

$tabGlobal.Controls.Add((New-Label "飞书 Webhook" 30 25))
$tabGlobal.Controls.Add($txtWebhook)
$tabGlobal.Controls.Add((New-Label "飞书 Secret" 30 65))
$tabGlobal.Controls.Add($txtSecret)
$tabGlobal.Controls.Add((New-Label "轮询间隔(秒)" 30 115))
$tabGlobal.Controls.Add($txtPollInterval)
$tabGlobal.Controls.Add((New-Label "重试次数" 30 155))
$tabGlobal.Controls.Add($txtRetryCount)
$tabGlobal.Controls.Add((New-Label "重试间隔(ms)" 30 195))
$tabGlobal.Controls.Add($txtRetryDelay)
$tabGlobal.Controls.Add((New-Label "备注编码" 30 235))
$tabGlobal.Controls.Add($txtSnmpTextEncoding)
$tabGlobal.Controls.Add($chkFirewall)

$globalTip = New-Object System.Windows.Forms.Label
$globalTip.Text = "端口备注出现问号时，优先保持 GB18030；如设备确认是 UTF-8，可改为 UTF-8。"
$globalTip.SetBounds(150, 315, 720, 26)
$tabGlobal.Controls.Add($globalTip)

$listSwitches = New-Object System.Windows.Forms.ListBox
$listSwitches.SetBounds(20, 20, 260, 520)
$tabSwitches.Controls.Add($listSwitches)

$btnAdd = New-Object System.Windows.Forms.Button
$btnAdd.Text = "添加"
$btnAdd.SetBounds(20, 555, 75, 30)
$tabSwitches.Controls.Add($btnAdd)

$btnDelete = New-Object System.Windows.Forms.Button
$btnDelete.Text = "删除"
$btnDelete.SetBounds(105, 555, 75, 30)
$tabSwitches.Controls.Add($btnDelete)

$btnDuplicate = New-Object System.Windows.Forms.Button
$btnDuplicate.Text = "复制"
$btnDuplicate.SetBounds(190, 555, 75, 30)
$tabSwitches.Controls.Add($btnDuplicate)

$detailX = 310
$txtName = New-TextBox ($detailX + 110) 20 200
$txtHost = New-TextBox ($detailX + 110) 60 200
$txtPort = New-TextBox ($detailX + 110) 100 80
$txtCommunity = New-TextBox ($detailX + 110) 140 200
$cmbVersion = New-Object System.Windows.Forms.ComboBox
$cmbVersion.DropDownStyle = "DropDownList"
[void]$cmbVersion.Items.Add("V2C")
[void]$cmbVersion.Items.Add("V1")
$cmbVersion.SetBounds(($detailX + 110), 180, 100, 24)
$txtTimeout = New-TextBox ($detailX + 110) 220 100
$txtMaxRepetitions = New-TextBox ($detailX + 110) 260 100
$txtTextEncoding = New-TextBox ($detailX + 110) 300 120

$tabSwitches.Controls.Add((New-Label "名称" $detailX 20))
$tabSwitches.Controls.Add($txtName)
$tabSwitches.Controls.Add((New-Label "IP/域名" $detailX 60))
$tabSwitches.Controls.Add($txtHost)
$tabSwitches.Controls.Add((New-Label "SNMP端口" $detailX 100))
$tabSwitches.Controls.Add($txtPort)
$tabSwitches.Controls.Add((New-Label "Community" $detailX 140))
$tabSwitches.Controls.Add($txtCommunity)
$tabSwitches.Controls.Add((New-Label "SNMP版本" $detailX 180))
$tabSwitches.Controls.Add($cmbVersion)
$tabSwitches.Controls.Add((New-Label "超时(ms)" $detailX 220))
$tabSwitches.Controls.Add($txtTimeout)
$tabSwitches.Controls.Add((New-Label "MaxRepetitions" $detailX 260))
$tabSwitches.Controls.Add($txtMaxRepetitions)
$tabSwitches.Controls.Add((New-Label "备注编码" $detailX 300))
$tabSwitches.Controls.Add($txtTextEncoding)

$txtPrefixes = New-MultiTextBox 650 40 260 120
$txtIncludeIndexes = New-MultiTextBox 650 210 260 100
$txtExcludeIndexes = New-MultiTextBox 650 360 260 100
$tabSwitches.Controls.Add((New-Label "端口名前缀" 650 15 160))
$tabSwitches.Controls.Add($txtPrefixes)
$tabSwitches.Controls.Add((New-Label "只监控ifIndex" 650 185 160))
$tabSwitches.Controls.Add($txtIncludeIndexes)
$tabSwitches.Controls.Add((New-Label "排除ifIndex" 650 335 160))
$tabSwitches.Controls.Add($txtExcludeIndexes)

$switchTip = New-Object System.Windows.Forms.Label
$switchTip.Text = "多值字段可每行一个，也可用逗号分隔。备注编码为空时使用基础配置里的 SnmpTextEncoding。"
$switchTip.SetBounds(310, 500, 600, 35)
$tabSwitches.Controls.Add($switchTip)

$btnSaveSwitch = New-Object System.Windows.Forms.Button
$btnSaveSwitch.Text = "保存当前交换机"
$btnSaveSwitch.SetBounds(310, 555, 140, 30)
$tabSwitches.Controls.Add($btnSaveSwitch)

$panelBottom = New-Object System.Windows.Forms.Panel
$panelBottom.Dock = "Bottom"
$panelBottom.Height = 55
$form.Controls.Add($panelBottom)
$panelBottom.BringToFront()

$btnSaveAll = New-Object System.Windows.Forms.Button
$btnSaveAll.Text = "保存配置"
$btnSaveAll.SetBounds(590, 12, 100, 30)
$panelBottom.Controls.Add($btnSaveAll)

$btnRaw = New-Object System.Windows.Forms.Button
$btnRaw.Text = "打开JSON"
$btnRaw.SetBounds(705, 12, 100, 30)
$panelBottom.Controls.Add($btnRaw)

$btnClose = New-Object System.Windows.Forms.Button
$btnClose.Text = "关闭"
$btnClose.SetBounds(820, 12, 100, 30)
$panelBottom.Controls.Add($btnClose)

$lblPath = New-Object System.Windows.Forms.Label
$lblPath.Text = "配置文件：$script:ConfigPath"
$lblPath.SetBounds(15, 18, 550, 24)
$panelBottom.Controls.Add($lblPath)

$listSwitches.Add_SelectedIndexChanged({
    if ($script:IsLoadingSwitch) { return }
    try {
        if ($script:SelectedIndex -ge 0 -and $script:SelectedIndex -lt $script:Switches.Count) {
            Save-CurrentSwitchFromFields
        }
        $script:SelectedIndex = $listSwitches.SelectedIndex
        Load-SwitchToFields $script:SelectedIndex
        Refresh-SwitchList
    }
    catch {
        Show-Error $_.Exception.Message
        $listSwitches.SelectedIndex = $script:SelectedIndex
    }
})

$btnAdd.Add_Click({
    try {
        Save-CurrentSwitchFromFields
        [void]$script:Switches.Add((New-DefaultSwitch))
        $script:SelectedIndex = $script:Switches.Count - 1
        Refresh-SwitchList
        Load-SwitchToFields $script:SelectedIndex
        $tabs.SelectedTab = $tabSwitches
    }
    catch {
        Show-Error $_.Exception.Message
    }
})

$btnDelete.Add_Click({
    if ($script:SelectedIndex -lt 0 -or $script:SelectedIndex -ge $script:Switches.Count) { return }
    $result = [System.Windows.Forms.MessageBox]::Show(
        "确定删除当前交换机配置吗？",
        "确认删除",
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Question)
    if ($result -ne [System.Windows.Forms.DialogResult]::Yes) { return }

    $script:Switches.RemoveAt($script:SelectedIndex)
    if ($script:Switches.Count -eq 0) {
        [void]$script:Switches.Add((New-DefaultSwitch))
    }
    if ($script:SelectedIndex -ge $script:Switches.Count) {
        $script:SelectedIndex = $script:Switches.Count - 1
    }
    Refresh-SwitchList
    Load-SwitchToFields $script:SelectedIndex
})

$btnDuplicate.Add_Click({
    try {
        Save-CurrentSwitchFromFields
        if ($script:SelectedIndex -lt 0 -or $script:SelectedIndex -ge $script:Switches.Count) { return }
        $copyJson = $script:Switches[$script:SelectedIndex] | ConvertTo-Json -Depth 20
        $copy = Convert-SwitchFromJson ($copyJson | ConvertFrom-Json)
        $copy.Name = "$($copy.Name)-复制"
        [void]$script:Switches.Add($copy)
        $script:SelectedIndex = $script:Switches.Count - 1
        Refresh-SwitchList
        Load-SwitchToFields $script:SelectedIndex
    }
    catch {
        Show-Error $_.Exception.Message
    }
})

$btnSaveSwitch.Add_Click({
    try {
        Save-CurrentSwitchFromFields
        Refresh-SwitchList
        Show-Info "当前交换机已暂存。点击“保存配置”后写入 appsettings.json。"
    }
    catch {
        Show-Error $_.Exception.Message
    }
})

$btnSaveAll.Add_Click({
    try {
        Save-ConfigFile
        Refresh-SwitchList
        Show-Info "配置已保存。已自动保留 appsettings.json.bak 备份。修改服务配置后请重启服务。"
    }
    catch {
        Show-Error $_.Exception.Message
    }
})

$btnRaw.Add_Click({
    Start-Process notepad.exe $script:ConfigPath
})

$btnClose.Add_Click({
    $form.Close()
})

Load-GlobalFields
$script:SelectedIndex = 0
Refresh-SwitchList
Load-SwitchToFields 0

[void]$form.ShowDialog()
