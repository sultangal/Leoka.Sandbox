﻿using Dapper;
using LeokaEstetica.Platform.Base.Abstractions.Connection;
using LeokaEstetica.Platform.Base.Abstractions.Repositories.Base;
using LeokaEstetica.Platform.Database.Abstractions.ProjectManagment;
using LeokaEstetica.Platform.Models.Dto.Input.ProjectManagement;
using LeokaEstetica.Platform.Models.Dto.Output.ProjectManagment;
using LeokaEstetica.Platform.Models.Dto.Output.Template;
using LeokaEstetica.Platform.Models.Dto.ProjectManagement.Output;
using LeokaEstetica.Platform.Models.Entities.Document;
using LeokaEstetica.Platform.Models.Entities.ProjectManagment;
using LeokaEstetica.Platform.Models.Entities.Template;
using LeokaEstetica.Platform.Models.Enums;
using SqlKata;
using SqlKata.Compilers;
using Enum = LeokaEstetica.Platform.Models.Enums.Enum;

namespace LeokaEstetica.Platform.Database.Repositories.ProjectManagment;

/// <summary>
/// Класс реализует методы репозитория управления проектами.
/// </summary>
internal sealed class ProjectManagmentRepository : BaseRepository, IProjectManagmentRepository
{
    /// <summary>
    /// Конструктор.
    /// </summary>
    /// <param name="connectionProvider">Провайдер БД.</param>
    public ProjectManagmentRepository(IConnectionProvider connectionProvider)
        : base(connectionProvider)
    {
    }

    #region Публичные методы.

    /// <summary>
    /// Метод получает список стратегий представления рабочего пространства.
    /// </summary>
    /// <returns>Список стратегий.</returns>
    public async Task<IEnumerable<ViewStrategyEntity>> GetViewStrategiesAsync()
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        var query = @"SELECT strategy_id, view_strategy_name, view_strategy_sys_name, position 
                      FROM project_management.view_strategies 
                      ORDER BY position";

        var result = await connection.QueryAsync<ViewStrategyEntity>(query);

        return result;
    }

    /// <summary>
    /// Метод получает элементы верхнего меню (хидера).
    /// </summary>
    /// <returns>Список элементов.</returns>
    public async Task<IEnumerable<ProjectManagmentHeaderEntity>> GetHeaderItemsAsync()
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        var query = @"SELECT header_id,
                             item_name,
                             item_url,
                             position,
                             header_type,
                             items,
                             has_items,
                             is_disabled,
                             control_type,
                             destination
                             FROM project_management.header
                             ORDER BY position";

        var result = await connection.QueryAsync<ProjectManagmentHeaderEntity>(query);

        return result;
    }

    /// <summary>
    /// Метод получает список шаблонов задач, которые пользователь может выбрать перед переходом в рабочее пространство.
    /// </summary>
    /// <param name="templateId">Id шаблона.</param>
    /// <returns>Список шаблонов задач.</returns>
    public async Task<IEnumerable<ProjectManagmentTaskTemplateEntityResult>> GetProjectManagmentTemplatesAsync(
        long? templateId)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        
        var query = @"SELECT tsit.status_id, 
                      tsit.template_id, 
                      tsit.is_custom_status, 
                      tst.status_name, 
                      tst.status_sys_name,
                      tst.task_status_id 
                      FROM templates.project_management_task_status_intermediate_templates AS tsit 
                      INNER JOIN templates.project_management_task_status_templates AS tst 
                        ON tsit.status_id = tst.status_id 
                      INNER JOIN templates.project_management_task_templates AS ptt 
                        ON tsit.template_id = ptt.template_id";

        IEnumerable<ProjectManagmentTaskTemplateEntityResult> result;

        if (templateId.HasValue)
        {
            var parameters = new DynamicParameters();
            parameters.Add("@templateId", templateId.Value);
            query += " WHERE tsit.template_id = @templateId";
            
            result = await connection.QueryAsync<ProjectManagmentTaskTemplateEntityResult>(query, parameters);
        }

        else
        {
            result = await connection.QueryAsync<ProjectManagmentTaskTemplateEntityResult>(query);
        }

        return result;
    }

    /// <summary>
    /// Метод получает список Id статусов, которым будем проставлять Id шаблона, к которому относятся эти статусы.
    /// </summary>
    /// <param name="templateStatusIds">Список Id статусов.</param>
    /// <returns>Словарь с Id шаблонов и статусов.</returns>
    public async Task<IEnumerable<GetTemplateStatusIdByStatusIdOutput>> GetTemplateStatusIdsByStatusIdsAsync(
        IEnumerable<long> templateStatusIds)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        var compiler = new PostgresCompiler();
        var query = new Query("templates.project_management_task_status_intermediate_templates")
            .WhereIn("status_id", templateStatusIds)
            .Select("status_id", "template_id")
            .OrderBy("template_id");
        var sql = compiler.Compile(query).ToString();

        var result = await connection.QueryAsync<GetTemplateStatusIdByStatusIdOutput>(sql);

        return result;
    }

    /// <summary>
    /// Метод получает задачи проекта для рабочего пространства.
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    /// <returns>Задачи проекта.</returns>
    public async Task<IEnumerable<ProjectTaskEntity>> GetProjectTasksAsync(long projectId)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        var compiler = new PostgresCompiler();
        var query = new Query("project_management.project_tasks")
            .Where("project_id", projectId)
            .Select("task_id", "task_status_id", "author_id", "watcher_ids", "name", "details", "created", "updated",
                "project_id", "project_task_id", "resolution_id", "tag_ids", "task_type_id", "executor_id",
                "priority_id")
            .OrderBy("project_id");
        var sql = compiler.Compile(query).ToString();

        var result = await connection.QueryAsync<ProjectTaskEntity>(sql);

        return result;
    }

    /// <summary>
    /// Метод получает названия тегов (меток) пользователя задач по их Id.
    /// </summary>
    /// <param name="tagIds">Id тегов (меток) задач.</param>
    /// <returns>Словарь с тегами (метками) задач.</returns>
    public async Task<IDictionary<int, ProjectTagOutput>> GetTagNamesByTagIdsAsync(IEnumerable<int> tagIds)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        var compiler = new PostgresCompiler();
        var query = new Query("project_management.project_tags")
            .WhereIn("tag_id", tagIds)
            .Select("tag_id", "tag_name", "position") // TODO: Добавить поле тип объекта из енамки.
            .OrderBy("position");
        var sql = compiler.Compile(query).ToString();
        
        // TODO: Добавить поле тип объекта из енамки.
        var result = (await connection.QueryAsync<ProjectTagOutput>(sql))
            .ToDictionary(k => k.TagId, v => new ProjectTagOutput
            {
                TagName = v.TagName,
                TagSysName = v.TagSysName,
                TagDescription = v.TagDescription,
                TagId = v.TagId,
                ProjectId = v.ProjectId
            });

        return result;
    }

    /// <summary>
    /// Метод получает названия типов задач по их Id.
    /// </summary>
    /// <param name="typeIds">Id типов задач.</param>
    /// <returns>Словарь с типами задач.</returns>
    public async Task<IDictionary<int, TaskTypeOutput>> GetTypeNamesByTypeIdsAsync(IEnumerable<int> typeIds)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        var compiler = new PostgresCompiler();
        var query = new Query("project_management.task_types")
            .WhereIn("type_id", typeIds)
            .Select("type_id", "type_name", "position")
            .OrderBy("position");
        var sql = compiler.Compile(query).ToString();
        
        var result = (await connection.QueryAsync<TaskTypeOutput>(sql))
            .ToDictionary(k => k.TypeId, v => new TaskTypeOutput
            {
                TypeName = v.TypeName,
                TypeSysName = v.TypeSysName
            });

        return result;
    }

    /// <summary>
    /// Метод получает названия резолюций задач по их Id.
    /// </summary>
    /// <param name="resolutionIds">Id резолюций задач.</param>
    /// <returns>Словарь с резолюциями задач.</returns>
    public async Task<IDictionary<int, TaskResolutionOutput>> GetResolutionNamesByResolutionIdsAsync(
        IEnumerable<int> resolutionIds)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        var compiler = new PostgresCompiler();
        var query = new Query("project_management.task_resolutions")
            .WhereIn("resolution_id", resolutionIds)
            .Select("resolution_id", "resolution_name", "position")
            .OrderBy("position");
        var sql = compiler.Compile(query).ToString();

        var result = (await connection.QueryAsync<TaskResolutionOutput>(sql))
            .ToDictionary(k => k.ResolutionId, v => new TaskResolutionOutput
            {
                ResolutionName = v.ResolutionName,
                ResolutionSysName = v.ResolutionSysName,
                Position = v.Position
            });

        return result;
    }

    /// <summary>
    /// Метод получает детали задачи.
    /// </summary>
    /// <param name="projectTaskId">Id задачи в рамках проекта.</param>
    /// <param name="projectId">Id проекта.</param>
    /// <returns>Данные задачи.</returns>
    public async Task<ProjectTaskEntity> GetTaskDetailsByTaskIdAsync(long projectTaskId, long projectId)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        var parameters = new DynamicParameters();
        parameters.Add("@project_id", projectId);
        parameters.Add("@project_task_id", projectTaskId);

        var query = @"SELECT task_id, task_status_id, author_id, watcher_ids, name, details, created, updated,
                      project_id, project_task_id, resolution_id, tag_ids, task_type_id, executor_id, priority_id 
                      FROM project_management.project_tasks 
                      WHERE project_id = @project_id AND project_task_id = @project_task_id";

        var result = await connection.QueryFirstOrDefaultAsync<ProjectTaskEntity>(query, parameters);

        return result;
    }

    /// <summary>
    /// Метод получает названия приоритетов задач по их Id.
    /// </summary>
    /// <param name="priorityIds">Id приоритетов задач.</param>
    /// <returns>Словарь с приоритетами задач.</returns>
    public async Task<IDictionary<int, TaskPriorityOutput>> GetPriorityNamesByPriorityIdsAsync(
        IEnumerable<int> priorityIds)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        var compiler = new PostgresCompiler();
        var query = new Query("project_management.task_priorities")
            .WhereIn("priority_id", priorityIds)
            .Select("priority_id", "priority_name")
            .OrderBy("position");
        var sql = compiler.Compile(query).ToString();

        var result = (await connection.QueryAsync<TaskPriorityOutput>(sql))
            .ToDictionary(k => k.PriorityId, v => new TaskPriorityOutput
            {
                PriorityName = v.PriorityName,
                PrioritySysName = v.PrioritySysName
            });

        return result;
    }

    /// <summary>
    /// Метод получает последний Id задачи в рамках проекта.
    /// </summary>
    /// <param name="projectId">Id проекта.</param>
    /// <returns>Последний Id задачи в рамках проекта.</returns>
    public async Task<long> GetLastProjectTaskIdAsync(long projectId)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        var compiler = new PostgresCompiler();
        var query = new Query("project_management.project_tasks")
            .Where("project_id", projectId)
            .Select("task_id")
            .AsMax("task_id");
        var sql = compiler.Compile(query).ToString();

        var result = await connection.ExecuteScalarAsync<long>(sql);

        return result;
    }

    /// <summary>
    /// Метод получает список приоритетов задачи.
    /// </summary>
    /// <returns>Список приоритетов задачи.</returns>
    public async Task<IEnumerable<TaskPriorityEntity>> GetTaskPrioritiesAsync()
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        var compiler = new PostgresCompiler();
        var query = new Query("project_management.task_priorities")
            .Select("priority_id", "priority_name", "priority_sys_name")
            .OrderBy("position");
        var sql = compiler.Compile(query).ToString();

        var result = await connection.QueryAsync<TaskPriorityEntity>(sql);

        return result;
    }

    /// <summary>
    /// Метод получает список типов задач.
    /// </summary>
    /// <returns>Список типов задач.</returns>
    public async Task<IEnumerable<TaskTypeEntity>> GetTaskTypesAsync()
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        var compiler = new PostgresCompiler();
        var query = new Query("project_management.task_types")
            .Select("type_id", "type_name", "type_sys_name")
            .OrderBy("position");
        var sql = compiler.Compile(query).ToString();

        var result = await connection.QueryAsync<TaskTypeEntity>(sql);

        return result;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<ProjectTagEntity>> GetProjectTagsAsync()
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();

        var query = @"SELECT tag_id, tag_name, tag_sys_name, tag_description, project_id, object_tag_type 
                      FROM project_management.project_tags 
                      ORDER BY position";

        var result = await connection.QueryAsync<ProjectTagEntity>(query);

        return result;
    }

    /// <summary>
    /// Метод создает задачу проекта.
    /// </summary>
    /// <param name="task">Задача для создания.</param>
    public async Task CreateProjectTaskAsync(ProjectTaskEntity task)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();

        var parameters = new DynamicParameters();
        parameters.Add("@task_status_id", task.TaskStatusId);
        parameters.Add("@author_id", task.AuthorId);
        parameters.Add("@watcher_ids", task.WatcherIds);
        parameters.Add("@name", task.Name);
        parameters.Add("@details", task.Details);
        parameters.Add("@created", DateTime.UtcNow);
        parameters.Add("@project_id", task.ProjectId);
        parameters.Add("@project_task_id", task.ProjectTaskId);
        parameters.Add("@resolution_id", task.ResolutionId);
        parameters.Add("@tag_ids", task.TagIds);
        parameters.Add("@task_type_id", task.TaskTypeId);
        parameters.Add("@executor_id", task.ExecutorId);
        parameters.Add("@priority_id", task.PriorityId);

        var sql = @"INSERT INTO project_management.project_tasks (task_status_id, author_id, watcher_ids, name, details, created, project_id, project_task_id, resolution_id, tag_ids, task_type_id, executor_id, priority_id) 
VALUES (@task_status_id, @author_id, @watcher_ids, @name, @details, @created, @project_id, @project_task_id, @resolution_id, @tag_ids, @task_type_id, @executor_id, @priority_id)";

        await connection.ExecuteAsync(sql, parameters);
    }

    /// <inheritdoc />
    public async Task<int> GetLastPositionUserTaskTagAsync(long userId)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        var compiler = new PostgresCompiler();
        var query = new Query("project_management.project_tags")
            .Where("user_id", userId)
            .Select("position")
            .AsMax("position");
        var sql = compiler.Compile(query).ToString();
        
        var lastPosition = await connection.ExecuteScalarAsync<int>(sql);
        
        if (lastPosition <= 0)
        {
            // Если позиций пока нету, то начнем с первой.
            return 1;
        }

        return lastPosition;
    }

    /// <inheritdoc />
    public async Task CreateProjectTaskTagAsync(ProjectTagEntity tag)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        
        // TODO: Добавить новые поля при создании.
        var parameters = new DynamicParameters();
        parameters.Add("@tag_name", tag.TagName);
        parameters.Add("@tag_sys_name", tag.TagSysName);
        parameters.Add("@tag_description", tag.TagDescription);
        parameters.Add("@position", tag.Position);

        var query = @"INSERT INTO project_management.project_tags (tag_name, tag_sys_name, position, tag_description) 
                      VALUES (@tag_name, @tag_sys_name, @tag_description, @position)";

        await connection.ExecuteAsync(query, parameters);
    }

    /// <inheritdoc />
    public async Task<bool> IfProjectHavingProjectTaskIdAsync(long projectId, long projectTaskId)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        var compiler = new PostgresCompiler();
        var query = new Query("project_management.project_tasks")
            .Where("project_id", projectId)
            .Where("project_task_id", projectTaskId)
            .AsCount();
        var sql = compiler.Compile(query).ToString();
            
        var count = await connection.ExecuteScalarAsync<int>(sql);

        return count > 0;
    }

    /// <inheritdoc />
    public async Task<long> GetProjectTaskStatusIdByProjectIdProjectTaskIdAsync(long projectId, long projectTaskId)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        var compiler = new PostgresCompiler();
        var query = new Query("project_management.project_tasks")
            .Where("project_id", projectId)
            .Where("project_task_id", projectTaskId)
            .Select("task_status_id");
        var sql = compiler.Compile(query).ToString();
        
        var result = await connection.QueryFirstOrDefaultAsync<long>(sql);

        return result;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<long>> GetProjectManagementTransitionIntermediateTemplatesAsync(
        long currentTaskStatusId)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        var compiler = new PostgresCompiler();
        var query = new Query("templates.project_management_transition_intermediate_templates")
            .Where("from_status_id", currentTaskStatusId)
            .Select("to_status_id");
        var sql = compiler.Compile(query).ToString();
        
        var result = await connection.QueryAsync<long>(sql);

        return result;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<TaskStatusIntermediateTemplateCompositeOutput>>
        GetTaskStatusIntermediateTemplatesAsync(IEnumerable<long> statusIds)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        
        var parameters = new DynamicParameters();
        parameters.Add("@statusIds", statusIds);

        var sql = @"SELECT tsit.status_id, tsit.template_id, tsit.is_custom_status, tst.task_status_id 
                    FROM templates.project_management_task_status_intermediate_templates AS tsit 
                    INNER JOIN templates.project_management_task_status_templates AS tst 
                        ON tsit.status_id = tst.status_id 
                    WHERE tsit.status_id = ANY(@statusIds)";
        
        var result = await connection.QueryAsync<TaskStatusIntermediateTemplateCompositeOutput>(sql, parameters);

        return result;
    }

    /// <inheritdoc />
    public async Task<IDictionary<long, GetTaskStatusTemplateOutput>> GetTaskStatusTemplatesAsync()
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        var compiler = new PostgresCompiler();
        var query = new Query("templates.project_management_task_status_templates")
            .Select("status_id", "status_name", "status_sys_name", "position", "task_status_id", "status_description");
        var sql = compiler.Compile(query).ToString();

        var result = (await connection.QueryAsync<GetTaskStatusTemplateOutput>(sql))
            .ToDictionary(k => k.StatusId, v => new GetTaskStatusTemplateOutput
            {
                Position = v.Position,
                StatusName = v.StatusName,
                StatusSysName = v.StatusSysName,
                TaskStatusId = v.TaskStatusId
            });

        return result;
    }

    /// <inheritdoc />
    public async Task<IDictionary<long, UserStatuseTemplateOutput>> GetUserTaskStatusTemplatesAsync()
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        var compiler = new PostgresCompiler();
        var query = new Query("templates.project_management_user_statuse_templates")
            .Select("status_id", "status_name", "status_sys_name", "position", "user_id", "status_description");
        var sql = compiler.Compile(query).ToString();

        var result = (await connection.QueryAsync<UserStatuseTemplateOutput>(sql))
            .ToDictionary(k => k.StatusId, v => new UserStatuseTemplateOutput
            {
                Position = v.Position,
                StatusName = v.StatusName,
                StatusSysName = v.StatusSysName,
                StatusDescription = v.StatusDescription,
                UserId = v.UserId
            });

        return result;
    }

    /// <inheritdoc />
    public async Task ChangeTaskStatusAsync(long projectId, long changeStatusId, long taskId)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        var compiler = new PostgresCompiler();
        var queryGetTask = new Query("project_management.project_tasks")
            .Where("project_id", projectId)
            .Where("task_id", taskId)
            .AsCount();
        var sqlGetTask = compiler.Compile(queryGetTask).ToString();
        var task = await connection.ExecuteScalarAsync<long?>(sqlGetTask);

        if (task is null)
        {
            throw new InvalidOperationException($"Задача с Id: {taskId} не найдена у проекта: {projectId}.");
        }

        var queryUpdateTask = new Query("project_management.project_tasks")
            .Where("project_id", projectId)
            .Where("task_id", taskId)
            .AsUpdate(new { task_status_id = changeStatusId });
        var sqlUpdateTask = compiler.Compile(queryUpdateTask).ToString();

        await connection.ExecuteAsync(sqlUpdateTask);
    }

    /// <inheritdoc />
    public async Task<ProjectManagmentTaskStatusTemplateEntity> GetTaskStatusByTaskStatusIdAsync(long taskStatusId,
        int templateId)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();

        var parameters = new DynamicParameters();
        parameters.Add("@task_status_id", taskStatusId);
        parameters.Add("@template_id", templateId);

        var sql = @"SELECT tst.status_id, tst.status_name, tst.status_sys_name, tst.task_status_id 
                    FROM templates.project_management_task_status_intermediate_templates AS tsit 
                    INNER JOIN templates.project_management_task_status_templates AS tst 
                        ON tsit.status_id = tst.status_id 
                    WHERE tst.task_status_id = @task_status_id 
                    AND tsit.template_id = @template_id";

        var result = await connection.QueryFirstOrDefaultAsync<ProjectManagmentTaskStatusTemplateEntity>(
            sql, parameters);

        return result;
    }

    /// <inheritdoc />
    public async Task UpdateTaskDetailsAsync(long projectId, long taskId, string changedTaskDetails)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();

        var parameters = new DynamicParameters();
        parameters.Add("@project_id", projectId);
        parameters.Add("@task_id", taskId);
        parameters.Add("@details", changedTaskDetails);

        var sql = @"UPDATE project_management.project_tasks 
                    SET details = @details 
                    WHERE project_id = @project_id 
                      AND project_task_id = @task_id";
        
        await connection.ExecuteAsync(sql, parameters);
    }

    /// <inheritdoc />
    public async Task UpdateTaskNameAsync(long projectId, long taskId, string changedTaskName)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();

        var parameters = new DynamicParameters();
        parameters.Add("@project_id", projectId);
        parameters.Add("@task_id", taskId);
        parameters.Add("@name", changedTaskName);

        var sql = @"UPDATE project_management.project_tasks 
                    SET name = @name 
                    WHERE project_id = @project_id 
                      AND project_task_id = @task_id";
        
        await connection.ExecuteAsync(sql, parameters);
    }

    /// <inheritdoc />
    public async Task AttachTaskTagAsync(int tagId, long projectTaskId, long projectId)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();

        var parameters = new DynamicParameters();
        parameters.Add("@tag_id", tagId);
        parameters.Add("@project_task_id", projectTaskId);
        parameters.Add("@project_id", projectId);

        var sql = @"UPDATE project_management.project_tasks 
                    SET tag_ids = ARRAY_APPEND(tag_ids, @tag_id) 
                    WHERE project_task_id = @project_task_id 
                      AND project_id = @project_id";
                      
        await connection.ExecuteAsync(sql, parameters);
    }

    /// <inheritdoc />
    public async Task DetachTaskTagAsync(int tagId, long projectTaskId, long projectId)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();

        var parameters = new DynamicParameters();
        parameters.Add("@tag_id", tagId);
        parameters.Add("@project_task_id", projectTaskId);
        parameters.Add("@project_id", projectId);

        var sql = @"UPDATE project_management.project_tasks 
                    SET tag_ids = ARRAY_REMOVE(tag_ids, @tag_id) 
                    WHERE project_task_id = @project_task_id 
                      AND project_id = @project_id";
                      
        await connection.ExecuteAsync(sql, parameters);
    }

    /// <inheritdoc />
    public async Task AttachTaskWatcherAsync(long watcherId, long projectTaskId, long projectId)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();

        var parameters = new DynamicParameters();
        parameters.Add("@watcher_ids", watcherId);
        parameters.Add("@project_task_id", projectTaskId);
        parameters.Add("@project_id", projectId);

        var sql = @"UPDATE project_management.project_tasks 
                    SET watcher_ids = ARRAY_APPEND(watcher_ids, @watcher_ids) 
                    WHERE project_task_id = @project_task_id 
                      AND project_id = @project_id";
                      
        await connection.ExecuteAsync(sql, parameters);
    }

    /// <inheritdoc />
    public async Task DetachTaskWatcherAsync(long watcherId, long projectTaskId, long projectId)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();

        var parameters = new DynamicParameters();
        parameters.Add("@watcherId", watcherId);
        parameters.Add("@project_task_id", projectTaskId);
        parameters.Add("@project_id", projectId);

        var sql = @"UPDATE project_management.project_tasks 
                    SET watcher_ids = ARRAY_REMOVE(watcher_ids, @watcherId::BIGINT) 
                    WHERE project_task_id = @project_task_id 
                      AND project_id = @project_id";
                      
        await connection.ExecuteAsync(sql, parameters);
    }

    /// <inheritdoc />
    public async Task UpdateTaskExecutorAsync(long executorId, long projectTaskId, long projectId)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();

        var parameters = new DynamicParameters();
        parameters.Add("@executor_id", executorId);
        parameters.Add("@project_task_id", projectTaskId);
        parameters.Add("@project_id", projectId);

        var sql = @"UPDATE project_management.project_tasks 
                    SET executor_id = @executor_id 
                    WHERE project_task_id = @project_task_id 
                      AND project_id = @project_id";
                      
        await connection.ExecuteAsync(sql, parameters);
    }

    /// <inheritdoc />
    public async Task UpdateTaskPriorityAsync(int priorityId, long projectTaskId, long projectId)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();

        var parameters = new DynamicParameters();
        parameters.Add("@priority_id", priorityId);
        parameters.Add("@project_task_id", projectTaskId);
        parameters.Add("@project_id", projectId);

        var sql = @"UPDATE project_management.project_tasks 
                    SET priority_id = @priority_id 
                    WHERE project_task_id = @project_task_id 
                      AND project_id = @project_id";
                      
        await connection.ExecuteAsync(sql, parameters);
    }

    /// <inheritdoc />
    public async Task CreateTaskLinkAsync(TaskLinkInput taskLinkInputd)
    {
        switch (taskLinkInputd.LinkType)
        {
            // Если создается обычная связь.
            case LinkTypeEnum.Link:
                await CreateTaskLinkDefaultAsync(taskLinkInputd.TaskFromLink, taskLinkInputd.TaskToLink,
                    LinkTypeEnum.Link, taskLinkInputd.ProjectId);
                break;
            
            // Если создается родительская связь.
            case LinkTypeEnum.Parent:
                await CreateTaskLinkParentAsync(taskLinkInputd.TaskFromLink, taskLinkInputd.ParentId!.Value,
                    LinkTypeEnum.Parent, taskLinkInputd.ProjectId);
                break;
            
            // Если создается дочерняя связь.
            case LinkTypeEnum.Child:
                await CreateTaskLinkChildAsync(taskLinkInputd.TaskFromLink, taskLinkInputd.ChildId!.Value,
                    LinkTypeEnum.Child, taskLinkInputd.ProjectId);
                break;
            
            // Если создается тип связи "зависит от".
            case LinkTypeEnum.Depend:
                await CreateTaskLinkDependAsync(taskLinkInputd.TaskFromLink, taskLinkInputd.DependId!.Value,
                    LinkTypeEnum.Depend, taskLinkInputd.ProjectId);
                break;
            
            default:
                throw new InvalidOperationException($"Недопустимый тип связи {taskLinkInputd.LinkType}.");
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<ProjectTaskEntity>> GetProjectTaskByProjectIdTaskIdsAsync(long projectId,
        IEnumerable<long> taskIds)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();

        var parameters = new DynamicParameters();
        parameters.Add("@project_id", projectId);
        parameters.Add("@task_ids", taskIds.AsList());

        var query = @"SELECT task_id,
                             task_status_id,
                             author_id,
                             watcher_ids,
                             name,
                             details,
                             created,
                             updated,
                             project_id,
                             project_task_id,
                             resolution_id,
                             tag_ids,
                             task_type_id,
                             executor_id,
                             priority_id 
                      FROM project_management.project_tasks 
                      WHERE project_id = @project_id 
                        AND task_id = ANY(@task_ids)";
        
        var result = await connection.QueryAsync<ProjectTaskEntity>(query, parameters);

        return result;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<TaskLinkEntity>> GetTaskLinksByProjectIdProjectTaskIdAsync(long projectId,
        long fromTaskId, LinkTypeEnum linkType)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        IEnumerable<TaskLinkEntity> result;

        if (linkType == LinkTypeEnum.Depend)
        {
            var parameters = new DynamicParameters();
            parameters.Add("@project_id", projectId);
            parameters.Add("@blocked_task_id", fromTaskId);
            parameters.Add("@link_type", new Enum(linkType));

            var query = @"SELECT link_id, 
                       from_task_id, 
                       to_task_id, 
                       link_type, 
                       parent_id, 
                       child_id, 
                       is_blocked, 
                       project_id, 
                       blocked_task_id 
                      FROM project_management.task_links 
                      WHERE project_id = @project_id 
                        AND blocked_task_id = @blocked_task_id 
                        AND link_type = @link_type";

            result = await connection.QueryAsync<TaskLinkEntity>(query, parameters);
        }

        else
        {
            var parameters = new DynamicParameters();
            parameters.Add("@project_id", projectId);
            parameters.Add("@from_task_id", fromTaskId);
            parameters.Add("@link_type", new Enum(linkType));

            var query = @"SELECT link_id, 
                       from_task_id, 
                       to_task_id, 
                       link_type, 
                       parent_id, 
                       child_id, 
                       is_blocked, 
                       project_id, 
                       blocked_task_id 
                      FROM project_management.task_links 
                      WHERE project_id = @project_id 
                        AND from_task_id = @from_task_id 
                        AND link_type = @link_type";

            result = await connection.QueryAsync<TaskLinkEntity>(query, parameters);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<AvailableTaskLinkOutput>> GetAvailableTaskLinkAsync(long projectId,
        LinkTypeEnum linkType)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();

        var parameters = new DynamicParameters();
        parameters.Add("@project_id", projectId);
        parameters.Add("@link_type", new Enum(linkType));

        var query = @"SELECT DISTINCT (pt.task_id) AS TaskId,
                                        pt.name AS TaskName,
                                        pt.project_task_id AS ProjectTaskId,
                                        pt.task_status_id AS TaskStatusId,
                                        pt.executor_id AS ExecutorId,
                                        pt.priority_id AS PriorityId,
                                        pt.updated AS LastUpdated,
                                        pt.created 
                      FROM project_management.project_tasks AS pt 
                               LEFT JOIN project_management.task_links AS tl ON pt.task_id = tl.to_task_id 
                      WHERE pt.project_id = @project_id 
                        AND pt.task_id NOT IN (SELECT tl.to_task_id 
                                               FROM project_management.task_links 
                                               WHERE tl.link_type = @link_type) 
                       ORDER BY pt.created";

        var result = await connection.QueryAsync<AvailableTaskLinkOutput>(query, parameters);

        return result;
    }

    /// <inheritdoc />
    public async Task RemoveTaskLinkAsync(LinkTypeEnum linkType, long removedLinkId, long currentTaskId,
        long projectId)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();

        // Если идет удаление обычной связи.
        if (linkType == LinkTypeEnum.Link)
        {
            var parameters = new DynamicParameters();
            parameters.Add("@from_task_id", currentTaskId);
            parameters.Add("@to_task_id", removedLinkId);
            parameters.Add("@project_id", projectId);
            parameters.Add("@link_type", new Enum(linkType));
            
            var query = @"DELETE 
                          FROM project_management.task_links 
                          WHERE from_task_id = @from_task_id 
                            AND to_task_id = @to_task_id 
                            AND project_id = @project_id 
                            AND link_type = @link_type";
            
            await connection.ExecuteAsync(query, parameters);
        }
        
        // Если идет удаление родительской связи.
        if (linkType == LinkTypeEnum.Parent)
        {
            // Сначала удаляем запись родителя.
            var parentParameters = new DynamicParameters();
            parentParameters.Add("@parent_id", removedLinkId);
            parentParameters.Add("@from_task_id", currentTaskId);
            parentParameters.Add("@project_id", projectId);
            parentParameters.Add("@link_type", new Enum(LinkTypeEnum.Parent));
            
            var parentQuery = @"DELETE 
                          FROM project_management.task_links 
                          WHERE parent_id = @parent_id 
                            AND from_task_id = @from_task_id 
                            AND project_id = @project_id 
                            AND link_type = @link_type";
            
            await connection.ExecuteAsync(parentQuery, parentParameters);
            
            // Затем удаляем запись дочки этого родителя.
            var childParameters = new DynamicParameters();
            parentParameters.Add("@child_id", removedLinkId);
            parentParameters.Add("@from_task_id", currentTaskId);
            parentParameters.Add("@project_id", projectId);
            parentParameters.Add("@link_type", new Enum(LinkTypeEnum.Child));
            
            var childQuery = @"DELETE 
                          FROM project_management.task_links 
                          WHERE child_id = @child_id 
                            AND from_task_id = @from_task_id 
                            AND project_id = @project_id 
                            AND link_type = @link_type";
            
            await connection.ExecuteAsync(childQuery, childParameters);
        }
        
        // Если идет удаление дочерней связи.
        if (linkType == LinkTypeEnum.Child)
        {
            // Сначала удаляем запись дочки.
            var childParameters = new DynamicParameters();
            childParameters.Add("@child_id", removedLinkId);
            childParameters.Add("@from_task_id", currentTaskId);
            childParameters.Add("@project_id", projectId);
            childParameters.Add("@link_type", new Enum(LinkTypeEnum.Child));
            
            var childQuery = @"DELETE 
                          FROM project_management.task_links 
                          WHERE child_id = @child_id 
                            AND from_task_id = @from_task_id 
                            AND project_id = @project_id 
                            AND link_type = @link_type";
            
            await connection.ExecuteAsync(childQuery, childParameters);
            
            // Затем удаляем запись родителя этой дочки.
            var parentParameters = new DynamicParameters();
            parentParameters.Add("@parent_id", currentTaskId);
            parentParameters.Add("@from_task_id", removedLinkId);
            parentParameters.Add("@project_id", projectId);
            parentParameters.Add("@link_type", new Enum(LinkTypeEnum.Parent));
            
            var parentQuery = @"DELETE 
                          FROM project_management.task_links 
                          WHERE child_id = @child_id 
                            AND from_task_id = @from_task_id 
                            AND project_id = @project_id 
                            AND link_type = @link_type";
            
            await connection.ExecuteAsync(parentQuery, parentParameters);
        }

        // Если идет удаление зависимой задачи.
        if (linkType == LinkTypeEnum.Depend)
        {
            // Сначала удаляем связь зависит от.
            var firstParameters = new DynamicParameters();
            firstParameters.Add("@from_task_id", currentTaskId);
            firstParameters.Add("@to_task_id", removedLinkId);
            firstParameters.Add("@project_id", projectId);
            firstParameters.Add("@is_blocked", false);
            firstParameters.Add("@link_type", new Enum(LinkTypeEnum.Depend));
            
            var firstQuery = @"DELETE 
                          FROM project_management.task_links 
                          WHERE from_task_id = @from_task_id 
                            AND to_task_id = @to_task_id 
                            AND project_id = @project_id 
                            AND is_blocked = @is_blocked 
                            AND link_type = @link_type";
            
            await connection.ExecuteAsync(firstQuery, firstParameters);
            
            // Затем удаляем связь блокирует.
            var secondParameters = new DynamicParameters();
            secondParameters.Add("@from_task_id", removedLinkId);
            secondParameters.Add("@blocked_task_id", currentTaskId);
            secondParameters.Add("@project_id", projectId);
            secondParameters.Add("@is_blocked", true);
            secondParameters.Add("@link_type", new Enum(LinkTypeEnum.Depend));
            
            var secondQuery = @"DELETE 
                          FROM project_management.task_links 
                          WHERE from_task_id = @from_task_id 
                            AND blocked_task_id = @blocked_task_id 
                            AND is_blocked = @is_blocked 
                            AND project_id = @project_id 
                            AND link_type = @link_type";
            
            await connection.ExecuteAsync(secondQuery, secondParameters);
        }
    }

    /// <inheritdoc />
    public async Task CreateProjectTaskDocumentsAsync(IEnumerable<ProjectTaskDocumentEntity> documents)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        var transaction = connection.BeginTransaction();

        var parameters = new List<DynamicParameters>();

        try
        {
            foreach (var d in documents)
            {
                var tempParameters = new DynamicParameters();
                tempParameters.Add("@document_type", new Enum(DocumentTypeEnum.ProjectTask));
                tempParameters.Add("@document_name", d.DocumentName);
                tempParameters.Add("@document_extension", Path.GetExtension(d.DocumentName));
                tempParameters.Add("@created", DateTime.UtcNow);
                tempParameters.Add("@updated", DateTime.UtcNow);
                tempParameters.Add("@project_id", d.ProjectId);
                tempParameters.Add("@task_id", d.TaskId);

                parameters.Add(tempParameters);
            }

            var query = @"INSERT INTO documents.project_documents (document_type, document_name, document_extension,
                                         created, updated, project_id, task_id) 
                      VALUES (@document_type, @document_name, @document_extension, @created, @updated, @project_id,
                              @task_id)";
                              
            await connection.ExecuteAsync(query, parameters);
            
            transaction.Commit();
        }
        
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<ProjectTaskDocumentEntity>> GetProjectTaskFilesAsync(long projectId, long taskId)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();

        var parameters = new DynamicParameters();
        parameters.Add("@projectId", projectId);
        parameters.Add("@taskId", taskId);

        var query = @"SELECT document_id, document_name, document_extension 
                      FROM documents.project_documents 
                      WHERE project_id = @projectId 
                        AND task_id = @taskId";

        var result = await connection.QueryAsync<ProjectTaskDocumentEntity>(query, parameters);

        return result;
    }

    #endregion

    #region Приватные методы.

    /// <summary>
    /// Метод создает обычную связь между задачами.
    /// </summary>
    /// <param name="taskFromLink">Id задачи, от которой исходит связь.</param>
    /// <param name="taskToLink">Id задачи, которую связывают.</param>
    /// <param name="linkType">Тип связи.</param>
    /// <param name="projectId">Id проекта.</param>
    private async Task CreateTaskLinkDefaultAsync(long taskFromLink, long taskToLink, LinkTypeEnum linkType,
        long projectId)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        var transaction = connection.BeginTransaction();
        
        try
        {
            // Параметры текущей задачи.
            var currentParameters = new DynamicParameters();
            currentParameters.Add("@from_task_id", taskFromLink);
            currentParameters.Add("@to_task_id", taskToLink);
            currentParameters.Add("@link_type", new Enum(linkType));
            currentParameters.Add("@is_blocked", false);
            currentParameters.Add("@project_id", projectId);
            
            // Параметры задачи, с которой устанавливается связь.
            var otherParameters = new DynamicParameters();
            otherParameters.Add("@from_task_id", taskFromLink);
            otherParameters.Add("@to_task_id", taskToLink);
            otherParameters.Add("@link_type", new Enum(linkType));
            otherParameters.Add("@is_blocked", false);
            otherParameters.Add("@project_id", projectId);

            var query = @"INSERT INTO project_management.task_links (
                                           from_task_id, to_task_id, link_type, is_blocked, project_id) 
                      VALUES (@from_task_id, @to_task_id, @link_type, @is_blocked, @project_id)";

            await connection.ExecuteAsync(query, currentParameters);
            await connection.ExecuteAsync(query, otherParameters);

            transaction.Commit();
        }

        catch
        {
            transaction.Rollback();

            throw;
        }

        finally
        {
            transaction.Dispose();
        }
    }
    
    /// <summary>
    /// Метод создает родительскую связь между задачами.
    /// </summary>
    /// <param name="taskFromLink">Id задачи, от которой исходит связь.</param>
    /// <param name="parentId">Id родительской задачи.</param>
    /// <param name="linkType">Тип связи.</param>
    /// <param name="projectId">Id проекта.</param>
    private async Task CreateTaskLinkParentAsync(long taskFromLink, long parentId, LinkTypeEnum linkType,
        long projectId)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        var transaction = connection.BeginTransaction();
        
        try
        {
            // Параметры текущей задачи.
            var currentParameters = new DynamicParameters();
            currentParameters.Add("@from_task_id", taskFromLink);
            
            // Проставляем родителя текущей задаче.
            currentParameters.Add("@parent_id", parentId);
            currentParameters.Add("@link_type", new Enum(linkType));
            currentParameters.Add("@is_blocked", false);
            currentParameters.Add("@project_id", projectId);
            
            var queryFirst = @"INSERT INTO project_management.task_links (
                                           from_task_id, parent_id, link_type, is_blocked, project_id) 
                      VALUES (@from_task_id, @parent_id, @link_type, @is_blocked, @project_id)";
            
            // Параметры задачи, с которой устанавливается связь.
            var otherParameters = new DynamicParameters();
            
            // Родительская задача становится текущей.
            otherParameters.Add("@from_task_id", parentId);
            
            // Текущая задача становится дочкой для родителя.
            otherParameters.Add("@child_id", taskFromLink);
            
            // Для родителя текущая задача становится дочкой.
            otherParameters.Add("@link_type", new Enum(LinkTypeEnum.Child));
            otherParameters.Add("@is_blocked", false);
            otherParameters.Add("@project_id", projectId);
            
            var querySecond = @"INSERT INTO project_management.task_links (
                                           from_task_id, child_id, link_type, is_blocked, project_id) 
                      VALUES (@from_task_id, @child_id, @link_type, @is_blocked, @project_id)";

            await connection.ExecuteAsync(queryFirst, currentParameters);
            await connection.ExecuteAsync(querySecond, otherParameters);

            transaction.Commit();
        }

        catch
        {
            transaction.Rollback();

            throw;
        }

        finally
        {
            transaction.Dispose();
        }
    }
    
    /// <summary>
    /// Метод создает дочернюю связь между задачами.
    /// </summary>
    /// <param name="taskFromLink">Id задачи, от которой исходит связь.</param>
    /// <param name="childId">Id дочерней задачи.</param>
    /// <param name="linkType">Тип связи.</param>
    /// <param name="projectId">Id проекта.</param>
    private async Task CreateTaskLinkChildAsync(long taskFromLink, long childId, LinkTypeEnum linkType,
        long projectId)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        var transaction = connection.BeginTransaction();
        
        try
        {
            // Параметры текущей задачи.
            var currentParameters = new DynamicParameters();
            currentParameters.Add("@from_task_id", taskFromLink);
            
            // Проставляем дочку текущей задаче.
            currentParameters.Add("@child_id", childId);
            currentParameters.Add("@link_type", new Enum(linkType));
            currentParameters.Add("@is_blocked", false);
            currentParameters.Add("@project_id", projectId);
            
            var queryFirst = @"INSERT INTO project_management.task_links (
                                           from_task_id, child_id, link_type, is_blocked, project_id) 
                      VALUES (@from_task_id, @child_id, @link_type, @is_blocked, @project_id)";
            
            // Параметры задачи, с которой устанавливается связь.
            var otherParameters = new DynamicParameters();
            
            // Дочка становится текущей.
            otherParameters.Add("@from_task_id", childId);
            
            // Текущая задача становится родителем для дочки.
            otherParameters.Add("@parent_id", taskFromLink);
            
            // Для дочки текущая задача становится родителем.
            otherParameters.Add("@link_type", new Enum(LinkTypeEnum.Parent));
            otherParameters.Add("@is_blocked", false);
            otherParameters.Add("@project_id", projectId);
            
            var querySecond = @"INSERT INTO project_management.task_links (
                                           from_task_id, parent_id, link_type, is_blocked, project_id) 
                      VALUES (@from_task_id, @parent_id, @link_type, @is_blocked, @project_id)";

            await connection.ExecuteAsync(queryFirst, currentParameters);
            await connection.ExecuteAsync(querySecond, otherParameters);

            transaction.Commit();
        }

        catch
        {
            transaction.Rollback();

            throw;
        }

        finally
        {
            transaction.Dispose();
        }
    }
    
    /// <summary>
    /// Метод создает зависимую связь между задачами.
    /// </summary>
    /// <param name="taskFromLink">Id задачи, от которой исходит связь.</param>
    /// <param name="childId">Id дочерней задачи.</param>
    /// <param name="linkType">Тип связи.</param>
    /// <param name="projectId">Id проекта.</param>
    private async Task CreateTaskLinkDependAsync(long taskFromLink, long dependId, LinkTypeEnum linkType,
        long projectId)
    {
        using var connection = await ConnectionProvider.GetConnectionAsync();
        var transaction = connection.BeginTransaction();
        
        try
        {
            // Параметры текущей задачи.
            var currentParameters = new DynamicParameters();
            currentParameters.Add("@from_task_id", taskFromLink);
            currentParameters.Add("@to_task_id", dependId);
            currentParameters.Add("@link_type", new Enum(linkType));
            currentParameters.Add("@is_blocked", false);
            currentParameters.Add("@project_id", projectId);
            
            var queryFirst = @"INSERT INTO project_management.task_links (
                                           from_task_id, to_task_id, link_type, is_blocked, project_id) 
                      VALUES (@from_task_id, @to_task_id, @link_type, @is_blocked, @project_id)";
            
            // Параметры задачи, с которой устанавливается связь.
            var otherParameters = new DynamicParameters();
            
            // Текущей задачей становится зависимая задача (то есть текущая от нее зависит).
            otherParameters.Add("@from_task_id", dependId);
            
            // Указываем, какую задачу блокирует.
            otherParameters.Add("@blocked_task_id", taskFromLink);
            otherParameters.Add("@link_type", new Enum(linkType));
            
            // Задача, от которой зависит текущая становится для нее блокирующей.
            otherParameters.Add("@is_blocked", true);
            otherParameters.Add("@project_id", projectId);
            
            var querySecond = @"INSERT INTO project_management.task_links (
                                           from_task_id, blocked_task_id, link_type, is_blocked, project_id) 
                      VALUES (@from_task_id, @blocked_task_id, @link_type, @is_blocked, @project_id)";

            await connection.ExecuteAsync(queryFirst, currentParameters);
            await connection.ExecuteAsync(querySecond, otherParameters);

            transaction.Commit();
        }

        catch
        {
            transaction.Rollback();

            throw;
        }

        finally
        {
            transaction.Dispose();
        }
    }

    #endregion
}