using System.ComponentModel;
using System.Runtime.CompilerServices;

using PowerSync.Common.Client;

namespace PowersyncDotnetTodoList.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        #region Fields
        private readonly PowerSyncDatabase _db;
        private bool _connected = false;
        #endregion

        #region Properties
        public bool Connected
        {
            get => _connected;
            set
            {
                if (_connected != value)
                {
                    _connected = value;
                    OnPropertyChanged();
                }
            }
        }
        #endregion

        #region Constructor
        public MainWindowViewModel(PowerSyncDatabase db)
        {
            _db = db;
            // Set up the listener to track the status changes
            _db.RunListener(
                (update) =>
                {
                    if (update.StatusChanged != null)
                    {
                        Connected = update.StatusChanged.Connected;
                    }
                }
            );
        }
        #endregion
    }
}
