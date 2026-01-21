using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using ircomm.Services;

namespace ircomm
{
    public partial class PreferencesWindow : Window
    {
        private Settings _settings = new();

        public PreferencesWindow()
        {
            InitializeComponent();
            LoadSettingsToUi();
        }

        private void LoadSettingsToUi()
        {
            _settings = SettingsStore.LoadSettings() ?? new Settings();
            AutoSaveCheckBox.IsChecked = _settings.AutoSaveChat;

            var defaultPath = Path.Combine(AppContext.BaseDirectory, "chat.txt");
            AutoSavePathTextBox.Text = string.IsNullOrWhiteSpace(_settings.AutoSaveFile) ? defaultPath : _settings.AutoSaveFile;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            _settings.AutoSaveChat = AutoSaveCheckBox.IsChecked == true;
            _settings.AutoSaveFile = string.IsNullOrWhiteSpace(AutoSavePathTextBox.Text) ? null : AutoSavePathTextBox.Text.Trim();

            try
            {
                if (!string.IsNullOrWhiteSpace(_settings.AutoSaveFile))
                {
                    var dir = Path.GetDirectoryName(_settings.AutoSaveFile);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                }
            }
            catch
            {
                
            }

            SettingsStore.SaveSettings(_settings);

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Select chat file",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                DefaultExt = "txt",
                FileName = "chat.txt"
            };

            if (dlg.ShowDialog(this) == true)
            {
                AutoSavePathTextBox.Text = dlg.FileName;
            }
        }
    }
}