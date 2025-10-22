using CodeAnalyzerLibrary;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CodeAnalyzerAPI.Services
{
    public interface ICustomCriteriaService
    {
        Task<List<AnalysisCriteria>> GetUserCriteriaAsync(string userId);
        Task<AnalysisCriteria> AddCustomCriteriaAsync(string userId, CustomCriteriaRequest request);
        Task<bool> DeleteCustomCriteriaAsync(string userId, string criteriaId);
        Task<AnalysisCriteria> UpdateCustomCriteriaAsync(string userId, string criteriaId, CustomCriteriaRequest request);
    }

    public class CustomCriteriaService : ICustomCriteriaService
    {
        private readonly ILogger<CustomCriteriaService> _logger;
        private const string StorageFilePath = "custom_criteria.json";
        private readonly Dictionary<string, UserCriteria> _userCriteria = new Dictionary<string, UserCriteria>();

        public CustomCriteriaService(ILogger<CustomCriteriaService> logger)
        {
            _logger = logger;
            LoadCriteriaFromStorage();
        }

        public async Task<List<AnalysisCriteria>> GetUserCriteriaAsync(string userId)
        {
            if (_userCriteria.TryGetValue(userId, out var userCriteria))
            {
                return userCriteria.Criteria;
            }
            return new List<AnalysisCriteria>();
        }

        public async Task<AnalysisCriteria> AddCustomCriteriaAsync(string userId, CustomCriteriaRequest request)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("User ID is required");

            if (string.IsNullOrWhiteSpace(request.Name))
                throw new ArgumentException("Criteria name is required");

            var criteria = new AnalysisCriteria
            {
                Id = Guid.NewGuid().ToString(),
                Name = request.Name,
                Description = request.Description,
                Type = request.Type,
                Rules = request.Rules,
                UserId = userId,
                IsCustom = true,
                CreatedAt = DateTime.UtcNow
            };

            if (!_userCriteria.ContainsKey(userId))
            {
                _userCriteria[userId] = new UserCriteria { UserId = userId };
            }

            _userCriteria[userId].Criteria.Add(criteria);
            await SaveCriteriaToStorage();

            _logger.LogInformation("Added custom criteria '{Name}' for user {UserId}", criteria.Name, userId);
            return criteria;
        }

        public async Task<bool> DeleteCustomCriteriaAsync(string userId, string criteriaId)
        {
            if (_userCriteria.TryGetValue(userId, out var userCriteria))
            {
                var criteriaToRemove = userCriteria.Criteria.FirstOrDefault(c => c.Id == criteriaId);
                if (criteriaToRemove != null)
                {
                    userCriteria.Criteria.Remove(criteriaToRemove);
                    await SaveCriteriaToStorage();
                    _logger.LogInformation("Deleted custom criteria '{Name}' for user {UserId}", criteriaToRemove.Name, userId);
                    return true;
                }
            }
            return false;
        }

        public async Task<AnalysisCriteria> UpdateCustomCriteriaAsync(string userId, string criteriaId, CustomCriteriaRequest request)
        {
            if (_userCriteria.TryGetValue(userId, out var userCriteria))
            {
                var existingCriteria = userCriteria.Criteria.FirstOrDefault(c => c.Id == criteriaId);
                if (existingCriteria != null)
                {
                    existingCriteria.Name = request.Name;
                    existingCriteria.Description = request.Description;
                    existingCriteria.Type = request.Type;
                    existingCriteria.Rules = request.Rules;

                    await SaveCriteriaToStorage();
                    _logger.LogInformation("Updated custom criteria '{Name}' for user {UserId}", existingCriteria.Name, userId);
                    return existingCriteria;
                }
            }
            throw new KeyNotFoundException($"Criteria with ID {criteriaId} not found for user {userId}");
        }

        private void LoadCriteriaFromStorage()
        {
            try
            {
                if (File.Exists(StorageFilePath))
                {
                    var json = File.ReadAllText(StorageFilePath);
                    var userCriteriaList = JsonSerializer.Deserialize<List<UserCriteria>>(json);
                    if (userCriteriaList != null)
                    {
                        _userCriteria.Clear();
                        foreach (var userCriteria in userCriteriaList)
                        {
                            _userCriteria[userCriteria.UserId] = userCriteria;
                        }
                    }
                    _logger.LogInformation("Loaded {Count} users' custom criteria from storage", _userCriteria.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading custom criteria from storage");
            }
        }

        private async Task SaveCriteriaToStorage()
        {
            try
            {
                var userCriteriaList = _userCriteria.Values.ToList();
                var json = JsonSerializer.Serialize(userCriteriaList, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(StorageFilePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving custom criteria to storage");
            }
        }
    }
}