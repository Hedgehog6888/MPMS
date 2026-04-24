using AutoMapper;
using MPMS.API.DTOs;
using MPMS.API.Models;

namespace MPMS.API.Mappings;

public class ApiMappingProfile : Profile
{
    public ApiMappingProfile()
    {
        CreateMap<CreateProjectRequest, Project>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.Status, opt => opt.MapFrom(_ => ProjectStatus.Planning))
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.IsMarkedForDeletion, opt => opt.Ignore())
            .ForMember(dest => dest.IsArchived, opt => opt.Ignore())
            .ForMember(dest => dest.Manager, opt => opt.Ignore())
            .ForMember(dest => dest.Tasks, opt => opt.Ignore())
            .ForMember(dest => dest.Members, opt => opt.Ignore())
            .ForMember(dest => dest.Files, opt => opt.Ignore());

        CreateMap<UpdateProjectRequest, Project>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.Manager, opt => opt.Ignore())
            .ForMember(dest => dest.Tasks, opt => opt.Ignore())
            .ForMember(dest => dest.Members, opt => opt.Ignore())
            .ForMember(dest => dest.Files, opt => opt.Ignore());

        CreateMap<Project, ProjectListResponse>()
            .ForCtorParam(nameof(ProjectListResponse.Status), opt => opt.MapFrom(src => src.Status.ToString()))
            .ForCtorParam(nameof(ProjectListResponse.ManagerName), opt => opt.MapFrom(src => src.Manager.Name));

        CreateMap<Project, ProjectResponse>()
            .ForCtorParam(nameof(ProjectResponse.Status), opt => opt.MapFrom(src => src.Status.ToString()))
            .ForCtorParam(nameof(ProjectResponse.ManagerName), opt => opt.MapFrom(src => src.Manager.Name))
            .ForCtorParam(nameof(ProjectResponse.TotalTasks), opt => opt.MapFrom(src => src.Tasks.Count))
            .ForCtorParam(nameof(ProjectResponse.CompletedTasks), opt => opt.MapFrom(src => src.Tasks.Count(t => t.Status == Models.TaskStatus.Completed)))
            .ForCtorParam(nameof(ProjectResponse.InProgressTasks), opt => opt.MapFrom(src => src.Tasks.Count(t => t.Status == Models.TaskStatus.InProgress)))
            .ForCtorParam(nameof(ProjectResponse.OverdueTasks), opt => opt.MapFrom(src =>
                src.Tasks.Count(t => t.DueDate < DateOnly.FromDateTime(DateTime.UtcNow)
                    && t.Status != Models.TaskStatus.Completed)));

        CreateMap<User, UserResponse>()
            .ForCtorParam(nameof(UserResponse.Role), opt => opt.MapFrom(src => src.Role.Name));

        CreateMap<CreateUserRequest, User>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.PasswordHash, opt => opt.Ignore())
            .ForMember(dest => dest.Role, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.IsBlocked, opt => opt.Ignore())
            .ForMember(dest => dest.BlockedAt, opt => opt.Ignore())
            .ForMember(dest => dest.BlockedReason, opt => opt.Ignore())
            .ForMember(dest => dest.ManagedProjects, opt => opt.Ignore())
            .ForMember(dest => dest.ProjectMemberships, opt => opt.Ignore())
            .ForMember(dest => dest.AssignedTasks, opt => opt.Ignore())
            .ForMember(dest => dest.AssignedStages, opt => opt.Ignore())
            .ForMember(dest => dest.UploadedFiles, opt => opt.Ignore())
            .ForMember(dest => dest.ActivityLogs, opt => opt.Ignore());

        CreateMap<UpdateUserRequest, User>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.PasswordHash, opt => opt.Ignore())
            .ForMember(dest => dest.Role, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.AvatarData, opt => opt.Ignore())
            .ForMember(dest => dest.IsBlocked, opt => opt.Ignore())
            .ForMember(dest => dest.BlockedAt, opt => opt.Ignore())
            .ForMember(dest => dest.BlockedReason, opt => opt.Ignore())
            .ForMember(dest => dest.ManagedProjects, opt => opt.Ignore())
            .ForMember(dest => dest.ProjectMemberships, opt => opt.Ignore())
            .ForMember(dest => dest.AssignedTasks, opt => opt.Ignore())
            .ForMember(dest => dest.AssignedStages, opt => opt.Ignore())
            .ForMember(dest => dest.UploadedFiles, opt => opt.Ignore())
            .ForMember(dest => dest.ActivityLogs, opt => opt.Ignore());

        CreateMap<ActivityLog, ActivityLogResponse>()
            .ForCtorParam(nameof(ActivityLogResponse.UserName), opt => opt.MapFrom(src => src.User.FirstName + " " + src.User.LastName))
            .ForCtorParam(nameof(ActivityLogResponse.ActionType), opt => opt.MapFrom(src => src.ActionType.ToString()))
            .ForCtorParam(nameof(ActivityLogResponse.EntityType), opt => opt.MapFrom(src => src.EntityType.ToString()));

        CreateMap<Material, MaterialResponse>()
            .ForCtorParam(nameof(MaterialResponse.CategoryName), opt => opt.MapFrom(src => src.Category != null ? src.Category.Name : null));

        CreateMap<CreateMaterialRequest, Material>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.Quantity, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.IsWrittenOff, opt => opt.Ignore())
            .ForMember(dest => dest.WrittenOffAt, opt => opt.Ignore())
            .ForMember(dest => dest.WrittenOffComment, opt => opt.Ignore())
            .ForMember(dest => dest.IsArchived, opt => opt.Ignore())
            .ForMember(dest => dest.Category, opt => opt.Ignore())
            .ForMember(dest => dest.StockMovements, opt => opt.Ignore())
            .ForMember(dest => dest.StageMaterials, opt => opt.Ignore());

        CreateMap<UpdateMaterialRequest, Material>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.Quantity, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.Category, opt => opt.Ignore())
            .ForMember(dest => dest.StockMovements, opt => opt.Ignore())
            .ForMember(dest => dest.StageMaterials, opt => opt.Ignore());

        CreateMap<MaterialCategory, MaterialCategoryResponse>();
        CreateMap<CreateMaterialCategoryRequest, MaterialCategory>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.Materials, opt => opt.Ignore());

        CreateMap<EquipmentCategory, EquipmentCategoryResponse>();
        CreateMap<CreateEquipmentCategoryRequest, EquipmentCategory>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.EquipmentItems, opt => opt.Ignore());

        CreateMap<FileAttachment, FileResponse>()
            .ForCtorParam(nameof(FileResponse.FileType), opt => opt.MapFrom(src => src.FileType ?? string.Empty))
            .ForCtorParam(nameof(FileResponse.UploadedByName), opt => opt.MapFrom(src => src.UploadedBy.Name))
            .ForCtorParam(nameof(FileResponse.ProjectName), opt => opt.MapFrom(src => src.Project != null ? src.Project.Name : null))
            .ForCtorParam(nameof(FileResponse.StageName), opt => opt.MapFrom(src => src.Stage != null ? src.Stage.Name : null));

        CreateMap<RegisterRequest, User>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.PasswordHash, opt => opt.Ignore())
            .ForMember(dest => dest.Role, opt => opt.Ignore())
            .ForMember(dest => dest.SubRole, opt => opt.Ignore())
            .ForMember(dest => dest.AdditionalSubRoles, opt => opt.Ignore())
            .ForMember(dest => dest.BirthDate, opt => opt.Ignore())
            .ForMember(dest => dest.HomeAddress, opt => opt.Ignore())
            .ForMember(dest => dest.AvatarData, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.IsBlocked, opt => opt.Ignore())
            .ForMember(dest => dest.BlockedAt, opt => opt.Ignore())
            .ForMember(dest => dest.BlockedReason, opt => opt.Ignore())
            .ForMember(dest => dest.ManagedProjects, opt => opt.Ignore())
            .ForMember(dest => dest.ProjectMemberships, opt => opt.Ignore())
            .ForMember(dest => dest.AssignedTasks, opt => opt.Ignore())
            .ForMember(dest => dest.AssignedStages, opt => opt.Ignore())
            .ForMember(dest => dest.UploadedFiles, opt => opt.Ignore())
            .ForMember(dest => dest.ActivityLogs, opt => opt.Ignore());

        CreateMap<Equipment, EquipmentResponse>()
            .ForCtorParam(nameof(EquipmentResponse.CategoryName), opt => opt.MapFrom(src => src.Category != null ? src.Category.Name : null))
            .ForCtorParam(nameof(EquipmentResponse.Status), opt => opt.MapFrom(src => src.Status.ToString()))
            .ForCtorParam(nameof(EquipmentResponse.Condition), opt => opt.MapFrom(src => src.Condition.ToString()));

        CreateMap<CreateEquipmentRequest, Equipment>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.Status, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CheckedOutProjectId, opt => opt.Ignore())
            .ForMember(dest => dest.CheckedOutProject, opt => opt.Ignore())
            .ForMember(dest => dest.CheckedOutTaskId, opt => opt.Ignore())
            .ForMember(dest => dest.CheckedOutTask, opt => opt.Ignore())
            .ForMember(dest => dest.IsWrittenOff, opt => opt.Ignore())
            .ForMember(dest => dest.WrittenOffAt, opt => opt.Ignore())
            .ForMember(dest => dest.WrittenOffComment, opt => opt.Ignore())
            .ForMember(dest => dest.IsArchived, opt => opt.Ignore())
            .ForMember(dest => dest.Category, opt => opt.Ignore())
            .ForMember(dest => dest.History, opt => opt.Ignore());

        CreateMap<UpdateEquipmentRequest, Equipment>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.Status, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CheckedOutProjectId, opt => opt.Ignore())
            .ForMember(dest => dest.CheckedOutProject, opt => opt.Ignore())
            .ForMember(dest => dest.CheckedOutTaskId, opt => opt.Ignore())
            .ForMember(dest => dest.CheckedOutTask, opt => opt.Ignore())
            .ForMember(dest => dest.Category, opt => opt.Ignore())
            .ForMember(dest => dest.History, opt => opt.Ignore());

        CreateMap<EquipmentHistoryEntry, EquipmentHistoryEntryResponse>()
            .ForCtorParam(nameof(EquipmentHistoryEntryResponse.EventType), opt => opt.MapFrom(src => src.EventType.ToString()))
            .ForCtorParam(nameof(EquipmentHistoryEntryResponse.PreviousStatus), opt => opt.MapFrom(src => src.PreviousStatus.HasValue ? src.PreviousStatus.Value.ToString() : null))
            .ForCtorParam(nameof(EquipmentHistoryEntryResponse.NewStatus), opt => opt.MapFrom(src => src.NewStatus.HasValue ? src.NewStatus.Value.ToString() : null));

        CreateMap<MaterialStockMovement, MaterialStockMovementResponse>()
            .ForCtorParam(nameof(MaterialStockMovementResponse.OperationType), opt => opt.MapFrom(src => src.OperationType.ToString()));

        CreateMap<DiscussionMessage, DiscussionMessageResponse>();

        CreateMap<CreateSyncedActivityLogRequest, SyncedActivityLog>()
            .ForMember(dest => dest.User, opt => opt.Ignore());

        CreateMap<SyncedActivityLog, SyncedActivityLogResponse>();
    }
}
