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
            try
            {
                Console.WriteLine("MainWindowViewModel constructor started");

                if (db == null)
                {
                    Console.WriteLine("ERROR: PowerSyncDatabase is null!");
                    throw new ArgumentNullException(nameof(db));
                }

                _db = db;
                Console.WriteLine("PowerSyncDatabase assigned successfully");

                // Set up the listener to track the status changes
                Console.WriteLine("Setting up RunListener...");
                _db.RunListener(
                    (update) =>
                    {
                        Console.WriteLine(
                            $"Listener callback triggered: StatusChanged = {update.StatusChanged?.Connected}"
                        );
                        if (update.StatusChanged != null)
                        {
                            Connected = update.StatusChanged.Connected;
                        }
                    }
                );
                Console.WriteLine("RunListener setup complete");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in MainWindowViewModel constructor: {ex}");
                throw;
            }
        }
        #endregion
    }
}
