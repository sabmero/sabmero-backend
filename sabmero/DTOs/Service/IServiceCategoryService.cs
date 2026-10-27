using sabmero.DTOs.Service;

namespace sabmero.Services;

public interface IServiceCategoryService
{
    // Main categories with their sub-categories. includeInactive is for admins only.
    Task<List<ServiceCategoryDto>> GetTreeAsync(bool includeInactive);

    Task<(bool Success, string Message, ServiceCategoryDto? Data)> CreateAsync(CreateServiceCategoryDto dto);

    Task<(bool Success, string Message)> UpdateAsync(int id, UpdateServiceCategoryDto dto);

    Task<(bool Success, string Message)> DeleteAsync(int id);
}
