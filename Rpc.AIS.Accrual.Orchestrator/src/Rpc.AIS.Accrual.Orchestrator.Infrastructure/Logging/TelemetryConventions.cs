using System;
using System.Net;
using System.Net.Http;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;

public static class TelemetryConventions
{
    public static class Outcomes
    {
        public const string Success = "Success";
        public const string Failed = "Failed";
        public const string Skipped = "Skipped";
        public const string Partial = "Partial";
        public const string Retryable = "Retryable";
        public const string Accepted = "Accepted";
    }

    public static class FailureCategories
    {
        public const string Validation = "Validation";
        public const string Authentication = "Authentication";
        public const string Authorization = "Authorization";
        public const string Throttle = "Throttle";
        public const string Timeout = "Timeout";
        public const string Network = "Network";
        public const string BusinessRule = "BusinessRule";
        public const string DataQuality = "DataQuality";
        public const string Serialization = "Serialization";
        public const string Unknown = "Unknown";
        public const string Unhandled = "Unhandled";
    }

    public static class Stages
    {
        public const string Inbound = "Inbound";
        public const string Accepted = "Accepted";

        public const string DiscoveryBegin = "Discovery.Begin";
        public const string DiscoveryEnd = "Discovery.End";

        public const string FetchFromFsaBegin = "FetchFromFsa.Begin";
        public const string FetchFromFsaEnd = "FetchFromFsa.End";

        public const string DeltaBuildBegin = "DeltaBuild.Begin";
        public const string DeltaBuildEnd = "DeltaBuild.End";

        public const string ValidateBegin = "Validate.Begin";
        public const string ValidateEnd = "Validate.End";

        public const string CreateBegin = "Create.Begin";
        public const string CreateEnd = "Create.End";

        public const string PostBegin = "Post.Begin";
        public const string PostEnd = "Post.End";

        public const string RetryBegin = "Retry.Begin";
        public const string RetryEnd = "Retry.End";

        public const string InvoiceAttributesBegin = "InvoiceAttributes.Begin";
        public const string InvoiceAttributesEnd = "InvoiceAttributes.End";

        public const string AttachmentsBegin = "Attachments.Begin";
        public const string AttachmentsEnd = "Attachments.End";

        public const string ProjectStatusBegin = "ProjectStatus.Begin";
        public const string ProjectStatusEnd = "ProjectStatus.End";

        public const string FinalizeBegin = "Finalize.Begin";
        public const string FinalizeEnd = "Finalize.End";

        public const string Completed = "Completed";
        public const string Failed = "Failed";
        public const string Skipped = "Skipped";
    }

    public static string OutcomeFromSuccess(bool success) =>
        success ? Outcomes.Success : Outcomes.Failed;

    public static string ClassifyFailure(Exception? ex, HttpStatusCode? statusCode = null)
    {
        if (statusCode is HttpStatusCode.Unauthorized) return FailureCategories.Authentication;
        if (statusCode is HttpStatusCode.Forbidden) return FailureCategories.Authorization;
        if (statusCode is HttpStatusCode.TooManyRequests) return FailureCategories.Throttle;
        if (statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout) return FailureCategories.Timeout;
        if (statusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity) return FailureCategories.Validation;
        if (statusCode is HttpStatusCode.Conflict) return FailureCategories.BusinessRule;

        if (ex is null) return FailureCategories.Unknown;
        if (ex is TaskCanceledException or TimeoutException) return FailureCategories.Timeout;
        if (ex is HttpRequestException hre)
        {
            if (hre.StatusCode is HttpStatusCode.Unauthorized) return FailureCategories.Authentication;
            if (hre.StatusCode is HttpStatusCode.Forbidden) return FailureCategories.Authorization;
            if (hre.StatusCode is HttpStatusCode.TooManyRequests) return FailureCategories.Throttle;
            if (hre.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout) return FailureCategories.Timeout;
            return FailureCategories.Network;
        }

        var name = ex.GetType().Name;
        if (name.Contains("Json", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Serialization", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Format", StringComparison.OrdinalIgnoreCase))
            return FailureCategories.Serialization;

        if (name.Contains("Argument", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("InvalidOperation", StringComparison.OrdinalIgnoreCase))
            return FailureCategories.Validation;

        return FailureCategories.Unhandled;
    }

    public static string NormalizeDependencyOperation(string? operationName)
    {
        if (string.IsNullOrWhiteSpace(operationName)) return "HTTP";
        var op = operationName.Trim();

        return op switch
        {
            "JournalValidate" => "Fscm.ValidateJournal",
            "JournalCreate" => "Fscm.CreateJournal",
            "JournalPost" => "Fscm.PostJournal",
            "UpdateInvoiceAttributes" => "Fscm.UpdateInvoiceAttributes",
            "UpdateProjectStatus" => "Fscm.UpdateProjectStatus",
            "CopyAttachment" => "Fscm.CopyAttachment",
            _ => op
        };
    }
}
