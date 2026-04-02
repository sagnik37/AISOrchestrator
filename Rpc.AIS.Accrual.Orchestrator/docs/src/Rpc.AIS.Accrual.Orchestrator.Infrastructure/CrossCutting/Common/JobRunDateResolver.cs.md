# JobRunDateResolver Feature Documentation

## Overview

The **JobRunDateResolver** centralizes the policy for determining the “job run date” across the accrual orchestrator. It ensures that all components use a consistent UTC-based date stamp when comparing or stamping periods. This utility removes ambiguity by always truncating the time component and returning the date portion of a UTC timestamp.

## Architecture Context

This class resides in the **Infrastructure** layer under **CrossCutting/Common**, reflecting its role as a shared utility. It is designed to be referenced by any component that needs a standard run-date calculation, such as period comparisons or metadata stamping in orchestrations.

## Component Structure

### 📅 JobRunDateResolver (`src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/CrossCutting/Common/JobRunDateResolver.cs`)

- **Purpose**- Provide a single point of truth for computing the run date from a UTC timestamp.
- **Namespace**- `Rpc.AIS.Accrual.Orchestrator.Infrastructure.Utils`
- **Key Method**

| Method | Description |
| --- | --- |
| `GetRunDate(DateTime utcNow)` → `DateTime` | Returns the date component (midnight UTC) of the provided timestamp. |


- **Code Snippet**

```csharp
  using System;

  // Compute the job run date from the current UTC time
  DateTime runDate = JobRunDateResolver.GetRunDate(DateTime.UtcNow);
  // runDate will be today’s date at 00:00:00 UTC
```

## Usage Example

In any orchestration or service where you need a standardized run date:

```csharp
public void StampJobMetadata(DateTimeOffset nowUtc)
{
    DateTime jobRunDate = JobRunDateResolver.GetRunDate(nowUtc.UtcDateTime);
    // Use jobRunDate for period lookups or audit stamps
}
```

## Key Classes Reference

| Class | Location | Responsibility |
| --- | --- | --- |
| JobRunDateResolver | `src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/CrossCutting/Common/JobRunDateResolver.cs` | Compute and expose the UTC run-date (date-only) for jobs. |


## Dependencies

- **System** (BCL)- Relies only on `System.DateTime`.

## Integration Points

- **Orchestration Components**: Used wherever consistent job-date stamping is required (e.g., period resolution, logging scopes).
- **Accounting Period Resolver**: Can be paired with fiscal-period logic to anchor comparisons at midnight UTC.

## Testing Considerations

- **Scenario**: Passing a `DateTime` with a non-midnight time returns only the date portion.
- **Example Test**

```csharp
  [Fact]
  public void GetRunDate_TruncatesTimeComponent()
  {
      var now = new DateTime(2026, 3, 2, 15, 30, 45, DateTimeKind.Utc);
      var runDate = JobRunDateResolver.GetRunDate(now);
      runDate.Should().Be(new DateTime(2026, 3, 2));
  }
```