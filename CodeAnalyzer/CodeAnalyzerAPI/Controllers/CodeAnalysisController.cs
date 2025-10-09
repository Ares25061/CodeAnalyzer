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

            _logger.LogInformation("Получен запрос на анализ: Путь={FolderPath}, Режим={Mode}, Критериев={CriteriaCount}",
                request.FolderPath, request.Mode, request.Criteria?.Count ?? 0);

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
                // 1. Анализ структуры проекта
                var structure = await _structureAnalyzer.AnalyzeStructureAsync(
                    request.FolderPath,
                    request.Extensions);

                if (!string.IsNullOrEmpty(structure.Error))
                {
                    return BadRequest(new AnalysisResponse
                    {
                        Success = false,
                        Error = structure.Error
                    });
                }

                // 2. Проверка критериев
                List<CriteriaCheckResult> results = new();
                if (request.Criteria?.Any() == true)
                {
                    results = await _criteriaValidator.ValidateCriteriaAsync(
                        structure, request.Criteria, request.Mode);
                }

                // 3. Если нужен полный анализ с нейросетью
                if (request.Mode == AnalysisMode.FullContent)
                {
                    await PerformFullContentAnalysis(structure, results);
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
                request.Extensions ?? new List<string> { ".cs", ".razor", ".cshtml", ".json" });

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
                    dbContexts = structure.DbContexts.Count
                }
            });
        }

        private async Task PerformFullContentAnalysis(ProjectStructure structure, List<CriteriaCheckResult> results)
        {
            // Здесь можно добавить логику для полного анализа содержимого с нейросетью
            // с разбивкой на группы файлов как вы хотели
            _logger.LogInformation("Начат полный анализ содержимого для {FileCount} файлов", structure.Files.Count);

            // Пример: разбиваем файлы на группы по 5
            var fileGroups = structure.Files
                .Where(f => f.Type != FileType.Unknown)
                .Select((file, index) => new { file, index })
                .GroupBy(x => x.index / 5)
                .Select(g => g.Select(x => x.file).ToList())
                .ToList();

            foreach (var fileGroup in fileGroups)
            {
                await AnalyzeFileGroupWithAI(fileGroup);
                // Добавляем задержку между запросами чтобы не перегружать API
                await Task.Delay(1000);
            }
        }

        private async Task AnalyzeFileGroupWithAI(List<ProjectFile> files)
        {
            try
            {
                var prompt = BuildAnalysisPrompt(files);
                var response = await _ollama.Completions.GenerateCompletionAsync(
                    model: "deepseek-v3.1:671b-cloud",
                    prompt: prompt,
                    options: new RequestOptions { Temperature = 0.1f, NumPredict = 4000 });

                _logger.LogDebug("AI анализ завершен для группы из {FileCount} файлов", files.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка AI анализа для группы файлов");
            }
        }

        private string BuildAnalysisPrompt(List<ProjectFile> files)
        {
            var prompt = new StringBuilder("Проанализируй следующие файлы проекта:\n\n");

            foreach (var file in files)
            {
                prompt.AppendLine($"Файл: {file.Path}");
                prompt.AppendLine($"Тип: {file.Type}");
                prompt.AppendLine("```csharp");
                prompt.AppendLine(file.Content.Length > 3000 ? file.Content.Substring(0, 3000) + "..." : file.Content);
                prompt.AppendLine("```\n");
            }

            prompt.AppendLine("Проверь:");
            prompt.AppendLine("1. Корректность кода");
            prompt.AppendLine("2. Наличие потенциальных ошибок");
            prompt.AppendLine("3. Соответствие best practices");
            prompt.AppendLine("4. Наличие необходимых атрибутов и конфигураций");

            return prompt.ToString();
        }
    }

    public class StructureAnalysisRequest
    {
        public string FolderPath { get; set; } = string.Empty;
        public List<string> Extensions { get; set; } = new List<string> { ".cs", ".razor", ".cshtml", ".json" };
    }
}