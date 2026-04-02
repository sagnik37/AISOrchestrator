# Rpc.AIS.Accrual.Orchestrator.Domain Project

## Overview

The **Rpc.AIS.Accrual.Orchestrator.Domain** project contains all of the core domain model definitions used throughout the accrual orchestrator solution. It defines the immutable data types, records and enums that represent work-order payloads, journal types, validation results, staging references and other core concepts. This library is designed to be internal-only and is **not** published as a NuGet package.

## Project File

```xml
<!-- src/Rpc.AIS.Accrual.Orchestrator.Domain/Rpc.AIS.Accrual.Orchestrator.Domain.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
</Project>
```

## Project Properties

| Element | Value | Description |
| --- | --- | --- |
| **Sdk** | Microsoft.NET.Sdk | Uses the standard .NET SDK to build a class-library targeting .NET 6+/.NET Core. |
| **IsPackable** | false | Disables NuGet packaging; this assembly is consumed by in-repo projects only. |


## Integration Points

This domain library is referenced by multiple layers in the solution to ensure consistency of business concepts:

- **Rpc.AIS.Accrual.Orchestrator.Application**
- **Rpc.AIS.Accrual.Orchestrator.Core**
- **Rpc.AIS.Accrual.Orchestrator.Infrastructure**

These references allow each layer—application services, orchestration logic and infrastructure adapters—to share the same set of domain types.

## Key Domain Models

While this project file itself has no code, the Domain assembly contains models such as:

| Model | Purpose |
| --- | --- |
| **AccrualStagingRef** | Carries staging reference data for accrual records. |
| **ValidationResult** | Indicates validity of a staging record (valid/invalid). |
| **JournalType** (enum) | Defines types of journals: Item, Expense, Hour. |
| **FscmEndpointType** (enum) | Identifies FSCM endpoint being invoked for validation. |
| **WorkOrderStatusUpdateResponse** | Represents the outcome of a status update call. |


## Dependencies

> 📦 **Note:** Because `IsPackable` is set to , this project will **not** produce a NuGet package. It exists purely to share domain types across in-solution layers.

- This project has **no** external NuGet package dependencies.
- It exposes only its own domain definitions.

---

*This project file is intentionally minimal, reflecting its role purely as a container for shared domain types.*