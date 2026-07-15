using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace ByHofman.EfCore.Provisioning.Tests.Infrastructure;

internal sealed class FakeHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "TestApp";
    public string ContentRootPath { get; set; } = "/";
    public IFileProvider ContentRootFileProvider { get; set; } = null!;
}
