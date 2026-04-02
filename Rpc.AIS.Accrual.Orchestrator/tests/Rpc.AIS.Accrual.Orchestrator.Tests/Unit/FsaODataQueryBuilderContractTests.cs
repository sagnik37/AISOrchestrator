using FluentAssertions;

using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

using Xunit;

namespace Rpc.AIS.Accrual.Orchestrator.Tests.Unit;

public sealed class FsaODataQueryBuilderContractTests
{
    [Fact]
    public void BuildOpenWorkOrdersRelative_includes_select_expand_filter_and_top()
    {
        var qb = new FsaODataQueryBuilder();

        var q = qb.BuildOpenWorkOrdersRelative(filter: "modifiedon gt 2026-01-01T00:00:00Z", pageSize: 500);

        q.Should().Contain("$select=");
        q.Should().Contain("$expand=");
        q.Should().Contain("$filter=");
        q.Should().Contain("$top=500");
        q.Should().Contain("msdyn_workorderid");
        q.Should().Contain("rpc_wellname");
    }
}
