using Microsoft.Extensions.Logging;
using CodeAnalyzerAPI.Models;

namespace CodeAnalyzerAPI.Services
{
    public interface IProjectStructureAnalyzer
    {
        Task<ProjectStructure> AnalyzeStructureAsync(string folderPath, List<string> extensions);
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
            _logger.LogInformation("Анализ структуры проекта: {FolderPath}", folderPath);

            var structure = new ProjectStructure { ProjectPath = folderPath };
            var fullPath = Path.GetFullPath(folderPath);

            if (!Directory.Exists(fullPath))
            {
                structure.Error = $"Папка {fullPath} не существует";
                return structure;
            }

            try
            {
                await AnalyzeFileStructure(fullPath, extensions, structure);
                _logger.LogInformation("Структурный анализ завершен. Найдено: {FilesCount} файлов, {ControllersCount} контроллеров, {PagesCount} страниц",
                    structure.Files.Count, structure.Controllers.Count, structure.Pages.Count);

                return structure;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при анализе структуры проекта");
                structure.Error = ex.Message;
                return structure;
            }
        }

        private async Task AnalyzeFileStructure(string folderPath, List<string> extensions, ProjectStructure structure)
        {
            var allFiles = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(file => extensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                .ToList();

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

                DetermineFileType(projectFile, structure);
                structure.Files.Add(projectFile);
            }
        }

        private void DetermineFileType(ProjectFile file, ProjectStructure structure)
        {
            // Определяем контроллеры
            if (file.Name.EndsWith("controller.cs", StringComparison.OrdinalIgnoreCase) ||
                (file.Directory?.Contains("controllers", StringComparison.OrdinalIgnoreCase) == true &&
                 file.Extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)))
            {
                file.Type = FileType.Controller;
                structure.Controllers.Add(file);
            }
            // Определяем страницы Razor
            else if (file.Extension.Equals(".razor", StringComparison.OrdinalIgnoreCase) ||
                     file.Extension.Equals(".cshtml", StringComparison.OrdinalIgnoreCase) ||
                     (file.Directory?.Contains("pages", StringComparison.OrdinalIgnoreCase) == true))
            {
                file.Type = FileType.Page;
                structure.Pages.Add(file);
            }
            // Определяем контексты БД
            else if (file.Name.Contains("context", StringComparison.OrdinalIgnoreCase) &&
                     file.Extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
            {
                file.Type = FileType.DbContext;
                structure.DbContexts.Add(file);
            }
            // Определяем файлы миграций
            else if (file.Directory?.Contains("migrations", StringComparison.OrdinalIgnoreCase) == true &&
                     file.Extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
            {
                file.Type = FileType.Migration;
                structure.Migrations.Add(file);
            }
            // Определяем конфигурационные файлы
            else if (file.Name.Contains("appsettings", StringComparison.OrdinalIgnoreCase) ||
                     file.Name.Equals("program.cs", StringComparison.OrdinalIgnoreCase) ||
                     file.Name.Equals("startup.cs", StringComparison.OrdinalIgnoreCase) ||
                     file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                     file.Extension.Equals(".config", StringComparison.OrdinalIgnoreCase))
            {
                file.Type = FileType.Config;
                structure.ConfigFiles.Add(file);
            }
        }
    }
}