using System.Reflection;
using MailClient.Core.Models;
using MailClient.ViewModels.Common;
using MailClient.ViewModels.Shell;
using Xunit;

namespace MailClient.ViewModels.Tests;

// ApplyMessages is the diffing algorithm that keeps MessageListViewModel.Messages in sync with a
// freshly-loaded page without a full Clear()+Add() (which visibly "jumps" the bound ListView — see
// the project memory for the full incident history). It's private, and the repo has no
// InternalsVisibleTo, so these tests invoke it via reflection rather than widening its visibility
// just to make it testable — that would be a real (if small) behavior-surface change, and the
// point of this suite is to pin down the existing behavior, not alter it.
//
// Each test here corresponds to a real regression this diffing logic went through in production:
// a no-op refresh silently replacing every row (visible "jump" on every click), IsBodyDownloaded
// leaking into the replace decision (selection highlight and sender logo resetting the first time
// any message was opened), and a freshly-cached logo never reaching the UI because nothing about
// the row's own fields had changed (forceReplaceAddresses).
public sealed class MessageListViewModelApplyMessagesTests
{
    [Fact]
    public void UnchangedMessage_IsNotReplaced()
    {
        var vm = CreateViewModel();
        var original = CreateMessage();
        InvokeApplyMessages(vm, [original]);

        // A second page load returns a brand new MailMessage instance (as a real DB re-query
        // always does), but with every field identical to the one already shown.
        var refetched = CreateMessage(id: original.Id);
        InvokeApplyMessages(vm, [refetched]);

        Assert.Same(original, vm.Messages[0]);
    }

    [Fact]
    public void ChangedSubject_ReplacesOnlyThatRow()
    {
        var vm = CreateViewModel();
        var messageA = CreateMessage(fromDisplay: "A");
        var messageB = CreateMessage(fromDisplay: "B");
        InvokeApplyMessages(vm, [messageA, messageB]);

        var updatedA = CreateMessage(id: messageA.Id, fromDisplay: "A", subject: "New subject");
        var refetchedB = CreateMessage(id: messageB.Id, fromDisplay: "B");
        InvokeApplyMessages(vm, [updatedA, refetchedB]);

        Assert.Same(updatedA, vm.Messages[0]);
        Assert.Same(messageB, vm.Messages[1]); // untouched row keeps its original reference
    }

    [Fact]
    public void IsBodyDownloadedChangingAlone_DoesNotReplaceTheRow()
    {
        // Regression: ReadingPaneViewModel.FetchAndCacheBodyAsync flips IsBodyDownloaded to true
        // in place, before MarkAsReadAsync runs, on the very first open of any message. That field
        // isn't shown anywhere in the row template, so a refresh must not treat it as a "real"
        // change — if it does, opening any not-yet-read message replaces its row and (before this
        // was fixed) visibly reset the selection highlight and the sender logo.
        var vm = CreateViewModel();
        var original = CreateMessage(isBodyDownloaded: false);
        InvokeApplyMessages(vm, [original]);

        var afterBodyFetch = CreateMessage(id: original.Id, isBodyDownloaded: true);
        InvokeApplyMessages(vm, [afterBodyFetch]);

        Assert.Same(original, vm.Messages[0]);
    }

    [Fact]
    public void IsReadAndIsFlaggedChanging_PropagatesInPlaceWithoutReplacing()
    {
        var vm = CreateViewModel();
        var original = CreateMessage(isRead: false, isFlagged: false);
        InvokeApplyMessages(vm, [original]);

        var afterMarkedRead = CreateMessage(id: original.Id, isRead: true, isFlagged: true);
        InvokeApplyMessages(vm, [afterMarkedRead]);

        Assert.Same(original, vm.Messages[0]);
        Assert.True(original.IsRead);
        Assert.True(original.IsFlagged);
    }

    [Fact]
    public void NewMessageInsertedAtTop_LeavesExistingRowsUntouched()
    {
        var vm = CreateViewModel();
        var messageA = CreateMessage();
        var messageB = CreateMessage();
        InvokeApplyMessages(vm, [messageA, messageB]);

        var newMessage = CreateMessage();
        InvokeApplyMessages(vm, [newMessage, messageA, messageB]);

        Assert.Equal(3, vm.Messages.Count);
        Assert.Same(newMessage, vm.Messages[0]);
        Assert.Same(messageA, vm.Messages[1]);
        Assert.Same(messageB, vm.Messages[2]);
    }

    [Fact]
    public void RemovedMessage_IsDroppedWithoutDisturbingTheRest()
    {
        var vm = CreateViewModel();
        var messageA = CreateMessage();
        var messageB = CreateMessage();
        var messageC = CreateMessage();
        InvokeApplyMessages(vm, [messageA, messageB, messageC]);

        InvokeApplyMessages(vm, [messageA, messageC]);

        Assert.Equal(2, vm.Messages.Count);
        Assert.Same(messageA, vm.Messages[0]);
        Assert.Same(messageC, vm.Messages[1]);
    }

    [Fact]
    public void ReorderedMessages_MoveRatherThanReplace()
    {
        var vm = CreateViewModel();
        var messageA = CreateMessage();
        var messageB = CreateMessage();
        var messageC = CreateMessage();
        InvokeApplyMessages(vm, [messageA, messageB, messageC]);

        // Same three messages, later re-fetched in a different order (e.g. a date correction
        // changed the sort position) — every object reference should be preserved, just moved.
        InvokeApplyMessages(vm, [messageB, messageA, messageC]);

        Assert.Same(messageB, vm.Messages[0]);
        Assert.Same(messageA, vm.Messages[1]);
        Assert.Same(messageC, vm.Messages[2]);
    }

    [Fact]
    public void ForceReplaceAddresses_ReplacesEvenWhenNothingElseChanged()
    {
        // Regression: PrefetchLogosAsync re-applies the page once a logo is newly cached so it
        // shows up immediately, but the row's own fields never change just because a file appeared
        // on disk — forceReplaceAddresses is what makes that row's OneTime-bound logo image
        // actually refresh instead of silently never picking up the new file.
        var vm = CreateViewModel();
        var original = CreateMessage(fromAddress: "sender@example.com");
        InvokeApplyMessages(vm, [original]);

        var refetched = CreateMessage(id: original.Id, fromAddress: "sender@example.com");
        InvokeApplyMessages(vm, [refetched], forceReplaceAddresses: new HashSet<string> { "sender@example.com" });

        Assert.Same(refetched, vm.Messages[0]);
    }

    [Fact]
    public void ForceReplaceAddresses_LeavesNonMatchingRowsAlone()
    {
        var vm = CreateViewModel();
        var messageA = CreateMessage(fromAddress: "a@example.com");
        var messageB = CreateMessage(fromAddress: "b@example.com");
        InvokeApplyMessages(vm, [messageA, messageB]);

        var refetchedA = CreateMessage(id: messageA.Id, fromAddress: "a@example.com");
        var refetchedB = CreateMessage(id: messageB.Id, fromAddress: "b@example.com");
        InvokeApplyMessages(vm, [refetchedA, refetchedB], forceReplaceAddresses: new HashSet<string> { "a@example.com" });

        Assert.Same(refetchedA, vm.Messages[0]);
        Assert.Same(messageB, vm.Messages[1]);
    }

    private static MessageListViewModel CreateViewModel()
    {
        var messageActions = new MessageActionService(
            new FakeMessageStore(),
            new FakeFolderStore(),
            new FakeOutboxStore(),
            new FakeOutboxProcessor(),
            new FakeMailSyncService());

        return new MessageListViewModel(
            new FakeMessageStore(),
            new FakeFolderStore(),
            new FakeMailSyncService(),
            new FakeUiDispatcher(),
            messageActions,
            new FakeSenderLogoService());
    }

    private static void InvokeApplyMessages(
        MessageListViewModel viewModel,
        IReadOnlyList<MailMessage> messages,
        IReadOnlySet<string>? forceReplaceAddresses = null)
    {
        var method = typeof(MessageListViewModel).GetMethod("ApplyMessages", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("ApplyMessages not found — has it been renamed?");
        method.Invoke(viewModel, [messages, forceReplaceAddresses]);
    }

    // A fixed AccountId/FolderId/Date shared by every message this helper creates by default, and
    // a Uid deterministically derived from Id rather than an incrementing counter: ApplyMessages'
    // change detection (DisplayState) compares all of these fields, so "the same message,
    // re-fetched with no changes" must produce values that are actually identical across two
    // CreateMessage calls given the same id — not merely two different-but-plausible ones (an
    // earlier version of this helper used a global counter for Uid/Date, which made every
    // "unchanged" test fail because the "refetch" always looked changed).
    private static readonly Guid SharedAccountId = Guid.NewGuid();
    private static readonly Guid SharedFolderId = Guid.NewGuid();
    private static readonly DateTimeOffset SharedDate = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static MailMessage CreateMessage(
        Guid? id = null,
        string subject = "Subject",
        string fromDisplay = "Sender",
        string fromAddress = "sender@example.com",
        bool isRead = false,
        bool isFlagged = false,
        bool isBodyDownloaded = false)
    {
        var actualId = id ?? Guid.NewGuid();
        return new MailMessage
        {
            Id = actualId,
            AccountId = SharedAccountId,
            FolderId = SharedFolderId,
            Uid = unchecked((uint)actualId.GetHashCode()),
            Subject = subject,
            FromDisplay = fromDisplay,
            FromAddress = fromAddress,
            ToRecipients = "recipient@example.com",
            Date = SharedDate,
            IsRead = isRead,
            IsFlagged = isFlagged,
            IsBodyDownloaded = isBodyDownloaded,
        };
    }
}
