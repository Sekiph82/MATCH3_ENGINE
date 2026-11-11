using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Match3.Engine.Runtime
{
    // Zero is a non-success sentinel so default(...) never reads as success.
    // Non-success members keep their original numbers; the success member moved.
    public enum ServiceResultStatus
    {
        None = 0,
        Disabled = 1,
        Denied = 2,
        Unavailable = 3,
        Failed = 4,
        NotFound = 5,
        Succeeded = 6
    }

    public sealed class ServiceResult<T>
    {
        private readonly T _value;

        public ServiceResultStatus Status { get; }
        public string ReasonCode { get; }
        public bool IsSuccess => Status == ServiceResultStatus.Succeeded;

        // Fail closed: a non-success result has no value to read.
        public T Value
        {
            get
            {
                if (!IsSuccess)
                    throw new InvalidOperationException(
                        "ServiceResult has no value: status " + Status +
                        " (" + ReasonCode + ").");
                return _value;
            }
        }

        private ServiceResult(ServiceResultStatus status, T value,
            string reasonCode)
        {
            if (status == ServiceResultStatus.None ||
                !Enum.IsDefined(typeof(ServiceResultStatus), status))
                throw new ArgumentOutOfRangeException(nameof(status));
            if (status == ServiceResultStatus.Succeeded &&
                ReferenceEquals(value, null))
                throw new ArgumentNullException(nameof(value));
            // Enum outcomes: the zero None sentinel or an undefined value is
            // never a successful outcome.
            if (status == ServiceResultStatus.Succeeded && value is Enum &&
                (!Enum.IsDefined(typeof(T), value) ||
                 Convert.ToInt64(value) == 0))
                throw new ArgumentOutOfRangeException(nameof(value));
            if (status != ServiceResultStatus.Succeeded &&
                string.IsNullOrWhiteSpace(reasonCode))
                throw new ArgumentException(
                    "A non-success result requires a reason code.",
                    nameof(reasonCode));
            Status = status;
            _value = value;
            ReasonCode = reasonCode;
        }

        public static ServiceResult<T> Success(T value) =>
            new ServiceResult<T>(ServiceResultStatus.Succeeded, value, null);

        public static ServiceResult<T> Rejected(
            ServiceResultStatus status, string reasonCode)
        {
            if (status == ServiceResultStatus.Succeeded)
                throw new ArgumentException(
                    "Use Success for successful results.", nameof(status));
            return new ServiceResult<T>(status, default(T), reasonCode);
        }
    }

    public sealed class GameServiceContext
    {
        public GameProfile Profile { get; }
        public PrivacySnapshot Privacy { get; }

        public GameServiceContext(GameProfile profile,
            PrivacySnapshot privacy)
        {
            Profile = profile ??
                throw new ArgumentNullException(nameof(profile));
            Privacy = privacy ??
                throw new ArgumentNullException(nameof(privacy));
            if (!StringComparer.Ordinal.Equals(profile.Id, privacy.GameId))
                throw new ArgumentException(
                    "Service context profile and privacy state must belong to the same game.",
                    nameof(privacy));
        }
    }

    public enum ConsentPurpose
    {
        Advertising = 0,
        Analytics = 1,
        Attribution = 2,
        CrashReporting = 3,
        CloudIdentity = 4,
        CloudSave = 5,
        Notifications = 6,
        Purchases = 7
    }

    public enum ConsentDecisionStatus
    {
        Unknown = 0,
        Denied = 1,
        Allowed = 2
    }

    public sealed class ConsentDecision
    {
        public ConsentDecisionStatus Status { get; }
        public string ReasonCode { get; }
        public bool IsAllowed => Status == ConsentDecisionStatus.Allowed;

        private ConsentDecision(ConsentDecisionStatus status,
            string reasonCode)
        {
            if (!Enum.IsDefined(typeof(ConsentDecisionStatus), status))
                throw new ArgumentOutOfRangeException(nameof(status));
            if (status != ConsentDecisionStatus.Allowed &&
                string.IsNullOrWhiteSpace(reasonCode))
                throw new ArgumentException(
                    "A non-allow decision requires a reason code.",
                    nameof(reasonCode));
            Status = status;
            ReasonCode = reasonCode;
        }

        public static ConsentDecision Allow() =>
            new ConsentDecision(ConsentDecisionStatus.Allowed, null);
        public static ConsentDecision Deny(string reasonCode) =>
            new ConsentDecision(ConsentDecisionStatus.Denied, reasonCode);
        public static ConsentDecision Unknown(string reasonCode) =>
            new ConsentDecision(ConsentDecisionStatus.Unknown, reasonCode);
    }

    public enum AdOutcome
    {
        None = 0,
        NotReady = 1,
        Skipped = 2,
        Rewarded = 3
    }

    public enum PurchaseOutcome
    {
        None = 0,
        Pending = 1,
        Cancelled = 2,
        Failed = 3,
        Purchased = 4
    }

    public interface IAdsService
    {
        Task<ServiceResult<AdOutcome>> ShowRewardedAsync(
            GameServiceContext context, CancellationToken cancellationToken);
    }

    public interface IIapService
    {
        Task<ServiceResult<PurchaseOutcome>> PurchaseAsync(
            GameServiceContext context, string productId,
            CancellationToken cancellationToken);
    }

    public interface IAnalyticsSink
    {
        ServiceResult<bool> Record(GameServiceContext context,
            string eventCode);
    }

    public interface IAttributionService
    {
        Task<ServiceResult<string>> GetCampaignTokenAsync(
            GameServiceContext context, CancellationToken cancellationToken);
    }

    public interface IConsentGate
    {
        ConsentDecision Evaluate(GameServiceContext context,
            ConsentPurpose purpose);
    }

    public interface IPlayerIdentity
    {
        Task<ServiceResult<string>> GetPlayerIdAsync(
            GameServiceContext context, CancellationToken cancellationToken);
    }

    public interface ICloudSave
    {
        Task<ServiceResult<SaveEnvelope>> ReadAsync(
            GameServiceContext context, string key,
            CancellationToken cancellationToken);
        Task<ServiceResult<bool>> WriteAsync(
            GameServiceContext context, string key, SaveEnvelope envelope,
            CancellationToken cancellationToken);
    }

    public interface IEntitlementService
    {
        Task<ServiceResult<bool>> HasEntitlementAsync(
            GameServiceContext context, string entitlementId,
            CancellationToken cancellationToken);
    }

    public interface IRemoteConfig
    {
        Task<ServiceResult<string>> GetStringAsync(
            GameServiceContext context, string key,
            CancellationToken cancellationToken);
    }

    public interface INotification
    {
        Task<ServiceResult<bool>> ScheduleAsync(
            GameServiceContext context, string notificationId,
            string messageCode, long scheduledAtUnixSeconds,
            CancellationToken cancellationToken);
        Task<ServiceResult<bool>> CancelAsync(
            GameServiceContext context, string notificationId,
            CancellationToken cancellationToken);
    }

    public interface ICrashReporter
    {
        ServiceResult<bool> RecordNonFatal(GameServiceContext context,
            string errorCode);
    }

    public sealed class DisabledCommercialServices : IAdsService,
        IIapService, IAnalyticsSink, IAttributionService, IConsentGate,
        IPlayerIdentity, ICloudSave, IEntitlementService, IRemoteConfig,
        INotification, ICrashReporter
    {
        public Task<ServiceResult<AdOutcome>> ShowRewardedAsync(
            GameServiceContext context, CancellationToken cancellationToken)
        {
            RequireContext(context);
            return Disabled<AdOutcome>(cancellationToken);
        }

        public Task<ServiceResult<PurchaseOutcome>> PurchaseAsync(
            GameServiceContext context, string productId,
            CancellationToken cancellationToken)
        {
            RequireContext(context);
            RequireCode(productId, nameof(productId));
            return Disabled<PurchaseOutcome>(cancellationToken);
        }

        public ServiceResult<bool> Record(GameServiceContext context,
            string eventCode)
        {
            RequireContext(context);
            RequireCode(eventCode, nameof(eventCode));
            return Rejected<bool>();
        }

        public Task<ServiceResult<string>> GetCampaignTokenAsync(
            GameServiceContext context, CancellationToken cancellationToken)
        {
            RequireContext(context);
            return Disabled<string>(cancellationToken);
        }

        public ConsentDecision Evaluate(GameServiceContext context,
            ConsentPurpose purpose)
        {
            RequireContext(context);
            if (!Enum.IsDefined(typeof(ConsentPurpose), purpose))
                throw new ArgumentOutOfRangeException(nameof(purpose));
            return ConsentDecision.Unknown("consent.not_configured");
        }

        public Task<ServiceResult<string>> GetPlayerIdAsync(
            GameServiceContext context, CancellationToken cancellationToken)
        {
            RequireContext(context);
            return Disabled<string>(cancellationToken);
        }

        public Task<ServiceResult<SaveEnvelope>> ReadAsync(
            GameServiceContext context, string key,
            CancellationToken cancellationToken)
        {
            RequireContext(context);
            RequireCode(key, nameof(key));
            return Disabled<SaveEnvelope>(cancellationToken);
        }

        public Task<ServiceResult<bool>> WriteAsync(
            GameServiceContext context, string key, SaveEnvelope envelope,
            CancellationToken cancellationToken)
        {
            RequireContext(context);
            RequireCode(key, nameof(key));
            if (envelope == null)
                throw new ArgumentNullException(nameof(envelope));
            return Disabled<bool>(cancellationToken);
        }

        public Task<ServiceResult<bool>> HasEntitlementAsync(
            GameServiceContext context, string entitlementId,
            CancellationToken cancellationToken)
        {
            RequireContext(context);
            RequireCode(entitlementId, nameof(entitlementId));
            return Disabled<bool>(cancellationToken);
        }

        public Task<ServiceResult<string>> GetStringAsync(
            GameServiceContext context, string key,
            CancellationToken cancellationToken)
        {
            RequireContext(context);
            RequireCode(key, nameof(key));
            return Disabled<string>(cancellationToken);
        }

        public Task<ServiceResult<bool>> ScheduleAsync(
            GameServiceContext context, string notificationId,
            string messageCode, long scheduledAtUnixSeconds,
            CancellationToken cancellationToken)
        {
            RequireContext(context);
            RequireCode(notificationId, nameof(notificationId));
            RequireCode(messageCode, nameof(messageCode));
            if (scheduledAtUnixSeconds < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(scheduledAtUnixSeconds));
            return Disabled<bool>(cancellationToken);
        }

        public Task<ServiceResult<bool>> CancelAsync(
            GameServiceContext context, string notificationId,
            CancellationToken cancellationToken)
        {
            RequireContext(context);
            RequireCode(notificationId, nameof(notificationId));
            return Disabled<bool>(cancellationToken);
        }

        public ServiceResult<bool> RecordNonFatal(
            GameServiceContext context, string errorCode)
        {
            RequireContext(context);
            RequireCode(errorCode, nameof(errorCode));
            return Rejected<bool>();
        }

        private static void RequireContext(GameServiceContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
        }

        private static void RequireCode(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
                throw new ArgumentException(
                    "A non-empty code no longer than 128 characters is required.",
                    parameterName);
        }

        private static ServiceResult<T> Rejected<T>() =>
            ServiceResult<T>.Rejected(
                ServiceResultStatus.Disabled, "service.disabled");

        private static Task<ServiceResult<T>> Disabled<T>(
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<ServiceResult<T>>(
                    cancellationToken);
            return Task.FromResult(Rejected<T>());
        }
    }
}
