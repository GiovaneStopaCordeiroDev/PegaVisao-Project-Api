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


        public ProdutoController(PegaVisaoContext context, IMapper mapper)
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

            var readProdutoDto = _mapper.Map<ReadProdutoDto>(produtoCriado);

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

            var readProdutoDto = _mapper.Map<ReadProdutoDto>(produto);

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

            var readProdutos = _mapper.Map<List<ReadProdutoDto>>(produtos);

            var fimTotal = DateTime.Now;

            Console.WriteLine($"Banco: {(fimBanco - inicio).TotalMilliseconds} ms");
            Console.WriteLine($"Total: {(fimTotal - inicio).TotalMilliseconds} ms");

            return Ok(readProdutos);
        }

       


        [HttpDelete("{id}")]
        public IActionResult DeletarProduto(int id)
        {
            var produto = _context.Produtos.FirstOrDefault(p => p.Id == id);
            if (produto == null) return NotFound();
            _context.Produtos.Remove(produto);
            _context.SaveChanges();
            return NoContent();
        }
    }
}

