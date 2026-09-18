using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using PegaVisaoApi.Data;
using PegaVisaoApi.DTO_s;
using PegaVisaoApi.Models;
using Microsoft.AspNetCore.Authorization;

namespace PegaVisaoApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class VariacaoController : ControllerBase
    {
  
        private PegaVisaoContext _context;
        private readonly IMapper _mapper;
        public VariacaoController(PegaVisaoContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public IActionResult CriarVariacao(CreateVariacaoDto dto)
        {
            VariacaoProduto Variacao = _mapper.Map<VariacaoProduto>(dto);
            _context.VariacaoProdutos.Add(Variacao);
            _context.SaveChanges();
            return CreatedAtAction(nameof(RecuperarVariacaoPorId), new { id = Variacao.Id }, Variacao);
        }

        [HttpGet("{id}")]
        public ActionResult<VariacaoProduto> RecuperarVariacaoPorId(int id)
        {
            var variacao = _context.VariacaoProdutos.FirstOrDefault(v => v.Id == id);
            if (variacao == null)
            {
                return NotFound();
            }
            return variacao;
        }

        [HttpGet]
        public ActionResult<IEnumerable<VariacaoProduto>> RecuperarVariacoes()
        {
            var variacoes = _context.VariacaoProdutos.ToList();
            return variacoes;
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public IActionResult AtualizaVariacao(int id, UpdateVariacaoDto dto)
        {
            var variacao = _context.VariacaoProdutos.FirstOrDefault(v => v.Id == id);
            if (variacao == null) return NotFound();
            _mapper.Map(dto, variacao);
            _context.SaveChanges();
            return NoContent();
        }


        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public IActionResult DeletarVariacao(int id)
        {
            var variacao = _context.VariacaoProdutos.FirstOrDefault(v => v.Id == id);
            if (variacao == null) return NotFound();
            _context.VariacaoProdutos.Remove(variacao);
            _context.SaveChanges();
            return NoContent();
        }
    }
}

