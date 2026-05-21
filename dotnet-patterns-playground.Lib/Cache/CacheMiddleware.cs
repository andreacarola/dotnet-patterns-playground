namespace dotnet_patterns_playground.Lib.Cache;

/// <summary>
/// Marks a GET action method as cacheable. The response will be stored in the distributed
/// cache and served from cache on subsequent identical requests.
/// Pair with <see cref="InvalidateCacheAttribute"/> on mutating endpoints to clear the cache.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class CacheResponseAttribute : Attribute
{
/// <summary>Cache TTL in minutes. Defaults to 60.</summary>
public int TTLMinutes { get; set; } = 60;

/// <summary>
/// When true, the cache key includes the authenticated user's email,
/// producing a separate cache entry per user.
/// </summary>
public bool VaryByUser { get; set; } = false;
}

/// <summary>
/// Marks an action method as one that should invalidate one or more cache entries
/// after successful execution.
/// Supports <c>{paramName}</c> tokens resolved from action arguments and
/// the special <c>{user.email}</c> token resolved from the authenticated user's identity.
/// Can be applied multiple times on the same method to invalidate multiple keys.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class InvalidateCacheAttribute : Attribute
{
public string KeyTemplate { get; }

public InvalidateCacheAttribute(string keyTemplate) => KeyTemplate = keyTemplate;
}

/// <summary>
/// Global action filter that implements the cache-aside pattern for HTTP GET responses.
/// Activated only on actions decorated with <see cref="CacheResponseAttribute"/>.
/// </summary>
public sealed class CacheResponseFilter : IAsyncActionFilter
{
private readonly IDistributedCache _cache;
private readonly ILogger<CacheResponseFilter> _logger;

public CacheResponseFilter(IDistributedCache cache, ILogger<CacheResponseFilter> logger)
    => (_cache, _logger) = (cache, logger);

public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
{
    var attribute = context.ActionDescriptor.EndpointMetadata
        .OfType<CacheResponseAttribute>()
        .FirstOrDefault();

    if (attribute is null || !HttpMethods.IsGet(context.HttpContext.Request.Method))
    {
        await next();
        return;
    }

    var cacheKey = BuildCacheKey(context, attribute.VaryByUser);

    var cached = await _cache.GetStringAsync(cacheKey, context.HttpContext.RequestAborted);
    if (cached is not null)
    {
        _logger.LogInformation("Cache hit for key {CacheKey}.", cacheKey);
        context.Result = new ContentResult
        {
            Content = cached,
            ContentType = "application/json",
            StatusCode = StatusCodes.Status200OK
        };
        return;
    }

    var executedContext = await next();

    if (executedContext.Exception is null &&
        executedContext.Result is ObjectResult { Value: not null } objectResult &&
        objectResult.StatusCode is null or (>= 200 and < 300))
    {
        var json = JsonSerializer.Serialize(objectResult.Value);
        await _cache.SetStringAsync(
            cacheKey,
            json,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(attribute.TTLMinutes)
            },
            context.HttpContext.RequestAborted);

        _logger.LogInformation("Cache set for key {CacheKey} with TTL {TTLMinutes} minutes.", cacheKey, attribute.TTLMinutes);
    }
}

private static string BuildCacheKey(ActionExecutingContext context, bool varyByUser)
{
    var path = context.HttpContext.Request.Path;
    var query = context.HttpContext.Request.QueryString;
    var key = $"{path}{query}";

    if (varyByUser)
    {
        var email = context.HttpContext.User.FindFirst(ClaimTypes.Email)?.Value;
        key = $"{key}::{email}";
    }

    return key;
}
}

/// <summary>
/// Global action filter that removes cache entries after a successful action execution.
/// Activated only on actions decorated with one or more <see cref="InvalidateCacheAttribute"/>.
/// </summary>
public sealed class InvalidateCacheFilter : IAsyncActionFilter
{
private readonly IDistributedCache _cache;
private readonly ILogger<InvalidateCacheFilter> _logger;

public InvalidateCacheFilter(IDistributedCache cache, ILogger<InvalidateCacheFilter> logger)
    => (_cache, _logger) = (cache, logger);

public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
{
    var executedContext = await next();

    if (executedContext.Exception is not null)
        return;

    var attributes = context.ActionDescriptor.EndpointMetadata
        .OfType<InvalidateCacheAttribute>()
        .ToArray();

    if (attributes.Length == 0)
        return;

    foreach (var attribute in attributes)
    {
        var resolvedKey = ResolveKey(attribute.KeyTemplate, context);
        await _cache.RemoveAsync(resolvedKey, context.HttpContext.RequestAborted);
        _logger.LogInformation("Cache invalidated for key {CacheKey}.", resolvedKey);
    }
}

private static string ResolveKey(string keyTemplate, ActionExecutingContext context)
{
    foreach (var (name, value) in context.ActionArguments)
    {
        keyTemplate = keyTemplate.Replace(
            $"{{{name}}}",
            value?.ToString() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    var email = context.HttpContext.User.FindFirst(ClaimTypes.Email)?.Value;
    return keyTemplate.Replace("{user.email}", email ?? string.Empty, StringComparison.OrdinalIgnoreCase);
}
}