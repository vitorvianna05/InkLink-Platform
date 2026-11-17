# Decisão 0020 — Cadastro e confirmação de contato

Tarefa T10.22; orienta T10.23 e T10.24 e os aplicativos (T11.03, T12.01 e T13.01). **Situação: aprovada e implementada** (T10.23–T10.25, [verificação P10](../verification/p10.md)). Em 05/11/2026, o responsável aprovou:
- a tela de cadastro do Keycloak;
- e-mail por SMTP e simuladores locais;
- a política dos códigos;
- o aceite obrigatório dos termos, com texto provisório até a definição.

Revisa a [decisão 0007](0007-p02-identity-and-sessions.md) (contas criadas só pela administração) e mantém as decisões [0003](0003-authentication.md) e [0017](0017-p10-web-login.md): nenhuma credencial é digitada no website.

## Contexto

As contas eram criadas no console do Keycloak, e o realm tinha o cadastro desativado. RF22 e RF23 pedem o cadastro pelo próprio usuário, com confirmação de e-mail e celular por código aleatório de 8 dígitos. Artist continua dependente de aprovação administrativa.

## Decisão

- **Tela de cadastro:** a página de cadastro do próprio Keycloak, com o tema InkLink, pede nome, sobrenome, e-mail e senha.
  - O website leva a ela por `GET /bff/register?returnUrl=…`, que inicia o mesmo Authorization Code + PKCE do login com `prompt=create`.
  - Ao concluir, o Keycloak autentica a conta, e o BFF devolve o navegador ao website.
  - A senha nunca passa pelo website, pelo BFF nem pelos serviços. Alternativa rejeitada: formulário próprio que criaria a conta pela API administrativa do Keycloak, porque a senha atravessaria o backend (contraria 0003 e 0017).
- **Realm:**
  - `registrationAllowed` e `registrationEmailAsUsername` ativos, `duplicateEmailsAllowed` inativo.
  - `verifyEmail` inativo: a confirmação é feita pela Identity, por código, e não pelo link do Keycloak.
  - O escopo `inklink.contact` coloca `email` e `email_verified` no access token dos clientes de usuário (website, iOS, Android e documentação).
- **Conta pendente:**
  - Uma conta criada pela página de cadastro chega com `email_verified=false`. No primeiro acesso, a Identity a provisiona como **pendente** com Client.
  - Contas criadas pela administração, pelos scripts e pelos testes já vêm com `emailVerified=true` e nascem ativas, assim como todas as que já existiam.
  - Enquanto pendente, `GET /api/v1/profiles/me` responde 403 `account.confirmation_required`. Como todos os serviços resolvem a conta por essa rota, nenhuma API privada aceita a conta.
  - A ausência da claim (tokens de teste com identidade controlada) não torna a conta pendente.
- **Conclusão do cadastro:** a conta fica ativa quando três condições são cumpridas, em qualquer ordem, por `/api/v1/registration`, que aceita contas pendentes:
  - aceite da versão vigente dos termos;
  - confirmação do e-mail;
  - informação e confirmação do celular.
- **Códigos:**
  - 8 dígitos, gerados com `RandomNumberGenerator`, um por canal e conta.
  - Válidos por 10 minutos e de uso único, com até 5 tentativas.
  - Reenvio só depois de 60 segundos, com no máximo 5 envios por hora por canal. Cada reenvio invalida o código anterior.
  - Guardados só como HMAC-SHA256, com segredo do ambiente, sobre conta, canal, destino e código. Nunca aparecem em logs, URLs nem respostas.
  - O código vale apenas para o destino a que foi enviado: trocar o celular exige um novo código.
- **Canais:**
  - **E-mail:** SMTP, compatível com SES, Resend e SendGrid, entre outros.
  - **SMS:** adaptador com provedor configurável. Em Development e Testing, o simulador entrega o SMS como e-mail ao Mailpit, que roda no Compose local e na P09.
  - O provedor de SMS de produção será configurado quando houver contrato; sem ele, o envio responde 503 e nada é simulado fora dos ambientes locais.
- **Celular:**
  - Formato E.164, com +55 por padrão para números brasileiros de 10 ou 11 dígitos.
  - Único entre contas confirmadas. Confirmar um número já confirmado por outra conta é recusado com a mesma mensagem de código inválido.
- **Termos:**
  - A versão vigente vem da configuração (`Registration:TermsVersion`), e a Identity registra a versão aceita e a data.
  - O texto é provisório (`/termos`) até o responsável fornecer o conteúdo; uma nova versão volta a exigir aceite só quando o responsável decidir, em tarefa própria.
- **Artist:**
  - A conta ativa pode solicitar a função; a Identity registra a solicitação como pendente.
  - A aprovação é feita pela ferramenta administrativa: `list-artist-requests` mostra as pendentes e `grant-artist` aprova e ativa a função.
  - A situação da solicitação aparece no website.

## Consequências

- **Enumeração:** a página do Keycloak informa quando um e-mail já está cadastrado, comportamento do provedor. A mitigação é a proteção contra força bruta do realm. As respostas da Identity (celular e códigos) não revelam outras contas.
- **Segredos:** os simuladores e o segredo dos códigos ficam só em arquivos ignorados ou variáveis do ambiente.
- **Aplicativos:** seguem o mesmo cadastro pelo navegador do sistema, com `prompt=create`, e a mesma API de confirmação.
