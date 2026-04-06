using MPMS.Models;

namespace MPMS.Services;

public interface IApiService
{
    bool IsOnline { get; }

    /// <summary>Fast connectivity probe — updates IsOnline with a short timeout.</summary>
    Task ProbeAsync();

    // Auth
    Task<LoginResult> LoginAsync(string username, string password);
    Task<List<RoleDto>?> GetRolesAsync();

    // Projects
    Task<List<ProjectListResponse>?> GetProjectsAsync(string? status = null, string? search = null);
    Task<ProjectResponse?> GetProjectAsync(Guid id);
    Task<ProjectResponse?> CreateProjectAsync(CreateProjectRequest request);
    Task<ProjectResponse?> UpdateProjectAsync(Guid id, UpdateProjectRequest request);
    Task<bool> DeleteProjectAsync(Guid id);

    // Tasks
    Task<List<TaskListResponse>?> GetTasksAsync(Guid? projectId = null, string? status = null,
        string? priority = null, Guid? assignedUserId = null, string? search = null);
    Task<TaskResponse?> GetTaskAsync(Guid id);
    Task<TaskResponse?> CreateTaskAsync(CreateTaskRequest request);
    Task<TaskResponse?> UpdateTaskAsync(Guid id, UpdateTaskRequest request);
    Task<bool> DeleteTaskAsync(Guid id);

    // Stages
    Task<StageResponse?> GetStageAsync(Guid id);
    Task<StageResponse?> CreateStageAsync(CreateStageRequest request);
    Task<StageResponse?> UpdateStageAsync(Guid id, UpdateStageRequest request);
    Task<bool> DeleteStageAsync(Guid id);
    Task<StageMaterialResponse?> AddStageMaterialAsync(Guid stageId, AddStageMaterialRequest request);
    Task<bool> RemoveStageMaterialAsync(Guid stageId, Guid stageMaterialId);

    // Materials & inventory
    Task<List<MaterialResponse>?> GetMaterialsAsync(string? search = null);
    Task<MaterialResponse?> CreateMaterialAsync(CreateMaterialRequest request);
    Task<MaterialResponse?> UpdateMaterialAsync(Guid id, UpdateMaterialRequest request);
    Task<bool> DeleteMaterialAsync(Guid id);

    Task<List<MaterialCategoryResponse>?> GetMaterialCategoriesAsync();
    Task<List<EquipmentCategoryResponse>?> GetEquipmentCategoriesAsync();
    Task<MaterialCategoryResponse?> CreateMaterialCategoryAsync(CreateMaterialCategoryRequest request);
    Task<EquipmentCategoryResponse?> CreateEquipmentCategoryAsync(CreateEquipmentCategoryRequest request);
    Task<List<MaterialStockMovementResponse>?> GetAllMaterialStockMovementsAsync();
    Task<MaterialStockMovementResponse?> RecordMaterialStockMovementAsync(Guid materialId, RecordMaterialStockRequest request);
    Task<List<EquipmentResponse>?> GetAllEquipmentAsync();
    Task<EquipmentResponse?> CreateEquipmentAsync(CreateEquipmentRequest request);
    Task<EquipmentResponse?> UpdateEquipmentAsync(Guid id, UpdateEquipmentRequest request);
    Task<bool> DeleteEquipmentAsync(Guid id);
    Task<EquipmentHistoryEntryResponse?> RecordEquipmentEventAsync(Guid equipmentId, RecordEquipmentEventRequest request);
    Task<List<EquipmentHistoryEntryResponse>?> GetAllEquipmentHistoryAsync();

    // Files
    Task<List<FileDto>?> GetFilesAsync(Guid? projectId = null, Guid? taskId = null, Guid? stageId = null);
    Task<bool> DeleteFileAsync(Guid id);

    // Users
    Task<List<UserResponse>?> GetUsersAsync(string? search = null);
    Task<UserResponse?> CreateUserAsync(CreateUserRequest request);
    Task<UserResponse?> UpdateUserAsync(Guid id, UpdateUserRequest request);
    Task<bool> DeleteUserAsync(Guid id);
    Task<bool> UploadUserAvatarAsync(Guid userId, byte[] avatarData);
}
