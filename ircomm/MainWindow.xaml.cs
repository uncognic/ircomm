using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;

namespace ircomm
{
    public partial class MainWindow : Window
    {
        private readonly Client _irc = new();

        private readonly ObservableCollection<string> _channels = new();
        private readonly ObservableCollection<string> _chatLines = new();
        private readonly ObservableCollection<string> _users = new();

        public MainWindow()
        {
            InitializeComponent();

            ChannelsListBox.ItemsSource = _channels;
            ChatListBox.ItemsSource = _chatLines;
            UsersListBox.ItemsSource = _users;

            ConnectButton.Click += ConnectButton_Click;
            SendButton.Click += SendButton_Click;
            ChannelsListBox.MouseDoubleClick += ChannelsListBox_MouseDoubleClick;

            SetStatus("Disconnected.", false);

            void Ui(Action a) => Dispatcher.Invoke(a);

            _irc.ChatLine += line => Ui(() => AddChatLine(line));

            _irc.NamesReceived += names =>
            {
                if (names is null) return;
                Ui(() =>
                {
                    foreach (var n in names)
                        if (!_users.Contains(n))
                            _users.Add(n);
                });
            };

            _irc.ChannelAdded += channel => Ui(() =>
            {
                if (!_channels.Contains(channel))
                    _channels.Add(channel);

                ChannelTitle.Text = channel;
                AddChatLine($"You joined {channel}");
                _users.Clear();
                ConnectButton.Content = "Disconnect";
                SetStatus($"Joined {channel}", false);
            });

            _irc.UserJoined += (channel, nick) => Ui(() =>
            {
                if (!_users.Contains(nick)) _users.Add(nick);
                AddChatLine($"{nick} joined {channel}");
            });

            _irc.UserLeft += (channel, nick) => Ui(() =>
            {
                if (_users.Contains(nick)) _users.Remove(nick);
                AddChatLine($"{nick} left {channel}");
            });

            _irc.NickChanged += (oldNick, newNick) => Ui(() =>
            {
                if (_users.Contains(oldNick))
                {
                    _users.Remove(oldNick);
                    if (!_users.Contains(newNick)) _users.Add(newNick);
                }
                AddChatLine($"{oldNick} is now known as {newNick}");
            });

            _irc.Connected += () => Ui(() =>
            {
                ConnectButton.Content = "Disconnect";
                AddChatLine("Connected.");
                SetStatus("Connected.", false);
            });

            _irc.Disconnected += () => Ui(() =>
            {
                ConnectButton.Content = "Connect";
                AddChatLine("Disconnected.");
                _users.Clear();
                SetStatus("Disconnected.", false);
            });
        }

        private async void ConnectButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_irc.IsConnected)
            {
                await DisconnectAsync();
                return;
            }

            var server = ServerTextBox.Text.Trim();
            if (string.IsNullOrEmpty(server))
            {
                MessageBox.Show("Invalid server.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!int.TryParse(PortTextBox.Text.Trim(), out var port))
            {
                MessageBox.Show("Invalid port.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var nick = UsernameTextBox.Text.Trim();
            if (string.IsNullOrEmpty(nick))
            {
                MessageBox.Show("Invalid username.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                ConnectButton.IsEnabled = false;
                SetStatus($"Connecting to {server}:{port}...", true);
                AddChatLine($"Connecting to {server}:{port}...");
                await _irc.ConnectAsync(server, port, nick);
            }
            catch (Exception ex)
            {
                AddChatLine($"Connection failed: {ex.Message}");
                SetStatus($"Connection failed: {ex.Message}", false);
                await DisconnectAsync();
            }
            finally
            {
                ConnectButton.IsEnabled = true;
            }
        }

        private async Task DisconnectAsync()
        {
            try
            {
                SetStatus("Disconnecting...", true);

                if (_irc.IsConnected)
                {
                    try
                    {
                        await _irc.SendRawAsync("QUIT :Client disconnecting");
                    }
                    catch
                    {
                        Debug.Write("Error sending QUIT");
                    }
                }
            }
            finally
            {
                try { await _irc.DisconnectAsync(); } catch { }

                Dispatcher.Invoke(() =>
                {
                    ConnectButton.Content = "Connect";
                    AddChatLine("Disconnected.");
                    _users.Clear();
                    SetStatus("Disconnected.", false);
                });
            }
        }

        private async void SendButton_Click(object? sender, RoutedEventArgs e)
        {
            var text = MessageTextBox.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            if (text.StartsWith("/"))
            {
                var parts = text.Substring(1).Split(new[] { ' ' }, 2, StringSplitOptions.None);
                var cmd = parts[0].ToLowerInvariant();
                var arg = parts.Length > 1 ? parts[1] : string.Empty;

                switch (cmd)
                {
                    case "join":
                        {
                            var channel = arg.Trim();
                            if (!channel.StartsWith("#") && !channel.StartsWith("&")) channel = "#" + channel;
                            await _irc.SendRawAsync($"JOIN {channel}");
                            MessageTextBox.Clear();
                            return;
                        }
                    case "nick":
                        {
                            var newNick = arg.Trim();
                            if (!string.IsNullOrEmpty(newNick))
                                await _irc.SendRawAsync($"NICK {newNick}");
                            MessageTextBox.Clear();
                            return;
                        }
                    case "msg":
                        {
                            var idx = arg.IndexOf(' ');
                            if (idx > 0)
                            {
                                var target = arg.Substring(0, idx);
                                var msg = arg.Substring(idx + 1);
                                await _irc.SendRawAsync($"PRIVMSG {target} :{msg}");
                            }
                            MessageTextBox.Clear();
                            return;
                        }
                    default:
                        AddChatLine($"Unknown command: {cmd}");
                        return;
                }
            }

            var targetChannel = ChannelsListBox.SelectedItem as string ?? ChannelTitle.Text;
            if (string.IsNullOrEmpty(targetChannel))
            {
                AddChatLine("No channel selected.");
                return;
            }

            await _irc.SendRawAsync($"PRIVMSG {targetChannel} :{text}");
            AddChatLine($"[{targetChannel}] <{UsernameTextBox.Text.Trim()}> {text}");
            MessageTextBox.Clear();
        }

        private void ChannelsListBox_MouseDoubleClick(object? sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ChannelsListBox.SelectedItem is string channel)
            {
                ChannelTitle.Text = channel;
                _users.Clear();
                AddChatLine($"Switched to {channel}");
                _ = _irc.SendRawAsync($"NAMES {channel}");
            }
        }

        private void AddChatLine(string line)
        {
            _chatLines.Add(line);
            if (ChatListBox.Items.Count > 0)
                ChatListBox.ScrollIntoView(ChatListBox.Items[ChatListBox.Items.Count - 1]);
        }

        private void SetStatus(string text, bool busy = false)
        {
            Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = text;
                BusyIndicator.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _ = DisconnectAsync();
        }
    }
}