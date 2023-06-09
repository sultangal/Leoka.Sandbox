using System.Runtime.CompilerServices;
using LeokaEstetica.Platform.Core.Exceptions;
using LeokaEstetica.Platform.Database.Abstractions.User;
using LeokaEstetica.Platform.Models.Entities.User;
using LeokaEstetica.Platform.Notifications.Abstractions;
using LeokaEstetica.Platform.Notifications.Consts;
using LeokaEstetica.Platform.Services.Abstractions.Search.Project;
using Microsoft.Extensions.Logging;

[assembly: InternalsVisibleTo("LeokaEstetica.Platform.Tests")]

namespace LeokaEstetica.Platform.Services.Services.Search.Project;

/// <summary>
/// Класс реализует методы сервиса поиска в проектах.
/// </summary>
internal sealed class ProjectFinderService : IProjectFinderService
{
    private readonly ILogger<ProjectFinderService> _logger;
    private readonly IUserRepository _userRepository;
    private readonly IProjectNotificationsService _projectNotificationsService;

    public ProjectFinderService(ILogger<ProjectFinderService> logger,
        IUserRepository userRepository,
        IProjectNotificationsService projectNotificationsService)
    {
        _logger = logger;
        _userRepository = userRepository;
        _projectNotificationsService = projectNotificationsService;
    }

    /// <summary>
    /// Метод ищет пользователей для приглашения в команду проекта.
    /// </summary>
    /// <param name="searchText">Поисковый запрос.</param>
    /// <param name="account">Аккаунт.</param>
    /// <param name="token">Токен пользователя.</param>
    /// <returns>Список пользователей, которых можно пригласить в команду проекта.</returns>
    public async Task<IEnumerable<UserEntity>> SearchInviteProjectMembersAsync(string searchText, string account,
        string token)
    {
        try
        {
            var users = await _userRepository.GetUserByEmailOrLoginAsync(searchText);

            // Если не удалось найти таких пользователей.
            if (!users.Any())
            {
                var userId = await _userRepository.GetUserByEmailAsync(account);

                if (userId <= 0)
                {
                    throw new NotFoundUserIdByAccountException(account);
                }
                
                var ex = new InvalidOperationException($"Пользователя по поисковому запросу {searchText} не найдено.");
                _logger.LogError(ex, "Ошибка поиска пользователей для приглашения в команду проекта. " +
                                     $"Поисковая строка была {searchText}");
                
                await _projectNotificationsService.SendNotificationWarningSearchProjectTeamMemberAsync(
                    "Внимание",
                    $"По запросу \"{searchText}\" не удалось найти пользователей. Попробуйте изменить запрос.",
                    NotificationLevelConsts.NOTIFICATION_LEVEL_WARNING, token);
                throw ex;
            }

            return users;
        }

        catch (Exception ex)
        {
            _logger.LogError(ex, ex.Message);
            throw;
        }
    }
}