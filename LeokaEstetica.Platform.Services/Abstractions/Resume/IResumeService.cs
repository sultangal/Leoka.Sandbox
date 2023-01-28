using LeokaEstetica.Platform.Models.Entities.Profile;

namespace LeokaEstetica.Platform.Services.Abstractions.Resume;

/// <summary>
/// Абстракция сервиса базы резюме.
/// </summary>
public interface IResumeService
{
    /// <summary>
    /// Метод получает список резюме.
    /// </summary>
    /// <returns>Список резюме.</returns>
    Task<List<ProfileInfoEntity>> GetProfileInfosAsync();

    /// <summary>
    /// Метод получает анкету пользователя.
    /// </summary>
    /// <param name="resumeId">Id анкеты пользователя.</param>
    /// <returns>Данные анкеты.</returns>
    Task<ProfileInfoEntity> GetResumeAsync(long resumeId);
}