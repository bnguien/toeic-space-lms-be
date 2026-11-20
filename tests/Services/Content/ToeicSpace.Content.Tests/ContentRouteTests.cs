using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using ToeicSpace.Content.API.Controllers;
using Xunit;

namespace ToeicSpace.Content.Tests;

public sealed class ContentRouteTests
{
    [Fact]
    public void Mvc_exposes_storage_but_no_course_management_or_checkout()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(UploadsController).Assembly);
        using var provider = services.BuildServiceProvider();
        var actions = provider.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>().ToArray();
        var routes = actions.Select(a => a.AttributeRouteInfo!.Template!).ToArray();
        Assert.Contains("api/v1/uploads/media/presign", routes);
        Assert.Contains("api/v1/uploads/media/confirm", routes);
        Assert.DoesNotContain(actions, a => a.ControllerName is "Courses" or "PublicCourses" or "CourseLessons"
            or "CourseModules" or "CoursePromotions" or "Enrollments" or "InternalCheckout");
    }
}
