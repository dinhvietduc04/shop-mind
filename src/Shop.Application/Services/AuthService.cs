using Microsoft.EntityFrameworkCore;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Application.Persistence;
using Shop.Domain.Entities;
using Shop.Domain.Exceptions;

namespace Shop.Application.Services;

public class AuthService(IAppDbContext db, IPasswordHasher hasher, IJwtTokenGenerator tokens) : IAuthService
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new EmailAlreadyExistsException(email);

        var user = new User
        {
            Email = email,
            PasswordHash = hasher.Hash(request.Password),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PhoneNumber = request.PhoneNumber?.Trim(),
            Role = Domain.Enums.UserRole.Customer,
        };
        db.Users.Add(user);
        // each user gets one active cart
        db.Carts.Add(new Cart { UserId = user.Id });
        await db.SaveChangesAsync(ct);
        var token = tokens.GenerateToken(user.Id, user.Email, user.Role.ToString());
        return new AuthResponse(token, user.ToDto());
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct)
            ?? throw new InvalidCredentialsException();
        if (!hasher.Verify(request.Password, user.PasswordHash))
            throw new InvalidCredentialsException();
        var token = tokens.GenerateToken(user.Id, user.Email, user.Role.ToString());
        return new AuthResponse(token, user.ToDto());
    }

    public async Task<UserDto> GetByIdAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("UserNotFound", "User not found.");
        return user.ToDto();
    }

    public async Task<UserDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("UserNotFound", "User not found.");
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.PhoneNumber = request.PhoneNumber?.Trim();
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return user.ToDto();
    }
}
