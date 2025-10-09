using Microsoft.AspNetCore.Mvc;
using CodeAnalyzerAPI.Models;
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

        [HttpPost("analyze")]
        public async Task<IActionResult> Analyze([FromBody] AnalyzeRequest request)
        {
            _logger.LogInformation("Получен запрос на анализ: Путь={FolderPath}, Режим={Mode}",
                request.FolderPath, request.Mode);

            if (string.IsNullOrWhiteSpace(request.FolderPath))
            {
                _logger.LogWarning("Недействительный запрос: Путь к папке пуст.");
                return BadRequest("Ошибка: путь к папке обязателен.");
            }

            _logger.LogInformation("Инициализация CodeAnalyzer...");
            var analyzer = new CodeAnalyzer(_ollama, _logger);

            try
            {
                _logger.LogInformation("Начало анализа для папки: {FolderPath}", request.FolderPath);
                var result = await analyzer.AnalyzeWithFullAccessAsync(
                    request.FolderPath,
                    request.Mode == AnalysisMode.FullContent ? "ci/cd" : "структурный анализ",
                    request.Extensions ?? new List<string> { ".cs", ".razor", ".cshtml", ".json", ".config" });

                _logger.LogInformation("Анализ успешно завершен.");
                return Ok(new { success = true, result = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при анализе папки: {FolderPath}. Подробности: {Message}", request.FolderPath, ex.Message);
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        [HttpPost("analyze-structure")]
        public async Task<IActionResult> AnalyzeStructureOnly([FromBody] StructureAnalysisRequest request)
        {
            _logger.LogInformation("Структурный анализ: {FolderPath}", request.FolderPath);

            try
            {
                // Используем зарегистрированный сервис вместо создания нового
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
                        migrationCommandsCount = structure.MigrationCommands.Count
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

        [HttpGet("check-ollama")]
        public async Task<IActionResult> CheckOllama()
        {
            try
            {
                _logger.LogInformation("Проверка подключения к Ollama...");

                // Простая проверка как в вашем рабочем примере
                var requestOptions = new RequestOptions
                {
                    Temperature = 0.1f,
                    NumPredict = 1,
                    TopK = 40,
                    TopP = 0.9f
                };

                var response = await _ollama.Completions.GenerateCompletionAsync(
                    model: "llama2",
                    prompt: "test",
                    stream: false,
                    options: requestOptions);

                var available = response != null && !string.IsNullOrEmpty(response.Response);
                _logger.LogInformation("Ollama доступен: {Available}", available);

                return Ok(new
                {
                    available = available,
                    status = available ? "Ollama доступен" : "Ollama недоступен"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проверке Ollama");
                return Ok(new
                {
                    available = false,
                    status = "Ollama недоступен",
                    error = ex.Message
                });
            }
        }
    }

    public class AnalyzeRequest
    {
        public string FolderPath { get; set; } = string.Empty;
        public AnalysisMode Mode { get; set; } = AnalysisMode.Structural;
        public List<string> Extensions { get; set; } = new List<string> { ".cs", ".razor", ".cshtml", ".json", ".config" };
    }

    public class StructureAnalysisRequest
    {
        public string FolderPath { get; set; } = string.Empty;
        public List<string> Extensions { get; set; } = new List<string> { ".cs", ".razor", ".cshtml", ".json", ".config" };
    }

    public class CodeAnalyzer
    {
        private readonly OllamaApiClient _client;
        private readonly string _model = "deepseek-coder:6.7b";
        private readonly ILogger<CodeAnalyzerController> _logger;

        private readonly List<string> _ciCdCriteria = new List<string>
        {
            "Количество и качество контроллеров: Подсчитай количество контроллеров, проверь наличие атрибутов ([ApiController], [Route]), обработку ошибок, структуру действий.",
            "Наличие подключения к БД: Проверь наличие DbContext, connection strings в конфигурации, вызовов services.AddDbContext.",
            "Структура проекта: Оцени общую архитектуру (разделение на слои, использование DI, наличие сервисов).",
            "Наличие миграций: Проверь наличие файлов миграций и команд выполнения миграций.",
            "Качество кода: Проверь наличие комментариев, последовательность именования, соответствие best practices."
        };

        public CodeAnalyzer(OllamaApiClient client, ILogger<CodeAnalyzerController> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private async Task<Dictionary<string, string>> ReadFilesFromFolderAsync(string folderPath, List<string> extensions, int maxFileSize = 100000)
        {
            var filesContent = new Dictionary<string, string>();
            _logger.LogInformation("Чтение файлов из папки: {FolderPath}, Расширения: {Extensions}",
                folderPath, string.Join(", ", extensions));

            try
            {
                var folder = Path.GetFullPath(folderPath);
                _logger.LogDebug("Разрешенный путь к папке: {Folder}", folder);

                if (!Directory.Exists(folder))
                {
                    _logger.LogWarning("Папка не существует: {Folder}", folder);
                    return new Dictionary<string, string> { { "error", $"Папка {folder} не существует" } };
                }

                var filePaths = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                    .Where(file => extensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    .ToList();

                _logger.LogInformation("Найдено {FileCount} файлов с подходящими расширениями.", filePaths.Count);

                foreach (var filePath in filePaths)
                {
                    var fileInfo = new FileInfo(filePath);
                    _logger.LogDebug("Обработка файла: {FilePath}, Размер: {FileSize} байт", filePath, fileInfo.Length);

                    if (fileInfo.Length > maxFileSize)
                    {
                        _logger.LogWarning("Пропуск файла {FilePath}, так как размер превышает {MaxFileSize} байт", filePath, maxFileSize);
                        continue;
                    }

                    try
                    {
                        string content = await File.ReadAllTextAsync(filePath);
                        string relPath = Path.GetRelativePath(folder, filePath);
                        filesContent[relPath] = content;
                        _logger.LogInformation("Успешно прочитан файл: {RelPath}, Длина содержимого: {ContentLength}", relPath, content.Length);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка при чтении файла: {FilePath}", filePath);
                    }
                }

                _logger.LogInformation("Успешно прочитано {FileCount} файлов.", filesContent.Count);
                return filesContent;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при чтении файлов из папки: {FolderPath}", folderPath);
                return new Dictionary<string, string> { { "error", ex.Message } };
            }
        }

        public async Task<string> AnalyzeWithFullAccessAsync(string folderPath, string userPrompt, List<string> fileExtensions)
        {
            _logger.LogInformation("Начало AnalyzeWithFullAccessAsync: Путь={FolderPath}, Промпт={Prompt}, Расширения={Extensions}",
                folderPath, userPrompt, string.Join(", ", fileExtensions));

            var filesContent = await ReadFilesFromFolderAsync(folderPath, fileExtensions);

            if (filesContent.Count == 0 || filesContent.ContainsKey("error"))
            {
                string errorMessage = filesContent.GetValueOrDefault("error", "Файлы не найдены");
                _logger.LogWarning("Файлы не найдены или произошла ошибка: {ErrorMessage}", errorMessage);
                return $"❌ Ошибка: {errorMessage}";
            }

            _logger.LogInformation("Найдено и обработано {FileCount} файлов для анализа.", filesContent.Count);

            bool isCiCdMode = string.IsNullOrWhiteSpace(userPrompt) || userPrompt.ToLowerInvariant() == "ci/cd";

            string result;
            if (filesContent.Count <= 5)
            {
                _logger.LogInformation("Анализ небольшого набора из {FileCount} файлов.", filesContent.Count);
                result = await AnalyzeSmallBatchAsync(filesContent, userPrompt, isCiCdMode);
            }
            else
            {
                _logger.LogInformation("Анализ большой кодовой базы с {FileCount} файлами.", filesContent.Count);
                result = await AnalyzeLargeCodebaseAsync(filesContent, userPrompt, isCiCdMode);
            }

            _logger.LogInformation("Анализ завершен, длина результата: {ResultLength}", result.Length);
            return result;
        }

        private async Task<string> AnalyzeSmallBatchAsync(Dictionary<string, string> filesContent, string userPrompt, bool isCiCdMode)
        {
            _logger.LogInformation("Начало AnalyzeSmallBatchAsync с {FileCount} файлами.", filesContent.Count);

            var filesContext = new StringBuilder("СОДЕРЖИМОЕ ФАЙЛОВ:\n\n");
            foreach (var kvp in filesContent)
            {
                filesContext.AppendLine($"🔹 ФАЙЛ: {kvp.Key}\n```csharp\n{kvp.Value}\n```\n");
            }

            string prompt;
            if (isCiCdMode)
            {
                var checksSection = new StringBuilder("Выполни следующие проверки:\n\n");
                for (int i = 0; i < _ciCdCriteria.Count; i++)
                {
                    checksSection.AppendLine($"{i + 1}. {_ciCdCriteria[i]}");
                }

                prompt = $"""
Ты - опытный разработчик C#. Проанализируй содержимое файлов проекта.

{checksSection}

{filesContext}

Проанализируй КАЖДЫЙ файл полностью. Учитывай все детали: код, структура, атрибуты, логику.
В конце дай общий итог и рекомендации.
""";
            }
            else
            {
                prompt = $"""
Ты - опытный разработчик C#. Проанализируй содержимое файлов проекта.

ЗАДАЧА: {userPrompt}

{filesContext}

Проанализируй КАЖДЫЙ файл полностью и дай точный ответ на вопрос.
""";
            }

            _logger.LogDebug("Сгенерирован промпт для анализа, длина: {PromptLength}", prompt.Length);
            return await AskOllamaAsync(prompt, 8000);
        }

        private async Task<string> AnalyzeLargeCodebaseAsync(Dictionary<string, string> filesContent, string userPrompt, bool isCiCdMode)
        {
            _logger.LogInformation("Начало AnalyzeLargeCodebaseAsync с {FileCount} файлами.", filesContent.Count);

            // Для больших проектов анализируем только ключевые файлы
            var keyFiles = filesContent
                .Where(kvp =>
                    kvp.Key.Contains("Controller") ||
                    kvp.Key.Contains("Context") ||
                    kvp.Key.Contains("Program.cs") ||
                    kvp.Key.Contains("Startup.cs"))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Length > 4000 ? kvp.Value.Substring(0, 4000) + "..." : kvp.Value);

            if (!keyFiles.Any())
            {
                keyFiles = filesContent.Take(10).ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Length > 4000 ? kvp.Value.Substring(0, 4000) + "..." : kvp.Value);
            }

            var keyFilesContext = new StringBuilder();
            foreach (var kvp in keyFiles)
            {
                keyFilesContext.AppendLine($"\n🎯 ФАЙЛ: {kvp.Key}\n```csharp\n{kvp.Value}\n```");
            }

            string prompt;
            if (isCiCdMode)
            {
                prompt = $"""
Ты - опытный разработчик C#. Проанализируй ключевые файлы большого проекта.

КЛЮЧЕВЫЕ ФАЙЛЫ:
{keyFilesContext}

Проанализируй архитектуру проекта, наличие контроллеров, подключение к БД, общую структуру.
Дай оценку качества кода и рекомендации по улучшению.
""";
            }
            else
            {
                prompt = $"""
Ты - опытный разработчик C#. Проанализируй ключевые файлы большого проекта.

ВОПРОС: {userPrompt}

КЛЮЧЕВЫЕ ФАЙЛЫ:
{keyFilesContext}

Дай ответ на вопрос на основе анализа ключевых файлов.
""";
            }

            return await AskOllamaAsync(prompt, 6000);
        }

        private async Task<string> AskOllamaAsync(string prompt, int maxTokens)
        {
            _logger.LogInformation("Начало AskOllamaAsync с maxTokens: {MaxTokens}", maxTokens);

            var requestOptions = new RequestOptions
            {
                Temperature = 0.1f,
                NumPredict = maxTokens,
                TopK = 40,
                TopP = 0.9f
            };

            try
            {
                _logger.LogDebug("Отправка запроса к Ollama API с моделью: {Model}", _model);

                var response = await _client.Completions.GenerateCompletionAsync(
                    model: _model,
                    prompt: prompt,
                    stream: false,
                    options: requestOptions);

                _logger.LogInformation("Вызов Ollama API завершен, длина ответа: {ResponseLength}", response.Response.Length);
                return response.Response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при вызове Ollama API: {Message}", ex.Message);
                return $"❌ Ошибка Ollama: {ex.Message}";
            }
        }
    }
}