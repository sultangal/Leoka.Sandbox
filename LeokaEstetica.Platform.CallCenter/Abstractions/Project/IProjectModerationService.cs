using LeokaEstetica.Platform.CallCenter.Models.Dto.Output.Project;
using LeokaEstetica.Platform.Models.Dto.Input.Moderation;
using LeokaEstetica.Platform.Models.Dto.Output.Moderation.Project;
using LeokaEstetica.Platform.Models.Dto.Output.Project;
using LeokaEstetica.Platform.Models.Entities.Moderation;

namespace LeokaEstetica.Platform.CallCenter.Abstractions.Project;

/// <summary>
/// Абстракция модерации проектов.
/// </summary>
public interface IProjectModerationService
{
    /// <summary>
    /// Метод получает список проектов для модерации.
    /// </summary>
    /// <returns>Список проектов.</returns>
    Task<ProjectsModerationResult> ProjectsModerationAsync();

    /// <summary>
    /// Метод получает проект для просмотра/изменения.
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    /// <returns>Данные проекта.</returns>
    Task<ProjectOutput> GetProjectModerationByProjectIdAsync(long projectId);

    /// <summary>
    /// Метод одобряет проект на модерации.
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    /// <param name="account">Аккаунт.</param>
    /// <returns>Выходная модель модерации.</returns>
    Task<ApproveProjectOutput> ApproveProjectAsync(long projectId, string account);
    
    /// <summary>
    /// Метод отклоняет проект на модерации.
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    /// <param name="account">Аккаунт.</param>
    /// <returns>Выходная модель модерации.</returns>
    Task<RejectProjectOutput> RejectProjectAsync(long projectId, string account);

    /// <summary>
    /// Метод создает результаты проекта. 
    /// </summary>
    /// <param name="prj">Данные проекта.</param>
    /// <param name="token">Токен.</param>
    /// <returns>Результаты проекта.</returns>
    Task<IEnumerable<ProjectRemarkEntity>> CreateProjectRemarksAsync(
        CreateProjectRemarkInput createProjectRemarkInput, string account, string token);
}