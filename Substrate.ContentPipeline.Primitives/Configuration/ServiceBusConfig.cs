// Copyright 2019 The Lawrence Industry and its affiliates. All rights reserved.

using System;

namespace Substrate.ContentPipeline.Primitives.Configuration
{
    public class ServiceBusConfig
    {
        public string ConnectionString { get; set; }
        public string ContentPublishTopic { get; set; }
    }
}
