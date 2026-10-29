using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Talkie.Shared;

namespace Talkie.Api.Infrastructure;

/// <summary>
/// Real-time notifications. Clients join their own user group and their company group;
/// the payload only says *what* changed, clients then fetch the data through the REST API
/// (so permissions are always evaluated per user).
/// </summary>
[Authorize]
public sealed class ChatHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        var companyId = Context.User?.FindFirst(ClaimNames.CompanyId)?.Value;
        if (userId is not null) await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(Guid.Parse(userId)));
        if (companyId is not null) await Groups.AddToGroupAsync(Context.ConnectionId, CompanyGroup(Guid.Parse(companyId)));
        await base.OnConnectedAsync();
    }

    public static string UserGroup(Guid userId) => $"user:{userId}";
    public static string CompanyGroup(Guid companyId) => $"company:{companyId}";
}

public sealed class ChatNotifier(IHubContext<ChatHub> hub)
{
    public Task MessageChangedInConversation(IEnumerable<Guid> memberIds, Guid conversationId, Guid messageId) =>
        hub.Clients.Groups(memberIds.Select(ChatHub.UserGroup).ToList())
            .SendAsync("MessageChanged", conversationId, (Guid?)null, messageId);

    public Task MessageChangedInChannel(Guid companyId, Guid channelId, Guid messageId) =>
        hub.Clients.Group(ChatHub.CompanyGroup(companyId))
            .SendAsync("MessageChanged", (Guid?)null, channelId, messageId);

    public Task ConversationsChanged(IEnumerable<Guid> memberIds) =>
        hub.Clients.Groups(memberIds.Select(ChatHub.UserGroup).ToList()).SendAsync("ConversationsChanged");

    public Task ChannelsChanged(Guid companyId) =>
        hub.Clients.Group(ChatHub.CompanyGroup(companyId)).SendAsync("ChannelsChanged");
}
