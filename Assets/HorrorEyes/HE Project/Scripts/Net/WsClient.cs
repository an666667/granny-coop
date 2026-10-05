using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GrannyCoop
{
    /// <summary>
    /// Thin WebSocket client wrapper (System.Net.WebSockets).
    /// Receives on a background task and exposes messages on the main thread via TryDequeue.
    /// Requires Player Settings -> Api Compatibility Level = .NET 4.x (or .NET Standard 2.0).
    /// </summary>
    public class WsClient
    {
        ClientWebSocket _ws;
        CancellationTokenSource _cts;
        readonly ConcurrentQueue<string> _inbox = new ConcurrentQueue<string>();

        public volatile bool Connected;
        public string LastError;

        public async void Connect(string url)
        {
            try
            {
                _ws = new ClientWebSocket();
                _cts = new CancellationTokenSource();
                await _ws.ConnectAsync(new Uri(url), _cts.Token);
                Connected = true;
                NetConfig.Log("ws connected: " + url);
                Task.Run(new Func<Task>(ReceiveLoop));
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Connected = false;
                NetConfig.LogError("ws connect failed: " + e.Message);
            }
        }

        async Task ReceiveLoop()
        {
            var buf = new byte[8192];
            var sb = new StringBuilder();
            try
            {
                while (_ws != null && _ws.State == WebSocketState.Open)
                {
                    var res = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), _cts.Token);
                    if (res.MessageType == WebSocketMessageType.Close) break;
                    if (res.MessageType == WebSocketMessageType.Text && res.Count > 0)
                        sb.Append(Encoding.UTF8.GetString(buf, 0, res.Count));
                    if (res.EndOfMessage)
                    {
                        if (sb.Length > 0) _inbox.Enqueue(sb.ToString());
                        sb.Length = 0;
                    }
                }
            }
            catch (Exception e)
            {
                LastError = e.Message;
            }
            Connected = false;
            NetConfig.Log("ws receive loop ended");
        }

        public void Send(string s)
        {
            if (_ws == null || _ws.State != WebSocketState.Open) return;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(s);
                var seg = new ArraySegment<byte>(bytes);
                Task.Run(() => { try { _ws.SendAsync(seg, WebSocketMessageType.Text, true, _cts.Token); } catch { } });
            }
            catch (Exception e)
            {
                NetConfig.LogError("ws send failed: " + e.Message);
            }
        }

        public bool TryDequeue(out string msg) { return _inbox.TryDequeue(out msg); }

        public void Close()
        {
            try { if (_cts != null) _cts.Cancel(); } catch { }
            try
            {
                if (_ws != null && _ws.State == WebSocketState.Open)
                    _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            }
            catch { }
            _ws = null;
            Connected = false;
        }
    }
}
