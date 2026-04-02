# JsonLooseKey Utility Documentation

## Overview ✨

The **JsonLooseKey** class provides flexible JSON property access by performing case-insensitive and “loose” key matching. It tolerates differences in casing, spaces, underscores, and hyphens. This utility ensures consistent parsing across diverse payload formats and eases integration with external JSON sources.

## Placement & Namespace

- **File:** `src/Rpc.AIS.Accrual.Orchestrator.Application/Features/Shared/Utilities/JsonLooseKey.cs`
- **Namespace:** `Rpc.AIS.Accrual.Orchestrator.Core.Utilities`
- **Type:** `public static class JsonLooseKey`

## Key Responsibilities

- **Normalize JSON keys** by removing non-alphanumeric characters and lowercasing.
- **Lookup JSON nodes** (`JsonNode`) in a `JsonObject` with exact, case-insensitive, or loose matching.
- **Retrieve string values** from a `JsonObject` by loose key.
- **Lookup properties** in a `JsonElement` with loose key matching.

## Methods Reference 🔑

| Method | Description | Signature |
| --- | --- | --- |
| **NormalizeKeyLoose** | Strips non-alphanumeric chars and lowercases the key for comparison. | `public static string NormalizeKeyLoose(string s)` |
| **TryGetNodeLoose** | Finds a `JsonNode` in a `JsonObject`, using exact, case-insensitive, or loose key match. | `public static bool TryGetNodeLoose(JsonObject obj, string key, out JsonNode? node)` |
| **TryGetStringLoose** | Retrieves a string representation of a node from a `JsonObject` by loose key. | `public static bool TryGetStringLoose(JsonObject obj, string key, out string? value)` |
| **TryGetPropertyLoose** | Finds a `JsonElement` property in a `JsonElement` object, using loose key matching. | `public static bool TryGetPropertyLoose(JsonElement obj, string key, out JsonElement value)` |


## Method Details

### NormalizeKeyLoose

Removes all non-letter-or-digit characters and converts to lower case.

```csharp
public static string NormalizeKeyLoose(string s)
{
    if (string.IsNullOrEmpty(s)) 
        return string.Empty;

    Span<char> buf = stackalloc char[s.Length];
    var idx = 0;
    foreach (var ch in s)
    {
        if (char.IsLetterOrDigit(ch))
            buf[idx++] = char.ToLowerInvariant(ch);
    }
    return new string(buf[..idx]);
}
```

### TryGetNodeLoose

1. **Exact match:** `obj.TryGetPropertyValue(key, out node)`.
2. **Case-insensitive match:** compares `kv.Key` to `key` ignoring case.
3. **Loose match:** compares normalized forms of `kv.Key` and `key`.

```csharp
public static bool TryGetNodeLoose(JsonObject obj, string key, out JsonNode? node)
{
    node = null;
    if (obj.TryGetPropertyValue(key, out var direct))
    {
        node = direct;
        return true;
    }
    foreach (var kv in obj)
    {
        if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
        {
            node = kv.Value;
            return true;
        }
    }
    var target = NormalizeKeyLoose(key);
    if (target.Length == 0) return false;
    foreach (var kv in obj)
    {
        if (NormalizeKeyLoose(kv.Key) == target)
        {
            node = kv.Value;
            return true;
        }
    }
    return false;
}
```

### TryGetStringLoose

Leverages `TryGetNodeLoose` to extract a node and converts it to string.

```csharp
public static bool TryGetStringLoose(JsonObject obj, string key, out string? value)
{
    value = null;
    if (!TryGetNodeLoose(obj, key, out var node) || node is null) 
        return false;
    value = node.ToString();
    return true;
}
```

### TryGetPropertyLoose

Supports loose lookup on a `JsonElement` representing an object.

```csharp
public static bool TryGetPropertyLoose(JsonElement obj, string key, out JsonElement value)
{
    value = default;
    if (obj.ValueKind != JsonValueKind.Object) 
        return false;
    if (obj.TryGetProperty(key, out value)) 
        return true;

    var target = NormalizeKeyLoose(key);
    foreach (var p in obj.EnumerateObject())
    {
        if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)
            || NormalizeKeyLoose(p.Name) == target)
        {
            value = p.Value;
            return true;
        }
    }
    return false;
}
```

## Usage Examples

- **Normalize a key**

```csharp
  var norm = JsonLooseKey.NormalizeKeyLoose("Work_Order-ID ");
  // norm == "workorderid"
```

- **Retrieve a JSON node**

```csharp
  if (JsonLooseKey.TryGetNodeLoose(payloadObj, "Journal lines", out var section))
  {
      // section holds the matching JsonNode
  }
```

- **Get a string value**

```csharp
  if (JsonLooseKey.TryGetStringLoose(itemObj, "DimensionDisplayValue", out var ddv))
  {
      // ddv holds the string value
  }
```

- **Access a JsonElement property**

```csharp
  if (JsonLooseKey.TryGetPropertyLoose(element, "WorkOrderGUID", out var guidEl))
  {
      var guidText = guidEl.GetString();
  }
```

## Integration Points

This utility is widely used across the application’s JSON handling layers:

- **WoPayloadJsonToolkit** for normalizing WO payloads and cleaning null sections.
- **DeltaJournalSectionBuilder** for tolerant reads of journal line properties.
- **WoDeltaPayloadClassifier** for extracting work order lists and GUIDs.

## Dependencies

- System.Text.Json
- System.Text.Json.Nodes

## Testing Considerations

- Verify **NormalizeKeyLoose** removes spaces, underscores, hyphens, and preserves only alphanumeric lowercase.
- Confirm **TryGetNodeLoose** matches keys in:- Exact casing
- Different casing
- Variations with spaces/underscores/hyphens
- Ensure **TryGetStringLoose** returns `false` when node is missing or null.
- Test **TryGetPropertyLoose** against `JsonElement` objects with mixed-case and loosely formatted keys.

## Key Classes Reference

| Class | Location | Responsibility |
| --- | --- | --- |
| JsonLooseKey | src/Rpc.AIS.Accrual.Orchestrator.Application/Features/Shared/Utilities/JsonLooseKey.cs | Loose-key JSON helpers for `JsonObject` and `JsonElement` parsing. |
