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
            CreateMap<ItemPedido, ReadItemPedidoDto>();
        }
    }
}
