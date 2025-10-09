using Microsoft.AspNetCore.Mvc;
using CodeAnalyzerAPI.Models;
using CodeAnalyzerAPI.Services;
using Ollama;
using System.Text;

namespace CodeAnalyzerAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CodeAnalyzerController : ControllerBase
    {
        private readonly IProjectStructureAnalyzer _structureAnalyzer;
        private readonly ICriteriaValidator _criteriaValidator;
        private readonly OllamaApiClient _ollama;
        private readonly ILogger<CodeAnalyzerController> _logger;

        public CodeAnalyzerController(
            IProjectStructureAnalyzer structureAnalyzer,
            ICriteriaValidator criteriaValidator,
            OllamaApiClient ollama,
            ILogger<CodeAnalyzerController> logger)
        {
            _structureAnalyzer = structureAnalyzer;
            _criteriaValidator = criteriaValidator;
            _ollama = ollama;
            _logger = logger;
        }

        [HttpPost("analyze")]
        public async Task<IActionResult> Analyze([FromBody] AnalysisRequest request)
        {
            var startTime = DateTime.UtcNow;

            _logger.LogInformation("Получен запрос на анализ: Путь={FolderPath}, Режим={Mode}",
                request.FolderPath, request.Mode);

            if (string.IsNullOrWhiteSpace(request.FolderPath))
            {
                return BadRequest(new AnalysisResponse
                {
                    Success = false,
                    Error = "Путь к папке обязателен"
                });
            }

            try
            {
                ProjectStructure structure;

                // Выбираем тип анализа
                if (request.Mode == AnalysisMode.FullContent)
                {
                    structure = await _structureAnalyzer.AnalyzeWithContentAsync(
                        request.FolderPath,
                        request.Extensions);
                }
                else
                {
                    structure = await _structureAnalyzer.AnalyzeStructureAsync(
                        request.FolderPath,
                        request.Extensions);
                }

                if (!string.IsNullOrEmpty(structure.Error))
                {
                    return BadRequest(new AnalysisResponse
                    {
                        Success = false,
                        Error = structure.Error
                    });
                }

                // Проверка критериев
                List<CriteriaCheckResult> results = new();
                if (request.Criteria?.Any() == true)
                {
                    results = await _criteriaValidator.ValidateCriteriaAsync(
                        structure, request.Criteria, request.Mode);
                }

                // Полный анализ с нейросетью
                if (request.Mode == AnalysisMode.FullContent)
                {
                    await PerformAiAnalysisAsync(structure, results);
                }

                var analysisTime = DateTime.UtcNow - startTime;

                return Ok(new AnalysisResponse
                {
                    Success = true,
                    Structure = structure,
                    Results = results,
                    AnalysisTime = analysisTime
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при анализе проекта");
                return StatusCode(500, new AnalysisResponse
                {
                    Success = false,
                    Error = ex.Message
                });
            }
        }

        [HttpPost("analyze-structure")]
        public async Task<IActionResult> AnalyzeStructureOnly([FromBody] StructureAnalysisRequest request)
        {
            _logger.LogInformation("Запрос на структурный анализ: {FolderPath}", request.FolderPath);

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
                    hasDatabaseConnection = structure.HasDatabaseConnection,
                    databaseConnectionFound = !string.IsNullOrEmpty(structure.DatabaseConnectionString)
                }
            });
        }

        private async Task PerformAiAnalysisAsync(ProjectStructure structure, List<CriteriaCheckResult> results)
        {
            try
            {
                _logger.LogInformation("Начат AI анализ для {FileCount} файлов", structure.Files.Count);

                // Группируем файлы по типам для анализа
                var analysisGroups = new[]
                {
                    new { Name = "Контроллеры", Files = structure.Controllers.Take(5) },
                    new { Name = "DbContext", Files = structure.DbContexts.Take(3) },
                    new { Name = "Миграции", Files = structure.Migrations.Take(3) },
                    new { Name = "Program.cs", Files = structure.ProgramFiles.Take(2) },
                    new { Name = "Сервисы", Files = structure.Services.Take(5) }
                };

                foreach (var group in analysisGroups.Where(g => g.Files.Any()))
                {
                    await AnalyzeFileGroupWithAI(group.Name, group.Files.ToList());
                    await Task.Delay(1000); // Задержка между запросами
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка AI анализа");
            }
        }

        private async Task AnalyzeFileGroupWithAI(string groupName, List<ProjectFile> files)
        {
            try
            {
                var prompt = BuildAnalysisPrompt(groupName, files);
                var response = await _ollama.Completions.GenerateCompletionAsync(
                    model: "deepseek-v3.1:671b-cloud",
                    prompt: prompt,
                    options: new RequestOptions { Temperature = 0.1f, NumPredict = 4000 });

                _logger.LogDebug("AI анализ завершен для группы: {GroupName}", groupName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка AI анализа для группы {GroupName}", groupName);
            }
        }

        private string BuildAnalysisPrompt(string groupName, List<ProjectFile> files)
        {
            var prompt = new StringBuilder();
            prompt.AppendLine($"Проанализируй {groupName} проекта:\n");

            foreach (var file in files)
            {
                prompt.AppendLine($"=== Файл: {file.Path} ===");
                prompt.AppendLine($"Размер: {file.Size} байт");
                prompt.AppendLine($"Обнаруженные паттерны: {string.Join(", ", file.FoundPatterns)}");

                if (!string.IsNullOrEmpty(file.Content))
                {
                    var preview = file.Content.Length > 2000 ? file.Content.Substring(0, 2000) + "..." : file.Content;
                    prompt.AppendLine("```csharp");
                    prompt.AppendLine(preview);
                    prompt.AppendLine("```");
                }
                prompt.AppendLine();
            }

            prompt.AppendLine("Задачи анализа:");
            prompt.AppendLine("1. Проверь корректность кода и архитектуры");
            prompt.AppendLine("2. Найди потенциальные проблемы");
            prompt.AppendLine("3. Проверь соответствие best practices");
            prompt.AppendLine("4. Оцени качество кода");

            return prompt.ToString();
        }
    }
}