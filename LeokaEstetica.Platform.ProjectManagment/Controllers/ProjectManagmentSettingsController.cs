using AutoMapper;
using LeokaEstetica.Platform.Base;
using LeokaEstetica.Platform.Base.Filters;
using LeokaEstetica.Platform.Database.Abstractions.Template;
using LeokaEstetica.Platform.Integrations.Abstractions.Pachca;
using LeokaEstetica.Platform.Models.Dto.Input.ProjectManagement;
using LeokaEstetica.Platform.Models.Dto.Output.ProjectManagment;
using LeokaEstetica.Platform.ProjectManagment.ValidationModels;
using LeokaEstetica.Platform.ProjectManagment.Validators;
using LeokaEstetica.Platform.Services.Abstractions.ProjectManagment;
using Microsoft.AspNetCore.Mvc;

namespace LeokaEstetica.Platform.ProjectManagment.Controllers;

/// <summary>
/// Контроллер управления проектами.
/// </summary>
[ApiController]
[Route("project-managment-settings")]
[AuthFilter]
public class ProjectManagmentSettingsController : BaseController
{
    private readonly ILogger<ProjectManagmentController> _logger;
    private readonly Lazy<IProjectManagmentTemplateRepository> _projectManagmentTemplateRepository;
    private readonly Lazy<IPachcaService> _pachcaService;
    private readonly IProjectManagmentService _projectManagmentService;
    private readonly IMapper _mapper;

    /// <summary>
    /// Конструктор.
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="projectManagmentTemplateRepository"></param>
    /// <param name="pachcaService"></param>
    /// <param name="projectManagmentService"></param>
    /// <param name="mapper"></param>
    public ProjectManagmentSettingsController(ILogger<ProjectManagmentController> logger,
        Lazy<IProjectManagmentTemplateRepository> projectManagmentTemplateRepository,
        Lazy<IPachcaService> pachcaService,
        IProjectManagmentService projectManagmentService,
         IMapper mapper)
    {
        _logger = logger;
        _projectManagmentTemplateRepository = projectManagmentTemplateRepository;
        _pachcaService = pachcaService;
        _projectManagmentService = projectManagmentService;
        _mapper = mapper;
    }
    
    /// <summary>
    /// Метод создает метку (тег) для задач пользователя.
    /// </summary>
    /// <param name="userTaskTagInput">Входная модель.</param>
    [HttpPost]
    [Route("user-tag")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    [ProducesResponseType(500)]
    [ProducesResponseType(404)]
    public async Task CreateUserTaskTagAsync([FromBody] UserTaskTagInput userTaskTagInput)
    {
        var validator = await new CreateUserTaskTagValidator().ValidateAsync(userTaskTagInput);

        if (validator.Errors.Any())
        {
            var exceptions = new List<InvalidOperationException>();

            foreach (var err in validator.Errors)
            {
                exceptions.Add(new InvalidOperationException(err.ErrorMessage));
            }

            var ex = new AggregateException("Ошибка создания метки (тега).",
                exceptions);
            _logger.LogError(ex, ex.Message);
            
            await _pachcaService.Value.SendNotificationErrorAsync(ex);
            
            throw ex;
        }

        await _projectManagmentService.CreateUserTaskTagAsync(userTaskTagInput.TagName,
            userTaskTagInput.TagDescription, GetUserName());
    }

    /// <summary>
    /// Метод получает список статусов для выбора для создания нового статуса.
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    /// <returns>Список статусов.</returns>
    [HttpGet]
    [Route("select-create-task-statuses")]
    [ProducesResponseType(200, Type = typeof(IEnumerable<TaskStatusOutput>))]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    [ProducesResponseType(500)]
    [ProducesResponseType(404)]
    public async Task<IEnumerable<TaskStatusOutput>> GetSelectableTaskStatusesAsync([FromQuery] long projectId)
    {
        var validator = await new GetTaskStatusValidator().ValidateAsync(
            new GetTaskStatusValidationModel(projectId));

        if (validator.Errors.Any())
        {
            var exceptions = new List<InvalidOperationException>();

            foreach (var err in validator.Errors)
            {
                exceptions.Add(new InvalidOperationException(err.ErrorMessage));
            }
            
            var ex = new AggregateException("Ошибка получения статусов для создания нового статуса.", exceptions);
            _logger.LogError(ex, ex.Message);
            
            await _pachcaService.Value.SendNotificationErrorAsync(ex);
            
            throw ex;
        }
        
        // Получаем шаблон, по которому управляется проект.
        var templateId = await _projectManagmentTemplateRepository.Value.GetProjectTemplateIdAsync(projectId);

        if (!templateId.HasValue || templateId.Value <= 0)
        {
            throw new InvalidOperationException($"Не удалось получить шаблон проекта. ProjectId: {projectId}");
        }

        var items = (await _projectManagmentService.GetSelectableTaskStatusesAsync(projectId, templateId.Value))
            .ToList();
        var result = _mapper.Map<IEnumerable<TaskStatusOutput>>(items);
        var resultItems = result.ToList();
        
        // Проставляем шаблон для выходной модели.
        foreach (var s in resultItems)
        {
            s.TemplateId = templateId.Value;
        }

        return resultItems;
    }

    /// <summary>
    /// Метод создает новый статус шаблона пользователя учитывая ассоциацию статуса.
    /// </summary>
    /// <param name="createTaskStatusInput">Входная модель.</param>
    /// <exception cref="AggregateException">Если входные параметры не прошли валидацию.</exception>
    [HttpPost]
    [Route("user-task-status")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    [ProducesResponseType(500)]
    [ProducesResponseType(404)]
    public async Task CreateUserTaskStatusTemplateAsync([FromBody] CreateTaskStatusInput createTaskStatusInput)
    {
        var validator = await new CreateTaskStatusValidator().ValidateAsync(createTaskStatusInput);

        if (validator.Errors.Any())
        {
            var exceptions = new List<InvalidOperationException>();

            foreach (var err in validator.Errors)
            {
                exceptions.Add(new InvalidOperationException(err.ErrorMessage));
            }
            
            var ex = new AggregateException("Ошибка получения статусов для создания нового статуса.", exceptions);
            _logger.LogError(ex, ex.Message);
            
            await _pachcaService.Value.SendNotificationErrorAsync(ex);
            
            throw ex;
        }

        await _projectManagmentService.CreateUserTaskStatusTemplateAsync(
            createTaskStatusInput.AssociationStatusSysName, createTaskStatusInput.StatusName,
            createTaskStatusInput.StatusDescription, createTaskStatusInput.ProjectId, GetUserName());
    }
}