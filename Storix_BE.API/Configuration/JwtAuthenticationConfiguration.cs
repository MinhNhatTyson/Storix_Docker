using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

namespace Storix_BE.API.Configuration
{
    public static class JwtAuthenticationConfiguration
    {
        public static void AddJwtAuthenticationService(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            }
                ).AddCookie().AddGoogle(options =>
                {
                    var clientId = configuration["Authentication:Google:ClientId"];
                    if(clientId == null)
                    {
                        throw new ArgumentNullException(nameof(clientId));
                    }
                    var clientSecret = configuration["Authentication:Google:ClientSecret"];
                    if (clientSecret == null)
                    {
                        throw new ArgumentNullException(nameof(clientSecret));
                    }

                    options.ClientId = clientId;
                    options.ClientSecret = clientSecret;
                    options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                }).AddJwtBearer(options =>
                {
                    options.RequireHttpsMetadata = false;
                    options.SaveToken = true;
                    JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = false,
                        ValidateIssuer = false,
                        ValidateAudience = false,
                        ValidateLifetime = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration.GetValue<string>("SECRET_KEY") ?? (configuration["jwt:key"]))),
                        ClockSkew = TimeSpan.Zero,
                        ValidIssuer = configuration.GetValue<string>("SECRET_ISSUER") ?? configuration["jwt:issuer"],
                        ValidAudience = configuration.GetValue<string>("SECRET_AUDIENCE") ?? configuration["jwt:audience"]
                    };
                    options.Events = new JwtBearerEvents
                    {
                        OnChallenge = async context =>
                        {
                            // Call this to skip the default logic and avoid using the default response
                            context.HandleResponse();

                            if (context.AuthenticateFailure != null && context.AuthenticateFailure.GetType() == typeof(SecurityTokenExpiredException))
                            {
                                context.Response.Headers.Add("IS-TOKEN-EXPIRED", "true");
                            }

                            context.Response.ContentType = "application/json";

                            var httpContext = context.HttpContext;

                            const int statusCode = StatusCodes.Status401Unauthorized;

                            var routeData = httpContext.GetRouteData();
                            var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

                            var factory = httpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();

                            var problemDetails = factory.CreateProblemDetails(httpContext, statusCode);

                            var result = new ObjectResult(problemDetails)
                            {
                                StatusCode = statusCode,
                                DeclaredType = typeof(ProblemDetails)
                            };

                            await result.ExecuteResultAsync(actionContext);
                        },

                        OnMessageReceived = context =>
                        {
                            var accessToken = context.Request.Query["access_token"];
                            var path = context.HttpContext.Request.Path;
                            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/project"))
                            {
                                context.Token = accessToken;
                            }
                            return System.Threading.Tasks.Task.CompletedTask;
                        }
                    };
                });

            services.AddAuthorization();
        }
    }
}
