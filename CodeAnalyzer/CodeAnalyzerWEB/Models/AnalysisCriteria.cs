using System;
using System.Collections.Generic;

namespace CodeAnalyzerWEB.Models
{
    public class AnalysisCriteria
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public CriteriaType Type { get; set; }
        public List<CriteriaRule> Rules { get; set; } = new List<CriteriaRule>();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class CriteriaRule
    {
        public string Property { get; set; } = string.Empty;
        public string Operator { get; set; } = "exists";
        public object Value { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public class CriteriaCheckResult
    {
        public string CriteriaId { get; set; } = string.Empty;
        public string CriteriaName { get; set; } = string.Empty;
        public bool Passed { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<string> Evidence { get; set; } = new List<string>();
    }

    public enum CriteriaType
    {
        Structural,
        FullContent
    }
}