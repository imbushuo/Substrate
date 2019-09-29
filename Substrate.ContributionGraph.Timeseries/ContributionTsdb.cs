// Copyright 2019 The Lawrence Industry and its affiliates. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RocksDbSharp;
using Substrate.ContributionGraph.Timeseries.Configuration;
using Substrate.ContributionGraph.Timeseries.Models;

namespace Substrate.ContributionGraph.Timeseries
{
    public class ContributionTsdb : IDisposable
    {
        private IOptions<ContributionTsDbConfig> _config;
        private ILogger<ContributionTsdb> _logger;

        private CloudStorageAccount _xStorageAccount;
        private CloudTableClient _xTableClient;

        private RocksDb _preAggDatabase;
        private BinaryFormatter _formatter;

        private bool _bypassLocalCache;

        public ContributionTsdb(
            IOptions<ContributionTsDbConfig> tsConfig,
            ILogger<ContributionTsdb> logger
        )
        {
            _config = tsConfig;
            _logger = logger;

            _xStorageAccount = CloudStorageAccount.Parse(_config.Value.AzStorageConnectionString);
            _xTableClient = _xStorageAccount.CreateCloudTableClient(new TableClientConfiguration());

            var tsTable = _xTableClient.GetTableReference(_config.Value.AzStorageTable);
            tsTable.CreateIfNotExists();

            _bypassLocalCache = string.IsNullOrEmpty(_config.Value.LocalPreAggCachePath);
            if (!_bypassLocalCache)
            {
                var options = new DbOptions().SetCreateIfMissing(true);
                // 1.5 hours are a bit far exceed the time span but should be okay
                _preAggDatabase = RocksDb.OpenWithTtl(options,
                    Path.Combine(_config.Value.LocalPreAggCachePath, "PreAggDb"),
                    (int)TimeSpan.FromHours(1.3).TotalSeconds);
                _formatter = new BinaryFormatter();
            }
        }

        private async Task<List<ContribSampleEntity>> GetSamplesFromXTableAsync(string username,
            DateTimeOffset notBefore, CancellationToken cancellationToken)
        {
            var tsTable = _xTableClient.GetTableReference(_config.Value.AzStorageTable);

            var samples = new List<ContribSampleEntity>();
            var contToken = default(TableContinuationToken);
            var gcQuery = tsTable.CreateQuery<ContribSampleEntity>().Where(
                TableQuery.CombineFilters(
                    TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, username),
                    TableOperators.And,
                    TableQuery.GenerateFilterConditionForDate("MetricTimeStampUtc",
                    QueryComparisons.GreaterThanOrEqual, notBefore.UtcDateTime)
                ));

            do
            {
                var result = await gcQuery.ExecuteSegmentedAsync(contToken, cancellationToken);
                contToken = result.ContinuationToken;

                if (result.Results != null)
                {
                    samples.AddRange(result.Results);
                }
            }
            while (contToken != null && !cancellationToken.IsCancellationRequested);

            return samples;
        }

        private async Task IngestSamplesToXTableAsync(List<ContribSampleEntity> samples, CancellationToken cancellationToken)
        {
            var tsTable = _xTableClient.GetTableReference(_config.Value.AzStorageTable);

            // Document said 100 ops as limit; use 50 for our chunks
            for (int batchBegin = 0; batchBegin < samples.Count; batchBegin += 50)
            {
                var insertOpertions = new TableBatchOperation();
                for (int i = batchBegin; i < Math.Min(50, samples.Count - batchBegin); i++)
                {
                    insertOpertions.Add(TableOperation.InsertOrReplace(samples[batchBegin + i]));
                }
                await tsTable.ExecuteBatchAsync(insertOpertions, cancellationToken);
            }
        }

        public async Task RunGarbageCollectionInXTableAsync(CancellationToken cancellationToken)
        {
            var tsTable = _xTableClient.GetTableReference(_config.Value.AzStorageTable);

            var notBeforeTime = DateTime.UtcNow.Subtract(TimeSpan.FromDays(_config.Value.RetentionDays));
            var contToken = default(TableContinuationToken);
            var gcQuery = tsTable.CreateQuery<ContribSampleEntity>().Where(
                TableQuery.GenerateFilterConditionForDate(
                    "MetricTimeStampUtc", QueryComparisons.LessThan, notBeforeTime
                ));

            _logger.LogInformation("TS Garbage Collection started");

            do
            {
                var result = await gcQuery.ExecuteSegmentedAsync(contToken, cancellationToken);
                contToken = result.ContinuationToken;

                if (result.Results != null)
                {
                    var deleteOperations = new TableBatchOperation();
                    foreach (var item in result.Results)
                    {
                        deleteOperations.Add(TableOperation.Delete(item));
                    }

                    await tsTable.ExecuteBatchAsync(deleteOperations, cancellationToken);
                    _logger.LogInformation($"TS Garbage Collection deleted batch of {result.Results.Count} item(s)");
                }
            }
            while (contToken != null && !cancellationToken.IsCancellationRequested);

            _logger.LogInformation("TS Garbage Collection completed");
        }

        public async Task IngestSamplesAsync(List<ContribSampleEntity> samples, CancellationToken cancellationToken)
        {
            if (_bypassLocalCache) throw new NotSupportedException();

            // Cutoff time span is 1 hour
            // Other potion are assumed aggregated and directly ingested into the database
            // The rest are not aggregated and will retain in the RocksDb for a while
            var currentTime = DateTime.UtcNow;
            var cutoffTime = new DateTime(currentTime.Year, currentTime.Month,
                currentTime.Day, currentTime.Hour, 0, 0, DateTimeKind.Utc);

            var xTableWritePotion = new List<ContribSampleEntity>();
            var aggDictionary = new ConcurrentDictionary<string, long>();

            foreach (var sample in samples)
            {
                if (sample.MetricTimeStampUtc < cutoffTime)
                {
                    xTableWritePotion.Add(sample);
                }
                else
                {
                    aggDictionary.AddOrUpdate(sample.PartitionKey, sample.Count,
                        (key, value) => value + sample.Count);
                }
            }

            // Ingest into XTable
            await IngestSamplesToXTableAsync(xTableWritePotion, cancellationToken);

            // Cache the 1hr data
            foreach (var k in aggDictionary)
            {
                long count;
                var kb = Encoding.UTF8.GetBytes(k.Key);

                var counterBytes = _preAggDatabase.Get(kb);
                if (counterBytes != null)
                {
                    count = (long) _formatter.Deserialize(new MemoryStream(counterBytes));
                    count += k.Value;
                }
                else
                {
                    count = k.Value;
                }

                var s = new MemoryStream();
                _formatter.Serialize(s, count);
                _preAggDatabase.Put(kb, s.GetBuffer());
            }
        }

        public async Task<List<ContribSampleEntity>> GetSamplesAsync(string username,
            DateTimeOffset notBefore, CancellationToken cancellationToken)
        {
            var ret = new List<ContribSampleEntity>();
            var n = DateTimeOffset.Now;

            if (n - notBefore > TimeSpan.FromHours(1))
            {
                var samples = await GetSamplesFromXTableAsync(username, notBefore, cancellationToken);
                ret.AddRange(samples);
            }

            if (!_bypassLocalCache)
            {
                var kb = Encoding.UTF8.GetBytes(username);
                var counterBytes = _preAggDatabase.Get(kb);
                if (counterBytes != null)
                {
                    var count = (long)_formatter.Deserialize(new MemoryStream(counterBytes));
                    ret.Add(new ContribSampleEntity(username, n, count));
                }
            }

            return ret;
        }

        public async Task FlushCacheAsync(CancellationToken cancellationToken)
        {
            if (_bypassLocalCache) throw new NotSupportedException();

            var writeBatch = new List<ContribSampleEntity>();
            var currentTime = DateTime.UtcNow;
            var cutoffTime = new DateTime(currentTime.Year, currentTime.Month,
                currentTime.Day, currentTime.Hour, 0, 0, DateTimeKind.Utc);

            using (var i = _preAggDatabase.NewIterator())
            {
                while (i.Valid())
                {
                    var k = Encoding.UTF8.GetString(i.Key());
                    var v = (long) _formatter.Deserialize(new MemoryStream(i.Value()));
                    writeBatch.Add(new ContribSampleEntity(k, cutoffTime, v));
                    i.Next();
                }
            }

            await IngestSamplesToXTableAsync(writeBatch, cancellationToken);
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _preAggDatabase?.Dispose();
                }

                _preAggDatabase = null;
                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
        #endregion
    }
}
