// Copyright 2019 The Lawrence Industry and its affiliates. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.ApplicationInsights;
using Substrate.ContentPipeline.Publisher.Configuration;
using Substrate.ContentPipeline.Publisher.DataAccess;
using Substrate.ContentPipeline.Publisher.Remote;

namespace Substrate.ContentPipeline.Publisher
{
    public class AppServiceBase
    {
        private IServiceCollection ServiceCollection { get; }
        private IServiceProvider ServiceProvider { get; }

        private InMemoryChannel _channel;

        public IConfigurationRoot Configuration { get; }

        public AppServiceBase(string[] args)
        {
            var builder = new ConfigurationBuilder()
                .AddCommandLine(args)
                .AddEnvironmentVariables()
                .AddJsonFile("appsettings.json")
#if DEBUG
                .AddJsonFile("appsettings.Development.json", true);
#else
                .AddJsonFile("appsettings.Production.json", true);
#endif

            Configuration = builder.Build();

            ServiceCollection = new ServiceCollection();
            AddServices(ServiceCollection);
            ServiceProvider = ServiceCollection.BuildServiceProvider();
        }

        private void AddServices(IServiceCollection services)
        {
            services.AddOptions()
                .Configure<ApiCredentials>(
                    Configuration.GetSection(nameof(ApiCredentials)))
                .Configure<RuntimeDirectory>(
                    Configuration.GetSection(nameof(RuntimeDirectory)));

            _channel = new InMemoryChannel();
            services.Configure<TelemetryConfiguration>((config) =>
            {
                config.TelemetryChannel = _channel;
            });

            services.AddLogging(logger =>
            {
                logger.AddConsole();
#if DEBUG
                logger.AddDebug();
#endif
                logger.AddFilter<ApplicationInsightsLoggerProvider>(
                    "", LogLevel.Information);

                logger.AddApplicationInsights(
                    Configuration["Telemetry:InstrumentationKey"]);
            });

            services.AddScoped<MediaWikiApiServices>();
            services.AddSingleton<LocalStateRepository>();
        }

        public async Task RunMainLoopAsync(CancellationToken cancellationToken)
        {
            var scopeFactory = ServiceProvider.GetRequiredService<IServiceScopeFactory>();
            var dbInstance = ServiceProvider.GetRequiredService<LocalStateRepository>();

            using (var scope = scopeFactory.CreateScope())
            {
                var apiSvc = scope.ServiceProvider.GetRequiredService<MediaWikiApiServices>();
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppServiceBase>>();

                var identity = await apiSvc.LoginAsync();
                if (identity != null)
                {
                    logger.LogInformation($"MW logged in as {identity.Identity.Name}");
                }
                else
                {
                    return;
                }

                DateTimeOffset lastAccess;
                lastAccess = dbInstance.Get<DateTimeOffset>(nameof(lastAccess));
                while (!cancellationToken.IsCancellationRequested)
                {
                    var currentTime = DateTimeOffset.Now; 
                    var changeLists = await apiSvc.GetRecentChangesSinceAsync(lastAccess);

                    logger.LogInformation($"{changeLists.Count} item(s) retrieved");
                    if (changeLists.Count > 0)
                    {
                        lastAccess = currentTime;
                        dbInstance.Put(nameof(lastAccess), lastAccess);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                }
            }

            _channel.Flush();
            dbInstance.Dispose();
        }
    }
}
