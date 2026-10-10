namespace Nerdy.Api.Features.Classes;

/// <summary>
/// Teacher features. Each sub-feature maps its own endpoints onto the shared groups, so a new one (e.g. class
/// announcements) is a new file plus one line here. Authorization inside the module is by class membership role,
/// not by the account's <see cref="Nerdy.Contracts.UserRole"/> (that only gates creating a class).
/// </summary>
public static class ClassesModule
{
    public static RouteGroupBuilder MapClassesModule(this RouteGroupBuilder api)
    {
        api.MapGroup("/classes")
            .WithTags("Classes")
            .RequireAuthorization()
            .MapClassroomEndpoints()
            .MapClassMemberEndpoints()
            .MapClassAssignmentEndpoints()
            .MapClassProgressEndpoints();

        api.MapGroup("/assignments")
            .WithTags("Classes")
            .RequireAuthorization()
            .MapAssignmentEndpoints();

        return api;
    }
}
