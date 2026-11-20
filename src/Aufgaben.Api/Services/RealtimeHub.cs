using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace Aufgaben.Api.Services;

/// <summary>
/// Verteilt Änderungen live an geöffnete Browser- und Teams-Fenster (Server-Sent Events).
/// Die Nachricht enthält nur Art und Kennung; die Oberfläche lädt danach selbst nach
/// und bekommt dabei nur, was sie sehen darf.
/// </summary>
public sealed class RealtimeHub
{
    private sealed record Subscriber(int UserId, Channel<string> Channel);

    private readonly ConcurrentDictionary<Guid, Subscriber> _subscribers = new();

    public int ConnectionCount => _subscribers.Count;

    public (Guid Id, ChannelReader<string> Reader) Subscribe(int userId)
    {
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(200)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        var id = Guid.NewGuid();
        _subscribers[id] = new Subscriber(userId, channel);
        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var sub))
        {
            sub.Channel.Writer.TryComplete();
        }
    }

    public void Publish(IEnumerable<int> userIds, object message)
    {
        var targets = userIds.ToHashSet();
        if (targets.Count == 0)
        {
            return;
        }
        var json = JsonSerializer.Serialize(message, JsonSerializerOptions.Web);
        foreach (var sub in _subscribers.Values)
        {
            if (targets.Contains(sub.UserId))
            {
                sub.Channel.Writer.TryWrite(json);
            }
        }
    }

    public void Broadcast(object message)
    {
        var json = JsonSerializer.Serialize(message, JsonSerializerOptions.Web);
        foreach (var sub in _subscribers.Values)
        {
            sub.Channel.Writer.TryWrite(json);
        }
    }
}
