using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Реализация генерации JWT-токенов на основе параметров из конфигурации.
    /// </summary>
    public class JwtService : IJwtService
    {
        private readonly IConfiguration _configuration;

        /// <summary>
        /// Создаёт экземпляр сервиса.
        /// </summary>
        /// <param name="configuration">Конфигурация приложения</param>
        /// <exception cref="ArgumentNullException">Если configuration равен null</exception>
        public JwtService(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>
        /// Формирует JWT-токен для пользователя.
        /// </summary>
        /// <param name="user">Пользователь</param>
        /// <param name="roles">Список ролей пользователя</param>
        /// <returns>Строка JWT-токена</returns>
        /// <exception cref="ArgumentNullException">Если user или roles равны null</exception>
        /// <exception cref="InvalidOperationException">Если в конфигурации отсутствуют параметры JWT</exception>
        public Task<string> GenerateTokenAsync(ApplicationUser user, IList<string> roles)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            if (roles == null) throw new ArgumentNullException(nameof(roles));

            var issuer = _configuration["Jwt:Issuer"]
                ?? throw new InvalidOperationException("Не задан Jwt:Issuer в конфигурации");
            var audience = _configuration["Jwt:Audience"]
                ?? throw new InvalidOperationException("Не задан Jwt:Audience в конфигурации");
            var key = _configuration["Jwt:Key"]
                ?? throw new InvalidOperationException("Не задан Jwt:Key в конфигурации");

            var expirationMinutes = 480;
            var expirationRaw = _configuration["Jwt:ExpirationMinutes"];
            if (!string.IsNullOrWhiteSpace(expirationRaw) && int.TryParse(expirationRaw, out var parsed))
                expirationMinutes = parsed;

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.UserName ?? string.Empty)
            };

            foreach (var role in roles)
                claims.Add(new Claim(ClaimTypes.Role, role));

            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
            var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
                signingCredentials: credentials);

            return Task.FromResult(new JwtSecurityTokenHandler().WriteToken(token));
        }
    }
}