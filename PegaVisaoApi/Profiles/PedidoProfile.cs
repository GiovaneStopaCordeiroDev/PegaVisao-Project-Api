using AutoMapper;
using PegaVisaoApi.DTO_s;
using PegaVisaoApi.Models;

namespace PegaVisaoApi.Profiles
{
    public class PedidoProfile : Profile
    {
        public PedidoProfile()
        {
            CreateMap<CreatePedidoDto, Pedido>();

            CreateMap<CreateItemPedidoDto, ItemPedido>();

            CreateMap<UpdatePedidoDto, Pedido>();

            CreateMap<Pedido, ReadPedidoDto>();

            CreateMap<ItemPedido, ReadItemPedidoDto>()
                .ForMember(destino => destino.ProdutoId, opt => opt.MapFrom(origem => origem.VariacaoProduto.ProdutoId))
                .ForMember(
                    destino => destino.NomeProduto,
                    opt => opt.MapFrom(
                        origem => origem.VariacaoProduto.Produto.Nome
                    )
                )
                .ForMember(
                    destino => destino.ImagemProduto,
                    opt => opt.MapFrom(
                        origem => origem.VariacaoProduto.Produto.ImagemPrincipal
                    )
                )
                .ForMember(
                    destino => destino.Cor,
                    opt => opt.MapFrom(
                        origem => origem.VariacaoProduto.Cor
                    )
                )
                .ForMember(
                    destino => destino.Tamanho,
                    opt => opt.MapFrom(
                        origem => origem.VariacaoProduto.Tamanho
                    )
                );
        }
    }
}
