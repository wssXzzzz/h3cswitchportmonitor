# H3C Switch Port Monitor

Windows 服务程序，用 SNMP 轮询 H3C 交换机端口状态，发现端口 `ifOperStatus` 变化后推送到飞书群机器人。端口备注读取标准 IF-MIB 的 `ifAlias` 字段，对应 H3C 接口下配置的 `description`。

## 功能

- 读取端口名：`ifName`，OID `1.3.6.1.2.1.31.1.1.1.1`
- 读取端口描述：`ifDescr`，OID `1.3.6.1.2.1.2.2.1.2`
- 读取管理状态：`ifAdminStatus`，OID `1.3.6.1.2.1.2.2.1.7`
- 读取运行状态：`ifOperStatus`，OID `1.3.6.1.2.1.2.2.1.8`
- 读取端口备注：`ifAlias`，OID `1.3.6.1.2.1.31.1.1.1.18`
- 本地持久化上一轮端口状态，服务重启后仍可发现停机期间发生的状态变化
- 支持飞书机器人签名校验 `Secret`
- 交换机 SNMP 读取失败和恢复时可选推送

## 交换机侧配置

示例仅供参考，实际命令以你的 H3C 设备型号和 Comware 版本为准：

```text
snmp-agent
snmp-agent sys-info version v2c
snmp-agent community read public
```

接口备注示例：

```text
interface GigabitEthernet1/0/1
 description 上联-防火墙
```

生产环境建议使用独立只读 community，并通过 ACL 限制监控服务器 IP。

## 配置

编辑发布目录中的 `appsettings.json`：

```json
{
  "Monitor": {
    "PollIntervalSeconds": 10,
    "Feishu": {
      "WebhookUrl": "https://open.feishu.cn/open-apis/bot/v2/hook/替换为你的机器人token",
      "Secret": "如果机器人启用了签名校验则填写"
    },
    "Switches": [
      {
        "Name": "核心交换机-1",
        "Host": "192.168.1.1",
        "Community": "public",
        "Version": "V2C",
        "IncludeNamePrefixes": [
          "GigabitEthernet",
          "Ten-GigabitEthernet",
          "FortyGigE",
          "HundredGigE",
          "Bridge-Aggregation"
        ]
      }
    ]
  }
}
```

如果只想监控指定端口，可以填写 `IncludeInterfaceIndexes`。如果想排除个别端口，填写 `ExcludeInterfaceIndexes`。

## 发布

在装有 .NET 8 SDK 的机器执行：

```powershell
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish
```

## 生成一键安装器

Windows 上执行：

```powershell
.\scripts\build-installer.ps1
```

macOS 或 Linux 上交叉构建 Windows 安装器：

```bash
./scripts/build-installer.sh
```

输出文件：

```text
artifacts\installer\H3CSwitchPortMonitorInstaller.exe
```

把这个 exe 复制到 Windows 服务器上，右键选择“以管理员身份运行”。安装器会自动：

- 释放服务程序到 `C:\H3CSwitchPortMonitor`
- 引导填写飞书机器人地址、SNMP community、交换机 IP 和轮询间隔
- 写入 `appsettings.json`
- 注册并启动 `H3CSwitchPortMonitor` Windows 服务

卸载：

```powershell
.\H3CSwitchPortMonitorInstaller.exe --uninstall
```

卸载并删除安装目录：

```powershell
.\H3CSwitchPortMonitorInstaller.exe --uninstall --remove-files
```

## 生成绿色版 zip

Windows 上执行：

```powershell
.\scripts\build-portable.ps1
```

macOS 或 Linux 上交叉构建：

```bash
./scripts/build-portable.sh
```

输出文件：

```text
artifacts\portable\H3CSwitchPortMonitor-portable-win-x64.zip
```

把 zip 复制到 Windows 电脑后直接解压。先双击 `edit-config.cmd` 修改配置，再双击 `run-console.cmd` 前台测试运行。需要后台长期运行时，右键 `install-service.cmd` 选择“以管理员身份运行”。

## 手动安装为 Windows 服务

以管理员身份打开 PowerShell：

```powershell
.\scripts\install-service.ps1 -ExePath "C:\H3CSwitchPortMonitor\H3CSwitchPortMonitor.exe"
```

卸载：

```powershell
.\scripts\uninstall-service.ps1
```

查看日志：

```powershell
Get-EventLog -LogName Application -Newest 50 | Where-Object Source -like "*H3C*"
```

## 飞书消息内容

端口状态变化时会发送类似内容：

```text
[端口状态变化]
设备：核心交换机-1 (192.168.1.1)
端口：GigabitEthernet1/0/1 (ifIndex 1)
状态：up -> down
管理状态：up
端口备注：上联-防火墙
时间：2026-04-16 16:30:00 +08:00
```
