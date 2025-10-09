using Microsoft.Extensions.Logging;
using CodeAnalyzerAPI.Models;
using System.Text.RegularExpressions;

namespace CodeAnalyzerAPI.Services
{
    public interface IProjectStructureAnalyzer
    {
        Task<ProjectStructure> AnalyzeStructureAsync(string folderPath, List<string> extensions);
        Task<ProjectStructure> AnalyzeWithContentAsync(string folderPath, List<string> extensions);
    }

    public class ProjectStructureAnalyzer : IProjectStructureAnalyzer
    {
        private readonly ILogger<ProjectStructureAnalyzer> _logger;

        public ProjectStructureAnalyzer(ILogger<ProjectStructureAnalyzer> logger)
        {
            _logger = logger;
        }

        public async Task<ProjectStructure> AnalyzeStructureAsync(string folderPath, List<string> extensions)
        {
            return await AnalyzeProjectAsync(folderPath, extensions, readContent: false);
        }

        public async Task<ProjectStructure> AnalyzeWithContentAsync(string folderPath, List<string> extensions)
        {
            return await AnalyzeProjectAsync(folderPath, extensions, readContent: true);
        }

        private async Task<ProjectStructure> AnalyzeProjectAsync(string folderPath, List<string> extensions, bool readContent)
        {
            _logger.LogInformation("Анализ проекта: {FolderPath}, ReadContent: {ReadContent}", folderPath, readContent);

            var structure = new ProjectStructure { ProjectPath = folderPath };
            var fullPath = Path.GetFullPath(folderPath);

            if (!Directory.Exists(fullPath))
            {
                structure.Error = $"Папка {fullPath} не существует";
                return structure;
            }

            try
            {
                // 1. Анализ файловой структуры
                await AnalyzeFileStructure(fullPath, extensions, structure, readContent);

                // 2. Поиск строк подключения к БД и команд миграций
                await FindDatabaseConfigurations(structure);

                _logger.LogInformation("Анализ завершен. Найдено: {FilesCount} файлов, {ControllersCount} контроллеров, {DbContextsCount} DbContext, {MigrationsCount} миграций",
                    structure.Files.Count, structure.Controllers.Count, structure.DbContexts.Count, structure.Migrations.Count);

                return structure;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при анализе проекта");
                structure.Error = ex.Message;
                return structure;
            }
        }

        private async Task AnalyzeFileStructure(string folderPath, List<string> extensions, ProjectStructure structure, bool readContent)
        {
            var allFiles = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(file => extensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                .ToList();

            _logger.LogInformation("Найдено {FileCount} файлов с расширениями: {Extensions}",
                allFiles.Count, string.Join(", ", extensions));

            foreach (var filePath in allFiles)
            {
                var fileInfo = new FileInfo(filePath);
                var relativePath = Path.GetRelativePath(folderPath, filePath);

                var projectFile = new ProjectFile
                {
                    Path = relativePath,
                    Name = Path.GetFileName(filePath),
                    Extension = Path.GetExtension(filePath).ToLowerInvariant(),
                    Size = fileInfo.Length,
                    Directory = Path.GetDirectoryName(relativePath) ?? string.Empty
                };

                // Читаем содержимое если нужно
                if (readContent)
                {
                    try
                    {
                        projectFile.Content = await File.ReadAllTextAsync(filePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Не удалось прочитать файл {FilePath}: {Message}", filePath, ex.Message);
                    }
                }

                // Определяем тип файла с учетом содержимого
                await DetermineFileTypeAsync(projectFile, structure, readContent);
                structure.Files.Add(projectFile);
            }
        }

        private async Task DetermineFileTypeAsync(ProjectFile file, ProjectStructure structure, bool hasContent)
        {
            var detectionResult = new FileDetectionResult();

            // 1. Определяем по имени файла и пути
            DetectByFileNameAndPath(file, detectionResult);

            // 2. Если есть содержимое, анализируем его
            if (hasContent && !string.IsNullOrEmpty(file.Content))
            {
                await DetectByContentAsync(file, detectionResult);
            }

            // Устанавливаем уверенность
            file.Confidence = detectionResult.Confidence;

            // Выбираем основной тип (приоритет в порядке проверки)
            file.Type = detectionResult.DetectedTypes.FirstOrDefault();

            // Добавляем в соответствующие коллекции
            AddToCollections(file, structure, detectionResult.DetectedTypes);
        }

        private void DetectByFileNameAndPath(ProjectFile file, FileDetectionResult result)
        {
            // БАЗОВЫЕ КОНТРОЛЛЕРЫ - низкая уверенность
            if (file.Name.Equals("BaseController.cs", StringComparison.OrdinalIgnoreCase) ||
                file.Name.Contains("BaseController") && file.Extension == ".cs")
            {
                result.DetectedTypes.Add(FileType.BaseController);
                file.FoundPatterns.Add("Базовый контроллер");
                result.Confidence = 0.3; // Низкая уверенность для базовых контроллеров
            }
            // ОБЫЧНЫЕ КОНТРОЛЛЕРЫ - высокая уверенность
            else if (file.Name.EndsWith("controller.cs", StringComparison.OrdinalIgnoreCase) ||
                    (file.Name.Contains("Controller") && file.Extension == ".cs" && !file.Name.Contains("Base")))
            {
                result.DetectedTypes.Add(FileType.Controller);
                file.FoundPatterns.Add("Имя файла содержит 'Controller'");
                result.Confidence = 0.9;
            }

            // DbContext - высокая уверенность при точном совпадении
            if (file.Name.EndsWith("Context.cs", StringComparison.OrdinalIgnoreCase) &&
                file.Extension == ".cs")
            {
                result.DetectedTypes.Add(FileType.DbContext);
                file.FoundPatterns.Add("Имя файла заканчивается на 'Context'");
                result.Confidence = Math.Max(result.Confidence, 0.95);
            }
            else if (file.Name.Contains("Context", StringComparison.OrdinalIgnoreCase) &&
                    file.Extension == ".cs")
            {
                result.DetectedTypes.Add(FileType.DbContext);
                file.FoundPatterns.Add("Имя файла содержит 'Context'");
                result.Confidence = Math.Max(result.Confidence, 0.7);
            }

            // МИГРАЦИИ - высокая уверенность
            if ((file.Directory?.Contains("Migration", StringComparison.OrdinalIgnoreCase) == true ||
                 file.Name.Contains("Migration", StringComparison.OrdinalIgnoreCase)) &&
                file.Extension == ".cs")
            {
                result.DetectedTypes.Add(FileType.Migration);
                file.FoundPatterns.Add("Найдено в папке/файле миграций");
                result.Confidence = Math.Max(result.Confidence, 0.9);
            }

            // Program.cs - абсолютная уверенность
            if (file.Name.Equals("Program.cs", StringComparison.OrdinalIgnoreCase))
            {
                result.DetectedTypes.Add(FileType.Program);
                file.FoundPatterns.Add("Главный файл Program.cs");
                result.Confidence = 1.0;
            }

            // СТРАНИЦЫ Razor
            if (file.Extension == ".razor" || file.Extension == ".cshtml")
            {
                result.DetectedTypes.Add(FileType.Page);
                file.FoundPatterns.Add("Файл Razor/Blazor");
                result.Confidence = Math.Max(result.Confidence, 0.95);
            }

            // КОНФИГУРАЦИОННЫЕ файлы
            if (file.Name.Contains("appsettings", StringComparison.OrdinalIgnoreCase) ||
                file.Extension == ".json" || file.Extension == ".config")
            {
                result.DetectedTypes.Add(FileType.Config);
                file.FoundPatterns.Add("Конфигурационный файл");
                result.Confidence = Math.Max(result.Confidence, 0.9);
            }

            // СЕРВИСЫ
            if (file.Name.EndsWith("Service.cs", StringComparison.OrdinalIgnoreCase) ||
                (file.Directory?.Contains("Service", StringComparison.OrdinalIgnoreCase) == true &&
                 file.Extension == ".cs"))
            {
                result.DetectedTypes.Add(FileType.Service);
                file.FoundPatterns.Add("Файл сервиса");
                result.Confidence = Math.Max(result.Confidence, 0.8);
            }
        }

        private async Task DetectByContentAsync(ProjectFile file, FileDetectionResult result)
        {
            var content = file.Content;

            // КОНТРОЛЛЕР - ищем атрибуты контроллера
            if (content.Contains("[ApiController]") ||
                content.Contains("ControllerBase") ||
                (content.Contains("Microsoft.AspNetCore.Mvc") && content.Contains("Controller")))
            {
                if (!result.DetectedTypes.Contains(FileType.Controller) && !result.DetectedTypes.Contains(FileType.BaseController))
                {
                    // Проверяем, не базовый ли это контроллер
                    if (content.Contains("abstract class") || content.Contains("class.*Controller.*:"))
                    {
                        result.DetectedTypes.Add(FileType.BaseController);
                        file.FoundPatterns.Add("Базовый контроллер (определено по содержимому)");
                        result.Confidence = 0.8;
                    }
                    else
                    {
                        result.DetectedTypes.Add(FileType.Controller);
                        file.FoundPatterns.Add("Содержит атрибуты контроллера");
                        result.Confidence = Math.Max(result.Confidence, 0.95);
                    }
                }
            }

            // DbContext - ищем наследование от DbContext
            if (content.Contains(": DbContext") ||
                content.Contains(":DbContext") ||
                content.Contains("Microsoft.EntityFrameworkCore.DbContext"))
            {
                if (!result.DetectedTypes.Contains(FileType.DbContext))
                {
                    result.DetectedTypes.Add(FileType.DbContext);
                    file.FoundPatterns.Add("Наследует от DbContext");
                    result.Confidence = Math.Max(result.Confidence, 0.99);
                }
            }

            // МИГРАЦИИ - ищем специфичные для миграций шаблоны
            if (content.Contains("[Migration(") ||
                content.Contains("Microsoft.EntityFrameworkCore.Migrations") ||
                content.Contains("partial class") && content.Contains("Migration"))
            {
                if (!result.DetectedTypes.Contains(FileType.Migration))
                {
                    result.DetectedTypes.Add(FileType.Migration);
                    file.FoundPatterns.Add("Содержит код миграции");
                    result.Confidence = Math.Max(result.Confidence, 0.98);
                }
            }
        }

        private void AddToCollections(ProjectFile file, ProjectStructure structure, List<FileType> detectedTypes)
        {
            if (detectedTypes.Contains(FileType.Controller))
                structure.Controllers.Add(file);

            if (detectedTypes.Contains(FileType.BaseController))
                structure.Controllers.Add(file); // Базовые контроллеры тоже в контроллеры

            if (detectedTypes.Contains(FileType.DbContext))
                structure.DbContexts.Add(file);

            if (detectedTypes.Contains(FileType.Migration))
                structure.Migrations.Add(file);

            if (detectedTypes.Contains(FileType.Page))
                structure.Pages.Add(file);

            if (detectedTypes.Contains(FileType.Model))
                structure.Models.Add(file);

            if (detectedTypes.Contains(FileType.Service))
                structure.Services.Add(file);

            if (detectedTypes.Contains(FileType.Program))
                structure.ProgramFiles.Add(file);

            if (detectedTypes.Contains(FileType.Config))
                structure.ConfigFiles.Add(file);
        }

        private async Task FindDatabaseConfigurations(ProjectStructure structure)
        {
            try
            {
                // Ищем в Program.cs и appsettings.json
                var configFiles = structure.Files
                    .Where(f => f.Type == FileType.Program ||
                               (f.Name.Contains("appsettings") && f.Type == FileType.Config))
                    .ToList();

                foreach (var file in configFiles)
                {
                    if (string.IsNullOrEmpty(file.Content) && System.IO.File.Exists(Path.Combine(structure.ProjectPath, file.Path)))
                    {
                        file.Content = await File.ReadAllTextAsync(Path.Combine(structure.ProjectPath, file.Path));
                    }

                    if (!string.IsNullOrEmpty(file.Content))
                    {
                        // Ищем строки подключения в Program.cs
                        var connectionPatterns = new[]
                        {
                            @"UseSqlServer\([^)]*?([""'])(.*?)\1",
                            @"UseNpgsql\([^)]*?([""'])(.*?)\1",
                            @"UseSqlite\([^)]*?([""'])(.*?)\1",
                            @"ConnectionString.*?=.*?([""'])(.*?)\1",
                            @"builder\.Configuration\[([""'])ConnectionString\1\]"
                        };

                        foreach (var pattern in connectionPatterns)
                        {
                            var matches = Regex.Matches(file.Content, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
                            foreach (Match match in matches)
                            {
                                if (match.Groups.Count >= 3 && !string.IsNullOrEmpty(match.Groups[2].Value))
                                {
                                    var connectionString = match.Groups[2].Value;
                                    if (!structure.DatabaseConnectionStrings.Contains(connectionString))
                                    {
                                        structure.DatabaseConnectionStrings.Add(connectionString);
                                        _logger.LogInformation("Найдена строка подключения: {Connection}",
                                            connectionString.Length > 50 ? connectionString.Substring(0, 50) + "..." : connectionString);
                                    }
                                }
                            }
                        }

                        // Ищем команды миграций в Program.cs
                        var migrationPatterns = new[]
                        {
                            @"Database\.Migrate\(\)",
                            @"context\.Database\.Migrate\(\)",
                            @"MigrateAsync\(\)",
                            @"await.*Migrate",
                            @"DbMigration"
                        };

                        foreach (var pattern in migrationPatterns)
                        {
                            if (Regex.IsMatch(file.Content, pattern, RegexOptions.IgnoreCase))
                            {
                                var matches = Regex.Matches(file.Content, $".*{pattern}.*", RegexOptions.IgnoreCase);
                                foreach (Match match in matches)
                                {
                                    if (!string.IsNullOrEmpty(match.Value))
                                    {
                                        structure.MigrationCommands.Add(match.Value.Trim());
                                        _logger.LogInformation("Найдена команда миграции: {Command}", match.Value.Trim());
                                    }
                                }
                            }
                        }
                    }
                }

                // Также ищем в других конфигурационных файлах
                var otherConfigs = structure.Files
                    .Where(f => f.Type == FileType.Config && f.Content != null)
                    .ToList();

                foreach (var config in otherConfigs)
                {
                    var connectionMatches = Regex.Matches(config.Content, @"""ConnectionString""\s*:\s*""([^""]*)""", RegexOptions.Singleline);
                    foreach (Match match in connectionMatches)
                    {
                        if (match.Success && !string.IsNullOrEmpty(match.Groups[1].Value))
                        {
                            var connectionString = match.Groups[1].Value;
                            if (!structure.DatabaseConnectionStrings.Contains(connectionString))
                            {
                                structure.DatabaseConnectionStrings.Add(connectionString);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Ошибка при поиске конфигураций БД: {Message}", ex.Message);
            }
        }

        // Вспомогательный класс для передачи результатов определения типа
        private class FileDetectionResult
        {
            public List<FileType> DetectedTypes { get; set; } = new List<FileType>();
            public double Confidence { get; set; } = 1.0;
        }
    }
}