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
    public class PedidoController : ControllerBase
    {
        private PegaVisaoContext _context;
        private IMapper _mapper;


        public PedidoController(PegaVisaoContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        [HttpPost]
        public IActionResult CriarPedido(CreatePedidoDto dto)
        {
            Pedido pedido = _mapper.Map<Pedido>(dto);
            pedido.Status = Status.Pendente;
            decimal valorTotal = 0;
            foreach (var item in pedido.Itens)
            {
                var variacao = _context.VariacaoProdutos.Include(v => v.Produto).FirstOrDefault(v => v.Id == item.VariacaoProdutoId);

                if (item.Quantidade <= 0)
                {
                    return BadRequest("A quantidade deve ser maior que zero.");
                }

                if (variacao == null)
                {
                    return BadRequest($"Variação de produto com ID {item.VariacaoProdutoId} não encontrada.");
                }

                Console.WriteLine($"Variação: {variacao.Id}");
                Console.WriteLine($"Produto: {variacao.Produto?.Nome}");
                Console.WriteLine($"Preço: {variacao.Produto?.Preco}");

                item.PrecoUnitario = variacao.Produto.Preco;
                valorTotal += item.PrecoUnitario * item.Quantidade;

                Console.WriteLine($"Variação: {variacao.Id}");
                Console.WriteLine($"Produto: {variacao.Produto?.Nome}");
                Console.WriteLine($"Preço: {variacao.Produto?.Preco}");
            }

            pedido.ValorTotal = valorTotal;

            _context.Pedidos.Add(pedido);
            _context.SaveChanges();
            var pedidoDto = _mapper.Map<ReadPedidoDto>(pedido);
            return CreatedAtAction(nameof(RecuperarPedidoPorId), new { id = pedido.Id }, pedidoDto);
        }

        [HttpGet("{id}")]
        public ActionResult<ReadPedidoDto> RecuperarPedidoPorId(int id)
        {
            var pedido = _context.Pedidos.Include(p => p.Itens).FirstOrDefault(p => p.Id == id);
            if (pedido == null)
            {
                return NotFound();
            }
            var pedidodto = _mapper.Map<ReadPedidoDto>(pedido);
            return Ok(pedidodto);
        }

        [HttpGet]
        public ActionResult<IEnumerable<ReadPedidoDto>> RecuperarPedidos()
        {
            var pedidos = _context.Pedidos.Include(p => p.Itens).ToList();
            var pedidosDto = _mapper.Map<List<ReadPedidoDto>>(pedidos);
            return Ok(pedidosDto);
        }

        [HttpPut("{id}")]
        public IActionResult AtualizaPedido(int id, UpdatePedidoDto dto)
        {
            var pedido = _context.Pedidos.FirstOrDefault(p => p.Id == id);
            if (pedido == null) return NotFound();
            _mapper.Map(dto, pedido);
            _context.SaveChanges();
            return NoContent();
        }


        [HttpDelete("{id}")]
        public IActionResult DeletarPedido(int id)
        {
            var pedido = _context.Pedidos.FirstOrDefault(p => p.Id == id);
            if (pedido == null) return NotFound();
            _context.Pedidos.Remove(pedido);
            _context.SaveChanges();
            return NoContent();
        }
    }
}
