using Dapper;
using LeokaEstetica.Platform.Base.Abstractions.Connection;
using LeokaEstetica.Platform.Base.Abstractions.Repositories.Base;
using LeokaEstetica.Platform.Base.Abstractions.Repositories.Chat;
using LeokaEstetica.Platform.Core.Data;
using LeokaEstetica.Platform.Core.Enums;
using LeokaEstetica.Platform.Core.Exceptions;
using LeokaEstetica.Platform.Core.Helpers;
using LeokaEstetica.Platform.Database.Abstractions.Project;
using LeokaEstetica.Platform.Models.Dto.Input.Project;
using LeokaEstetica.Platform.Models.Dto.Output.Moderation.Project;
using LeokaEstetica.Platform.Models.Dto.Output.Project;
using LeokaEstetica.Platform.Models.Dto.Output.Vacancy;
using LeokaEstetica.Platform.Models.Entities.Communication;
using LeokaEstetica.Platform.Models.Entities.Configs;
using LeokaEstetica.Platform.Models.Entities.Moderation;
using LeokaEstetica.Platform.Models.Entities.Project;
using LeokaEstetica.Platform.Models.Entities.ProjectTeam;
using LeokaEstetica.Platform.Models.Entities.Vacancy;
using LeokaEstetica.Platform.Models.Enums;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace LeokaEstetica.Platform.Database.Repositories.Project;

/// <summary>
/// Класс реализует метод репозитория проектов.
/// </summary>
internal sealed class ProjectRepository : BaseRepository, IProjectRepository
{
	private readonly PgContext _pgContext;
	private readonly IChatRepository _chatRepository;

	/// <summary>
	/// Список статусов вакансий, которые надо исключать при атаче вакансий к проекту.
	/// </summary>
	private static readonly List<long> _excludedVacanciesStatuses = new()
	{
		(int)VacancyModerationStatusEnum.ModerationVacancy,
		(int)VacancyModerationStatusEnum.RejectedVacancy
	};

	/// <summary>
	/// Конструктор.
	/// </summary>
	/// <param name="pgContext">Датаконтекст.</param>
	/// <param name="chatRepository">Репозиторий чата.</param>
	public ProjectRepository(PgContext pgContext,
		IChatRepository chatRepository,
		IConnectionProvider connectionProvider) : base(connectionProvider)
	{
		_pgContext = pgContext;
		_chatRepository = chatRepository;
	}

    /// <summary>
    /// Метод фильтрует проекты в зависимости от фильтров.
    /// </summary>
    /// <param name="filters">Фильтры.</param>
    /// <returns>Список проектов после фильтрации.</returns>
    public async Task<IEnumerable<CatalogProjectOutput>> FilterProjectsAsync(FilterProjectInput filters)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        
        var parameters = new DynamicParameters();

        var query =
            "SELECT u.\"ProjectId\", u.\"ProjectName\", u.\"DateCreated\", u.\"ProjectIcon\", u.\"ProjectDetails\"," + 
            " u.\"UserId\", p0.\"StageSysName\" AS \"ProjectStageSysName\" " +
            "FROM \"Projects\".\"CatalogProjects\" AS c " +
            "         INNER JOIN \"Projects\".\"UserProjects\" AS u ON c.\"ProjectId\" = u.\"ProjectId\" " +
            "         LEFT JOIN \"Moderation\".\"Projects\" AS p ON u.\"ProjectId\" = p.\"ProjectId\" " +
            "         INNER JOIN \"Subscriptions\".\"UserSubscriptions\" AS u0 ON u.\"UserId\" = u0.\"UserId\" " +
            "         INNER JOIN \"Subscriptions\".\"Subscriptions\" AS s ON u0.\"SubscriptionId\" = s.\"ObjectId\" " +
            "         INNER JOIN \"Projects\".\"UserProjectsStages\" AS u1 ON u.\"ProjectId\" = u1.\"ProjectId\" " +
            "         INNER JOIN \"Projects\".\"UserProjects\" AS u2 ON c.\"ProjectId\" = u2.\"ProjectId\" " +
            "         INNER JOIN \"Projects\".\"ProjectStages\" AS p0 ON p0.\"StageId\" = u1.\"StageId\" " +
            "WHERE " +
            "    (NOT (EXISTS ( " +
            "        SELECT 1 " +
            "        FROM \"Projects\".\"ArchivedProjects\" AS a " +
            "        WHERE a.\"ProjectId\" = u.\"ProjectId\")) AND u.\"IsPublic\") " +
            "  AND (p.\"ModerationStatusId\" NOT IN (2, 3) OR ((p.\"ModerationStatusId\" IS NULL))) ";
        
        if (filters.ProjectStages != null && 
            filters.ProjectStages.Count != 0 && 
            !filters.ProjectStages.Contains(FilterProjectStageTypeEnum.None))
        {
            //TODO: В будущем завести в БД енамки и передавать сразу в виде энамок, а не стингов
            var stageList = filters.ProjectStages?.Select(e => e.ToString()).ToList();
            parameters.Add("@stageList", stageList);
            query += " AND p0.\"StageSysName\" = ANY(@stageList) ";
        }

        if (filters.IsAnyVacancies)
            query += "  AND EXISTS ( " +
                     "    SELECT 1 " +
                     "    FROM \"Projects\".\"ProjectVacancies\" AS ppv " +
                     "    WHERE ppv.\"ProjectId\" = u.\"ProjectId\") ";
        
        query += "ORDER BY u2.\"DateCreated\" DESC, s.\"ObjectId\" DESC ";

        var result = await connection.QueryAsync<CatalogProjectOutput>(query, parameters);

        return result;
    }

    /// <summary>
    /// Метод создает новый проект пользователя.
    /// Если не указано, то выводится текст  в бд "не указано".
    /// </summary>
    /// <param name="createProjectInput">Входная модель.</param>
    /// <returns>Данные нового проекта.</returns>
    public async Task<UserProjectEntity> CreateProjectAsync(CreateProjectInput createProjectInput)
    {
        var transaction = await _pgContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted);

        try
        {
            var project = new UserProjectEntity
            {
                ProjectName = createProjectInput.ProjectName,
                ProjectDetails = createProjectInput.ProjectDetails,
                UserId = createProjectInput.UserId,
                ProjectCode = Guid.NewGuid(),
                DateCreated = DateTime.UtcNow,
                Conditions = createProjectInput.Conditions?? "Не указано",
                Demands = createProjectInput.Demands?? "Не указано",
                IsPublic = true
            };
            await _pgContext.UserProjects.AddAsync(project);

			// Дергаем сохранение тут, так как нам нужен Id добавленного проекта.
			// Фактического сохраненеия не произойдет, пока мы не завершили транзакцию.
			await _pgContext.SaveChangesAsync();

			var statusSysName = ProjectStatusNameEnum.Moderation.ToString();
			var statusName = ProjectStatus.GetProjectStatusNameBySysName(statusSysName);

			// Проставляем проекту статус "На модерации".
			await _pgContext.ProjectStatuses.AddAsync(new ProjectStatusEntity
			{
				ProjectId = project.ProjectId,
				ProjectStatusSysName = statusSysName,
				ProjectStatusName = statusName
			});

			// Записываем стадию проекта.
			await _pgContext.UserProjectsStages.AddAsync(new UserProjectStageEntity
			{
				ProjectId = project.ProjectId,
				StageId = (int)createProjectInput.ProjectStageEnum
			});

			// Отправляем проект на модерацию.
			await SendModerationProjectAsync(project.ProjectId);

			// Создаем команду проекта по дефолту.
			await _pgContext.ProjectsTeams.AddAsync(new ProjectTeamEntity
			{
				Created = DateTime.UtcNow,
				ProjectId = project.ProjectId
			});

			await _pgContext.SaveChangesAsync();
			await transaction.CommitAsync();

			return project;
		}

		catch
		{
			await transaction.RollbackAsync();
			throw;
		}
	}

	/// <summary>
	/// Метод получает названия полей для таблицы проектов пользователя.
	/// Все названия столбцов этой таблицы одинаковые у всех пользователей.
	/// </summary>
	/// <returns>Список названий полей таблицы.</returns>
	public async Task<IEnumerable<ProjectColumnNameEntity>> UserProjectsColumnsNamesAsync()
	{
		var result = await _pgContext.ProjectColumnsNames
			.OrderBy(o => o.Position)
			.ToListAsync();

		return result;
	}

	/// <summary>
	/// Метод проверяет, создан ли уже такой заказ под текущим пользователем с таким названием.
	/// </summary>
	/// <param name="projectName">Название проекта.</param>
	/// <param name="userId">Id пользователя.</param>
	/// <returns>Создал либо нет.</returns>
	public async Task<bool> CheckCreatedProjectByProjectNameAsync(string projectName, long userId)
	{
		var result = await _pgContext.UserProjects
			.AnyAsync(p => p.UserId == userId
						   && p.ProjectName.Equals(projectName));

		return result;
	}

	/// <summary>
	/// Метод получает список проектов пользователя.
	/// </summary>
	/// <param name="userId">Id пользователя.</param>
	/// <param name="isCreateVacancy">Признак создания вакансии.</param>
	/// <returns>Список проектов.</returns>
	public async Task<UserProjectResultOutput> UserProjectsAsync(long userId, bool isCreateVacancy)
	{
		var result = new UserProjectResultOutput
		{
			UserProjects = await _pgContext.ModerationProjects.AsNoTracking()
				.Include(ms => ms.ModerationStatus).AsNoTracking()
				.Include(up => up.UserProject).AsNoTracking()
				.Where(u => u.UserProject.UserId == userId)
				.Select(p => new UserProjectOutput
				{
					ProjectName = p.UserProject.ProjectName,
					ProjectDetails = p.UserProject.ProjectDetails,
					ProjectIcon = p.UserProject.ProjectIcon,
					ProjectStatusName = p.ModerationStatus.StatusName,
					ProjectStatusSysName = p.ModerationStatus.StatusSysName,
					ProjectCode = p.UserProject.ProjectCode,
					ProjectId = p.UserProject.ProjectId
				})
				.OrderByDescending(o => o.ProjectId)
				.ToListAsync()
		};

		if (isCreateVacancy)
		{
			var excludedStatuses = new[]
				{ ProjectStatusNameEnum.Archived.ToString(), ProjectModerationStatusEnum.ArchivedProject.ToString() };
			var removedProjectsIds = new List<long>();

			foreach (var prj in result.UserProjects)
			{
				if (excludedStatuses.Contains(prj.ProjectStatusSysName))
				{
					removedProjectsIds.Add(prj.ProjectId);
				}
			}

			if (removedProjectsIds.Any())
			{
				result.UserProjects = result.UserProjects.Where(p => !removedProjectsIds.Contains(p.ProjectId));
			}
		}

		return result;
	}

	/// <summary>
	/// Метод получает список проектов для каталога.
	/// </summary>
	/// <returns>Список проектов.</returns>
	public async Task<IEnumerable<CatalogProjectOutput>> CatalogProjectsAsync()
	{
		var archivedProjects = _pgContext.ArchivedProjects.Select(x => x.ProjectId).AsQueryable();

		var result = await (from cp in _pgContext.CatalogProjects.AsNoTracking()
							join p in _pgContext.UserProjects.AsNoTracking()
								on cp.ProjectId
								equals p.ProjectId
							join mp in _pgContext.ModerationProjects.AsNoTracking()
								on p.ProjectId
								equals mp.ProjectId
								into table
							from tbl in table.DefaultIfEmpty()
							join us in _pgContext.UserSubscriptions.AsNoTracking()
								on p.UserId
								equals us.UserId
							join s in _pgContext.Subscriptions.AsNoTracking()
								on us.SubscriptionId
								equals s.ObjectId
							join ups in _pgContext.UserProjectsStages.AsNoTracking()
								on p.ProjectId
								equals ups.ProjectId
							where !archivedProjects.Contains(p.ProjectId)
								  && p.IsPublic == true
								  && !new[]
									  {
							  (int)VacancyModerationStatusEnum.ModerationVacancy,
							  (int)VacancyModerationStatusEnum.RejectedVacancy
									  }
									  .Contains(tbl.ModerationStatusId)
							orderby cp.Project.DateCreated descending, s.ObjectId descending
							select new CatalogProjectOutput
							{
								ProjectId = p.ProjectId,
								ProjectName = p.ProjectName,
								DateCreated = p.DateCreated,
								ProjectIcon = p.ProjectIcon,
								ProjectDetails = p.ProjectDetails,
								UserId = p.UserId,
								ProjectStageSysName = _pgContext.ProjectStages.AsNoTracking()
									.FirstOrDefault(x => x.StageId == ups.StageId).StageSysName
							})
			.ToListAsync();

		return result;
	}

	/// <summary>
	/// Метод обновляет проект пользователя.
	/// </summary>
	/// <param name="updateProjectInput">Входная модель.</param>
	/// <returns>Данные нового проекта.</returns>
	public async Task<UpdateProjectOutput> UpdateProjectAsync(UpdateProjectInput updateProjectInput)
	{
		var transaction = await _pgContext.Database
			.BeginTransactionAsync(IsolationLevel.ReadCommitted);

		try
		{
			var userId = updateProjectInput.UserId;
			var projectId = updateProjectInput.ProjectId;

			var project = await _pgContext.UserProjects.FirstOrDefaultAsync(p => p.UserId == userId
				&& p.ProjectId == projectId);

			if (project is null)
			{
				throw new InvalidOperationException(
					$"Проект не найден для обновления. ProjectId был {projectId}. UserId был {userId}");
			}

			project.ProjectName = updateProjectInput.ProjectName;
			project.ProjectDetails = updateProjectInput.ProjectDetails;
			project.Conditions = updateProjectInput.Conditions;
			project.Demands = updateProjectInput.Demands;

			// Проставляем стадию проекта.
			var stage = await _pgContext.UserProjectsStages
				.Where(p => p.ProjectId == projectId)
				.FirstOrDefaultAsync();

			if (stage is null)
			{
				throw new InvalidOperationException($"У проекта не записана стадия. ProjectId был {projectId}.");
			}

            stage.StageId = (int)updateProjectInput.ProjectStageEnum;
            
            // Если проект уже был на модерации, то обновим статус.
            var isModerationExists = await IsModerationExistsProjectAsync(projectId.Value);
            
            if (!isModerationExists)
            {
                // Отправляем проект на модерацию.
                await SendModerationProjectAsync(projectId.Value);
            }
            
            else
            {
                await UpdateModerationProjectStatusAsync(projectId.Value, ProjectModerationStatusEnum.ModerationProject);
            }
            
            var stageEntity = await _pgContext.ProjectStages.AsNoTracking()
                .Where(ps => ps.StageId == stage.StageId)
                .Select(ps => new ProjectStageEntity
                {
                    StageName = ps.StageName,
                    StageSysName = ps.StageSysName,
                    StageId = ps.StageId
                })
                .FirstOrDefaultAsync();

            var result = new UpdateProjectOutput
            {
                DateCreated = project.DateCreated,
                ProjectName = project.ProjectName,
                ProjectDetails = project.ProjectDetails,
                ProjectIcon = project.ProjectIcon,
                ProjectId = projectId.Value,
                Conditions = project.Conditions,
                Demands = project.Demands,
                StageId = stage.StageId,
                StageName = stageEntity.StageName,
                StageSysName = stageEntity.StageSysName
            };

			await _pgContext.SaveChangesAsync();

			await transaction.CommitAsync();

			return result;
		}

		catch
		{
			await transaction.RollbackAsync();
			throw;
		}
	}

	/// <summary>
	/// Метод получает проект для изменения или просмотра.
	/// </summary>
	/// <param name="projectId">Id проекта.</param>
	/// <returns>Данные проекта.</returns>
	public async Task<(UserProjectEntity UserProject, ProjectStageEntity ProjectStage)> GetProjectAsync(long projectId)
	{
		(UserProjectEntity UserProject, ProjectStageEntity ProjectStage) result = (null, null);

		result.Item1 = await _pgContext.UserProjects.AsNoTracking()
			.FirstOrDefaultAsync(p => p.ProjectId == projectId);

		// Получаем стадию проекта пользователя.
		var projectStageId = await _pgContext.UserProjectsStages.AsNoTracking()
			.Where(p => p.ProjectId == projectId)
			.Select(p => p.StageId)
			.FirstOrDefaultAsync();

		// Берем полные данные о стадии проекта.
		result.Item2 = await _pgContext.ProjectStages.AsNoTracking()
			.Where(ps => ps.StageId == projectStageId)
			.Select(ps => new ProjectStageEntity
			{
				StageName = ps.StageName,
				StageSysName = ps.StageSysName,
				StageId = ps.StageId
			})
			.FirstOrDefaultAsync();

		return result;
	}

	/// <summary>
	/// Метод отправляет проект на модерацию.
	/// </summary>
	/// <param name="projectId">Id проекта.</param>
	private async Task SendModerationProjectAsync(long projectId)
	{
		// Добавляем проект в таблицу модерации проектов.
		await _pgContext.ModerationProjects.AddAsync(new ModerationProjectEntity
		{
			DateModeration = DateTime.UtcNow,
			ProjectId = projectId,
			ModerationStatusId = (int)ProjectModerationStatusEnum.ModerationProject
		});
	}

	/// <summary>
	/// Метод получает стадии проекта для выбора.
	/// </summary>
	/// <returns>Стадии проекта.</returns>
	public async Task<IEnumerable<ProjectStageEntity>> ProjectStagesAsync()
	{
		var result = await _pgContext.ProjectStages
			.OrderBy(o => o.Position)
			.ToListAsync();

		return result;
	}

	/// <summary>
	/// Метод получает список вакансий проекта. Список вакансий, которые принадлежат владельцу проекта.
	/// </summary>
	/// <param name="projectId">Id проекта, вакансии которого нужно получить.</param>
	/// <returns>Список вакансий.</returns>
	public async Task<IEnumerable<ProjectVacancyOutput>> ProjectVacanciesAsync(long projectId)
	{
		using var connection = await ConnectionProvider.GetConnectionAsync();

		var parameters = new DynamicParameters();
		parameters.Add("@projectId", projectId);

		var query = "SELECT uv.\"VacancyId\", " +
					"uv.\"VacancyName\", " +
					"uv.\"VacancyText\", " +
					"mv.\"ModerationStatusId\", " +
					"pv.\"ProjectVacancyId\", " +
					"pv.\"ProjectId\" " +
					"FROM \"Projects\".\"ProjectVacancies\" AS pv " +
					"INNER JOIN \"Moderation\".\"Vacancies\" AS mv " +
					"ON pv.\"VacancyId\" = mv.\"VacancyId\" " +
					"INNER JOIN \"Vacancies\".\"UserVacancies\" AS uv " +
					"ON pv.\"VacancyId\" = uv.\"VacancyId\" " +
					"WHERE pv.\"ProjectId\" = @projectId";

		var result = await connection.QueryAsync<ProjectVacancyOutput>(query, parameters);

		return result;
	}

	/// <summary>
	/// Метод прикрепляет вакансию к проекту.
	/// </summary>
	/// <param name="projectId">Id проекта.</param>
	/// <param name="vacancyId">Id вакансии.</param>
	/// <returns>Флаг успеха.</returns>
	public async Task<bool> AttachProjectVacancyAsync(long projectId, long vacancyId)
	{
		var isDublicateProjectVacancy = await _pgContext.ProjectVacancies
			.AnyAsync(p => p.ProjectId == projectId
						   && p.VacancyId == vacancyId);

		// Если такая вакансия уже прикреплена к проекту.
		if (isDublicateProjectVacancy)
		{
			return true;
		}

		await _pgContext.ProjectVacancies.AddAsync(new ProjectVacancyEntity
		{
			ProjectId = projectId,
			VacancyId = vacancyId
		});
		await _pgContext.SaveChangesAsync();

		return false;
	}

	/// <summary>
	/// Метод получает список вакансий проекта, которые можно прикрепить к проекту.
	/// </summary>
	/// <param name="projectId">Id проекта.</param>
	/// <param name="userId">Id пользователя.</param>
	/// <param name="isInviteProject">Признак приглашения в проект.</param>
	/// <returns>Список вакансий проекта.</returns>
	public async Task<IEnumerable<ProjectVacancyEntity>> ProjectVacanciesAvailableAttachAsync(long projectId,
		long userId, bool isInviteProject)
	{
		// Получаем Id вакансий, которые уже прикреплены к проекту. Их исключаем.
		var attachedVacanciesIds = _pgContext.ProjectVacancies
			.Where(p => p.ProjectId == projectId)
			.Select(p => p.VacancyId);

		// Получаем Id вакансий, которые еще на модерации либо отклонены модератором, так как их нельзя атачить.
		var moderationVacanciesIds = _pgContext.ModerationVacancies
			.Where(v => _excludedVacanciesStatuses.Contains(v.ModerationStatusId))
			.Select(v => v.VacancyId);

		// Получаем вакансии, которые в архиве, так как их нельзя атачить.
		var archivedVacancies = _pgContext.ArchivedVacancies.Select(v => v.VacancyId);

		// Получаем вакансии, которые можно прикрепить к проекту.
		var result = _pgContext.UserVacancies
			.Where(v => v.UserId == userId
						&& !moderationVacanciesIds.Contains(v.VacancyId))
			.Select(v => new ProjectVacancyEntity
			{
				ProjectId = projectId,
				VacancyId = v.VacancyId,
				UserVacancy = new UserVacancyEntity
				{
					VacancyName = v.VacancyName,
					VacancyText = v.VacancyText,
					Employment = v.Employment,
					WorkExperience = v.WorkExperience,
					DateCreated = v.DateCreated,
					Payment = v.Payment,
					UserId = userId,
					VacancyId = v.VacancyId,
				}
			});

		// Если не идет приглашение пользователя в проект, то отсекаем вакансии, которые уже прикреплены к проекту.
		if (!isInviteProject)
		{
			result = result.Where(v => !attachedVacanciesIds.Contains(v.VacancyId));
		}

		// Иначе наоборот, нам нужны только вакансии, которые уже прикреплены к проекту.
		else
		{
			result = result.Where(v => attachedVacanciesIds.Contains(v.VacancyId));
		}

		// Отсекаем вакансии с ненужными статусами.
		result = result.Where(v => !archivedVacancies.Contains(v.VacancyId));

		result = result.OrderByDescending(o => o.VacancyId);

		return await result.ToListAsync();
	}

	/// <summary>
	/// Метод записывает отклик на проект.
	/// Отклик может быть с указанием вакансии, на которую идет отклик (если указана VacancyId).
	/// Отклик может быть без указаниея вакансии, на которую идет отклик (если не указана VacancyId).
	/// </summary>
	/// <param name="projectId">Id проекта.</param>
	/// <param name="vacancyId">Id вакансии.</param>
	/// <param name="userId">Id пользователя.</param>
	/// <returns>Выходная модель с записанным откликом.</returns>
	public async Task<ProjectResponseEntity> WriteProjectResponseAsync(long projectId, long? vacancyId, long userId)
	{
		var isDublicate = await _pgContext.ProjectResponses
			.AnyAsync(p => p.UserId == userId
						   && p.ProjectId == projectId);

		// Если уже оставляли отклик на проект.
		if (isDublicate)
		{
			throw new DublicateProjectResponseException();
		}

		var response = new ProjectResponseEntity
		{
			ProjectId = projectId,
			UserId = userId,
			VacancyId = vacancyId,
			ProjectResponseStatuseId = (int)ProjectResponseStatusEnum.Wait,
			DateResponse = DateTime.UtcNow
		};

		await _pgContext.ProjectResponses.AddAsync(response);
		await _pgContext.SaveChangesAsync();

		return response;
	}

	/// <summary>
	/// Метод находит Id владельца проекта.
	/// </summary>
	/// <param name="projectId">Id проекта.</param>
	/// <returns>Id владельца проекта.</returns>
	public async Task<long> GetProjectOwnerIdAsync(long projectId)
	{
		var result = await _pgContext.UserProjects
			.Where(p => p.ProjectId == projectId)
			.Select(p => p.UserId)
			.FirstOrDefaultAsync();

		return result;
	}

	/// <summary>
	/// Метод получает данные команды проекта.
	/// </summary>
	/// <param name="projectId">Id проекта.</param>
	/// <returns>Данные команды проекта.</returns>
	public async Task<ProjectTeamEntity?> GetProjectTeamAsync(long projectId)
	{
		var result = await _pgContext.ProjectsTeams
			.Where(t => t.ProjectId == projectId)
			.Select(t => new ProjectTeamEntity
			{
				ProjectId = t.ProjectId,
				TeamId = t.TeamId,
			})
			.FirstOrDefaultAsync();

		return result;
	}

	/// <summary>
	/// Метод получает список участников команды проекта по Id команды.
	/// </summary>
	/// <param name="teamId">Id проекта.</param>
	/// <returns>Список участников команды проекта.</returns>
	public async Task<List<ProjectTeamMemberEntity>> GetProjectTeamMembersAsync(long teamId)
	{
		var result = await (from ptm in _pgContext.ProjectTeamMembers
							where ptm.TeamId == teamId
							select new ProjectTeamMemberEntity
							{
								UserId = ptm.UserId,
								Joined = ptm.Joined,
								TeamId = ptm.TeamId,
								MemberId = ptm.MemberId,
								UserVacancy = new UserVacancyEntity
								{
									VacancyId = ptm.VacancyId ?? 0
								},
								Role = ptm.Role
							})
			.ToListAsync();

		return result;
	}

	/// <summary>
	/// Метод получает названия полей для таблицы команды проекта пользователя.
	/// </summary>
	/// <returns>Список названий полей таблицы.</returns>
	public async Task<IEnumerable<ProjectTeamColumnNameEntity>> ProjectTeamColumnsNamesAsync()
	{
		var result = await _pgContext.ProjectTeamColumnNames
			.OrderBy(o => o.Position)
			.ToListAsync();

		return result;
	}

	/// <inheritdoc />
	public async Task<ProjectTeamMemberEntity> AddProjectTeamMemberAsync(long userId, long? vacancyId, long teamId,
		string? role)
	{
		var result = new ProjectTeamMemberEntity
		{
			Joined = DateTime.UtcNow,
			UserId = userId,
			VacancyId = vacancyId,
			TeamId = teamId,
			Role = role
		};

		await _pgContext.ProjectTeamMembers.AddAsync(result);

		// Добавляем вакансию отклика.
		if (vacancyId is not null)
		{
			var vacancy = new ProjectTeamVacancyEntity
			{
				VacancyId = (long)vacancyId,
				IsActive = true,
				TeamId = teamId
			};

			await _pgContext.ProjectsTeamsVacancies.AddAsync(vacancy);
		}

		await _pgContext.SaveChangesAsync();

		return result;
	}

	/// <summary>
	/// Метод находит Id команды проекта.
	/// </summary>
	/// <param name="projectId">Id проекта.</param>
	/// <returns>Id команды.</returns>
	public async Task<long> GetProjectTeamIdAsync(long projectId)
	{
		var result = await _pgContext.ProjectsTeams
			.Where(p => p.ProjectId == projectId)
			.Select(p => p.TeamId)
			.FirstOrDefaultAsync();

		return result;
	}

	/// <summary>
	/// Метод получает список проектов для дальнейшей фильтрации.
	/// </summary>
	/// <returns>Список проектов без выгрузки в память, так как этот список будем еще фильтровать.</returns>
	public async Task<List<CatalogProjectOutput>> GetFiltersProjectsAsync()
	{
		var result = await _pgContext.CatalogProjects
			.Include(p => p.Project)
			.Where(p => p.Project.IsPublic == true)
			.Select(p => new CatalogProjectOutput
			{
				ProjectId = p.Project.ProjectId,
				ProjectName = p.Project.ProjectName,
				DateCreated = p.Project.DateCreated,
				ProjectIcon = p.Project.ProjectIcon,
				ProjectDetails = p.Project.ProjectDetails,
				HasVacancies = _pgContext.ProjectVacancies.Any(pv => pv.ProjectId == p.ProjectId), // Если у проекта есть вакансии.
				ProjectStageSysName = (from ps in _pgContext.UserProjectsStages
									   join s in _pgContext.ProjectStages
										   on ps.StageId
										   equals s.StageId
									   select s.StageSysName)
					.FirstOrDefault(),
				UserId = p.Project.UserId,
				IsModeration = _pgContext.ModerationProjects.Any(pm => new[]
						{
							(int)VacancyModerationStatusEnum.ModerationVacancy,
							(int)VacancyModerationStatusEnum.RejectedVacancy
						}
						.Contains(pm.ModerationStatusId)),
				IsArchived = _pgContext.ArchivedProjects.Any(ap => ap.ProjectId == p.ProjectId)
			})
			.ToListAsync();

		return await Task.FromResult(result);
	}

	/// <summary>
	/// Метод проверяет владельца проекта.
	/// </summary>
	/// <param name="projectId">Id проекта.</param>
	/// <param name="userId">Id пользователя.</param>
	/// <returns>Признак является ли пользователь владельцем проекта.</returns>
	public async Task<bool> CheckProjectOwnerAsync(long projectId, long userId)
	{
		var result = await _pgContext.UserProjects
			.AnyAsync(p => p.ProjectId == projectId
						   && p.UserId == userId);

		return result;
	}

	/// <summary>
	/// Метод удаляет вакансию проекта.
	/// </summary>
	/// <param name="vacancyId">Id вакансии.</param>
	/// <param name="projectId">Id проекта.</param>
	/// <returns>Признак удаления вакансии проекта.</returns>
	public async Task<bool> DeleteProjectVacancyByIdAsync(long vacancyId, long projectId)
	{
		var vacancy = await _pgContext.ProjectVacancies
			.FirstOrDefaultAsync(v => v.VacancyId == vacancyId
									  && v.ProjectId == projectId);

		if (vacancy is null)
		{
			return false;
		}

		_pgContext.ProjectVacancies.Remove(vacancy);
		await _pgContext.SaveChangesAsync();

		return true;
	}

    /// <summary>
    /// Метод удаляет проект.
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    /// <param name="userId">Id пользователя.</param>
    /// <returns>Кортеж с результатами.</returns>
    public async Task<(bool Success, List<string> RemovedVacancies, string ProjectName)> RemoveProjectAsync(
        long projectId, long userId)
    {
        // TODO: Заменим на транзакцию от ADO.NET, когда перепишем EF Core тут на Dapper.
        await using var transaction = await _pgContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        
        var result = (Success: false, RemovedVacancies: new List<string>(), ProjectName: string.Empty);

        try
        {
            // Удаляем вакансии проекта.
            result = await RemoveProjectVacanciesAsync(projectId, result);

            // Удаляем чат диалога и все сообщения по Id текущего пользователя.
            await RemoveProjectChatAsync(userId, projectId);
            
            using var connection = await ConnectionProvider.GetConnectionAsync();
            
            // Удаляем участников команды проекта.
            await RemoveProjectTeamMembersAsync(projectId, connection);
            
            // Удаляем вакансии участников проекта (фактически мы не трогаем вакансии проекта,
            // а удаляем их из таблицы вакансий, на которые были приглашены участники).
            await RemoveProjectTeamMemberProjectVacanciesAsync(projectId, connection);

            // Удаляем команду проекта.
            await RemoveUserProjectTeamAsync(projectId);

            // Удаляем комментарии проекта.
            await RemoveProjectCommentsAsync(projectId);

            // Удаляем основную информацию диалога.
            await RemoveMainInfoDialogAsync(projectId);

            // Удаляем проект из каталога.
            await RemoveProjectCatalogAsync(projectId);

            // Удаляем проект из статусов.
            await RemoveProjectStatusAsync(projectId);

            // Удаляем проект из модерации.
            await RemoveProjectModerationAsync(projectId);

            // Удаляем проект из стадий.
            await RemoveProjectStagesAsync(projectId);

            // Удаляем настройки проекта.
            await RemoveMoveNotCompletedTasksSettingsAsync(projectId, connection);

            // Удаляем стратегию проекта.
            await RemoveProjectUserStrategyAsync(projectId, connection, userId);

            // Удаляем настройки длительности спринтов.
            await RemoveSprintDurationSettingsAsync(projectId, connection);

            // Удаляем проект из проектов компании.
            var companyId = await RemoveProjectCompanyAsync(projectId, connection);

            // Удаляем проект из общего пространства компании.
            await RemoveProjectWorkSpaceAsync(projectId, companyId, connection);

            // Удаляем проект пользователя.
            result = await RemoveUserProjectAsync(projectId, userId, result);
            
            // Удаляем все приглашения в проект.
            await RemoveProjectNotificationsAsync(projectId, connection);
            
            // Удаляем все роли участников проекта (удаляем участников).
            await RemoveProjectTeamMemberRolesAsync(projectId, connection);
            
            // Удаляем wiki проекта.
            await RemoveProjectWikiAsync(projectId, connection);
            
            // Удаляем настройки проекта.
            await RemoveProjectSettingsAsync(projectId, connection);

            await _pgContext.SaveChangesAsync();

            await transaction.CommitAsync();
        }

        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

		result.Success = true;

		return result;
	}

	/// <summary>
	/// Метод получает комментарии проекта.
	/// </summary>
	/// <param name="projectId">Id проекта.</param>
	/// <returns>Список комментариев проекта.</returns>
	private async Task<ICollection<ProjectCommentEntity>?> GetProjectCommentsAsync(long projectId)
	{
		var result = await _pgContext.ProjectComments
			.Where(c => c.ProjectId == projectId)
			.ToListAsync();

		return result;
	}

	/// <summary>
	/// Метод получает название проекта по его Id.
	/// </summary>
	/// <param name="projectId">Id проекта.</param>
	/// <returns>Название проекта.</returns>
	public async Task<string> GetProjectNameByProjectIdAsync(long projectId)
	{
		var result = await _pgContext.CatalogProjects
			.Include(p => p.Project)
			.Where(p => p.ProjectId == projectId)
			.Select(p => p.Project.ProjectName)
			.FirstOrDefaultAsync();

		return result;
	}

	/// <summary>
	/// Метод првоеряет, находится ли проект на модерации.
	/// </summary>
	/// <param name="projectId">Id проекта.</param>
	/// <returns>Признак модерации.</returns>
	public async Task<bool> CheckProjectModerationAsync(long projectId)
	{
		var result = await _pgContext.ModerationProjects
			.AnyAsync(p => p.ProjectId == projectId
						   && p.ModerationStatusId == (int)ProjectModerationStatusEnum.ModerationProject);

		return result;
	}

	/// <inheritdoc />
	public async Task<bool> CheckProjectArchivedAsync(long projectId)
	{
		var result = await _pgContext.ModerationProjects
			.AnyAsync(p => p.ProjectId == projectId
						   && p.ModerationStatusId == (int)ProjectModerationStatusEnum.ArchivedProject);

		return result;
	}

	/// <summary>
	/// Метод получает список вакансий доступных к отклику.
	/// Для владельца проекта будет возвращаться пустой список.
	/// </summary>
	/// <param name="userId">Id пользователя.</param>
	/// <param name="projectId">Id проекта.</param>
	/// <returns>Список вакансий доступных к отклику.</returns>
	public async Task<IEnumerable<ProjectVacancyEntity>> GetAvailableResponseProjectVacanciesAsync(long userId,
		long projectId)
	{
		// Получаем Id вакансий, которые еще на модерации либо отклонены модератором, так как их нельзя показывать.
		var moderationVacanciesIds = _pgContext.ModerationVacancies
			.Where(v => _excludedVacanciesStatuses.Contains(v.ModerationStatusId))
			.Select(v => v.VacancyId)
			.AsQueryable();

		// Получаем вакансии, на которые можно отправить отклики.
		var result = await _pgContext.ProjectVacancies
			.Where(v => v.ProjectId == projectId
						&& v.UserVacancy.UserId == userId
						&& !moderationVacanciesIds.Contains(v.VacancyId))
			.Select(v => new ProjectVacancyEntity
			{
				ProjectId = projectId,
				VacancyId = v.VacancyId,
				UserVacancy = new UserVacancyEntity
				{
					VacancyName = v.UserVacancy.VacancyName,
					VacancyText = v.UserVacancy.VacancyText,
					Employment = v.UserVacancy.Employment,
					WorkExperience = v.UserVacancy.WorkExperience,
					DateCreated = v.UserVacancy.DateCreated,
					Payment = v.UserVacancy.Payment,
					UserId = userId,
					VacancyId = v.VacancyId,
				}
			})
			.OrderBy(o => o.VacancyId)
			.ToListAsync();

		return result;
	}

	/// <summary>
	/// Метод получает название вакансии проекта по ее Id.
	/// </summary>
	/// <param name="vacancyId">Id вакансии.</param>
	/// <returns>Название вакансии.</returns>
	public async Task<string> GetProjectVacancyNameByIdAsync(long vacancyId)
	{
		var result = await _pgContext.UserVacancies
			.Where(v => v.VacancyId == vacancyId)
			.Select(v => v.VacancyName)
			.FirstOrDefaultAsync();

		return result;
	}

	/// <summary>
	/// Метод находит почту владельца проекта по Id проекта.
	/// </summary>
	/// <param name="projectId">Id проекта.</param>
	/// <returns>Почта владельца проекта.</returns>
	public async Task<string> GetProjectOwnerEmailByProjectIdAsync(long projectId)
	{
		var ownerId = await _pgContext.CatalogProjects
			.Where(v => v.ProjectId == projectId)
			.Select(v => v.Project.UserId)
			.FirstOrDefaultAsync();

		var result = await _pgContext.Users
			.Where(u => u.UserId == ownerId)
			.Select(u => u.Email)
			.FirstOrDefaultAsync();

		return result;
	}

	/// <summary>
	/// Метод проверяет добавляли ли уже пользоваетля в команду проекта.
	/// Если да, то не даем добавить повторно, чтобы не было дублей.
	/// </summary>
	/// <param name="teamId">Id команды проекта.</param>
	/// <param name="userId">Id пользователя.</param>
	/// <returns>Признак проверки.</returns>
	public async Task<bool> CheckProjectTeamMemberAsync(long teamId, long userId)
	{
		var result = await _pgContext.ProjectTeamMembers.AnyAsync(m => m.UserId == userId
																	   && m.TeamId == teamId);

		return result;
	}

	/// <summary>
	/// Метод получает список проектов пользователя из архива.
	/// </summary>
	/// <param name="userId">Id пользователя.</param>
	/// <returns>Список архивированных проектов.</returns>
	public async Task<IEnumerable<ArchivedProjectEntity>> GetUserProjectsArchiveAsync(long userId)
	{
		var result = await _pgContext.ArchivedProjects.AsNoTracking()
			.Include(a => a.UserProject)
			.Where(a => a.UserId == userId)
			.ToListAsync();

		return result;
	}

	/// <summary>
	/// Метод получает Id проекта по Id вакансии, которая принадлежит этому проекту.
	/// </summary>
	/// <param name="vacancyId">Id вакансии.</param>
	/// <returns>Id проекта.</returns>
	public async Task<long> GetProjectIdByVacancyIdAsync(long vacancyId)
	{
		var userId = await _pgContext.UserVacancies.AsNoTracking()
			.Where(v => v.VacancyId == vacancyId)
			.Select(v => v.UserId)
			.FirstOrDefaultAsync();

		var result = await _pgContext.UserProjects.AsNoTracking()
			.Where(p => p.UserId == userId)
			.Select(p => p.ProjectId)
			.FirstOrDefaultAsync();

		return result;
	}

	/// <summary>
	/// Метод получает название проекта по его Id.
	/// </summary>
	/// <param name="projectId">Id проекта.</param>
	/// <returns>Название проекта.</returns>
	public async Task<string> GetProjectNameByIdAsync(long projectId)
	{
		var result = await _pgContext.UserProjects.AsNoTracking()
			.Where(p => p.ProjectId == projectId)
			.Select(p => p.ProjectName)
			.FirstOrDefaultAsync();

		return result;
	}

	/// <summary>
	/// Метод удаляет участника проекта из команды.
	/// </summary>
	/// <param name="userId">Id пользователя</param>
	/// <param name="projectTeamId">Id команды проекта.</param>
	public async Task DeleteProjectTeamMemberAsync(long userId, long projectTeamId)
	{
		await DeleteTeamMemberAsync(userId, projectTeamId);
	}

	/// <summary>
	/// Метод покидания команды проекта.
	/// </summary>
	/// <param name="userId">Id пользователя</param>
	/// <param name="projectTeamId">Id команды проекта.</param>
	public async Task LeaveProjectTeamAsync(long userId, long projectTeamId)
	{
		await DeleteTeamMemberAsync(userId, projectTeamId);
	}

	/// <summary>
	/// Метод проверяет, есть ли пользователь в команде проекта.
	/// </summary>
	/// <param name="projectId">Id проекта.</param>
	/// <param name="userId">Id пользователя.</param>
	/// <returns>Признак проверки.</returns>
	public async Task<bool> CheckExistsProjectTeamMemberAsync(long projectId, long userId)
	{
		var result = await _pgContext.ProjectTeamMembers
			.AnyAsync(m => m.ProjectTeam.ProjectId == projectId && m.UserId == userId);

		return result;
	}

	/// <summary>
	/// Метод добавляет проект в архив.
	/// </summary>
	/// <param name="projectId">Id проекта.</param>
	/// <param name="userId">Id пользователя.</param>
	public async Task AddProjectArchiveAsync(long projectId, long userId)
	{
		await _pgContext.ArchivedProjects.AddAsync(new ArchivedProjectEntity
		{
			ProjectId = projectId,
			UserId = userId,
			DateArchived = DateTime.UtcNow
		});

		// Изменяем статус проекта на "В архиве".
		await UpdateModerationProjectStatusAsync(projectId, ProjectModerationStatusEnum.ArchivedProject);

		await _pgContext.SaveChangesAsync();
	}

	/// <summary>
	/// Метод проверяет, находится ли такой проект в архиве.
	/// </summary>
	/// <param name="projectId">Id проекта.</param>
	/// <returns>Признак проверки.</returns>
	public async Task<bool> CheckProjectArchiveAsync(long projectId)
	{
		var result = await _pgContext.ArchivedProjects.AnyAsync(p => p.ProjectId == projectId);

		return result;
	}

	/// <summary>
	/// Метод удаляет из архива проект.
	/// При удалении из архива проект отправляется на модерацию.
	/// </summary>
	/// <param name="projectId">Id проекта.</param>
	/// <param name="userId">Id пользователя.</param>
	public async Task<bool> DeleteProjectArchiveAsync(long projectId, long userId)
	{
		var transaction = await _pgContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

		try
		{
			var pa = await _pgContext.ArchivedProjects.FirstOrDefaultAsync(p => p.ProjectId == projectId
				&& p.UserId == userId);

			if (pa is null)
			{
				return false;
			}

			_pgContext.ArchivedProjects.Remove(pa);

			await _pgContext.SaveChangesAsync();

			await transaction.CommitAsync();

			return true;
		}

		catch
		{
			await transaction.RollbackAsync();
			throw;
		}
	}

	/// <summary>
	/// Метод получает кол-во проектов пользователя в каталоге.
	/// </summary>
	/// <param name="userId">Id пользователя.</param>
	/// <returns>Кол-во проектов в каталоге.</returns>
	public async Task<long> GetUserProjectsCatalogCountAsync(long userId)
	{
		var result = await _pgContext.CatalogProjects.AsNoTracking()
			.CountAsync(p => p.Project.UserId == userId);

		return result;
	}

	/// <summary>
	/// Метод получает Id команды проекта по Id проекта.
	/// </summary>
	/// <param name="projectId">Id проекта.</param>
	/// <returns>Id команды.</returns>
	public async Task<long> GetProjectTeamIdByProjectIdAsync(long projectId)
	{
		var result = await _pgContext.ProjectsTeams
			.Where(t => t.ProjectId == projectId)
			.Select(t => t.TeamId)
			.FirstOrDefaultAsync();

		return result;
	}

	/// <summary>
	/// Метод получает список Id пользователей, которые находся в команде проекта.
	/// </summary>
	/// <param name="teamId">Id команды.</param>
	/// <returns>Список Id пользователей.</returns>
	public async Task<IEnumerable<long>> GetProjectTeamMemberIdsAsync(long teamId)
	{
		var result = await _pgContext.ProjectTeamMembers
			.Where(t => t.TeamId == teamId)
			.Select(t => t.UserId)
			.Distinct()
			.ToListAsync();

		return result;
	}

    /// <inheritdoc />
    public async Task SetProjectManagementNameAsync(long projectId, string projectManagementName)
    {
        var prj = await _pgContext.UserProjects.FirstOrDefaultAsync(p => p.ProjectId == projectId);
        prj!.ProjectManagementName = projectManagementName;
        
        await _pgContext.SaveChangesAsync();
        
        await Task.CompletedTask;
    }

	/// <inheritdoc />
	public async Task SetProjectTeamMemberRoleAsync(long userId, string? role, long teamId)
	{
		using var connection = await ConnectionProvider.GetConnectionAsync();

		var parameters = new DynamicParameters();
		parameters.Add("@userId", userId);
		parameters.Add("@role", role);
		parameters.Add("@teamId", teamId);

        var query = "UPDATE \"Teams\".\"ProjectsTeamsMembers\" " +
                    "SET \"Role\" = @role " +
                    "WHERE \"UserId\" = @userId " +
                    "AND \"TeamId\" = @teamId";
        
        await connection.ExecuteAsync(query, parameters);
        
        await Task.CompletedTask;
    }

	/// <inheritdoc />
	public async Task RemoveUserProjectTeamAsync(long userId, long teamId)
	{
		using var connection = await ConnectionProvider.GetConnectionAsync();

		var parameters = new DynamicParameters();
		parameters.Add("@userId", userId);
		parameters.Add("@teamId", teamId);

        var query = "DELETE FROM \"Teams\".\"ProjectsTeamsMembers\" " +
                    "WHERE \"UserId\" = @userId " +
                    "AND \"TeamId\" = @teamId";
                    
        await connection.ExecuteAsync(query, parameters);
        
        await Task.CompletedTask;
    }

	/// <summary>
	/// Метод обновляет видимость проекта
	/// </summary>
	/// <param name="projectId">Id проекта.</param>
	/// <param name="isPublic">Видимость проекта.</param>
	/// <returns>Возращает признак видимости проекта.</returns>
	public async Task<UpdateProjectOutput> UpdateVisibleProjectAsync(long projectId, bool isPublic)
	{
		using var connection = await ConnectionProvider.GetConnectionAsync();

		var parameters = new DynamicParameters();
		parameters.Add("@projectId", projectId);
		parameters.Add("@isPublic", isPublic);

		var query = "UPDATE \"Projects\".\"UserProjects\"" +
					"SET \"IsPublic\" = @isPublic " +
					"WHERE \"ProjectId\" = @projectId ";


		await connection.ExecuteAsync(query, parameters);

		var result = new UpdateProjectOutput
		{
			ProjectId = projectId,
			IsPublic = isPublic,
			ProjectRemarks = new List<ProjectRemarkOutput>(),

		};

		return result;
	}

	#region Приватные методы.

	/// Метод проверяет, был ли уже такой проект на модерации. 
	/// </summary>
	/// <param name="projectId">Id проекта.</param>
	/// <returns>Признак модерации.</returns>
	private async Task<bool> IsModerationExistsProjectAsync(long projectId)
	{
		var result = await _pgContext.ModerationProjects
			.AnyAsync(p => p.ProjectId == projectId);

		return result;
	}

	/// <summary>
	/// Метод обновляет статус проекта на модерации.
	/// </summary>
	/// <param name="projectId">Id проекта.</param>
	/// <param name="status">Статус проекта.</param>
	private async Task UpdateModerationProjectStatusAsync(long projectId, ProjectModerationStatusEnum status)
	{
		var prj = await _pgContext.ModerationProjects.FirstOrDefaultAsync(p => p.ProjectId == projectId);

        if (prj is null)
        {
            throw new InvalidOperationException($"Не найден проект для модерации. ProjectId: {projectId}");
        }
        
        prj.ModerationStatusId = (int)status;
        
        await Task.CompletedTask;
    }

	/// <summary>
	/// Метод удаляет участника проекта.
	/// </summary>
	/// <param name="userId">Id пользователя</param>
	/// <param name="projectTeamId">Id команды проекта.</param>
	private async Task DeleteTeamMemberAsync(long userId, long projectTeamId)
	{
		var teamMember = await _pgContext.ProjectTeamMembers
			.FirstOrDefaultAsync(m => m.UserId == userId && m.TeamId == projectTeamId);

		// Удаляем участника команды.
		if (teamMember is not null)
		{
			_pgContext.ProjectTeamMembers.Remove(teamMember);
		}

		var projectTeamVacancy = await _pgContext.ProjectsTeamsVacancies
			.FirstOrDefaultAsync(v => v.VacancyId == teamMember.VacancyId);

		// Удаляем вакансию из ProjectsTeamsVacancies.
		if (projectTeamVacancy is not null)
		{
			_pgContext.ProjectsTeamsVacancies.Remove(projectTeamVacancy);
		}

        await _pgContext.SaveChangesAsync();
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Метод удаляет вакансии проекта.
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    /// <param name="result">Кортеж результата.</param>
    private async Task<(bool Success, List<string> RemovedVacancies, string ProjectName)> RemoveProjectVacanciesAsync(
        long projectId, (bool Success, List<string> RemovedVacancies, string ProjectName) result)
    {
        // Удаляем вакансии проекта.
        var projectVacancies = _pgContext.ProjectVacancies
            .Where(v => v.ProjectId == projectId)
            .AsQueryable();

        if (await projectVacancies.AnyAsync())
        {
            // Записываем названия вакансий, которые будут удалены.
            var removedVacanciesNames = projectVacancies.Select(v => v.UserVacancy.VacancyName);
            result.RemovedVacancies.AddRange(removedVacanciesNames);

            _pgContext.ProjectVacancies.RemoveRange(projectVacancies);
        }

        return await Task.FromResult(result);
    }

    /// <summary>
    /// Метод удаляет чат и все сообщения в проекте.
    /// </summary>
    /// <param name="userId">Id пользователя.</param>
    /// <param name="projectId">Id проекта.</param>
    private async Task RemoveProjectChatAsync(long userId, long projectId)
    {
        var projectDialogs = await _chatRepository.GetDialogsAsync(userId);

        // Если у проекта есть диалоги.
        if (projectDialogs is not null && projectDialogs.Count > 0)
        {
            // Перед удалением диалога, сначала смотрим сообщения диалога.
            foreach (var d in projectDialogs)
            {
                var projectDialogMessages = await _chatRepository.GetDialogMessagesAsync(d.DialogId, false);

                // Если есть сообщения, дропаем их.
                if (projectDialogMessages is not null && projectDialogMessages.Count > 0)
                {
                    _pgContext.DialogMessages.RemoveRange(projectDialogMessages);

                    // Дропаем участников диалога.
                    var dialogMembers = await _chatRepository.GetDialogMembersByDialogIdAsync(d.DialogId);

                    if (dialogMembers is not null && dialogMembers.Count > 0)
                    {
                        _pgContext.DialogMembers.RemoveRange(dialogMembers);
                    }
                }
            }
        }

        // Иначе будем искать диалоги по ProjectId.
        else
        {
            var prjDialogs = await _pgContext.Dialogs
                .Where(d => d.ProjectId == projectId)
                .ToListAsync();

            // Перед удалением диалога, сначала смотрим сообщения диалога.
            foreach (var d in prjDialogs)
            {
                // TODO: Если при удалении проекта надо будет также чистить сообщения нейросети, то тут доработать.
                var projectDialogMessages = await _chatRepository.GetDialogMessagesAsync(d.DialogId, false);

                // Если есть сообщения, дропаем их.
                if (projectDialogMessages is not null && projectDialogMessages.Count > 0)
                {
                    _pgContext.DialogMessages.RemoveRange(projectDialogMessages);
                }

                // Дропаем участников диалога.
                var dialogMembers = await _chatRepository.GetDialogMembersByDialogIdAsync(d.DialogId);

                if (dialogMembers is not null && dialogMembers.Count > 0)
                {
                    _pgContext.DialogMembers.RemoveRange(dialogMembers);

                    // Применяем сохранение здесь, так как каталог проектов имеет FK на диалог и иначе не даст удалить.
                    await _pgContext.SaveChangesAsync();
                }
            }
        }
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Метод удаляет команду проекта.
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    private async Task RemoveUserProjectTeamAsync(long projectId)
    {
        // Смотрим команду проекта.
        var projectTeam = await GetProjectTeamAsync(projectId);

        if (projectTeam is not null)
        {
            _pgContext.ProjectsTeams.Remove(projectTeam);
        }
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Метод удаляет комментарии проекта.
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    private async Task RemoveProjectCommentsAsync(long projectId)
    {
        // Получаем комментарии проекта.
        var projectComments = await GetProjectCommentsAsync(projectId);

        if (projectComments is not null && projectComments.Count > 0)
        {
            // Дропаем комментарии проекта из модерации.
            var moderationProjectComments = _pgContext.ProjectCommentsModeration
                .Where(c => c.ProjectComment.ProjectId == projectId)
                .AsQueryable();

            if (await moderationProjectComments.AnyAsync())
            {
                _pgContext.ProjectCommentsModeration.RemoveRange(moderationProjectComments);
            }

            _pgContext.ProjectComments.RemoveRange(projectComments);
        }
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Метод удаляет основную информацию о диалоге проекта.
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    private async Task RemoveMainInfoDialogAsync(long projectId)
    {
        var mainInfoDialog = await _pgContext.Dialogs.FirstOrDefaultAsync(d => d.ProjectId == projectId);

        if (mainInfoDialog is not null)
        {
            _pgContext.Dialogs.Remove(mainInfoDialog);
                
            // Применяем сохранение здесь, так как каталог проектов имеет FK на диалог и иначе не даст удалить.
            await _pgContext.SaveChangesAsync();
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Метод удаляет проект из каталога проектов.
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    private async Task RemoveProjectCatalogAsync(long projectId)
    {
        var catalogProject = await _pgContext.CatalogProjects
            .FirstOrDefaultAsync(p => p.ProjectId == projectId);

        if (catalogProject is not null)
        {
            _pgContext.CatalogProjects.Remove(catalogProject);   
        }
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Метод удаляет проект из статусов проекта.
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    private async Task RemoveProjectStatusAsync(long projectId)
    {
        var projectStatus = await _pgContext.ProjectStatuses
            .FirstOrDefaultAsync(p => p.ProjectId == projectId);
            
        if (projectStatus is not null)
        {
            _pgContext.ProjectStatuses.Remove(projectStatus);
        }
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Метод удаляет проект из модерации проектов.
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    private async Task RemoveProjectModerationAsync(long projectId)
    {
        var moderationProject = await _pgContext.ModerationProjects
            .FirstOrDefaultAsync(p => p.ProjectId == projectId);
            
        if (moderationProject is not null)
        {
            _pgContext.ModerationProjects.Remove(moderationProject);
        }
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Метод удаляет проект из стадий проектов.
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    private async Task RemoveProjectStagesAsync(long projectId)
    {
        var projectStage = await _pgContext.UserProjectsStages
            .FirstOrDefaultAsync(p => p.ProjectId == projectId);

        if (projectStage is not null)
        {
            _pgContext.UserProjectsStages.Remove(projectStage);
        }
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Метод удаляет настройки проекта.
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    /// <param name="connection">Подключение к БД.</param>
    private async Task RemoveMoveNotCompletedTasksSettingsAsync(long projectId, IDbConnection connection)
    {
        var deleteMoveNotCompletedTasksSettingsParameters = new DynamicParameters();
        deleteMoveNotCompletedTasksSettingsParameters.Add("@projectId", projectId);

        var deleteMoveNotCompletedTasksSettingsQuery = "DELETE FROM settings.move_not_completed_tasks_settings " +
                                                       "WHERE project_id = @projectId";

        await connection.ExecuteAsync(deleteMoveNotCompletedTasksSettingsQuery,
            deleteMoveNotCompletedTasksSettingsParameters);
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Метод удаляет стратегию проекта.
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    /// <param name="connection">Подключение к БД.</param>
    /// <param name="userId">Id пользователя.</param>
    private async Task RemoveProjectUserStrategyAsync(long projectId, IDbConnection connection, long userId)
    {
        var deleteProjectUserStrategyParameters = new DynamicParameters();
        deleteProjectUserStrategyParameters.Add("@projectId", projectId);
        deleteProjectUserStrategyParameters.Add("@userId", userId);

        var deleteProjectUserStrategyQuery = "DELETE FROM settings.project_user_strategy " +
                                             "WHERE project_id = @projectId " +
                                             "AND user_id = @userId";

        await connection.ExecuteAsync(deleteProjectUserStrategyQuery, deleteProjectUserStrategyParameters);
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Метод удаляет настройки длительности спринтов.
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    /// <param name="connection">Подключение к БД.</param>
    private async Task RemoveSprintDurationSettingsAsync(long projectId, IDbConnection connection)
    {
        var deleteSprintDurationSettingsParameters = new DynamicParameters();
        deleteSprintDurationSettingsParameters.Add("@projectId", projectId);

        var deleteSprintDurationSettingsQuery = "DELETE FROM settings.sprint_duration_settings " +
                                                "WHERE project_id = @projectId";
                                                    
        await connection.ExecuteAsync(deleteSprintDurationSettingsQuery, deleteSprintDurationSettingsParameters);
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Метод удаляет проект из проектов компании.
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    /// <param name="connection">Подключение к БД.</param>
    /// <returns>Id компании.</returns>
    private async Task<long> RemoveProjectCompanyAsync(long projectId, IDbConnection connection)
    {
        var companyIdParameters = new DynamicParameters();
        companyIdParameters.Add("@projectId", projectId);

        var companyIdQuery = "SELECT organization_id " +
                             "FROM project_management.organization_projects " +
                             "WHERE project_id = @projectId";
            
        var companyId = await connection.ExecuteScalarAsync<long>(companyIdQuery, companyIdParameters);
        
        var deleteCompanyProjectParameters = new DynamicParameters();
        deleteCompanyProjectParameters.Add("@projectId", projectId);
        deleteCompanyProjectParameters.Add("@companyId", companyId);

        var deleteCompanyProjectQuery = "DELETE FROM project_management.organization_projects " +
                                        "WHERE project_id = @projectId " +
                                        "AND organization_id = @companyId";

        await connection.ExecuteAsync(deleteCompanyProjectQuery, deleteCompanyProjectParameters);

        return companyId;
    }

    /// <summary>
    /// Метод удаляет проект из общего пространства компании.
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    /// <param name="companyId">Id компании.</param>
    /// <param name="connection">Подключение к БД.</param>
    private async Task RemoveProjectWorkSpaceAsync(long projectId, long companyId, IDbConnection connection)
    {
        var deleteProjectWorkSpaceParameters = new DynamicParameters();
        deleteProjectWorkSpaceParameters.Add("@projectId", projectId);
        deleteProjectWorkSpaceParameters.Add("@companyId", companyId);

        var deleteProjectWorkSpaceQuery = "DELETE FROM project_management.workspaces " +
                                          "WHERE project_id = @projectId " +
                                          "AND organization_id = @companyId";

        await connection.ExecuteAsync(deleteProjectWorkSpaceQuery, deleteProjectWorkSpaceParameters);
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Метод удаляет проект пользователя.
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    /// <param name="userId">Id пользователя.</param>
    /// <param name="result">Кортеж результата.</param>
    private async Task<(bool Success, List<string> RemovedVacancies, string ProjectName)> RemoveUserProjectAsync(
        long projectId, long userId, (bool Success, List<string> RemovedVacancies, string ProjectName) result)
    {
        var userProject = await _pgContext.UserProjects
            .FirstOrDefaultAsync(p => p.ProjectId == projectId
                                      && p.UserId == userId);

        if (userProject is not null)
        {
            // Записываем название проекта, который будет удален.
            result.ProjectName = userProject.ProjectName;

            _pgContext.UserProjects.Remove(userProject);
        }

        return await Task.FromResult(result);
    }

    /// <summary>
    /// Метод удаляет все приглашения в проект.
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    private async Task RemoveProjectNotificationsAsync(long projectId, IDbConnection connection)
    {
        var parameters = new DynamicParameters();
        parameters.Add("@projectId", projectId);

        var query = "DELETE FROM \"Notifications\".\"Notifications\"" +
                    "WHERE \"ProjectId\" = @projectId ";

        await connection.ExecuteAsync(query, parameters);
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Метод удаляет участников команды проекта.
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    /// <param name="connection">Подключение к БД.</param>
    private async Task RemoveProjectTeamMembersAsync(long projectId, IDbConnection connection)
    {
        var parameters = new DynamicParameters();
        parameters.Add("@projectId", projectId);

        var query = "DELETE " +
                    "FROM \"Teams\".\"ProjectsTeamsMembers\" " +
                    "WHERE \"UserId\" = ANY (SELECT ptm.\"UserId\" " +
                    "FROM \"Teams\".\"ProjectsTeams\" AS pt " +
                    "INNER JOIN \"Teams\".\"ProjectsTeamsMembers\" AS ptm " +
                    "ON pt.\"TeamId\" = ptm.\"TeamId\" " +
                    "WHERE pt.\"ProjectId\" = @projectId)";
                    
        await connection.ExecuteAsync(query, parameters);
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Метод удаляет вакансии участников проекта (фактически мы не трогаем вакансии проекта,
    // а удаляем их из таблицы вакансий, на которые были приглашены участники).
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    /// <param name="connection">Подключение к БД.</param>
    private async Task RemoveProjectTeamMemberProjectVacanciesAsync(long projectId, IDbConnection connection)
    {
        var parameters = new DynamicParameters();
        parameters.Add("@projectId", projectId);

        var query = "DELETE " +
                    "FROM \"Teams\".\"ProjectsTeamsVacancies\" AS ptv " +
                    "WHERE \"TeamId\" = (SELECT pt.\"TeamId\" " +
                    "FROM \"Teams\".\"ProjectsTeams\" AS pt " +
                    "INNER JOIN \"Teams\".\"ProjectsTeamsMembers\" AS ptm " +
                    "ON pt.\"TeamId\" = ptm.\"TeamId\" " +
                    "LEFT JOIN \"Vacancies\".\"UserVacancies\" AS uv " +
                    "ON (ptv.\"VacancyId\" = uv.\"VacancyId\" OR uv.\"VacancyId\" IS NULL) " +
                    "WHERE pt.\"ProjectId\" = @projectId " +
                    "LIMIT 1)";
        
        await connection.ExecuteAsync(query, parameters);
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Метод удаляет все роли участников проекта (удаляем участников).
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    /// <param name="connection">Подключение к БД.</param>
    private async Task RemoveProjectTeamMemberRolesAsync(long projectId, IDbConnection connection)
    {
        var parameters = new DynamicParameters();
        parameters.Add("@projectId", projectId);

        var query = "DELETE " +
                    "FROM roles.project_member_roles AS pmr " +
                    "WHERE project_member_id = ANY (SELECT ptm.\"UserId\" " +
                    "FROM \"Teams\".\"ProjectsTeams\" AS pt " +
                    "INNER JOIN \"Teams\".\"ProjectsTeamsMembers\" AS ptm " +
                    "ON pt.\"TeamId\" = ptm.\"TeamId\" " +
                    "WHERE pt.\"ProjectId\" = @projectId)";
                    
        await connection.ExecuteAsync(query, parameters);
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Метод удаляет дерево wiki проекта.
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    /// <param name="connection">Подключение к БД.</param>
    /// <exception cref="InvalidOperationException">Если нет дерева проекта.</exception>
    private async Task RemoveProjectWikiAsync(long projectId, IDbConnection connection)
    {
        var parameters = new DynamicParameters();
        parameters.Add("@projectId", projectId);

        var projectWikiTreeIdQuery = "SELECT wiki_tree_id " +
                                     "FROM project_management.wiki_tree " +
                                     "WHERE project_id = @projectId";
        
        var projectTreeId = await connection.ExecuteScalarAsync<long?>(projectWikiTreeIdQuery, parameters);

        if (!projectTreeId.HasValue)
        {
            throw new InvalidOperationException($"Ошибка получения дерева Wiki проекта {projectId}.");
        }
        
        var folderIdsParameters = new DynamicParameters();
        folderIdsParameters.Add("@projectTreeId", projectTreeId);

        var folderIdsQuery = "SELECT folder_id " +
                             "FROM project_management.wiki_tree_folders " +
                             "WHERE wiki_tree_id = @projectTreeId";

        var folderIds = (await connection.QueryAsync<long>(folderIdsQuery, folderIdsParameters))?.AsList();

        if (folderIds is not null && folderIds.Count > 0)
        {
            // Удаляем связи между папками.
            var removeFolderRelationsParameters = new DynamicParameters();
            removeFolderRelationsParameters.Add("@folderIds", folderIds);
            
            var removeFolderRelationsQuery = "DELETE " +
                                             "FROM project_management.wiki_tree_folder_relations " +
                                             "WHERE folder_id = ANY (@folderIds)";

            await connection.ExecuteAsync(removeFolderRelationsQuery, removeFolderRelationsParameters);
            
            // Удаляем папки.
            var foldersParameters = new DynamicParameters();
            foldersParameters.Add("@folderIds", folderIds);
            foldersParameters.Add("@projectTreeId", projectTreeId);

            var removeFoldersQuery = "DELETE " +
                                     "FROM project_management.wiki_tree_folders " +
                                     "WHERE folder_id = ANY (@folderIds) " +
                                     "AND wiki_tree_id = @projectTreeId";
                                     
            await connection.ExecuteAsync(removeFoldersQuery, foldersParameters);
            
            // Удаляем страницы.
            var removePagesQuery = "DELETE " +
                                   "FROM project_management.wiki_tree_pages " +
                                   "WHERE folder_id = ANY (@folderIds) " +
                                   "AND wiki_tree_id = @projectTreeId";
            
            await connection.ExecuteAsync(removePagesQuery, foldersParameters);
        }
        
        // Удаляем дерево.
        var removeTreeParameters = new DynamicParameters();
        removeTreeParameters.Add("@projectId", projectId);
        removeTreeParameters.Add("@projectTreeId", projectTreeId);

        var removeTreeQuery = "DELETE " +
                              "FROM project_management.wiki_tree " +
                              "WHERE wiki_tree_id = @projectTreeId " +
                              "AND project_id = @projectId";
        
        await connection.ExecuteAsync(removeTreeQuery, removeTreeParameters);
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Метод удаляет настройки проекта.
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    /// <param name="connection">Подключение к БД.</param>
    private async Task RemoveProjectSettingsAsync(long projectId, IDbConnection connection)
    {
        var projectParameters = new DynamicParameters();
        projectParameters.Add("@projectId", projectId);

        var removeProjectManagmentProjectSettingsQuery = "DELETE " +
                                                         "FROM \"Configs\".\"ProjectManagmentProjectSettings\" " +
                                                         "WHERE \"ProjectId\" = @projectId";
        
        await connection.ExecuteAsync(removeProjectManagmentProjectSettingsQuery, projectParameters);

        var removeMoveNotCompletedTasksSettingsQuery = "DELETE " +
                                                       "FROM settings.move_not_completed_tasks_settings " +
                                                       "WHERE project_id = @projectId";
                                                       
        await connection.ExecuteAsync(removeMoveNotCompletedTasksSettingsQuery, projectParameters);

        var removeProjectUserStrategyQuery = "DELETE " +
                                             "FROM settings.project_user_strategy " +
                                             "WHERE project_id = @projectId";
                                                       
        await connection.ExecuteAsync(removeProjectUserStrategyQuery, projectParameters);
        
        var removeSprintDurationSettingsQuery = "DELETE " +
                                             "FROM settings.project_user_strategy " +
                                             "WHERE project_id = @projectId";
                                                       
        await connection.ExecuteAsync(removeSprintDurationSettingsQuery, projectParameters);
        
        await Task.CompletedTask;
    }

	#endregion
}
