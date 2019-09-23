using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Substrate.ContentPipeline.Primitives.Configuration;
using Substrate.Edge.Caching;
using Substrate.Edge.Configuration;
using Substrate.MediaWiki.Configuration;
using Substrate.MediaWiki.Remote;

namespace Substrate.Edge
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc(options =>
            {
                options.EnableEndpointRouting = false;
            }).SetCompatibilityVersion(CompatibilityVersion.Version_3_0);

            services.Configure<ApiCredentials>(Configuration.GetSection(nameof(ApiCredentials)));
            services.Configure<CachingConfig>(Configuration.GetSection(nameof(CachingConfig)));
            services.Configure<ServiceBusConfig>(Configuration.GetSection(nameof(ServiceBusConfig)));

            services.AddSingleton<MediaWikiApiServices>();
            services.AddSingleton<PageRepository>();
            services.AddHostedService<PageCacheUpdater>();

            services.AddApplicationInsightsTelemetry();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.EnvironmentName == "Development")
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseCors(builder =>
            {
                builder.AllowAnyOrigin();
            });
            
            app.UseMvc(routes =>
            {
                routes.MapRoute("mainpage-forwarded", "/",
                    new { controller = "Pages", action = "Index" }
                );

                routes.MapRoute("metadata", "/Special:EdgeCacheStatus/{**id}",
                    new { controller = "Pages", action = "GetMetadata" }
                );

                routes.MapRoute("default", "{*url}",
                    new { controller = "Pages", action = "GetPage" }
                );
            });
        }
    }
}
