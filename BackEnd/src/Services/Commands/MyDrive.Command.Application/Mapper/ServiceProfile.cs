using AutoMapper;
using MyDrive.BuidingBlock.Contract.Abstractions.Shared;
using MyDrive.BuidingBlock.Contract.Services.V1.Folder;
using MyDrive.Command.Domain.Entities;

namespace MyDrive.Command.Application.Mapper;

public class ServiceProfile : Profile
{
    public ServiceProfile()
    {
        //V1
        CreateMap<Folder,Response.FolderResponse>().ReverseMap();
        CreateMap<PagedResult<Folder>, PagedResult<Response.FolderResponse>>().ReverseMap();
    }
}
