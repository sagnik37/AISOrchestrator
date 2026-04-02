using System;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Utils;

/// <summary>
/// Centralized policy to resolve the "job run date" used for stamping and period comparisons.
/// Updated: always uses UTC date (no config flags).
/// </summary>
public static class JobRunDateResolver
{
    public static DateTime GetRunDate(DateTime utcNow) => utcNow.Date;
}
