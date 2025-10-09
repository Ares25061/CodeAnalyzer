using System;
using System.Collections.Generic;

namespace CodeAnalyzerWEB.Models
{
    public class AnalysisRequest
    {
        public string FolderPath { get; set; } = string.Empty;
        public List<AnalysisCriteria> Criteria { get; set; } = new List<AnalysisCriteria>();
        public List<string> Extensions { get; set; } = new List<string> { ".cs", ".razor", ".cshtml", ".json", ".config", ".xml" };
        public AnalysisMode Mode { get; set; } = AnalysisMode.Structural;
        public bool UseOllama { get; set; } = true;
    }

    public class AnalysisResponse
    {
        public bool Success { get; set; }
        public ProjectStructure Structure { get; set; } = new ProjectStructure();
        public List<CriteriaCheckResult> Results { get; set; } = new List<CriteriaCheckResult>();
        public string Error { get; set; } = string.Empty;
        public TimeSpan AnalysisTime { get; set; }
        public string OllamaStatus { get; set; } = string.Empty;
    }

    public class StructureAnalysisRequest
    {
        public string FolderPath { get; set; } = string.Empty;
        public List<string> Extensions { get; set; } = new List<string> { ".cs", ".razor", ".cshtml", ".json", ".config" };
        public bool CheckOllama { get; set; } = false;
    }

    public class StructureAnalysisResponse
    {
        public bool Success { get; set; }
        public ProjectStructure Structure { get; set; } = new ProjectStructure();
        public StructureSummary Summary { get; set; } = new StructureSummary();
        public string Error { get; set; } = string.Empty;
        public string OllamaStatus { get; set; } = string.Empty;
    }

    public class StructureSummary
    {
        public int TotalFiles { get; set; }
        public int Controllers { get; set; }
        public int Pages { get; set; }
        public int Migrations { get; set; }
        public int DbContexts { get; set; }
        public int Services { get; set; }
        public bool HasDatabaseConnection { get; set; }
        public bool DatabaseConnectionFound { get; set; }
        public int DatabaseConnectionsCount { get; set; }
        public int MigrationCommandsCount { get; set; }
        public bool OllamaAvailable { get; set; }
    }

    public enum AnalysisMode
    {
        Structural,
        FullContent
    }
}