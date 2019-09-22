// Copyright 2019 The Lawrence Industry and its affiliates. All rights reserved.
//
using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Substrate.ContentPipeline.Primitives.Configuration;
using Substrate.ContentPipeline.Primitives.Models;
using Substrate.MediaWiki.Remote;

namespace Substrate.Edge.Caching
{
    public class PageCacheUpdater : IHostedService
    {
        private ILogger<PageCacheUpdater> _logger;
        private IOptions<ServiceBusConfig> _sbConfig;

        private CancellationTokenSource _cancellationTokenSource;
        private IServiceProvider _provider;
        private IServiceScopeFactory _scopeFactory;
        private ISubscriptionClient _subClient;

        private MediaWikiApiServices _apiClient;
        private PageRepository _cache;

        public PageCacheUpdater(IServiceProvider provider,
            IOptions<ServiceBusConfig> sbConfig,
            ILogger<PageCacheUpdater> logger,
            MediaWikiApiServices apiClient,
            PageRepository cache)
        {
            _logger = logger;
            _provider = provider;
            _sbConfig = sbConfig;
            _subClient = new SubscriptionClient(
                _sbConfig.Value.ConnectionString,
                _sbConfig.Value.ContentPublishTopic,
                _sbConfig.Value.Subscription);
            _cancellationTokenSource = new CancellationTokenSource();
            _scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();
            _apiClient = apiClient;
            _cache = cache;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Background cache updater is starting");
            RunTaskLoop(_cancellationTokenSource.Token);
            _logger.LogInformation("Background cache updater started");

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Background cache updater is stopping");
            _cancellationTokenSource.Cancel();
            _logger.LogInformation("Background cache updater stopped");

            return Task.CompletedTask;
        }

        public async void RunTaskLoop(CancellationToken cancellationToken)
        {
            var messageHandlerOptions = new MessageHandlerOptions(ExceptionReceivedHandler)
            {
                MaxConcurrentCalls = 20,
                AutoComplete = false
            };

            _subClient.RegisterMessageHandler(HandleMessageAsync, messageHandlerOptions);

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(500, cancellationToken);
            }

            await _subClient.CloseAsync();
        }

        private async Task HandleMessageAsync(Message message, CancellationToken cancellationToken)
        {
            if (_apiClient.CurrentIdentity == null || DateTimeOffset.Now - _apiClient.LastLogin > TimeSpan.FromDays(2))
            {
                await _apiClient.LoginAsync();
            }

            var formatter = new BinaryFormatter();
            if (message.Body == null)
            {
                await _subClient.AbandonAsync(message.SystemProperties.LockToken);
                return;
            }

            var objectStream = new MemoryStream(message.Body);
            var updateEvent = (ContentPageChangeEventArgs)formatter.Deserialize(objectStream);

            if (updateEvent.Title == null)
            {
                _logger.LogWarning($"Changeset {updateEvent.ChangesetId} has null title");
                await _subClient.CompleteAsync(message.SystemProperties.LockToken);
                return;
            }

            var prevMeta = _cache.GetPageMetadata(updateEvent.Title);
            if (prevMeta != null && prevMeta.ChangeSetId > updateEvent.ChangesetId)
            {
                _logger.LogWarning($"Update to page {updateEvent.Title} has higher local rev, {prevMeta.ChangeSetId} > {updateEvent.ChangesetId}");
                await _subClient.CompleteAsync(message.SystemProperties.LockToken);
                return;
            }

            try
            {
                var (meta, page) = await _apiClient.GetPageAsync(updateEvent.Title, updateEvent.ChangesetId);
                if (meta != null && page != null)
                {
                    _cache.PutPageContent(updateEvent.Title, meta, page);
                    await _subClient.CompleteAsync(message.SystemProperties.LockToken);
                    _logger.LogInformation($"Updated page {updateEvent.Title} to rev {updateEvent.ChangesetId}");
                }
            }
            catch (Exception exc)
            {
                _logger.LogError(exc, "MW getting update failed");
            }
        }

        private Task ExceptionReceivedHandler(ExceptionReceivedEventArgs arg)
        {
            _logger.LogError(arg.Exception, "Error in message handling");
            return Task.CompletedTask;
        }
    }
}
