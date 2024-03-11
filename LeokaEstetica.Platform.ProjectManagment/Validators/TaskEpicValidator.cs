using FluentValidation;
using LeokaEstetica.Platform.Core.Constants;

namespace LeokaEstetica.Platform.ProjectManagment.Validators;

/// <summary>
/// Класс валидатора эпика.
/// </summary>
public class TaskEpicValidator : AbstractValidator<(long EpicId, long ProjectId, string ProjectTaskId)>
{
    /// <summary>
    /// Конструктор.
    /// </summary>
    public TaskEpicValidator()
    {
        RuleFor(p => p.ProjectTaskId)
            .NotNull()
            .WithMessage(ValidationConst.ProjectManagmentValidation.NOT_VALID_PROJECT_TASK_ID)
            .NotEmpty()
            .WithMessage(ValidationConst.ProjectManagmentValidation.NOT_VALID_PROJECT_TASK_ID);
        
        RuleFor(p => p.ProjectId)
            .Must(p => p > 0)
            .WithMessage(ValidationConst.ProjectManagmentValidation.NOT_VALID_PROJECT_ID);
            
        RuleFor(p => p.EpicId)
            .Must(p => p > 0)
            .WithMessage(ValidationConst.ProjectManagmentValidation.NOT_VALID_EPIC_ID);
    }
}