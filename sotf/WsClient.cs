using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SotfPassthrough
{
    /// <summary>WebSocket client to the Minecraft mod (a server on 127.0.0.1:25599). Reconnects forever; never blocks the game thread.</summary>
    public sealed class WsClient
    {
        readonly Uri _uri;
        readonly ConcurrentQueue<string> _out = new ConcurrentQueue<string>();
        readonly ConcurrentQueue<string> _in = new ConcurrentQueue<string>();
        volatile bool _connected;
        public bool Connected => _connected;
        /// <summary>True once after each (re)connect: the game side should resend its ground.</summary>
        public volatile bool JustConnected;

        public WsClient(string url)
        {
            _uri = new Uri(url);
            Task.Run(Loop);
        }

        public void Send(string json)
        {
            if (_connected && _out.Count < 256) _out.Enqueue(json);
        }

        public bool TryReceive(out string msg) => _in.TryDequeue(out msg);

        async Task Loop()
        {
            var buf = new byte[64 * 1024];
            while (true)
            {
                try
                {
                    using var ws = new ClientWebSocket();
                    await ws.ConnectAsync(_uri, CancellationToken.None);
                    _connected = true; JustConnected = true;
                    var recv = Task.Run(async () =>
                    {
                        while (ws.State == WebSocketState.Open)
                        {
                            var sb = new StringBuilder();
                            WebSocketReceiveResult r;
                            do
                            {
                                r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
                                if (r.MessageType == WebSocketMessageType.Close) return;
                                sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                            } while (!r.EndOfMessage);
                            if (_in.Count < 512) _in.Enqueue(sb.ToString());
                        }
                    });
                    while (ws.State == WebSocketState.Open && !recv.IsCompleted)
                    {
                        if (_out.TryDequeue(out var m))
                            await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(m)), WebSocketMessageType.Text, true, CancellationToken.None);
                        else await Task.Delay(2);
                    }
                }
                catch (Exception) { /* Minecraft not up yet, or it closed: retry */ }
                _connected = false;
                while (_out.TryDequeue(out _)) { }
                await Task.Delay(1000);
            }
        }
    }
}
