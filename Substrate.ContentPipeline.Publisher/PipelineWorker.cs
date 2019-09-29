// Copyright 2019 The Lawrence Industry and its affiliates. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Substrate.ContentPipeline.Primitives.Configuration;
using Substrate.ContentPipeline.Primitives.Models;
using Substrate.ContentPipeline.Publisher.Configuration;
using Substrate.ContentPipeline.Publisher.DataAccess;
using Substrate.ContributionGraph.Timeseries;
using Substrate.ContributionGraph.Timeseries.Models;
using Substrate.MediaWiki.Remote;

namespace Substrate.ContentPipeline.Publisher
{
    public class PipelineWorker
    {
        private IOptions<ServiceBusConfig> _sbConfig;
        private IOptions<Telemetry> _telemetryConfig;

        private ILogger<PipelineWorker> _logger;
        private MediaWikiApiServices _apiSvc;
        private LocalStateRepository _stateRepo;

        private DateTimeOffset _lastLogin;
        private static readonly TimeSpan LoginValidity = TimeSpan.FromDays(2);

        private ITopicClient _topicClient;
        private BinaryFormatter _formatter;

        private TelemetryClient _telemetryClient;

        private ContributionTsdb _tsdb;
        private DateTimeOffset _lastGcRun;
        private DateTimeOffset _lastCacheFlush;

        public PipelineWorker(MediaWikiApiServices apiService,
            LocalStateRepository stateRepo,
            ContributionTsdb tsdb,
            ILogger<PipelineWorker> logger,
            IOptions<ServiceBusConfig> sbConfig,
            IOptions<Telemetry> telemetryConfig)
        {
            _apiSvc = apiService;
            _stateRepo = stateRepo;
            _tsdb = tsdb;
            _logger = logger;
            _lastLogin = DateTimeOffset.MinValue;
            _sbConfig = sbConfig;
            _topicClient = new TopicClient(_sbConfig.Value.ConnectionString, _sbConfig.Value.ContentPublishTopic);
            _formatter = new BinaryFormatter();
            _telemetryConfig = telemetryConfig;
            _lastGcRun = DateTimeOffset.Now;
            _lastCacheFlush = DateTimeOffset.Now;

            var configuration = TelemetryConfiguration.CreateDefault();
            configuration.InstrumentationKey = _telemetryConfig.Value.InstrumentationKey;

            _telemetryClient = new TelemetryClient(configuration);
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

        private async Task SendBatchMessagesWithRetry(IList<Message> updateMessages)
        {
            if (updateMessages.Count < 1) return;

            for (int i = 0; i < 3; i++)
            {
                try
                {
                    await _topicClient.SendAsync(updateMessages);
                    break;
                }
                catch (Exception exc)
                {
                    _logger.LogError(exc, "EventBus exception; enter retry");
                    _telemetryClient.TrackException(exc, new Dictionary<string, string>
                    {
                        { "Category", "SendBatchMessagesWithRetry" }
                    });
                }
            }
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

                // Also run GC periodically
                if (DateTimeOffset.Now - _lastGcRun > TimeSpan.FromDays(14))
                {
                    try
                    {
                        await _tsdb.RunGarbageCollectionInXTableAsync(cancellationToken);
                        _lastGcRun = DateTimeOffset.Now;
                    }
                    catch (Exception exc)
                    {
                        _logger.LogError(exc, "Failed to run TS GC");
                    }
                }

                // Get last access time stamp from database
                lastAccess = _stateRepo.Get<DateTimeOffset>(nameof(lastAccess));
                var currentTime = DateTimeOffset.Now;

                List<ContentPageChangeEventArgs> changeLists = null;
                try
                {
                    changeLists = await _apiSvc.GetRecentChangesSinceAsync(lastAccess);
                }
                catch (Exception exc)
                {
                    _logger.LogError(exc, "MW failed to retrieve recent changes");
                    _telemetryClient.TrackException(exc, new Dictionary<string, string>
                    {
                        { "Category", "GetRecentChangesSinceAsync" }
                    });
                    await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
                    continue;
                }

                _logger.LogInformation($"{changeLists.Count} item(s) retrieved");
                _telemetryClient.TrackEvent("NewItemIngestion", null, new Dictionary<string, double>
                {
                    { "Count", changeLists.Count }
                });

                // Update time stamp if new item retrieved
                if (changeLists.Count > 0)
                {
                    lastAccess = currentTime;
                    _stateRepo.Put(nameof(lastAccess), lastAccess);
                }

                // Enqueue update events into message topics, send in batch manner
                var clusteredUpdates = changeLists.Where(i => i.Title != null).GroupBy(i => i.Title);
                var updateMessages = new List<Message>();

                foreach (var updateSeries in clusteredUpdates)
                {
                    var sortedSeries = updateSeries.OrderByDescending(i => i.ChangesetId);
                    var topItem = sortedSeries.First();

                    var memoryStream = new MemoryStream();
                    _formatter.Serialize(memoryStream, topItem);
                    updateMessages.Add(new Message(memoryStream.GetBuffer()));

                    if (updateMessages.Count > 50)
                    {
                        await SendBatchMessagesWithRetry(updateMessages);
                        updateMessages.Clear();
                    }
                }

                // Final one in message bus
                await SendBatchMessagesWithRetry(updateMessages);
                updateMessages.Clear();

                // Ingest edit stats into TS
                try
                {
                    var tsSamples = changeLists.Where(g => g.User != null).Select(k => new ContribSampleEntity(k.User, k.EventTimeStamp, 1));
                    await _tsdb.IngestSamplesAsync(tsSamples.ToList(), cancellationToken);
                }
                catch (Exception exc)
                {
                    _logger.LogError(exc, "TS failed to ingest edits");
                }

                // Flush TSDB cache every hour
                if (DateTimeOffset.Now - _lastCacheFlush >= TimeSpan.FromHours(1))
                {
                    await _tsdb.FlushCacheAsync(cancellationToken);
                    _lastCacheFlush = DateTimeOffset.Now;
                }


                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            }

            // Flush local TSDB cache
            await _tsdb.FlushCacheAsync(default(CancellationToken));
        }
    }
}
