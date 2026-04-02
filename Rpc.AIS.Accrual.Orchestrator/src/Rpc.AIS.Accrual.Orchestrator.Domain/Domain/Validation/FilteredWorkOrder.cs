using System.Collections.Generic;
using System.Text.Json;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;

/// <summary>
/// A work order JSON object with a resolved journal section key and the journal lines selected for posting.
/// </summary>
public sealed record FilteredWorkOrder(JsonElement WorkOrder, string SectionKey, IReadOnlyList<JsonElement> Lines);
