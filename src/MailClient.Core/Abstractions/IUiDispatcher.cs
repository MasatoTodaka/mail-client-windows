namespace MailClient.Core.Abstractions;

// Marshals work onto the UI thread. ViewModels must route any mutation of UI-bound state
// (ObservableCollections, observable properties read by bindings) through this when the
// triggering event may arrive on a background thread — e.g. MailSyncService.MessageArrived,
// which fires from the IMAP IDLE watcher's thread. WinUI throws COMException 0x8001010E
// (wrong thread) otherwise, which as an unhandled exception kills the whole process.
public interface IUiDispatcher
{
    void Post(Action action);
}
