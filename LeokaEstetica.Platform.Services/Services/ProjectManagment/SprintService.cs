﻿using AutoMapper;
using Dapper;
using LeokaEstetica.Platform.Base.Abstractions.Repositories.User;
using LeokaEstetica.Platform.Core.Constants;
using LeokaEstetica.Platform.Core.Enums;
using LeokaEstetica.Platform.Core.Exceptions;
using LeokaEstetica.Platform.Core.Extensions;
using LeokaEstetica.Platform.Database.Abstractions.Config;
using LeokaEstetica.Platform.Database.Abstractions.ProjectManagment;
using LeokaEstetica.Platform.Integrations.Abstractions.Discord;
using LeokaEstetica.Platform.Models.Dto.Output.ProjectManagment;
using LeokaEstetica.Platform.Models.Dto.Output.Template;
using LeokaEstetica.Platform.Models.Enums;
using LeokaEstetica.Platform.Notifications.Abstractions;
using LeokaEstetica.Platform.Notifications.Consts;
using LeokaEstetica.Platform.Services.Abstractions.ProjectManagment;
using Microsoft.Extensions.Logging;

namespace LeokaEstetica.Platform.Services.Services.ProjectManagment;

/// <summary>
/// Класс реализует методы сервиса спринтов.
/// </summary>
internal sealed class SprintService : ISprintService
{
    private readonly ILogger<SprintService>? _logger;
    private readonly ISprintRepository _sprintRepository;
    private readonly IProjectManagementTemplateService _projectManagementTemplateService;
    private readonly IUserRepository _userRepository;
    private readonly IProjectSettingsConfigRepository _projectSettingsConfigRepository;
    private readonly IMapper _mapper;
    private readonly IProjectManagmentRepository _projectManagmentRepository;
    private readonly Lazy<IDistributionStatusTaskService> _distributionStatusTaskService;
    private readonly IDiscordService _discordService;
    private readonly ISprintNotificationsService _sprintNotificationsService;

    /// <summary>
    /// Список недопустимых для начала спринта статусов.
    /// </summary>
    private readonly List<SprintStatusEnum> _notAvailableStartSprintStatuses = new()
    {
        SprintStatusEnum.InWork,
        SprintStatusEnum.Completed,
        SprintStatusEnum.Closed
    };

    /// <summary>
    /// Конструктор.
    /// <param name="Логгер"></param>
    /// <param name="sprintRepository">Репозиторий спринтов.</param>
    /// <param name="projectManagementTemplateService">Сервис шаблонов проекта.</param>
    /// <param name="userRepository">Репозиторий пользователей.</param>
    /// <param name="mapper">Автомаппер.</param>
    /// <param name="projectManagmentRepository">Репозиторий модуля УП.</param>
    /// <param name="distributionStatusTaskService">Сервис распределения по статусам.</param>
    /// <param name="discordService">Сервис уведомлений дискорда.</param>
    /// <param name="sprintNotificationsService">Сервис уведомлений спринтов.</param>
    /// </summary>
    public SprintService(ILogger<SprintService>? logger,
        ISprintRepository sprintRepository,
        IProjectManagementTemplateService projectManagementTemplateService,
        IUserRepository userRepository,
        IProjectSettingsConfigRepository projectSettingsConfigRepository,
        IMapper mapper,
        IProjectManagmentRepository projectManagmentRepository,
        Lazy<IDistributionStatusTaskService> distributionStatusTaskService,
        IDiscordService discordService,
        ISprintNotificationsService sprintNotificationsService)
    {
        _logger = logger;
        _sprintRepository = sprintRepository;
        _projectManagementTemplateService = projectManagementTemplateService;
        _userRepository = userRepository;
        _projectSettingsConfigRepository = projectSettingsConfigRepository;
        _mapper = mapper;
        _projectManagmentRepository = projectManagmentRepository;
        _distributionStatusTaskService = distributionStatusTaskService;
        _discordService = discordService;
        _sprintNotificationsService = sprintNotificationsService;
    }

    #region Публичные методы

    /// <inheritdoc />
    public async Task<IEnumerable<TaskSprintExtendedOutput>> GetSprintsAsync(long projectId)
    {
        try
        {
            var result = await _sprintRepository.GetSprintsAsync(projectId);

            return result ?? Enumerable.Empty<TaskSprintExtendedOutput>();
        }
        
        catch (Exception ex)
        {
            _logger?.LogError(ex, ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<TaskSprintExtendedOutput> GetSprintAsync(long projectSprintId, long projectId, string account)
    {
        try
        {
            // Получаем данные спринта.
            var result = await _sprintRepository.GetSprintAsync(projectSprintId, projectId);

            if (result is null)
            {
                throw new InvalidOperationException("Не удалось получить детали спринта. " +
                                                    $"ProjectSprintId: {projectSprintId}. " +
                                                    $"ProjectId: {projectId}.");
            }
            
            var userId = await _userRepository.GetUserByEmailAsync(account);

            if (userId <= 0)
            {
                var ex = new NotFoundUserIdByAccountException(account);
                throw ex;
            }

            // TODO: Этот код дублируется в этом сервисе. Вынести в приватный метод и кортежем вернуть нужные данные.
            // Получаем настройки проекта.
            var projectSettings = await _projectSettingsConfigRepository.GetProjectSpaceSettingsByProjectIdAsync(
                projectId, userId);
            var projectSettingsItems = projectSettings?.AsList();

            if (projectSettingsItems is null || !projectSettingsItems.Any())
            {
                throw new InvalidOperationException("Ошибка получения настроек проекта. " +
                                                    $"ProjectId: {projectId}. " +
                                                    $"UserId: {userId}");
            }

            var template = projectSettingsItems.Find(x =>
                x.ParamKey.Equals(GlobalConfigKeys.ConfigSpaceSetting.PROJECT_MANAGEMENT_TEMPLATE_ID));
            var templateId = Convert.ToInt32(template!.ParamValue);

            // Получаем набор статусов, которые входят в выбранный шаблон.
            var items = await _projectManagementTemplateService.GetProjectManagmentTemplatesAsync(templateId);
            var templateStatusesItems = _mapper.Map<IEnumerable<ProjectManagmentTaskTemplateResult>>(items);
            var statuses = templateStatusesItems?.AsList();

            if (statuses is null || !statuses.Any())
            {
                throw new InvalidOperationException("Не удалось получить набор статусов шаблона." +
                                                    $" TemplateId: {templateId}." +
                                                    $"ProjectId: {projectId}.");
            }

            // Проставляем Id шаблона статусам.
            await _projectManagementTemplateService.SetProjectManagmentTemplateIdsAsync(statuses);

            // Получаем выбранную пользователем стратегию представления.
            var strategy = await _projectManagmentRepository.GetProjectUserStrategyAsync(projectId, userId);
            
            // Добавляем в результат статусы.
            var projectManagmentTaskStatuses = statuses.First().ProjectManagmentTaskStatusTemplates.AsList();

            // Получаем задачи спринта, если они есть.
            // Это могут быть задачи, ошибки, истории, эпики - все, что может входить в спринт.
            var sprintTasks = (await _sprintRepository.GetProjectSprintTasksAsync(projectId, projectSprintId,
                strategy!))?.AsList();

            if (sprintTasks is not null && sprintTasks.Any() && projectManagmentTaskStatuses.Any())
            {
                // Заполняем статусы задачами.
                await _distributionStatusTaskService.Value.DistributionStatusTaskAsync(projectManagmentTaskStatuses,
                    sprintTasks, ModifyTaskStatuseTypeEnum.Sprint, projectId, null, strategy!);

                // Делаем плоский вид, чтобы отобразить в обычной таблице на фронте.
                result.SprintTasks = projectManagmentTaskStatuses
                    .SelectMany(x => (x.ProjectManagmentTasks ?? new List<ProjectManagmentTaskOutput>())
                        .Select(y => y)
                        .OrderBy(o => o.Created));
            }
            
            // Заполняем доп.поля деталей спринта.
            await ModificateSprintDetailsAsync(result);

            return result;
        }
        
        catch (Exception ex)
        {
            _logger?.LogError(ex, ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UpdateSprintNameAsync(long projectSprintId, long projectId, string sprintName, string account)
    {
        try
        {
            var userId = await _userRepository.GetUserByEmailAsync(account);

            if (userId <= 0)
            {
                var ex = new NotFoundUserIdByAccountException(account);
                throw ex;
            }

            await _sprintRepository.UpdateSprintNameAsync(projectSprintId, projectId, sprintName);

            // TODO: Добавить запись активности (кто изменил название спринта).
        }
        
        catch (Exception ex)
        {
            _logger?.LogError(ex, ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UpdateSprintDetailsAsync(long projectSprintId, long projectId, string sprintDetails,
        string account)
    {
        try
        {
            var userId = await _userRepository.GetUserByEmailAsync(account);

            if (userId <= 0)
            {
                var ex = new NotFoundUserIdByAccountException(account);
                throw ex;
            }

            await _sprintRepository.UpdateSprintDetailsAsync(projectSprintId, projectId, sprintDetails);

            // TODO: Добавить запись активности (кто изменил описание спринта).
        }
        
        catch (Exception ex)
        {
            _logger?.LogError(ex, ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task InsertOrUpdateSprintExecutorAsync(long projectSprintId, long projectId, long executorId,
        string account)
    {
        try
        {
            var userId = await _userRepository.GetUserByEmailAsync(account);

            if (userId <= 0)
            {
                var ex = new NotFoundUserIdByAccountException(account);
                throw ex;
            }
            
            await _sprintRepository.InsertOrUpdateSprintExecutorAsync(projectSprintId, projectId, executorId);
            
            // TODO: Добавить запись активности (кто назначил/обновил исполнителя спринта).
        }
        
        catch (Exception ex)
        {
            _logger?.LogError(ex, ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task InsertOrUpdateSprintWatchersAsync(long projectSprintId, long projectId,
        IEnumerable<long> watcherIds, string account)
    {
        try
        {
            var userId = await _userRepository.GetUserByEmailAsync(account);

            if (userId <= 0)
            {
                var ex = new NotFoundUserIdByAccountException(account);
                throw ex;
            }
            
            await _sprintRepository.InsertOrUpdateSprintWatchersAsync(projectSprintId, projectId, watcherIds);
            
            // TODO: Добавить запись активности (кто назначил/обновил наблюдателей спринта).
        }
        
        catch (Exception ex)
        {
            _logger?.LogError(ex, ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task StartSprintAsync(long projectSprintId, long projectId, string account, string token)
    {
        try
        {
            var userId = await _userRepository.GetUserByEmailAsync(account);

            if (userId <= 0)
            {
                var ex = new NotFoundUserIdByAccountException(account);
                throw ex;
            }
            
            // Ищем уже активный спринт проекта.
            var activeSprint = await _sprintRepository.CheckActiveSprintAsync(projectId);

            // Нельзя начать спринт проекта, если уже есть активный спринт у проекта. 
            if (activeSprint)
            {
                var ex = new InvalidOperationException("У проекта уже имеется запущенный спринт. " +
                                                       "Начать новый невозможно. " +
                                                       $"ProjectSprintId: {projectSprintId}. " +
                                                       $"ProjectId: {projectId}.");
                await _discordService.SendNotificationErrorAsync(ex);
                
                _logger?.LogError(ex, ex.Message);

                if (!string.IsNullOrWhiteSpace(token))
                {
                    await _sprintNotificationsService.SendNotificationWarningStartSprintAsync("Внимание",
                        ex.Message, NotificationLevelConsts.NOTIFICATION_LEVEL_WARNING, token);
                }

                return;
            }

            // Получаем данные спринта.
            var sprint = await _sprintRepository.GetSprintAsync(projectSprintId, projectId);

            if (sprint is null)
            {
                var ex = new InvalidOperationException("Не удалось получить данные спринта. " +
                                                       $"ProjectSprintId: {projectSprintId}. " +
                                                       $"ProjectId: {projectId}.");
                await _discordService.SendNotificationErrorAsync(ex);
                
                _logger?.LogError(ex, ex.Message);
                
                if (!string.IsNullOrWhiteSpace(token))
                {
                    await _sprintNotificationsService.SendNotificationWarningStartSprintAsync("Внимание",
                        ex.Message, NotificationLevelConsts.NOTIFICATION_LEVEL_WARNING, token);
                }

                return;
            }
            
            // Проверяем даты на корректность.
            if (!sprint.DateStart.HasValue || !sprint.DateEnd.HasValue)
            {
                // Нельзя начать спринт - даты не заполнены.
                var ex = new InvalidOperationException(
                    "Нельзя начать спринт - даты (начала и окончания) не заполнены. " +
                    $"ProjectSprintId: {projectSprintId}. " +
                    $"ProjectId: {projectId}.");
                await _discordService.SendNotificationErrorAsync(ex);
                
                _logger?.LogError(ex, ex.Message);
                
                if (!string.IsNullOrWhiteSpace(token))
                {
                    await _sprintNotificationsService.SendNotificationWarningStartSprintAsync("Внимание",
                        ex.Message, NotificationLevelConsts.NOTIFICATION_LEVEL_WARNING, token);
                }

                return;
            }
            
            if (sprint.DateEnd!.Value < DateTime.UtcNow)
            {
                // Нельзя начать спринт - какая-то из дат либо обе находятся в прошлом.
                var ex = new InvalidOperationException(
                    "Нельзя начать спринт - какая-то из дат либо обе находятся в прошлом. " +
                    $"ProjectSprintId: {projectSprintId}. " +
                    $"ProjectId: {projectId}.");
                await _discordService.SendNotificationErrorAsync(ex);
                
                _logger?.LogError(ex, ex.Message);
                
                if (!string.IsNullOrWhiteSpace(token))
                {
                    await _sprintNotificationsService.SendNotificationWarningStartSprintAsync("Внимание",
                        ex.Message, NotificationLevelConsts.NOTIFICATION_LEVEL_WARNING, token);
                }

                return;
            }

            if (_notAvailableStartSprintStatuses.Contains((SprintStatusEnum)sprint.SprintStatusId))
            {
                // Обнаружен недопустимый для старта спринта статус.
                var ex = new InvalidOperationException(
                    "Обнаружен недопустимый для старта спринта статус. " +
                    $"ProjectSprintId: {projectSprintId}. " +
                    $"ProjectId: {projectId}. " +
                    $"SprintStatus: {((SprintStatusEnum)sprint.SprintStatusId).ToString()}.");
                await _discordService.SendNotificationErrorAsync(ex);
                
                _logger?.LogError(ex, ex.Message);
                
                if (!string.IsNullOrWhiteSpace(token))
                {
                    await _sprintNotificationsService.SendNotificationWarningStartSprintAsync("Внимание",
                        ex.Message, NotificationLevelConsts.NOTIFICATION_LEVEL_WARNING, token);
                }

                return;
            }
            
            // Проверяем, имеет ли спринт задачи.
            var sprintTaskCount = await _sprintRepository.GetCountSprintTasksAsync(projectSprintId, projectId);
            
            if (sprintTaskCount == 0)
            {
                // Нельзя начать пустой спринт.
                var ex = new InvalidOperationException(
                    "Нельзя начать пустой спринт. " +
                    $"ProjectSprintId: {projectSprintId}. " +
                    $"ProjectId: {projectId}. " +
                    $"SprintStatus: {((SprintStatusEnum)sprint.SprintStatusId).ToString()}.");
                await _discordService.SendNotificationErrorAsync(ex);
                
                _logger?.LogError(ex, ex.Message);
                
                if (!string.IsNullOrWhiteSpace(token))
                {
                    await _sprintNotificationsService.SendNotificationWarningStartSprintAsync("Внимание",
                        ex.Message, NotificationLevelConsts.NOTIFICATION_LEVEL_WARNING, token);
                }

                return;
            }
            
            // Запускаем спринт проекта.
            await _sprintRepository.RunSprintAsync(projectSprintId, projectId);
            
            if (!string.IsNullOrWhiteSpace(token))
            {
                await _sprintNotificationsService.SendNotificationSuccessStartSprintAsync("Все хорошо",
                    $"Спринт \"{sprint.SprintName}\" успешно начат.", NotificationLevelConsts.NOTIFICATION_LEVEL_SUCCESS,
                    token);
            }

            // TODO: Добавить запись активности (кто начал спринт).
        }
        
        catch (Exception ex)
        {
            _logger?.LogError(ex, ex.Message);
            throw;
        }
    }

    #endregion

    #region Приватные методы.

    /// <summary>
    /// Метод заполняет доп.поля деталей спринта.
    /// </summary>
    /// <param name="sprintData">Данные спринта до модификации.</param>
    private async Task ModificateSprintDetailsAsync(TaskSprintExtendedOutput sprintData)
    {
        // Заполняем название исполнителя, если он задан у спринта.
        if (sprintData.ExecutorId.HasValue)
        {
            var executors = await _userRepository.GetExecutorNamesByExecutorIdsAsync(
                new[] { sprintData.ExecutorId.Value });

            if (executors.TryGet(sprintData.ExecutorId.Value) is not null)
            {
                sprintData.ExecutorName = executors.TryGet(sprintData.ExecutorId.Value)?.FullName.Trim();
            }
        }
        
        // Заполняем наблюдателей, если они заданы у спринта.
        if (sprintData.WatcherIds is not null && sprintData.WatcherIds.Any())
        {
            var watchers = await _userRepository.GetWatcherNamesByWatcherIdsAsync(sprintData.WatcherIds);
            
            // Названия наблюдателей задачи.
            if (watchers is not null && watchers.Count > 0)
            {
                foreach (var w in sprintData.WatcherIds)
                {
                    var watcher = watchers.TryGet(w)?.FullName;
                            
                    // Если такое бахнуло, то не добавляем в список, но и не ломаем приложение.
                    // Просто логируем такое.
                    if (watcher is null)
                    {
                        var ex = new InvalidOperationException("Обнаружен наблюдатель с NULL. " +
                                                               $"WatcherId: {w}");
                        await _discordService.SendNotificationErrorAsync(ex);
                        _logger?.LogError(ex, ex.Message);
                                
                        continue;
                    }

                    if (sprintData.WatcherNames is null)
                    {
                        sprintData.WatcherNames = new List<string>();   
                    }

                    sprintData.WatcherNames.Add(watcher);
                }
            }
        }
        
        // Заполняем автора (кто создал спринт).
        var authors = await _userRepository.GetAuthorNamesByAuthorIdsAsync(new [] { sprintData.CreatedBy });
        
        if (authors.Count == 0)
        {
            throw new InvalidOperationException("Не удалось получить авторов задач.");
        }
        
        sprintData.AuthorName = authors.TryGet(sprintData.CreatedBy)?.FullName;
    }

    #endregion
}