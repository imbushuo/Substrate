// Copyright 2019 The Lawrence Industry and its affiliates. All rights reserved.

using System;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Substrate.ContentPipeline.Publisher.DataAccess;
using Substrate.ContentPipeline.Publisher.Remote;

namespace Substrate.ContentPipeline.Publisher
{
    public class PipelineWorker
    {
        private ILogger<PipelineWorker> _logger;
        private MediaWikiApiServices _apiSvc;
        private LocalStateRepository _stateRepo;

        private DateTimeOffset _lastLogin;
        private static readonly TimeSpan LoginValidity = TimeSpan.FromDays(2);

        public PipelineWorker(MediaWikiApiServices apiService,
            LocalStateRepository stateRepo,
            ILogger<PipelineWorker> logger)
        {
            _apiSvc = apiService;
            _stateRepo = stateRepo;
            _logger = logger;
            _lastLogin = DateTimeOffset.MinValue;
        }

        private async Task<IPrincipal> RefreshSessionAsync()
        {
            var principal = await _apiSvc.LoginAsync();
            if (principal != null)
            {
                _logger.LogInformation($"MW logged in as {principal.Identity.Name}");
                _lastLogin = DateTimeOffset.UtcNow;
            }

            return principal;
        }

        public async Task RunMainLoopAsync(CancellationToken cancellationToken)
        {
            DateTimeOffset lastAccess;

            while (!cancellationToken.IsCancellationRequested)
            {
                // For long-run, token will self-refresh.
                if (DateTimeOffset.Now - _lastLogin > LoginValidity)
                {
                    _logger.LogInformation("Refresh MW token");

                    // 3 retries with some cool-down period
                    IPrincipal principal = null;
                    for (int i = 0; i < 3; i++)
                    {
                        principal = await RefreshSessionAsync();
                        if (principal == null)
                        {
                            _logger.LogWarning("MW Failed to log in; retries after 20s");
                            await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (principal == null)
                    {
                        _logger.LogError("MW failed to log in; worker exit");
                        return;
                    }
                }

                // Get last access time stamp from database
                lastAccess = _stateRepo.Get<DateTimeOffset>(nameof(lastAccess));
                var currentTime = DateTimeOffset.Now;
                var changeLists = await _apiSvc.GetRecentChangesSinceAsync(lastAccess);

                _logger.LogInformation($"{changeLists.Count} item(s) retrieved");

                // Update time stamp if new item retrieved
                if (changeLists.Count > 0)
                {
                    lastAccess = currentTime;
                    _stateRepo.Put(nameof(lastAccess), lastAccess);
                }

                // Enqueue update events into message topics

                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            }
        }
    }
}
