// Copyright 2019 The Lawrence Industry and its affiliates. All rights reserved.

using System;

namespace Substrate.ContentPipeline.Primitives.Models
{
    [Serializable]
    public class ContentPageChangeEventArgs
    {
        public DateTimeOffset EventTimeStamp { get; set; }
        public string Title { get; set; }
        public ulong ChangesetId { get; set; }
        public string User { get; set; }

        public ContentPageChangeEventArgs()
        {
        }

        public ContentPageChangeEventArgs(string title, ulong changesetId,
            DateTimeOffset timestamp, string user)
        {
            Title = title;
            ChangesetId = changesetId;
            EventTimeStamp = timestamp;
            User = user;
        }
    }
}
