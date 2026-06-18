# Blazor Page Template

This is the canonical template for a list page in the Admin UI. Copy this and
change only the route, title, API call, and columns.

```razor
@page "/{route}"
@using SchoolCollab.{Context}.Admin.Services
@using SchoolCollab.{Context}.Core.DTOs
@implements IDisposable

@inject {Context}ApiClient Api
@inject ILogger<Index> Logger
@inject NavigationManager Nav

<PageTitle>{Entities}</PageTitle>

<h1>{Entities}</h1>

<ErrorBoundary>
    <ChildContent>
        @if (_items is null)
        {
            <FluentProgressRing />
        }
        else if (_items.Length == 0)
        {
            <FluentMessageBar Intent="MessageIntent.Info">
                No {entities} found.
            </FluentMessageBar>
        }
        else
        {
            @if (!string.IsNullOrWhiteSpace(_error))
            {
                <FluentMessageBar Intent="MessageIntent.Error">
                    @_error
                </FluentMessageBar>
            }

            <FluentButton Appearance="Appearance.Accent"
                          OnClick="@(() => Nav.NavigateTo("/{route}/create"))">
                Create
            </FluentButton>

            <FluentDataGrid Items="@_items" TItem="{Entity}Dto">
                <TemplateColumn Title="Name" Sortable="true">
                    @context.Name
                </TemplateColumn>
                <!-- Add more columns here -->
                <TemplateColumn Title="Actions">
                    <FluentButton Appearance="Appearance.Outline"
                                  OnClick="@(() => Nav.NavigateTo($"/{route}/{context.Id}"))">
                        View
                    </FluentButton>
                </TemplateColumn>
            </FluentDataGrid>
        }
    </ChildContent>
    <ErrorContent Context="ex">
        <FluentMessageBar Intent="MessageIntent.Error">
            Something went wrong: @ex.Message
        </FluentMessageBar>
    </ErrorContent>
</ErrorBoundary>

@code {
    private {Entity}Dto[]? _items;
    private string? _error;
    private CancellationTokenSource? _loadCts;
    private volatile bool _disposed;

    protected override async Task OnInitializedAsync()
    {
        _loadCts = new CancellationTokenSource();
        try
        {
            _items = await Api.ListAsync(_loadCts.Token);
            if (_disposed) return;
            Logger.LogInformation("Loaded {Count} {Entities}", _items?.Length ?? 0, nameof({Entity}));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (_disposed) return;
            _error = ex.Message;
            Logger.LogError(ex, "Failed to load {Entities}", nameof({Entity}));
        }
    }

    // Optimistic toggle example (for enable/disable actions)
    private async Task OnToggleAsync(Guid id, bool disable)
    {
        if (_items is null) return;
        var idx = Array.FindIndex(_items, x => x.Id == id);
        if (idx < 0) return;
        var previous = _items[idx];
        _items[idx] = previous with { IsDisabled = disable };
        StateHasChanged();
        try
        {
            await (disable ? Api.DisableAsync(id) : Api.EnableAsync(id));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to toggle {Entity} {Id}", nameof({Entity}), id);
            var i = Array.FindIndex(_items, x => x.Id == id);
            if (i >= 0) _items[i] = previous;   // rollback
            _error = ex.Message;
            StateHasChanged();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }
}
```

## Detail / Create / Edit Pages

Follow the same patterns for detail and form pages:

- **Detail page**: Load single entity in `OnInitializedAsync` with CTS + `_disposed` guard
- **Create page**: Form model, `FluentTextField` / `FluentSelect` inputs, submit via API
- **Edit page**: Load entity → populate form → submit changes

### Form Page Template (Create/Edit)

```razor
@page "/{route}/create"
@using SchoolCollab.{Context}.Admin.Services
@implements IDisposable

@inject {Context}ApiClient Api
@inject ILogger<Create> Logger
@inject NavigationManager Nav

<PageTitle>Create {Entity}</PageTitle>

<h1>Create {Entity}</h1>

<ErrorBoundary>
    <ChildContent>
        @if (!string.IsNullOrWhiteSpace(_error))
        {
            <FluentMessageBar Intent="MessageIntent.Error">@_error</FluentMessageBar>
        }

        <FluentEditForm Model="_model" OnValidSubmit="@HandleValidSubmit">
            <FluentValidationSummary />

            <FluentTextField Label="Name"
                             @bind-Value="_model.Name"
                             Placeholder="Enter name" />

            <!-- More fields -->

            <FluentButton Appearance="Appearance.Accent" Type="ButtonType.Submit">
                Save
            </FluentButton>
            <FluentButton Appearance="Appearance.Outline"
                          OnClick="@(() => Nav.NavigateTo("/{route}"))">
                Cancel
            </FluentButton>
        </FluentEditForm>
    </ChildContent>
    <ErrorContent Context="ex">
        <FluentMessageBar Intent="MessageIntent.Error">
            Something went wrong: @ex.Message
        </FluentMessageBar>
    </ErrorContent>
</ErrorBoundary>

@code {
    private Create{Entity}Request _model = new();
    private string? _error;
    private CancellationTokenSource? _saveCts;
    private volatile bool _disposed;

    private async Task HandleValidSubmit()
    {
        _saveCts = new CancellationTokenSource();
        try
        {
            await Api.CreateAsync(_model, _saveCts.Token);
            if (_disposed) return;
            Nav.NavigateTo("/{route}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (_disposed) return;
            _error = ex.Message;
            Logger.LogError(ex, "Failed to create {Entity}", nameof({Entity}));
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _saveCts?.Cancel();
        _saveCts?.Dispose();
        _saveCts = null;
    }
}
```

## Required Rules Checklist

- [ ] No per-page `@rendermode` — inherited from `App.razor`
- [ ] `@implements IDisposable` on every page with async data loading
- [ ] `CancellationTokenSource` in `OnInitializedAsync`, cancelled in `Dispose()`
- [ ] `volatile bool _disposed` checked after every `await`
- [ ] `FluentProgressRing` for loading (not `<p>Loading…</p>`)
- [ ] `FluentMessageBar` for errors and empty state
- [ ] `ErrorBoundary` wrapping main content
- [ ] `@key` on all `@foreach` rendering components
- [ ] Structured logging: `Logger.LogInformation("Loaded {Count}", items.Length)`
- [ ] Never `Console.WriteLine`
- [ ] Optimistic mutations: mutate in-memory → `StateHasChanged()` → API call → rollback on error