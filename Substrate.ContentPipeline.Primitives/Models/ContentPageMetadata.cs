// Copyright 2019 The Lawrence Industry and its affiliates. All rights reserved.

using System;

namespace Substrate.ContentPipeline.Primitives.Models
{
    [Serializable]
    public class ContentPageMetadata
    {
        public ulong ChangeSetId { get; set; }
        public ulong PageId { get; set; }
    }
}
