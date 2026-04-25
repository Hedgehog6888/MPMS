using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MPMS.Models;

namespace MPMS.Services;

public class ApiService : IApiService
{
    private readonly HttpClient _http;
    private readonly IAuthService _auth;

    /// <summary>Совпадает с AddJsonOptions в MPMS.API (camelCase) — иначе record/DTO могут не собраться из ответа.</summary>
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ApiService(HttpClient http, IAuthService auth)
    {
        _http = http;
        _auth = auth;
    }

    public bool IsOnline { get; private set; } = true;

    public string? LastUsersPullError { get; private set; }

    public void ClearLastUsersPullError() => LastUsersPullError = null;

    public async Task ProbeAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await _http.GetAsync(Api("auth/roles"),
                HttpCompletionOption.ResponseHeadersRead, cts.Token);
            IsOnline = true;
        }
        catch
        {
            IsOnline = false;
        }
    }

    public async Task<bool> VerifyAuthAsync()
    {
        if (string.IsNullOrWhiteSpace(_auth.Token)) return false;
        var response = await SendWithRetryAsync(() => _http.GetAsync(Api("auth/me")));
        return response?.IsSuccessStatusCode ?? false;
    }

    // ── Auth ──────────────────────────────────────────────────────────────────
    public async Task<LoginResult> LoginAsync(string username, string password)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                Api("auth/login"),
                new LoginRequest(username, password),
                JsonOpts);
            IsOnline = true;

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return LoginResult.WrongCredentials();

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                var msg = await TryReadErrorMessageAsync(response);
                return LoginResult.Blocked(string.IsNullOrWhiteSpace(msg)
                    ? "Учётная запись заблокирована"
                    : msg);
            }

            if (!response.IsSuccessStatusCode)
                return LoginResult.Fail($"Ошибка сервера: {(int)response.StatusCode}");

            AuthResponse? auth;
            try
            {
                auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOpts);
            }
            catch (System.Text.Json.JsonException)
            {
                IsOnline = false;
                return LoginResult.Fail("Некорректный ответ сервера при входе.");
            }

            return auth is not null ? LoginResult.Ok(auth) : LoginResult.Fail("Ошибка разбора ответа сервера.");
        }
        catch (HttpRequestException)
        {
            IsOnline = false;
            return LoginResult.Offline();
        }
        catch (OperationCanceledException)
        {
            IsOnline = false;
            return LoginResult.Offline();
        }
    }

    public async Task<AuthResponse?> RefreshAsync(string token, string refreshToken)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                Api("auth/refresh"),
                new RefreshRequest(token, refreshToken),
                JsonOpts);
            IsOnline = true;

            if (!response.IsSuccessStatusCode) return null;

            return await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOpts);
        }
        catch (HttpRequestException) { IsOnline = false; return null; }
        catch (OperationCanceledException) { IsOnline = false; return null; }
    }

    public Task<UserResponse?> GetCurrentUserAsync()
        => GetAsync<UserResponse>("auth/me");

    public async Task<List<RoleDto>?> GetRolesAsync()
        => await GetAsync<List<RoleDto>>("auth/roles");

    // ── Projects ──────────────────────────────────────────────────────────────
    public async Task<List<ProjectListResponse>?> GetProjectsAsync(
        string? status = null, string? search = null)
    {
        var q = BuildQuery(("status", status), ("search", search));
        return await GetAsync<List<ProjectListResponse>>($"projects{q}");
    }

    public async Task<ProjectResponse?> GetProjectAsync(Guid id)
        => await GetAsync<ProjectResponse>($"projects/{id}");

    public async Task<ProjectResponse?> CreateProjectAsync(CreateProjectRequest request)
        => await PostAsync<ProjectResponse>("projects", request);

    public async Task<ProjectResponse?> UpdateProjectAsync(Guid id, UpdateProjectRequest request)
        => await PutAsync<ProjectResponse>($"projects/{id}", request);

    public async Task<bool> DeleteProjectAsync(Guid id)
        => await DeleteAsync($"projects/{id}");

    // ── Tasks ─────────────────────────────────────────────────────────────────
    public async Task<List<TaskListResponse>?> GetTasksAsync(
        Guid? projectId = null, string? status = null, string? priority = null,
        Guid? assignedUserId = null, string? search = null)
    {
        var q = BuildQuery(
            ("projectId", projectId?.ToString()),
            ("status", status),
            ("priority", priority),
            ("assignedUserId", assignedUserId?.ToString()),
            ("search", search));
        return await GetAsync<List<TaskListResponse>>($"tasks{q}");
    }

    public async Task<TaskResponse?> GetTaskAsync(Guid id)
        => await GetAsync<TaskResponse>($"tasks/{id}");

    public async Task<TaskResponse?> CreateTaskAsync(CreateTaskRequest request)
        => await PostAsync<TaskResponse>("tasks", request);

    public async Task<TaskResponse?> UpdateTaskAsync(Guid id, UpdateTaskRequest request)
        => await PutAsync<TaskResponse>($"tasks/{id}", request);

    public async Task<bool> DeleteTaskAsync(Guid id)
        => await DeleteAsync($"tasks/{id}");

    // ── Stages ────────────────────────────────────────────────────────────────
    public async Task<StageResponse?> GetStageAsync(Guid id)
        => await GetAsync<StageResponse>($"taskstages/{id}");

    public async Task<StageResponse?> CreateStageAsync(CreateStageRequest request)
        => await PostAsync<StageResponse>("taskstages", request);

    public async Task<StageResponse?> UpdateStageAsync(Guid id, UpdateStageRequest request)
        => await PutAsync<StageResponse>($"taskstages/{id}", request);

    public async Task<bool> DeleteStageAsync(Guid id)
        => await DeleteAsync($"taskstages/{id}");

    public async Task<StageMaterialResponse?> AddStageMaterialAsync(
        Guid stageId, AddStageMaterialRequest request)
        => await PostAsync<StageMaterialResponse>($"taskstages/{stageId}/materials", request);

    public async Task<bool> RemoveStageMaterialAsync(Guid stageId, Guid stageMaterialId)
        => await DeleteAsync($"taskstages/{stageId}/materials/{stageMaterialId}");

    // ── Materials ─────────────────────────────────────────────────────────────
    public async Task<List<MaterialResponse>?> GetMaterialsAsync(string? search = null)
    {
        var q = BuildQuery(("search", search));
        return await GetAsync<List<MaterialResponse>>($"materials{q}");
    }

    public async Task<MaterialResponse?> CreateMaterialAsync(CreateMaterialRequest request)
        => await PostAsync<MaterialResponse>("materials", request);

    public async Task<MaterialResponse?> UpdateMaterialAsync(Guid id, UpdateMaterialRequest request)
        => await PutAsync<MaterialResponse>($"materials/{id}", request);

    public async Task<bool> DeleteMaterialAsync(Guid id)
        => await DeleteAsync($"materials/{id}");

    public Task<List<MaterialCategoryResponse>?> GetMaterialCategoriesAsync()
        => GetAsync<List<MaterialCategoryResponse>>("material-categories");

    public Task<List<EquipmentCategoryResponse>?> GetEquipmentCategoriesAsync()
        => GetAsync<List<EquipmentCategoryResponse>>("equipment-categories");

    public Task<MaterialCategoryResponse?> CreateMaterialCategoryAsync(CreateMaterialCategoryRequest request)
        => PostAsync<MaterialCategoryResponse>("material-categories", request);

    public Task<EquipmentCategoryResponse?> CreateEquipmentCategoryAsync(CreateEquipmentCategoryRequest request)
        => PostAsync<EquipmentCategoryResponse>("equipment-categories", request);

    public Task<List<MaterialStockMovementResponse>?> GetAllMaterialStockMovementsAsync()
        => GetAsync<List<MaterialStockMovementResponse>>("inventory/material-stock-movements");

    public Task<MaterialStockMovementResponse?> RecordMaterialStockMovementAsync(Guid materialId, RecordMaterialStockRequest request)
        => PostAsync<MaterialStockMovementResponse>($"materials/{materialId}/stock-movements", request);

    public Task<List<EquipmentResponse>?> GetAllEquipmentAsync()
        => GetAsync<List<EquipmentResponse>>("inventory/equipment");

    public Task<EquipmentResponse?> CreateEquipmentAsync(CreateEquipmentRequest request)
        => PostAsync<EquipmentResponse>("equipment", request);

    public Task<EquipmentResponse?> UpdateEquipmentAsync(Guid id, UpdateEquipmentRequest request)
        => PutAsync<EquipmentResponse>($"equipment/{id}", request);

    public Task<bool> DeleteEquipmentAsync(Guid id)
        => DeleteAsync($"equipment/{id}");

    public Task<EquipmentHistoryEntryResponse?> RecordEquipmentEventAsync(Guid equipmentId, RecordEquipmentEventRequest request)
        => PostAsync<EquipmentHistoryEntryResponse>($"equipment/{equipmentId}/history", request);

    public Task<List<EquipmentHistoryEntryResponse>?> GetAllEquipmentHistoryAsync()
        => GetAsync<List<EquipmentHistoryEntryResponse>>("inventory/equipment-history");

    // ── Files ─────────────────────────────────────────────────────────────────
    public async Task<List<FileDto>?> GetFilesAsync(
        Guid? projectId = null, Guid? taskId = null, Guid? stageId = null)
    {
        var q = BuildQuery(
            ("projectId", projectId?.ToString()),
            ("taskId", taskId?.ToString()),
            ("stageId", stageId?.ToString()));
        return await GetAsync<List<FileDto>>($"files{q}");
    }

    public async Task<bool> DeleteFileAsync(Guid id) => await DeleteAsync($"files/{id}");

    public async Task<byte[]?> DownloadFileAsync(Guid id)
    {
        var response = await SendWithRetryAsync(() => _http.GetAsync(Api($"files/{id}")));
        if (response == null || !response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<FileDto?> UploadFileAsync(
        string filePath, Guid? projectId = null, Guid? taskId = null, Guid? stageId = null, DateTime? originalCreatedAt = null, Guid? id = null)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists) return null;

        var q = BuildQuery(
            ("projectId", projectId?.ToString()),
            ("taskId", taskId?.ToString()),
            ("stageId", stageId?.ToString()),
            ("originalCreatedAt", originalCreatedAt.HasValue ? FormatUtcInstantForQuery(originalCreatedAt.Value) : null),
            ("id", id?.ToString()));

        // We can't reuse MultipartFormDataContent across retries, so we need a factory or create it inside.
        var response = await SendWithRetryAsync(async () => {
            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(filePath));
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(GetMimeType(fileInfo.Extension));
            content.Add(fileContent, "file", fileInfo.Name);
            return await _http.PostAsync(Api($"files/upload{q}"), content);
        });

        if (response == null || !response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<FileDto>(JsonOpts);
    }

    private static string GetMimeType(string extension) => extension.ToLower() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "application/octet-stream"
    };

    // ── Users ─────────────────────────────────────────────────────────────────
    public async Task<List<UserResponse>?> GetUsersAsync(string? search = null)
    {
        LastUsersPullError = null;
        var q = BuildQuery(("search", search));
        var uri = Api($"users{q}");
        
        var response = await SendWithRetryAsync(() => _http.GetAsync(uri));
        if (response == null) return null;

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            LastUsersPullError = response.StatusCode == HttpStatusCode.Unauthorized
                ? "Список пользователей: 401 — сессия истекла."
                : $"Список пользователей: HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {TruncateForDiag(body, 500)}";
            return null;
        }

        try
        {
            var list = await response.Content.ReadFromJsonAsync<List<UserResponse>>(JsonOpts);
            return list ?? [];
        }
        catch (JsonException ex)
        {
            LastUsersPullError = "Список пользователей: разбор JSON — " + ex.Message;
            return null;
        }
    }

    private static string TruncateForDiag(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return s.Length <= max ? s : s[..max] + "…";
    }

    public async Task<UserResponse?> CreateUserAsync(CreateUserRequest request)
        => await PostAsync<UserResponse>("users", request);

    public async Task<UserResponse?> UpdateUserAsync(Guid id, UpdateUserRequest request)
        => await PutAsync<UserResponse>($"users/{id}", request);

    public async Task<bool> DeleteUserAsync(Guid id) => await DeleteAsync($"users/{id}");

    /// <summary>
    /// Локальная SQLite/EF часто отдаёт UTC-моменты с Kind=Unspecified.
    /// <see cref="DateTime.ToUniversalTime"/> для Unspecified трактует их как локальные и сдвигает инкрементальный since.
    /// </summary>
    private static string FormatUtcInstantForQuery(DateTime dt)
    {
        var utc = dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
        };
        return utc.ToString("O", CultureInfo.InvariantCulture);
    }

    public Task<List<DiscussionMessageResponse>?> GetDiscussionMessagesAsync(DateTime? since = null)
    {
        var q = since.HasValue
            ? $"?since={Uri.EscapeDataString(FormatUtcInstantForQuery(since.Value))}"
            : "";
        return GetAsync<List<DiscussionMessageResponse>>($"discussion-messages{q}");
    }

    public async Task<DiscussionMessageResponse?> PostDiscussionMessageAsync(CreateDiscussionMessageRequest request)
    {
        var response = await SendWithRetryAsync(() => _http.PostAsJsonAsync(Api("discussion-messages"), request, JsonOpts));
        if (response == null || !response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<DiscussionMessageResponse>(JsonOpts);
    }

    public Task<List<SyncedActivityLogResponse>?> GetSyncedActivityLogsAsync(DateTime? since = null)
    {
        var q = since.HasValue
            ? $"?since={Uri.EscapeDataString(FormatUtcInstantForQuery(since.Value))}"
            : "";
        return GetAsync<List<SyncedActivityLogResponse>>($"synced-activity-logs{q}");
    }

    public async Task<SyncedActivityLogResponse?> PostSyncedActivityLogAsync(CreateSyncedActivityLogRequest request)
    {
        var response = await SendWithRetryAsync(() => _http.PostAsJsonAsync(Api("synced-activity-logs"), request, JsonOpts));
        if (response == null || !response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<SyncedActivityLogResponse>(JsonOpts);
    }

    public async Task<bool> ReplaceTaskAssigneesAsync(Guid taskId, ReplaceTaskAssigneesRequest request)
        => await PutNoContentAsync($"tasks/{taskId}/assignees", request);

    public async Task<bool> ReplaceStageAssigneesAsync(Guid stageId, ReplaceStageAssigneesRequest request)
        => await PutNoContentAsync($"taskstages/{stageId}/assignees", request);

    public async Task<bool> UploadUserAvatarAsync(Guid userId, byte[] avatarData)
    {
        var request = new UploadAvatarRequest(avatarData);
        var response = await SendWithRetryAsync(() => _http.PutAsJsonAsync(Api($"users/{userId}/avatar"), request, JsonOpts));
        return response?.IsSuccessStatusCode ?? false;
    }

    private static async Task<string?> TryReadErrorMessageAsync(HttpResponseMessage response)
    {
        try
        {
            var doc = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            if (doc.ValueKind == JsonValueKind.Object && doc.TryGetProperty("message", out var m))
                return m.GetString();
        }
        catch { /* ignore */ }
        return null;
    }

    private async Task<bool> PutNoContentAsync(string url, object body)
    {
        var response = await SendWithRetryAsync(() => _http.PutAsJsonAsync(Api(url), body, JsonOpts));
        return response?.IsSuccessStatusCode ?? false;
    }

    /// <summary>Абсолютный URI: база из <see cref="IAuthService.ApiBaseUrl"/>, путь относительно /api/.</summary>
    private Uri Api(string relativePathAndQuery)
    {
        var baseUrl = _auth.ApiBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = "http://localhost:5147/api/";
        baseUrl = baseUrl.TrimEnd('/') + "/";
        var rel = relativePathAndQuery.TrimStart('/');
        return new Uri($"{baseUrl}{rel}", UriKind.Absolute);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private void AttachToken()
    {
        if (!string.IsNullOrWhiteSpace(_auth.Token))
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _auth.Token!.Trim());
        else
            _http.DefaultRequestHeaders.Authorization = null;
    }

    private async Task<HttpResponseMessage?> SendWithRetryAsync(Func<Task<HttpResponseMessage>> requestFunc)
    {
        try
        {
            AttachToken();
            var response = await requestFunc();
            IsOnline = true;

            if (response.StatusCode == HttpStatusCode.Unauthorized && _auth.IsAuthenticated)
            {
                // Try refresh
                if (await _auth.TryRefreshJwtIfNeededAsync(this))
                {
                    // Retry once
                    AttachToken();
                    response = await requestFunc();
                }
            }

            return response;
        }
        catch (HttpRequestException) { IsOnline = false; return null; }
        catch (OperationCanceledException) { IsOnline = false; return null; }
    }

    private async Task<T?> GetAsync<T>(string url)
    {
        var response = await SendWithRetryAsync(() => _http.GetAsync(Api(url)));
        if (response == null || !response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<T>(JsonOpts);
    }

    private async Task<T?> PostAsync<T>(string url, object body)
    {
        var response = await SendWithRetryAsync(() => _http.PostAsJsonAsync(Api(url), body, JsonOpts));
        if (response == null || !response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<T>(JsonOpts);
    }

    private async Task<T?> PutAsync<T>(string url, object body)
    {
        var response = await SendWithRetryAsync(() => _http.PutAsJsonAsync(Api(url), body, JsonOpts));
        if (response == null || !response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<T>(JsonOpts);
    }

    private async Task<bool> DeleteAsync(string url)
    {
        var response = await SendWithRetryAsync(() => _http.DeleteAsync(Api(url)));
        if (response == null) return false;
        // 404 — уже удалено на сервере; для очереди синхронизации считаем успехом (идемпотентность).
        if (response.StatusCode == HttpStatusCode.NotFound)
            return true;
        return response.IsSuccessStatusCode;
    }

    private static string BuildQuery(params (string key, string? value)[] pairs)
    {
        var parts = pairs
            .Where(p => p.value is not null)
            .Select(p => $"{p.key}={Uri.EscapeDataString(p.value!)}");
        var qs = string.Join("&", parts);
        return qs.Length > 0 ? "?" + qs : string.Empty;
    }
}
