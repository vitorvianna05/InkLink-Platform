# Ambiente integrado local — P09

Decisão [0015](../decisions/0015-p09-backend-validation.md). Pré-requisitos: Windows, PowerShell 7, SDK global.json, ferramentas .NET, Docker com containers Linux e certificado localhost confiável já configurado. Não se aplica a produção. Nenhum script muda a confiança do certificado.

## Preparar e executar

Na raiz do produto:

~~~powershell
dotnet tool restore
dotnet restore InkLink.Platform.slnx --locked-mode
dotnet build InkLink.Platform.slnx --configuration Release --no-restore --warnaserror
./src/backend/scripts/integration/Initialize-Integration.ps1
./src/backend/scripts/integration/Start-Integration.ps1
./src/backend/scripts/integration/Test-Integration.ps1
~~~

Initialize gera credenciais novas preservadas nas reexecuções, certificados exportados, importação do realm e Compose derivado da fonte existente. Inicia dependências e aplica as sete migrações de contexto com owner distinto do runtime; não usa migração automática na inicialização dos serviços. O projeto Compose é inklink-integration, com rede e volumes próprios. Arquivos ficam em artifacts/integration, ignorado pelo Git; não compartilhar esse diretório, pois contém segredos/certificados. Não depende de localStorage ou dados do protótipo.

| Componente | HTTPS/porta local |
| --- | --- |
| Identity | https://localhost:5280 |
| Catalog | https://localhost:5293 |
| Engagements | https://localhost:5294 |
| Messaging | https://localhost:5295 |
| Scheduling | https://localhost:5296 |
| Finance | https://localhost:5297 |
| Gateway / Swagger | https://localhost:5291/swagger |
| BFF | https://localhost:5292/bff/login |
| Keycloak | https://localhost:5289 |
| PostgreSQL: Identity/BFF/Catalog/Engagements/Messaging/Scheduling/Finance | 55329–55335, somente loopback |
| RabbitMQ | 5773, somente loopback |

Todos os oito hosts e as chamadas HTTP entre eles usam o certificado confiável. Bancos/broker são dependências locais em rede/loopback; esta entrega não afirma TLS de produção para PostgreSQL/AMQP. Desenvolvimento permanece nas portas e volumes anteriores. Hosts são publicados individualmente em releases próprias e recebem configuração por ambiente, sem gravar seus segredos no User Secrets de desenvolvimento.

Start verifica se a porta está livre antes de iniciar e só aceita o health check quando PID, criação, assembly e proprietário do listener correspondem ao processo iniciado, conferindo novamente após a resposta HTTP. Falha remove o registro e encerra somente o processo próprio correspondente. Start registra somente processos que inicia. Reexecutar não reinicia um processo próprio ativo; não toma posse de processos existentes. Stop valida PID, criação e assembly antes de encerrar. Se outra aplicação usar uma porta reservada, não a mate para prosseguir; libere a porta conscientemente ou ajuste a configuração do ambiente em conjunto.

## Dados sintéticos e verificação

Test-Integration cria três usuários administrativos com sufixo por execução no realm isolado, vincula perfis e concede Artist pela ferramenta administrativa. O cliente de password grant existe exclusivamente nesse realm gerado para automação; clientes reais permanecem Code/PKCE. O roteiro autentica as duas funções e um terceiro sem autorização, publica portfólio/flash, pesquisa, favorita e solicita atendimento via Gateway.

Interrompe exclusivamente RabbitMQ da integração e verifica Outbox pendente sem ler payload privado. Ao retomar, aguarda conversa, envia pelas duas identidades, verifica acesso negado, encerra e mantém histórico bloqueado. Novo pedido exige reserva para habilitar o novo chat. Uma reserva futura impede encerramento. Aguarda o horário real de término (normalmente até três minutos) antes de concluir a sessão, registra receitas/custos e verifica R$ 4.000 / R$ 940 → R$ 3.060 / 76,5%, clientes, ticket e idempotência.

Cada rodada usa novas contas; dados sintéticos são preservados para diagnóstico e não misturados com o artista de outra execução. Resultados sem tokens ficam em smoke.json e contratos OpenAPI públicos em contracts/. O roteiro não altera relógio ou estados de negócio diretamente. É possível parar após a execução; as evidências permanecem. Não há rotina destrutiva automática de limpeza de volumes.

## Diagnóstico e recuperação

~~~powershell
./src/backend/scripts/integration/Get-IntegrationDiagnostics.ps1
~~~

O diagnóstico mostra health por host, contagens/idade de Outbox pendente e quarentena e contagens de lifecycle por situação, sem corpo de mensagem, payload ou credenciais. Gateway/BFF expõem liveness; os seis serviços verificam sua persistência em readiness. Liveness não deve ser interpretado como saúde de todas as dependências. Logs estruturados dos processos estão no diretório ignorado; pesquisar traceId devolvido em X-Trace-Id ou Problem Details. Não copiar tokens para logs.

Broker indisponível: manter a escrita local/Outbox e recuperar o broker, sem inserir eventos manualmente. Quarentena exige investigar contrato/permissão/dependência antes de reprocessar; não zerar tentativas cegamente. Lifecycle Pending/Closing não é sucesso definitivo; consultar o endpoint autorizado e usar retry somente após corrigir a causa. Guias de [Messaging](messaging.md), [Agenda](scheduling.md) e [ciclo](lifecycle.md) detalham estados e orçamento de falhas.

## Publicação independente e retorno

Com smoke completo e build Release atualizado:

~~~powershell
./src/backend/scripts/integration/Publish-Service.ps1 -Service Finance
./src/backend/scripts/integration/Test-Integration.ps1 -VerifyOnly
~~~

O primeiro comando publica somente Finance, para apenas seu processo e verifica o novo host. Os outros sete PIDs/instantes/assemblies devem permanecer válidos; deployment.json registra o resultado. Se a inicialização falhar, tenta restaurar a release anterior. A verificação posterior autentica novamente as contas existentes, verifica os seis documentos e consulta recebimento, indicadores, bloqueio e isolamento preservados. post-deployment.json registra as verificações. Esta demonstração usa schema e contratos compatíveis; não é zero downtime e não resolve rollback de mudanças destrutivas.

Para rollback explícito de um binário compatível, manter a release anterior, parar somente o serviço pelo helper e iniciá-lo com Start-IntegrationService apontando para a release anterior registrada. Nunca apagar ou reverter bancos como consequência automática de rollback. A política geral de coexistência está nos [contratos](consumer-contracts.md).

## Encerrar

~~~powershell
./src/backend/scripts/integration/Stop-Integration.ps1
# Opcional: parar também as dependências exclusivas, preservando os volumes
./src/backend/scripts/integration/Stop-Integration.ps1 -Dependencies
~~~

Não existe publicação em nuvem ou configuração de produção neste incremento. Os frontends usarão os contratos reais após o aceite técnico de M5; validações visuais, distribuição mobile, carga e coleta/alertas de produção são posteriores.

## Regressão automatizada

Com o ambiente integrado parado para reduzir concorrência de recursos locais:

~~~powershell
./src/backend/scripts/integration/Stop-Integration.ps1 -Dependencies
./src/backend/scripts/Test-Backend.ps1
~~~

O runner restaura dependências bloqueadas, compila Release com warnings como erros e executa cada projeto com prefixo TRX próprio, continuando até registrar todos os projetos. Retorna falha se algum projeto falhar. Com build válido já existente, usar -NoBuild. -Project aceita nomes completos de projetos para reexecução direcionada; isso não equivale a uma nova suíte completa. A CI utiliza o mesmo runner e preserva os relatórios mesmo após falhas.

Coleções dos projetos de integração são serializadas por xunit.runner.json, copiado via Directory.Build.targets, para evitar múltiplos Keycloak/RabbitMQ concorrentes dentro do mesmo projeto. Testes de concorrência de negócio continuam disparando operações simultâneas explicitamente. Essa organização limita pressão de CPU/memória e não amplia timeouts ou altera regras dos serviços. A CI tem orçamento limitado de 90 minutos para o conjunto; a duração real depende do host e das imagens disponíveis.

Os harnesses de RabbitMQ também limitam os schedulers da CLI de provisionamento para evitar concorrência desnecessária de VMs Erlang. Credenciais, permissões, protocolo e testes explícitos de disputa continuam iguais.

Para navegação manual simultânea em ambientes que usam localhost, utilizar perfis de navegador separados, mantendo cookies e sessões de cada ambiente independentes no navegador. A geração do realm é idempotente para a configuração atual; alterações futuras de clientes/escopos em um realm já existente exigem sincronização administrativa específica, sem apagar usuários ou volumes.

### Regressão da inicialização local

No Windows/PowerShell 7, executar `./src/backend/scripts/tests/Test-IntegrationStartup.ps1`. A verificação usa uma porta real temporária para conferir o proprietário do listener e seis cenários controlados: porta ocupada antes da execução, processo encerrado, porta tomada durante a inicialização, troca de proprietário após o health check, encerramento após a resposta e inicialização saudável. Não inicia/para hosts reais nem altera bancos ou manifesto. Falhas na consulta de listeners impedem a inicialização; não são interpretadas como porta livre.
