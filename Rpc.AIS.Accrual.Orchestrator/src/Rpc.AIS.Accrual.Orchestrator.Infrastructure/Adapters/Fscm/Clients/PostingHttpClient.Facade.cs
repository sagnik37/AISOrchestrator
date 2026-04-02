using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// Typed HttpClient facade for posting. Keeps DI surface small by delegating to a workflow built from <see cref="IPostingWorkflowFactory"/>.
/// </summary>
public sealed class PostingHttpClient : IPostingClient
{
    private readonly IPostingClient _inner;

    public PostingHttpClient(HttpClient httpClient, IPostingWorkflowFactory factory)
    {
        if (httpClient is null) throw new ArgumentNullException(nameof(httpClient));
        if (factory is null) throw new ArgumentNullException(nameof(factory));

        _inner = factory.Create(httpClient);
    }

    public Task<PostResult> PostAsync(RunContext context, JournalType journalType, IReadOnlyList<AccrualStagingRef> records, CancellationToken ct)
        => _inner.PostAsync(context, journalType, records, ct);

    public Task<PostResult> PostFromWoPayloadAsync(RunContext context, JournalType journalType, string woPayloadJson, CancellationToken ct)
        => _inner.PostFromWoPayloadAsync(context, journalType, woPayloadJson, ct);

    public Task<PostResult> PostValidatedWoPayloadAsync(RunContext context, JournalType journalType, string woPayloadJson, IReadOnlyList<PostError> preErrors, string? validationResponseRaw, CancellationToken ct)
        => _inner.PostValidatedWoPayloadAsync(context, journalType, woPayloadJson, preErrors, validationResponseRaw, ct);

    public Task<List<PostResult>> ValidateOnceAndPostAllJournalTypesAsync(RunContext context, string woPayloadJson, CancellationToken ct)
        => _inner.ValidateOnceAndPostAllJournalTypesAsync(context, woPayloadJson, ct);
}
