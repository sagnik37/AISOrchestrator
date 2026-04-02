# FSA Line Fetcher Workflow – Parsing Utilities

## Overview

The **FsaLineFetcherWorkflow.Parsing** partial class provides helper methods to translate between string-based work order identifiers and GUIDs, and to extract work order GUIDs from JSON payloads. These utilities support the broader FSA (Field Service Automation) line-fetching workflow by ensuring consistent GUID handling and presence detection in OData responses.

## Component Placement

- **Namespace:** Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients
- **Assembly Path:** `src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/FsaLineFetcherWorkflow.Parsing.cs`
- **Layer:** Data Access / Infrastructure – assists higher-level fetcher logic in `FsaLineFetcherWorkflow`

## Key Methods

| Method | Signature | Description |
| --- | --- | --- |
| **ParseWorkOrderGuids** | `static IReadOnlyList<Guid> ParseWorkOrderGuids(List<string> workOrderIds)` | Converts a list of string IDs to a list of `Guid`. Trims whitespace and surrounding braces; ignores null, empty, or unparseable entries. |
| **ExtractWorkOrderGuidSet** | `static HashSet<string> ExtractWorkOrderGuidSet(JsonDocument presenceDoc)` | Reads an OData JSON document’s `value` array, locates the `_msdyn_workorder_value` string in each row, parses it as a GUID, and returns a case-insensitive set of GUID strings. |


### 1. ParseWorkOrderGuids

- **Parameters:**- `workOrderIds` (List<string>): Raw work order identifiers, possibly including braces or whitespace.
- **Returns:**- `IReadOnlyList<Guid>`: Cleaned GUID list; empty if input is null or contains no valid GUIDs.
- **Behavior:**- Iterates each string.
- Skips null, empty, or whitespace-only entries.
- Trims `{` and `}` characters.
- Attempts `Guid.TryParse`; adds successful parses to the output list.

### 2. ExtractWorkOrderGuidSet

- **Parameters:**- `presenceDoc` (JsonDocument): OData payload containing a top-level `"value"` array of JSON objects.
- **Returns:**- `HashSet<string>`: Unique GUID strings extracted from `_msdyn_workorder_value` properties; empty if document is null or malformed.
- **Behavior:**- Verifies `presenceDoc.RootElement` has a `"value"` array.
- For each array element that is an object:- Looks for a string property named `_msdyn_workorder_value`.
- Trims braces and whitespace; parses as `Guid`.
- On success, adds the normalized GUID (`D` format) to the set.

## Usage Example

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

// Parsing string IDs into GUIDs
var rawIds = new List<string> { "{D2719E5A-4B3F-4A31-9B24-1234567890AB}", "invalid", "  abcd1234-0000-0000-0000-abcdefabcdef  " };
var guids = FsaLineFetcherWorkflow.ParseWorkOrderGuids(rawIds);
// guids now contains two valid GUID values

// Extracting GUID set from an OData response
var json = @"{""value"":[{""_msdyn_workorder_value"":""{D2719E5A-4B3F-4A31-9B24-1234567890AB}""},{""otherProp"":42}]}";
using var doc = JsonDocument.Parse(json);
var guidSet = FsaLineFetcherWorkflow.ExtractWorkOrderGuidSet(doc);
// guidSet contains "d2719e5a-4b3f-4a31-9b24-1234567890ab"
```

```card
{
    "title": "GUID Parsing Note",
    "content": "Both methods trim surrounding braces and ignore invalid or empty inputs to ensure stability."
}
```

## Error Handling

- **Invalid Strings:**- `ParseWorkOrderGuids` silently skips any string that fails `Guid.TryParse`.
- **Malformed JSON:**- `ExtractWorkOrderGuidSet` returns an empty set if:- `presenceDoc` is null.
- The root element lacks a `"value"` array.
- An element’s `_msdyn_workorder_value` is missing or not a string.

## Dependencies

- System.Collections.Generic
- System.Linq
- System.Text.Json
- Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options (for options in surrounding workflow)
- Rpc.AIS.Accrual.Orchestrator.Core.Domain (for `RunContext`, though not directly used here)

## Testing Considerations

- **Empty Input:**- `ParseWorkOrderGuids(null)` and `ParseWorkOrderGuids(new List<string>())` should both yield an empty list.
- **Varied Formatting:**- Strings with extra whitespace, leading/trailing braces, or mixed casing should parse correctly.
- **Invalid Values:**- Non-GUID or malformed strings should be skipped without exception.
- **JSON Edge Cases:**- Documents missing `"value"`, with non-array `"value"`, or rows lacking `_msdyn_workorder_value` should result in an empty set.
- **Case-Insensitive Uniqueness:**- `ExtractWorkOrderGuidSet` treats GUID strings case-insensitively when adding to the `HashSet`.