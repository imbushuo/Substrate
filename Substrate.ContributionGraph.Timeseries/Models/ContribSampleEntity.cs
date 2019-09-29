﻿// Copyright 2019 The Lawrence Industry and its affiliates. All rights reserved.

using System;
using Microsoft.Azure.Cosmos.Table;

namespace Substrate.ContributionGraph.Timeseries.Models
{
    public class ContribSampleEntity : TableEntity
    {
        public ContribSampleEntity()
        {
        }

        public ContribSampleEntity(string username, DateTimeOffset timeStamp, long count)
        {
            // The partition is based on username
            PartitionKey = username;

            // Identifier as UTC time in our time series
            RowKey = timeStamp.UtcDateTime.ToString();

            // Because XTable/Cosmos cannot take DateTimeOffset,
            // the actual property will be assigned with .UtcDateTime
            MetricTimeStampUtc = timeStamp.UtcDateTime;

            Count = count;
        }

        public DateTime MetricTimeStampUtc { get; set; }

        public long Count { get; set; }
    }
}
