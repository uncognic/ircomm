using System;
using System.Collections.Generic;
using System.Windows;
using ircomm.Services;

namespace ircomm
{
    public partial class EditProfileWindow : Window
    {
        public Profile? EditedProfile { get; private set; }
        private readonly Profile? _existingProfile;

        public EditProfileWindow()
        {
            InitializeComponent();
        }

        public EditProfileWindow(Profile? existing) : this()
        {
            if (existing is null) return;

            _existingProfile = existing;

            Title = "Edit Profile";

            NameTextBox.Text = existing.Name ?? string.Empty;
            ServerTextBox.Text = existing.Server ?? string.Empty;
            PortTextBox.Text = existing.Port.ToString();
            UsernameTextBox.Text = existing.Username ?? string.Empty;
            PasswordBox.Password = existing.Password ?? string.Empty;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var name = NameTextBox.Text?.Trim();
            var server = ServerTextBox.Text?.Trim();
            var portText = PortTextBox.Text?.Trim();
            var username = UsernameTextBox.Text?.Trim();
            var password = PasswordBox.Password?.Trim();

            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show(this, "Please enter a profile name.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (string.IsNullOrEmpty(server))
            {
                MessageBox.Show(this, "Please enter a server.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (!int.TryParse(portText, out var port) || port <= 0)
            {
                MessageBox.Show(this, "Invalid port.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (string.IsNullOrEmpty(username))
            {
                MessageBox.Show(this, "Please enter a username.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

    
            EditedProfile = new Profile
            {
                Name = name,
                Server = server,
                Port = port,
                Username = username,
                Password = password,
                Channels = _existingProfile != null && _existingProfile.Channels != null ? new List<string>(_existingProfile.Channels) : new List<string>()
            };

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}