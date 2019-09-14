// Copyright 2019 The Lawrence Industry and its affiliates. All rights reserved.

using System;

namespace Substrate.ContentPipeline.Primitives.Models
{
    public class ContentPageChangeEventArgs
    {
        public DateTimeOffset EventTimeStamp { get; set; }
        public string Title { get; set; }
        public ulong ChangesetId { get; set; }

        public ContentPageChangeEventArgs()
        {
        }

        public ContentPageChangeEventArgs(string title, ulong changesetId,
            DateTimeOffset timestamp)
        {
            Title = title;
            ChangesetId = changesetId;
            EventTimeStamp = timestamp;
        }
    }
}
