using CodeAnalyzerLibrary;

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
                Passed = true,
                Evidence = new List<string>()
            };

            foreach (var rule in criterion.Rules)
            {
                var ruleResult = await ValidateRuleAsync(structure, rule, mode);
                if (!ruleResult.Passed)
                {
                    result.Passed = false;
                }
                result.Evidence.AddRange(ruleResult.Evidence);
            }

            result.Message = result.Passed ? "Критерий выполнен успешно" : "Критерий не выполнен";
            return result;
        }

        private async Task<CriteriaCheckResult> ValidateRuleAsync(ProjectStructure structure, CriteriaRule rule, AnalysisMode mode)
        {
            var result = new CriteriaCheckResult
            {
                Evidence = new List<string>(),
                Passed = true
            };

            try
            {
                int actualValue = GetPropertyValue(rule.Property, structure);
                int expectedValue = 0;

                if (!string.IsNullOrEmpty(rule.Value))
                {
                    if (!int.TryParse(rule.Value, out expectedValue))
                    {
                        result.Passed = false;
                        result.Evidence.Add($"Некорректное значение: {rule.Value}");
                        return result;
                    }
                }

                bool rulePassed = false;
                string message = string.Empty;

                switch (rule.Operator.ToLower())
                {
                    case "equals":
                        rulePassed = actualValue == expectedValue;
                        message = $"{rule.Property}: {actualValue} == {expectedValue}";
                        break;

                    case "greater_than":
                        rulePassed = actualValue > expectedValue;
                        message = $"{rule.Property}: {actualValue} > {expectedValue}";
                        break;

                    case "greater_than_or_equal":
                        rulePassed = actualValue >= expectedValue;
                        message = $"{rule.Property}: {actualValue} >= {expectedValue}";
                        break;

                    case "less_than":
                        rulePassed = actualValue < expectedValue;
                        message = $"{rule.Property}: {actualValue} < {expectedValue}";
                        break;

                    case "less_than_or_equal":
                        rulePassed = actualValue <= expectedValue;
                        message = $"{rule.Property}: {actualValue} <= {expectedValue}";
                        break;

                    case "exists":
                        rulePassed = actualValue > 0;
                        message = $"{rule.Property}: {actualValue} (должен существовать)";
                        break;

                    default:
                        rulePassed = false;
                        message = $"Неизвестный оператор: {rule.Operator}";
                        break;
                }

                result.Passed = rulePassed;
                result.Evidence.Add(message);

                if (!rulePassed && !string.IsNullOrEmpty(rule.ErrorMessage))
                {
                    result.Evidence.Add(rule.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.Evidence.Add($"Ошибка проверки правила: {ex.Message}");
            }

            return result;
        }

        private int GetPropertyValue(string property, ProjectStructure structure)
        {
            return property.ToLower() switch
            {
                "controllers_count" or "controllers" => GetControllersWithoutBase(structure), 
                "controllers_count_excluding_base" => structure.GetControllersExcludingBase(),
                "controllers_count_excluding_base_abstract" => structure.GetControllersExcludingBaseAbstract(),
                "controllers_count_excluding_specific" => structure.GetControllersExcludingSpecific(),
                "controllers_without_base" => GetControllersWithoutBase(structure),
                "pages_count" or "pages" => structure.TotalPages,
                "dbcontext_count" or "dbcontext" => structure.DbContexts.Count,
                "migrations_count" or "migrations" => structure.Migrations.Count,
                "services_count" or "services" => structure.Services.Count,
                "files_count" or "files" => structure.TotalFiles,
                "models_count" or "models" => structure.Models.Count,
                "config_files_count" or "config_files" => structure.ConfigFiles.Count,
                "program_files_count" or "program_files" => structure.ProgramFiles.Count,
                "has_database_connection" => structure.HasDatabaseConnection ? 1 : 0,
                "database_connections_count" => structure.DatabaseConnectionStrings.Count,
                "migration_commands_count" => structure.MigrationCommands.Count,
                _ => 0
            };
        }

        private int GetControllersWithoutBase(ProjectStructure structure)
        {
            return structure.Controllers.Count(c =>
                !c.Name.Contains("BaseController", StringComparison.OrdinalIgnoreCase) &&
                !c.Name.Contains("Base", StringComparison.OrdinalIgnoreCase) &&
                c.Type != FileType.BaseController);
        }
    }
}