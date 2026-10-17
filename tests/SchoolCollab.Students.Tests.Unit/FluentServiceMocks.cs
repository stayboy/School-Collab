using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
using Moq;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Creates the dialog-service mock the bUnit tests hand to a component under test.
/// </summary>
/// <remarks>
/// <para>
/// FluentUI v5's service <em>interfaces</em> cannot be mocked:
/// <c>IFluentServiceBase&lt;TComponent&gt;</c> declares <c>ProviderId</c>, <c>Items</c> and
/// <c>OnUpdatedAsync</c> with <c>internal</c> accessors, and Castle's proxy — generated in its own
/// dynamic assembly — cannot implement them, so every <c>new Mock&lt;IDialogService&gt;()</c> fails
/// with <c>TypeLoadException: Method 'set_ProviderId' … does not have an implementation</c>. The
/// concrete <see cref="DialogService"/> already implements those members, so mocking it loads.
/// </para>
/// <para>
/// Register before anything renders: bUnit 2.x refuses service registration once the first service
/// has been resolved from the context. Only the non-<c>sealed</c> virtual members are interceptable
/// — <c>ShowDialogAsync(Type, DialogOptions)</c> and its generic overloads. In 5.0.1 the convenience
/// wrappers (<c>ShowConfirmationAsync</c>, <c>ShowMessageBoxAsync</c>, the toast/bar helpers) are
/// emitted <c>virtual final</c>, so Moq rejects any Setup/Verify on them: a test that needs one
/// cannot use this helper.
/// </para>
/// </remarks>
internal static class FluentServiceMocks
{
    /// <summary>
    /// Registers a loose mock of the concrete <see cref="DialogService"/> as the
    /// <see cref="IDialogService"/> the component injects, replacing the real registration
    /// <c>AddFluentUIComponents()</c> left in the test context's services.
    /// </summary>
    internal static Mock<DialogService> RegisterDialogService(this BunitContext context)
    {
        foreach (var registration in context.Services.Where(s => s.ServiceType == typeof(IDialogService)).ToList())
        {
            context.Services.Remove(registration);
        }

        var mock = CreateDialogServiceMock();
        context.Services.AddSingleton<IDialogService>(mock.Object);
        return mock;
    }

    /// <summary>
    /// Builds the loose mock over a <em>standalone</em> provider.
    /// </summary>
    /// <remarks>
    /// <see cref="DialogService"/>'s constructor resolves <c>IJSRuntime</c> from the
    /// <c>IServiceProvider</c> it is handed, so handing it the test context's own provider would
    /// resolve from that provider and seal it against further registration
    /// (<c>InvalidOperationException: New services/implementations cannot be registered …</c>).
    /// The stub runtime satisfies that resolution without touching the context, and no call a test
    /// observes reaches it — the proxy intercepts them. The localizer may be null: it is stored,
    /// not resolved.
    /// </remarks>
    private static Mock<DialogService> CreateDialogServiceMock()
    {
        var provider = new ServiceCollection()
            .AddSingleton<IJSRuntime>(Mock.Of<IJSRuntime>())
            .BuildServiceProvider();

        return new Mock<DialogService>(provider, (IFluentLocalizer)null!);
    }
}
