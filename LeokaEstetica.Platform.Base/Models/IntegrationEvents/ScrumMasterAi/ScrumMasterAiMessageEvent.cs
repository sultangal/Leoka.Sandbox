﻿using LeokaEstetica.Platform.Base.Models.Dto;
using LeokaEstetica.Platform.Core.Enums;

namespace LeokaEstetica.Platform.Base.Models.IntegrationEvents.ScrumMasterAi;

/// <summary>
/// Класс события
/// </summary>
public class ScrumMasterAiMessageEvent : BaseEventMessage
{
    /// <summary>
    /// Конструктор.
    /// </summary>
    /// <param name="message">Сообщение.</param>
    /// <param name="connectionId">Id подключения сокетов.</param>
    /// <param name="userId">Id пользователя.</param>
    /// <param name="scrumMasterAiEventType">Тип ивента нейросети.</param>
    public ScrumMasterAiMessageEvent(string? message, string? connectionId, long userId,
        ScrumMasterAiEventTypeEnum scrumMasterAiEventType)
    {
        Message = message;
        ConnectionId = connectionId;
        UserId = userId;
        ScrumMasterAiEventType = scrumMasterAiEventType;
    }

    /// <summary>
    /// Сообщение.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// <param name="connectionId">Id подключения сокетов.</param>
    /// </summary>
    public string? ConnectionId { get; }

    /// <summary>
    /// Id пользователя.
    /// </summary>
    public long UserId { get; }

    /// <summary>
    /// Тип ивента нейросети.
    /// </summary>
    public ScrumMasterAiEventTypeEnum ScrumMasterAiEventType { get; }
}