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
        public async Task<IActionResult> Analyze([FromBody] AnalysisRequest request)
        {
            _logger.LogInformation("Получен запрос на анализ: Путь={FolderPath}, Режим={Mode}",
                request.FolderPath, request.Mode);

            if (string.IsNullOrWhiteSpace(request.FolderPath))
            {
                _logger.LogWarning("Недействительный запрос: Путь к папке пуст.");
                return BadRequest("Ошибка: путь к папке обязателен.");
            }

            try
            {
                _logger.LogInformation("Начало анализа для папки: {FolderPath}", request.FolderPath);

                // Читаем файлы из папки
                var filesContent = await ReadFilesFromFolderAsync(
                    request.FolderPath,
                    request.Extensions ?? new List<string> { ".cs", ".razor", ".cshtml", ".json", ".config" });

                if (filesContent.Count == 0 || filesContent.ContainsKey("error"))
                {
                    string errorMessage = filesContent.GetValueOrDefault("error", "Файлы не найдены");
                    _logger.LogWarning("Файлы не найдены или произошла ошибка: {ErrorMessage}", errorMessage);
                    return BadRequest(new { success = false, error = errorMessage });
                }

                _logger.LogInformation("Найдено и обработано {FileCount} файлов для анализа.", filesContent.Count);

                // Формируем промпт для анализа
                string prompt = CreateAnalysisPrompt(filesContent, request.Mode);

                // Отправляем запрос к Ollama
                var result = await AskOllamaAsync(prompt);

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
                        string content = await System.IO.File.ReadAllTextAsync(filePath); // Явно указываем System.IO.File
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

        private string CreateAnalysisPrompt(Dictionary<string, string> filesContent, AnalysisMode mode)
        {
            var filesContext = new StringBuilder("СОДЕРЖИМОЕ ФАЙЛОВ ПРОЕКТА:\n\n");

            foreach (var kvp in filesContent)
            {
                // Ограничиваем длину содержимого файла для больших файлов
                string content = kvp.Value.Length > 4000 ? kvp.Value.Substring(0, 4000) + "..." : kvp.Value;
                filesContext.AppendLine($"🔹 ФАЙЛ: {kvp.Key}\n```csharp\n{content}\n```\n");
            }

            if (mode == AnalysisMode.FullContent)
            {
                return $"""
Ты - опытный разработчик C# и архитектор ПО. Проанализируй содержимое файлов проекта и дай развернутую оценку.

{filesContext}

Проведи детальный анализ по следующим аспектам:
1. Архитектура проекта и структура
2. Качество кода и соответствие best practices
3. Наличие и качество контроллеров API
4. Работа с базой данных (DbContext, миграции)
5. Использование Dependency Injection
6. Обработка ошибок и валидация
7. Безопасность и аутентификация
8. Общие рекомендации по улучшению

Дай развернутый ответ с конкретными примерами из кода.
""";
            }
            else
            {
                return $"""
Ты - опытный разработчик C#. Проанализируй структуру проекта на основе предоставленных файлов.

{filesContext}

Сделай структурный анализ проекта:
- Общая архитектура и организация кода
- Основные компоненты и их назначение
- Зависимости между модулями
- Ключевые технологии и фреймворки
- Потенциальные проблемы архитектуры

Дай краткий, но информативный анализ.
""";
            }
        }

        private async Task<string> AskOllamaAsync(string prompt)
        {
            _logger.LogInformation("Начало AskOllamaAsync, длина промпта: {PromptLength}", prompt.Length);

            try
            {
                _logger.LogDebug("Отправка запроса к Ollama API с моделью: deepseek-v3.1:671b-cloud");

                // Используем тот же подход, что и в рабочем ChatBotController
                var response = await _ollama.Completions.GenerateCompletionAsync(
                    model: "deepseek-v3.1:671b-cloud",
                    prompt: prompt,
                    stream: false);

                _logger.LogInformation("Вызов Ollama API завершен, длина ответа: {ResponseLength}", response.Response.Length);
                return response.Response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при вызове Ollama API: {Message}", ex.Message);
                return $"❌ Ошибка при анализе кода: {ex.Message}";
            }
        }
    }
}