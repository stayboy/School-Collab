using Microsoft.Extensions.DependencyInjection;

namespace SchoolCollab.AI.Chat.Services;

/// <summary>
/// DI wiring for the AI chat RCL. Registers the <see cref="AiChatClient"/>
/// HttpClient (the host supplies the base address of its own AI server) and
/// the scoped <see cref="AiChatHub"/> used to mirror the inline chat into the
/// drawer panel. Call once from the host's service-registration pipeline, e.g.
/// <c>builder.Services.AddAiChat(c =&gt; c.BaseAddress = new Uri("https+http://settings-ai"));</c>.
/// </summary>
public static class AiChatModule
{
    public static IServiceCollection AddAiChat(this IServiceCollection services, Action<HttpClient> configure)
    {
        services.AddHttpClient<AiChatClient>(configure);
        services.AddScoped<AiChatHub>();
        return services;
    }
}