using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO.Admin;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Реализация административного сервиса пользователей через ASP.NET Identity.
    /// </summary>
    public class UserAdminService : IUserAdminService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole<int>> _roleManager;
        private readonly ILogger<UserAdminService> _logger;

        /// <summary>
        /// Создаёт экземпляр сервиса.
        /// </summary>
        /// <param name="userManager">Менеджер пользователей</param>
        /// <param name="roleManager">Менеджер ролей</param>
        /// <param name="logger">Логгер</param>
        public UserAdminService(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole<int>> roleManager,
            ILogger<UserAdminService> logger)
        {
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _roleManager = roleManager ?? throw new ArgumentNullException(nameof(roleManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<UserListDto>> GetAllAsync()
        {
            var users = await _userManager.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync();
            var result = new List<UserListDto>(users.Count);

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                result.Add(new UserListDto
                {
                    Id = user.Id,
                    Email = user.Email,
                    FullName = user.FullName,
                    IsActive = user.IsActive,
                    RegisteredAt = user.RegisteredAt,
                    Roles = roles.ToList()
                });
            }

            return result;
        }

        /// <inheritdoc />
        public async Task<UserEditDto> GetByIdAsync(int id)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return null;

            var roles = await _userManager.GetRolesAsync(user);

            return new UserEditDto
            {
                Id = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                IsActive = user.IsActive,
                Role = roles.FirstOrDefault() ?? "User"
            };
        }

        /// <inheritdoc />
        public async Task<UserListDto> CreateAsync(UserEditDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (string.IsNullOrWhiteSpace(dto.Email))
                throw new ArgumentException("Email обязателен", nameof(dto));
            if (string.IsNullOrWhiteSpace(dto.Password))
                throw new ArgumentException("Пароль обязателен при создании", nameof(dto));

            var user = new ApplicationUser
            {
                UserName = dto.Email,
                Email = dto.Email,
                EmailConfirmed = true,
                FullName = dto.FullName ?? dto.Email,
                IsActive = dto.IsActive,
                RegisteredAt = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(user, dto.Password);
            if (!result.Succeeded)
            {
                var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Не удалось создать пользователя: {errors}");
            }

            var role = string.IsNullOrWhiteSpace(dto.Role) ? "User" : dto.Role;
            if (await _roleManager.RoleExistsAsync(role))
                await _userManager.AddToRoleAsync(user, role);

            _logger.LogInformation("Администратор создал пользователя {Email} с ролью {Role}", dto.Email, role);

            var roles = await _userManager.GetRolesAsync(user);
            return new UserListDto
            {
                Id = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                IsActive = user.IsActive,
                RegisteredAt = user.RegisteredAt,
                Roles = roles.ToList()
            };
        }

        /// <inheritdoc />
        public async Task<UserListDto> UpdateAsync(int id, UserEditDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return null;

            user.FullName = dto.FullName ?? user.FullName;
            user.IsActive = dto.IsActive;

            if (!string.Equals(user.Email, dto.Email, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(dto.Email))
            {
                user.Email = dto.Email;
                user.UserName = dto.Email;
            }

            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                var errors = string.Join("; ", updateResult.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Не удалось обновить пользователя: {errors}");
            }

            // Обновление роли
            if (!string.IsNullOrWhiteSpace(dto.Role))
            {
                var currentRoles = await _userManager.GetRolesAsync(user);
                if (!currentRoles.Contains(dto.Role))
                {
                    await _userManager.RemoveFromRolesAsync(user, currentRoles);
                    if (await _roleManager.RoleExistsAsync(dto.Role))
                        await _userManager.AddToRoleAsync(user, dto.Role);
                }
            }

            _logger.LogInformation("Администратор обновил пользователя {Email}", user.Email);

            var roles = await _userManager.GetRolesAsync(user);
            return new UserListDto
            {
                Id = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                IsActive = user.IsActive,
                RegisteredAt = user.RegisteredAt,
                Roles = roles.ToList()
            };
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(int id)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return false;

            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Не удалось удалить пользователя: {errors}");
            }

            _logger.LogInformation("Администратор удалил пользователя {Email}", user.Email);
            return true;
        }

        /// <inheritdoc />
        public async Task<bool> ResetPasswordAsync(int id, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword))
                throw new ArgumentException("Новый пароль обязателен", nameof(newPassword));

            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return false;

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, newPassword);

            if (!result.Succeeded)
            {
                var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Не удалось сбросить пароль: {errors}");
            }

            _logger.LogInformation("Администратор сбросил пароль пользователя {Email}", user.Email);
            return true;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> GetRolesAsync()
        {
            var roles = await _roleManager.Roles
                .AsNoTracking()
                .OrderBy(r => r.Name)
                .Select(r => r.Name)
                .ToListAsync();
            return roles;
        }
    }
}