# Validação do Pix — PegaVisão

## Diagnóstico e contrato

O código cria o Pix com `POST /v1/orders`. A resposta possui `id` da order (`ORD...`) e `transactions.payments[0].id` da transação (`PAY...`). Antes, apenas o segundo era salvo e depois enviado a `/v1/payments/{id}`. O webhook também ignorava o tema `order` e nunca atribuía `Pedido.Status = Pago`.

Agora salvamos ambos os IDs, consultamos `GET /v1/orders/{ORD...}` e usamos `external_reference` da resposta autenticada para localizar o pedido, conferindo IDs existentes e valor. Isso também permite recuperar pedidos antigos quando a notificação correta for reenviada. Não extraímos IDs de URLs de boleto/ticket nem presumimos que `reference_id` seja um ID de Payments.

Exemplo reduzido do contrato documentado (não é uma captura da sua cobrança):

```json
{
  "id": "ORD...",
  "external_reference": "22",
  "total_amount": "50.00",
  "status": "action_required",
  "status_detail": "waiting_transfer",
  "transactions": {
    "payments": [{
      "id": "PAY...",
      "amount": "50.00",
      "status": "action_required",
      "status_detail": "waiting_transfer",
      "payment_method": {
        "id": "pix",
        "qr_code": "...",
        "qr_code_base64": "..."
      }
    }]
  }
}
```

Orders confirma com `processed` / `accredited`, tanto na order quanto na transação. O banco mantém `MercadoPagoStatus = processed`; o status de negócio passa para `Pago`. Não se grava `approved` artificialmente. A consulta numérica de Payments continua separada e reconhece `approved`. [Referência oficial da consulta](https://www.mercadopago.com.br/developers/pt/reference/online-payments/checkout-api/get-order/get).

## Configuração e teste integrado

1. No backend, configure `MercadoPago:AccessToken` pelo mecanismo já usado. Por variável de ambiente, o nome é `MercadoPago__AccessToken`. Não inclua credenciais em commits.
2. Para sandbox, use as credenciais de **teste da aplicação Orders** e `MercadoPago__PixTeste=true`. Essa opção envia os dados predefinidos `test_user_br@testuser.com` e `APRO`. A documentação informa que o Pix começa pendente e o pagamento de teste é aprovado automaticamente; não pague esse QR com dinheiro real. [Teste oficial de Pix](https://www.mercadopago.com.br/developers/pt/docs/checkout-api-orders/integration-test/pix).
3. Em Suas integrações, configure o evento **Order (Mercado Pago)** e a URL HTTPS pública `https://SEU-BACKEND/api/MercadoPago/webhook`, no ambiente correspondente. `localhost` não é acessível pelo provedor. Para desenvolvimento, use uma URL pública de túnel que encaminhe à API local. [Configuração oficial](https://www.mercadopago.com.br/developers/pt/docs/checkout-api-orders/notifications).
4. Inicie backend e frontend. Faça login, adicione produtos, finalize endereço e escolha Pix. Observe a resposta de `POST /Pedido`: IDs `ORD...` e `PAY...`, QR Code e inicialmente `Pendente` (a confirmação automática de teste pode chegar muito rápido).
5. Confirme que o carrinho continua no localStorage enquanto aguarda. A página consulta `GET /Pedido/{id}` a cada cinco segundos, sem requisições sobrepostas. O endpoint exige JWT e só retorna pedido do próprio usuário.
6. Observe o recebimento de `type: order`, `data.id: ORD...` e a consulta autenticada à API. Após a confirmação, a tela deve ir para `/pedidos` e limpar o carrinho correspondente. Se você sair da tela, abrir Meus pedidos posteriormente também verifica a confirmação. Carrinho ou endereço alterados durante a espera são preservados.
7. Reenvie a mesma notificação pelo painel do Mercado Pago. A primeira transição registra `transicaoPago True`; as seguintes, `False`. Todas as entregas processadas devem receber 200. Confira que o estoque permaneceu inalterado.
8. Para um Pix real, use credenciais de produção, `MercadoPago__PixTeste=false` (padrão), dados reais do cliente e webhook do ambiente de produção. Gere uma nova cobrança de pequeno valor e pague-a pelo aplicativo bancário, conferindo destinatário e valor. Repita as verificações anteriores. O código não marca como pago por geração do QR ou retorno de navegação.

No sandbox, teste também um pedido de outro usuário em `GET /Pedido/{id}`: deve retornar 404. Um POST manual contendo `status: processed` para uma order ainda pendente não deve confirmar, pois o corpo não é fonte do status. Para falha transitória, indisponibilize a consulta ao provedor no ambiente de teste: o webhook deve responder 503; restaure e reenvie o evento.

## Logs e Supabase

Observe `MP webhook recebido`, `MP confirmado na API`, `transicaoPago`, `MP divergência de vínculo ou valor` e `MP falha ao processar ... solicitar reenvio`. Não há log do body completo, token ou QR Code no webhook.

Na tabela `Pedidos`, confira:

| Campo | Ao criar | Após confirmação Orders |
|---|---|---|
| `Status` | `0` (Pendente) | `4` (Pago) |
| `MercadoPagoOrderId` | `ORD...` | mesmo ID |
| `MercadoPagoPaymentId` | `PAY...` | mesmo ID |
| `MercadoPagoStatus` | `action_required` | `processed` |
| `ValorTotal` | calculado no backend | igual ao total confirmado |
| `PixQrCode`, `PixQrCodeBase64` | dados retornados | preservados |

O enum é inteiro no PostgreSQL; a API o apresenta como texto. Confira também `UsuarioId` e `ItemPedidos`. A ausência de QR Code não impede mais que os IDs sejam persistidos.

Consulta de diagnóstico, somente para Admin:

```text
GET /api/MercadoPago/teste-pagamento/22
Authorization: Bearer SEU_JWT_ADMIN
```

Esse GET consulta o provedor e retorna status/detalhe/confirmação, sem alterar o banco. Se um pedido antigo tiver apenas `PAY...`, reenvie a notificação `order` correspondente ou recupere o `ORD...` da resposta original verificada. Não invente um ORD a partir de PAY. A associação automática exige mesmo valor, referência e Payment ID.

## Idempotência e limites

Webhooks concorrentes usam transação e `SELECT ... FOR UPDATE`. Somente `Pendente -> Pago` representa uma transição nova. Uma futura baixa de estoque deve entrar no bloco indicado, na mesma transação; chamadas externas exigirão outro mecanismo de entrega confiável. Hoje existe cadastro de estoque, mas não existe baixa por pagamento; nenhuma baixa foi adicionada.

Eventos alheios à integração e divergências permanentes recebem 200 com log; falhas de consulta, timeout, banco e resposta inesperada recebem 503 para reenvio. Payloads inválidos recebem 400. O 404 do provedor também recebe 503, pois pode ser atraso de visibilidade. Erros persistentes exigem intervenção pelo log; não há fila durável nem limite local de tentativas. A API é consultada antes do 200, pois não existe processamento persistido em segundo plano.

O endpoint preserva o acesso anônimo necessário ao provedor e confirma pela API com Access Token. Não foi adicionada validação HMAC de `x-signature`; autenticar a origem da notificação é uma proteção adicional ainda pendente. O status do body nunca é confiável.

Pedidos cancelados que recebam pagamento geram aviso para conciliação e não são convertidos silenciosamente para Pago. Reembolso/expiração e retomada visual do QR após recarregar a página não foram implementados. O marcador local acompanha o último Pix deste navegador; múltiplos checkouts simultâneos exigem evolução específica.

## Migration e verificações locais

Não é necessária migration nova: `MercadoPagoOrderId` já consta da migration `20260920132422_AdicionarDadosMercadoPagoPedido`, e o enum continua sendo inteiro. Nenhum comando de alteração de banco foi executado. Se o ambiente ainda não recebeu as migrations existentes, revise-as antes de executar, na raiz do backend:

```powershell
dotnet ef database update --project PegaVisaoApi/PegaVisaoApi.csproj
```

Verificações executadas: build .NET, 16 verificações com HTTP simulado, build Vite, lint e quatro testes Node. Permanecem avisos preexistentes de nulabilidade/.NET, de lint e o aviso do restore sobre a versão do AutoMapper. Não houve pagamento real, acesso ao Supabase ou teste de concorrência contra PostgreSQL nesta execução.

```powershell
# Na raiz do backend:
dotnet run --project tests/PixFlowChecks/PixFlowChecks.csproj
# Na raiz do frontend:
node --test src/services/pixPendente.test.js
npm.cmd run build
npm.cmd run lint
```

## Arquivos alterados ou adicionados

- Backend `Controllers/MercadoPagoController.cs`: Orders, confirmação, vínculo, transação, logs, respostas HTTP e diagnóstico Admin.
- Backend `Controllers/PedidoController.cs`: persistência de ambos os IDs, consulta do próprio pedido e cancelamento condicional.
- Backend `Services/MercadoPagoService.cs`: consulta separada Orders/Payments, timeout, configuração do pagador e chave de configuração do token corrigida no método de cartão.
- Backend `Services/EstadoPagamentoMercadoPago.cs`: interpretação e regras de confirmação das respostas.
- Backend `Models/Pedido.cs`: `Pago = 4`, mantendo valores existentes.
- Backend `tests/PixFlowChecks/PixFlowChecks.csproj` e `Program.cs`: verificações sem cobranças nem banco.
- Backend `PIX-VALIDACAO.md`: este roteiro.
- Frontend `src/pages/pagamentos/pagamentos.jsx`: consulta periódica e preservação do carrinho ao criar Pix.
- Frontend `src/pages/pedidos/pedidos.jsx`: limpeza após confirmação autenticada e apresentação de Pago.
- Frontend `src/services/pixPendente.js` e `pixPendente.test.js`: correlação local e testes da limpeza.

Alterações anteriores do usuário em outros arquivos foram preservadas. DTOs, autenticação JWT, CSS e migrations não foram alterados.
