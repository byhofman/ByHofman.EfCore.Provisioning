using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace ByHofman.EfCore.Provisioning.Tests.Infrastructure;

internal sealed class FakeApplicationBuilder : IApplicationBuilder
{
    public FakeApplicationBuilder(IServiceProvider services) => ApplicationServices = services;

    public IServiceProvider ApplicationServices { get; set; }
    public IFeatureCollection ServerFeatures { get; } = new FeatureCollection();
    public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();

    public IApplicationBuilder Use(Func<RequestDelegate, RequestDelegate> middleware) => this;
    public IApplicationBuilder New() => throw new NotSupportedException();
    public RequestDelegate Build() => _ => Task.CompletedTask;
}
