namespace CodeAnalyzerAPI.Models
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
        public string Error { get; set; } = string.Empty;
        public int TotalFiles => Files.Count;
        public int TotalControllers => Controllers.Count;
        public int TotalPages => Pages.Count;
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
        Component
    }
}