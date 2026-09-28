# Cupons

## Painel e compra
A seção **Cupons** permite criar, editar, ativar/desativar e excluir logicamente códigos. Códigos são únicos, inclusive após exclusão. O cliente informa um código no resumo do pagamento; não há catálogo público de cupons.

- Tipos: Percentual ou Fixo (reais), com início/vencimento, mínimo, teto e limites opcionais.
- Todos os produtos ou lista de produtos (todas as suas variações). No cupom restrito, mínimo e desconto usam apenas o subtotal participante.
- Valor fixo é aplicado uma vez; desconto nunca ultrapassa o subtotal participante e não inclui frete. Total de pagamento deve ser positivo.
- Pedidos pendentes reservam uso. Cancelamento e expiração confirmada pelo fluxo de conciliação liberam uso e estoque. O relógio local sozinho não libera uma cobrança ainda aberta.
- Alterar/excluir cupom não muda valores já gravados em pedidos.
- Datas no painel: Brasília; API: UTC.

## API
CRUD administrativo em `/api/cupons` requer papel Admin. DTO acrescenta `todosProdutos` (padrão true) e `produtoIds` (obrigatórios para restritos). Produtos inexistentes são rejeitados.
`POST /api/cupons/validar` requer autenticação e recebe código e itens (`variacaoProdutoId`, `quantidade`). Preços vêm do banco.
Criação de pedido recebe `cupomCodigo` e repete validação sob bloqueio transacional, com reserva de estoque. Preview não reserva usos.
Pedido retorna `cupomCodigo`, `valorDesconto`, `subtotalProdutos`, `valorFrete` e `valorTotal`.

## Migration e publicação
Migration: `20260928185000_RestringirCuponsPorProduto`. Acrescenta `TodosProdutos=true` para cupons existentes e tabela associativa `CupomProduto`; não remove dados.
Publicar API antes do frontend.
No Render, `Database__AplicarMigrations=true` habilita EF migrations antes do servidor e dos workers, usando a conexão já configurada. Advisory lock PostgreSQL serializa inicializações concorrentes. Erros impedem a inicialização da nova versão.
Após confirmar a migration e o deploy, pode-se definir essa variável como false para que futuras migrations sejam aplicadas deliberadamente.
Alternativa local autorizada: `dotnet ef database update --project PegaVisaoApi`, com conexão fornecida por ambiente, nunca versionada.
Rollback: manter schema aditivo e retornar ao aplicativo anterior; não executar Down em produção.

## Testes
`CUPONS_TEST_CONNECTION` deve apontar para PostgreSQL 127.0.0.1 e banco base `estoque_tests`.
`dotnet run --project tests/CuponsChecks` cria banco descartável e valida CRUD, abrangência, mínimo, teto, datas, limites, concorrência real, estoque, histórico e payload Pix/cartão sem chamadas externas.
`ESTOQUE_TEST_CONNECTION` usa o mesmo formato para `tests/EstoqueChecks`.
Frontend: `node --test src/services/*.test.js`, build, lint; testes browser em `tests/browser/cupons-review.mjs` com API simulada, incluindo desktop/celular.

