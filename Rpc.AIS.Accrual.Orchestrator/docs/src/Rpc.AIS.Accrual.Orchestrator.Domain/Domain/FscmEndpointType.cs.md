# FscmEndpointType Enumeration

## Overview

The **FscmEndpointType** enum identifies which FSCM endpoint an operation targets.

It drives endpoint-specific local validation and error-code generation.

Consumers use it to tailor request rules before calling FSCM services.

## Definition

```csharp
namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain;

/// <summary>
/// Represents the FSCM endpoint being invoked.
/// Used to drive endpoint-specific local validation rules.
/// </summary>
public enum FscmEndpointType
{
    Unknown = 0,

    // Subproject
    SubProjectCreate,

    // Journals
    JournalValidate,
    JournalCreate,
    JournalPost,

    // Project
    InvoiceAttributesUpdate,
    ProjectStatusUpdate
}
```

## Members

| Name | Value | Description |
| --- | --- | --- |
| **Unknown** | 0 | Default; no endpoint specified. |
| **SubProjectCreate** | 1 | Create a new FSCM subproject. |
| **JournalValidate** | 2 | Validate journal payload locally. |
| **JournalCreate** | 3 | Create journal entries in FSCM. |
| **JournalPost** | 4 | Post validated journals to FSCM. |
| **InvoiceAttributesUpdate** | 5 | Update invoice attributes in FSCM. |
| **ProjectStatusUpdate** | 6 | Update project or subproject status. |


## Usage & Integration

- **Local Request Validation**

`FscmEndpointRequestValidator.Validate` uses this enum to build error codes and messages per endpoint .

- **WO Payload Validation**

`IWoLocalValidator.ValidateLocally` receives the endpoint type to apply rules specific to subproject or journal operations .

- **Pre-flight Endpoint Checks**

The posting coordinator invokes `TryValidateEndpoint(FscmEndpointType, …)` to enforce required fields per endpoint .

## Related Components

- **FscmEndpointRequestValidator** (`*.Application/Features/Validation/Services/Validation/FscmEndpointRequestValidator.cs`)

Builds a list of `EndpointValidationError` entries based on missing fields for each endpoint.

- **IWoLocalValidator** (`*.Application/Ports/Common/Abstractions/IWoLocalValidator.cs`)

Drives in-memory payload validation for work orders before FSCM calls.

- **PostingContextRequest.TryValidateEndpoint** (in the Durable orchestrator)

Performs pre-validation using this enum to generate HTTP 400 responses for bad requests.

## See Also

- **EndpointValidationError**: record carrying error code and message for validation failures.
- **RequiredFieldRule\<TRequest>**: rule that checks presence of request properties.
- **JournalType**: enum listing Item, Expense, Hour journal categories.