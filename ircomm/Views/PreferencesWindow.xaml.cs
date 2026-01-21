using System.Windows;

namespace ircomm
{
    public partial class PreferencesWindow : Window
    {
        public PreferencesWindow()
        {
            InitializeComponent();
        }
        
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}