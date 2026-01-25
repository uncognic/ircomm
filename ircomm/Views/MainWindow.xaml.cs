using ircomm.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Text.RegularExpressions;

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


        private readonly Dictionary<string, List<string>> _messageStore = new(StringComparer.OrdinalIgnoreCase);


        private readonly Dictionary<string, HashSet<string>> _userStore = new(StringComparer.OrdinalIgnoreCase);

        private string? _currentServerPseudo;

        private Settings _settings = new();


        private bool _awaitingUserMode = false;

        public MainWindow()
        {
            InitializeComponent();

            ChannelsListBox.ItemsSource = _channels;
            ChatListBox.ItemsSource = _chatLines;
            UsersListBox.ItemsSource = _users;


            _users.CollectionChanged += (_, __) => Dispatcher.Invoke(UpdateUsersTitle);
            UpdateUsersTitle();

            ProfileComboBox.ItemsSource = _profiles;
            ProfileComboBox.SelectionChanged += ProfileComboBox_SelectionChanged;
            ChannelsListBox.SelectionChanged += ChannelsListBox_SelectionChanged;
            DirectConnectionButton.Click += DirectConnectionButton_Click;

            ConnectButton.Click += ConnectButton_Click;
            SendButton.Click += SendButton_Click;
            ChannelsListBox.MouseDoubleClick += ChannelsListBox_MouseDoubleClick;
            AddChannelButton.Click += AddChannelButton_Click;

            SetStatus("Disconnected.", false);

            var loaded = ProfileStore.LoadProfiles();
            foreach (var p in loaded) _profiles.Add(p);

            _settings = SettingsStore.LoadSettings() ?? new Settings();
            if (string.IsNullOrWhiteSpace(_settings.AutoSaveFile))
            {
                _settings.AutoSaveFile = Path.Combine(AppContext.BaseDirectory, "chat.txt");
            }

            void Ui(Action a) => Dispatcher.Invoke(a);


            _irc.ChatLine += line => Ui(() => RouteIncomingLine(line));


            _irc.NamesReceived += (channel, names) =>
            {
                if (string.IsNullOrWhiteSpace(channel)) return;
                Ui(() =>
                {
                    EnsureUserStore(channel);
                    var set = _userStore[channel];
                    set.Clear();
                    if (names != null)
                    {
                        foreach (var n in names)
                            set.Add(n);
                    }


                    if (string.Equals(ChannelTitle.Text, channel, StringComparison.OrdinalIgnoreCase))
                    {
                        _users.Clear();
                        foreach (var n in set.OrderBy(x => x))
                            _users.Add(n);
                    }
                });
            };

            _irc.ChannelAdded += channel => Ui(() =>
            {
                if (!_channels.Contains(channel))
                    _channels.Add(channel);

                EnsureMessageStore(channel);
                EnsureUserStore(channel);

                AddChatLine($"You joined {channel}", channel);


                SwitchToChannel(channel);

                if (ProfileComboBox.SelectedItem is Profile prof)
                {
                    prof.Channels ??= new List<string>();
                    if (!prof.Channels.Contains(channel))
                    {
                        prof.Channels.Add(channel);
                        ProfileStore.SaveProfiles(_profiles);
                    }
                }
            });

            _irc.UserJoined += (channel, nick) => Ui(() =>
            {
                if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(nick)) return;

                EnsureUserStore(channel);
                var set = _userStore[channel];
                var added = set.Add(nick);

                if (string.Equals(ChannelTitle.Text, channel, StringComparison.OrdinalIgnoreCase))
                {
                    if (added && !_users.Contains(nick))
                        _users.Add(nick);
                }

                AddChatLine($"{nick} joined {channel}", channel);
            });

            _irc.UserLeft += (channel, nick) => Ui(() =>
            {
                if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(nick)) return;

                EnsureUserStore(channel);
                var set = _userStore[channel];
                var removed = set.Remove(nick);

                if (string.Equals(ChannelTitle.Text, channel, StringComparison.OrdinalIgnoreCase))
                {
                    if (removed && _users.Contains(nick))
                        _users.Remove(nick);
                }

                AddChatLine($"{nick} left {channel}", channel);
            });

            _irc.NickChanged += (oldNick, newNick) => Ui(() =>
            {

                if (!string.IsNullOrWhiteSpace(oldNick) && !string.IsNullOrWhiteSpace(newNick))
                {
                    foreach (var kv in _userStore)
                    {
                        if (kv.Value.Remove(oldNick))
                        {

                            kv.Value.Add(newNick);
                        }
                    }


                    if (_users.Contains(oldNick))
                    {
                        _users.Remove(oldNick);
                        if (!_users.Contains(newNick)) _users.Add(newNick);
                    }
                }


                AddChatLine($"{oldNick} is now known as {newNick}");
            });

            _irc.Connected += () => Ui(async () =>
            {
                ConnectButton.Content = "Disconnect";
                AddChatLine("Connected.");

              
                SetStatus("Connected (waiting for server acknowledgement)...", true);

                if (!string.IsNullOrEmpty(_currentServerPseudo))
                {
                    if (!_channels.Contains(_currentServerPseudo))
                        _channels.Insert(0, _currentServerPseudo);

                    EnsureMessageStore(_currentServerPseudo);

                    SwitchToChannel(_currentServerPseudo);
                }

                if (ProfileComboBox.SelectedItem is Profile prof)
                {
                    if (prof.Channels != null && prof.Channels.Count > 0)
                    {
                        foreach (var ch in prof.Channels)
                        {
                            try
                            {
                                await _irc.SendRawAsync($"JOIN {ch}");
                            }
                            catch
                            {
                                Debug.WriteLine("Failed to join channels");
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(prof.Password))
                    {
                        try
                        {
                            var auth = new NickServAuthService(_irc);
                            var ok = await auth.IdentifyAsync(prof.Password, TimeSpan.FromSeconds(8));
                            AddChatLine(ok ? "NickServ: identified successfully." : "NickServ: identify failed or timed out.", _currentServerPseudo);
                        }
                        catch
                        {
                            AddChatLine("NickServ: identify attempt failed.", _currentServerPseudo);
                        }
                    }
                }
            });

            _irc.Disconnected += () => Ui(() =>
            {
                ConnectButton.Content = "Connect";
                AddChatLine("Disconnected.");
                _users.Clear();
                SetStatus("Disconnected.", false);

                _userStore.Clear();

                _awaitingUserMode = false;
            });
        }

        private void ProfileComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ProfileComboBox.SelectedItem is Profile profile)
            {
                ConnectButton.ToolTip = profile.Name;

                _channels.Clear();
                if (profile.Channels != null)
                {
                    foreach (var ch in profile.Channels)
                    {
                        if (!_channels.Contains(ch))
                            _channels.Add(ch);
                    }
                }
            }
            else
                ConnectButton.ToolTip = "Use a profile or click Direct Connection";
        }

        private async void DirectConnectionButton_Click(object? sender, RoutedEventArgs e)
        {
            var win = new DirectConnectionWindow { Owner = this };
            if (win.ShowDialog() == true && win.ResultProfile != null)
            {
                await ConnectToAsync(win.ResultProfile);
            }
        }

        private async Task ConnectToAsync(Profile profile)
        {
            if (_irc.IsConnected)
            {
                await DisconnectAsync();
                return;
            }

            if (profile == null || string.IsNullOrEmpty(profile.Server))
            {
                MessageBox.Show("Invalid server.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (profile.Port <= 0)
            {
                MessageBox.Show("Invalid port.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrEmpty(profile.Username))
            {
                MessageBox.Show("Invalid username.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _currentNick = profile.Username;


            _currentServerPseudo = $"{profile.Server}:{profile.Port}";
            EnsureMessageStore(_currentServerPseudo);

            Profile? selectedProfile = ProfileComboBox.SelectedItem as Profile;
            bool willHandleAuthInConnected = ReferenceEquals(selectedProfile, profile);

            try
            {
                ConnectButton.IsEnabled = false;


                _awaitingUserMode = true;

                SetStatus($"Connecting to {profile.Server}:{profile.Port}...", true);
                AddChatLine($"Connecting to {profile.Server}:{profile.Port}...", _currentServerPseudo);
                await _irc.ConnectAsync(profile.Server ?? string.Empty, profile.Port, profile.Username ?? string.Empty);


                if (!willHandleAuthInConnected && !string.IsNullOrEmpty(profile.Password))
                {
                    try
                    {
                        var auth = new NickServAuthService(_irc);
                        var ok = await auth.IdentifyAsync(profile.Password, TimeSpan.FromSeconds(8));
                        AddChatLine(ok ? "NickServ: identified successfully." : "NickServ: identify failed or timed out.", _currentServerPseudo);
                    }
                    catch
                    {
                        AddChatLine("NickServ: identify attempt failed.", _currentServerPseudo);
                    }
                }
            }
            catch (Exception ex)
            {
                AddChatLine($"Connection failed: {ex.Message}", _currentServerPseudo);
                SetStatus($"Connection failed: {ex.Message}", false);
                _awaitingUserMode = false;
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
                await ConnectToAsync(profile);
                return;
            }

            var win = new DirectConnectionWindow { Owner = this };
            if (win.ShowDialog() == true && win.ResultProfile != null)
            {
                await ConnectToAsync(win.ResultProfile);
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

                _awaitingUserMode = false;
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
            AddChatLine($"[{targetChannel}] <{_currentNick}> {text}", targetChannel);
            MessageTextBox.Clear();
        }

        private void ChannelsListBox_MouseDoubleClick(object? sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ChannelsListBox.SelectedItem is string channel)
            {

                SwitchToChannel(channel);
                _users.Clear();

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


        private void RouteIncomingLine(string line)
        {

            if (line.StartsWith("<- "))
            {
                var raw = line.Substring(3);

                var rest = raw;
                string prefix = string.Empty;
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
                    var serverKey = _currentServerPseudo ?? prefix;
                    AddChatLine(raw, serverKey);

                  
                    if (_awaitingUserMode && (numeric == 1 || numeric == 4 || numeric == 5))
                    {
                        FinishHandshake($"numeric {numeric}");
                    }

                    return;
                }


                if (_awaitingUserMode && string.Equals(command, "MODE", StringComparison.OrdinalIgnoreCase))
                {
                    var tokens = parameters.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length > 0)
                    {
                        var target = tokens[0];
                        if (!string.IsNullOrEmpty(_currentNick) && string.Equals(target, _currentNick, StringComparison.OrdinalIgnoreCase))
                        {
                            AddChatLine(raw, _currentServerPseudo);
                            FinishHandshake("MODE");
                            return;
                        }
                    }
                }

                AddChatLine(raw, _currentServerPseudo);
                return;
            }

            if (line.StartsWith("-> "))
            {
                var rawOut = line.Substring(3);
                AddChatLine(rawOut);
                return;
            }

            var m = Regex.Match(line, @"^\[(?<target>[^\]]+)\]\s*(?<rest>.*)$");
            if (m.Success)
            {
                var target = m.Groups["target"].Value;
                AddChatLine(line, target);
                return;
            }

            AddChatLine(line);
        }


        private void FinishHandshake(string? reason = null)
        {
            if (!_awaitingUserMode) return;
            _awaitingUserMode = false;

 
            if (!string.IsNullOrEmpty(reason))
                AddChatLine($"Server handshake finished ({reason}).", _currentServerPseudo);

            SetStatus("Connected.", false);
        }

        private void EnsureMessageStore(string channel)
        {
            if (string.IsNullOrEmpty(channel)) return;
            if (!_messageStore.ContainsKey(channel))
                _messageStore[channel] = new List<string>();
        }

        private void EnsureUserStore(string channel)
        {
            if (string.IsNullOrEmpty(channel)) return;
            if (!_userStore.ContainsKey(channel))
                _userStore[channel] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private void AddChatLine(string line, string? channel = null)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var entry = $"[{timestamp}] {line}";

            string target = channel ?? ChannelsListBox.SelectedItem as string ?? ChannelTitle.Text;
            if (string.IsNullOrEmpty(target))
                target = _currentServerPseudo ?? "System";

            EnsureMessageStore(target);
            _messageStore[target].Add(entry);

            var shownChannel = ChannelTitle.Text;
            if (string.Equals(shownChannel, target, StringComparison.OrdinalIgnoreCase) ||
                (string.IsNullOrEmpty(shownChannel) && string.Equals(_currentServerPseudo, target, StringComparison.OrdinalIgnoreCase)))
            {
                _chatLines.Add(entry);
                if (ChatListBox.Items.Count > 0)
                    ChatListBox.ScrollIntoView(ChatListBox.Items[ChatListBox.Items.Count - 1]);
            }

            try
            {
                if (_settings?.AutoSaveChat == true)
                {
                    var path = string.IsNullOrWhiteSpace(_settings.AutoSaveFile) ? Path.Combine(AppContext.BaseDirectory, "chat.txt") : _settings.AutoSaveFile;

                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    File.AppendAllText(path, entry + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to auto-save chat line: {ex.Message}");
            }
        }


        private void SwitchToChannel(string channel)
        {
            if (string.IsNullOrEmpty(channel)) return;

            ChannelTitle.Text = channel;

            if (ChannelsListBox.Items.Contains(channel))
                ChannelsListBox.SelectedItem = channel;

            _users.Clear();
            _chatLines.Clear();

            EnsureMessageStore(channel);
            foreach (var msg in _messageStore[channel])
                _chatLines.Add(msg);


            EnsureUserStore(channel);
            var set = _userStore[channel];
            foreach (var n in set.OrderBy(x => x))
                _users.Add(n);

            if (ChatListBox.Items.Count > 0)
                ChatListBox.ScrollIntoView(ChatListBox.Items[ChatListBox.Items.Count - 1]);
        }

        private void ChannelsListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ChannelsListBox.SelectedItem is string channel)
            {
                SwitchToChannel(channel);
            }
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
            var win = new AboutWindow { Owner = this };
            win.ShowDialog();
        }

        private void PreferencesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var win = new PreferencesWindow { Owner = this };
            win.ShowDialog();

            _settings = SettingsStore.LoadSettings() ?? new Settings();
            if (string.IsNullOrWhiteSpace(_settings.AutoSaveFile))
            {
                _settings.AutoSaveFile = Path.Combine(AppContext.BaseDirectory, "chat.txt");
            }
        }

        private void ExportChatMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_chatLines.Count == 0)
            {
                MessageBox.Show(this, "There are no chat lines to export.", "Export Chat", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title = "Export Chat",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = "chat.txt",
                DefaultExt = "txt"
            };

            if (dlg.ShowDialog(this) != true) return;

            try
            {
                File.WriteAllLines(dlg.FileName, _chatLines);
                MessageBox.Show(this, $"Chat exported to {dlg.FileName}.", "Export Chat", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to export chat: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearChatMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_chatLines.Count == 0) return;

            var res = MessageBox.Show(this, "Clear chat history?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;


            _chatLines.Clear();
        }

        private void AddProfileMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var win = new AddProfileWindow { Owner = this };
            if (win.ShowDialog() == true && win.CreatedProfile != null)
            {
                _profiles.Add(win.CreatedProfile);
                ProfileStore.SaveProfiles(_profiles);
                MessageBox.Show(this, $"Profile '{win.CreatedProfile.Name}' added.", "Profiles", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void EditProfileMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ProfileComboBox.SelectedItem is not Profile selected) return;

            var win = new EditProfileWindow(selected) { Owner = this };
            if (win.ShowDialog() == true && win.EditedProfile != null)
            {
                var idx = _profiles.IndexOf(selected);
                if (idx >= 0)
                {

                    _profiles[idx] = win.EditedProfile;
                    ProfileStore.SaveProfiles(_profiles);

                    ProfileComboBox.SelectedItem = win.EditedProfile;

                    MessageBox.Show(this, $"Profile '{win.EditedProfile.Name}' updated.", "Profiles", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void DeleteProfileMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ProfileComboBox.SelectedItem is not Profile selected) return;

            var res = MessageBox.Show(this, $"Delete profile '{selected.Name}'?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;

            _profiles.Remove(selected);
            ProfileStore.SaveProfiles(_profiles);
        }

        private void DisconnectMenuItem_Click(object sender, RoutedEventArgs e)
        {
            DisconnectAsync();
        }


        private void UpdateUsersTitle()
        {
            UsersTitle.Text = $"Users ({_users.Count})";
        }
    }
}