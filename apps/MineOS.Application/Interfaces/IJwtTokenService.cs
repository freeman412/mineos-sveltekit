using MineOS.Domain.Entities;

namespace MineOS.Application.Interfaces;

public interface IJwtTokenService
{
    string CreateToken(User user);
}
