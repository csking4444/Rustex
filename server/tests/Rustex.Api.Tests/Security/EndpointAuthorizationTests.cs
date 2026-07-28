using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Rustex.Api.Controllers;
using Xunit;

namespace Rustex.Api.Tests.Security;

/// <summary>Guards the shape of the API's public surface.
///
/// Program.cs sets a fallback policy requiring an authenticated user, so a newly added controller
/// is protected by default and cannot be accidentally public. The remaining risk is the opposite
/// one — someone adds <c>[AllowAnonymous]</c> without thinking it through. This test pins the
/// exact set that is allowed to be reachable without a login, so widening it has to be a
/// deliberate edit here rather than a side effect somewhere else.</summary>
public class EndpointAuthorizationTests
{
    /// <summary>Every endpoint that may be reached without authentication, and why.</summary>
    private static readonly HashSet<string> ExpectedAnonymous =
    [
        // Sign-in itself cannot require being signed in.
        "AuthController.Register",
        "AuthController.Login",
        "AuthController.DiscordLogin",
        "AuthController.DiscordCallback",
        "AuthController.GoogleLogin",
        "AuthController.GoogleCallback",
        "AuthController.SteamLogin",
        "AuthController.SteamCallback",
        // Exchanging/revoking a refresh token: the access token is expired by definition here.
        "AuthController.Refresh",
        "AuthController.Logout",

        // Load balancer probe.
        "VersionController.Get",

        // Public reference data — item names, equivalent to a wiki lookup.
        "RustItemsController.Search",
        "RustItemsController.GetById",

        // Redeemed by the rustex-pair helper using a short-lived link code, on a separate JWT
        // scheme with its own audience.
        "RustPlusAccountController.RedeemLinkCode",
    ];

    private static IEnumerable<Type> Controllers() =>
        typeof(ServersController).Assembly
            .GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

    private static bool IsAnonymous(MethodInfo action, Type controller) =>
        action.GetCustomAttribute<AllowAnonymousAttribute>() is not null
        || controller.GetCustomAttribute<AllowAnonymousAttribute>() is not null;

    private static IEnumerable<(Type Controller, MethodInfo Action)> Actions() =>
        from controller in Controllers()
        from action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        where action.GetCustomAttributes<HttpMethodAttribute>().Any()
        select (controller, action);

    [Fact]
    public void AnonymousEndpoints_AreExactlyTheExpectedSet()
    {
        var actual = Actions()
            .Where(x => IsAnonymous(x.Action, x.Controller))
            .Select(x => $"{x.Controller.Name}.{x.Action.Name}")
            .ToHashSet();

        var unexpected = actual.Except(ExpectedAnonymous).OrderBy(x => x).ToList();
        var missing = ExpectedAnonymous.Except(actual).OrderBy(x => x).ToList();

        Assert.True(
            unexpected.Count == 0,
            "These endpoints became reachable without authentication. If that is intended, add them to "
            + $"ExpectedAnonymous with a reason:\n  {string.Join("\n  ", unexpected)}");

        Assert.True(
            missing.Count == 0,
            "These endpoints are listed as anonymous but no longer are. Remove them from the list "
            + $"if they are now authenticated:\n  {string.Join("\n  ", missing)}");
    }

    /// <summary>A controller with a class-level <c>[AllowAnonymous]</c> plus an action-level
    /// <c>[Authorize]</c> is a trap: the authorization middleware finds the AllowAnonymous in the
    /// endpoint metadata and skips the policy entirely, so the action is public despite looking
    /// protected. This catches that combination.</summary>
    [Fact]
    public void NoController_MixesClassLevelAllowAnonymousWithActionLevelAuthorize()
    {
        var offenders = Controllers()
            .Where(c => c.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            .SelectMany(c => c
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<AuthorizeAttribute>() is not null)
                .Select(m => $"{c.Name}.{m.Name}"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "[Authorize] on an action is silently ignored when its controller has [AllowAnonymous]. "
            + $"Move [AllowAnonymous] onto the individual anonymous actions instead:\n  {string.Join("\n  ", offenders)}");
    }
}
