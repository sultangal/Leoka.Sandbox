using System.Runtime.CompilerServices;
using AutoMapper;
using LeokaEstetica.Platform.CallCenter.Abstractions.Ticket;
using LeokaEstetica.Platform.Core.Exceptions;
using LeokaEstetica.Platform.Core.Extensions;
using LeokaEstetica.Platform.Database.Abstractions.Ticket;
using LeokaEstetica.Platform.Database.Abstractions.User;
using LeokaEstetica.Platform.Models.Dto.Output.Ticket;
using LeokaEstetica.Platform.Models.Entities.Ticket;
using Microsoft.Extensions.Logging;

[assembly: InternalsVisibleTo("LeokaEstetica.Platform.Tests")]

namespace LeokaEstetica.Platform.CallCenter.Services.Ticket;

/// <summary>
/// Класс реализует методы сервиса тикетов.
/// </summary>
internal sealed class TicketService : ITicketService
{
    private readonly ITicketRepository _ticketRepository;
    private readonly ILogger<TicketService> _logger;
    private readonly IUserRepository _userRepository;
    private readonly IMapper _mapper;

    /// <summary>
    /// Конструктор.
    /// </summary>
    /// <param name="ticketRepository">Репозиторий тикетов.</param>
    /// <param name="ticketRepository">Логер.</param>
    /// <param name="userRepository">Репозиторий пользователя.</param>
    /// <param name="mapper">Автомаппер.</param>
    public TicketService(ITicketRepository ticketRepository, 
        ILogger<TicketService> logger, 
        IUserRepository userRepository, 
        IMapper mapper)
    {
        _ticketRepository = ticketRepository;
        _logger = logger;
        _userRepository = userRepository;
        _mapper = mapper;
    }

    #region Публичные методы.

    /// <summary>
    /// Метод получает список категорий тикетов.
    /// </summary>
    /// <param name="account">Аккаунт.</param>
    /// <returns>Категории тикетов.</returns>
    public async Task<IEnumerable<TicketCategoryEntity>> GetTicketCategoriesAsync()
    {
        try
        {
            var result = await _ticketRepository.GetTicketCategoriesAsync();

            return result;
        }
        
        catch (Exception ex)
        {
            _logger.LogError(ex, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Метод создает тикет.
    /// </summary>
    /// <param name="title">Название категории тикета.</param>
    /// <param name="message">Сообщение тикета.</param>
    /// <param name="account">Аккаунт.</param>
    public async Task CreateTicketAsync(string title, string message, string account)
    {
        try
        {
            var userId = await _userRepository.GetUserByEmailAsync(account);

            if (userId <= 0)
            {
                var ex = new NotFoundUserIdByAccountException(account);
                throw ex;
            }

            var createdTicketId = await _ticketRepository.CreateTicketAsync(title, message, userId);

            if (createdTicketId <= 0)
            {
                throw new InvalidOperationException("Ошибка при создании тикета." +
                                                    $"TicketId: {createdTicketId}." +
                                                    $"UserId: {userId}." +
                                                    $"Title: {title}." +
                                                    $"Message: {message}");
            }
        }
        
        catch (Exception ex)
        {
            _logger.LogError(ex, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Метод получает список тикетов для профиля пользователя.
    /// </summary>
    /// <param name="account">Аккаунт.</param>
    /// <returns>Список тикетов.</returns>
    public async Task<IEnumerable<TicketOutput>> GetUserProfileTicketsAsync(string account)
    {
        try
        {
            var userId = await _userRepository.GetUserByEmailAsync(account);

            if (userId <= 0)
            {
                var ex = new NotFoundUserIdByAccountException(account);
                throw ex;
            }

            var result = new List<TicketOutput>();

            var tickets = await _ticketRepository.GetUserProfileTicketsAsync(userId);

            if (!tickets.Any())
            {
                return result;
            }

            result = _mapper.Map<List<TicketOutput>>(tickets);

            await FillStatusNamesAsync(result);

            return result;
        }
        
        catch (Exception ex)
        {
            _logger.LogError(ex, ex.Message);
            throw;
        }
    }

    #endregion

    #region Приватные методы.

    /// <summary>
    /// Метод проставляет названия статусов тикетов.
    /// </summary>
    /// <param name="tickets">Список тикетов.</param>
    private async Task FillStatusNamesAsync(List<TicketOutput> tickets)
    {
        var ids = tickets.Select(s => s.TicketId);
        var statuses = await _ticketRepository.GetTicketStatusNamesAsync(ids);
        
        foreach (var t in tickets)
        {
            t.StatusName = statuses.TryGet(t.TicketId);
        }
    }

    #endregion
}