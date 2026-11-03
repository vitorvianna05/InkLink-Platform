# Verificação do incremento GW — documentação interativa

Data: 03/11/2026. Situação: concluído no recorte TGW.01–TGW.04. Decisão 0012 e guia development/gateway-documentation.md. Não conclui P07/P09.

## Resultado

Build Release da solução: zero avisos/erros, com --warnaserror; SDK e pacotes anteriores preservados. Nova dependência de UI fixada em Swashbuckle.AspNetCore.SwaggerUI 10.3.1 e lockfiles atualizados nos consumidores. O gerador OpenAPI dos serviços permanece o mesmo. Formatação integral verificada sem alterações e diff --check aprovado.

49 testes finais aprovados, zero falhas/ignorados nas duas rodadas finais: 7 do catálogo/UI e 42 da regressão Identity/Gateway/BFF. A primeira rodada de catálogo teve cinco falhas na expectativa do teste, que procurava a configuração no HTML. Nesta versão da biblioteca a configuração está em index.js; o teste passou a consultar esse recurso e as sete verificações passaram. Nenhum contrato de produção foi relaxado para contornar a expectativa.

Relatórios em artifacts/test-results (ignorados pelo Git):

- gateway-docs-routing-final_net10.0_20261103121039.trx — 7 aprovados.
- gateway-docs-security_net10.0_20261103121253.trx — 42 aprovados.

Comandos:

~~~powershell
dotnet restore InkLink.Platform.slnx --disable-parallel -m:1 -p:NuGetAudit=false
dotnet build InkLink.Platform.slnx --configuration Release --no-restore --warnaserror -m:1 --disable-build-servers
dotnet test src/backend/services/Identity/tests/InkLink.Identity.IntegrationTests/InkLink.Identity.IntegrationTests.csproj --configuration Release --no-build --filter "FullyQualifiedName~GatewayDocumentationTests" --logger "trx;LogFilePrefix=gateway-docs-routing-final" --results-directory artifacts/test-results
dotnet test src/backend/services/Identity/tests/InkLink.Identity.IntegrationTests/InkLink.Identity.IntegrationTests.csproj --configuration Release --no-build --filter "FullyQualifiedName!~GatewayDocumentationTests" --logger "trx;LogFilePrefix=gateway-docs-security" --results-directory artifacts/test-results
dotnet format InkLink.Platform.slnx --no-restore --verify-no-changes
~~~

NuGetAudit desabilitado somente no comando local de restauração para evitar consultas externas, sem modificar a configuração do projeto/CI. INKLINK_TEST_POSTGRES removido apenas durante o comando de integração e restaurado no finally.

## Evidências por tarefa

| Tarefa | Evidência |
| --- | --- |
| TGW.01 | Decisão 0012 e requirements/architecture/plan/tasks registram incremento após P06 e antes de P07; T09.01 permanece consolidação futura. |
| TGW.02 | UI e JS respondem; cinco documentos selecionáveis; servers de documento/caminhos/operações apontam para a origem do Gateway; caminhos internos, versões retiradas e componentes não alcançáveis são filtrados. Desconhecido retorna 404, dependência indisponível retorna 503 explícito e não afeta outro documento. Produção não publica UI, assets ou documentos. |
| TGW.03 | Keycloak HTTPS real emite token por authorization_code com code_verifier e code_challenge S256 para inklink-swagger, sem segredo; CORS aceita a origem registrada; password grant é negado. JWT é validado no Gateway e Identity; GET/PUT autorizados chegam ao serviço real. Regressão de identidade, sessões BFF, CSRF, isolamento, expiração e tokens inválidos passou. |
| TGW.04 | Perfis VS alinhados, guia com dependências/contas/Artist/Idempotency-Key, smoke dos cinco documentos reais e conferência de rotas existentes. |

## Conferência operacional

Initialize-Local.ps1 atualizou User Secrets para aprovar o cliente de documentação; Configure-Keycloak.ps1 sincronizou o cliente público/callbacks/escopos sem apagar usuários ou substituir credenciais existentes.

A porta 5180 já estava em uso na primeira tentativa; o processo existente foi preservado. A conferência foi executada com DLLs Release em portas temporárias: Identity 15180, Catalog 15193, Engagements 15194, Messaging 15195, Scheduling 15196 e Gateway 15191, com destinos de integração substituídos apenas no ambiente dos processos temporários.

Resultados: HTML/index.js do Swagger 200; documentos válidos de Identity (1 caminho), Catalog (15), Engagements v2 (5), Messaging (4) e Scheduling (6), todos com servers=/ e sem caminhos /internal/ no catálogo. Consulta pública de artistas através do Gateway retornou 200; perfil sem token retornou 401.

Todos os hosts temporários foram encerrados em finally. Não foram criados usuários ou dados de demonstração no ambiente local. O teste de login PKCE usou contas e certificado temporários isolados do fixture Keycloak; o certificado não foi instalado no sistema.

## Limites

A conferência da UI verificou HTTP/assets/configuração; o fluxo de login foi exercitado por HTTP contra Keycloak real, não por clique manual em navegador. A ferramenta mantém token em memória para testes de desenvolvimento e não muda a sessão do produto via BFF. Logout do diálogo não encerra SSO do provedor; o guia orienta perfis/janelas independentes ou logout no Keycloak para troca de conta.

A funcionalidade é de Development/Testing. Não se publica produção, não se comprova CI remota e não se habilitam automaticamente origens/callbacks alternativos. Contratos internos continuam sujeitos a políticas técnicas, sem inclusão no catálogo; a rota técnica de Identity já existente não foi alterada. O Gateway não implementa regras de negócio dos serviços.

A regressão desta entrega cobre os componentes modificados de Identidade/Gateway/BFF e o catálogo interativo; não foi reexecutada a suíte completa de todos os contextos. Não houve commit/push.
