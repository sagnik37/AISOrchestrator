# SOLID Seventh Pass Notes

This pass applies the next refactor wave on top of v6 with a focus on strict SRP and dependency inversion around the FSCM invoice-attributes integration path.

## What changed

### 1) `FscmInvoiceAttributesHttpClient` became an orchestration layer
The client no longer mixes payload construction, HTTP transport/audit logging, and response parsing in one class.

New collaborators:
- `IInvoiceAttributesPayloadBuilder` / `InvoiceAttributesPayloadBuilder`
- `IInvoiceAttributesHttpTransport` / `InvoiceAttributesHttpTransport`
- `IInvoiceAttributesResponseParser` / `InvoiceAttributesResponseParser`

### 2) Request building moved out of the client
`InvoiceAttributesPayloadBuilder` now owns:
- endpoint URL normalization
- definitions payload creation
- current-values payload creation
- update payload creation

### 3) HTTP transport and payload audit logging moved out of the client
`InvoiceAttributesHttpTransport` now owns:
- outbound payload logging to AIS/App Insights
- request/response transport
- correlation headers
- inbound response logging
- transient/auth failure handling

### 4) Response parsing moved out of the client
`InvoiceAttributesResponseParser` now owns parsing of:
- attribute definitions
- current attribute values

### 5) DI updated
Infrastructure registrations now include the new abstractions so the main client depends on injected collaborators rather than inline implementation details.

## SOLID impact

- **SRP**: each class now has one primary reason to change.
- **DIP**: the main client depends on interfaces instead of concrete helper logic.
- **ISP**: collaborators have narrowly scoped contracts.
- **OCP**: alternate payload/transport/parser implementations can be introduced without rewriting the client.

## Behavior goal

This is intended as a safe structural refactor only. No business-rule changes were intentionally introduced.

## Suggested next wave

Highest-value next candidates:
- `FsaDeltaPayloadEnricher.WoHeader.cs`
- `InvoiceAttributeSyncRunner.cs`
- `WoDeltaPayloadService.cs`
- `FscmWoPayloadValidationClient.cs`
