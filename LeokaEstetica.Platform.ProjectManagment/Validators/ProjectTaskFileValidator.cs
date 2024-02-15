using FluentValidation;
using LeokaEstetica.Platform.Core.Constants;

namespace LeokaEstetica.Platform.ProjectManagment.Validators;

/// <summary>
/// Класс валидатора файлов задачи проекта
/// </summary>
public class ProjectTaskFileValidator : AbstractValidator<(long ProjectId, long ProjectTaskId)>
{
    /// <summary>
    /// Конструктор.
    /// </summary>
    public ProjectTaskFileValidator()
    {
        RuleFor(p => p.ProjectId)
            .Must(p => p > 0)
            .WithMessage(ValidationConst.ProjectManagmentValidation.NOT_VALID_TASK_FROM_LINK);

        RuleFor(p => p.ProjectTaskId)
            .Must(p => p > 0)
            .WithMessage(ValidationConst.ProjectManagmentValidation.NOT_VALID_PROJECT_TASK_ID);
    }
}