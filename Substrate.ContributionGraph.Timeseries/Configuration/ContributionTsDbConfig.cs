// Copyright 2019 The Lawrence Industry and its affiliates. All rights reserved.

using System;

namespace Substrate.ContributionGraph.Timeseries.Configuration
{
    public class ContributionTsDbConfig
    {
        public string AzStorageConnectionString { get; set; }
        public string AzStorageTable { get; set; }
        public int RetentionDays { get; set; }
        public string LocalPreAggCachePath { get; set; }
    }
}
