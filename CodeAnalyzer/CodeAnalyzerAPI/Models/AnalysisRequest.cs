// CodeAnalyzerAPI/Models/AnalysisRequest.cs
using System;
using System.Collections.Generic;

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
        Structural,
        FullContent
    }

    public class AnalysisResponse
    {
        public bool Success { get; set; }
        public ProjectStructure Structure { get; set; } = new ProjectStructure();
        public List<CriteriaCheckResult> Results { get; set; } = new List<CriteriaCheckResult>();
        public string Error { get; set; } = string.Empty;
        public TimeSpan AnalysisTime { get; set; }
    }

    public class StructureAnalysisRequest
    {
        public string FolderPath { get; set; } = string.Empty;
        public List<string> Extensions { get; set; } = new List<string> { ".cs", ".razor", ".cshtml", ".json" };
    }

    public class StructureAnalysisResponse
    {
        public bool Success { get; set; }
        public ProjectStructure Structure { get; set; } = new ProjectStructure();
        public StructureSummary Summary { get; set; } = new StructureSummary();
    }

    public class StructureSummary
    {
        public int TotalFiles { get; set; }
        public int Controllers { get; set; }
        public int Pages { get; set; }
        public int Migrations { get; set; }
        public int DbContexts { get; set; }
    }
}