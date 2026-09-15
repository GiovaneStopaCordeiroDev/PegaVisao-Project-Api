using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PegaVisaoApi.Data;
using PegaVisaoApi.DTO_s;
using PegaVisaoApi.Models;

namespace PegaVisaoApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ProdutoController : ControllerBase
    {
        private PegaVisaoContext _context;
        private readonly IMapper _mapper;

        public ProdutoController(
            PegaVisaoContext context,
            IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        [HttpPost]
        public IActionResult CriarProduto(CreateProdutoDto dto)
        {
            Produto produto = _mapper.Map<Produto>(dto);

            _context.Produtos.Add(produto);
            _context.SaveChanges();

            foreach (var variacaoDto in dto.Variacoes)
            {
                var variacao = new VariacaoProduto
                {
                    Cor = variacaoDto.Cor,
                    Tamanho = variacaoDto.Tamanho,
                    Estoque = variacaoDto.Estoque,
                    ProdutoId = produto.Id
                };

                _context.VariacaoProdutos.Add(variacao);
            }

            _context.SaveChanges();

            var produtoCriado = _context.Produtos
                .Include(p => p.Variacoes)
                .FirstOrDefault(p => p.Id == produto.Id);

            var readProdutoDto =
                _mapper.Map<ReadProdutoDto>(produtoCriado);

            return CreatedAtAction(
                nameof(RecuperarProdutoPorId),
                new { id = produto.Id },
                readProdutoDto
            );
        }

        [HttpGet("{id}")]
        public ActionResult<ReadProdutoDto> RecuperarProdutoPorId(int id)
        {
            var produto = _context.Produtos
                .AsNoTracking()
                .Include(p => p.Variacoes)
                .FirstOrDefault(p => p.Id == id);

            if (produto == null)
                return NotFound();

            var readProdutoDto =
                _mapper.Map<ReadProdutoDto>(produto);

            return Ok(readProdutoDto);
        }

        [HttpGet]
        public ActionResult<IEnumerable<ReadProdutoDto>> RecuperarProdutos(
            int skip = 0,
            int take = 30)
        {
            var inicio = DateTime.Now;

            var produtos = _context.Produtos
                .AsNoTracking()
                .Include(p => p.Variacoes)
                .Skip(skip)
                .Take(take)
                .ToList();

            var fimBanco = DateTime.Now;

            var readProdutos =
                _mapper.Map<List<ReadProdutoDto>>(produtos);

            var fimTotal = DateTime.Now;

            Console.WriteLine(
                $"Banco: {(fimBanco - inicio).TotalMilliseconds} ms"
            );

            Console.WriteLine(
                $"Total: {(fimTotal - inicio).TotalMilliseconds} ms"
            );

            return Ok(readProdutos);
        }

        [HttpPut("{id}")]
        public IActionResult AlterarProduto(
            int id,
            UpdateProdutoDto dto)
        {
            var produto = _context.Produtos
                .Include(p => p.Variacoes)
                .FirstOrDefault(p => p.Id == id);

            if (produto == null)
                return NotFound();

            // Atualiza os dados principais do produto
            produto.Nome = dto.Nome;
            produto.Descricao = dto.Descricao;
            produto.Preco = dto.Preco;
            produto.ImagemPrincipal = dto.ImagemPrincipal;
            produto.CategoriaId = dto.CategoriaId;


            // IDs das variações que continuam no produto
            var idsVariacoesRecebidas = dto.Variacoes
                .Where(v => v.Id > 0)
                .Select(v => v.Id)
                .ToList();


            // Remove as variações que foram excluídas no painel
            var variacoesParaRemover = produto.Variacoes
                .Where(v => !idsVariacoesRecebidas.Contains(v.Id))
                .ToList();

            foreach (var variacao in variacoesParaRemover)
            {
                _context.VariacaoProdutos.Remove(variacao);
            }


            // Atualiza as existentes e adiciona as novas
            foreach (var variacaoDto in dto.Variacoes)
            {
                if (variacaoDto.Id > 0)
                {
                    // Variação existente
                    var variacaoExistente = produto.Variacoes
                        .FirstOrDefault(v => v.Id == variacaoDto.Id);

                    if (variacaoExistente != null)
                    {
                        variacaoExistente.Cor = variacaoDto.Cor;
                        variacaoExistente.Tamanho = variacaoDto.Tamanho;
                        variacaoExistente.Estoque = variacaoDto.Estoque;
                    }
                }
                else
                {
                    // Nova variação
                    var novaVariacao = new VariacaoProduto
                    {
                        Cor = variacaoDto.Cor,
                        Tamanho = variacaoDto.Tamanho,
                        Estoque = variacaoDto.Estoque,
                        ProdutoId = produto.Id
                    };

                    _context.VariacaoProdutos.Add(novaVariacao);
                }
            }


            try
                {
                    _context.SaveChanges();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("========== ERRO AO ALTERAR PRODUTO ==========");
                    Console.WriteLine(ex.ToString());
                    Console.WriteLine("=============================================");

                    return StatusCode(500, new
                    {
                        mensagem = "Erro ao alterar produto.",
                        erro = ex.Message,
                        detalhe = ex.InnerException?.Message
                    });
                }


            // Busca novamente para devolver o produto atualizado
            var produtoAtualizado = _context.Produtos
                .AsNoTracking()
                .Include(p => p.Variacoes)
                .FirstOrDefault(p => p.Id == id);

            var readProdutoDto =
                _mapper.Map<ReadProdutoDto>(produtoAtualizado);

            return Ok(readProdutoDto);
        }

        [HttpDelete("{id}")]
        public IActionResult DeletarProduto(int id)
        {
            var produto = _context.Produtos
                .FirstOrDefault(p => p.Id == id);

            if (produto == null)
                return NotFound();

            _context.Produtos.Remove(produto);

            _context.SaveChanges();

            return NoContent();
        }
    }
}