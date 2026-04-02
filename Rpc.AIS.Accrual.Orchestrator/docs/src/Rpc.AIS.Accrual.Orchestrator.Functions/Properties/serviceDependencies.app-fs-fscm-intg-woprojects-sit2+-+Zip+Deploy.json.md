# Service Dependencies – SIT2 Zip Deploy

## Overview

This JSON file configures **service dependencies** for the `app-fs-fscm-intg-woprojects` Azure Function when deploying to the **SIT2** environment via Zip Deploy. It allows environment-specific overrides of default dependencies defined in `serviceDependencies.json`.

## Purpose

- Provide a mechanism to inject or override connection settings (e.g., Application Insights, Key Vault) per environment.
- Ensure the Function App picks up correct resource IDs and connection strings during SIT2 deployments.

## File Structure

```json
{
  "dependencies": {}
}
```

- **dependencies**: Object mapping dependency keys to their configuration.
- In this SIT2 file, no entries are defined, so it inherits defaults or disables environment-specific services.

## Dependencies

- The **dependencies** object is currently empty.
- No service bindings (Application Insights, etc.) are overridden for SIT2.

## Environment Configuration

- Filename pattern:

```plaintext
  serviceDependencies.{FunctionAppName}-{Environment} - Zip Deploy.json
```

- **FunctionAppName**: `app-fs-fscm-intg-woprojects`
- **Environment**: `sit2`

```plaintext
  src/Rpc.AIS.Accrual.Orchestrator.Functions/Properties/
```

## Comparison with Defaults

| File | Defined Dependencies |
| --- | --- |
| **serviceDependencies.json** (default) | - `appInsights1` (core) |
| **serviceDependencies.…-sit2 – Zip Deploy.json** | *None (empty object)* |


## Important Note

```card
{
    "title": "Empty Dependencies",
    "content": "No service dependencies are defined for SIT2; default settings apply during deployment."
}
```

## Integration Points

- **Zip Deploy Pipeline**: Reads this file to apply environment-specific overrides.
- **Azure Functions Worker**: Merges this with the base `serviceDependencies.json` at startup.

## See Also

- `serviceDependencies.json` (base defaults)
- `serviceDependencies.app-fs-fscm-intg-woprojects-dev-EastUs - Zip Deploy.json` (DEV override)