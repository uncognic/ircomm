using System;
using System.Windows;
using ircomm.Services;

namespace ircomm
{
    public partial class EditProfileWindow : Window
    {
        public Profile? EditedProfile { get; private set; }

        public EditProfileWindow()
        {
            InitializeComponent();
        }

        public EditProfileWindow(Profile? existing) : this()
        {
            if (existing is null) return;

            Title = "Edit Profile";

            NameTextBox.Text = existing.Name ?? string.Empty;
            ServerTextBox.Text = existing.Server ?? string.Empty;
            PortTextBox.Text = existing.Port.ToString();
            UsernameTextBox.Text = existing.Username ?? string.Empty;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var name = NameTextBox.Text?.Trim();
            var server = ServerTextBox.Text?.Trim();
            var portText = PortTextBox.Text?.Trim();
            var username = UsernameTextBox.Text?.Trim();

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
                Username = username
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