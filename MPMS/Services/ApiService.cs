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

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ApiService(HttpClient http, IAuthService auth)
    {
        _http = http;
        _auth = auth;
    }

    public bool IsOnline { get; private set; } = true;

    public async Task ProbeAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await _http.GetAsync("auth/roles",
                HttpCompletionOption.ResponseHeadersRead, cts.Token);
            IsOnline = true;
        }
        catch
        {
            IsOnline = false;
        }
    }

    // ── Auth ──────────────────────────────────────────────────────────────────
    public async Task<LoginResult> LoginAsync(string username, string password)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                "auth/login",
                new LoginRequest(username, password),
                JsonOpts);
            IsOnline = true;

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return LoginResult.WrongCredentials();

            if (!response.IsSuccessStatusCode)
                return LoginResult.Fail($"Ошибка сервера: {(int)response.StatusCode}");

            var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOpts);
            return auth is not null ? LoginResult.Ok(auth) : LoginResult.Fail("Ошибка разбора ответа сервера.");
        }
        catch (HttpRequestException)
        {
            IsOnline = false;
            return LoginResult.Offline();
        }
        catch (TaskCanceledException)
        {
            IsOnline = false;
            return LoginResult.Fail("Превышено время ожидания. Проверьте, что API запущен.");
        }
    }

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

    // ── Users ─────────────────────────────────────────────────────────────────
    public async Task<List<UserResponse>?> GetUsersAsync(string? search = null)
    {
        var q = BuildQuery(("search", search));
        return await GetAsync<List<UserResponse>>($"users{q}");
    }

    public async Task<UserResponse?> CreateUserAsync(CreateUserRequest request)
        => await PostAsync<UserResponse>("users", request);

    public async Task<UserResponse?> UpdateUserAsync(Guid id, UpdateUserRequest request)
        => await PutAsync<UserResponse>($"users/{id}", request);

    // ── Helpers ───────────────────────────────────────────────────────────────
    private void AttachToken()
    {
        if (_auth.Token is not null)
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _auth.Token);
    }

    private async Task<T?> GetAsync<T>(string url)
    {
        try
        {
            AttachToken();
            var response = await _http.GetAsync(url);
            IsOnline = true;
            if (!response.IsSuccessStatusCode) return default;
            return await response.Content.ReadFromJsonAsync<T>(JsonOpts);
        }
        catch (HttpRequestException)       { IsOnline = false; return default; }
        catch (OperationCanceledException) { IsOnline = false; return default; }
    }

    private async Task<T?> PostAsync<T>(string url, object body)
    {
        try
        {
            AttachToken();
            var response = await _http.PostAsJsonAsync(url, body, JsonOpts);
            IsOnline = true;
            if (!response.IsSuccessStatusCode) return default;
            return await response.Content.ReadFromJsonAsync<T>(JsonOpts);
        }
        catch (HttpRequestException)       { IsOnline = false; return default; }
        catch (OperationCanceledException) { IsOnline = false; return default; }
    }

    private async Task<T?> PutAsync<T>(string url, object body)
    {
        try
        {
            AttachToken();
            var response = await _http.PutAsJsonAsync(url, body, JsonOpts);
            IsOnline = true;
            if (!response.IsSuccessStatusCode) return default;
            return await response.Content.ReadFromJsonAsync<T>(JsonOpts);
        }
        catch (HttpRequestException)       { IsOnline = false; return default; }
        catch (OperationCanceledException) { IsOnline = false; return default; }
    }

    private async Task<bool> DeleteAsync(string url)
    {
        try
        {
            AttachToken();
            var response = await _http.DeleteAsync(url);
            IsOnline = true;
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)       { IsOnline = false; return false; }
        catch (OperationCanceledException) { IsOnline = false; return false; }
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
