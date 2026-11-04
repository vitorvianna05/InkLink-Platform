using System.Text.RegularExpressions;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace InkLink.ObjectStorage;

public enum ObjectVisibility
{
    /// <summary>Leitura pública pela base configurada (CDN em produção).</summary>
    Public,
    /// <summary>Somente por endereço assinado de curta duração.</summary>
    Private,
}

/// <summary>Operações de objetos usadas pelos serviços donos das imagens (Catalog e Identity).</summary>
public interface IObjectStorage
{
    Task PutAsync(ObjectVisibility visibility, string key, Stream content, string contentType, CancellationToken cancellationToken);
    /// <summary>Remove o objeto. Remover um objeto inexistente não é erro.</summary>
    Task DeleteAsync(ObjectVisibility visibility, string key, CancellationToken cancellationToken);
    Uri GetPublicUrl(string key);
    Task<Uri> CreateSignedReadUrlAsync(string key, CancellationToken cancellationToken);
}

public sealed partial class S3ObjectStorage : IObjectStorage, IDisposable
{
    private readonly ObjectStorageOptions options;
    private readonly AmazonS3Client client;

    public S3ObjectStorage(IOptions<ObjectStorageOptions> options)
    {
        this.options = options.Value;
        client = CreateClient(this.options);
    }

    internal static AmazonS3Client CreateClient(ObjectStorageOptions options) => new(
        new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey),
        new AmazonS3Config
        {
            ServiceURL = options.ServiceUrl,
            AuthenticationRegion = options.Region,
            ForcePathStyle = true,
            // Serviços compatíveis com S3 (R2, SeaweedFS) não exigem as somas de verificação
            // adicionais que o SDK envia por padrão; calcular somente quando a operação requer.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            Timeout = TimeSpan.FromSeconds(30),
            MaxErrorRetry = 2,
        });

    public async Task PutAsync(ObjectVisibility visibility, string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        var request = new PutObjectRequest
        {
            BucketName = Bucket(visibility),
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
        };
        // Chaves são imutáveis (decisão 0018): imagens públicas podem ser armazenadas em cache por longo prazo.
        request.Headers.CacheControl = visibility == ObjectVisibility.Public ? "public, max-age=31536000, immutable" : "private, no-store";
        await client.PutObjectAsync(request, cancellationToken);
    }

    public async Task DeleteAsync(ObjectVisibility visibility, string key, CancellationToken cancellationToken)
    {
        ValidateKey(key);
        await client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = Bucket(visibility), Key = key }, cancellationToken);
    }

    public Uri GetPublicUrl(string key)
    {
        ValidateKey(key);
        return new Uri(options.PublicBaseUrl.TrimEnd('/') + "/" + EscapeKey(key));
    }

    public async Task<Uri> CreateSignedReadUrlAsync(string key, CancellationToken cancellationToken)
    {
        ValidateKey(key);
        cancellationToken.ThrowIfCancellationRequested();
        var service = new Uri(options.ServiceUrl);
        var signed = await client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = Bucket(ObjectVisibility.Private),
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(options.SignedUrlLifetime),
            Protocol = service.Scheme == Uri.UriSchemeHttp ? Protocol.HTTP : Protocol.HTTPS,
        });
        var uri = new Uri(signed);
        if (string.IsNullOrWhiteSpace(options.SignedUrlBaseUrl)) return uri;
        // A assinatura cobre caminho, query e host do endpoint. O proxy local recebe o endereço na base
        // pública, remove o prefixo e encaminha ao endpoint com o mesmo host, preservando a validade.
        return new Uri(options.SignedUrlBaseUrl.TrimEnd('/') + uri.PathAndQuery);
    }

    private string Bucket(ObjectVisibility visibility) => visibility switch
    {
        ObjectVisibility.Public => options.PublicBucket,
        ObjectVisibility.Private when !string.IsNullOrWhiteSpace(options.PrivateBucket) => options.PrivateBucket,
        ObjectVisibility.Private => throw new InvalidOperationException("This service has no private object storage bucket configured."),
        _ => throw new ArgumentOutOfRangeException(nameof(visibility)),
    };

    /// <summary>Chaves geradas pelo serviço: segmentos em minúsculas, dígitos, '-', '_' e '.', sem '..' nem barra inicial.</summary>
    public static void ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 512 || !KeyPattern().IsMatch(key) || key.Split('/').Any(segment => segment is "." or ".."))
            throw new ArgumentException("Object key is invalid.", nameof(key));
    }

    private static string EscapeKey(string key) => string.Join('/', key.Split('/').Select(Uri.EscapeDataString));

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]*(/[a-z0-9][a-z0-9._-]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    public void Dispose() => client.Dispose();
}
