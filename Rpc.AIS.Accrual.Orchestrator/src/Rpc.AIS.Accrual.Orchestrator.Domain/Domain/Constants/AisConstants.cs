// File: Rpc.AIS.Accrual.Orchestrator.Domain/Domain/Constants/AisConstants.cs
namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain;

/// <summary>
/// Centralizes magic numbers/strings used across AIS payload generation and FSCM interactions.
/// Keep values stable to avoid breaking external contracts.
/// </summary>
public static class AisConstants
{
    public static class WoPayloadSectionKeys
    {
        // Canonical payload keys (do not change; these are external contract strings).
        public const string WOItemLines = "WOItemLines";
        public const string WOExpLines = "WOExpLines";
        public const string WOHourLines = "WOHourLines";

        // Backward-compatible aliases used in some internal code.
        public const string ItemLines = WOItemLines;
        public const string ExpenseLines = WOExpLines;
        public const string HourLines = WOHourLines;
    }

    public static class AccountingPeriodStrategies
    {
        public const string EffectiveMonthFirst = "EffectiveMonthFirst";
    }

    public static class Delta
    {
        /// <summary>
        /// Tolerance used for price comparisons in delta calculation.
        /// </summary>
        public const decimal PriceComparisonTolerance = 0.0001m;
    }
}

/// <summary>
/// FSCM Project status codes used by AIS when updating subprojects.
/// </summary>
public enum FscmProjectStatus : int
{
    Posted = 5,
    Cancelled = 6
}
public enum FscmSubProjectStatus : int
{
    Inprocess = 3
}
