using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using PegaVisaoApi.Controllers;
using PegaVisaoApi.Data;
using PegaVisaoApi.DTO_s;
using PegaVisaoApi.Models;
using PegaVisaoApi.Services;
using PegaVisaoApi.Services.Frete;
using PegaVisaoApi.Services.MelhorEnvio;

int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("OK: " + name); checks++; }
async Task Rejeitar(Func<Task> action, string nome) { try { await action(); } catch (FreteException ex) when (ex.Status == 422) { Check(true, nome); return; } throw new Exception(nome); }
var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CUPONS_TEST_CONNECTION") ?? "");
if (connection.Host != "127.0.0.1" || connection.Database != "estoque_tests") throw new Exception("Exige PostgreSQL local e banco estoque_tests.");
await using var admin = new NpgsqlConnection(connection.ConnectionString);
await admin.OpenAsync();
var database = "cupons_test_" + Guid.NewGuid().ToString("N");
await using (var command = new NpgsqlCommand($"CREATE DATABASE {database}", admin)) await command.ExecuteNonQueryAsync();
connection.Database = database;
var options = new DbContextOptionsBuilder<PegaVisaoContext>().UseNpgsql(connection.ConnectionString).Options;
PegaVisaoContext Db() => new(options);
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Frete:DesabilitadoParaTeste"]="true" }).Build();
FreteService Frete(PegaVisaoContext db) => new(db, null!, null!, Options.Create(new MelhorEnvioOptions()), config, NullLogger<FreteService>.Instance);
CupomDto Dto(string codigo = "PEGA10") => new() { Codigo=codigo, Tipo="Percentual", Valor=10, InicioEm=DateTime.UtcNow.AddHours(-1), ValidadeEm=DateTime.UtcNow.AddDays(1) };
try {
    int userId, productId, v1, v2, v3, cupomId;
    await using (var db = Db()) {
        await db.Database.MigrateAsync();
        Check(!db.Database.HasPendingModelChanges(), "Migration e modelo sincronizados");
        var user=new Usuario { Nome="Teste", Email="cupons@example.invalid", SenhaHash="fake" };
        var product=new Produto { Nome="Moletom", Preco=100, Descricao="Teste", ImagemPrincipal="", Categoria=new Categoria { Nome="Teste" } };
        var other=new Produto { Nome="Camiseta", Preco=50, Descricao="Teste", ImagemPrincipal="", Categoria=product.Categoria };
        var a=new VariacaoProduto { Produto=product, Cor="Preto", Tamanho="M", Estoque=100 };
        var b=new VariacaoProduto { Produto=product, Cor="Amarelo", Tamanho="G", Estoque=100 };
        var c=new VariacaoProduto { Produto=other, Cor="Preto", Tamanho="G", Estoque=100 };
        db.AddRange(user,a,b,c); await db.SaveChangesAsync();
        userId=user.Id; productId=product.Id; v1=a.Id; v2=b.Id; v3=c.Id;
        var result=(CreatedAtActionResult)await new CuponsController(db).Criar(Dto(), default);
        cupomId=((Cupom)result.Value!).Id;
        Check(((Cupom)result.Value!).TodosProdutos, "Cupom geral criado e serializado");
    }
    List<CreateItemPedidoDto> Itens() => [new() { VariacaoProdutoId=v1, Quantidade=1 },new() { VariacaoProdutoId=v2, Quantidade=2 },new() { VariacaoProdutoId=v3, Quantidade=1 }];
    async Task<decimal> Validar(string codigo="PEGA10", List<CreateItemPedidoDto>? itens=null) {
        await using var db=Db(); var carrinho=await Frete(db).PrepararCarrinhoAsync(itens??Itens(),true,default);
        return (await new CupomService(db).ValidarAsync(codigo,carrinho,userId,false,default)).Desconto;
    }
    Check(await Validar(" pega10 ")==35, "Normaliza código e aplica percentual ao carrinho geral");
    var restrito=Dto(); restrito.TodosProdutos=false; restrito.ProdutoIds=[productId];
    await using (var db=Db()) { Check(await new CuponsController(db).Editar(cupomId,restrito,default) is OkObjectResult,"Edita cupom com seleção de produto"); }
    Check(await Validar()==30,"Desconto inclui todas as variações e quantidades só do produto participante");
    await Rejeitar(async()=>{await Validar(itens:[new(){VariacaoProdutoId=v3,Quantidade=1}]);},"Rejeita carrinho sem participantes");
    restrito.ValorMinimo=320;
    await using (var db=Db()) await new CuponsController(db).Editar(cupomId,restrito,default);
    await Rejeitar(async()=>{await Validar();},"Produtos não participantes não completam mínimo");
    restrito.ValorMinimo=0; restrito.Tipo="Fixo"; restrito.Valor=500;
    await using (var db=Db()) await new CuponsController(db).Editar(cupomId,restrito,default);
    Check(await Validar()==300,"Desconto fixo aplicado uma vez e limitado ao subtotal elegível");
    restrito.DescontoMaximo=12.34m;
    await using (var db=Db()) await new CuponsController(db).Editar(cupomId,restrito,default);
    Check(await Validar()==12.34m,"Teto de desconto respeitado");
    restrito.Ativo=false;
    await using (var db=Db()) await new CuponsController(db).Editar(cupomId,restrito,default);
    await Rejeitar(async()=>{await Validar();},"Cupom inativo bloqueado");
    restrito.Ativo=true; restrito.ValidadeEm=DateTime.UtcNow.AddMinutes(-1);
    await using (var db=Db()) await new CuponsController(db).Editar(cupomId,restrito,default);
    await Rejeitar(async()=>{await Validar();},"Cupom vencido bloqueado");
    restrito.InicioEm=DateTime.UtcNow.AddHours(1); restrito.ValidadeEm=DateTime.UtcNow.AddDays(1);
    await using (var db=Db()) await new CuponsController(db).Editar(cupomId,restrito,default);
    await Rejeitar(async()=>{await Validar();},"Cupom futuro bloqueado");
    restrito.InicioEm=DateTime.UtcNow.AddHours(-1); restrito.LimiteTotal=1;
    await using (var db=Db()) await new CuponsController(db).Editar(cupomId,restrito,default);

    async Task<Pedido?> CriarPedido() {
        await using var db=Db(); var frete=Frete(db);
        var cotacao=JsonSerializer.SerializeToElement(await frete.CotarAsync(userId,"17013113",Itens(),default));
        var id=cotacao.GetProperty("Id").GetGuid();
        var pedido=new Pedido { UsuarioId=userId,CupomCodigo="PEGA10",ValorTotal=0.01m,ValorDesconto=999,
            Cep="17013113",Rua="Teste",Numero="1",Bairro="Centro",Cidade="Bauru",Estado="SP",FormaPagamento="Pix" };
        try { await frete.SalvarPedidoAsync(pedido,id,int.MaxValue,Itens(),default); return pedido; }
        catch (FreteException e) when(e.Status==422) { return null; }
    }
    var concorrentes=await Task.WhenAll(CriarPedido(),CriarPedido());
    Check(concorrentes.Count(p=>p!=null)==1,"Duas compras concorrentes respeitam última utilização");
    var pedido=concorrentes.Single(p=>p!=null)!;
    Check(pedido.ValorDesconto==12.34m && pedido.ValorTotal==337.66m,"Criação recalcula preços, ignora total manipulado e grava desconto");
    await using(var db=Db()) {
        Check((await db.VariacaoProdutos.FindAsync(v1))!.EstoqueReservado==1,"Só a compra aprovada reserva estoque");
        var listar=(OkObjectResult)await new CuponsController(db).Listar(default);
        var json=JsonSerializer.SerializeToElement(listar.Value);
        Check(json[0].GetProperty("usos").GetInt32()==1 && json[0].GetProperty("cupom").GetProperty("ProdutoIds").GetArrayLength()==1,"Painel lista utilizações e produtos");
        await new EstoqueService(db).CancelarSemPagamentoAsync(pedido.Id);
    }
    Check(await Validar()==12.34m,"Cancelamento libera uso do cupom");
    await using(var db=Db()) Check((await db.VariacaoProdutos.FindAsync(v1))!.EstoqueReservado==0,"Cancelamento libera estoque");
    var segundo=(await CriarPedido())!;
    await using(var db=Db()) await new EstoqueService(db).AplicarPagamentoAsync(segundo.Id,new("ORDTEST","",segundo.Id.ToString(),"expired","expired",segundo.ValorTotal,false){EncerradoSemPagamento=true});
    Check(await Validar()==12.34m,"Expiração confirmada libera utilização");
    restrito.LimiteTotal=null; restrito.LimitePorUsuario=1;
    await using(var db=Db()) await new CuponsController(db).Editar(cupomId,restrito,default);
    var terceiro=(await CriarPedido())!;
    await using(var db=Db()) await new EstoqueService(db).AplicarPagamentoAsync(terceiro.Id,new("ORDPAGO","PAYTEST",terceiro.Id.ToString(),"processed","accredited",terceiro.ValorTotal,true));
    await Rejeitar(async()=>{await Validar();},"Compra paga consome limite por cliente");
    restrito.Valor=1;
    await using(var db=Db()) {
        await new CuponsController(db).Editar(cupomId,restrito,default);
        var salvo=await db.Pedidos.Include(p=>p.Itens).ThenInclude(i=>i.VariacaoProduto).ThenInclude(v=>v.Produto).SingleAsync(p=>p.Id==terceiro.Id);
        Check(salvo.ValorDesconto==12.34m && salvo.CupomCodigo=="PEGA10","Edição mantém desconto histórico");
        salvo.ValorFrete=10; salvo.ValorTotal+=10;
        var pix=JsonSerializer.Deserialize<JsonElement>(PayloadPagamento.Criar(salvo,true,"teste@example.invalid","Teste"));
        Check(pix.GetProperty("total_amount").GetString()=="347.66","Pix cobra total descontado com frete integral");
        salvo.FormaPagamento="Cartão";
        var cartao=JsonSerializer.Deserialize<JsonElement>(PayloadPagamento.Criar(salvo,true));
        var soma=cartao.GetProperty("items").EnumerateArray().Sum(i=>decimal.Parse(i.GetProperty("unit_price").GetString()!,System.Globalization.CultureInfo.InvariantCulture));
        Check(soma==347.66m && cartao.GetProperty("total_amount").GetString()=="347.66","Cartão soma linhas em centavos e preserva frete");
        await new CuponsController(db).Excluir(cupomId,default);
        Check(await db.Pedidos.AnyAsync(p=>p.Id==terceiro.Id && p.ValorDesconto==12.34m),"Exclusão lógica mantém pedido e desconto");
    }
    await Rejeitar(async()=>{await Validar();},"Cupom excluído não pode ser aplicado");
    var invalid=Dto(); invalid.TodosProdutos=false;
    Check(!Validator.TryValidateObject(invalid,new ValidationContext(invalid),new List<ValidationResult>(),true),"DTO rejeita seleção vazia");
    Check(typeof(CuponsController).GetCustomAttribute<AuthorizeAttribute>()?.Roles=="Admin","CRUD exige administrador");
    Check(typeof(ValidacaoCupomController).GetCustomAttribute<AuthorizeAttribute>()!=null,"Validação exige cliente autenticado");
    Check(CupomService.Calcular(new Cupom{Valor=12.5m,InicioEm=DateTime.UtcNow.AddDays(-1),ValidadeEm=DateTime.UtcNow.AddDays(1)},99.99m,DateTime.UtcNow)==12.50m,"Arredondamento monetário em centavos");
    Console.WriteLine($"{checks} verificações aprovadas; nenhuma chamada a provedores de pagamento.");
} finally {
    NpgsqlConnection.ClearAllPools();
    await using var drop=new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)",admin);
    await drop.ExecuteNonQueryAsync();
}
