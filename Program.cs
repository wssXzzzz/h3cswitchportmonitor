using H3CSwitchPortMonitor;
using H3CSwitchPortMonitor.Models;
using H3CSwitchPortMonitor.Services;

try
{
    Host.CreateDefaultBuilder(args)
        .UseWindowsService(options =>
        {
            options.ServiceName = "H3CSwitchPortMonitor";
        })
        .ConfigureServices((context, services) =>
        {
            services.Configure<MonitorOptions>(context.Configuration.GetSection("Monitor"));
            services.AddHttpClient<FeishuNotifier>();
            services.AddSingleton<ISnmpClient, SharpSnmpClient>();
            services.AddSingleton<WindowsFirewallConfigurator>();
            services.AddSingleton<PortStateStore>();
            services.AddHostedService<Worker>();
        })
        .Build()
        .Run();
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    StartupDiagnostics.RecordFatal("Program failed to start.", ex);
    Environment.ExitCode = 1;
}
