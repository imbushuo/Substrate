// Copyright 2019 The Lawrence Industry and its affiliates. All rights reserved.
//
using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RocksDbSharp;
using Substrate.ContentPipeline.Publisher.Configuration;

namespace Substrate.ContentPipeline.Publisher.DataAccess
{
    public class LocalStateRepository : IDisposable
    {
        private IOptions<RuntimeDirectory> _runtimeDirectory;
        private ILogger<LocalStateRepository> _logger;
        private RocksDb _dbInstance;

        public LocalStateRepository(ILogger<LocalStateRepository> logger,
            IOptions<RuntimeDirectory> runtimeDirOptions)
        {
            _logger = logger;
            _runtimeDirectory = runtimeDirOptions;

            var options = new DbOptions().SetCreateIfMissing(true);
            _dbInstance = RocksDb.Open(options, $"{_runtimeDirectory.Value.LocalStateRepositoryDb}/LocalOptions.db");
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
