using AutoMapper;
using PegaVisaoApi.DTO_s;
using PegaVisaoApi.Models;

namespace PegaVisaoApi.AutoMapper
{
    public class ProdutoProfile : Profile
    {
        public ProdutoProfile()
        {
            CreateMap<CreateProdutoDto, Produto>()
                .ForMember(
                    destino => destino.Variacoes,
                    opcao => opcao.Ignore()
                );

            CreateMap<UpdateProdutoDto, Produto>()
                .ForMember(
                    destino => destino.Variacoes,
                    opcao => opcao.Ignore()
                );

            CreateMap<Produto, ReadProdutoDto>();
        }
    }
}