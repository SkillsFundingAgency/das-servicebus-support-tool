﻿using Microsoft.AspNetCore.Http;
using SFA.DAS.Tools.Servicebus.Support.Audit.Types;
using System.Linq;
using System.Security.Claims;

namespace SFA.DAS.Tools.Servicebus.Support.Audit.MessageBuilders
{
    public class ChangedByMessageBuilder : IAuditMessageBuilder
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private const string _userIdClaim = ClaimTypes.NameIdentifier;
        private const string _userEmailClaim = ClaimTypes.Email;

        public ChangedByMessageBuilder(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public void Build(AuditMessage message)
        {
            message.ChangedBy = new Actor();
            SetOriginIpAddess(message.ChangedBy);
            SetUserIdAndEmail(message.ChangedBy);
        }

        private void SetOriginIpAddess(Actor actor)
        {
            actor.OriginIpAddress = _httpContextAccessor.HttpContext.Connection.RemoteIpAddress.ToString() == "::1"
                ? "127.0.0.1"
                : _httpContextAccessor.HttpContext.Connection.RemoteIpAddress.ToString();
        }

        private void SetUserIdAndEmail(Actor actor)
        {
            var user = _httpContextAccessor.HttpContext.User;
            if (user == null || user.Identity == null || !user.Identity.IsAuthenticated)
            {
                return;
            }

            var userIdClaim = user.Claims.FirstOrDefault(c => c.Type.Equals(_userIdClaim, System.StringComparison.CurrentCultureIgnoreCase));
            if (userIdClaim == null)
            {
                throw new InvalidContextException($"User does not have claim {_userIdClaim} to populate AuditMessage.ChangedBy.Id");
            }
            actor.Id = userIdClaim.Value;


            var userEmailClaim = user.Claims.FirstOrDefault(c => c.Type.Equals(_userEmailClaim, System.StringComparison.CurrentCultureIgnoreCase));
            if (userEmailClaim == null)
            {
                throw new InvalidContextException($"User does not have claim {_userEmailClaim} to populate AuditMessage.ChangedBy.EmailAddress");
            }
            actor.EmailAddress = userEmailClaim.Value;
        }
    }
}
