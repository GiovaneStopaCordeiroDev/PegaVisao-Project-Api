# Autorização Melhor Envio — primeira etapa

Implementado: conexão OAuth da conta da loja, callback com state e cookie de correlação,
tokens criptografados persistidos no PostgreSQL, renovação e botão no painel Admin.
Ainda não implementado: cadastro de peso/dimensões, cotação no checkout, soma do frete
ao Pix, contratação/compra de etiquetas e rastreamento. O checkout/Pix não foi modificado.

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
