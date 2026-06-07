using System.Net.Http.Headers;

namespace Marouanvs.Splunk.Tests.TestInfrastructure;

internal sealed record RequestSnapshot(
    HttpMethod Method,
    Uri Uri,
    AuthenticationHeaderValue? Authorization,
    string UserAgent,
    string Body);
