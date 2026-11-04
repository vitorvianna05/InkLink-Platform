# Armazenamento de objetos (imagens)

Implementa a preparação da [decisão 0018](../decisions/0018-p10-images.md) (T10.16). Os serviços donos das imagens usam o building block `InkLink.ObjectStorage`, que fala a API S3. O mesmo adaptador aponta para o SeaweedFS no ambiente local e para o Cloudflare R2 em produção, mudando apenas a configuração. Catalog e Identity passam a usá-lo em T10.17 e T10.18.

## Ambientes

| Ambiente | Endpoint S3 (loopback) | Leitura pelo navegador |
| --- | --- | --- |
| Desenvolvimento | http://127.0.0.1:5188 | https://localhost:5173/media/... |
| Integração P09 | http://127.0.0.1:5288 | https://localhost:5173/media/... (Start-WebIntegration.ps1) |
| Produção | `https://<conta>.r2.cloudflarestorage.com`, região `auto` | domínio público do R2 com CDN |

O SeaweedFS (`chrislusf/seaweedfs:4.48`) roda como serviço `object-storage` no Compose, com volume próprio. O MinIO não é usado: está em modo de manutenção desde 12/2025 e deixou de publicar imagens.

## Buckets e permissões

| Bucket | Leitura | Escrita |
| --- | --- | --- |
| `inklink-catalog-media` | pública (obras) | somente `inklink-catalog` |
| `inklink-identity-media` | pública (foto de artista) | somente `inklink-identity` |
| `inklink-identity-private` | somente por endereço assinado (foto de cliente) | somente `inklink-identity` |

As permissões ficam em `src/backend/scripts/object-storage-identities.json`, um template versionado. Os segredos `OBJECT_STORAGE_CATALOG_SECRET` e `OBJECT_STORAGE_IDENTITY_SECRET` são gerados por `Initialize-Local.ps1` e `Initialize-Integration.ps1` apenas nos arquivos ignorados (`.env.local`, `artifacts/local`, `artifacts/integration`). `configure-object-storage.sh` cria os buckets de forma idempotente e é chamado por `Start-Database.ps1` e `Initialize-Integration.ps1`.

## Executar

**Desenvolvimento**, na raiz: executar `./src/backend/scripts/Initialize-Local.ps1` e depois `./src/backend/scripts/Start-Database.ps1`.
- A inicialização preserva as credenciais existentes, acrescenta as do armazenamento e grava a seção `ObjectStorage` nos User Secrets de Catalog e Identity.
- Ambientes criados antes desta entrega precisam executar novamente os dois scripts, porque o Compose monta `artifacts/local/object-storage.json`.

**Integração P09:**
- `./src/backend/scripts/integration/Initialize-Integration.ps1` sobe o SeaweedFS isolado na porta 5288 e cria os buckets.
- `Start-Integration.ps1` fornece `ObjectStorage__*` a Catalog e Identity.

## Configuração (`ObjectStorage`)

| Chave | Uso |
| --- | --- |
| `ServiceUrl`, `Region` | Endpoint S3 e região de assinatura. HTTP só é aceito em loopback, em Development/Testing |
| `AccessKeyId`, `SecretAccessKey` | Credencial do serviço, sempre via User Secrets ou ambiente |
| `PublicBucket`, `PublicBaseUrl` | Bucket público e base HTTPS que o navegador usa para lê-lo |
| `PrivateBucket` | Bucket privado (somente Identity) |
| `SignedUrlBaseUrl` | Base HTTPS que substitui o endpoint nos endereços assinados quando o navegador não o alcança (proxy `/media` local); vazio em produção |
| `SignedUrlLifetime` | Validade dos endereços assinados; padrão de 10 minutos, máximo de 1 hora |

Chaves de objeto são geradas pelo serviço, em minúsculas e imutáveis. Objetos públicos recebem `Cache-Control: public, max-age=31536000, immutable`; privados, `private, no-store`.

`AddObjectStorageCheck` registra a prontidão: uma sonda que recebe 404 indica credenciais válidas, e um 403 deixa o serviço não pronto.

## Proxy `/media` local

O servidor do website encaminha `/media/<bucket>/<chave>` a `INKLINK_MEDIA_URL`, removendo o prefixo e usando o host do endpoint.
- Os endereços assinados continuam válidos, sem expor outra origem, outro certificado ou CORS.
- Cookies e `Authorization` do website são removidos antes do encaminhamento.

Em produção, as imagens são lidas diretamente do domínio público do R2.

## Produção (R2)

1. Criar os três buckets.
2. Habilitar domínio público apenas para os dois buckets `-media`.
3. Emitir um token de API por serviço, restrito aos buckets dele.
4. Configurar `ServiceUrl` com o endpoint da conta, `Region=auto`, `PublicBaseUrl` com o domínio público e `SignedUrlBaseUrl` vazio.

Credenciais nunca vão para o repositório nem para o frontend.
