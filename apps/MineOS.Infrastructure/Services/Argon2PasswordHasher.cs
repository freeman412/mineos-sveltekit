using Isopoh.Cryptography.Argon2;
using MineOS.Application.Interfaces;

namespace MineOS.Infrastructure.Services;

public sealed class Argon2PasswordHasher : IPasswordHasher
{
    public string Hash(string password)
    {
        return Argon2.Hash(password);
    }

    public bool Verify(string password, string hash)
    {
        return Argon2.Verify(hash, password);
    }
}
