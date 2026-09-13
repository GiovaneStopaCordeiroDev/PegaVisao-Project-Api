using AutoMapper;
using PegaVisaoApi.DTO_s;
using PegaVisaoApi.Models;

namespace PegaVisaoApi.Profiles
{
    public class VariacaoProfile : Profile
    {
        public VariacaoProfile()
        {
            CreateMap<CreateVariacaoDto, VariacaoProduto>();
            CreateMap<UpdateVariacaoDto, VariacaoProduto>();
            CreateMap<VariacaoProduto, ReadVariacaoDto>();
        }
    }
}
