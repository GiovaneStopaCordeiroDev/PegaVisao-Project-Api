# Melhor Envio — conexão e cálculo de frete

Implementado: OAuth da conta da loja, tokens criptografados, renovação, cadastro de
peso/dimensões, cotação no checkout, seleção de entrega e soma do frete ao pedido/Pix.
Compra de etiquetas, postagem e rastreamento automático ficam para outra etapa.

## Ativação do cálculo de frete

A migration `20260921202120_AdicionarFreteAoCheckout` foi aplicada ao banco configurado na API em 21/09/2026.
Para aplicar em outro ambiente, execute na pasta do repositório da API antes de publicar a versão com frete:

```powershell
dotnet ef database update --project PegaVisaoApi/PegaVisaoApi.csproj
```

Ela adiciona medidas opcionais aos produtos existentes, dados da entrega aos pedidos e
`CotacoesFrete` com RLS. Pedidos antigos mantêm seu total e recebem `ValorFrete = 0`.
O backend usa a conexão PostgreSQL própria, não a chave anônima do Supabase.
Publique backend e frontend juntos: o POST de pedido passa a exigir uma cotação.

No painel Admin, edite cada produto e informe peso em kg e altura/largura/comprimento
em cm da unidade embalada. Use medidas reais, incluindo a embalagem. As medidas do
produto valem para todas as suas variações; produtos com embalagens muito diferentes
precisam de cadastros separados nesta versão. Carrinhos com produto sem medidas
retornam uma mensagem e não recebem frete zero como alternativa.

Com a conta Sandbox já conectada, é possível testar a cotação. Para concluir um pedido
com frete Sandbox, configure também o Pix de teste (`MercadoPago__PixTeste=true` e
credenciais de teste adequadas). Não use pagamento real nesse fluxo. Para vendas reais,
configure credenciais Melhor Envio de produção, `MelhorEnvio__Sandbox=false`, autorize
novamente a conta e use o Pix de produção. Tokens são separados por ambiente/aplicativo.

## Funcionamento

- `POST /api/Frete/cotacoes` (JWT): recebe `cep` e `itens` com `variacaoProdutoId` e
  `quantidade`. Peso, dimensões e preços vêm exclusivamente do cadastro no backend.
- Consulta o Melhor Envio por produtos e guarda os serviços disponíveis, usando
  `custom_price` e `custom_delivery_time`. Serviços indisponíveis são descartados.
- A resposta contém `id`, `cepDestino`, `subtotal`, `expiraEm`, `sandbox` e `opcoes`.
  Cada opção contém `servicoId`, `servico`, `transportadora`, `valor` e `prazoDias`.
- O cliente escolhe uma opção; checkout e pagamento mostram frete e total.
- `GET /api/Frete/cotacoes/{id}` (JWT) recupera somente a cotação do usuário autenticado.
- `POST /Pedido` recebe adicionalmente `cotacaoFreteId` e `freteServicoId`. O servidor
  valida dono, CEP, carrinho, preço, medidas, origem, ambiente e validade (15 minutos).
  Valores enviados pelo navegador não determinam o valor cobrado.
- Consumo da cotação e criação do pedido usam uma única transação no banco. Reutilizar
  a mesma cotação é rejeitado; após erro que já criou pedido, confira Meus pedidos.
- O total cobrado via Pix é produtos + frete. A confirmação continua sendo feita pela
  integração Mercado Pago existente. Cotação vencida não cancela um Pix já emitido.
- O pedido preserva serviço, transportadora, preço, prazo e os volumes retornados pelo
  provedor. O prazo exibido começa após a postagem; a cotação não compra uma etiqueta.

## Validação desta etapa

```powershell
dotnet run --project tests/FreteChecks/FreteChecks.csproj
dotnet run --project tests/MelhorEnvioChecks/MelhorEnvioChecks.csproj
dotnet run --project tests/PixFlowChecks/PixFlowChecks.csproj
```

No frontend: `npm run build`, `npm run lint` e
`node --test src/services/freteCheckout.test.js src/services/pixPendente.test.js`.
As verificações automatizadas usam HTTP simulado. Ainda é necessário validar a cotação
real no Sandbox após migration/deploy e testar a reserva concorrente da cotação em
PostgreSQL. Nenhuma etiqueta foi comprada nem pagamento realizado por esses testes.

Roteiro manual: cadastrar medidas reais; montar carrinho; informar CEP; calcular e
selecionar entrega; conferir subtotal + frete no pagamento; gerar Pix de teste; conferir
pedido. Repetir mudando CEP/quantidade ou aguardando 15 minutos: deve exigir nova cotação.
Sem conexão ou sem serviços disponíveis, deve informar o erro e impedir a finalização.

Documentação do provedor: https://docs.melhorenvio.com.br/reference/calculo-de-fretes-por-produtos

## 1. Variáveis do Render

Use as credenciais do aplicativo Sandbox. As opções não são inseridas em appsettings.json
para evitar sobrescrever suas alterações ou guardar segredos no Git.

| Variável | Valor |
|---|---|
| `MelhorEnvio__ClientId` | ID do aplicativo Sandbox |
| `MelhorEnvio__ClientSecret` | Segredo atual do aplicativo Sandbox |
| `MelhorEnvio__Sandbox` | `true` |
| `MelhorEnvio__RedirectUri` | `https://pegavisao-project-api.onrender.com/api/MelhorEnvio/callback` |
| `MelhorEnvio__CepOrigem` | `17013113` |
| `MelhorEnvio__EmailContato` | Seu e-mail de suporte real |
| `MelhorEnvio__TokenEncryptionKey` | Chave aleatória de 32 bytes, codificada em Base64 |

As duas últimas variáveis são novas. O e-mail é usado no User-Agent exigido pelo provedor.
Para criar a chave de criptografia no seu PowerShell e copiá-la sem imprimi-la:

```powershell
$chaveFrete = New-Object byte[] 32
$geradorFrete = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$geradorFrete.GetBytes($chaveFrete)
$geradorFrete.Dispose()
[Convert]::ToBase64String($chaveFrete) | Set-Clipboard
[Array]::Clear($chaveFrete, 0, $chaveFrete.Length)
```

Cole no campo `MelhorEnvio__TokenEncryptionKey` do Render. Mantenha essa mesma chave
entre reinícios/deploys. Não é ClientSecret nem Access Token e não deve ir para o Git.
Se a chave for perdida ou alterada, será necessário reconectar a conta. Regere o ClientSecret
que foi exposto na captura de tela antes de usar a integração.

## 2. Migration (preparada, não aplicada)

`20260921150509_AdicionarConexaoMelhorEnvio` cria somente `MelhorEnvioConexoes`.
Ela guarda tokens criptografados, vencimento e hashes de tentativas de autorização.
A chave primária separa ambiente e aplicativo, por exemplo `sandbox:123`.
RLS fica habilitada sem políticas públicas, bloqueando acesso pela API pública do Supabase.
A conexão PostgreSQL do backend precisa ser do proprietário da tabela ou de um papel
de serviço com acesso apropriado/BYPASSRLS; não use os papéis públicos anon/authenticated.

Na raiz do backend, depois de revisar a connection string do banco de destino:

```powershell
dotnet ef database update --project PegaVisaoApi/PegaVisaoApi.csproj
```

Esse comando aplica migrations pendentes; não foi executado nesta implementação.
Não existe migração automática na inicialização. Sem a tabela, a integração retorna 503
e registra erro; o restante da API não depende dela.

## 3. Publicar e conectar

1. Publique backend e frontend com os arquivos desta implementação, após configurar o
   ambiente e aplicar a migration. Nenhum commit/push/deploy foi feito automaticamente.
2. Entre no PegaVisão como administrador e abra `/admin`.
3. No bloco **Entrega · Melhor Envio**, clique em **Conectar Melhor Envio**.
4. A API autentica o JWT Admin e confirma no banco que esse usuário ainda é administrador.
5. O navegador passa por `/api/MelhorEnvio/iniciar`, recebe um cookie seguro e segue para
   a autorização do Sandbox. Use a conta de teste correspondente, não a conta de produção.
6. Autorize a permissão de cotação (`shipping-calculate`). Nenhuma compra de etiqueta é autorizada.
7. O callback deve mostrar **Melhor Envio conectado com sucesso**.
8. Volte ao painel e clique em **Atualizar status**: deve exibir **Sandbox — Conta conectada**.

O navegador precisa aceitar o cookie HTTPS da API. Não copie o link para outro navegador
no meio do fluxo. Iniciar outra tentativa invalida a anterior. O link e state vencem em
dez minutos e são de uso único; não os envie a terceiros. Não é necessário desativar CORS
nem colocar tokens OAuth no React. O retorno para o painel é manual nesta etapa.

## Endpoints e segurança

| Endpoint | Acesso | Função |
|---|---|---|
| `POST /api/MelhorEnvio/autorizar` | JWT Admin | Emite link temporário para iniciar a autorização |
| `GET /api/MelhorEnvio/iniciar?ticket=...` | Ticket temporário | Vincula navegador e redireciona ao provedor |
| `GET /api/MelhorEnvio/callback` | State + cookie válidos | Troca código e persiste os tokens |
| `GET /api/MelhorEnvio/status` | JWT Admin | Retorna ambiente, conexão e validade; nunca tokens |
| `POST /api/MelhorEnvio/renovar` | JWT Admin | Verifica/renova se estiver próximo de vencer |

State e cookie são aleatórios; o banco armazena somente hashes. O callback rejeita estado
expirado/reutilizado, outro navegador ou administrador que perdeu permissão. AES-GCM protege
tokens com contexto de ambiente/aplicativo e finalidade. Bloqueio transacional PostgreSQL
serializa autorização/renovação, inclusive entre múltiplas instâncias da API.

Foi removido o endpoint público `PUT /api/Auth/resetar-senha-admin`, que redefinia a senha
administrativa para uma constante. Ele permitiria assumir a conta autorizadora. A senha
já existente não foi alterada; se ainda usa a senha fixa antiga, troque-a por um processo
controlado antes de disponibilizar a conexão. Login, cadastro e emissão JWT foram mantidos.

O worker verifica a renovação ao iniciar e a cada 12 horas, renovando quando faltam até
três dias. Futuras cotações devem chamar `ObterAccessTokenAsync`, que usa a mesma proteção.
Se o serviço ficar suspenso por tempo suficiente para o refresh token vencer, ou a permissão
for revogada, reconecte pelo painel. Não há garantia de execução periódica enquanto o Render
estiver suspenso. Uma falha de banco depois que o provedor já trocou um código/token pode
exigir reconexão, pois os dois serviços não compartilham uma transação.

## Verificação e diagnóstico

- Logs de sucesso: `Melhor Envio conectado` e `Melhor Envio tokens renovados`.
- HTTP 400: link/state/cookie inválidos, autorização negada ou tentativa já utilizada.
- HTTP 401/403: login/permissão de administrador.
- HTTP 409: conexão ausente ou grant revogado/expirado; reconecte.
- HTTP 502: recusa/retorno inesperado do provedor; confira ambiente, credenciais e callback.
- HTTP 503: configuração, tabela, criptografia ou disponibilidade; confira o tipo do erro no log.
- Não exponha query strings de callback, códigos, cookies ou tokens em logs/reports públicos.

Testes locais:

```powershell
dotnet run --project tests/MelhorEnvioChecks/MelhorEnvioChecks.csproj
dotnet run --project tests/PixFlowChecks/PixFlowChecks.csproj
```

No frontend: `npm.cmd run build`, `npm.cmd run lint` e
`node --test src/services/pixPendente.test.js`.

As verificações automatizadas usam HTTP simulado e metadados do EF; não substituem o teste
integrado de callback, concorrência e persistência contra PostgreSQL. Nenhuma autorização
real, emissão de etiqueta ou alteração no Supabase foi executada pelo assistente.

Referências: [OAuth e permissões](https://docs.melhorenvio.com.br/reference/fluxo-de-autoriza%C3%A7%C3%A3o),
[troca e renovação de tokens/User-Agent](https://docs.melhorenvio.com.br/reference/solicitacao-do-token),
[ambientes separados](https://docs.melhorenvio.com.br/docs/sandbox).

## Teste temporário de cartão sem frete

Após publicar esta versão, configure `Frete__DesabilitadoParaTeste=true` no Render.
A opção é desligada por padrão e só vale para contas administradoras (IsAdmin no banco).
Entre com essa conta, volte ao checkout e calcule novamente: selecione “Sem frete — teste”,
com valor zero. Nesse modo não há consulta ao Melhor Envio, exigência de medidas nem
contratação de entrega. A cotação continua vinculada ao cliente/carrinho e vence em 15 minutos.
O pagamento continua no ambiente definido pelas credenciais Mercado Pago: credenciais
reais podem gerar cobrança real. O modo não altera nem corrige o checkout externo do cartão.

Ao terminar, defina `Frete__DesabilitadoParaTeste=false` e salve/reimplante. Cotações do modo
anterior passam a ser rejeitadas. Outros clientes continuam com o frete normal durante o teste.
Nenhuma migration adicional é necessária.
