using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;
using Microsoft.Azure.ServiceBus.Management;
using Microsoft.Azure.ServiceBus.Primitives;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SFA.DAS.Tools.Servicebus.Support.Domain.Queue;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SFA.DAS.Tools.Servicebus.Support.Infrastructure.Services.SvcBusService
{
    public class AsbService : IAsbService
    {
        private readonly IConfiguration _config;
        private readonly ILogger _logger;
        private readonly int _batchSize;
        private readonly TokenProvider _tokenProvider;
        private readonly ServiceBusConnectionStringBuilder _sbConnectionStringBuilder;
        private readonly ManagementClient _managementClient;
        private readonly IUserService _userService;

        public AsbService(IUserService userService, IConfiguration config, ILogger<AsbService> logger)
        {
            _config = config ?? throw new Exception("config is null");
            _logger = logger ?? throw new Exception("logger is null");

            var serviceBusConnectionString = _config.GetValue<string>("ServiceBusRepoSettings:ServiceBusConnectionString");
            _batchSize = _config.GetValue<int>("ServiceBusRepoSettings:PeekMessageBatchSize");
            _tokenProvider = TokenProvider.CreateManagedIdentityTokenProvider();
            _sbConnectionStringBuilder = new ServiceBusConnectionStringBuilder(serviceBusConnectionString);
            _managementClient = new ManagementClient(_sbConnectionStringBuilder, _tokenProvider);
            _userService = userService;
        }

        public async Task<IEnumerable<QueueInfo>> GetErrorMessageQueuesAsync()
        {
            var queues = new List<QueueInfo>();
            var queuesDetails = await _managementClient.GetQueuesRuntimeInfoAsync().ConfigureAwait(false);
            var regexString = _config.GetValue<string>("ServiceBusRepoSettings:QueueSelectionRegex");
            var queueSelectionRegex = new Regex(regexString);
            var errorQueues = queuesDetails.Where(q => queueSelectionRegex.IsMatch(q.Path));//.Select(x => x.Path);

            foreach (var queue in errorQueues)
            {
                queues.Add(new QueueInfo()
                {
                    Name = queue.Path,
                    MessageCount = queue.MessageCount
                });
            }

            return queues;
        }

        public async Task<QueueInfo> GetQueueDetailsAsync(string name)
        {
            var result = new QueueInfo();

            if (!string.IsNullOrEmpty(name))
            {
                var queue = await _managementClient.GetQueueRuntimeInfoAsync(name).ConfigureAwait(false);

                result.Name = queue.Path;
                result.MessageCount = queue.MessageCountDetails.ActiveMessageCount;
            }
            return result;
        }

        public async Task<IEnumerable<QueueMessage>> PeekMessagesAsync(string queueName, int qty)
        {
            var messageReceiver = new MessageReceiver(_sbConnectionStringBuilder.Endpoint, queueName, _tokenProvider);
            var totalMessages = 0;
            var formattedMessages = new List<QueueMessage>();
            var messageQtyToGet = CalculateMessageQtyToGet(qty, 0, _batchSize);
            var peekedMessages = messageQtyToGet > 0 ? await messageReceiver.PeekAsync(messageQtyToGet) : null;

            _logger.LogDebug($"Peeked Message Count: {peekedMessages?.Count}");

            if (peekedMessages != null)
            {
                while (peekedMessages?.Count > 0 && totalMessages < qty)
                {
                    totalMessages += peekedMessages.Count;
                    
                    foreach (var msg in peekedMessages)
                    {
                        formattedMessages.Add(CreateMessage(msg, queueName));
                    }

                    messageQtyToGet = CalculateMessageQtyToGet(qty, totalMessages, _batchSize);                    
                    peekedMessages = messageQtyToGet > 0 ? await messageReceiver.PeekAsync(messageQtyToGet) : null;
                }
            }

            await messageReceiver.CloseAsync();

            return formattedMessages;
        }

        public async Task<IEnumerable<QueueMessage>> ReceiveMessagesAsync(string queueName, int qty)
        {
            var messageReceiver = new MessageReceiver(_sbConnectionStringBuilder.Endpoint, queueName, _tokenProvider);
            var totalMessages = 0;
            var formattedMessages = new List<QueueMessage>();
            var queueInfo = await GetQueueDetailsAsync(queueName);
            
            if( queueInfo.MessageCount < qty)
            {
                qty = Convert.ToInt32(queueInfo.MessageCount);
            }

            var messageQtyToGet = CalculateMessageQtyToGet(qty, 0, _batchSize);
            var receivedMessages = messageQtyToGet > 0 ? await messageReceiver.ReceiveAsync(messageQtyToGet) : null;

            if (receivedMessages != null)
            {
                _logger.LogDebug($"Received Message Count: {receivedMessages.Count}");

                while (receivedMessages?.Count > 0 && totalMessages < qty)
                {
                    totalMessages += receivedMessages.Count;
                    foreach (var msg in receivedMessages)
                    {
                        formattedMessages.Add(CreateMessage(msg, queueName));
                        await messageReceiver.CompleteAsync(msg.SystemProperties.LockToken);
                    }

                    messageQtyToGet = CalculateMessageQtyToGet(qty, totalMessages, _batchSize);
                    receivedMessages = messageQtyToGet > 0 ? await messageReceiver.ReceiveAsync(messageQtyToGet) : null;
                }
            }

            return formattedMessages;
        }

        public async Task SendMessageToErrorQueueAsync(QueueMessage msg) => await SendMessageAsync(msg, msg.Queue);

        public async Task SendMessageToProcessingQueueAsync(QueueMessage msg) => await SendMessageAsync(msg, msg.ProcessingEndpoint);

        private int CalculateMessageQtyToGet(int totalExpected, int received, int batchSize)
        {
            var qtyRequired = totalExpected - received;

            if (qtyRequired >= batchSize)
            {
                return batchSize;
            }

            else if (qtyRequired < batchSize && qtyRequired > 0)
            {
                return qtyRequired;
            }

            return 0;
        }

        private async Task SendMessageAsync(QueueMessage errorMessage, string queueName)
        {
            var messageSender = new MessageSender(_sbConnectionStringBuilder.Endpoint, queueName, _tokenProvider);

            if (!errorMessage.IsReadOnly)
            {
                await messageSender.SendAsync(errorMessage.OriginalMessage);
            }
        }

        private QueueMessage CreateMessage(Message message, string queueName)
        {
            return new QueueMessage
            {
                Id = message.MessageId,
                UserId = _userService.GetUserId(),
                OriginalMessage = message,
                Queue = queueName,
                IsReadOnly = false,
                Body = Encoding.UTF8.GetString(message.Body),
                OriginatingEndpoint = message.UserProperties["NServiceBus.OriginatingEndpoint"].ToString(),
                ProcessingEndpoint = message.UserProperties["NServiceBus.ProcessingEndpoint"].ToString(),
                Exception = message.UserProperties["NServiceBus.ExceptionInfo.Message"].ToString(),
                ExceptionType = message.UserProperties["NServiceBus.ExceptionInfo.ExceptionType"].ToString()
            };
        }
    }
}
