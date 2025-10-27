using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeAnalyzerLibrary
{
    public class AnalysisMessage
    {
        public string Text { get; set; } = string.Empty;
        public MessageType Type { get; set; }
        public string FolderPath { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public List<string> Criteria { get; set; } = new();
        public bool UseOllama { get; set; }
        public string CustomPrompt { get; set; } = string.Empty;
        public StructureSummary StructureSummary { get; set; }
        public List<CriteriaCheckResult> CriteriaResults { get; set; } = new();
        public string AiAnalysis { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public string AnalysisType { get; set; } = "project";
        public string ExpectedFont { get; set; } = string.Empty;
        public string ExpectedFontSize { get; set; } = string.Empty;
        public string ExpectedLineSpacing { get; set; } = string.Empty;
        public WordAnalysisResult WordAnalysisResult { get; set; }
    }

    public class WordAnalysisResult
    {
        public string FileName { get; set; } = string.Empty;
        public int ParagraphsCount { get; set; }
        public int PagesCount { get; set; }
        public int SectionsCount { get; set; }
        public List<FormattingCheckResult> FormattingChecks { get; set; } = new();
        public List<string> StructureIssues { get; set; } = new();
        public int PassedChecks { get; set; }
        public int FailedChecks { get; set; }
        public int TotalChecks { get; set; }
    }

    public class CriteriaOption
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool Selected { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    public enum MessageType
    {
        User,
        Bot
    }
    public class ApiAnalysisResponse
    {
        public bool Success { get; set; }
        public StructureSummary Structure { get; set; }
        public List<CriteriaCheckResult> CriteriaResults { get; set; } = new();
        public string AiAnalysis { get; set; } = string.Empty;
        public AnalysisSummary Summary { get; set; }
        public string Error { get; set; } = string.Empty;
    }

    public class ApiStructureAnalysisResponse
    {
        public bool Success { get; set; }
        public ProjectStructure Structure { get; set; }
        public StructureSummary Summary { get; set; }
        public string Error { get; set; } = string.Empty;
    }
    public class ConnectionCheckResponse
    {
        public bool Success { get; set; }
        public bool Connected { get; set; }
        public string? Response { get; set; }
        public string? Message { get; set; }
    }
}
