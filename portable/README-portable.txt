H3C 交换机端口监控 - 绿色版

一、最简单的使用方式

1. 把整个压缩包解压到 Windows 电脑任意目录，例如：
   D:\H3CSwitchPortMonitor

2. 双击 edit-config.cmd，填写 appsettings.json：
   - Monitor:Feishu:WebhookUrl：飞书群机器人 Webhook 地址
   - Monitor:Feishu:Secret：飞书机器人签名密钥，未启用签名可留空
   - Monitor:Switches:Host：H3C 交换机 IP
   - Monitor:Switches:Community：SNMP community
   - Monitor:PollIntervalSeconds：轮询间隔秒数

3. 双击 run-console.cmd 前台运行。
   这个模式适合先测试 SNMP 和飞书是否正常。

二、长期后台运行

1. 右键 install-service.cmd，选择“以管理员身份运行”。
2. 安装完成后，Windows 服务名是：
   H3CSwitchPortMonitor
3. 修改 appsettings.json 后，双击 restart-service.cmd 重启服务。
4. 卸载服务时，右键 uninstall-service.cmd，选择“以管理员身份运行”。

三、网络要求

- Windows 电脑必须能访问交换机 UDP 161 端口。
- 交换机 SNMP ACL 需要放行这台 Windows 电脑的 IP。
- Windows 电脑必须能访问飞书机器人 Webhook，也就是 HTTPS 443 出站可达。

四、交换机侧 SNMP 示例

snmp-agent
snmp-agent sys-info version v2c
snmp-agent community read public

接口备注示例：

interface GigabitEthernet1/0/1
 description 上联-防火墙

程序读取的端口备注是 IF-MIB 的 ifAlias，通常对应 H3C 接口 description。
