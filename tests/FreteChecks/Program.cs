using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using PegaVisaoApi.DTO_s;
using PegaVisaoApi.Models;
using PegaVisaoApi.Services;
using PegaVisaoApi.Services.Frete;
using PegaVisaoApi.Services.MelhorEnvio;
int checks = 0;
void Check(bool ok, string nome) { if (!ok) throw new Exception(nome); Console.WriteLine("OK: " + nome); checks++; }
void Rejeita(Action acao, int status, string nome) { try { acao(); throw new Exception(nome); } catch (FreteException ex) { Check(ex.Status == status, nome); } }
Check(FreteRegras.NormalizarCep("17013-113") == "17013113", "CEP normalizado");
Rejeita(() => FreteRegras.NormalizarCep("abc17013113"), 400, "CEP invalido");
Check(FreteRegras.AgruparItens([new() { VariacaoProdutoId=1,Quantidade=2 },new() { VariacaoProdutoId=1,Quantidade=3 }])[1] == 5, "Agrupa quantidades");
Rejeita(() => FreteRegras.AgruparItens([new() { VariacaoProdutoId=1,Quantidade=80 },new() { VariacaoProdutoId=1,Quantidade=80 }]),400,"Limite acumulado");
Rejeita(() => FreteRegras.AgruparItens([]),400,"Carrinho vazio");
Rejeita(() => FreteRegras.PrepararItem(new() { Produto = new() { Nome="Sem medidas", Preco=30 } },1),422,"Produto sem medidas");
var produto = new Produto { Nome="Teste", Preco=30, PesoKg=.5m, AlturaCm=5,LarguraCm=20,ComprimentoCm=30 };
var item = FreteRegras.PrepararItem(new() { Id=1, Produto=produto },2);
Check(item.Preco==30 && item.PesoKg==.5m && item.Quantidade==2,"Dados do cadastro");
var semMedidas = new DadosEnvioProdutoDto();
Check(!Validator.TryValidateObject(semMedidas,new ValidationContext(semMedidas),[],true),"Medidas obrigatorias no cadastro");
var hash = FreteRegras.HashCarrinho([item],"17013113");
foreach (var alterado in new[] { item with { Quantidade=3 },item with { Preco=31 },item with { PesoKg=.6m },item with { ComprimentoCm=31 } })
 Check(hash != FreteRegras.HashCarrinho([alterado],"17013113"),"Alteracao invalida hash");
Check(hash != FreteRegras.HashCarrinho([item],"01001000"),"Origem invalida hash");
var agora = DateTime.UtcNow;
var cotacao = new CotacaoFrete { UsuarioId=1,CepDestino="01001000",CarrinhoHash=hash,ConexaoId="sandbox:1",ExpiraEm=agora.AddMinutes(15) };
FreteRegras.ValidarCotacao(cotacao,1,"01001000",hash,"sandbox:1",agora); Check(true,"Cotacao valida");
Rejeita(() => FreteRegras.ValidarCotacao(cotacao,2,"01001000",hash,"sandbox:1",agora),404,"Outro usuario");
Rejeita(() => FreteRegras.ValidarCotacao(cotacao,1,"01001000",hash,"sandbox:1",agora.AddMinutes(15)),409,"Expiracao");
Rejeita(() => FreteRegras.ValidarCotacao(cotacao,1,"17013113",hash,"sandbox:1",agora),409,"Outro destino");
Rejeita(() => FreteRegras.ValidarCotacao(cotacao,1,"01001000",hash,"producao:1",agora),409,"Outro ambiente");
cotacao.ConsumidaEm=agora;
Rejeita(() => FreteRegras.ValidarCotacao(cotacao,1,"01001000",hash,"sandbox:1",agora),409,"Cotacao consumida");
const string resposta = """
[{"id":1,"name":"PAC","company":{"name":"Correios"},"price":"99.00","custom_price":"15.50","delivery_time":99,"custom_delivery_time":4,"packages":[{}]},
{"id":2,"name":"Falhou","company":{"name":"Correios"},"error":"indisponivel","custom_price":"10.00","custom_delivery_time":2},
{"id":3,"name":"Sem preco","company":{"name":"Correios"},"price":"8.00","custom_delivery_time":1},
{"id":4,"name":"Gratis","company":{"name":"Teste"},"custom_price":0,"custom_delivery_time":1}]
""";
using var json = JsonDocument.Parse(resposta);
var opcoes = FreteRegras.LerOpcoes(json.RootElement);
Check(opcoes.Count==2 && opcoes[0].Valor==0 && opcoes[1].Valor==15.50m && opcoes[1].PrazoDias==4,"Usa custom price/prazo; filtra erros; aceita zero explicito");
var handler = new FakeHandler(resposta);
var config = new MelhorEnvioOptions { ClientId="1",ClientSecret="fake",RedirectUri="https://example.com/api/MelhorEnvio/callback",EmailContato="test@example.com",TokenEncryptionKey=Convert.ToBase64String(new byte[32]),Sandbox=true };
var client = new MelhorEnvioFreteClient(new HttpClient(handler),Options.Create(config));
await client.CotarAsync("fake","17013113","01001000",[item],default);
using var payload = JsonDocument.Parse(handler.Body!);
var p = payload.RootElement.GetProperty("products")[0];
Check(handler.Url=="https://sandbox.melhorenvio.com.br/api/v2/me/shipment/calculate" && handler.Bearer=="fake","Endpoint sandbox autenticado");
Check(p.GetProperty("insurance_value").GetDecimal()==30 && p.GetProperty("quantity").GetInt32()==2 && p.GetProperty("weight").GetDecimal()==.5m && p.GetProperty("height").GetDecimal()==5,"Valor unitario seguro, quantidade, kg e cm");
Check(payload.RootElement.GetProperty("to").GetProperty("postal_code").GetString()=="01001000","CEP destino enviado");
var mp = new MercadoPagoService(new HttpClient(handler),new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["MercadoPago:AccessToken"]="fake" }).Build());
handler.Response="{}";
await mp.CriarPixAsync(new Pedido { Id=1,ValorTotal=75.50m,ValorFrete=15.50m },"test@example.com","Teste");
using var pix = JsonDocument.Parse(handler.Body!);
Check(pix.RootElement.GetProperty("total_amount").GetString()=="75.50" && pix.RootElement.GetProperty("transactions").GetProperty("payments")[0].GetProperty("amount").GetString()=="75.50","Pix cobra produtos mais frete");
Console.WriteLine($"{checks} verificacoes aprovadas.");
sealed class FakeHandler(string response) : HttpMessageHandler {
 public string Response=response; public string? Body,Url,Bearer;
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
  Body=request.Content==null?null:await request.Content.ReadAsStringAsync(ct); Url=request.RequestUri!.ToString(); Bearer=request.Headers.Authorization?.Parameter;
  return new(HttpStatusCode.OK) { Content=new StringContent(Response) };
 }
}
