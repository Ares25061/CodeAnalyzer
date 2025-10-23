using Microsoft.Extensions.Logging;
using CodeAnalyzerLibrary;
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
                await AnalyzeFileStructure(fullPath, extensions, structure, readContent);
                await FindDatabaseConfigurations(structure);

                _logger.LogInformation("Анализ завершен. Найдено: {FilesCount} файлов, {ControllersCount} контроллеров, {PagesCount} страниц, {DbContextsCount} DbContext, {MigrationsCount} миграций",
                    structure.Files.Count, structure.Controllers.Count, structure.Pages.Count, structure.DbContexts.Count, structure.Migrations.Count);

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

                bool shouldReadContent = readContent ||
                                       projectFile.Extension == ".razor" ||
                                       projectFile.Extension == ".cshtml";

                if (shouldReadContent)
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

                await DetermineFileTypeAsync(projectFile, structure, shouldReadContent);
                structure.Files.Add(projectFile);
            }
        }

        private async Task DetermineFileTypeAsync(ProjectFile file, ProjectStructure structure, bool hasContent)
        {
            var detectionResult = new FileDetectionResult();
            DetectByFileNameAndPath(file, detectionResult);
            if (hasContent && !string.IsNullOrEmpty(file.Content))
            {
                await DetectByContentAsync(file, detectionResult);
            }
            file.Confidence = detectionResult.Confidence;
            file.Type = detectionResult.DetectedTypes.FirstOrDefault();
            AddToCollections(file, structure, detectionResult.DetectedTypes);
        }

        private void DetectByFileNameAndPath(ProjectFile file, FileDetectionResult result)
        {
            if (file.Name.Equals("BaseController.cs", StringComparison.OrdinalIgnoreCase) ||
                file.Name.Contains("BaseController") && file.Extension == ".cs")
            {
                result.DetectedTypes.Add(FileType.BaseController);
                file.FoundPatterns.Add("Базовый контроллер");
                result.Confidence = 0.3;
            }
            else if (file.Name.EndsWith("controller.cs", StringComparison.OrdinalIgnoreCase) ||
                    (file.Name.Contains("Controller") && file.Extension == ".cs" && !file.Name.Contains("Base")))
            {
                result.DetectedTypes.Add(FileType.Controller);
                file.FoundPatterns.Add("Имя файла содержит 'Controller'");
                result.Confidence = 0.9;
            }

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

            if ((file.Directory?.Contains("Migration", StringComparison.OrdinalIgnoreCase) == true ||
                 file.Name.Contains("Migration", StringComparison.OrdinalIgnoreCase)) &&
                file.Extension == ".cs")
            {
                result.DetectedTypes.Add(FileType.Migration);
                file.FoundPatterns.Add("Найдено в папке/файле миграций");
                result.Confidence = Math.Max(result.Confidence, 0.9);
            }

            if (file.Name.Equals("Program.cs", StringComparison.OrdinalIgnoreCase))
            {
                result.DetectedTypes.Add(FileType.Program);
                file.FoundPatterns.Add("Главный файл Program.cs");
                result.Confidence = 1.0;
            }

            if (file.Extension == ".razor" || file.Extension == ".cshtml")
            {
                file.FoundPatterns.Add("Файл Razor/Blazor (требуется проверка содержимого)");
                result.Confidence = 0.3;
            }

            if (file.Name.Contains("appsettings", StringComparison.OrdinalIgnoreCase) ||
                file.Extension == ".json" || file.Extension == ".config")
            {
                result.DetectedTypes.Add(FileType.Config);
                file.FoundPatterns.Add("Конфигурационный файл");
                result.Confidence = Math.Max(result.Confidence, 0.9);
            }

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

            if (content.Contains("abstract class") &&
                (content.Contains("ControllerBase") || content.Contains("Controller")))
            {
                if (!result.DetectedTypes.Contains(FileType.BaseController))
                {
                    result.DetectedTypes.Add(FileType.BaseController);
                    file.FoundPatterns.Add("Базовый контроллер (абстрактный класс)");
                    result.Confidence = 0.9;
                }
            }
            else if (content.Contains("[ApiController]") ||
                     (content.Contains("ControllerBase") && !content.Contains("abstract class")) ||
                     (content.Contains("Microsoft.AspNetCore.Mvc") &&
                      content.Contains("Controller") &&
                      !content.Contains("abstract")))
            {
                if (!result.DetectedTypes.Contains(FileType.Controller))
                {
                    result.DetectedTypes.Add(FileType.Controller);
                    file.FoundPatterns.Add("Содержит атрибуты контроллера");
                    result.Confidence = Math.Max(result.Confidence, 0.95);
                }
            }

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

            if (file.Extension == ".razor" || file.Extension == ".cshtml")
            {
                bool isRealPage = await IsRealPageAsync(file, content);

                if (isRealPage)
                {
                    if (!result.DetectedTypes.Contains(FileType.Page))
                    {
                        result.DetectedTypes.Add(FileType.Page);
                        file.FoundPatterns.Add("Настоящая страница (содержит @page)");
                        result.Confidence = Math.Max(result.Confidence, 0.95);
                    }
                }
                else
                {
                    file.FoundPatterns.Add("Файл Razor/Blazor (не страница - нет @page)");
                }
            }
        }

        private async Task<bool> IsRealPageAsync(ProjectFile file, string content)
        {
            var pageDirectivePatterns = new[]
            {
                @"^\s*@page\s",
                @"^\s*@page\s+""[^""]*""",
                @"^\s*@page\s+""[^""]*""\s+.*$",
                @"@page\s*$",
                @"^\s*@page\s*\r?\n"
            };

            foreach (var pattern in pageDirectivePatterns)
            {
                if (Regex.IsMatch(content, pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void AddToCollections(ProjectFile file, ProjectStructure structure, List<FileType> detectedTypes)
        {
            if (detectedTypes.Contains(FileType.Controller))
                structure.Controllers.Add(file);

            if (detectedTypes.Contains(FileType.BaseController))
                structure.Controllers.Add(file);

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

        private class FileDetectionResult
        {
            public List<FileType> DetectedTypes { get; set; } = new List<FileType>();
            public double Confidence { get; set; } = 1.0;
        }
    }
}