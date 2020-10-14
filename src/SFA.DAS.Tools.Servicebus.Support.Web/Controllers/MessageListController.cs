﻿using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using SFA.DAS.Tools.Servicebus.Support.Application;
using SFA.DAS.Tools.Servicebus.Support.Application.Queue.Queries.GetMessages;
using SFA.DAS.Tools.Servicebus.Support.Application.Queue.Queries.GetMessagesById;
using SFA.DAS.Tools.Servicebus.Support.Application.Queue.Queries.GetQueueDetails;
using SFA.DAS.Tools.Servicebus.Support.Application.Services;
using SFA.DAS.Tools.Servicebus.Support.Domain;
using SFA.DAS.Tools.Servicebus.Support.Domain.Queue;
using SFA.DAS.Tools.Servicebus.Support.Infrastructure.Services;
using SFA.DAS.Tools.Servicebus.Support.Web.App_Start;
using SFA.DAS.Tools.Servicebus.Support.Web.Models;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static SFA.DAS.Tools.Servicebus.Support.Web.Startup;

namespace SFA.DAS.Tools.Servicebus.Support.Web.Controllers
{
    [ServiceFilter(typeof(KeepUserSessionActiveFilter))]
    public class MessageListController : Controller
    {
        private readonly IUserService _userService;
        private readonly IMessageService _messageService;
        private readonly IQueryHandler<GetMessagesQuery, GetMessagesQueryResponse> _getMessagesQuery;
        private readonly IQueryHandler<GetMessagesByIdQuery, GetMessagesByIdQueryResponse> _getMessagesByIdQuery;
        private readonly IQueryHandler<GetQueueDetailsQuery, GetQueueDetailsQueryResponse> _getQueueDetailsQuery;
        private readonly IUserSessionService _userSessionService;
        private readonly Settings _settings;

        public MessageListController(
            IUserService userService,
            IQueryHandler<GetMessagesQuery, GetMessagesQueryResponse> getMessagesQuery,
            IQueryHandler<GetMessagesByIdQuery, GetMessagesByIdQueryResponse> getMessagesByIdQuery,
            IQueryHandler<GetQueueDetailsQuery, GetQueueDetailsQueryResponse> getQueueDetailsQuery,
            IMessageService messageService,
            IUserSessionService userSessionService,
            IOptions<Settings> settings)
        {
            _userService = userService;
            _getMessagesQuery = getMessagesQuery;
            _getMessagesByIdQuery = getMessagesByIdQuery;
            _getQueueDetailsQuery = getQueueDetailsQuery;
            _messageService = messageService;
            _userSessionService = userSessionService;
            _settings = settings.Value;
        }

        public async Task<IActionResult> Index()
        {
            var response = await _getMessagesQuery.Handle(new GetMessagesQuery()
            {
                UserId = _userService.GetUserId(),
                SearchProperties = new SearchProperties
                {
                    Offset = 0,
                    Limit = 1
                }
            });

            if (!response.Messages.Any())
            {
                await DeleteUserSession();
                return RedirectToAction("Index", "Servicebus");
            }

            var queueName = GetQueueName(response.Messages);
            HttpContext.Session.SetString("queueName", queueName);

            return View(new MessageListViewModel()
            {
                Count = response.Count,
                QueueInfo = (await _getQueueDetailsQuery.Handle(new GetQueueDetailsQuery()
                {
                    QueueName = queueName
                })).QueueInfo,
                UserSession = await _userSessionService.GetUserSession()

            });
        }

        public async Task<IActionResult> ReceiveMessages(string queue)
        {
            await _messageService.ProcessMessages(queue);

            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> AbortMessages(string data)
        {
            var selectedMessages = JsonConvert.DeserializeObject<SelectedMessages>(data);
            var response = await _getMessagesByIdQuery.Handle(new GetMessagesByIdQuery()
            {
                UserId = _userService.GetUserId(),
                Ids = selectedMessages.Ids
            });

            await _messageService.AbortMessages(response.Messages, selectedMessages.Queue);

            return Json(string.Empty);
        }

        public async Task<IActionResult> ReleaseMessages(string queue)
        {
            var response = await _getMessagesQuery.Handle(new GetMessagesQuery()
            {
                UserId = _userService.GetUserId(),
                SearchProperties = new SearchProperties()
            });

            await _messageService.AbortMessages(response.Messages, queue);

            return RedirectToAction("Index", "Servicebus");
        }

        [HttpPost]
        public async Task<IActionResult> ReplayMessages(string data)
        {
            var selectedMessages = JsonConvert.DeserializeObject<SelectedMessages>(data);
            var response = await _getMessagesByIdQuery.Handle(new GetMessagesByIdQuery()
            {
                UserId = _userService.GetUserId(),
                Ids = selectedMessages.Ids
            });

            var processingQueueName = GetProcessingQueueName(selectedMessages.Queue);
            await _messageService.ReplayMessages(response.Messages, processingQueueName);

            return Json(string.Empty);
        }

        [HttpPost]
        public async Task<IActionResult> DeleteMessages(string data)
        {
            var selectedMessages = JsonConvert.DeserializeObject<SelectedMessages>(data);
            await _messageService.DeleteMessages(selectedMessages.Ids);

            return Json(string.Empty);
        }

        public async Task<IActionResult> Data(string sort, string order, string search, int offset, int limit)
        {
            var response = await _getMessagesQuery.Handle(new GetMessagesQuery()
            {
                UserId = _userService.GetUserId(),
                SearchProperties = new SearchProperties
                {
                    Sort = sort,
                    Order = order,
                    Search = search,
                    Offset = offset,
                    Limit = limit
                }
            });

            var queueMessages = response.Messages.ToList();

            return Json(new
            {
                Total = response.Count,
                TotalNotFiltered = response.UnfilteredCount,
                Rows = queueMessages.Select(msg => new
                {
                    Id = msg.Id,
                    OriginatingEndpoint = msg.OriginatingEndpoint,
                    ProcessingEndpoint = msg.ProcessingEndpoint,
                    Body = msg.Body,
                    Exception = msg.Exception,
                    ExceptionType = msg.ExceptionType
                })
            });
        }

        private string GetQueueName(IEnumerable<QueueMessage> messages)
        {
            var name = "";
            if (messages?.Count() > 0)
            {
                name = messages.First().Queue;
            }

            return name;
        }

        private string GetProcessingQueueName(string errorQueueName)
        {
            return Regex.Replace(errorQueueName, _settings.ErrorQueueRegex, "");
        }

        private async Task DeleteUserSession()
        {
            await _userSessionService.DeleteUserSession();
        }
    }
}
