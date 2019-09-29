// Copyright 2019 The Lawrence Industry and its affiliates. All rights reserved.

using System;

namespace Substrate.ContentPipeline.Primitives.Models
{
    [Serializable]
    public class FlowThreadCommentChangeEventArgs
    {
        public DateTimeOffset EventTimeStamp { get; set; }

        public string PageTitle { get; set; }
        public ulong PageId { get; set; }

        public ulong UserId { get; set; }
        public string Username { get; set; }
    }
}
