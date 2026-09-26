using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Models.Auth;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace Hatch.Api.Tests.Auth;

/// <summary>
/// The API half of the boundary. It runs as an authorization filter rather than
/// inside the action, which is the property worth pinning: a refused request
/// must not reach model binding or the action body, or a guarded endpoint has
/// already done half of what it was told not to.
/// </summary>
public class RequireAdminAttributeTests
{
    [Fact]
    public async Task LetsAnAdministratorThrough()
    {
        var context = NewContext(new StubRoleGate
        {
            Decision = RoleDecision.Allow(new EfPerson
            {
                Id = Guid.NewGuid(),
                Name = "Ada",
                Role = PersonRole.Admin,
                CreatedAt = DateTimeOffset.UnixEpoch,
                UpdatedAt = DateTimeOffset.UnixEpoch,
            }),
        });

        await new RequireAdminAttribute().OnAuthorizationAsync(context);

        // A null Result is the filter pipeline's "carry on" - anything else
        // short-circuits before the action runs.
        Assert.Null(context.Result);
    }

    /// <summary>
    /// The rollback. An install with no wall behaves
    /// exactly as it did before these attributes existed, which is the only
    /// reason it was safe to put them on thirty-odd actions at once.
    /// </summary>
    [Fact]
    public async Task LetsEveryoneThroughWhileTheWallIsOff()
    {
        var context = NewContext(new StubRoleGate { Enabled = false, Decision = RoleDecision.Dormant });

        await new RequireAdminAttribute().OnAuthorizationAsync(context);

        Assert.Null(context.Result);
    }

    /// <summary>
    /// A 403, not the 404 the admin app's bundle gets. The entities behind
    /// these verbs are already listed by unguarded GETs, so there is nothing
    /// left for a 404 to conceal and a great deal for it to confuse.
    /// </summary>
    [Theory]
    [InlineData(RoleDecision.NotAdmin)]
    [InlineData(RoleDecision.NoPerson)]
    [InlineData(RoleDecision.PendingApproval)]
    [InlineData(RoleDecision.NoGrant)]
    public async Task RefusesEveryoneElseWithAReasonTheAdminAppCanRender(string reason)
    {
        var context = NewContext(new StubRoleGate { Decision = RoleDecision.Refuse(reason) });

        await new RequireAdminAttribute().OnAuthorizationAsync(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        // The two refusals want different sentences on screen: an unlinked
        // device is fixed on the Sessions page, a non-admin is fixed by
        // somebody else.
        Assert.Equal(reason, Assert.IsType<AuthErrorDto>(result.Value).Error);
    }

    [Fact]
    public async Task AsksAboutTheRequestItIsGuarding()
    {
        var gate = new StubRoleGate();
        var context = NewContext(gate);
        context.HttpContext.Request.Method = HttpMethods.Delete;
        context.HttpContext.Request.Path = "/api/zones/2b1a";

        await new RequireAdminAttribute().OnAuthorizationAsync(context);

        Assert.Equal(("DELETE", "/api/zones/2b1a", "10.0.0.7", PersonRole.Admin, null), gate.LastAsked);
    }

    /// <summary>The level and the scope both reach the gate: what the attribute declares is what is asked.</summary>
    [Fact]
    public async Task AsksForTheLevelAndScopeItDeclares()
    {
        var gate = new StubRoleGate();
        var context = NewContext(gate);

        await new RequireRoleAttribute(PersonRole.User) { AcceptScope = ApiKeyScopes.Hatch }.OnAuthorizationAsync(context);

        Assert.Equal(PersonRole.User, gate.LastAsked.Minimum);
        Assert.Equal(ApiKeyScopes.Hatch, gate.LastAsked.AcceptScope);
    }

    private static AuthorizationFilterContext NewContext(IRoleGate gate)
    {
        var services = new ServiceCollection();
        services.AddSingleton(gate);

        var http = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
        http.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.7");
        http.Request.Method = HttpMethods.Post;
        http.Request.Path = "/api/people";

        return new AuthorizationFilterContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            []);
    }
}
