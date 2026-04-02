using System;
using System.Collections.Generic;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.Validation;

public static class FscmEndpointRequestValidator
{
    public static List<EndpointValidationError> Validate<TRequest>(
        FscmEndpointType endpoint,
        TRequest? request,
        params RequiredFieldRule<TRequest>[] rules)
    {
        var errors = new List<EndpointValidationError>();

        if (request is null)
        {
            errors.Add(new EndpointValidationError($"AIS_{endpoint}_NULL_REQUEST", "Request is null."));
            return errors;
        }

        foreach (var rule in rules)
        {
            if (!rule.IsSatisfied(request))
            {
                var code = string.IsNullOrWhiteSpace(rule.ErrorCode)
                    ? $"AIS_{endpoint}_MISSING_{rule.FieldName.ToUpperInvariant()}"
                    : rule.ErrorCode!;

                var message = string.IsNullOrWhiteSpace(rule.Message)
                    ? $"{rule.FieldName} is mandatory."
                    : rule.Message!;

                errors.Add(new EndpointValidationError(code, message));
            }
        }

        return errors;
    }

    public static RequiredFieldRule<TRequest> Required<TRequest>(
        string fieldName,
        Func<TRequest, string?> getValue,
        string errorCode,
        string message)
        => new(fieldName, r => !string.IsNullOrWhiteSpace(getValue(r)), errorCode, message);

    public static RequiredFieldRule<TRequest> RequiredGuid<TRequest>(
        string fieldName,
        Func<TRequest, Guid> getValue,
        string errorCode,
        string message)
        => new(fieldName, r => getValue(r) != Guid.Empty, errorCode, message);

    public static RequiredFieldRule<TRequest> Custom<TRequest>(
        string fieldName,
        Func<TRequest, bool> predicate,
        string errorCode,
        string message)
        => new(fieldName, predicate, errorCode, message);
}

public sealed record RequiredFieldRule<TRequest>(
    string FieldName,
    Func<TRequest, bool> IsSatisfied,
    string? ErrorCode,
    string? Message);

public sealed record EndpointValidationError(string Code, string Message);
