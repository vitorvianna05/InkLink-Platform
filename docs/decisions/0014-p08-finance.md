# Decisão 0014 — Gestão financeira, sessões e dashboard

Tarefas T08.01–T08.06. Políticas iniciais aprovadas pelo responsável em 03/11/2026.

## Lançamentos

Somente Artist registra seus lançamentos manuais em BRL. Receipt exige atendimento do qual ele é o artista; permite parcelas independentes e não limita automaticamente o somatório ao valor da proposta. Cost pode ser geral ou vinculado a atendimento próprio. Descrição obrigatória de 1–4000 caracteres; valor positivo até 9.999.999,99, com duas casas decimais; data efetiva não futura no fuso do artista. IDs são UUIDv7.

Lançamentos são imutáveis e não possuem edição/exclusão por API. Estorno é outro lançamento, integral e único por original, com motivo obrigatório, mesma espécie, valor, atendimento e data efetiva. Seu sinal é negativo nos indicadores. É uma correção gerencial que recompõe o período original; não representa devolução bancária nem fluxo de pagamento. Um estorno não pode ser estornado. Novo lançamento correto pode ser registrado após a correção.

Criação e estorno exigem Idempotency-Key por artista. Repetição normalizada recupera o registro original mesmo com fonte operacional temporariamente indisponível; reutilização divergente gera 409. Unicidade de chave e de estorno protege concorrência, com bloqueio local do lançamento original. Infraestrutura persiste; Application coordena contexto e Domain protege entradas e imutabilidade.

Finance tem banco/schema/credenciais/migrações próprios, sem tabelas ou implementações de outros serviços. O ledger não publica fatos externos neste incremento: não se adiciona Outbox sem uma integração de escrita que a exija.

## Sessões

Scheduling mantém SessionCompletion vinculada a uma reserva. Só o Artist proprietário pode concluir após o término previsto, informando completedAt com offset, entre o fim reservado e agora. O valor da sessão é positivo e usa o valor de referência da reserva quando omitido. Conclusão repetida com mesmo instante/valor retorna o resultado; mudança gera 409. Correção de conclusão será um incremento próprio.

Reserva confirmada e sessão concluída são conceitos distintos. O recurso de reserva conserva seu contrato Confirmed; o recurso completion informa execução/valor. Concluir não libera retroativamente a ocupação, não encerra chat, não cria recebimento e não altera a proposta.

## Indicadores e períodos

Períodos locais [from,to), de 1 a 366 dias. Scheduling é origem do fuso IANA do artista (padrão America/Sao_Paulo), reservas e sessões. Finance registra a data efetiva escolhida e seu fuso de referência; a data já registrada não é reinterpretada se o calendário mudar posteriormente.

- Receitas/custos: soma assinada por data efetiva, incluindo correções por estorno na data original.
- Lucro estimado: receitas menos custos. Margem: lucro/receitas × 100, zero sem receitas.
- Sessões e clientes atendidos: conclusão no período local, com clientes distintos.
- Ticket médio: valores registrados das sessões concluídas / quantidade de sessões, zero sem sessões.
- Reservas/valor agendado: reservas do período local ainda sem conclusão, incluindo horários passados cuja execução não foi registrada. Soma das referências preservadas em cada reserva, não soma de recebimentos ou de propostas distintas.
- Conversas ativas: quantidade atual de conversas com escrita permitida em Messaging, independente do período financeiro. Espera por agendamento e histórico bloqueado não entram nessa quantidade.

Múltiplas reservas podem carregar a mesma referência da proposta: não se presume rateio automático. Valor agendado é uma referência gerencial por reserva, não saldo a cobrar. Valor efetivo da sessão é informado na conclusão e pode diferir.

Valores e médias são decimais; margens/ticket arredondam para duas casas, afastando do zero no ponto médio. Lucro negativo é permitido. Moeda sempre BRL.

## Composição e segurança

Finance compõe o dashboard por contratos síncronos de Scheduling e Messaging e verifica atendimento por API v2 de Engagements. Usa o access token delegado do artista, sem credencial técnica privilegiada ou impersonação. Cada origem verifica seu próprio recurso. O serviço não compartilha entidades nem bancos alheios.

Os resumos retornam identidade do artista e instante de observação. Falha, 2xx inesperado ou contrato inválido não viram zero: a consulta retorna indisponibilidade explícita. Os instantes de observação são distintos; não há afirmação de snapshot global entre bancos.

Não há projeção operacional nova em Finance. Reconstruir a resposta significa consultar novamente as fontes oficiais, sem gravar lançamentos ou reaplicar eventos. Autoridade e autorização críticas continuam em seus contextos. Repetição de conclusão e estorno não duplica efeitos.

Gateway publica /api/v1/finance e inclui o OpenAPI no catálogo interativo em Development/Testing. BFF já encaminha esse namespace protegido; produção mantém a política de documentação existente. Program.cs usa extensões por responsabilidade. Fontes externas têm timeout e não seguem redirects nem recebem cookies.

## Limites

Sem processamento de pagamento, cobrança, upload de comprovantes, integração bancária, competência contábil/fiscal, estorno parcial, edição de sessão concluída ou cancelamento/reagendamento. Finanças não cria serviço de dashboard separado e não antecipa os frontends. P09 continua responsável pelo aceite integrado do backend.

### Revisão P08 — contratos e limites civis de período

Respostas operacionais precisam conter todos os campos obrigatórios, inclusive contadores e valores. Ausência ou null não equivale a zero; Finance rejeita o contrato com 503 e code finance.source_unavailable. Um resumo completo com contadores explicitamente zerados permanece válido. A exigência fica no adaptador de integração, sem introduzir serialização em Domain/Application.

Períodos por data usam o primeiro instante existente do dia no fuso do artista. Meia-noite inexistente avança até o primeiro minuto válido, conforme a precisão da Agenda; meia-noite repetida utiliza sua primeira ocorrência em UTC. A regra também se aplica ao limite exclusivo, mantendo períodos adjacentes sem lacunas ou sobreposição. Não altera a rejeição de horários de reserva ambíguos/inexistentes.
