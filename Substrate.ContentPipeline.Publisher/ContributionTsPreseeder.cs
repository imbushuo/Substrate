// Copyright 2019 The Lawrence Industry and its affiliates. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Substrate.ContributionGraph.Timeseries;
using Substrate.ContributionGraph.Timeseries.Models;
using Substrate.MediaWiki.Remote;

namespace Substrate.ContentPipeline.Publisher
{
    public class ContributionTsPreseeder
    {
        private ContributionTsdb _tsdb;
        private ILogger<ContributionTsPreseeder> _logger;
        private MediaWikiApiServices _apiSvc;

        public ContributionTsPreseeder(
            ContributionTsdb tsdb,
            ILogger<ContributionTsPreseeder> logger,
            MediaWikiApiServices apiSvc
        )
        {
            _tsdb = tsdb;
            _logger = logger;
            _apiSvc = apiSvc;
        }

        public async Task RunGcAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Start GC");
            await _tsdb.RunGarbageCollectionInXTableAsync(cancellationToken);
            _logger.LogInformation("GC completed");
        }

        public async Task RunSeedingAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Start seeding data of 37 days");
            if (_apiSvc.CurrentIdentity == null)
            {
                var id = await _apiSvc.LoginAsync();
                if (id == null)
                {
                    _logger.LogError("MW failed to log in");
                    return;
                }
            }

            var end = DateTimeOffset.UtcNow;
            var b = end.Subtract(TimeSpan.FromDays(37));
            int retryCount = 0;

            do
            {
                try
                {
                    var tsSamples = new List<ContribSampleEntity>();
                    var s = b.AddHours(1);
                    var changes = await _apiSvc.GetRecentChangesSinceAsync(b, 0, s);
                    _logger.LogInformation($"Get changes from {b} to {s}: {changes.Count} items");

                    var userGrouping = changes.Where(g => g.User != null).GroupBy(g => g.User);
                    foreach (var g in userGrouping)
                    {
                        var count = g.Count();
                        tsSamples.Add(new ContribSampleEntity(g.Key, b, count));
                    }

                    await _tsdb.IngestSamplesAsync(tsSamples, cancellationToken);
                    b = s;
                    retryCount = 0;
                }
                catch (Exception exc)
                {
                    _logger.LogError(exc, "Caught an error and enter retry.");

                    if (retryCount > 3)
                    {
                        break;
                    }
                    else
                    {
                        retryCount++;
                    }
                }
            }
            while (b < end && !cancellationToken.IsCancellationRequested);

            await _tsdb.FlushCacheAsync(cancellationToken);
            _logger.LogInformation("Seeding completed");
        }
    }
}
