using Auth.Api.Database.Models;
using Auth.Api.Services.Dtos;
using AutoMapper;

namespace Auth.Api.Infrastructure.Mapper;

public class ServiceProfile: Profile
{
    public ServiceProfile()
    {
        CreateMap<AppUser, RegisterRequest>()
            .ReverseMap()
            .ForMember(des => des.Id, act=> act.MapFrom(src=> Guid.NewGuid()))
            .ForMember(des => des.FullName, act=> act.MapFrom(src=> src.FirstName + " " + src.LastName));
    }
}
