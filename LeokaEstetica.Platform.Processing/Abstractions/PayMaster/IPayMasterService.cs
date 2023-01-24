using LeokaEstetica.Platform.Models.Dto.Input.Commerce.PayMaster;
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
    /// <param name="createOrderInput">Входная модель.</param>
    /// <param name="account">Аккаунт.</param>
    /// <returns>Данные платежа.</returns>
    Task<CreateOrderOutput> CreateOrderAsync(CreateOrderInput createOrderInput, string account);
}