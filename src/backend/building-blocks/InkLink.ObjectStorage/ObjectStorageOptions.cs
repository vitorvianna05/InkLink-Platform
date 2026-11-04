using System.Net;

namespace InkLink.ObjectStorage;

/// <summary>
/// Configuração do armazenamento de objetos pela API S3 (decisão 0018). O mesmo adaptador atende
/// Cloudflare R2 em produção e SeaweedFS no ambiente local, mudando apenas a configuração.
/// </summary>
public sealed class ObjectStorageOptions
{
    /// <summary>Endpoint S3 usado pelo serviço (R2: https://&lt;conta&gt;.r2.cloudflarestorage.com).</summary>
    public string ServiceUrl { get; set; } = string.Empty;
    /// <summary>Região de assinatura SigV4 ("auto" no R2).</summary>
    public string Region { get; set; } = "us-east-1";
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;
    /// <summary>Bucket com leitura pública (obras e foto de artista).</summary>
    public string PublicBucket { get; set; } = string.Empty;
    /// <summary>Base HTTPS pela qual o navegador lê os objetos públicos (CDN em produção; /media local).</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;
    /// <summary>Bucket privado, lido apenas por endereço assinado (foto de cliente). Opcional.</summary>
    public string PrivateBucket { get; set; } = string.Empty;
    /// <summary>
    /// Base HTTPS que substitui a origem do endpoint nos endereços assinados, quando o navegador não
    /// alcança o endpoint diretamente (proxy /media local). Vazio: o endereço assinado usa o endpoint.
    /// </summary>
    public string SignedUrlBaseUrl { get; set; } = string.Empty;
    /// <summary>Validade dos endereços assinados de leitura (10 minutos na decisão 0018).</summary>
    public TimeSpan SignedUrlLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Valida a configuração. HTTP só é aceito para endpoint em loopback em ambiente local/teste;
    /// endereços entregues ao navegador são sempre HTTPS.
    /// </summary>
    public void Validate(bool localEnvironment)
    {
        static bool Missing(string value) => string.IsNullOrWhiteSpace(value);
        if (Missing(ServiceUrl) || Missing(Region) || Missing(AccessKeyId) || Missing(SecretAccessKey) || Missing(PublicBucket) || Missing(PublicBaseUrl))
            throw new InvalidOperationException("Object storage requires endpoint, region, credentials, public bucket and public base URL.");
        if (!Uri.TryCreate(ServiceUrl, UriKind.Absolute, out var service) || !string.IsNullOrEmpty(service.UserInfo) || service.Query.Length > 0
            || (service.Scheme != Uri.UriSchemeHttps && !(localEnvironment && service.Scheme == Uri.UriSchemeHttp && IsLoopback(service))))
            throw new InvalidOperationException("Object storage endpoint must be HTTPS; HTTP is accepted only on loopback in local environments.");
        RequireBrowserBase(PublicBaseUrl, "public base URL");
        if (!Missing(SignedUrlBaseUrl)) RequireBrowserBase(SignedUrlBaseUrl, "signed URL base");
        if (!Missing(PrivateBucket) && PrivateBucket == PublicBucket)
            throw new InvalidOperationException("Object storage private bucket must differ from the public bucket.");
        if (SignedUrlLifetime <= TimeSpan.Zero || SignedUrlLifetime > TimeSpan.FromHours(1))
            throw new InvalidOperationException("Object storage signed URL lifetime must be positive and at most one hour.");
    }

    private static void RequireBrowserBase(string value, string name)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new InvalidOperationException($"Object storage {name} must be an absolute HTTPS URL without credentials, query or fragment.");
    }

    private static bool IsLoopback(Uri uri) =>
        uri.IsLoopback || (IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address));
}
