using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Host.Internal;
using OmniSharp.Host.Loader;
using OmniSharp.Plugins;
using OmniSharp.Services;
using OmniSharp.Stdio;
using OmniSharp.Stdio.Services;
using OmniSharp.Utilities;

namespace OmniSharp.Http
{
    public class Program
    {
        public static int Main(string[] args)
        {
            var application = new OmniSharpHttpCommandLineApplication();
            application.OnExecute(() =>
            {
                var environment = application.CreateEnvironment();
                var writer = new SharedConsoleWriter();
                var plugins = application.CreatePluginAssemblies();

                var program = new Program(environment, writer, plugins, application.Port, application.Interface);
                program.Start();

                return 0;
            });
            return application.Execute(args);
        }

        private readonly IOmniSharpEnvironment _environment;
        private readonly ISharedTextWriter _sharedTextWriter;
        private readonly PluginAssemblies _pluginAssemblies;
        private readonly int _serverPort;
        private readonly string _serverInterface;

        public Program(
            IOmniSharpEnvironment environment,
            ISharedTextWriter sharedTextWriter,
            PluginAssemblies pluginAssemblies,
            int serverPort,
            string serverInterface)
        {
            _environment = environment;
            _sharedTextWriter = sharedTextWriter;
            _pluginAssemblies = pluginAssemblies;
            _serverPort = serverPort;
            _serverInterface = serverInterface;
        }

        public void Start()
        {
            var config = new ConfigurationBuilder()
                .AddCommandLine(new[] { "--server.urls", $"http://{_serverInterface}:{_serverPort}" });

            var builder = new WebHostBuilder()
                .UseConfiguration(config.Build())
                .UseEnvironment("OmniSharp")
                .ConfigureServices(serviceCollection =>
                {
                    serviceCollection.AddSingleton(_environment);
                    serviceCollection.AddSingleton(_sharedTextWriter);
                    serviceCollection.AddSingleton(_pluginAssemblies);
                })
                .UseStartup(typeof(Startup))
                .UseKestrel();

            using (var app = builder.Build())
            {
                app.Start();

                var appLifeTime = app.Services.GetRequiredService<IApplicationLifetime>();

                Console.CancelKeyPress += (sender, e) =>
                {
                    appLifeTime.StopApplication();
                    e.Cancel = true;
                };

                if (_environment.HostProcessId != -1)
                {
                    try
                    {
                        var hostProcess = Process.GetProcessById(_environment.HostProcessId);
                        hostProcess.EnableRaisingEvents = true;
                        hostProcess.OnExit(() => appLifeTime.StopApplication());
                    }
                    catch
                    {
                        // If the process dies before we get here then request shutdown
                        // immediately
                        appLifeTime.StopApplication();
                    }
                }

                appLifeTime.ApplicationStopping.WaitHandle.WaitOne();
            }
        }
    }

    public class OmniSharpHttpCommandLineApplication : OmniSharpCommandLineApplication
    {
        private readonly CommandOption _serverInterface;
        private readonly CommandOption _port;

        public OmniSharpHttpCommandLineApplication() : base()
        {
            _port = Application.Option("-p | --_port", "OmniSharp _port (defaults to 2000).", CommandOptionType.SingleValue);
            _serverInterface = Application.Option("-i | --interface", "Server interface address (defaults to 'localhost').", CommandOptionType.SingleValue);
        }


        public int Port => _port.GetValueOrDefault(2000);
        public string Interface => _serverInterface.GetValueOrDefault("localhost");
    }
}
