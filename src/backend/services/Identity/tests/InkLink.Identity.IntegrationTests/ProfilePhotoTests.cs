using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using InkLink.Identity.Application;
using InkLink.Identity.Infrastructure.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using SkiaSharp;

namespace InkLink.Identity.IntegrationTests;

/// <summary>T10.18: uma foto por conta; a do cliente é privada e a do artista é pública e projetada por evento.</summary>
[Collection("security")]
public sealed class ProfilePhotoTests(SecurityFixture fixture, MediaFixture media) : IClassFixture<MediaFixture>
{
    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(new SKColor(30, 120, 200));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static MultipartFormDataContent Form(byte[] content, string field = "image")
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new("image/png");
        return new MultipartFormDataContent { { file, field, "foto.png" } };
    }

    private async Task<HttpClient> SignInAsync(PhotoIdentityHost host, string user)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await fixture.TokenAsync(user))["access_token"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/profiles/me")).StatusCode);
        return client;
    }

    private async Task MakeArtistAsync(PhotoIdentityHost host, HttpClient client)
    {
        var id = (await client.GetFromJsonAsync<JsonObject>("/api/v1/profiles/me"))!["id"]!.GetValue<Guid>();
        await using var database = fixture.Database.CreateOwnerContext();
        var link = await database.ExternalAccounts.SingleAsync(account => account.ProfileId == id);
        using var scope = host.Services.CreateScope();
        await new ActivateArtist(scope.ServiceProvider.GetRequiredService<IAccountStore>()).ExecuteAsync(link.Issuer, link.Subject, CancellationToken.None);
    }

    private async Task<HttpClient> TechnicalAsync(PhotoIdentityHost host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await fixture.TokenAsync(service: true))["access_token"]!.GetValue<string>());
        return client;
    }

    [Fact]
    public async Task Client_photo_is_private_signed_and_never_published()
    {
        using var host = new PhotoIdentityHost(fixture, media);
        using var client = await SignInAsync(host, "carol");
        var response = await client.PutAsync("/api/v1/profiles/me/photo", Form(Png(900, 600)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var photo = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        var url = photo["photoUrl"]!.GetValue<string>();
        Assert.StartsWith("https://localhost:5173/media/inklink-identity-private/profiles/", url);
        Assert.Contains("X-Amz-Signature=", url);
        Assert.Contains("/128.webp?", photo["photoThumbnailUrl"]!.GetValue<string>());

        using var signed = await media.StorageGetAsync(url);
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        using (var bitmap = SKBitmap.Decode(await signed.Content.ReadAsByteArrayAsync()))
            Assert.Equal((512, 512), (bitmap.Width, bitmap.Height));
        using var unsigned = await media.StorageGetAsync(url[..url.IndexOf('?', StringComparison.Ordinal)]);
        Assert.Equal(HttpStatusCode.Forbidden, unsigned.StatusCode);

        var me = (await client.GetFromJsonAsync<JsonObject>("/api/v1/profiles/me"))!;
        Assert.Contains("X-Amz-Signature=", me["photoUrl"]!.GetValue<string>());
        var id = me["id"]!.GetValue<Guid>();
        using var technical = await TechnicalAsync(host);
        // Sem include=photo, o chamador técnico recebe só o nome; a foto assinada sai apenas quando pedida.
        var nameOnly = (await technical.GetFromJsonAsync<JsonObject>($"/api/v1/internal/profiles/{id}"))!;
        Assert.Null(nameOnly["photoUrl"]);
        Assert.Null(nameOnly["photoThumbnailUrl"]);
        var internalProfile = (await technical.GetFromJsonAsync<JsonObject>($"/api/v1/internal/profiles/{id}?include=photo"))!;
        Assert.Contains("inklink-identity-private", internalProfile["photoUrl"]!.GetValue<string>());

        await using var database = fixture.Database.CreateOwnerContext();
        Assert.Equal(0, await database.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM identity.outbox WHERE \"Payload\"::text LIKE {"%" + id + "%"}").SingleAsync());
        Assert.Equal(0, await database.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM identity.object_cleanups WHERE \"Key\" LIKE {"profiles/" + id.ToString("N") + "%"}").SingleAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/v1/profiles/me/photo")).StatusCode);
        Assert.Null((await client.GetFromJsonAsync<JsonObject>("/api/v1/profiles/me"))!["photoUrl"]);
    }

    [Fact]
    public async Task Artist_photo_is_public_replaced_cleaned_and_published_to_the_catalog_queue()
    {
        var published = new System.Collections.Concurrent.ConcurrentBag<System.Diagnostics.Activity>();
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name == "InkLink.Identity.Events",
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) => System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = published.Add,
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);
        using var host = new PhotoIdentityHost(fixture, media);
        using var client = await SignInAsync(host, "alice");
        await MakeArtistAsync(host, client);
        var id = (await client.GetFromJsonAsync<JsonObject>("/api/v1/profiles/me"))!["id"]!.GetValue<Guid>();

        var first = (await (await client.PutAsync("/api/v1/profiles/me/photo", Form(Png(700, 700)))).Content.ReadFromJsonAsync<JsonObject>())!["photoUrl"]!.GetValue<string>();
        Assert.StartsWith($"https://localhost:5173/media/inklink-identity-media/profiles/{id:N}/", first);
        Assert.DoesNotContain("X-Amz-Signature", first);
        Assert.Equal(HttpStatusCode.OK, (await media.StorageGetAsync(first)).StatusCode);

        var second = (await (await client.PutAsync("/api/v1/profiles/me/photo", Form(Png(800, 800)))).Content.ReadFromJsonAsync<JsonObject>())!["photoUrl"]!.GetValue<string>();
        Assert.NotEqual(first, second);
        Assert.True(await host.Services.GetRequiredService<ObjectCleanupWorker>().RunOnceAsync(default) >= 2);
        Assert.Equal(HttpStatusCode.NotFound, (await media.StorageGetAsync(first)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await media.StorageGetAsync(second)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/v1/profiles/me/photo")).StatusCode);

        // O Outbox publica os três eventos na fila do Catálogo, na ordem das revisões, com rastreamento W3C e métricas.
        var events = new List<JsonObject>();
        var traceParents = new List<string>();
        using var connection = new ConnectionFactory { Uri = new(media.CatalogConsumerUri), AutomaticRecoveryEnabled = false }.CreateConnection();
        using var channel = connection.CreateModel();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var delivery = channel.BasicGet("inklink.catalog.profile-photos.v1", autoAck: true);
            if (delivery is null) { await Task.Delay(300); continue; }
            var envelope = JsonNode.Parse(Encoding.UTF8.GetString(delivery.Body.Span))!.AsObject();
            if (envelope["data"]!["profileId"]!.GetValue<Guid>() == id)
            {
                events.Add(envelope);
                if (delivery.BasicProperties.Headers?.TryGetValue("traceparent", out var header) == true && header is byte[] bytes) traceParents.Add(Encoding.UTF8.GetString(bytes));
            }
            if (events.Count(item => item["data"]!["photoUrl"] is null) == 1 && events.Count >= 3) break;
        }
        var mine = events.OrderBy(item => item["data"]!["revision"]!.GetValue<int>()).TakeLast(3).ToArray();
        Assert.Equal(3, mine.Length);
        Assert.All(mine, item => Assert.Equal("identity.profile-photo-changed.v1", item["type"]!.GetValue<string>()));
        Assert.Equal(first, mine[0]["data"]!["photoUrl"]!.GetValue<string>());
        Assert.Equal(second, mine[1]["data"]!["photoUrl"]!.GetValue<string>());
        Assert.Null(mine[2]["data"]!["photoUrl"]);
        Assert.True(mine[2]["data"]!["revision"]!.GetValue<int>() > mine[1]["data"]!["revision"]!.GetValue<int>());
        Assert.Equal(events.Count, traceParents.Count);
        Assert.All(traceParents, value => Assert.Matches("^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$", value));
        Assert.Contains(published, activity => activity.OperationName == "profile-photo.publish" && traceParents.Contains(activity.Id!));
    }

    [Fact]
    public async Task Invalid_uploads_are_rejected_without_storing_a_photo()
    {
        using var host = new PhotoIdentityHost(fixture, media);
        using var anonymous = host.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsync("/api/v1/profiles/me/photo", Form(Png(600, 600)))).StatusCode);
        using var client = await SignInAsync(host, "bob");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.PutAsJsonAsync("/api/v1/profiles/me/photo", new { image = "x" })).StatusCode);
        foreach (var content in new[] { Form("GIF89a\u0001\u0000"u8.ToArray()), Form(Png(200, 200)), Form(Png(600, 600), "photo") })
        {
            var response = await client.PutAsync("/api/v1/profiles/me/photo", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.NotNull((await response.Content.ReadFromJsonAsync<JsonObject>())!["errors"]!["image"]);
        }
        Assert.Null((await client.GetFromJsonAsync<JsonObject>("/api/v1/profiles/me"))!["photoUrl"]);
    }
}
