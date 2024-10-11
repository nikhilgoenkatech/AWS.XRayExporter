using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http.Headers;

[assembly: FunctionsStartup(typeof(XRayConnector.Startup))]


namespace XRayConnector
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            builder.Services.AddHttpClient("XRayConnector", config =>
            {
                var productValue = new ProductInfoHeaderValue("XRayConnector", "1.0");
                var commentValue = new ProductInfoHeaderValue("(+https://github.com/dtPaTh/AWS.XRayExporter)");

                config.DefaultRequestHeaders.UserAgent.Add(productValue);
                config.DefaultRequestHeaders.UserAgent.Add(commentValue);
            });
        }
    }
}



