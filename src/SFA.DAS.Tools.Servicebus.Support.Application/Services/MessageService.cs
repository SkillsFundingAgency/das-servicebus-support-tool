﻿using Microsoft.Extensions.Logging;
using SFA.DAS.Tools.Servicebus.Support.Application.Queue.Commands.DeleteQueueMessages;
using SFA.DAS.Tools.Servicebus.Support.Application.Queue.Commands.SendMessages;
using SFA.DAS.Tools.Servicebus.Support.Audit;
using SFA.DAS.Tools.Servicebus.Support.Domain.Queue;
using SFA.DAS.Tools.Servicebus.Support.Infrastructure.Services.Batching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;

namespace SFA.DAS.Tools.Servicebus.Support.Application.Services
{
    public class MessageService : IMessageService
    {
        private readonly ILogger<MessageService> _logger;
        private readonly ICommandHandler<SendMessagesCommand, SendMessagesCommandResponse> _sendMessagesCommand;
        private readonly ICommandHandler<DeleteQueueMessagesCommand, DeleteQueueMessagesCommandResponse>
            _deleteQueueMessageCommand;
        private readonly IAuditService _auditService;
        private readonly IBatchSendMessageStrategy _batchSendMessageStrategy;

        public MessageService(
            IBatchSendMessageStrategy batchSendMessageStrategy,
            ILogger<MessageService> logger,
            ICommandHandler<SendMessagesCommand, SendMessagesCommandResponse> sendMessagesCommand,
            ICommandHandler<DeleteQueueMessagesCommand, DeleteQueueMessagesCommandResponse> deleteQueueMessageCommand,
            IAuditService auditService
        )
        {
            _batchSendMessageStrategy = batchSendMessageStrategy;
            _logger = logger;
            _sendMessagesCommand = sendMessagesCommand;
            _deleteQueueMessageCommand = deleteQueueMessageCommand;
            _auditService = auditService;
        }
        
        public async Task ReplayMessages(IEnumerable<QueueMessage> messages, string queue) => await SendMessageAndDeleteFromDb(messages, queue,true);
        
        public async Task AbortMessages(IEnumerable<QueueMessage> messages, string queue) => await SendMessageAndDeleteFromDb(messages, queue);

        private async Task SendMessageAndDeleteFromDb(IEnumerable<QueueMessage> messages, string queue, bool createAudit = false)
        {
            await _batchSendMessageStrategy.Execute(messages,
                async (messages) =>
                {
                    using var ts = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
                    try
                    {
                        await _sendMessagesCommand.Handle(new SendMessagesCommand()
                        {
                            Messages = messages,
                            QueueName = queue
                        });

                        await _deleteQueueMessageCommand.Handle(new DeleteQueueMessagesCommand()
                        {
                            Ids = messages.Select(x => x.Id).ToList()
                        });

                        ts.Complete();

                        if (createAudit)
                        {                               
                            foreach (var message in messages)
                            {
                                await _auditService.WriteAudit(new MessageQueueReplayAuditMessage(message));                                
                            }
                        }                        
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send messages");
                    }
                });
        }
    }
}
