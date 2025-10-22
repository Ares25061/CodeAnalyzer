using Microsoft.AspNetCore.Mvc;
using CodeAnalyzerLibrary;
using CodeAnalyzerAPI.Services;
using Ollama;
using Microsoft.Extensions.Logging;
using System.Text;

namespace CodeAnalyzerAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CodeAnalyzerController : ControllerBase
    {
        private readonly OllamaApiClient _ollama;
        private readonly ILogger<CodeAnalyzerController> _logger;
        private readonly IProjectStructureAnalyzer _structureAnalyzer;

        public CodeAnalyzerController(
            OllamaApiClient ollama,
            ILogger<CodeAnalyzerController> logger,
            IProjectStructureAnalyzer structureAnalyzer)
        {
            _ollama = ollama ?? throw new ArgumentNullException(nameof(ollama));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _structureAnalyzer = structureAnalyzer ?? throw new ArgumentNullException(nameof(structureAnalyzer));
        }

        /// <summary>
        /// Полный анализ кодовой базы проекта с проверкой критериев и AI-анализом
        /// </summary>
        /// <remarks>
        /// Пример запроса:
        ///
        ///     POST /api/codeanalyzer/analyze
        ///     {
        ///       "folderPath": "C:/Projects/MyProject",
        ///       "useOllama": true,
        ///       "customPrompt": "Проанализируй архитектуру проекта",
        ///       "extensions": [".cs", ".razor", ".json"],
        ///       "criteria": [
        ///         {
        ///           "id": 1,
        ///           "name": "Проверка контроллеров",
        ///           "description": "Должно быть не менее 2 контроллеров",
        ///           "rules": [
        ///             {
        ///               "property": "controllers_count",
        ///               "operator": "greater_than_or_equal",
        ///               "value": 2
        ///             }
        ///           ]
        ///         }
        ///       ]
        ///     }
        ///
        /// </remarks>
        /// <param name="request">Запрос на анализ проекта</param>
        /// <returns>Результаты анализа структуры, проверки критериев и AI-анализ</returns>
        [HttpPost("analyze")]
        public async Task<IActionResult> Analyze([FromBody] AnalysisRequest request)
        {
            _logger.LogInformation("Получен запрос на анализ: Путь={FolderPath}, Критериев={CriteriaCount}, UseOllama={UseOllama}, HasPrompt={HasPrompt}",
                request.FolderPath, request.Criteria?.Count ?? 0, request.UseOllama, !string.IsNullOrEmpty(request.CustomPrompt));

            if (string.IsNullOrWhiteSpace(request.FolderPath))
            {
                return BadRequest(new { success = false, error = "Путь к папке обязателен" });
            }

            try
            {
                var structure = await _structureAnalyzer.AnalyzeStructureAsync(
                    request.FolderPath,
                    request.Extensions ?? new List<string> { ".cs", ".razor", ".cshtml", ".json", ".config" });

                if (!string.IsNullOrEmpty(structure.Error))
                {
                    return BadRequest(new { success = false, error = structure.Error });
                }

                var criteriaResults = CheckCriteria(request.Criteria, structure);

                string aiAnalysis = string.Empty;
                if (request.UseOllama)
                {
                    aiAnalysis = await GetAIAnalysis(request.Criteria, criteriaResults, structure, request.CustomPrompt);
                }

                var response = new
                {
                    success = true,
                    structure = new
                    {
                        totalFiles = structure.TotalFiles,
                        controllers = structure.TotalControllers,
                        pages = structure.TotalPages,
                        migrations = structure.Migrations.Count,
                        dbContexts = structure.DbContexts.Count,
                        services = structure.Services.Count,
                        controllerNames = structure.Controllers.Select(c => c.Name).ToList(),
                        fileNames = structure.Files.Select(f => f.Name).ToList()
                    },
                    criteriaResults = criteriaResults,
                    aiAnalysis = aiAnalysis,
                    summary = new
                    {
                        totalCriteria = criteriaResults.Count,
                        passedCriteria = criteriaResults.Count(r => r.Passed),
                        failedCriteria = criteriaResults.Count(r => !r.Passed),
                        message = $"Проверено критериев: {criteriaResults.Count}, Выполнено: {criteriaResults.Count(r => r.Passed)}"
                    }
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при анализе папки: {FolderPath}", request.FolderPath);
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Анализ только структуры проекта без проверки критериев
        /// </summary>
        /// <remarks>
        /// Пример запроса:
        ///
        ///     POST /api/codeanalyzer/analyze-structure
        ///     {
        ///       "folderPath": "C:/Projects/MyProject",
        ///       "extensions": [".cs", ".razor", ".cshtml"]
        ///     }
        ///
        /// </remarks>
        /// <param name="request">Запрос на анализ структуры</param>
        /// <returns>Детальная информация о структуре проекта</returns>
        [HttpPost("analyze-structure")]
        public async Task<IActionResult> AnalyzeStructureOnly([FromBody] StructureAnalysisRequest request)
        {
            _logger.LogInformation("Структурный анализ: {FolderPath}", request.FolderPath);

            try
            {
                var structure = await _structureAnalyzer.AnalyzeStructureAsync(
                    request.FolderPath,
                    request.Extensions ?? new List<string> { ".cs", ".razor", ".cshtml", ".json", ".config" });

                if (!string.IsNullOrEmpty(structure.Error))
                {
                    return BadRequest(new { success = false, error = structure.Error });
                }

                return Ok(new
                {
                    success = true,
                    structure = structure,
                    summary = new
                    {
                        totalFiles = structure.TotalFiles,
                        controllers = structure.TotalControllers,
                        pages = structure.TotalPages,
                        migrations = structure.Migrations.Count,
                        dbContexts = structure.DbContexts.Count,
                        services = structure.Services.Count,
                        hasDatabaseConnection = structure.HasDatabaseConnection,
                        databaseConnectionsCount = structure.DatabaseConnectionStrings.Count,
                        migrationCommandsCount = structure.MigrationCommands.Count,
                        controllerNames = structure.Controllers.Select(c => c.Name).ToList()
                    },
                    databaseConnections = structure.DatabaseConnectionStrings,
                    migrationCommands = structure.MigrationCommands
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при структурном анализе");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        private List<CriteriaCheckResult> CheckCriteria(List<AnalysisCriteria> criteria, ProjectStructure structure)
        {
            var results = new List<CriteriaCheckResult>();

            if (criteria == null || !criteria.Any())
                return results;

            foreach (var criterion in criteria)
            {
                var result = new CriteriaCheckResult
                {
                    CriteriaId = criterion.Id,
                    CriteriaName = criterion.Name,
                    Passed = true,
                    Message = "Критерий выполнен успешно",
                    Evidence = new List<string>()
                };

                try
                {
                    bool allRulesPassed = true;

                    foreach (var rule in criterion.Rules)
                    {
                        bool rulePassed = CheckRule(rule, structure, out string ruleMessage);
                        result.Evidence.Add(ruleMessage);

                        if (!rulePassed)
                        {
                            allRulesPassed = false;
                            result.Passed = false;
                            result.Message = "Критерий не выполнен";
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Passed = false;
                    result.Message = $"Ошибка проверки критерия: {ex.Message}";
                    result.Evidence.Add($"Исключение: {ex.Message}");
                }

                results.Add(result);
            }

            return results;
        }

        private bool CheckRule(CriteriaRule rule, ProjectStructure structure, out string message)
        {
            int actualValue = GetPropertyValue(rule.Property, structure);

            int expectedValue = 0;
            if (!string.IsNullOrEmpty(rule.Value))
            {
                if (!int.TryParse(rule.Value, out expectedValue))
                {
                    message = $"Некорректное значение: {rule.Value}";
                    return false;
                }
            }

            bool passed = false;

            switch (rule.Operator.ToLower())
            {
                case "equals":
                    passed = actualValue == expectedValue;
                    message = $"{rule.Property}: {actualValue} == {expectedValue}";
                    break;

                case "greater_than":
                    passed = actualValue > expectedValue;
                    message = $"{rule.Property}: {actualValue} > {expectedValue}";
                    break;

                case "greater_than_or_equal":
                    passed = actualValue >= expectedValue;
                    message = $"{rule.Property}: {actualValue} >= {expectedValue}";
                    break;

                case "less_than":
                    passed = actualValue < expectedValue;
                    message = $"{rule.Property}: {actualValue} < {expectedValue}";
                    break;

                case "less_than_or_equal":
                    passed = actualValue <= expectedValue;
                    message = $"{rule.Property}: {actualValue} <= {expectedValue}";
                    break;

                case "exists":
                    passed = actualValue > 0;
                    message = $"{rule.Property}: {actualValue} (должен существовать)";
                    break;

                default:
                    passed = false;
                    message = $"Неизвестный оператор: {rule.Operator}";
                    break;
            }

            return passed;
        }

        private int GetPropertyValue(string property, ProjectStructure structure)
        {
            return property.ToLower() switch
            {
                "controllers_count" or "controllers" => GetControllersWithoutBase(structure),
                "controllers_count_excluding_base" => GetControllersExcludingBase(structure),
                "controllers_count_excluding_base_abstract" => GetControllersExcludingBaseAbstract(structure),
                "controllers_count_excluding_specific" => GetControllersExcludingSpecific(structure),
                "controllers_without_base" => GetControllersWithoutBase(structure),
                "pages_count" or "pages" => structure.TotalPages,
                "dbcontext_count" or "dbcontext" => structure.DbContexts.Count,
                "migrations_count" or "migrations" => structure.Migrations.Count,
                "services_count" or "services" => structure.Services.Count,
                "files_count" or "files" => structure.TotalFiles,
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


        private int GetControllersExcludingBase(ProjectStructure structure)
        {
            var excludedKeywords = new[] { "base" };
            return structure.Controllers.Count(c =>
                !excludedKeywords.Any(keyword =>
                    c.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
        }

        private int GetControllersExcludingBaseAbstract(ProjectStructure structure)
        {
            var excludedKeywords = new[] { "base", "abstract", "generic" };
            return structure.Controllers.Count(c =>
                !excludedKeywords.Any(keyword =>
                    c.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
        }

        private int GetControllersExcludingSpecific(ProjectStructure structure)
        {
            var excludedNames = new[] { "BaseController" };
            return structure.Controllers.Count(c =>
                !excludedNames.Any(name =>
                    c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        }
        /// <summary>
        /// Проверка подключения к Ollama и работоспособности AI-модели
        /// </summary>
        /// <remarks>
        /// Пример запроса:
        ///
        ///     GET /api/codeanalyzer/check-connection
        ///
        /// </remarks>
        /// <returns>Результат проверки подключения</returns>
        [HttpGet("check-connection")]
        public async Task<IActionResult> CheckConnection()
        {
            try
            {
                _logger.LogInformation("Проверка подключения к Ollama");

                var response = await _ollama.Completions.GenerateCompletionAsync(
                    model: "deepseek-v3.1:671b-cloud",
                    prompt: "Тебе необходимо написать лишь одно слово: Арбуз. Тебе не нужно на что то отвечать, просто напиши Арбуз.",
                    stream: false);

                var result = response.Response?.Trim();
                var isConnected = string.Equals(result, "Арбуз", StringComparison.OrdinalIgnoreCase);

                _logger.LogInformation("Результат проверки подключения: {Result}, Успешно: {IsConnected}", result, isConnected);

                return Ok(new
                {
                    success = true,
                    connected = isConnected,
                    response = result,
                    message = isConnected ? "Подключение к Ollama работает корректно" : "Некорректный ответ от Ollama"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проверке подключения к Ollama");
                return Ok(new
                {
                    success = false,
                    connected = false,
                    response = string.Empty,
                    message = $"Ошибка подключения: {ex.Message}"
                });
            }
        }
        private async Task<string> GetAIAnalysis(
    List<AnalysisCriteria> criteria,
    List<CriteriaCheckResult> results,
    ProjectStructure structure,
    string customPrompt)
        {
            try
            {
                int GetControllersWithoutBase()
                {
                    return structure.Controllers.Count(c =>
                        !c.Name.Contains("BaseController", StringComparison.OrdinalIgnoreCase) &&
                        !c.Name.Contains("Base", StringComparison.OrdinalIgnoreCase));
                }

                var basePrompt = $"""
                СТРУКТУРА ПРОЕКТА:
                - Файлов: {structure.TotalFiles}
                - Контроллеров (без BaseController): {GetControllersWithoutBase()}
                - Всего файлов контроллеров: {structure.TotalControllers}
                - Имена контроллеров: {string.Join(", ", structure.Controllers.Select(c => c.Name))}
                - Страниц: {structure.TotalPages}
                - DbContext: {structure.DbContexts.Count}
                - Миграций: {structure.Migrations.Count}

                КРИТЕРИИ ПРОВЕРКИ:
                {string.Join("\n", criteria.Select(c => $"- {c.Name}: {c.Description}"))}

                РЕЗУЛЬТАТЫ ПРОВЕРКИ:
                {string.Join("\n", results.Select(r => $"- {r.CriteriaName}: {(r.Passed ? "✅ ВЫПОЛНЕНО" : "❌ НЕ ВЫПОЛНЕНО")}"))}

                ДЕТАЛИ ПРОВЕРКИ:
                {string.Join("\n", results.SelectMany(r => r.Evidence.Select(e => $"- {r.CriteriaName}: {e}")))}

                ОБЩАЯ СТАТИСТИКА:
                - Всего критериев: {results.Count}
                - Выполнено: {results.Count(r => r.Passed)}
                - Не выполнено: {results.Count(r => !r.Passed)}
                """;

                string finalPrompt;

                if (!string.IsNullOrEmpty(customPrompt))
                {
                    finalPrompt = $"""
            {basePrompt}

            ДОПОЛНИТЕЛЬНАЯ ИНСТРУКЦИЯ ПОЛЬЗОВАТЕЛЯ:
            {customPrompt}

            ЗАДАЧА: Проанализируй проект согласно критериям и дополнительной инструкции пользователя. 
            Обрати внимание на реальные результаты проверки критериев выше.
            """;
                }
                else
                {
                    finalPrompt = $"""
            {basePrompt}

            ЗАДАЧА: Дай краткий итог по проверке критериев на основе РЕАЛЬНЫХ РЕЗУЛЬТАТОВ проверки выше. 
            Не придумывай свои результаты - используй только те, что указаны в РЕЗУЛЬТАТАХ ПРОВЕРКИ.

            Формат ответа должен соответствовать реальным результатам:
            ✅ Выполнено: X критериев (если есть выполненные)
            ❌ Не выполнено: Y критериев (если есть невыполненные)
            Основные проблемы: [перечисли реальные проблемы из результатов проверки]

            ВАЖНО: Не меняй фактические результаты проверки!
            """;
                }

                _logger.LogInformation(
                    "Отправка промта к Ollama. Длина: {PromptLength}",
                    finalPrompt.Length);

                var response = await _ollama.Completions.GenerateCompletionAsync(
                    model: "deepseek-v3.1:671b-cloud",
                    prompt: finalPrompt,
                    stream: false);

                return response.Response?.Trim() ?? "Не удалось получить анализ";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при AI-анализе");
                return $"Ошибка AI-анализа: {ex.Message}";
            }
        }
    }
}