# Login Google e recuperação de senha

## Configuração

Configure os valores secretos nas variáveis do Render; não no Git.

| Variável no Render | Valor |
| --- | --- |
| `Google__ClientId` | Client ID OAuth do tipo Aplicativo Web |
| `Auth__FrontendUrl` | `https://pegavisao.vercel.app` (origem fixa confiável) |
| `Email__Provedor` | `Resend` (padrão via HTTPS) ou `Smtp` |
| `Email__ApiKey` | Chave Resend, somente backend |
| `Email__Host` | Servidor SMTP do serviço de e-mail |
| `Email__Porta` | `587` (STARTTLS obrigatório) ou `465` (TLS) |
| `Email__Usuario` | Usuário SMTP |
| `Email__Senha` | Credencial SMTP/senha de aplicativo do provedor |
| `Email__Remetente` | E-mail remetente autorizado pelo provedor |

No Render gratuito, as portas SMTP 25/465/587 são bloqueadas. Use Resend via HTTPS: configure Provedor, ApiKey e Remetente autorizado de um domínio verificado; Host/Porta/Usuario/Senha são usados somente quando Provedor=Smtp. O domínio de testes do Resend limita os destinatários, não serve para todos os clientes. Fontes: https://render.com/docs/free e https://resend.com/docs/api-reference/emails/send-email

No frontend/Vercel, configure `VITE_GOOGLE_CLIENT_ID` com o mesmo Client ID e refaça o build. Localmente use `.env.local` (ignorado no Git).
Não é necessário Client Secret neste fluxo. Nunca colocar segredos em variáveis `VITE_*`.

No Google Cloud, configure a tela de consentimento/Google Auth Platform e crie cliente OAuth do tipo Web, com origens JavaScript autorizadas `https://pegavisao.vercel.app` e `http://localhost:5173`. O botão usa popup e callback JavaScript; não há rota de redirecionamento OAuth no backend. Se o aplicativo estiver em testes, habilite os usuários de teste necessários no Google.

Documentação oficial: https://developers.google.com/identity/gsi/web/guides/get-google-api-clientid e https://developers.google.com/identity/gsi/web/guides/verify-google-id-token

## Publicação

1. Aplicar `dotnet ef database update` no projeto da API **antes** do deploy do código, usando a conexão correta de produção. A migration adiciona campos aos usuários e não remove dados.
2. Configurar o envio de e-mail e o Client ID. Publicar API e frontend.
3. Testar recuperação em uma conta sua: recebimento, link, senha nova e rejeição da senha antiga. Testar Google em conta nova e vínculo com conta existente.

Sem configuração de e-mail, recuperação retorna 503 com mensagem amigável; sem Client ID no frontend, botão Google fica indisponível. Builds e testes locais não substituem esses testes reais de integração.

## Comportamento e segurança

- `/api/Auth/esqueci-senha`: resposta genérica para e-mails cadastrados ou inexistentes; intervalo de 2 minutos entre envios por conta. Falhas SMTP são registradas somente por tipo, sem destinatário ou link.
- `/api/Auth/redefinir-senha`: segredo aleatório de 256 bits, apenas SHA-256 no banco, expira em 30 minutos e é consumido por atualização atômica. Novo envio substitui o link anterior. Link usa fragmento para não expor token em query de requests/Referer.
- Trocar senha incrementa a versão de sessão; JWTs anteriores deixam de ser aceitos pela API. O frontend pode continuar mostrando o usuário até nova navegação/login, mas sua sessão antiga não autoriza operações.
- `/api/Auth/google`: Google.Apis.Auth valida assinatura, emissor, prazo e audiência; o backend exige e-mail verificado. Vínculo usa subject único. Conta com mesmo e-mail exige senha atual antes do primeiro vínculo; preserva ID, pedidos e papel existente. Novas contas sempre são clientes.
- Limite em memória de 15 chamadas/minuto por IP de conexão nas rotas Auth. Em proxy sem encaminhamento confiável configurado, o IP pode ser compartilhado entre clientes; em múltiplas instâncias, considerar limite distribuído e configurar encaminhamento somente dos proxies confiáveis.
- Senhas novas exigem 8 caracteres e máximo de 72 bytes (limite BCrypt). Não há login automático após redefinição.
- Google e envio de e-mail reais requerem credenciais configuradas pelo responsável da loja.

## Testes

`tests/AuthChecks`: exige `AUTH_TEST_CONNECTION` com PostgreSQL local `127.0.0.1` e banco base `estoque_tests`; cria banco isolado e o remove ao terminar. Valida migrations, hash, expiração, uso único concorrente, cooldown, falha de envio, versão do JWT, fluxo de vínculo Google e rejeição de token forjado. O envio e identidades válidas são simulados; não envia e-mail real.