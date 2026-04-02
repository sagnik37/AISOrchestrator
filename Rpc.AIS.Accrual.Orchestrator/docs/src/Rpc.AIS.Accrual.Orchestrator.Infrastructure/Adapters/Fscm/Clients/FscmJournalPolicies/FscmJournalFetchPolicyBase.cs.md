# FSCM Journal Fetch Policy Base Feature Documentation

## Overview

The **FscmJournalFetchPolicyBase** class serves as the foundational helper for all FSCM journal‐type fetch policies. It defines the common interface and utility methods for selecting OData entity sets, building `$select` clauses, and normalizing numeric fields from JSON payloads. By centralizing shared logic, it enables derived policies to focus solely on journal‐type specific mappings.

This base policy integrates with the **FscmJournalFetchHttpClient** and **FscmJournalFetchPolicyResolver** to implement an open‐closed approach: new journal types can be supported by adding a policy class without modifying core fetch logic. It underpins accurate quantity and unit‐price extraction across heterogeneous FSCM environments.

## Architecture Overview

```mermaid
flowchart TB
    subgraph Data Access Layer
        PolicyBase[FscmJournalFetchPolicyBase]
        IFetchPolicy[IFscmJournalFetchPolicy]
        Resolver[FscmJournalFetchPolicyResolver]
        HttpClient[FscmJournalFetchHttpClient]
        HourPolicy[HourJournalFetchPolicy]
        ItemPolicy[ItemJournalFetchPolicy]
        ExpensePolicy[ExpenseJournalFetchPolicy]
    end

    IFetchPolicy <|-- PolicyBase
    PolicyBase <|-- HourPolicy
    PolicyBase <|-- ItemPolicy
    PolicyBase <|-- ExpensePolicy
    Resolver --> IFetchPolicy
    HttpClient -->|uses| Resolver
    HttpClient -->|uses| PolicyBase
```

## Component Structure

### 3. Data Access Layer 🔌

#### **FscmJournalFetchPolicyBase**

- **Purpose:**- Implements the `IFscmJournalFetchPolicy` interface.
- Provides abstract properties for OData metadata and mapping rules.
- Offers a shared helper (`TryGetDecimal`) to safely parse numeric JSON fields.

- **Key Properties:**

| Property | Type | Description |
| --- | --- | --- |
| JournalType | JournalType | The journal category (Hour, Item, Expense) |
| EntitySet | string | FSCM OData entity set name (e.g., `"JournalTrans"`) |
| Select | string | Comma-separated `$select` list to request required fields |
| SelectFallback | string | Optional fallback `$select` when primary Select fails due to missing fields (defaults to Select) |


- **Key Methods:**

| Method | Signature | Description |
| --- | --- | --- |
| GetQuantity | abstract decimal GetQuantity(JsonElement row) | Extracts the journal quantity or hours. Must return non-null (0 if missing). |
| GetUnitPrice | abstract decimal? GetUnitPrice(JsonElement row) | Extracts the unit price for the journal type; may return null if unavailable. |
| TryGetDecimal (static) | protected static decimal? TryGetDecimal(JsonElement obj, string propName) | Attempts to read a decimal from a JSON property, handling numbers and strings with invariant culture parsing. |


```csharp
protected static decimal? TryGetDecimal(JsonElement obj, string propName)
{
    if (!obj.TryGetProperty(propName, out var p))
        return null;
    return p.ValueKind switch
    {
        JsonValueKind.Number when p.TryGetDecimal(out var d) => d,
        JsonValueKind.String => TryParseDecimal(p.GetString()),
        _ => null
    };

    static decimal? TryParseDecimal(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
            ? d
            : null;
    }
}
```

#### **Derived Policies**

- **HourJournalFetchPolicy**

Handles time‐based entries (`JournalType.Hour`) by selecting fields like `"Hours"`, `"SalesPrice"`, and `"DimensionDisplayValue"`; uses fallback name resolution for quantity/unit price.

- **ItemJournalFetchPolicy**

Targets material journals (`JournalType.Item`) with extended `$select` lists including `"Quantity"`, `"ProjectSalesPrice"`, and various surcharge/discount fields; implements advanced unit‐price normalization.

- **ExpenseJournalFetchPolicy**

Configures select lists for expense lines (`JournalType.Expense`) and provides fallbacks to remove problematic fields when necessary.

#### **FscmJournalFetchPolicyResolver**

- **Purpose:**- Collects all registered `IFscmJournalFetchPolicy` instances.
- Ensures one policy per `JournalType`.
- Resolves the correct policy at runtime or throws on missing/duplicate registrations.

## Key Classes Reference

| Class | Location | Responsibility |
| --- | --- | --- |
| IFscmJournalFetchPolicy | src/.../FscmJournalPolicies/IFscmJournalFetchPolicy.cs | Defines the contract for journal fetch metadata and mapping rules. |
| FscmJournalFetchPolicyBase | src/.../FscmJournalPolicies/FscmJournalFetchPolicyBase.cs | Implements shared helpers and abstract members for fetch policies. |
| HourJournalFetchPolicy | src/.../FscmJournalPolicies/HourJournalFetchPolicy.cs | Fetch policy for hour journals (`JournalTrans`). |
| ItemJournalFetchPolicy | src/.../FscmJournalPolicies/ItemJournalFetchPolicy.cs | Fetch policy for item journals (`ProjectItemJournalTrans`). |
| ExpenseJournalFetchPolicy | src/.../FscmJournalPolicies/ExpenseJournalFetchPolicy.cs | Fetch policy for expense journals (`ExpenseJournalLines`). |
| FscmJournalFetchPolicyResolver | src/.../FscmJournalPolicies/FscmJournalFetchPolicyResolver.cs | Maps `JournalType` to its corresponding `IFscmJournalFetchPolicy`. |


## Dependencies

- **Namespaces:**- `System`
- `System.Globalization`
- `System.Text.Json`
- `Rpc.AIS.Accrual.Orchestrator.Core.Domain` (for `JournalType`)
- `Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.FscmJournalPolicies`

No external services or API endpoints are defined in this class; it integrates within the FSCM OData fetch flow through the `FscmJournalFetchHttpClient`.