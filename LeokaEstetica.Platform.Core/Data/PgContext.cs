﻿using LeokaEstetica.Platform.Core.Extensions;
using LeokaEstetica.Platform.Models.Entities.Landing;
using LeokaEstetica.Platform.Models.Entities.Log;
using Microsoft.EntityFrameworkCore;

namespace LeokaEstetica.Platform.Core.Data;

/// <summary>
/// Класс датаконтекста Postgres.
/// </summary>
public class PgContext : DbContext
{
    private readonly DbContextOptions<PgContext> _options;

    public PgContext(DbContextOptions<PgContext> options) 
        : base(options)
    {
        _options = options;
    }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Настраиваем все маппинги приложения.
        MappingsExtensions.Configure(modelBuilder);
    }
    
    /// <summary>
    /// Таблица фона.
    /// </summary>
    public DbSet<FonEntity> Fons { get; set; }

    /// <summary>
    /// Таблица логов.
    /// </summary>
    public DbSet<LogEntity> Logs { get; set; }
}