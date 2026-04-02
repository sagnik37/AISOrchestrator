using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.WoPayloadValidationPipeline;

public sealed partial class WoLocalValidator
{
    private static bool TryValidateOperationsDate(
        Guid woGuid,
        string? woNumber,
        JournalType journalType,
        Guid lineGuid,
        JsonElement ln,
        List<WoPayloadValidationFailure> invalidFailures)
    {
        var raw =
    // New canonical key
    WoPayloadJsonHelpers.TryGetString(ln, "OperationDate")

    // Backward compatibility
    ?? WoPayloadJsonHelpers.TryGetString(ln, "RPCWorkingDate")
    ?? WoPayloadJsonHelpers.TryGetString(ln, "rpc_OperationsDate")
    ?? WoPayloadJsonHelpers.TryGetString(ln, "rpc_operationsdate")
    ?? WoPayloadJsonHelpers.TryGetString(ln, "RPCOperationsDate")
    ?? WoPayloadJsonHelpers.TryGetString(ln, "OperationsDate");


        if (string.IsNullOrWhiteSpace(raw))
        {
            invalidFailures.Add(new WoPayloadValidationFailure(
                woGuid,
                woNumber,
                journalType,
                lineGuid,
                "AIS_LINE_MISSING_RPC_OPERATIONSDATE",
                "Journal line missing RPCWorkingDate (Operations/Working Date) derived from Field Service.",
                ValidationDisposition.Invalid));
            return false;
        }

        if (!TryParseFscmOrIsoDate(raw, out _))
        {
            invalidFailures.Add(new WoPayloadValidationFailure(
                woGuid,
                woNumber,
                journalType,
                lineGuid,
                "AIS_LINE_INVALID_RPC_OPERATIONSDATE",
                $"Journal line has invalid rpc_OperationsDate value: '{raw}'.",
                ValidationDisposition.Invalid));
            return false;
        }

        return true;
    }

    private static bool TryValidateTransactionDate(
        Guid woGuid,
        string? woNumber,
        JournalType journalType,
        Guid lineGuid,
        JsonElement ln,
        List<WoPayloadValidationFailure> invalidFailures)
    {
        var raw = WoPayloadJsonHelpers.TryGetString(ln, "TransactionDate")
               ?? WoPayloadJsonHelpers.TryGetString(ln, "transactionDate");

        if (string.IsNullOrWhiteSpace(raw))
        {
            invalidFailures.Add(new WoPayloadValidationFailure(
                woGuid,
                woNumber,
                journalType,
                lineGuid,
                "AIS_LINE_MISSING_TRANSACTIONDATE",
                "Journal line missing TransactionDate.",
                ValidationDisposition.Invalid));
            return false;
        }

        if (!TryParseFscmOrIsoDate(raw, out _))
        {
            invalidFailures.Add(new WoPayloadValidationFailure(
                woGuid,
                woNumber,
                journalType,
                lineGuid,
                "AIS_LINE_INVALID_TRANSACTIONDATE",
                $"Journal line has invalid TransactionDate value: '{raw}'.",
                ValidationDisposition.Invalid));
            return false;
        }

        return true;
    }

    private static bool TryValidateRequiredQuantity(
        Guid woGuid,
        string? woNumber,
        JournalType journalType,
        Guid lineGuid,
        JsonElement ln,
        List<WoPayloadValidationFailure> invalidFailures)
    {
        // Hour lines don't have Quantity; they use Duration.
        var fieldName = journalType == JournalType.Hour ? "Duration" : "Quantity";

        if (!ln.TryGetProperty(fieldName, out _))
        {
            invalidFailures.Add(new WoPayloadValidationFailure(
                woGuid,
                woNumber,
                journalType,
                lineGuid,
                journalType == JournalType.Hour
                    ? "AIS_LINE_MISSING_DURATION"
                    : "AIS_LINE_MISSING_QUANTITY",
                journalType == JournalType.Hour
                    ? "Journal line missing required Duration."
                    : "Journal line missing required Quantity.",
                ValidationDisposition.Invalid));
            return false;
        }

        if (!WoPayloadJsonHelpers.TryGetNumber(ln, fieldName, out var val))
        {
            invalidFailures.Add(new WoPayloadValidationFailure(
                woGuid,
                woNumber,
                journalType,
                lineGuid,
                journalType == JournalType.Hour
                    ? "AIS_LINE_INVALID_DURATION"
                    : "AIS_LINE_INVALID_QUANTITY",
                journalType == JournalType.Hour
                    ? "Journal line has invalid Duration."
                    : "Journal line has invalid Quantity.",
                ValidationDisposition.Invalid));
            return false;
        }

        // Optional: enforce > 0 (recommended to avoid 0 duration/qty lines)
        //if (val <= 0m)
        //{
        //    invalidFailures.Add(new WoPayloadValidationFailure(
        //        woGuid,
        //        woNumber,
        //        journalType,
        //        lineGuid,
        //        journalType == JournalType.Hour
        //            ? "AIS_LINE_NONPOSITIVE_DURATION"
        //            : "AIS_LINE_NONPOSITIVE_QUANTITY",
        //        journalType == JournalType.Hour
        //            ? "Journal line Duration must be greater than zero."
        //            : "Journal line Quantity must be greater than zero.",
        //        ValidationDisposition.Invalid));
        //    return false;
        //}

        return true;
    }


    private static bool TryValidateOptionalNumericFields(
        Guid woGuid,
        string? woNumber,
        JournalType journalType,
        Guid lineGuid,
        JsonElement ln,
        List<WoPayloadValidationFailure> invalidFailures)
    {
        if (ln.ValueKind == JsonValueKind.Object && ln.TryGetProperty("Price", out _))
        {
            if (!WoPayloadJsonHelpers.TryGetNumber(ln, "Price", out _))
            {
                invalidFailures.Add(new WoPayloadValidationFailure(
                    woGuid,
                    woNumber,
                    journalType,
                    lineGuid,
                    "AIS_LINE_INVALID_PRICE",
                    "Line has invalid Price.",
                    ValidationDisposition.Invalid));
                return false;
            }
        }

        return true;
    }

    private static bool TryParseFscmOrIsoDate(string raw, out DateTimeOffset utc)
    {
        utc = default;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var m = FscmDateRegex.Match(raw);
        if (m.Success && long.TryParse(m.Groups[1].Value, out var ms))
        {
            utc = DateTimeOffset.FromUnixTimeMilliseconds(ms);
            return true;
        }

        return DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out utc);
    }
}


