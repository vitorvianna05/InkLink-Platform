using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using InkLink.Finance.Application.Accounts;
using InkLink.Finance.Application.Ledger;
namespace InkLink.Finance.Infrastructure.Integration;

public sealed record FinanceAddresses(Uri Scheduling, Uri Messaging, Uri Engagements);
public sealed class FinanceSources(HttpClient http, FinanceAddresses addresses, ICurrentFinanceAccount current) : IFinanceSources
{
    private static readonly JsonSerializerOptions SourceJson = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };
    public async Task<ArtistCalendarContext> CalendarAsync(CancellationToken ct)
    {
        var result = await GetAsync<ArtistCalendarContext>(new(addresses.Scheduling, "api/v1/scheduling/artist-context"), ct);
        if (result.ArtistId == Guid.Empty || result.ObservedAt == default || string.IsNullOrWhiteSpace(result.TimeZoneId)
            || !TimeZoneInfo.TryConvertIanaIdToWindowsId(result.TimeZoneId, out _)) throw ManageLedger.Unavailable();
        return result;
    }
    public async Task VerifyEngagementAsync(Guid id, Guid artist, CancellationToken ct)
    {
        var context = await GetAsync<EngagementContext>(new(addresses.Engagements, $"api/v2/engagements/{id}"), ct, privateResource: true);
        if (context.Id != id || context.ArtistAccountId != artist) throw ManageLedger.NotFound();
    }
    private async Task<T> GetAsync<T>(Uri address, CancellationToken ct, bool privateResource = false)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", current.Credential);
            using var response = await http.SendAsync(request, ct);
            if (privateResource && response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden) throw ManageLedger.NotFound();
            if (response.StatusCode != HttpStatusCode.OK) throw ManageLedger.Unavailable();
            return await response.Content.ReadFromJsonAsync<T>(SourceJson, ct) ?? throw ManageLedger.Unavailable();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        { throw ManageLedger.Unavailable(); }
    }
    private sealed record EngagementContext(Guid Id, Guid ArtistAccountId);
}
