using ircomm.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ircomm
{
    public partial class MainWindow : Window
    {
        private readonly Client _irc = new();

        private readonly ObservableCollection<string> _channels = new();
        private readonly ObservableCollection<string> _chatLines = new();
        private readonly ObservableCollection<string> _users = new();
        private readonly ObservableCollection<Profile> _profiles = new();

        private string _currentNick = string.Empty;

        public MainWindow()
        {
            InitializeComponent();

            ChannelsListBox.ItemsSource = _channels;
            ChatListBox.ItemsSource = _chatLines;
            UsersListBox.ItemsSource = _users;

            ProfileComboBox.ItemsSource = _profiles;
            ProfileComboBox.SelectionChanged += ProfileComboBox_SelectionChanged;
            DirectConnectionButton.Click += DirectConnectionButton_Click;

            ConnectButton.Click += ConnectButton_Click;
            SendButton.Click += SendButton_Click;
            ChannelsListBox.MouseDoubleClick += ChannelsListBox_MouseDoubleClick;
            AddChannelButton.Click += AddChannelButton_Click;

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

        private void ProfileComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {

            if (ProfileComboBox.SelectedItem is Profile profile)
                ConnectButton.ToolTip = profile.Name;
            else
                ConnectButton.ToolTip = "Use a profile or click Direct Connection";
        }

        private async void DirectConnectionButton_Click(object? sender, RoutedEventArgs e)
        {
            var win = new DirectConnectionWindow { Owner = this };
            if (win.ShowDialog() == true && win.ResultProfile != null)
            {
                await ConnectToAsync(win.ResultProfile.Server ?? string.Empty, win.ResultProfile.Port, win.ResultProfile.Username ?? string.Empty);
            }
        }

        private async Task ConnectToAsync(string server, int port, string nick)
        {
            if (_irc.IsConnected)
            {
                await DisconnectAsync();
                return;
            }

            if (string.IsNullOrEmpty(server))
            {
                MessageBox.Show("Invalid server.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (port <= 0)
            {
                MessageBox.Show("Invalid port.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrEmpty(nick))
            {
                MessageBox.Show("Invalid username.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _currentNick = nick;

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

        private async void ConnectButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_irc.IsConnected)
            {
                await DisconnectAsync();
                return;
            }

            if (ProfileComboBox.SelectedItem is Profile profile)
            {
                await ConnectToAsync(profile.Server ?? string.Empty, profile.Port, profile.Username ?? string.Empty);
                return;
            }


            var win = new DirectConnectionWindow { Owner = this };
            if (win.ShowDialog() == true && win.ResultProfile != null)
            {
                await ConnectToAsync(win.ResultProfile.Server ?? string.Empty, win.ResultProfile.Port, win.ResultProfile.Username ?? string.Empty);
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
                    case "quit":
                        {
                            await _irc.SendRawAsync("QUIT :Client exiting");
                            MessageTextBox.Clear();
                            AddChatLine("Quit successful.");
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
            AddChatLine($"[{targetChannel}] <{_currentNick}> {text}");
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

        private async void AddChannelButton_Click(object? sender, RoutedEventArgs e)
        {
            var input = ShowChannelDialog();
            if (string.IsNullOrWhiteSpace(input)) return;

            var channel = input.Trim();
            if (!channel.StartsWith("#") && !channel.StartsWith("&")) channel = "#" + channel;

            await _irc.SendRawAsync($"JOIN {channel}");
        }

        private string? ShowChannelDialog()
        {
            var win = new Window
            {
                Title = "Join Channel",
                Width = 380,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = this,
                ShowInTaskbar = false
            };

            var panel = new StackPanel { Margin = new Thickness(10) };
            panel.Children.Add(new TextBlock { Text = "Enter channel name (without leading #):", Margin = new Thickness(0, 0, 0, 8) });

            var textBox = new TextBox { Width = 340 };
            panel.Children.Add(textBox);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "OK", Width = 80, IsDefault = true };
            var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };

            ok.Click += (_, __) =>
            {
                if (string.IsNullOrWhiteSpace(textBox.Text))
                {
                    MessageBox.Show(win, "Please enter a channel name.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                win.DialogResult = true;
                win.Close();
            };

            cancel.Click += (_, __) =>
            {
                win.DialogResult = false;
                win.Close();
            };

            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            win.Content = panel;

            var result = win.ShowDialog();
            return result == true ? textBox.Text : null;
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

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(this, "IRComm\nVersion 1.0", "About IRComm", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void PreferencesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var win = new PreferencesWindow { Owner = this };
            win.ShowDialog();
        }

        private void AddProfileMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var win = new AddProfileWindow { Owner = this };
            if (win.ShowDialog() == true && win.CreatedProfile != null)
            {
                _profiles.Add(win.CreatedProfile);
                MessageBox.Show(this, $"Profile '{win.CreatedProfile.Name}' added.", "Profiles", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void EditProfileMenuItem_Click(object sender, RoutedEventArgs e)
        {

        }

        private void DeleteProfileMenuItem_Click(object sender, RoutedEventArgs e)
        {

        }

        private void DisconnectMenuItem_Click(object sender, RoutedEventArgs e)
        {
            DisconnectAsync();
        }

    }
}