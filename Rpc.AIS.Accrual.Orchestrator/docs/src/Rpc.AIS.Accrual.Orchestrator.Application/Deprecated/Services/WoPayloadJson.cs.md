# WoPayloadJson Utility Documentation

## Overview ✨

The **WoPayloadJson** utility centralizes JSON parsing for Work Order (WO) payloads. It provides safe methods to extract string and numeric values from `JsonElement` instances. By reusing these helpers, journal policies and the validation engine avoid duplicating parsing logic and ensure consistent behavior across the application.

## Class Definition

- **Name:** WoPayloadJson
- **Namespace:** `Rpc.AIS.Accrual.Orchestrator.Core.Services`
- **Accessibility:** `internal static`
- **Responsibility:**- Navigate `JsonElement` objects representing WO payloads
- Safely read string properties and numeric properties with minimal boilerplate

## Dependencies

- System.Text.Json- `JsonElement`
- `JsonValueKind`

## Methods 📑

| Method | Description | Parameters | Return Type |
| --- | --- | --- | --- |
| TryGetString | Retrieves a non-empty string property from a JSON object. | `JsonElement obj`<br>`string key` | `string?` |
| TryGetNumber | Parses a numeric property (number or numeric string). | `JsonElement obj`<br>`string key`<br>`out decimal value` | `bool` |


### TryGetString

Extracts a string value if the property exists and is not null/whitespace.

```csharp
internal static string? TryGetString(JsonElement obj, string key)
```

- **Parameters:**- `obj`- Must be a JSON object (`ValueKind == Object`), otherwise returns `null`.
- `key`- The exact property name to look up (case-sensitive).
- **Behavior:**- Returns `null` if:- `obj` is not an object
- The property does not exist
- The property is JSON `null`
- The property’s string value is empty or whitespace
- Otherwise returns the trimmed string.
- **Example:**

```csharp
  JsonElement wo = /* JSON object for a Work Order */;
  string? customer = WoPayloadJson.TryGetString(wo, "CustomerName");
  if (customer != null) {
      Console.WriteLine($"Customer: {customer}");
  }
```

### TryGetNumber

Parses a decimal value from either a JSON number or a numeric string.

```csharp
internal static bool TryGetNumber(JsonElement obj, string key, out decimal value)
```

- **Parameters:**- `obj`- Must be a JSON object, otherwise returns `false`.
- `key`- The exact property name to look up (case-sensitive).
- `out decimal value`- Outputs the parsed decimal on success; remains `0` on failure.
- **Behavior:**- Returns `false` if:- `obj` is not an object
- The property does not exist
- The property is neither a number nor a string
- Parsing fails
- If the property is a JSON number, uses `TryGetDecimal`.
- If the property is a JSON string, uses `decimal.TryParse`.
- **Example:**

```csharp
  JsonElement wo = /* JSON object for a Work Order */;
  if (WoPayloadJson.TryGetNumber(wo, "Quantity", out decimal qty)) {
      Console.WriteLine($"Quantity: {qty}");
  } else {
      Console.WriteLine("Quantity not available or invalid.");
  }
```

## Usage in Application

- **Where used:**- **Journal policies** apply uniform rules when reading payload fields.
- **Validation engine** inspects payloads for schema compliance and filters invalid entries.
- **Benefit:**- Eliminates repeated `TryGetProperty` and null/whitespace checks.
- Improves readability and reduces parsing bugs.

## Key Considerations

- **Graceful failure:** Both methods avoid throwing; they return `null` or `false`.
- **Case-sensitivity:** Keys must match exactly; loose matching is handled elsewhere (e.g., via `JsonLooseKey`).
- **Centralization:** Changes to parsing rules propagate automatically to all consumers.