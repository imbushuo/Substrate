// Copyright 2019 The Lawrence Industry and its affiliates. All rights reserved.

using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RocksDbSharp;
using Substrate.ContentPipeline.Primitives.Models;
using Substrate.Edge.Configuration;

namespace Substrate.Edge.Caching
{
    public class PageRepository : IDisposable
    {
        private IOptions<CachingConfig> _cachingConfig;
        private ILogger<CachingConfig> _logger;
        private RocksDb _dbInstance;
        private BinaryFormatter _formatter;

        public PageRepository(IOptions<CachingConfig> cachingConfig,
            ILogger<CachingConfig> logger)
        {
            _cachingConfig = cachingConfig;
            _logger = logger;

            var options = new DbOptions()
                .SetCreateIfMissing(true);

            _dbInstance = RocksDb.OpenWithTtl(options,
                Path.Combine(_cachingConfig.Value.Path, "PageCache.db"),
                _cachingConfig.Value.CacheTimeToLive);
            _formatter = new BinaryFormatter();
        }

        public byte[] GetPageContent(string title)
        {
            if (title == null) return null;

            return _dbInstance.Get(Encoding.UTF8.GetBytes($"{title}@Content"));
        }

        public ContentPageMetadata GetPageMetadata(string title)
        {
            if (title == null) return null;

            var serializedMetadata = _dbInstance.Get(Encoding.UTF8.GetBytes($"{title}@Metadata"));
            if (serializedMetadata != null)
            {
                using (var metadataStream = new MemoryStream(serializedMetadata))
                {
                    var prevMetadata = (ContentPageMetadata)_formatter.Deserialize(metadataStream);
                    return prevMetadata;
                }
            }

            return null;
        }

        public void PutPageContent(string title, ContentPageMetadata meta, byte[] pageContent)
        {
            if (title == null || meta == null) return;

            var serializedMetadata = _dbInstance.Get(Encoding.UTF8.GetBytes($"{title}@Metadata"));
            if (serializedMetadata != null)
            {
                using (var metadataStream = new MemoryStream(serializedMetadata))
                {
                    var prevMetadata = (ContentPageMetadata) _formatter.Deserialize(metadataStream);
                    if (prevMetadata.ChangeSetId > meta.ChangeSetId)
                    {
                        // Update is not required
                        _logger.LogWarning($"Cache update request for {title} has rev {prevMetadata.ChangeSetId} > {meta.ChangeSetId}");
                        return;
                    }
                }
            }

            using (var metadataStream = new MemoryStream())
            {
                _formatter.Serialize(metadataStream, meta);
                _dbInstance.Put(Encoding.UTF8.GetBytes($"{title}@Metadata"), metadataStream.GetBuffer());
                _dbInstance.Put(Encoding.UTF8.GetBytes($"{title}@Content"), pageContent);
            }
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _dbInstance.Dispose();
                }

                _dbInstance = null;
                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
}
