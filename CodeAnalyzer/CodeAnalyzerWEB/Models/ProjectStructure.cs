using System.Collections.Generic;

namespace CodeAnalyzerWEB.Models
{
    public class ProjectStructure
    {
        public string ProjectPath { get; set; } = string.Empty;
        public List<ProjectFile> Files { get; set; } = new List<ProjectFile>();
        public List<ProjectFile> Controllers { get; set; } = new List<ProjectFile>();
        public List<ProjectFile> Pages { get; set; } = new List<ProjectFile>();
        public List<ProjectFile> Models { get; set; } = new List<ProjectFile>();
        public List<ProjectFile> DbContexts { get; set; } = new List<ProjectFile>();
        public List<ProjectFile> Migrations { get; set; } = new List<ProjectFile>();
        public List<ProjectFile> ConfigFiles { get; set; } = new List<ProjectFile>();
        public List<ProjectFile> Services { get; set; } = new List<ProjectFile>();
        public List<ProjectFile> ProgramFiles { get; set; } = new List<ProjectFile>();
        public string Error { get; set; } = string.Empty;
        public int TotalFiles => Files.Count;
        public int TotalControllers => Controllers.Count;
        public int TotalPages => Pages.Count;
        public bool HasDbContext => DbContexts.Count > 0;
        public bool HasMigrations => Migrations.Count > 0;
        public bool HasDatabaseConnection => DatabaseConnectionStrings.Count > 0;
        public List<string> DatabaseConnectionStrings { get; set; } = new List<string>();
        public List<string> MigrationCommands { get; set; } = new List<string>();
        public bool OllamaAvailable { get; set; }
        public string OllamaStatus { get; set; } = "Не проверено";

        // Новые методы для улучшенной фильтрации
        public int GetControllersExcludingBase()
        {
            var excludedKeywords = new[] { "base" };
            return Controllers.Count(c =>
                !excludedKeywords.Any(keyword =>
                    c.Name.Contains(keyword, System.StringComparison.OrdinalIgnoreCase)));
        }

        public int GetControllersExcludingBaseAbstract()
        {
            var excludedKeywords = new[] { "base", "abstract", "generic" };
            return Controllers.Count(c =>
                !excludedKeywords.Any(keyword =>
                    c.Name.Contains(keyword, System.StringComparison.OrdinalIgnoreCase)));
        }

        public int GetControllersExcludingSpecific()
        {
            var excludedNames = new[] { "BaseController" };
            return Controllers.Count(c =>
                !excludedNames.Any(name =>
                    c.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase)));
        }
    }

    public class ProjectFile
    {
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Directory { get; set; } = string.Empty;
        public FileType Type { get; set; }
        public string Content { get; set; } = string.Empty;
        public List<string> FoundPatterns { get; set; } = new List<string>();
        public double Confidence { get; set; } = 1.0;
    }

    public enum FileType
    {
        Unknown,
        Controller,
        Page,
        Model,
        DbContext,
        Migration,
        Config,
        Service,
        Program,
        Entity,
        BaseController
    }
}