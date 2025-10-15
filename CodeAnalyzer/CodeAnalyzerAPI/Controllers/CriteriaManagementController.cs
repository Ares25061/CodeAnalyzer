using Microsoft.AspNetCore.Mvc;
using CodeAnalyzerLibrary;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CodeAnalyzerAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CriteriaManagementController : ControllerBase
    {
        private readonly ILogger<CriteriaManagementController> _logger;
        private readonly string _storagePath;

        public CriteriaManagementController(ILogger<CriteriaManagementController> logger, IWebHostEnvironment env)
        {
            _logger = logger;
            _storagePath = Path.Combine(env.ContentRootPath, "Data", "CriteriaTemplates");

            Directory.CreateDirectory(_storagePath);
        }

        /// <summary>
        /// Получить все шаблоны критериев
        /// </summary>
        [HttpGet("templates")]
        public async Task<ActionResult<CriteriaListResponse>> GetAllTemplates()
        {
            try
            {
                var templates = await LoadAllTemplatesAsync();
                var categories = GroupTemplatesByCategory(templates);

                return Ok(new CriteriaListResponse
                {
                    Success = true,
                    Templates = templates,
                    Categories = categories,
                    TotalCount = templates.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении шаблонов критериев");
                return StatusCode(500, new CriteriaListResponse
                {
                    Success = false,
                    Message = $"Ошибка: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Получить шаблон по ID
        /// </summary>
        [HttpGet("templates/{id}")]
        public async Task<ActionResult<CriteriaManagementResponse>> GetTemplateById(string id)
        {
            try
            {
                var template = await LoadTemplateAsync(id);
                if (template == null)
                {
                    return NotFound(new CriteriaManagementResponse
                    {
                        Success = false,
                        Message = "Шаблон не найден"
                    });
                }

                return Ok(new CriteriaManagementResponse
                {
                    Success = true,
                    Template = template,
                    Message = "Шаблон найден"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении шаблона {TemplateId}", id);
                return StatusCode(500, new CriteriaManagementResponse
                {
                    Success = false,
                    Message = $"Ошибка: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Создать новый шаблон критерия
        /// </summary>
        [HttpPost("templates")]
        public async Task<ActionResult<CriteriaManagementResponse>> CreateTemplate([FromBody] CriteriaTemplate template)
        {
            try
            {
                // Валидация
                var validationResult = ValidateTemplate(template);
                if (!validationResult.Success)
                {
                    return BadRequest(validationResult);
                }

                // Генерируем новый ID если нужно
                if (string.IsNullOrEmpty(template.Id) || template.Id == Guid.Empty.ToString())
                {
                    template.Id = Guid.NewGuid().ToString();
                }

                template.CreatedAt = DateTime.UtcNow;
                template.UpdatedAt = DateTime.UtcNow;

                // Сохраняем
                await SaveTemplateAsync(template);

                _logger.LogInformation("Создан новый шаблон критерия: {TemplateName} ({TemplateId})",
                    template.Name, template.Id);

                return Ok(new CriteriaManagementResponse
                {
                    Success = true,
                    Template = template,
                    Message = "Шаблон успешно создан"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании шаблона");
                return StatusCode(500, new CriteriaManagementResponse
                {
                    Success = false,
                    Message = $"Ошибка: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Обновить существующий шаблон
        /// </summary>
        [HttpPut("templates/{id}")]
        public async Task<ActionResult<CriteriaManagementResponse>> UpdateTemplate(string id, [FromBody] CriteriaTemplate template)
        {
            try
            {
                // Проверяем существование
                var existingTemplate = await LoadTemplateAsync(id);
                if (existingTemplate == null)
                {
                    return NotFound(new CriteriaManagementResponse
                    {
                        Success = false,
                        Message = "Шаблон не найден"
                    });
                }

                // Валидация
                var validationResult = ValidateTemplate(template);
                if (!validationResult.Success)
                {
                    return BadRequest(validationResult);
                }

                // Обновляем поля
                template.Id = id; // Гарантируем что ID не изменится
                template.CreatedAt = existingTemplate.CreatedAt;
                template.UpdatedAt = DateTime.UtcNow;

                // Сохраняем
                await SaveTemplateAsync(template);

                _logger.LogInformation("Обновлен шаблон критерия: {TemplateName} ({TemplateId})",
                    template.Name, template.Id);

                return Ok(new CriteriaManagementResponse
                {
                    Success = true,
                    Template = template,
                    Message = "Шаблон успешно обновлен"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении шаблона {TemplateId}", id);
                return StatusCode(500, new CriteriaManagementResponse
                {
                    Success = false,
                    Message = $"Ошибка: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Удалить шаблон
        /// </summary>
        [HttpDelete("templates/{id}")]
        public async Task<ActionResult<CriteriaManagementResponse>> DeleteTemplate(string id)
        {
            try
            {
                var template = await LoadTemplateAsync(id);
                if (template == null)
                {
                    return NotFound(new CriteriaManagementResponse
                    {
                        Success = false,
                        Message = "Шаблон не найден"
                    });
                }

                var filePath = GetTemplateFilePath(id);
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }

                _logger.LogInformation("Удален шаблон критерия: {TemplateName} ({TemplateId})",
                    template.Name, template.Id);

                return Ok(new CriteriaManagementResponse
                {
                    Success = true,
                    Message = "Шаблон успешно удален"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при удалении шаблона {TemplateId}", id);
                return StatusCode(500, new CriteriaManagementResponse
                {
                    Success = false,
                    Message = $"Ошибка: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Дублировать шаблон
        /// </summary>
        [HttpPost("templates/{id}/duplicate")]
        public async Task<ActionResult<CriteriaManagementResponse>> DuplicateTemplate(string id)
        {
            try
            {
                var originalTemplate = await LoadTemplateAsync(id);
                if (originalTemplate == null)
                {
                    return NotFound(new CriteriaManagementResponse
                    {
                        Success = false,
                        Message = "Шаблон не найден"
                    });
                }

                var newTemplate = new CriteriaTemplate
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = $"{originalTemplate.Name} (Копия)",
                    Description = originalTemplate.Description,
                    Type = originalTemplate.Type,
                    Category = originalTemplate.Category,
                    Priority = originalTemplate.Priority,
                    IsActive = originalTemplate.IsActive,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    CreatedBy = "system"
                };

                // Копируем правила
                foreach (var rule in originalTemplate.Rules)
                {
                    newTemplate.Rules.Add(new CriteriaRule
                    {
                        Property = rule.Property,
                        Operator = rule.Operator,
                        Value = rule.Value,
                        ErrorMessage = rule.ErrorMessage
                    });
                }

                await SaveTemplateAsync(newTemplate);

                _logger.LogInformation("Дублирован шаблон критерия: {OriginalId} -> {NewId}", id, newTemplate.Id);

                return Ok(new CriteriaManagementResponse
                {
                    Success = true,
                    Template = newTemplate,
                    Message = "Шаблон успешно дублирован"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при дублировании шаблона {TemplateId}", id);
                return StatusCode(500, new CriteriaManagementResponse
                {
                    Success = false,
                    Message = $"Ошибка: {ex.Message}"
                });
            }
        }

        #region Вспомогательные методы

        private async Task<List<CriteriaTemplate>> LoadAllTemplatesAsync()
        {
            var templates = new List<CriteriaTemplate>();

            if (!Directory.Exists(_storagePath))
                return templates;

            foreach (var filePath in Directory.GetFiles(_storagePath, "*.json"))
            {
                try
                {
                    var json = await System.IO.File.ReadAllTextAsync(filePath);
                    var template = JsonSerializer.Deserialize<CriteriaTemplate>(json);
                    if (template != null)
                    {
                        templates.Add(template);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Ошибка при загрузке файла шаблона: {FilePath}", filePath);
                }
            }

            return templates.OrderBy(t => t.Priority).ThenBy(t => t.Name).ToList();
        }

        private async Task<CriteriaTemplate> LoadTemplateAsync(string id)
        {
            var filePath = GetTemplateFilePath(id);
            if (!System.IO.File.Exists(filePath))
                return null;

            try
            {
                var json = await System.IO.File.ReadAllTextAsync(filePath);
                return JsonSerializer.Deserialize<CriteriaTemplate>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при загрузке шаблона {TemplateId}", id);
                return null;
            }
        }

        private async Task SaveTemplateAsync(CriteriaTemplate template)
        {
            var filePath = GetTemplateFilePath(template.Id);
            var json = JsonSerializer.Serialize(template, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await System.IO.File.WriteAllTextAsync(filePath, json);
        }

        private string GetTemplateFilePath(string id)
        {
            return Path.Combine(_storagePath, $"{id}.json");
        }

        private List<CriteriaCategory> GroupTemplatesByCategory(List<CriteriaTemplate> templates)
        {
            return templates
                .GroupBy(t => t.Category)
                .Select(g => new CriteriaCategory
                {
                    Name = g.Key,
                    Description = $"Критерии категории {g.Key}",
                    Count = g.Count(),
                    Templates = g.ToList()
                })
                .OrderBy(c => c.Name)
                .ToList();
        }

        private CriteriaManagementResponse ValidateTemplate(CriteriaTemplate template)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(template.Name))
                errors.Add("Название критерия обязательно");

            if (string.IsNullOrWhiteSpace(template.Description))
                errors.Add("Описание критерия обязательно");

            if (template.Rules == null || !template.Rules.Any())
                errors.Add("Добавьте хотя бы одно правило");

            foreach (var rule in template.Rules)
            {
                if (string.IsNullOrWhiteSpace(rule.Property))
                    errors.Add("Свойство правила обязательно");

                if (string.IsNullOrWhiteSpace(rule.Operator))
                    errors.Add("Оператор правила обязателен");
            }

            return new CriteriaManagementResponse
            {
                Success = !errors.Any(),
                Message = errors.Any() ? "Обнаружены ошибки валидации" : "Валидация пройдена",
                Errors = errors
            };
        }

        #endregion
    }
}