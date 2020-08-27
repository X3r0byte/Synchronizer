using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Documents;

namespace CleanSlate
{
    public class MainWindowViewModel : BaseViewModel
    {
        ObservableCollection<SyncTable> serverTables;
        ObservableCollection<SyncTable> localTables;

        public MainWindowViewModel()
        {
            ServerTables = new ObservableCollection<SyncTable>();
            LocalTables = new ObservableCollection<SyncTable>();
        }

        public ObservableCollection<SyncTable> ServerTables
        {
            get { return serverTables; }
            set
            {
                serverTables = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<SyncTable> LocalTables
        {
            get { return localTables; }
            set
            {
                localTables = value;
                OnPropertyChanged();
            }
        }

    }
    public abstract class BaseViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged([CallerMemberName] string caller = null)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(caller));
            }
        }

        private void VerifyPropertyName(string propertyName)
        {
            if (TypeDescriptor.GetProperties(this)[propertyName] == null)
                throw new ArgumentNullException(GetType().Name + " does not contain property: " + propertyName);
        }
    }

    public class SyncTable
	{
        public string name { get; set; }
        public string pkColumn { get; set; }

		public override string ToString()
		{
            return name;
		}
	}
}
