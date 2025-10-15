using System.ComponentModel.DataAnnotations;

namespace CodeAnalyzerLibrary
{
    public class CriteriaTemplate
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required(ErrorMessage = "Название обязательно")]
        [StringLength(100, ErrorMessage = "Название не должно превышать 100 символов")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Описание обязательно")]
        [StringLength(500, ErrorMessage = "Описание не должно превышать 500 символов")]
        public string Description { get; set; } = string.Empty;

        public CriteriaType Type { get; set; } = CriteriaType.Structural;

        [Required(ErrorMessage = "Хотя бы одно правило должно быть задано")]
        [MinLength(1, ErrorMessage = "Добавьте хотя бы одно правило")]
        public List<CriteriaRule> Rules { get; set; } = new List<CriteriaRule>();

        public string Category { get; set; } = "Общие";
        public int Priority { get; set; } = 1;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; } = "system";
    }

    public class CriteriaCategory
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Count { get; set; }
        public List<CriteriaTemplate> Templates { get; set; } = new List<CriteriaTemplate>();
    }

    public class CriteriaManagementResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public CriteriaTemplate Template { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class CriteriaListResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<CriteriaTemplate> Templates { get; set; } = new List<CriteriaTemplate>();
        public List<CriteriaCategory> Categories { get; set; } = new List<CriteriaCategory>();
        public int TotalCount { get; set; }
    }
}