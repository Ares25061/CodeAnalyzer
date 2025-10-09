// CodeAnalyzerAPI/Services/CriteriaValidator.cs
using CodeAnalyzerAPI.Models;

namespace CodeAnalyzerAPI.Services
{
    public interface ICriteriaValidator
    {
        Task<List<CriteriaCheckResult>> ValidateCriteriaAsync(ProjectStructure structure, List<AnalysisCriteria> criteria, AnalysisMode mode);
    }

    public class CriteriaValidator : ICriteriaValidator
    {
        private readonly IProjectStructureAnalyzer _structureAnalyzer;
        private readonly ILogger<CriteriaValidator> _logger;

        public CriteriaValidator(IProjectStructureAnalyzer structureAnalyzer, ILogger<CriteriaValidator> logger)
        {
            _structureAnalyzer = structureAnalyzer;
            _logger = logger;
        }

        public async Task<List<CriteriaCheckResult>> ValidateCriteriaAsync(ProjectStructure structure, List<AnalysisCriteria> criteria, AnalysisMode mode)
        {
            var results = new List<CriteriaCheckResult>();

            foreach (var criterion in criteria)
            {
                // Пропускаем критерии полного анализа в структурном режиме
                if (mode == AnalysisMode.Structural && criterion.Type == CriteriaType.FullContent)
                {
                    results.Add(new CriteriaCheckResult
                    {
                        CriteriaId = criterion.Id,
                        CriteriaName = criterion.Name,
                        Passed = false,
                        Message = "Требуется полный анализ содержимого файлов",
                        Evidence = new List<string> { "Этот критерий требует полного анализа содержимого файлов" }
                    });
                    continue;
                }

                var result = await ValidateCriterionAsync(structure, criterion, mode);
                results.Add(result);
            }

            return results;
        }

        private async Task<CriteriaCheckResult> ValidateCriterionAsync(ProjectStructure structure, AnalysisCriteria criterion, AnalysisMode mode)
        {
            var result = new CriteriaCheckResult
            {
                CriteriaId = criterion.Id,
                CriteriaName = criterion.Name,
                Passed = true
            };

            foreach (var rule in criterion.Rules)
            {
                var ruleResult = await ValidateRuleAsync(structure, rule, mode);
                if (!ruleResult.Passed)
                {
                    result.Passed = false;
                    result.Message = ruleResult.Message;
                    result.Evidence.AddRange(ruleResult.Evidence);
                }
            }

            if (result.Passed && string.IsNullOrEmpty(result.Message))
            {
                result.Message = "Критерий выполнен успешно";
            }

            return result;
        }

        private async Task<CriteriaCheckResult> ValidateRuleAsync(ProjectStructure structure, CriteriaRule rule, AnalysisMode mode)
        {
            return rule.Property.ToLowerInvariant() switch
            {
                "controllers" => ValidateControllers(structure, rule),
                "pages" => ValidatePages(structure, rule),
                "migrations" => ValidateMigrations(structure, rule),
                "dbcontext" => ValidateDbContext(structure, rule),
                "config" => ValidateConfig(structure, rule),
                _ => new CriteriaCheckResult { Passed = false, Message = $"Неизвестное свойство: {rule.Property}" }
            };
        }

        private CriteriaCheckResult ValidateControllers(ProjectStructure structure, CriteriaRule rule)
        {
            var count = structure.Controllers.Count;
            return ValidateCountRule("контроллеров", count, rule, structure.Controllers.Select(c => c.Path).ToList());
        }

        private CriteriaCheckResult ValidatePages(ProjectStructure structure, CriteriaRule rule)
        {
            var count = structure.Pages.Count;
            return ValidateCountRule("страниц", count, rule, structure.Pages.Select(p => p.Path).ToList());
        }

        private CriteriaCheckResult ValidateDatabaseConnection(ProjectStructure structure, CriteriaRule rule)
        {
            var evidence = new List<string>();

            if (structure.HasDatabaseConnection)
            {
                evidence.Add($"Найдена строка подключения: {structure.DatabaseConnectionString}");
            }

            if (structure.DbContexts.Any())
            {
                evidence.AddRange(structure.DbContexts.Select(d => $"DbContext: {d.Path}"));
            }

            if (structure.Migrations.Any())
            {
                evidence.AddRange(structure.Migrations.Select(m => $"Миграция: {m.Path}"));
            }

            return new CriteriaCheckResult
            {
                Passed = structure.HasDatabaseConnection,
                Message = structure.HasDatabaseConnection
                    ? "Подключение к БД настроено"
                    : "Подключение к БД не найдено",
                Evidence = evidence
            };
        }

        private CriteriaCheckResult ValidateMigrations(ProjectStructure structure, CriteriaRule rule)
        {
            var count = structure.Migrations.Count;
            var evidence = structure.Migrations.Select(m => m.Path).ToList();

            if (structure.DbContexts.Any() && count == 0)
            {
                evidence.Add("ВНИМАНИЕ: DbContext найден, но миграций нет");
            }

            return ValidateCountRule("миграций", count, rule, evidence);
        }

        private CriteriaCheckResult ValidateDbContext(ProjectStructure structure, CriteriaRule rule)
        {
            var exists = structure.DbContexts.Any();
            var evidence = structure.DbContexts.Select(d => d.Path).ToList();

            if (rule.Operator == "exists" && bool.TryParse(rule.Value?.ToString(), out bool required))
            {
                return new CriteriaCheckResult
                {
                    Passed = exists == required,
                    Message = exists ? "DbContext найден" : "DbContext не найден",
                    Evidence = evidence
                };
            }

            return ValidateCountRule("DbContext", structure.DbContexts.Count, rule, evidence);
        }

        private CriteriaCheckResult ValidateCountRule(string itemName, int actualCount, CriteriaRule rule, List<string> evidence)
        {
            if (rule.Operator == "count_greater_than" && int.TryParse(rule.Value?.ToString(), out int minCount))
            {
                return new CriteriaCheckResult
                {
                    Passed = actualCount >= minCount,
                    Message = actualCount >= minCount
                        ? $"Найдено {actualCount} {itemName} (требуется: {minCount}+)"
                        : $"Найдено только {actualCount} {itemName} (требуется: {minCount}+)",
                    Evidence = evidence
                };
            }
            else if (rule.Operator == "exists")
            {
                return new CriteriaCheckResult
                {
                    Passed = actualCount > 0,
                    Message = actualCount > 0 ? $"{itemName} найдены" : $"{itemName} не найдены",
                    Evidence = evidence
                };
            }

            return new CriteriaCheckResult
            {
                Passed = false,
                Message = $"Неизвестный оператор: {rule.Operator}",
                Evidence = evidence
            };
        }

        private CriteriaCheckResult ValidateConfig(ProjectStructure structure, CriteriaRule rule)
        {
            // Здесь можно добавить проверку конфигурационных файлов
            var configFiles = structure.ConfigFiles.Where(f =>
                f.Name.Contains("appsettings", StringComparison.OrdinalIgnoreCase)).ToList();

            return new CriteriaCheckResult
            {
                Passed = configFiles.Any(),
                Message = configFiles.Any() ? "Конфигурационные файлы найдены" : "Конфигурационные файлы не найдены",
                Evidence = configFiles.Select(f => f.Path).ToList()
            };
        }
    }
}