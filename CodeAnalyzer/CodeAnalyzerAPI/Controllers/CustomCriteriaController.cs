using Microsoft.AspNetCore.Mvc;
using CodeAnalyzerLibrary;
using CodeAnalyzerAPI.Services;
using Microsoft.Extensions.Logging;

namespace CodeAnalyzerAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CustomCriteriaController : ControllerBase
    {
        private readonly ICustomCriteriaService _criteriaService;
        private readonly ILogger<CustomCriteriaController> _logger;

        public CustomCriteriaController(ICustomCriteriaService criteriaService, ILogger<CustomCriteriaController> logger)
        {
            _criteriaService = criteriaService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetUserCriteria()
        {
            var userId = GetUserId();
            var criteria = await _criteriaService.GetUserCriteriaAsync(userId);
            return Ok(new { success = true, criteria });
        }

        [HttpPost]
        public async Task<IActionResult> AddCriteria([FromBody] CustomCriteriaRequest request)
        {
            try
            {
                var userId = GetUserId();
                var criteria = await _criteriaService.AddCustomCriteriaAsync(userId, request);
                return Ok(new { success = true, criteria });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding custom criteria");
                return BadRequest(new { success = false, error = ex.Message });
            }
        }

        [HttpPut("{criteriaId}")]
        public async Task<IActionResult> UpdateCriteria(string criteriaId, [FromBody] CustomCriteriaRequest request)
        {
            try
            {
                var userId = GetUserId();
                var criteria = await _criteriaService.UpdateCustomCriteriaAsync(userId, criteriaId, request);
                return Ok(new { success = true, criteria });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating custom criteria");
                return BadRequest(new { success = false, error = ex.Message });
            }
        }

        [HttpDelete("{criteriaId}")]
        public async Task<IActionResult> DeleteCriteria(string criteriaId)
        {
            try
            {
                var userId = GetUserId();
                var deleted = await _criteriaService.DeleteCustomCriteriaAsync(userId, criteriaId);
                if (deleted)
                {
                    return Ok(new { success = true });
                }
                return NotFound(new { success = false, error = "Criteria not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting custom criteria");
                return BadRequest(new { success = false, error = ex.Message });
            }
        }

        private string GetUserId()
        {
            return User.Identity?.Name ?? "anonymous";
        }
    }
}