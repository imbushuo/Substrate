// Copyright 2019 The Lawrence Industry and its affiliates. All rights reserved.

using System;

namespace Substrate.ContentPipeline.Primitives.Models
{
    public class ContentPageMetadata
    {
        public ulong CurrentChangeSetId { get; set; }
        public DateTimeOffset CurrentTimestamp { get; set; }
        public int NamespaceId { get; set; }
    }
}
