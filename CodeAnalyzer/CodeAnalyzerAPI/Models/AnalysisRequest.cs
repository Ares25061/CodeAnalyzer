namespace CodeAnalyzerAPI.Models
{
    public class AnalysisRequest
    {
        public string FolderPath { get; set; } = string.Empty;
        public List<AnalysisCriteria> Criteria { get; set; } = new List<AnalysisCriteria>();
        public List<string> Extensions { get; set; } = new List<string> { ".cs", ".razor", ".cshtml", ".json", ".config", ".xml" };
        public AnalysisMode Mode { get; set; } = AnalysisMode.Structural;
    }

    public enum AnalysisMode
    {
        Structural,    // Только структура
        FullContent    // Полный анализ
    }

    public class AnalysisResponse
    {
        public bool Success { get; set; }
        public ProjectStructure Structure { get; set; } = new ProjectStructure();
        public List<CriteriaCheckResult> Results { get; set; } = new List<CriteriaCheckResult>();
        public string Error { get; set; } = string.Empty;
        public TimeSpan AnalysisTime { get; set; }
    }
}