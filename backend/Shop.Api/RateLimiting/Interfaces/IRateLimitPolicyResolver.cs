using Microsoft.AspNetCore.Http;
using Shop.Api.RateLimiting.Models;

namespace Shop.Api.RateLimiting.Interfaces;

public interface IRateLimitPolicyResolver
{
    IReadOnlyList<RateLimitPolicy> Resolve(HttpContext context);
}