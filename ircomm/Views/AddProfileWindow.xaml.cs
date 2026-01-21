using System;
using System.Windows;
using ircomm.Services;

namespace ircomm
{
    public partial class AddProfileWindow : Window
    {
        public Profile? CreatedProfile { get; private set; }

        public AddProfileWindow()
        {
            InitializeComponent();
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

            CreatedProfile = new Profile
            {
                Name = name,
                Server = server,
                Port = port,
                Username = username,
                Password = password
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