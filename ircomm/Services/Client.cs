using System;
using System.IO;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ircomm.Services
{

    public class Client : IDisposable
    {
        private TcpClient? _tcp;
        private Stream? _stream;
        private CancellationTokenSource? _cts;
        private Task? _listenTask;
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        private readonly object _stateLock = new();
        private string? _nick;
        private SynchronizationContext? _syncContext;

        private const int ReadBufferSize = 8192;
        private readonly byte[] _readBuffer = new byte[ReadBufferSize];
        private readonly StringBuilder _receiveBuffer = new();

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

            if (string.IsNullOrWhiteSpace(nick)) throw new ArgumentNullException(nameof(nick));

            _nick = nick;
            _syncContext = SynchronizationContext.Current;
            _cts = new CancellationTokenSource();

            _tcp = new TcpClient { NoDelay = true };
            try
            {
                await _tcp.ConnectAsync(server, port).ConfigureAwait(false);

                try
                {
                    _tcp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                }
                catch { }

                var netStream = _tcp.GetStream();

                if (port == 6697)
                {
                    var ssl = new SslStream(netStream, false, (sender, certificate, chain, errors) =>
                    {

                        return errors == System.Net.Security.SslPolicyErrors.None;
                    });

                    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                    {
                        TargetHost = server,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        CertificateRevocationCheckMode = X509RevocationMode.Online
                    }).ConfigureAwait(false);

                    _stream = ssl;
                    PostEvent(() => ChatLine?.Invoke("Using TLS (SslStream)."));
                }
                else
                {
                    _stream = netStream;
                }


                _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));


                await SendRawAsync($"NICK {_nick}").ConfigureAwait(false);
                await SendRawAsync($"USER {_nick} 0 * :{_nick}").ConfigureAwait(false);

                PostEvent(() => ChatLine?.Invoke("Connected."));
                PostEvent(() => Connected?.Invoke());
            }
            catch (Exception ex)
            {
                CleanupConnection();
                throw new InvalidOperationException($"Connect failed: {ex.Message}", ex);
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                if (IsConnected)
                {
                    try
                    {
                        await SendRawAsync("QUIT :Client disconnecting").ConfigureAwait(false);
                    }
                    catch
                    {
                        PostEvent(() => ChatLine?.Invoke("Send QUIT failed."));
                    }
                }
            }
            finally
            {
                try { _cts?.Cancel(); } catch { }

                try
                {
                    var t = _listenTask;
                    if (t != null)
                    {
                        await Task.WhenAny(t, Task.Delay(2000)).ConfigureAwait(false);
                    }
                }
                catch { }

                CleanupConnection();
                PostEvent(() => ChatLine?.Invoke("Disconnected."));
                PostEvent(() => Disconnected?.Invoke());
            }
        }

        public async Task SendRawAsync(string line)
        {
            if (line == null) throw new ArgumentNullException(nameof(line));
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_stream is null || _tcp is null || !_tcp.Connected) throw new InvalidOperationException("Not connected.");

                var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
                await _stream.WriteAsync(bytes, 0, bytes.Length, CancellationToken.None).ConfigureAwait(false);

                try { await _stream.FlushAsync(CancellationToken.None).ConfigureAwait(false); } catch { }

                PostEvent(() => ChatLine?.Invoke($"-> {line}"));
            }
            catch (Exception ex)
            {
                PostEvent(() => ChatLine?.Invoke($"Send failed: {ex.Message}"));
                throw;
            }
            finally
            {
                try { _writeLock.Release(); } catch { }
            }
        }

        public Task JoinAsync(string channel) => SendRawAsync($"JOIN {channel}");
        public Task PartAsync(string channel, string? reason = null) => SendRawAsync(reason == null ? $"PART {channel}" : $"PART {channel} :{reason}");
        public Task SendMessageAsync(string target, string message) => SendRawAsync($"PRIVMSG {target} :{message}");
        public Task ChangeNickAsync(string newNick)
        {
            _nick = newNick;
            return SendRawAsync($"NICK {newNick}");
        }

        private async Task ListenLoopAsync(CancellationToken ct)
        {
            var localStream = _stream;
            if (localStream is null) return;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int read;
                    try
                    {
                        read = await localStream.ReadAsync(_readBuffer, 0, _readBuffer.Length, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (IOException ioEx)
                    {
                        PostEvent(() => ChatLine?.Invoke($"Listen loop ended: connection closed ({ioEx.Message})."));
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        PostEvent(() => ChatLine?.Invoke($"Listen loop read error: {ex.Message}"));
                        break;
                    }

                    if (read == 0)
                    {

                        PostEvent(() => ChatLine?.Invoke("Remote closed the connection (read==0)."));
                        break;
                    }


                    var chunk = Encoding.UTF8.GetString(_readBuffer, 0, read);
                    _receiveBuffer.Append(chunk);


                    while (true)
                    {
                        var line = ExtractLineFromReceiveBuffer();
                        if (line is null) break;
                        try
                        {
                            ProcessServerLine(line);
                        }
                        catch (Exception ex)
                        {
                            PostEvent(() => ChatLine?.Invoke($"Error processing line: {ex.Message}"));
                        }
                    }
                }
            }
            finally
            {
                try { _cts?.Cancel(); } catch { }
                CleanupConnection();
                PostEvent(() => ChatLine?.Invoke("Listen loop stopped."));
                PostEvent(() => Disconnected?.Invoke());
            }
        }


        private string? ExtractLineFromReceiveBuffer()
        {
            for (int i = 0; i < _receiveBuffer.Length; i++)
            {
                if (_receiveBuffer[i] == '\n')
                {
                    int lineEnd = i;
                    int length = lineEnd;
                    if (lineEnd > 0 && _receiveBuffer[lineEnd - 1] == '\r') length = lineEnd - 1;

                    var line = _receiveBuffer.ToString(0, length);
                    _receiveBuffer.Remove(0, i + 1);
                    return line;
                }
            }
            return null;
        }

        private void ProcessServerLine(string line)
        {
            PostEvent(() => ChatLine?.Invoke($"<- {line}"));

            if (line.StartsWith("PING ", StringComparison.OrdinalIgnoreCase))
            {
                var token = line.Substring(5).Trim();

                _ = SendRawAsync($"PONG {token}");
                return;
            }

            var prefix = string.Empty;
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
            var parameters = firstSpace > 0 ? rest.Substring(firstSpace + 1) : string.Empty;

            if (int.TryParse(command, out var numeric))
            {
                if (numeric == 353)
                {
                    var trailingIndex = parameters.IndexOf(" :");
                    if (trailingIndex >= 0)
                    {
                        var namesPart = parameters.Substring(trailingIndex + 2);
                        var names = namesPart.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        PostEvent(() => NamesReceived?.Invoke(names));
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
                    PostEvent(() => ChatLine?.Invoke($"[{displayTarget}] <{senderNick}> {message}"));
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
                    PostEvent(() => ChannelAdded?.Invoke(channel));
                    PostEvent(() => ChatLine?.Invoke($"You joined {channel}"));
                }
                else
                {
                    PostEvent(() => UserJoined?.Invoke(channel, nick));
                    PostEvent(() => ChatLine?.Invoke($"{nick} joined {channel}"));
                }
                return;
            }

            if (string.Equals(command, "PART", StringComparison.OrdinalIgnoreCase))
            {
                var channel = parameters.Split(' ')[0].Trim();
                var nick = prefix.Split('!')[0];
                PostEvent(() => UserLeft?.Invoke(channel, nick));
                PostEvent(() => ChatLine?.Invoke($"{nick} left {channel}"));
                return;
            }

            if (string.Equals(command, "NICK", StringComparison.OrdinalIgnoreCase))
            {
                var newNick = parameters.Trim();
                if (newNick.StartsWith(":")) newNick = newNick.Substring(1);
                var oldNick = prefix.Split('!')[0];
                if (oldNick == _nick) _nick = newNick;
                PostEvent(() => NickChanged?.Invoke(oldNick, newNick));
                PostEvent(() => ChatLine?.Invoke($"{oldNick} is now known as {newNick}"));
                return;
            }
        }

        private void PostEvent(Action action)
        {
            try
            {
                if (_syncContext != null)
                {
                    _syncContext.Post(_ => action(), null);
                }
                else
                {
                    Task.Run(action);
                }
            }
            catch
            {

            }
        }

        private void CleanupConnection()
        {
            lock (_stateLock)
            {
                try { _stream?.Close(); } catch { }
                try { _tcp?.Client?.Shutdown(SocketShutdown.Both); } catch { }
                try { _tcp?.Close(); } catch { }

                try { _stream?.Dispose(); } catch { }
                try { _tcp?.Dispose(); } catch { }

                _stream = null;
                _tcp = null;

                try { _listenTask?.Wait(100); } catch { }

                try { _cts?.Dispose(); } catch { }
                _cts = null;
                _listenTask = null;
            }
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            CleanupConnection();
            _writeLock.Dispose();
        }
    }
}