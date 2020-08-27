using MahApps.Metro.Controls;
using System.Windows.Input;

namespace CleanSlate
{
    /// <summary>
    /// Interaction logic for SubWindow.xaml
    /// </summary>
    public partial class SubWindow : MetroWindow
    {
        public SubWindow()
        {
            InitializeComponent();
        }

        // closes the current window
        private void Btncool_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            this.Close();
        }

        // spins up and redirects via web browser
        private void Hyperlink_RequestNavigate(object sender,
                                       System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(e.Uri.AbsoluteUri);
        }

        // allows user to drag the app by click + holding anywhere on the window
        private void Subwindow_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }
    }
}
