using Microsoft.JSInterop;

namespace ResearchEngine.Blazor.Services;

public sealed class LocalStorageService
{
    private readonly IJSRuntime _js;

    public LocalStorageService(IJSRuntime js)
    {
        _js = js;
    }

    public async ValueTask<string?> GetAsync(string key)
    {
        try
        {
            return await _js.InvokeAsync<string?>("researchEngine.storage.getItem", key);
        }
        catch
        {
            return null;
        }
    }

    public async ValueTask SetAsync(string key, string value)
    {
        try
        {
            await _js.InvokeVoidAsync("researchEngine.storage.setItem", key, value);
        }
        catch
        {
            // ignore (localStorage may be blocked)
        }
    }

    public async ValueTask RemoveAsync(string key)
    {
        try
        {
            await _js.InvokeVoidAsync("researchEngine.storage.removeItem", key);
        }
        catch
        {
            // ignore
        }
    }
}