using System.Security.Claims;
using System.Threading.Tasks;
using BobsBookstoreClassic.Data;
using Bookstore.Domain.Customers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Bookstore.Web
{
    public static class AuthenticationSetup
    {
        public static void ConfigureAuthentication(IServiceCollection services, IConfiguration configuration)
        {
            if (BookstoreConfiguration.GetSetting("Services/Authentication") == "aws")
            {
                ConfigureCognitoAuthentication(services, configuration);
            }
            else
            {
                // Local auth – cookie only, the LocalAuthenticationMiddleware handles sign-in
                services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                    .AddCookie(options =>
                    {
                        options.LoginPath = "/Authentication/Login";
                    });
            }
        }

        private static void ConfigureCognitoAuthentication(IServiceCollection services, IConfiguration configuration)
        {
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie()
            .AddOpenIdConnect(options =>
            {
                options.ClientId = BookstoreConfiguration.GetSetting("Authentication/Cognito/LocalClientId");
                options.MetadataAddress = BookstoreConfiguration.GetSetting("Authentication/Cognito/MetadataAddress");
                options.ResponseType = "code";
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.SaveTokens = true;
                options.UseTokenLifetime = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "cognito:username",
                    RoleClaimType = "cognito:groups"
                };
                options.Events = new OpenIdConnectEvents
                {
                    OnRedirectToIdentityProvider = context =>
                    {
                        var returnUrl = $"{context.Request.Scheme}://{context.Request.Host}/signin-oidc";
                        context.ProtocolMessage.RedirectUri = returnUrl;
                        return Task.CompletedTask;
                    },
                    OnAuthorizationCodeReceived = context =>
                    {
                        var returnUrl = $"{context.Request.Scheme}://{context.Request.Host}/signin-oidc";
                        context.TokenEndpointRequest.RedirectUri = returnUrl;
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = async context =>
                    {
                        var service = context.HttpContext.RequestServices.GetRequiredService<ICustomerService>();

                        var identity = (ClaimsIdentity)context.Principal.Identity;

                        var dto = new CreateOrUpdateCustomerDto(
                            identity.FindFirst(c => c.Type.Contains("nameidentifier"))?.Value,
                            identity.Name,
                            identity.FindFirst(c => c.Type.Contains("givenname"))?.Value,
                            identity.FindFirst(c => c.Type.Contains("surname"))?.Value);

                        await service.CreateOrUpdateCustomerAsync(dto);
                    }
                };
            });
        }
    }
}
