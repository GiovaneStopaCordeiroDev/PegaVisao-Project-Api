# Estoque: reserva e baixa

## Comportamento
- Estoque é a quantidade física; EstoqueReservado são as unidades aguardando pagamento.
- EstoqueDisponivel = Estoque - EstoqueReservado. O catálogo e a página do produto usam esse valor.
- Adicionar ao carrinho não reserva. A criação do pedido reserva na mesma transação que consome a cotação.
- Quantidades repetidas da mesma variação são agrupadas. A reserva usa UPDATE condicional no PostgreSQL; falta em qualquer item desfaz a transação inteira.
- Pagamento confirmado pela API autenticada baixa a quantidade física e a reserva, junto com o status Pago.
- Cancelamento/expiração confirmados da order liberam a reserva. Relógio local, fechamento da página, timeout ou tentativa de cartão rejeitada não liberam estoque.
- Webhook e conciliador usam o mesmo serviço e bloqueiam a linha do pedido. Repetições não descontam/liberam novamente.
- O conciliador consulta reservas conhecidas periodicamente (lease de 2 minutos). Indisponibilidade do provedor mantém a reserva e gera log.
- Reembolsos/devoluções não repõem estoque automaticamente: depende do retorno físico da mercadoria.

## Compatibilidade e operação
- Migration AdicionarReservaEstoque cria os campos e restrições. Aplicá-la ANTES de publicar esta versão da API.
- Pedidos anteriores recebem EstadoEstoque=Legado e NÃO baixam estoque retroativamente. Conferir manualmente pedidos antigos ainda pendentes antes da ativação.
- O cadastro continua recebendo estoque físico. Não permite quantidade inferior à reservada; alterações concorrentes retornam conflito.
- Variações com histórico não podem ser removidas. Pedidos não podem ter itens alterados ou ser apagados fisicamente após criação; exclusão da lista de cancelados continua disponível.
- Em timeout na criação sem Order ID, a reserva permanece retida por segurança. Conferir o pedido no Mercado Pago; não liberar só por ausência de ID local.
- Para recuperar uma order conhecida: POST /api/MercadoPago/conciliar-estoque/{pedidoId}, JWT Admin, body {"orderId":"ORD..."}. O backend consulta o provedor e valida referência e valor antes de vincular/conciliar.
- Reservas sem cobrança identificável exigem análise operacional; não existe liberação automática cega. Investigar logs de "sem ID do provedor".
- HTTP 409 na criação indica estoque insuficiente (não inicia cobrança).
- Nenhuma mudança foi aplicada ao banco de produção por estes testes.

## Validação
Executar:
    dotnet run --project tests/EstoqueChecks
    dotnet run --project tests/PixFlowChecks
    dotnet run --project tests/FreteChecks

Para executar também os cenários de concorrência real:
- Configurar ESTOQUE_TEST_CONNECTION com PostgreSQL localhost/127.0.0.1, banco estoque_tests e usuário de testes com CREATEDB.
- O teste cria um banco aleatório estoque_test_<guid>, aplica todas as migrations e o remove ao terminar.
- Nunca usar credenciais de produção. Sem essa variável, o programa informa explicitamente que a integração não foi executada.
- Cobertura: última unidade, notificações duplicadas/concorrentes, rollback parcial, reserva pendente, confirmação/cancelamento, valor divergente, edição administrativa concorrente e compatibilidade legada.

Referência do cancelamento do provedor:
https://www.mercadopago.com.br/developers/pt/reference/online-payments/checkout-pro/cancel-order/post

## Prazo de pagamento e galeria
- Novos pedidos recebem PagamentoExpiraEm em UTC ao iniciar a criação do pagamento (15 minutos).
- O Checkout Pro recebe expiration_time=PT15M.
- O Pix recebe expiration_time=PT30M, mínimo documentado pelo Mercado Pago. Aos 15 minutos, o conciliador solicita cancelamento se a order estiver created/action_required e consulta o resultado.
- O cancelamento acontece no próximo ciclo do conciliador, não exatamente no segundo zero. Falhas de rede mantêm a reserva para nova tentativa. Processing/approved/processed não são cancelados pelo prazo.
- O contador usa o prazo persistido e a hora do servidor. Em zero, oculta o QR Code e aguarda conciliação; não confirma cancelamento pelo relógio do navegador.
- A lista de pedidos atualiza status enquanto houver pedidos pendentes.
- Pedidos antigos ficam com prazo nulo, sem cancelamento retroativo.
- ImagemPrincipal continua obrigatória; ImagemSecundaria e ImagemTerciaria são opcionais. Cadastro/edição aceitam URLs e a página mostra miniaturas das imagens cadastradas.
- Aplicar a migration AdicionarPrazoPagamentoEImagensProduto antes do deploy. Ainda não aplicada em produção nesta alteração.
- Referências: https://www.mercadopago.com.br/developers/pt/docs/checkout-api-orders/payment-integration/pix e https://www.mercadopago.com.br/developers/pt/docs/checkout-pro-orders/additional-settings/define-order-validity

## Parcelamento dos produtos
- Confirmado pela loja: até 12 parcelas sem juros. Política de exibição adotada: parcela mínima de R$ 5, reduzindo a quantidade para produtos baratos.
- ReadProdutoDto retorna parcelamento.quantidade e parcelamento.valorParcela, calculados no backend; não há taxas do cartão estimadas pelo frontend.
- Checkout Pro recebe o mesmo limite calculado pelo total (produtos + frete), com installments_cost=seller e interest_free no intervalo de 2 ao limite.
- Isso coloca o custo do parcelamento sem juros na loja. Conferir a habilitação e as taxas da conta Mercado Pago antes de publicar.
- Valores da página são simulação do produto, sem frete; a aprovação e disponibilidade final são confirmadas no checkout. Ajustes de centavos podem ocorrer.
- Referência: https://www.mercadopago.com.br/developers/pt/reference/online-payments/checkout-pro/create-order/post
