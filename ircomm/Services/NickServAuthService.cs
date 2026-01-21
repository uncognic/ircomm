using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ircomm.Services
{

    public class NickServAuthService
    {
        private readonly Client _client;
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

        public NickServAuthService(Client client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async Task<bool> IdentifyAsync(string password, TimeSpan? timeout = null)
        {
            if (password is null) throw new ArgumentNullException(nameof(password));
            if (!_client.IsConnected) throw new InvalidOperationException("Client is not connected.");

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cts = new CancellationTokenSource(timeout ?? DefaultTimeout);

            void OnChatLine(string line)
            {
                try
                {
                    if (string.IsNullOrEmpty(line)) return;

                    var normalized = line.Trim();
                    if (normalized.StartsWith("<- ")) normalized = normalized.Substring(3).Trim();
                    if (normalized.StartsWith("-> ")) normalized = normalized.Substring(3).Trim();


                    if (!normalized.Contains("NickServ", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!normalized.Contains("<NickServ>", StringComparison.OrdinalIgnoreCase))
                            return;
                    }

                    var lower = normalized.ToLowerInvariant();
                    if (lower.Contains("you are now identified"))
                    {
                        tcs.TrySetResult(true);
                        return;
                    }

                    
                    if (lower.Contains("invalid password"))
                    {
                        tcs.TrySetResult(false);
                        return;
                    }

                    if (lower.Contains("this nickname is registered") && lower.Contains("you may use"))
                    {
                        tcs.TrySetResult(true);
                        return;
                    }
                }
                catch
                {
                    Debug.WriteLine("Parse error");
                }
            }

            try
            {
                _client.ChatLine += OnChatLine;

                try
                {
                    await _client.SendMessageAsync("NickServ", $"IDENTIFY {password}").ConfigureAwait(false);
                }
                catch
                {
                    try { await _client.SendRawAsync($"PRIVMSG NickServ :IDENTIFY {password}").ConfigureAwait(false); } catch { }
                }

                using (cts)
                {
                    cts.Token.Register(() => tcs.TrySetResult(false));
                    return await tcs.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                _client.ChatLine -= OnChatLine;
            }
        }
    }
}