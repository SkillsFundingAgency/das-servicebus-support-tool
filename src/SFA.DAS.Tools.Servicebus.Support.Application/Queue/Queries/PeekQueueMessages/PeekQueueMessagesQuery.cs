﻿using SFA.DAS.Tools.Servicebus.Support.Infrastructure.Services;

namespace SFA.DAS.Tools.Servicebus.Support.Application.Queue.Queries.PeekQueueMessages
{
    public class PeekQueueMessagesQuery
    {
        public string QueueName { get; set; }
        public int Quantity { get; set; }
    }
}
