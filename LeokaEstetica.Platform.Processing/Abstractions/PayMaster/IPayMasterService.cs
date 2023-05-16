using LeokaEstetica.Platform.Models.Dto.Output.Commerce.PayMaster;

namespace LeokaEstetica.Platform.Processing.Abstractions.PayMaster;

/// <summary>
/// Абстракция сервиса работы с платежной системой PayMaster.
/// </summary>
public interface IPayMasterService
{
    /// <summary>
    /// Метод создает заказ.
    /// </summary>
    /// <param name="publicId">Публичный ключ тарифа.</param>
    /// <param name="account">Аккаунт.</param>
    /// <param name="token">Токен пользователя.</param>
    /// <returns>Данные платежа.</returns>
    Task<CreateOrderOutput> CreateOrderAsync(Guid publicId, string account, string token);
}