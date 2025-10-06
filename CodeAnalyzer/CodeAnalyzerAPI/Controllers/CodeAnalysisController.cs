using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using Ollama;
using Microsoft.Extensions.Logging;

namespace MarketplaceApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CodeAnalyzerController : ControllerBase
    {
        private readonly OllamaApiClient _ollama;
        private readonly ILogger<CodeAnalyzerController> _logger;

        public CodeAnalyzerController(OllamaApiClient ollama, ILogger<CodeAnalyzerController> logger)
        {
            _ollama = ollama ?? throw new ArgumentNullException(nameof(ollama));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpPost("analyze")]
        public async Task<IActionResult> Analyze([FromBody] AnalyzeRequest request)
        {
            _logger.LogInformation("Получен запрос на анализ: Путь={FolderPath}, Промпт={Prompt}, Расширения={Extensions}",
                request.FolderPath, request.Prompt, string.Join(", ", request.Extensions ?? new List<string>()));

            if (string.IsNullOrWhiteSpace(request.FolderPath) || string.IsNullOrWhiteSpace(request.Prompt))
            {
                _logger.LogWarning("Недействительный запрос: Путь к папке или промпт пусты.");
                return BadRequest("Ошибка: путь к папке и промпт обязательны.");
            }

            _logger.LogInformation("Инициализация CodeAnalyzer...");
            var analyzer = new CodeAnalyzer(_ollama, _logger);

            try
            {
                _logger.LogInformation("Начало анализа для папки: {FolderPath}", request.FolderPath);
                var result = await analyzer.AnalyzeWithFullAccessAsync(
                    request.FolderPath,
                    request.Prompt,
                    request.Extensions ?? new List<string> { ".cs", ".js", ".py", ".txt", ".md" });

                _logger.LogInformation("Анализ успешно завершен.");
                return Ok(new { success = true, result = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при анализе папки: {FolderPath}. Подробности: {Message}", request.FolderPath, ex.Message);
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
    }

    public class AnalyzeRequest
    {
        public string FolderPath { get; set; }
        public string Prompt { get; set; }
        public List<string> Extensions { get; set; }
    }

    public class CodeAnalyzer
    {
        private readonly OllamaApiClient _client;
        private readonly string _model = "deepseek-v3.1:671b-cloud";
        private readonly ILogger<CodeAnalyzerController> _logger;

        public CodeAnalyzer(OllamaApiClient client, ILogger<CodeAnalyzerController> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private async Task<Dictionary<string, string>> ReadFilesFromFolderAsync(string folderPath, List<string> extensions, int maxFileSize = 100000)
        {
            var filesContent = new Dictionary<string, string>();
            _logger.LogInformation("Чтение файлов из папки: {FolderPath}, Расширения: {Extensions}, Максимальный размер: {MaxFileSize}",
                folderPath, string.Join(", ", extensions), maxFileSize);

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

            string result;
            if (filesContent.Count <= 5)
            {
                _logger.LogInformation("Анализ небольшого набора из {FileCount} файлов.", filesContent.Count);
                result = await AnalyzeSmallBatchAsync(filesContent, userPrompt);
            }
            else
            {
                _logger.LogInformation("Анализ большой кодовой базы с {FileCount} файлами.", filesContent.Count);
                result = await AnalyzeLargeCodebaseAsync(filesContent, userPrompt);
            }

            _logger.LogInformation("Анализ завершен, длина результата: {ResultLength}", result.Length);
            return result;
        }

        private async Task<string> AnalyzeSmallBatchAsync(Dictionary<string, string> filesContent, string userPrompt)
        {
            _logger.LogInformation("Начало AnalyzeSmallBatchAsync с {FileCount} файлами.", filesContent.Count);

            var filesContext = new StringBuilder("ПОЛНОЕ СОДЕРЖИМОЕ ВСЕХ ФАЙЛОВ:\n\n");
            foreach (var kvp in filesContent)
            {
                filesContext.AppendLine($"🔹 ФАЙЛ: {kvp.Key}\n```csharp\n{kvp.Value}\n```\n");
                _logger.LogDebug("Добавлен файл в контекст: {FileName}, Длина содержимого: {ContentLength}", kvp.Key, kvp.Value.Length);
            }

            string prompt = $"""
Ты - опытный разработчик C#. Проанализируй полное содержимое всех файлов и ответь на вопрос.

ЗАДАЧА: {userPrompt}

{filesContext}

Проанализируй КАЖДЫЙ файл полностью и дай точный ответ на вопрос.
Учитывай все детали: код, структура, атрибуты, логику.
""";

            _logger.LogDebug("Сгенерирован промпт для анализа небольшого набора, длина: {PromptLength}", prompt.Length);
            return await AskOllamaAsync(prompt, 8000);
        }

        private async Task<string> AnalyzeLargeCodebaseAsync(Dictionary<string, string> filesContent, string userPrompt)
        {
            _logger.LogInformation("Начало AnalyzeLargeCodebaseAsync с {FileCount} файлами.", filesContent.Count);

            _logger.LogDebug("Начало анализа структуры...");
            string structureAnalysis = await AnalyzeStructureAsync(filesContent, userPrompt);

            _logger.LogDebug("Начало анализа содержимого групп...");
            string contentAnalysis = await AnalyzeByGroupsAsync(filesContent, userPrompt);

            _logger.LogDebug("Начало анализа ключевых файлов...");
            string detailedAnalysis = await AnalyzeKeyFilesAsync(filesContent, userPrompt);

            _logger.LogDebug("Начало синтеза результатов...");
            string finalResult = await SynthesizeResultsAsync(structureAnalysis, contentAnalysis, detailedAnalysis, userPrompt, filesContent.Keys.ToList());

            _logger.LogInformation("Анализ большой кодовой базы завершен.");
            return finalResult;
        }

        private async Task<string> AnalyzeStructureAsync(Dictionary<string, string> filesContent, string userPrompt)
        {
            _logger.LogInformation("Анализ структуры для {FileCount} файлов.", filesContent.Count);
            var fileList = string.Join("\n", filesContent.Select(kvp => $"- {kvp.Key} ({kvp.Value.Length} chars)"));

            string prompt = $"""
Проанализируй структуру этих файлов:

ФАЙЛЫ:
{fileList}

ВОПРОС: {userPrompt}

Дай предварительный анализ:
1. Сколько всего файлов и их примерный размер
2. Общая структура проекта
""";

            _logger.LogDebug("Сгенерирован промпт для анализа структуры, длина: {PromptLength}", prompt.Length);
            return await AskOllamaAsync(prompt, 3000);
        }

        private async Task<string> AnalyzeByGroupsAsync(Dictionary<string, string> filesContent, string userPrompt, int groupSize = 3)
        {
            _logger.LogInformation("Начало AnalyzeByGroupsAsync с {FileCount} файлами, размер группы: {GroupSize}.", filesContent.Count, groupSize);
            var fileItems = filesContent.ToList();
            var groups = new List<List<KeyValuePair<string, string>>>();
            for (int i = 0; i < fileItems.Count; i += groupSize)
            {
                groups.Add(fileItems.GetRange(i, Math.Min(groupSize, fileItems.Count - i)));
            }

            _logger.LogInformation("Файлы разделены на {GroupCount} групп.", groups.Count);
            var groupResults = new List<Dictionary<string, object>>();

            for (int i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                _logger.LogDebug("Обработка группы {GroupIndex}/{GroupCount}", i + 1, groups.Count);

                var groupContext = new StringBuilder();
                foreach (var kvp in group)
                {
                    string preview = kvp.Value.Length > 4000 ? kvp.Value.Substring(0, 4000) + "..." : kvp.Value;
                    groupContext.AppendLine($"\n\n📄 ФАЙЛ: {kvp.Key}\n```csharp\n{preview}\n```");
                    _logger.LogDebug("Добавлен файл в контекст группы: {FileName}, Длина превью: {PreviewLength}", kvp.Key, preview.Length);
                }

                string prompt = $"""
Проанализируй эту группу файлов:

{groupContext}

ВОПРОС: {userPrompt}

Проанализируй содержимое этих файлов и ответь на вопрос применительно к этой группе.
Будь внимателен к деталям кода, структуры, логики.
""";

                _logger.LogDebug("Сгенерирован промпт для группы {GroupIndex}, длина: {PromptLength}", i + 1, prompt.Length);
                string result = await AskOllamaAsync(prompt, 4000);
                groupResults.Add(new Dictionary<string, object>
                {
                    { "files", group.Select(g => g.Key).ToList() },
                    { "analysis", result }
                });
            }

            var groupsSummary = string.Join("\n\n", groupResults.Select((res, i) =>
                $"Группа {i + 1} ({string.Join(", ", (List<string>)res["files"])}):\n{res["analysis"]}"));

            string summaryPrompt = $"""
На основе анализа всех групп файлов, суммируй информацию:

ВОПРОС: {userPrompt}

АНАЛИЗ ПО ГРУППАМ:
{groupsSummary}

Дай сводный ответ по всем группам.
""";

            _logger.LogDebug("Сгенерирован промпт для суммирования групп, длина: {PromptLength}", summaryPrompt.Length);
            return await AskOllamaAsync(summaryPrompt, 4000);
        }

        private async Task<string> AnalyzeKeyFilesAsync(Dictionary<string, string> filesContent, string userPrompt)
        {
            _logger.LogInformation("Начало AnalyzeKeyFilesAsync...");
            var keyFiles = filesContent.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Length > 6000 ? kvp.Value.Substring(0, 6000) : kvp.Value);

            if (keyFiles.Count == 0)
            {
                _logger.LogWarning("Ключевые файлы не найдены.");
                return "❌ Ключевые файлы не найдены";
            }

            _logger.LogInformation("Найдено {KeyFileCount} ключевых файлов.", keyFiles.Count);

            var keyFilesContext = new StringBuilder();
            foreach (var kvp in keyFiles)
            {
                keyFilesContext.AppendLine($"\n\n🎯 КЛЮЧЕВОЙ ФАЙЛ: {kvp.Key}\n```csharp\n{kvp.Value}\n```");
                _logger.LogDebug("Добавлен ключевой файл в контекст: {FileName}, Длина содержимого: {ContentLength}", kvp.Key, kvp.Value.Length);
            }

            string prompt = $"""
ДЕТАЛЬНЫЙ АНАЛИЗ КЛЮЧЕВЫХ ФАЙЛОВ:

{keyFilesContext}

ВОПРОС: {userPrompt}

Проанализируй эти файлы максимально подробно. Обрати внимание на:
- Классы и их наследование
- Атрибуты
- Методы и логику работы
""";

            _logger.LogDebug("Сгенерирован промпт для анализа ключевых файлов, длина: {PromptLength}", prompt.Length);
            return await AskOllamaAsync(prompt, 6000);
        }

        private async Task<string> SynthesizeResultsAsync(string structure, string content, string detailed, string question, List<string> allFiles)
        {
            _logger.LogInformation("Начало SynthesizeResultsAsync...");
            string prompt = $"""
СИНТЕЗИРУЙ ОКОНЧАТЕЛЬНЫЙ ОТВЕТ:

ВОПРОС: {question}

ВСЕ ФАЙЛЫ ДЛЯ АНАЛИЗА: {string.Join(", ", allFiles)}

ЭТАПЫ АНАЛИЗА:

1. 📊 СТРУКТУРНЫЙ АНАЛИЗ:
{structure}

2. 📈 АНАЛИЗ СОДЕРЖИМОГО:
{content}

3. 🔍 ДЕТАЛЬНЫЙ АНАЛИЗ:
{detailed}

На основе ВСЕХ этапов анализа дай ОКОНЧАТЕЛЬНЫЙ, ТОЧНЫЙ ответ на вопрос.
Учти информацию из всех этапов. Будь конкретен и точен.
""";

            _logger.LogDebug("Сгенерирован промпт для синтеза результатов, длина: {PromptLength}", prompt.Length);
            return await AskOllamaAsync(prompt, 5000);
        }

        private async Task<string> AskOllamaAsync(string prompt, int maxTokens)
        {
            _logger.LogInformation("Начало AskOllamaAsync с maxTokens: {MaxTokens}", maxTokens);
            _logger.LogDebug("Длина промпта: {PromptLength}", prompt.Length);

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

                // Используем нестримируемый запрос для получения полного ответа
                var response = await _client.Completions.GenerateCompletionAsync(
                    model: _model,
                    prompt: prompt,
                    stream: false, // Важно: отключаем стриминг
                    options: requestOptions);

                _logger.LogInformation("Вызов Ollama API завершен, длина ответа: {ResponseLength}", response.Response.Length);
                return response.Response;
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("SSL"))
            {
                _logger.LogError(ex, "Ошибка SSL при вызове Ollama API. Проверьте сертификат или URL сервера.");
                return $"❌ Ошибка SSL: Не удалось установить соединение с Ollama. Проверьте настройки сервера или используйте HTTP вместо HTTPS.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при вызове Ollama API: {Message}", ex.Message);
                return $"❌ Ошибка Ollama: {ex.Message}";
            }
        }
    }
}