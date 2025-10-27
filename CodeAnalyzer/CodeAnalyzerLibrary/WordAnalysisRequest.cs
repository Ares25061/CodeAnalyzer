using System;
using System.Collections.Generic;

namespace CodeAnalyzerLibrary
{
    public class WordAnalysisRequest
    {
        public string FilePath { get; set; } = string.Empty;
        public DocumentFormattingRules FormattingRules { get; set; } = new DocumentFormattingRules();
        public DocumentStructureRules StructureRules { get; set; } = new DocumentStructureRules();
        public bool UseOllama { get; set; } = true;
        public string CustomPrompt { get; set; } = string.Empty;
    }

    public class DocumentFormattingRules
    {
        public string ExpectedFont { get; set; } = "Times New Roman";
        public int ExpectedFontSize { get; set; } = 14;
        public double ExpectedLineSpacing { get; set; } = 1.5;
        public bool RequireParagraphIndent { get; set; } = true;
        public double ExpectedParagraphIndent { get; set; } = 1.25;
        public List<string> AllowedFonts { get; set; } = new List<string> { "Times New Roman", "Arial" };
    }

    public class DocumentStructureRules
    {
        public bool CheckStructure { get; set; } = true;
        public bool RequireExamples { get; set; } = true;
        public bool CheckSections { get; set; } = true;
        public List<string> RequiredSections { get; set; } = new List<string>();
        public int MinParagraphsPerSection { get; set; } = 3;
    }

    public class WordDocumentAnalysisResult
    {
        public string FilePath { get; set; } = string.Empty;
        public string ActualFont { get; set; } = string.Empty;
        public double ActualFontSize { get; set; }
        public double ActualLineSpacing { get; set; }
        public bool HasParagraphIndents { get; set; }
        public int PagesCount { get; set; }
        public int ParagraphsCount { get; set; }
        public int SectionsCount { get; set; }
        public List<FormattingCheckResult> FormattingChecks { get; set; } = new List<FormattingCheckResult>();
        public List<string> StructureIssues { get; set; } = new List<string>();
        public string Error { get; set; } = string.Empty;
    }

    public class FormattingCheckResult
    {
        public string CheckName { get; set; } = string.Empty;
        public bool Passed { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ExpectedValue { get; set; } = string.Empty;
        public string ActualValue { get; set; } = string.Empty;
    }

    public class WordAnalysisResponse
    {
        public bool Success { get; set; }
        public WordDocumentAnalysisResult AnalysisResult { get; set; } = new WordDocumentAnalysisResult();
        public string AiAnalysis { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }
}