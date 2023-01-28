using AutoMapper;
using LeokaEstetica.Platform.Base;
using LeokaEstetica.Platform.Core.Filters;
using LeokaEstetica.Platform.Database.Abstractions.User;
using LeokaEstetica.Platform.Finder.Abstractions.Resume;
using LeokaEstetica.Platform.Models.Dto.Output.Resume;
using LeokaEstetica.Platform.Services.Abstractions.Resume;
using LeokaEstetica.Platform.Services.Builders;
using Microsoft.AspNetCore.Mvc;

namespace LeokaEstetica.Platform.Controllers.Resume;

/// <summary>
/// TODO: Доступ к этому модулю только для пользователей, которые приобрели подписку.
/// Контроллер работы с резюме.
/// </summary>
[AuthFilter]
[ApiController]
[Route("resumes")]
public class ResumeController : BaseController
{
    private readonly IResumeService _resumeService;
    private readonly IMapper _mapper;
    private readonly IResumeFinderService _resumeFinderService;
    private readonly IResumePaginationService _resumePaginationService;
    private readonly IUserRepository _userRepository;

    /// <summary>
    /// Конструктор.
    /// </summary>
    /// <param name="resumeService">Сервис резюме.</param>
    /// <param name="mapper">Автомаппер.</param>
    /// <param name="resumeFinderService">Поисковый сервис резюме.</param>
    /// <param name="resumePaginationService">Сервис пагинации резюме.</param>
    /// <param name="userRepository">Репозиторий пользователя.</param>
    public ResumeController(IResumeService resumeService, 
        IMapper mapper, 
        IResumeFinderService resumeFinderService, 
        IResumePaginationService resumePaginationService, 
        IUserRepository userRepository)
    {
        _resumeService = resumeService;
        _mapper = mapper;
        _resumeFinderService = resumeFinderService;
        _resumePaginationService = resumePaginationService;
        _userRepository = userRepository;
    }

    /// <summary>
    /// Метод получает список резюме.
    /// </summary>
    /// <returns>Список резюме.</returns>
    [HttpGet]
    [Route("")]
    [ProducesResponseType(200, Type = typeof(ResumeResultOutput))]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    [ProducesResponseType(500)]
    [ProducesResponseType(404)]
    public async Task<ResumeResultOutput> GetProfileInfosAsync()
    {
        var items = await _resumeService.GetProfileInfosAsync();
        var result = new ResumeResultOutput
        {
            // Приводим к выходной модели.
            CatalogResumes = _mapper.Map<IEnumerable<ResumeOutput>>(items)
        };

        // Записываем коды пользователей.
        result.CatalogResumes = await FillUserCodesBuilder.Fill(result.CatalogResumes, _userRepository);

        return result;
    }

    /// <summary>
    /// Метод находит резюме по поисковому запросу.
    /// </summary>
    /// <param name="searchText">Поисковая строка.</param>
    /// <returns>Список резюме после поиска.</returns>
    [HttpGet]
    [Route("search")]
    [ProducesResponseType(200, Type = typeof(ResumeResultOutput))]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    [ProducesResponseType(500)]
    [ProducesResponseType(404)]
    public async Task<ResumeResultOutput> SearchResumesAsync([FromQuery] string searchText)
    {
        var result = await _resumeFinderService.SearchResumesAsync(searchText);

        return result;
    }
    
    /// <summary>
    /// Метод пагинации резюме.
    /// </summary>
    /// <param name="page">Номер страницы.</param>
    /// <returns>Список резюме.</returns>
    [HttpGet]
    [Route("pagination/{page}")]
    [ProducesResponseType(200, Type = typeof(PaginationResumeOutput))]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    [ProducesResponseType(500)]
    [ProducesResponseType(404)]
    public async Task<PaginationResumeOutput> GetResumesPaginationAsync([FromRoute] int page)
    {
        var result = await _resumePaginationService.GetResumesPaginationAsync(page);

        return result;
    }

    /// <summary>
    /// Метод получает анкету пользователя по ее 
    /// </summary>
    /// <param name="resumeId">Id анкеты пользователя.</param>
    /// <returns>Данные анкеты.</returns>
    [HttpGet]
    [Route("{resumeId}")]
    [ProducesResponseType(200, Type = typeof(ResumeOutput))]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    [ProducesResponseType(500)]
    [ProducesResponseType(404)]
    public async Task<ResumeOutput> GetResumeAsync([FromRoute] long resumeId)
    {
        var resume = await _resumeService.GetResumeAsync(resumeId);
        var result = _mapper.Map<ResumeOutput>(resume);

        return result;
    }
}