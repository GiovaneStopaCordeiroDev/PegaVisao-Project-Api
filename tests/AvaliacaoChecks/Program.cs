using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PegaVisaoApi.Controllers;
using PegaVisaoApi.Data;
using PegaVisaoApi.Models;

var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("AVALIACAO_TEST_CONNECTION") ?? "");
if (connection.Host != "127.0.0.1" || connection.Database != "estoque_tests") throw new Exception("Exige banco local estoque_tests.");
await using var admin = new NpgsqlConnection(connection.ConnectionString);
await admin.OpenAsync();
var database = "avaliacao_test_" + Guid.NewGuid().ToString("N");
await using (var cmd = new NpgsqlCommand($"CREATE DATABASE {database}", admin)) await cmd.ExecuteNonQueryAsync();
connection.Database = database;
var options = new DbContextOptionsBuilder<PegaVisaoContext>().UseNpgsql(connection.ConnectionString).Options;
int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("OK: " + name); checks++; }
AvaliacaoProdutoController Controller(PegaVisaoContext db, int? user = 1) => new(db) {
    ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
        user.HasValue ? new[] { new Claim(ClaimTypes.NameIdentifier, user.Value.ToString()) } : Array.Empty<Claim>(), "teste")) } }
};
try {
    await using var db = new PegaVisaoContext(options);
    await db.Database.MigrateAsync();
    Check(!(await db.Database.GetPendingMigrationsAsync()).Any(), "Migration aplicada em banco isolado");
    db.Usuarios.AddRange(new Usuario { Id = 1, Nome = "Comprador", Email = "a@example.invalid" }, new Usuario { Id = 2, Nome = "Outro", Email = "b@example.invalid" });
    var categoria = new Categoria { Nome = "Teste" };
    var produto = new Produto { Nome = "Produto", Descricao = "Teste", ImagemPrincipal = "teste.png", Preco = 10, Categoria = categoria };
    var outro = new Produto { Nome = "Outro", Descricao = "Teste", ImagemPrincipal = "teste.png", Preco = 20, Categoria = categoria };
    var variacao = new VariacaoProduto { Produto = produto, Cor = "Preto", Tamanho = "M", Estoque = 10 };
    db.Produtos.Add(outro);
    var pedido = new Pedido { UsuarioId = 1, Status = Status.Pendente, Cep = "00000000", Rua = "Teste", Numero = "1", Bairro = "Teste", Cidade = "Teste", Estado = "SP", FormaPagamento = "Pix", Itens = new List<ItemPedido> { new() { VariacaoProduto = variacao, Quantidade = 1, PrecoUnitario = 10 } } };
    db.Pedidos.Add(pedido);
    await db.SaveChangesAsync();
    var controller = Controller(db);
    Check(await Controller(db, null).Avaliar(produto.Id, new(5), default) is UnauthorizedResult, "Sem identidade não avalia");
    Check(await controller.Avaliar(produto.Id, new(0), default) is BadRequestObjectResult && await controller.Avaliar(produto.Id, new(6), default) is BadRequestObjectResult, "Notas fora de 1 a 5 rejeitadas");
    foreach (var status in new[] { Status.Pendente, Status.Cancelado }) {
        pedido.Status = status; await db.SaveChangesAsync();
        Check(await controller.Avaliar(produto.Id, new(5), default) is ObjectResult { StatusCode: 403 }, $"Pedido {status} não habilita avaliação");
    }
    foreach (var status in new[] { Status.Pago, Status.Enviado, Status.Entregue }) {
        pedido.Status = status; await db.SaveChangesAsync();
        Check(await controller.Avaliar(produto.Id, new(4), default) is NoContentResult, $"Pedido {status} habilita avaliação");
    }
    Check(await controller.Avaliar(outro.Id, new(5), default) is ObjectResult { StatusCode: 403 }, "Produto não comprado bloqueado");
    Check(await Controller(db, 2).Avaliar(produto.Id, new(5), default) is ObjectResult { StatusCode: 403 }, "Compra de outro usuário não habilita avaliação");
    await controller.Avaliar(produto.Id, new(2), default);
    Check(await db.AvaliacoesProdutos.CountAsync() == 1 && await db.AvaliacoesProdutos.Select(a => a.Nota).SingleAsync() == 2, "Atualizar nota preserva uma avaliação por usuário/produto");
    var resumo = JsonSerializer.SerializeToElement(((OkObjectResult)await controller.Resumo(produto.Id, default)).Value);
    Check(resumo.GetProperty("total").GetInt32() == 1 && resumo.GetProperty("media").GetDouble() == 2, "Resumo público corresponde às avaliações");
    var minha = JsonSerializer.SerializeToElement(((OkObjectResult)await controller.Minha(produto.Id, default)).Value);
    Check(minha.GetProperty("nota").GetInt32() == 2 && minha.GetProperty("podeAvaliar").GetBoolean(), "Cliente consulta sua própria nota e permissão");
    var alheia = JsonSerializer.SerializeToElement(((OkObjectResult)await Controller(db, 2).Minha(produto.Id, default)).Value);
    Check(alheia.GetProperty("nota").ValueKind == JsonValueKind.Null && !alheia.GetProperty("podeAvaliar").GetBoolean(), "Nota pessoal não vaza para outro cliente");
    await Task.WhenAll(Enumerable.Range(1, 5).Select(async nota => {
        await using var paralelo = new PegaVisaoContext(options);
        await Controller(paralelo).Avaliar(produto.Id, new(nota), default);
    }));
    Check(await db.AvaliacoesProdutos.CountAsync() == 1, "Avaliações concorrentes não duplicam registro");
    Check(await controller.Avaliar(int.MaxValue, new(5), default) is NotFoundResult, "Produto inexistente retorna 404");
    Console.WriteLine($"{checks} verificações passaram.");
} finally {
    NpgsqlConnection.ClearAllPools();
    await using var cmd = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", admin);
    await cmd.ExecuteNonQueryAsync();
}
