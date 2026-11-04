# Decisão 0018 — Armazenamento e política de imagens

Tarefa T10.15; orienta T10.16–T10.18. **Situação: aprovada pelo responsável em 04/11/2026.** As escolhas da primeira seção foram definidas diretamente pelo responsável; as propostas da segunda seção foram aprovadas sem ajustes.

## Contexto

O portfólio guarda apenas uma URL HTTPS informada pelo artista e seus metadados; Catalog não baixa nem serve arquivos (decisão 0008). O perfil em Identity tem somente o nome de exibição. O responsável pediu upload de imagens no portfólio e uma foto de perfil para cliente e artista.

A arquitetura (seção 6.4) já prevê arquivos de portfólio em armazenamento próprio de mídia, com metadados sob Catalog, e desaconselha separar processos sem autonomia de negócio (seção 4.7). Messaging e Scheduling já validam a participação do solicitante e então obtêm o nome do interlocutor em `GET /api/v1/internal/profiles/{id}` de Identity, com credencial técnica.

## Escolhas aprovadas

- Armazenamento de objetos pela API S3 (`AWSSDK.S3`), isolada na camada Infrastructure de cada serviço dono.
- Produção: Cloudflare R2 (compatível com S3, sem cobrança de saída de dados). Desenvolvimento e integração local: SeaweedFS. MinIO não é usado: entrou em modo de manutenção em 12/2025 e deixou de publicar imagens.
- Portfólio somente por upload; o campo de URL deixa de existir na interface e nos contratos de escrita.
- Uma foto de perfil por conta, para cliente e para artista.
- A foto do cliente é visível aos artistas com quem ele está em contato; a foto do artista é pública.

## Propostas aprovadas

### Serviço dono de cada imagem

- **Obras (tatuagens e flashes): Catalog.** Recebe o arquivo, grava o objeto e mantém os metadados. Não se cria um serviço de mídia.
- **Foto de perfil: Identity.** É um dado da conta, e cada conta tem uma única foto.
  - Conta com a função Artist tem foto pública; conta somente Client tem foto privada.
  - Catalog mantém uma projeção da URL pública da foto do artista para cards e perfil público, atualizada por evento `identity.profile-photo-changed.v1` publicado com Outbox (padrão de P05).
  - Messaging, Engagements e Scheduling continuam validando a participação e passam a receber, no endpoint interno de perfis, a URL da foto do interlocutor. Para foto privada, Identity devolve um endereço assinado de curta duração somente a chamadores técnicos autorizados.

### "Em contato"

Um artista vê a foto do cliente quando existe atendimento entre os dois, em qualquer estado, inclusive encerrado. É a mesma regra que mantém o histórico de conversa acessível depois do encerramento. Terceiros nunca recebem o endereço.

### Envio, formatos e limites

- O envio passa pelo backend (website → BFF → Gateway → serviço dono, `multipart/form-data`), com CSRF e idempotência, sem credenciais do armazenamento no navegador e sem CORS no bucket. Envio direto ao bucket por URL assinada fica para um incremento futuro, se o volume justificar.
- Formatos aceitos: JPEG, PNG e WebP, verificados pelo conteúdo do arquivo (assinatura e decodificação), não pela extensão.
- Tamanho máximo: 10 MB por obra e 5 MB por foto de perfil. Dimensão mínima: 600 px no menor lado para obras e 256 px para fotos.
- Limites de corpo configurados de forma coerente em BFF, Gateway e serviço.

### Processamento

- Toda imagem é decodificada e regravada em WebP no servidor com SkiaSharp (licença MIT). Isso remove metadados EXIF, inclusive localização GPS, e neutraliza conteúdo malicioso embutido.
- Variantes: obras com 1600 px (detalhe) e 480 px (card); fotos com 512 px e 128 px.
- O texto alternativo continua obrigatório nas obras.

### Endereços e entrega

- Chaves de objeto aleatórias (UUIDv7) e imutáveis; trocar uma imagem cria nova chave. Isso permite cache longo sem servir versões antigas.
- Imagens públicas: base pública configurável por ambiente. Em produção, um domínio do R2 com CDN; localmente, a rota `/media` encaminhada ao SeaweedFS pelo mesmo proxy do website, evitando outra origem e outro certificado.
- Foto privada: endereço assinado com validade de 10 minutos, gerado por Identity.

### Remoção e falhas

- Excluir uma obra ou trocar a foto remove os objetos antigos depois da confirmação da transação. A remoção pendente é registrada para nova tentativa, como a Outbox.
- Objetos enviados cuja gravação no banco falhou são removidos por uma varredura de órfãos com mais de 24 horas.
- Falha do armazenamento não cria obra sem imagem nem foto pela metade; a operação inteira falha com erro explícito (503).

### Obras já publicadas por URL

Não há dados de produção, apenas dados sintéticos locais. Novas obras exigem upload. As existentes continuam exibindo a URL antiga, marcada como externa, até que o artista troque a imagem; o Estúdio indica essa pendência. A remoção de `imageUrl` dos contratos de escrita é uma mudança incompatível e segue a política de versões da decisão 0005.

### Fora do recorte

Moderação de conteúdo, recorte manual na interface, vídeos, múltiplas fotos por obra e migração automática de imagens externas.

## Consequências

- Novos componentes de infraestrutura: SeaweedFS no Compose, buckets públicos e privados, credenciais geradas em arquivos ignorados e configuração do R2 para produção.
- Identity passa a publicar evento por Outbox e a assinar endereços. Catalog ganha uma projeção da foto do artista.
- A seção 6.4 de architecture.md e a decisão 0008 foram atualizadas para refletir esta decisão.
