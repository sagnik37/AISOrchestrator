using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;

using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services;

internal sealed record CustomerChangeRequest(
    Guid WorkOrderGuid,
    string? WorkOrderId,
    string? OldSubProjectId,
    string? ParentProjectId,
    string? ProjectName,
    int? IsFsaProject,
    int? ProjectStatus,
    string? LegalEntity)
{

        public static CustomerChangeRequest? TryParse(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                // FS envelope: { "_request": { "WOList": [ { ... } ] } }
                var payloadRoot = root;
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("_request", out var req) && req.ValueKind == JsonValueKind.Object &&
                    req.TryGetProperty("WOList", out var woList) && woList.ValueKind == JsonValueKind.Array)
                {
                    var first = woList.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind == JsonValueKind.Object)
                        payloadRoot = first;
                }

                Guid wo = Guid.Empty;
                if (TryGetString(payloadRoot, "WorkOrderGUID", out var woStr) ||
                    TryGetString(payloadRoot, "WorkOrderGuid", out woStr) ||
                    TryGetString(payloadRoot, "workOrderGuid", out woStr))
                {
                    // allow "{GUID}" format
                    var trimmed = woStr?.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                        Guid.TryParse(trimmed.Trim('{', '}'), out wo);
                }

                TryGetString(payloadRoot, "WorkOrderID", out var woId);

                // NOTE: oldSubProjectId might be provided under different casing/key styles depending on caller.
                TryGetString(payloadRoot, "OldSubProjectId", out var old);
                if (string.IsNullOrWhiteSpace(old))
                    TryGetString(payloadRoot, "OldSubProjectID", out old);

                // Root-level fallback (older callers)
                TryGetString(payloadRoot, "ParentProjectId", out var parent);
                TryGetString(payloadRoot, "ProjectName", out var name);

                int? isFsaProject = null;
                int? projectStatus = null;

                // NEW contract: NewSubProjectOverrides
                if (payloadRoot.ValueKind == JsonValueKind.Object &&
                    payloadRoot.TryGetProperty("NewSubProjectOverrides", out var ov) &&
                    ov.ValueKind == JsonValueKind.Object)
                {
                    if (TryGetString(ov, "ParentProjectId", out var ovParent) && !string.IsNullOrWhiteSpace(ovParent))
                        parent = ovParent;

                    if (TryGetString(ov, "ProjectName", out var ovName) && !string.IsNullOrWhiteSpace(ovName))
                        name = ovName;

                    if (TryGetInt32(ov, "IsFSAProject", out var n1))
                        isFsaProject = n1;

                    if (TryGetInt32(ov, "ProjectStatus", out var n2))
                        projectStatus = n2;
                }

                // Legal entity from FS payload: Company
                TryGetString(payloadRoot, "Company", out var le);

                return new CustomerChangeRequest(
                    WorkOrderGuid: wo,
                    WorkOrderId: woId,
                    OldSubProjectId: old,
                    ParentProjectId: parent,
                    ProjectName: name,
                    IsFsaProject: isFsaProject,
                    ProjectStatus: projectStatus,
                    LegalEntity: le);
            }
            catch (Exception ex)
            {
                ThrottledTrace.Debug(
                    key: "CustomerChangeOrchestrator.ParseRequest",
                    message: "Failed to parse customer change request JSON (best-effort).",
                    ex: ex);
                return null;
            }
        }

        private static bool TryGetString(JsonElement root, string prop, out string? value)
        {
            value = null;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty(prop, out var el)) return false;

            if (el.ValueKind == JsonValueKind.String)
            {
                value = el.GetString();
                return true;
            }

            value = el.ToString();
            return true;
        }

        private static bool TryGetInt32(JsonElement root, string prop, out int value)
        {
            value = default;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty(prop, out var el)) return false;

            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
            {
                value = n;
                return true;
            }

            if (el.ValueKind == JsonValueKind.String &&
                int.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
            {
                value = s;
                return true;
            }

            return false;
        }
    }

