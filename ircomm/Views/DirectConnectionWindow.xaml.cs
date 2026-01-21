using System;
using System.Windows;
using ircomm.Services;

namespace ircomm
{
    public partial class DirectConnectionWindow : Window
    {
        public Profile? ResultProfile { get; private set; }

        public DirectConnectionWindow()
        {
            InitializeComponent();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var server = ServerTextBox.Text?.Trim();
            var portText = PortTextBox.Text?.Trim();
            var username = UsernameTextBox.Text?.Trim();

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

            ResultProfile = new Profile
            {
                Server = server,
                Port = port,
                Username = username,
                Name = $"Direct: {server}:{port}"
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