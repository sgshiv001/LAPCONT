// LapCont — Service — Outbound per-phone relay routes with independent protected credentials
// License: MIT
using System.Net.WebSockets;
namespace LapCont.Service;
/// <summary>One route/channel. Normal relay CA/hostname trust; inner mutual TLS encrypts payloads.</summary>
public static class RelayConnector
{
    public sealed record Route(string PhoneId, string Url, string AgentCredential);
    public static async Task RunAsync(string pc, Route route, string channel, Func<WebSocket, byte[], CancellationToken, Task> endpoint, Action<string> log, CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket(); socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                socket.Options.SetRequestHeader("Authorization", "Bearer " + route.AgentCredential);
                socket.Options.SetRequestHeader("X-LapCont-Route", pc); socket.Options.SetRequestHeader("X-LapCont-Phone", route.PhoneId);
                socket.Options.SetRequestHeader("X-LapCont-Role", "agent"); socket.Options.SetRequestHeader("X-LapCont-Channel", channel);
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct)) { timeout.CancelAfter(TimeSpan.FromSeconds(10)); await socket.ConnectAsync(new Uri(route.Url), timeout.Token); }
                var first = new byte[65536]; var length = 0;
                while (true) { if (length == first.Length) throw new IOException("Relay frame too large"); var r = await socket.ReceiveAsync(first.AsMemory(length), ct);
                    if (r.MessageType != WebSocketMessageType.Binary) throw new IOException("Relay peer disconnected"); length += r.Count; if (r.EndOfMessage) break; }
                if (length < 1) throw new IOException("Empty relay frame"); attempt = 0; await endpoint(socket, first[..length], ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception e) { log($"relay channel={channel} error={e.GetType().Name}"); }
            attempt = Math.Min(attempt + 1, 9); var delay = Math.Min(300000, 1000 * (1 << attempt));
            await Task.Delay(TimeSpan.FromMilliseconds(delay * (0.75 + Random.Shared.NextDouble() * 0.25)), ct);
        }
    }
    public static async Task RevokeAsync(string pc, Route route, CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var uri = new UriBuilder(route.Url) { Scheme = "https", Path = "/revoke", Query = "" }.Uri;
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + route.AgentCredential);
        request.Headers.Add("X-LapCont-Route", pc); request.Headers.Add("X-LapCont-Phone", route.PhoneId); request.Headers.Add("X-LapCont-Role", "agent");
        using var result = await client.SendAsync(request, ct); result.EnsureSuccessStatusCode();
    }
}
