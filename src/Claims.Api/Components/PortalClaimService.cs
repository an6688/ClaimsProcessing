using Claims.Application;
using Claims.Domain;

namespace Claims.Api.Components;

public sealed class PortalClaimService(IServiceScopeFactory scopeFactory)
{
    public Task<ClaimResponse> Submit(SubmitClaimRequest request, CancellationToken ct) =>
        InScope(service => service.Submit(request, ct));

    public Task<ClaimResponse> Get(Guid id, CancellationToken ct) =>
        InScope(service => service.Get(id, ct));

    public Task<ClaimResponse> Process(Guid id, CancellationToken ct) =>
        InScope(service => service.Process(id, ct));

    public Task<Page<ClaimResponse>> List(ClaimStatus? status, CancellationToken ct) =>
        InScope(service => service.List(null, status, 1, 100, ct));

    private async Task<T> InScope<T>(Func<ClaimService, Task<T>> operation)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ClaimService>();
        return await operation(service);
    }
}

