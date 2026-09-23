# Avaliações de produtos

Cada cliente pode dar uma nota inteira de 1 a 5 por produto e atualizar essa nota.
A API confere a identidade do token e a existência de um item daquele produto em
pedido do cliente com status Pago, Enviado ou Entregue. Pedidos pendentes ou
cancelados não habilitam a avaliação. A regra vale para todas as variações do produto.

- `GET /Produto/{produtoId}/avaliacoes`: média e quantidade públicas, sem dados pessoais.
- `GET /Produto/{produtoId}/avaliacoes/minha`: nota do cliente e permissão para avaliar; exige autenticação.
- `PUT /Produto/{produtoId}/avaliacoes/minha`: salva `{ "nota": 5 }`; exige autenticação e compra elegível.

O banco possui restrição de nota entre 1 e 5 e índice único por produto/cliente.
O upsert PostgreSQL evita duplicação em requisições concorrentes.

## Publicação

A migration `AdicionarAvaliacoesProdutos` precisa ser aplicada no banco do ambiente
antes de disponibilizar a nova interface. Na raiz deste repositório, com a conexão
do ambiente configurada de forma segura:

```powershell
dotnet ef database update --project PegaVisaoApi --startup-project PegaVisaoApi
```

Publicar também a API e o frontend. A aplicação não executa migrations automaticamente.
A implementação foi validada em PostgreSQL temporário local. Em 23/09/2026,
a migration foi aplicada no banco configurado, após autorização do responsável.

## Testes

`tests/AvaliacaoChecks` exige `AVALIACAO_TEST_CONNECTION` apontando para
`Host=127.0.0.1` e `Database=estoque_tests`, com um usuário local capaz de criar bancos.
O programa cria e remove seu próprio banco temporário e aplica todas as migrations.

```powershell
dotnet run --project tests/AvaliacaoChecks
```

Verifica notas inválidas, identidade ausente, status do pedido, produto e comprador
corretos, atualização da nota, resumo público, privacidade e gravações concorrentes.

## Resumo do carrinho

O frontend detalha preço unitário, quantidade e total de cada item, preservando o
subtotal, o frete e o total geral. Não foi criada uma regra de desconto promocional:
o modelo atual de produto possui apenas o preço normal.
