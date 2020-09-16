﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SFA.DAS.Tools.Servicebus.Support.Infrastructure.Services
{
    public interface IBatchMessageStrategy
    {
        Task<IList<TOut>> Execute<TIn, TOut>(string queueName, long qty, int batchSize, Func<int, Task<IList<TIn>>> getMessages, Func<TIn, Task<TOut>> processMessage);
    }
}
