using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace Talkie.Web.Client.Services;

/// <summary>SignalR connection for live chat updates.</summary>
public sealed class ChatHubClient(NavigationManager navigation, TokenStore tokens) : IAsyncDisposable
{
    private HubConnection? _connection;

    public event Func<Guid?, Guid?, Guid, Task>? MessageChanged;
    public event Func<Task>? ConversationsChanged;
    public event Func<Task>? ChannelsChanged;

    public async Task EnsureStartedAsync()
    {
        if (_connection is not null || tokens.Token is null) return;

        _connection = new HubConnectionBuilder()
            .WithUrl(navigation.ToAbsoluteUri("/hubs/chat"), o => o.AccessTokenProvider = () => Task.FromResult<string?>(tokens.Token))
            .WithAutomaticReconnect()
            .Build();

        _connection.On<Guid?, Guid?, Guid>("MessageChanged", async (conversationId, channelId, messageId) =>
        {
            if (MessageChanged is not null) await MessageChanged.Invoke(conversationId, channelId, messageId);
        });
        _connection.On("ConversationsChanged", async () =>
        {
            if (ConversationsChanged is not null) await ConversationsChanged.Invoke();
        });
        _connection.On("ChannelsChanged", async () =>
        {
            if (ChannelsChanged is not null) await ChannelsChanged.Invoke();
        });

        try
        {
            await _connection.StartAsync();
        }
        catch (Exception)
        {
            // Live updates are a nice-to-have; the app keeps working without them.
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    public async Task StopAsync()
    {
        if (_connection is null) return;
        await _connection.DisposeAsync();
        _connection = null;
    }

    public ValueTask DisposeAsync() => _connection?.DisposeAsync() ?? ValueTask.CompletedTask;
}
