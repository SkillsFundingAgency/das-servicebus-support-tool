﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using Microsoft.AspNetCore.Mvc;
using SFA.DAS.Tools.Servicebus.Support.Core.Models;
using SFA.DAS.Tools.Servicebus.Support.Infrastructure.Services;
using SFA.DAS.Tools.Servicebus.Support.Infrastructure.Services.SvcBusService;
using SFA.DAS.Tools.Servicebus.Support.Web.Models;

namespace SFA.DAS.Tools.Servicebus.Support.Web.Controllers
{
    public class MessageListController : Controller
    {
        private readonly ISvcBusService _svcBusService;
        private readonly ICosmosDbContext _cosmosDbContext;

        public MessageListController(ISvcBusService svcBusService, ICosmosDbContext cosmosDbContext)
        {
            _svcBusService = svcBusService;
            _cosmosDbContext = cosmosDbContext;
        }

        public async Task<IActionResult> Index()
        {
            var messages = await _cosmosDbContext.GetQueueMessagesAsync(UserService.GetUserId());

            var queueName = getQueueName(messages);

            var vm = new MessageListViewModel()
            {
                Messages = messages,
                QueueInfo = await _svcBusService.GetQueueDetailsAsync(queueName)
            };

            return View(vm);
        }


        public async Task<IActionResult> ReceiveMessages(string queue)
        {
            using (var ts = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
            {
                ReceiveMessagesResponse response = null;

                try
                {
                    response = await _svcBusService.ReceiveMessagesAsync(queue, 50);//todo custom qty 
                    var result = await _cosmosDbContext.BulkCreateQueueMessagesAsync(response.Messages);
                    
                    ts.Complete();
                }catch(Exception ex)
                {
                    ts.Dispose();
                }
                finally
                {
                    //if ( response != null)
                    //    await _svcBusService.CloseMessageReceiver(response.MessageReceiver);
                }
                
            }                        

            return RedirectToAction("Index");
        }

        public async Task<IActionResult> AbortMessages()
        {
            var messages = await _cosmosDbContext.GetQueueMessagesAsync(UserService.GetUserId());

            foreach (var msg in messages)
            {
                await _svcBusService.SendMessageToErrorQueueAsync(msg);
                await _cosmosDbContext.DeleteQueueMessageAsync(msg);
            }

            return RedirectToAction("Index", "Home");
        }


        private string getQueueName(IEnumerable<QueueMessage> messages)
        {
            var name = "";
            if (messages?.Count() > 0)
            {
                name = messages.First().Queue;
            }

            return name;
        }
    }
}
