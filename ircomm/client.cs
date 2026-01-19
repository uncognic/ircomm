using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ircomm
{
    public class Client
    {
        private TcpClient? _tcp;
        private StreamWriter? _writer;
        private CancellationTokenSource? _cts;
        private Task? _listenTask;
        private string? _nick;

        public bool IsConnected => _tcp?.Connected ?? false;

        public event Action<string>? ChatLine;
        public event Action? Connected;
        public event Action? Disconnected;
        public event Action<string>? ChannelAdded;
        public event Action<string[]?>? NamesReceived;
        public event Action<string, string>? UserJoined;
        public event Action<string, string>? UserLeft;
        public event Action<string, string>? NickChanged;

        public async Task ConnectAsync(string server, int port, string nick)
        {
            if (IsConnected)
                throw new InvalidOperationException("Already connected.");

            _nick = nick ?? throw new ArgumentNullException(nameof(nick));

            _tcp = new TcpClient();
            await _tcp.ConnectAsync(server, port).ConfigureAwait(false);

            var stream = _tcp.GetStream();
            _writer = new StreamWriter(stream, Encoding.UTF8)
            {
                NewLine = "\r\n",
                AutoFlush = true
            };

            await SendRawAsync($"NICK {_nick}").ConfigureAwait(false);
            await SendRawAsync($"USER {_nick} 0 * :{_nick}").ConfigureAwait(false);

            _cts = new CancellationTokenSource();
            _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));

            ChatLine?.Invoke("Connected.");
            Connected?.Invoke();
        }

        public async Task DisconnectAsync()
        {
            try
            {
                _cts?.Cancel();

                if (IsConnected)
                {
                    try
                    {
                        await SendRawAsync("QUIT :Client disconnecting").ConfigureAwait(false);
                    }
                    catch
                    {
                        ChatLine?.Invoke("Send QUIT failed.");
                    }
                }
            }
            finally
            {
                try
                {
                    _writer?.Dispose();
                    _tcp?.Close();
                }
                catch { }

                _writer = null;
                _tcp = null;

                try
                {
                    _listenTask?.Wait(100);
                }
                catch { }

                _cts?.Dispose();
                _cts = null;

                ChatLine?.Invoke("Disconnected.");
                Disconnected?.Invoke();
            }
        }

        public async Task SendRawAsync(string line)
        {
            try
            {
                if (_writer is null) throw new InvalidOperationException("Not connected.");
                await _writer.WriteLineAsync(line).ConfigureAwait(false);
                ChatLine?.Invoke($"-> {line}");
            }
            catch (Exception ex)
            {
                ChatLine?.Invoke($"Send failed: {ex.Message}");
            }
        }

        private async Task ListenLoopAsync(CancellationToken ct)
        {
            if (_tcp is null) return;

            try
            {
                using var reader = new StreamReader(_tcp.GetStream(), Encoding.UTF8);
                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line == null) break;
                    ProcessServerLine(line);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ChatLine?.Invoke($"Error in listen loop: {ex.Message}");
            }
            finally
            {
                try
                {
                    await DisconnectAsync().ConfigureAwait(false);
                }
                catch { }
            }
        }

        private void ProcessServerLine(string line)
        {
            ChatLine?.Invoke($"<- {line}");

            if (line.StartsWith("PING ", StringComparison.Ordinal))
            {
                var token = line.Substring("PING ".Length).Trim();
                _ = SendRawAsync($"PONG {token}");
                return;
            }

            var prefix = "";
            var rest = line;
            if (rest.StartsWith(":"))
            {
                var idx = rest.IndexOf(' ');
                if (idx > 0)
                {
                    prefix = rest.Substring(1, idx - 1);
                    rest = rest.Substring(idx + 1);
                }
            }

            var firstSpace = rest.IndexOf(' ');
            var command = firstSpace > 0 ? rest.Substring(0, firstSpace) : rest;
            var parameters = firstSpace > 0 ? rest.Substring(firstSpace + 1) : "";

            if (int.TryParse(command, out var numeric))
            {
                if (numeric == 353)
                {
                    var trailingIndex = parameters.IndexOf(" :");
                    if (trailingIndex >= 0)
                    {
                        var namesPart = parameters.Substring(trailingIndex + 2);
                        var names = namesPart.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        NamesReceived?.Invoke(names);
                    }
                }
                return;
            }

            if (string.Equals(command, "PRIVMSG", StringComparison.OrdinalIgnoreCase))
            {
                var targetEnd = parameters.IndexOf(" :");
                if (targetEnd >= 0)
                {
                    var target = parameters.Substring(0, targetEnd).Trim();
                    var message = parameters.Substring(targetEnd + 2);
                    var senderNick = prefix.Split('!')[0];

                    var displayTarget = target.StartsWith("#") ? target : senderNick;
                    ChatLine?.Invoke($"[{displayTarget}] <{senderNick}> {message}");
                }
                return;
            }

            if (string.Equals(command, "JOIN", StringComparison.OrdinalIgnoreCase))
            {
                var channel = parameters.Trim();
                if (channel.StartsWith(":")) channel = channel.Substring(1);
                var nick = prefix.Split('!')[0];
                if (nick == _nick)
                {
                    ChannelAdded?.Invoke(channel);
                    ChatLine?.Invoke($"You joined {channel}");
                }
                else
                {
                    UserJoined?.Invoke(channel, nick);
                    ChatLine?.Invoke($"{nick} joined {channel}");
                }
                return;
            }

            if (string.Equals(command, "PART", StringComparison.OrdinalIgnoreCase))
            {
                var channel = parameters.Split(' ')[0].Trim();
                var nick = prefix.Split('!')[0];
                UserLeft?.Invoke(channel, nick);
                ChatLine?.Invoke($"{nick} left {channel}");
                return;
            }

            if (string.Equals(command, "NICK", StringComparison.OrdinalIgnoreCase))
            {
                var newNick = parameters.Trim();
                if (newNick.StartsWith(":")) newNick = newNick.Substring(1);
                var oldNick = prefix.Split('!')[0];
                NickChanged?.Invoke(oldNick, newNick);
                ChatLine?.Invoke($"{oldNick} is now known as {newNick}");
                return;
            }
        }
    }
}