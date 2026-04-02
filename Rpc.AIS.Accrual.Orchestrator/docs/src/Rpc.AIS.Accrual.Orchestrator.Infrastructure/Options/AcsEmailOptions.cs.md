# ACS Email Options Feature Documentation

## Overview

This feature provides a strongly-typed configuration model for Azure Communication Services (ACS) Email within the Orchestrator Infrastructure. It allows the application to load settings such as connection string, sender address, and behavior flags from configuration (e.g. `local.settings.json` or App Settings with Key Vault references).

By centralizing ACS Email settings in a single `AcsEmailOptions` class, the application can:

- Enable or disable email sending globally.
- Supply the ACS connection string securely via Key Vault.
- Define sender address and display name.
- Control whether to await service-side processing or return immediately for faster function execution.

This configuration model is consumed by the `AcsEmailSender` implementation of `IEmailSender`, ensuring that email-sending behavior is consistent and easily adjustable without code changes.

## Architecture Overview

```mermaid
flowchart TB
    subgraph Configuration_Binding
        Options[AcsEmailOptions\n(Section: "AcsEmail")]
    end

    subgraph Infrastructure
        Sender[AcsEmailSender]:::service
        Noop[NoopEmailSender]:::service
        EmailClient[EmailClient]:::external
        Logger[ILogger]:::external
    end

    Options -->|Inject via DI| Sender
    Options -->|Inject via DI| Noop
    Sender --> EmailClient
    Sender --> Logger

    classDef service fill:#bbf,stroke:#333,stroke-width:1px;
    classDef external fill:#ffb,stroke:#333,stroke-width:1px;
```

## Component Structure

### Configuration Model

#### **AcsEmailOptions** (`src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Options/AcsEmailOptions.cs`)

- **Purpose**: Captures all ACS Email–related settings for binding from configuration.
- **Binding**: Mapped from the `AcsEmail` section in application settings.
- **Usage**: Injected into `AcsEmailSender` to control email-sending behavior.

**Properties**

| Property | Type | Description | Default |
| --- | --- | --- | --- |
| SectionName | string | Configuration section key for binding options. | "AcsEmail" |
| Enabled | bool | When `false`, email sending is disabled and `NoopEmailSender` is used. | true |
| ConnectionString | string | ACS Email connection string (use Key Vault references). Example: `"AcsEmail:ConnectionString"`. | "" |
| FromAddress | string | Sender email address. Must be a verified sender/domain in ACS Email (e.g. `"no-reply@domain.com"`). | "" |
| FromDisplayName | string | Optional display name for the sender (e.g. `"Acme Notifications"`). | "" |
| WaitUntilCompleted | bool | If `true`, waits until ACS completes processing; if `false`, returns once accepted (recommended for funcs). | false |


## Configuration Example

```json
{
  "AcsEmail": {
    "Enabled": true,
    "ConnectionString": "<Your ACS connection string from Key Vault>",
    "FromAddress": "no-reply@domain.com",
    "FromDisplayName": "ACS Notifications",
    "WaitUntilCompleted": false
  }
}
```

```card
{
    "title": "Key Vault Integration",
    "content": "Store the ConnectionString in Key Vault and reference it via App Settings for secure handling."
}
```

## Integration Points

- **Dependency Injection**

Registered as a singleton via `IOptions<AcsEmailOptions>.Value` and injected into:

- `AcsEmailSender` (real email delivery)
- `NoopEmailSender` (when `Enabled = false`)

- **Email Sending Pipeline**- `AcsEmailSender` reads these options to initialize `EmailClient`
- Respects `Enabled` and `WaitUntilCompleted` flags
- Uses `FromAddress`/`FromDisplayName` when composing messages

## Dependencies

- Microsoft.Extensions.Options
- Azure.Communication.Email (for `EmailClient`)
- Microsoft.Extensions.Logging (for logging in `AcsEmailSender`)

## Key Classes Reference

| Class | Location | Responsibility |
| --- | --- | --- |
| AcsEmailOptions | src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Options/AcsEmailOptions.cs | Defines ACS Email configuration settings. |
| AcsEmailSender | src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Notifications/AcsEmailSender.cs | Sends emails via ACS Email, honoring options and logging outcomes. |
| NoopEmailSender | src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Notifications/NoopEmailSender.cs | Placeholder sender when email sending is disabled. |
