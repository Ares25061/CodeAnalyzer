using System;
using System.Collections.Generic;

namespace CodeAnalyzerLibrary
{
    public class AnalysisCriteria
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public CriteriaType Type { get; set; }
        public List<CriteriaRule> Rules { get; set; } = new List<CriteriaRule>();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string UserId { get; set; } = string.Empty;
        public bool IsCustom { get; set; }
        public bool Selected { get; set; } = true;
    }

    public class CriteriaRule
    {
        public string Property { get; set; } = string.Empty;
        public string Operator { get; set; } = "exists";
        public string Value { get; set; } = string.Empty;
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

    public class CustomCriteriaRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public CriteriaType Type { get; set; }
        public List<CriteriaRule> Rules { get; set; } = new List<CriteriaRule>();
    }

    public class UserCriteria
    {
        public string UserId { get; set; } = string.Empty;
        public List<AnalysisCriteria> Criteria { get; set; } = new List<AnalysisCriteria>();
    }
    public class CustomCriteriaResponse
    {
        public bool Success { get; set; }
        public List<AnalysisCriteria> Criteria { get; set; } = new List<AnalysisCriteria>();
        public string Error { get; set; } = string.Empty;
    }
}