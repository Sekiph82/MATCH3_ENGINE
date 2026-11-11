// ENG-M15-015 | Purchase panel complete state matrix
using System;

namespace Match3.Engine.Runtime.Commerce
{
    public enum PurchasePanelPhase
    {
        Idle,
        Purchasing,
        PendingApproval,
        AskToBuy,
        AlreadyOwned,
        Completed,
        Canceled,
        Refunded,
        ValidationFailed,
        Restoring,
        Restored
    }

    public readonly struct PurchasePanelResult
    {
        public PurchasePanelPhase Phase { get; }
        public string Reason { get; }

        public PurchasePanelResult(PurchasePanelPhase phase, string reason)
        {
            if (string.IsNullOrEmpty(reason))
                throw new ArgumentException("Reason must not be null or empty.", nameof(reason));
            Phase = phase;
            Reason = reason;
        }

        // Terminal states: panel can close
        public bool CanClose => Phase == PurchasePanelPhase.Completed
            || Phase == PurchasePanelPhase.Canceled
            || Phase == PurchasePanelPhase.AlreadyOwned
            || Phase == PurchasePanelPhase.Refunded
            || Phase == PurchasePanelPhase.ValidationFailed
            || Phase == PurchasePanelPhase.Restored;

        // Only grant reward for genuine successful outcomes
        public bool ShouldGrantReward => Phase == PurchasePanelPhase.Completed
            || Phase == PurchasePanelPhase.Restored;

        public static PurchasePanelResult Success(string reason)
            => new PurchasePanelResult(PurchasePanelPhase.Completed, reason);

        public static PurchasePanelResult Canceled(string reason)
            => new PurchasePanelResult(PurchasePanelPhase.Canceled, reason);

        public static PurchasePanelResult AlreadyOwned(string reason)
            => new PurchasePanelResult(PurchasePanelPhase.AlreadyOwned, reason);

        public static PurchasePanelResult Refunded(string reason)
            => new PurchasePanelResult(PurchasePanelPhase.Refunded, reason);

        public static PurchasePanelResult ValidationFailed(string reason)
            => new PurchasePanelResult(PurchasePanelPhase.ValidationFailed, reason);

        public static PurchasePanelResult PendingApproval(string reason)
            => new PurchasePanelResult(PurchasePanelPhase.PendingApproval, reason);

        public static PurchasePanelResult AskToBuy(string reason)
            => new PurchasePanelResult(PurchasePanelPhase.AskToBuy, reason);

        public static PurchasePanelResult Restored(string reason)
            => new PurchasePanelResult(PurchasePanelPhase.Restored, reason);
    }

    public sealed class PurchasePanelStateMachine
    {
        public PurchasePanelPhase CurrentPhase { get; private set; } = PurchasePanelPhase.Idle;

        // In-flight: waiting for a store response
        public bool IsPurchaseInFlight => CurrentPhase == PurchasePanelPhase.Purchasing
            || CurrentPhase == PurchasePanelPhase.PendingApproval
            || CurrentPhase == PurchasePanelPhase.AskToBuy
            || CurrentPhase == PurchasePanelPhase.Restoring;

        public void StartPurchase()
        {
            if (IsPurchaseInFlight)
                throw new InvalidOperationException("Purchase already in flight");
            CurrentPhase = PurchasePanelPhase.Purchasing;
        }

        public void TransitionTo(PurchasePanelPhase phase)
        {
            // Prevent duplicate grant: once terminal-success state reached, block re-entry
            if ((CurrentPhase == PurchasePanelPhase.Completed || CurrentPhase == PurchasePanelPhase.Restored)
                && phase == PurchasePanelPhase.Purchasing)
                throw new InvalidOperationException("Cannot re-enter purchase flow after completion");
            CurrentPhase = phase;
        }

        public void Reset() => CurrentPhase = PurchasePanelPhase.Idle;

        public PurchasePanelResult GetResult()
        {
            switch (CurrentPhase)
            {
                case PurchasePanelPhase.Completed:
                    return PurchasePanelResult.Success("Purchase completed successfully.");
                case PurchasePanelPhase.Canceled:
                    return PurchasePanelResult.Canceled("Purchase was canceled.");
                case PurchasePanelPhase.AlreadyOwned:
                    return PurchasePanelResult.AlreadyOwned("Product already owned.");
                case PurchasePanelPhase.Refunded:
                    return PurchasePanelResult.Refunded("Purchase was refunded.");
                case PurchasePanelPhase.ValidationFailed:
                    return PurchasePanelResult.ValidationFailed("Receipt validation failed.");
                case PurchasePanelPhase.Restored:
                    return PurchasePanelResult.Restored("Purchase restored.");
                default:
                    throw new InvalidOperationException(
                        "No result available; purchase not in terminal state");
            }
        }
    }
}
