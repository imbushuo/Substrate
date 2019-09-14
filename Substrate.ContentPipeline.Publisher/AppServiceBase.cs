// Copyright 2019 The Lawrence Industry and its affiliates. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.ApplicationInsights;
using Substrate.ContentPipeline.Publisher.Configuration;
using Substrate.ContentPipeline.Publisher.Remote;

namespace Substrate.ContentPipeline.Publisher
{
    public class AppServiceBase
    {
        private IServiceCollection ServiceCollection { get; }
        private IServiceProvider ServiceProvider { get; }

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
            services.AddOptions();
            services.Configure<ApiCredentials>(
                Configuration.GetSection(nameof(ApiCredentials)));

            services.AddLogging(logger =>
            {
                logger.AddConsole();
#if DEBUG
                logger.AddDebug();
#endif
                logger.AddApplicationInsights(
                    Configuration["Telemetry:InstrumentationKey"]);
                logger.AddFilter<ApplicationInsightsLoggerProvider>(
                    "", LogLevel.Information);
            });

            services.AddScoped<MediaWikiApiServices>();
        }

        public async Task RunMainLoopAsync(CancellationToken cancellationToken)
        {
            var scopeFactory = ServiceProvider.GetRequiredService<IServiceScopeFactory>();
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

                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                }
            }
        }
    }
}
