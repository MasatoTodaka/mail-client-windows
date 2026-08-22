using MailClient.Core.Abstractions;
using Microsoft.UI.Dispatching;

namespace MailClient.Platform;

public sealed class DispatcherQueueUiDispatcher(DispatcherQueue dispatcherQueue) : IUiDispatcher
{
    public void Post(Action action)
    {
        if (!dispatcherQueue.TryEnqueue(() => action()))
            action(); // dispatcher not accepting new work (e.g. shutting down) — run inline as a fallback
    }
}
