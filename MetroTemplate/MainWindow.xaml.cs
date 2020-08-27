using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using Microsoft.Win32;
using Syncfusion.UI.Xaml.Spreadsheet.Helpers;
using Syncfusion.XlsIO.Implementation;
using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CleanSlate
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        // init the theme manager so the app can be customized!
        ThemeManagerLite themeManager = new ThemeManagerLite();
        public MainWindowViewModel context = new MainWindowViewModel();

        public MainWindow(string[] args)
        {
            InitializeComponent();

            // set handlers
            // load saved user settings
            themeManager.ChangeTheme(Properties.Settings.Default.Theme);

            this.Height = Properties.Settings.Default.MainWindowHeight;
            this.Width = Properties.Settings.Default.MainWindowWidth;

            txtLocalConnectionString.Text = Properties.Settings.Default.LocalConnectionString;
            txtServerConnectionString.Text = Properties.Settings.Default.ServerConnectionString;
        }


        private async void btnInit_Click(object sender, RoutedEventArgs e)
        {
            Loading(true);
            await Synchronizer.Init(txtLocalConnectionString.Text, txtServerConnectionString.Text);
            Loading(false);

            Properties.Settings.Default.LocalConnectionString = txtLocalConnectionString.Text;
            Properties.Settings.Default.ServerConnectionString = txtServerConnectionString.Text;
            Properties.Settings.Default.Save();

            Refresh();
        }

        private async void btnSync_Click(object sender, RoutedEventArgs e)
        {
            Loading(true);
            await Synchronizer.SyncAsync();
            Loading(false);

            Refresh();
        }


        private async void btnDesync_Click(object sender, RoutedEventArgs e)
        {
            Loading(true);
            await Synchronizer.DesyncAsync();
            Loading(false);

            Refresh();
        }

        private void btnRefresh_Click(object sender, RoutedEventArgs e)
        {
            Refresh();
        }


        private void txtSearchServer_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                listServerTables.ItemsSource = context.ServerTables.Where(x => x.name.Contains(txtSearchServer.Text));
            }
            catch (Exception ex) { }
        }

        private void txtSearchLocal_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                listLocalTables.ItemsSource = context.LocalTables.Where(x => x.name.Contains(txtSearchLocal.Text));
            }
            catch (Exception ex) { }
        }

        private async void listServerTables_SelectionChanged(object sender, EventArgs e)
        {
            if (listServerTables.SelectedItem != null)
            {
                var table = (SyncTable)listServerTables.SelectedItem;

                await Task.Run(() =>
                {
                    try
                    {
                        string sql = $"SELECT * FROM {table.name}";
                        var data = Synchronizer.GetData(sql, Synchronizer.serverConnectionString);
                        dgTableData.Invoke(() => { dgTableData.ItemsSource = data.AsDataView(); });
                    }
                    catch (Exception ex)
                    {

                    }
                });
            }
        }

        private async void listLocalTables_SelectionChanged(object sender, EventArgs e)
        {
            if (listLocalTables.SelectedItem != null)
            {
                var table = (SyncTable)listLocalTables.SelectedItem;

                await Task.Run(() =>
                {
                    try
                    {
                        string sql = $"SELECT * FROM {table.name}";
                        var data = Synchronizer.GetData(sql, Synchronizer.clientConnectionString);
                        dgTableData.Invoke(() => { dgTableData.ItemsSource = data.AsDataView(); });
                    }
                    catch (Exception ex)
                    {

                    }
                });
            }
        }

        private async void btnImport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var table = (SyncTable)listServerTables.SelectedItem;
                string sql = $"SELECT * FROM {table.name}";
                var data = Synchronizer.GetData(sql, Synchronizer.serverConnectionString);

                context.LocalTables.Add(table);
            }
            catch (Exception ex)
            {

            }

            Loading(true);
            await Synchronizer.SyncAsync();
            Loading(false);

            Refresh();
        }


        private async void btnRemove_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var table = (SyncTable)listLocalTables.SelectedItem;
                string sql = $"SELECT * FROM {table.name}";
                var data = Synchronizer.GetData(sql, Synchronizer.clientConnectionString);


                Synchronizer.ClearSingleTableData(table.name);
                context.LocalTables.Remove(table);
            }
            catch (Exception ex)
            {

            }

            Loading(true);
            await Synchronizer.SyncAsync();
            Loading(false);

            Refresh();
        }

        private void Refresh()
        {
            listLocalTables.ItemsSource = context.LocalTables;
            listServerTables.ItemsSource = context.ServerTables;
        }

        // sets the theme on click from the theme radio button list
        private void SetTheme_Click(object sender, RoutedEventArgs e)
        {
            RadioButton radioButton = sender as RadioButton;
            string theme = radioButton.Content.ToString();
            themeManager.ChangeTheme(theme);
        }

        // various use of the Metro popup
        private async void Message_ClickAsync(object sender, RoutedEventArgs e)
        {
            MessageDialogResult res = await this.ShowMessageAsync("Hey, Listen!", "These are just some controls to take up space :)",
                MessageDialogStyle.AffirmativeAndNegative);

            if (res == MessageDialogResult.Affirmative)
            {
                await this.ShowMessageAsync("You clicked OK! ", res.ToString());
            }
        }

        // launches the SubWindow form via dialog
        private void menuinformation_Click(object sender, RoutedEventArgs e)
        {
            SubWindow subWindow = new SubWindow();
            subWindow.ShowDialog();
        }

        // this allows the user to drag the app by clicking and holding anywhere on the window!
        private void Mainwindow_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }

        // persist user resize because its nice to have and easy to do
        private async void Mainwindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            await Task.Run(() =>
            {
                try
                {
                    this.Invoke(() => { Properties.Settings.Default.MainWindowHeight = (int)Height; });
                    this.Invoke(() => { Properties.Settings.Default.MainWindowWidth = (int)Width; });
                    Properties.Settings.Default.Save();
                }
                catch (Exception ex)
                {
                    ShowMessage("Hey, listen!", ex.Message);
                }
            });
        }

        private async void menusave_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(() =>
            {
                try
                {

                }
                catch (Exception ex)
                {
                    ShowMessage("Hey, listen!", ex.Message);
                }
                Loading(false);
            });
        }

        private async void menuopen_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(() =>
            {
                Loading(true);

                try
                {

                }
                catch (Exception ex)
                {
                    ShowMessage("Hey, listen!", ex.Message);
                }
                Loading(false);
            });
        }

        private async void menusaveas_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(() =>
            {
                try
                {

                }
                catch (Exception ex)
                {
                    ShowMessage("Hey, listen!", ex.Message);
                }
                Loading(false);
            });
        }

        private async void menunew_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(() =>
            {
                Loading(true);
                try
                {

                }
                catch (Exception ex)
                {
                    ShowMessage("Hey, listen!", ex.Message);
                }
                Loading(false);
            });
        }

        private async void ShowMessage(string title, string message)
        {
            await this.Invoke(async () => { await this.ShowMessageAsync(title, message); });
        }

        private void Loading(bool load)
        {
            loader.Invoke(() => { loader.IsIndeterminate = load; });
        }
	}
}
