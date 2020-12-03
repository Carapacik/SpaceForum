using System.Net.Mail;
using System.Security.Claims;
using System.Globalization;
using System.Threading.RateLimiting;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SpaceForum.Application.Email;
using SpaceForum.Application.Discussions;
using SpaceForum.Application.Forums;
using SpaceForum.Application.Members;
using SpaceForum.Application.Messaging;
using SpaceForum.Application.Security;
using SpaceForum.Domain.Discussions;
using SpaceForum.Infrastructure.Email;
using SpaceForum.Infrastructure.Identity;
using SpaceForum.Infrastructure.Persistence;
using SpaceForum.Infrastructure.Security;
using SpaceForum.Web.Components;
using SpaceForum.Web.Components.Account;
using SpaceForum.Web.Development;
using SpaceForum.Web.Health;
using SpaceForum.Web.Media;
using SpaceForum.Web.Rendering;
using SpaceForum.Web.Security;
using SpaceForum.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var cultures = new[] { new CultureInfo("en"), new CultureInfo("ru") };
    options.DefaultRequestCulture = new RequestCulture("en");
    options.SupportedCultures = cultures;
    options.SupportedUICultures = cultures;
    options.RequestCultureProviders =
    [
        new CookieRequestCultureProvider(),
        new AcceptLanguageHeaderRequestCultureProvider(),
    ];
});

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.SetPostgresVersion(18, 0)));
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"]);
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = MediaUploadProcessor.MaximumVideoBytes + (64 * 1024));
builder.Services.AddOptions<S3Options>()
    .Bind(builder.Configuration.GetSection(S3Options.SectionName))
    .Validate(options => Uri.TryCreate(options.ServiceUrl, UriKind.Absolute, out _), "S3:ServiceUrl is invalid.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.AccessKey), "S3:AccessKey is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.SecretKey), "S3:SecretKey is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.BucketName), "S3:BucketName is required.")
    .ValidateOnStart();
builder.Services.AddSingleton<IAmazonS3>(services =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<S3Options>>().Value;
    return new AmazonS3Client(
        new BasicAWSCredentials(options.AccessKey, options.SecretKey),
        new AmazonS3Config
        {
            ServiceURL = options.ServiceUrl,
            ForcePathStyle = true,
            AuthenticationRegion = "us-east-1",
        });
});
builder.Services.AddSingleton<S3MediaStorage>();
builder.Services.AddSingleton<MediaUploadProcessor>();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Password.RequiredUniqueChars = 4;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddOptions<SmtpOptions>()
    .Bind(builder.Configuration.GetSection(SmtpOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.Host), "Email:Smtp:Host is required.")
    .Validate(options => options.Port is > 0 and <= 65_535, "Email:Smtp:Port is invalid.")
    .Validate(options => MailAddress.TryCreate(options.FromAddress, out _), "Email:Smtp:FromAddress is invalid.")
    .Validate(
        options => builder.Environment.IsDevelopment() || options.EnableSsl,
        "SMTP TLS must be enabled outside Development.")
    .ValidateOnStart();
builder.Services.AddScoped<IEmailDelivery, SmtpEmailDelivery>();
builder.Services.AddScoped<IEmailSender<ApplicationUser>, IdentityEmailSender>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ForumMarkdownRenderer>();
builder.Services.AddScoped<IMemberProfileRepository, MemberProfileRepository>();
builder.Services.AddScoped<IForumCategoryRepository, ForumCategoryRepository>();
builder.Services.AddScoped<IDiscussionRepository, DiscussionRepository>();
builder.Services.AddScoped<IForumFeatureRepository, ForumFeatureRepository>();
builder.Services.AddScoped<IForumAdministrationRepository, ForumAdministrationRepository>();
builder.Services.AddScoped<ForumSettingsService>();
builder.Services.AddScoped<IMessagingRepository, MessagingRepository>();
builder.Services.AddScoped<MessagingService>();
builder.Services.AddScoped<IForumAdministrationAccess, IdentityForumAdministrationAccess>();
builder.Services.AddScoped<IForumPostingAccess, IdentityForumPostingAccess>();
builder.Services.AddScoped<IForumModerationAccess, IdentityForumModerationAccess>();
builder.Services.AddScoped<GetCategoriesHandler>();
builder.Services.AddScoped<CreateCategoryHandler>();
builder.Services.AddScoped<DeleteCategoryHandler>();
builder.Services.AddScoped<GetDiscussionsHandler>();
builder.Services.AddScoped<CreateTopicHandler>();
builder.Services.AddScoped<CreateReplyHandler>();
builder.Services.AddScoped<VoteTopicHandler>();
builder.Services.AddScoped<ModerateDiscussionHandler>();
builder.Services.AddScoped<ForumFeatureService>();
builder.Services.AddScoped<CreateMemberProfileHandler>();
builder.Services.AddScoped<GetMemberProfileHandler>();
builder.Services.AddScoped<UpdateMemberProfileHandler>();
builder.Services.AddScoped<ISecurityAuditWriter, SecurityAuditWriter>();
builder.Services.AddScoped<AdminBootstrapper>();
builder.Services.AddScoped<DemoDataSeeder>();
builder.Services.AddSingleton<SqlMigrationRunner>();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(
        ForumPolicies.ModerateForum,
        policy => policy.RequireRole(ForumRoles.Moderator, ForumRoles.Administrator))
    .AddPolicy(
        ForumPolicies.AdministerForum,
        policy => policy.RequireRole(ForumRoles.Administrator));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
        AuthenticationRateLimitPolicy.CreatePartition);
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        }

        await context.HttpContext.Response.WriteAsync(
            "Too many requests. Please try again later.",
            cancellationToken);
    };
});

var app = builder.Build();

if (args.Contains("--migrate", StringComparer.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var migrationRunner = scope.ServiceProvider.GetRequiredService<SqlMigrationRunner>();
    await migrationRunner.RunAsync(CancellationToken.None);
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    foreach (var roleName in ForumRoles.All)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            var role = new IdentityRole<Guid>(roleName)
            {
                Id = Guid.CreateVersion7(new DateTimeOffset(2020, 12, 3, 0, 0, 0, TimeSpan.Zero)),
            };
            var result = await roleManager.CreateAsync(role);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not create the '{roleName}' role: {string.Join(", ", result.Errors.Select(error => error.Description))}");
            }
        }
    }

    return;
}

if (args.Contains("--bootstrap-admin", StringComparer.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var bootstrapper = scope.ServiceProvider.GetRequiredService<AdminBootstrapper>();
    Environment.ExitCode = await bootstrapper.RunAsync(
        builder.Configuration["BootstrapAdmin:Email"],
        CancellationToken.None);
    return;
}

if (args.Contains("--seed-demo", StringComparer.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
    Environment.ExitCode = await seeder.RunAsync(CancellationToken.None);
    return;
}

await app.Services.GetRequiredService<S3MediaStorage>().EnsureBucketAsync(CancellationToken.None);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/dev"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await next(context);
    });
}

app.UseRequestLocalization();

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;
    if (!string.IsNullOrEmpty(path)
        && !Path.HasExtension(path)
        && path.Any(char.IsUpper))
    {
        context.Response.Redirect(
            $"{path.ToLowerInvariant()}{context.Request.QueryString}",
            permanent: true,
            preserveMethod: true);
        return;
    }

    await next(context);
});

app.UseRateLimiter();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});
app.MapGet("/api/search/suggestions", async (
    string? q,
    GetDiscussionsHandler discussions,
    HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
    {
        return Results.Ok(Array.Empty<object>());
    }

    var results = await discussions.SearchAsync(q, 5, context.RequestAborted);
    return Results.Ok(results.Select(topic => new
    {
        topic.Title,
        topic.CategoryName,
        topic.AuthorDisplayName,
        Url = TopicRoutePath(topic),
    }));
});
app.MapGet("/culture/set", (string culture, string? returnUrl, HttpContext context) =>
{
    var selectedCulture = culture is "en" or "ru" ? culture : "en";
    context.Response.Cookies.Append(
        CookieRequestCultureProvider.DefaultCookieName,
        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(selectedCulture)),
        new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            IsEssential = true,
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
        });

    var safeReturnUrl = !string.IsNullOrWhiteSpace(returnUrl)
        && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
        && returnUrl.StartsWith('/')
        && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            ? returnUrl
            : "/";
    return Results.LocalRedirect(safeReturnUrl);
});
app.MapGet("/api/topics/{topicId:guid}/activity", async (Guid topicId, IDiscussionRepository discussions, HttpContext context) =>
    Results.Ok(new { LastPostNumber = await discussions.GetLastPostNumberAsync(topicId, context.RequestAborted) }));
app.MapSpaceForumMedia();
app.MapPost("/api/markdown/preview", (MarkdownPreviewRequest input, ForumMarkdownRenderer markdown) =>
{
    if (input.Body.Length > Post.BodyMaxLength)
    {
        return Results.BadRequest();
    }

    return Results.Content(markdown.ToHtml(input.Body), "text/html; charset=utf-8");
}).RequireAuthorization();
app.MapPost("/actions/categories/{categoryId:guid}/delete", async (
    Guid categoryId,
    DeleteCategoryHandler deleteCategory,
    IAntiforgery antiforgery,
    HttpContext context) =>
{
    var memberId = context.User.GetMemberId();
    if (memberId is null)
    {
        return Results.Unauthorized();
    }

    await antiforgery.ValidateRequestAsync(context);
    var result = await deleteCategory.HandleAsync(memberId.Value, categoryId, context.RequestAborted);
    return result.Status switch
    {
        DeleteCategoryStatus.Deleted => Results.Redirect("/admin/categories?delete=deleted"),
        DeleteCategoryStatus.NotEmpty => Results.Redirect("/admin/categories?delete=not-empty"),
        DeleteCategoryStatus.NotFound => Results.Redirect("/admin/categories?delete=not-found"),
        _ => Results.Forbid(),
    };
}).RequireAuthorization(ForumPolicies.AdministerForum);
app.MapPost("/actions/topics/{topicId:guid}/close", async (
    Guid topicId,
    ModerateDiscussionHandler moderation,
    IDiscussionRepository repository,
    IAntiforgery antiforgery,
    HttpContext context) =>
{
    var memberId = context.User.GetMemberId();
    if (memberId is null)
    {
        return Results.Unauthorized();
    }

    await antiforgery.ValidateRequestAsync(context);
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var isClosed = bool.TryParse(form["isClosed"], out var requestedState) && requestedState;
    var returnPage = Math.Max(1, int.TryParse(form["returnPage"], out var requestedPage) ? requestedPage : 1);
    var result = await moderation.SetClosedAsync(memberId.Value, topicId, isClosed, context.RequestAborted);
    if (result.Status == ModerateDiscussionStatus.Forbidden)
    {
        return Results.Forbid();
    }

    var route = await repository.GetRouteAsync(topicId, context.RequestAborted);
    return route is null ? Results.Redirect("/") : Results.Redirect(TopicPageRoute(route, returnPage));
}).RequireAuthorization();
app.MapPost("/actions/topics/{topicId:guid}/delete", async (
    Guid topicId,
    ModerateDiscussionHandler moderation,
    IAntiforgery antiforgery,
    HttpContext context) =>
{
    var memberId = context.User.GetMemberId();
    if (memberId is null)
    {
        return Results.Unauthorized();
    }

    await antiforgery.ValidateRequestAsync(context);
    var result = await moderation.DeleteTopicAsync(memberId.Value, topicId, context.RequestAborted);
    return result.Status == ModerateDiscussionStatus.Forbidden ? Results.Forbid() : Results.Redirect("/");
}).RequireAuthorization();
app.MapPost("/actions/topics/{topicId:guid}/posts/{postId:guid}/delete", async (
    Guid topicId,
    Guid postId,
    ModerateDiscussionHandler moderation,
    IDiscussionRepository repository,
    IAntiforgery antiforgery,
    HttpContext context) =>
{
    var memberId = context.User.GetMemberId();
    if (memberId is null)
    {
        return Results.Unauthorized();
    }

    await antiforgery.ValidateRequestAsync(context);
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var returnPostNumber = int.TryParse(form["returnPostNumber"], out var requestedPostNumber)
        ? Math.Max(1, requestedPostNumber)
        : 1;
    var result = await moderation.DeletePostAsync(memberId.Value, postId, context.RequestAborted);
    if (result.Status == ModerateDiscussionStatus.Forbidden)
    {
        return Results.Forbid();
    }

    var route = await repository.GetRouteAsync(topicId, context.RequestAborted);
    return route is null ? Results.Redirect("/") : Results.Redirect(TopicPostRoute(route, returnPostNumber));
}).RequireAuthorization();
app.MapPost("/actions/topics/{topicId:guid}/posts/{postId:guid}/edit", async (Guid topicId, Guid postId, ForumFeatureService features, IDiscussionRepository discussions, IAntiforgery antiforgery, HttpContext context) =>
{
    var memberId = context.User.GetMemberId(); if (memberId is null) return Results.Unauthorized();
    await antiforgery.ValidateRequestAsync(context); var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var result = await features.EditPostAsync(memberId.Value, postId, form["body"].ToString(), context.RequestAborted);
    if (result.Status == ForumFeatureStatus.Forbidden) return Results.Forbid();
    var route = await discussions.GetRouteAsync(topicId, context.RequestAborted);
    return route is null ? Results.Redirect("/") : Results.Redirect(TopicPostRoute(route, Math.Max(1, int.TryParse(form["postNumber"], out var number) ? number : 1)));
}).RequireAuthorization();
app.MapPost("/actions/topics/{topicId:guid}/posts/{postId:guid}/visibility", async (Guid topicId, Guid postId, ForumFeatureService features, IDiscussionRepository discussions, IAntiforgery antiforgery, HttpContext context) =>
{
    var memberId = context.User.GetMemberId(); if (memberId is null) return Results.Unauthorized(); await antiforgery.ValidateRequestAsync(context);
    var form = await context.Request.ReadFormAsync(context.RequestAborted); var hidden = bool.TryParse(form["hidden"], out var requested) && requested;
    var result = await features.SetPostHiddenAsync(memberId.Value, postId, hidden, context.RequestAborted); if (result.Status == ForumFeatureStatus.Forbidden) return Results.Forbid();
    var route = await discussions.GetRouteAsync(topicId, context.RequestAborted); return route is null ? Results.Redirect("/") : Results.Redirect(TopicPostRoute(route, Math.Max(1, int.TryParse(form["postNumber"], out var number) ? number : 1)));
}).RequireAuthorization(ForumPolicies.AdministerForum);
app.MapPost("/actions/topics/{topicId:guid}/posts/{postId:guid}/react", async (Guid topicId, Guid postId, ForumFeatureService features, IDiscussionRepository discussions, IAntiforgery antiforgery, HttpContext context) =>
{
    var memberId = context.User.GetMemberId(); if (memberId is null) return Results.Unauthorized(); await antiforgery.ValidateRequestAsync(context); var form = await context.Request.ReadFormAsync(context.RequestAborted);
    await features.ToggleReactionAsync(memberId.Value, postId, form["reaction"].ToString(), context.RequestAborted); var route = await discussions.GetRouteAsync(topicId, context.RequestAborted);
    return route is null ? Results.Redirect("/") : Results.Redirect(TopicPostRoute(route, Math.Max(1, int.TryParse(form["postNumber"], out var number) ? number : 1)));
}).RequireAuthorization();
app.MapPost("/actions/topics/{topicId:guid}/posts/{postId:guid}/report", async (Guid topicId, Guid postId, ForumFeatureService features, IDiscussionRepository discussions, IAntiforgery antiforgery, HttpContext context) =>
{
    var memberId = context.User.GetMemberId(); if (memberId is null) return Results.Unauthorized(); await antiforgery.ValidateRequestAsync(context); var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var result = await features.ReportAsync(memberId.Value, postId, form["reason"].ToString(), form["details"].ToString(), context.RequestAborted);
    if (result.Status == ForumFeatureStatus.Forbidden) return Results.Forbid();
    if (!result.Succeeded) return Results.BadRequest(result.Error);
    var route = await discussions.GetRouteAsync(topicId, context.RequestAborted);
    return route is null ? Results.Redirect("/") : Results.Redirect(TopicPostRoute(route, Math.Max(1, int.TryParse(form["postNumber"], out var number) ? number : 1)));
}).RequireAuthorization();
app.MapPost("/actions/topics/{topicId:guid}/posts/{postId:guid}/bookmark", async (Guid topicId, Guid postId, IForumFeatureRepository features, IDiscussionRepository discussions, TimeProvider timeProvider, IAntiforgery antiforgery, HttpContext context) =>
{
    var memberId = context.User.GetMemberId(); if (memberId is null) return Results.Unauthorized(); await antiforgery.ValidateRequestAsync(context); var form = await context.Request.ReadFormAsync(context.RequestAborted);
    await features.ToggleBookmarkAsync(postId, memberId.Value, timeProvider.GetUtcNow(), context.RequestAborted); var route = await discussions.GetRouteAsync(topicId, context.RequestAborted);
    return route is null ? Results.Redirect("/") : Results.Redirect(TopicPostRoute(route, Math.Max(1, int.TryParse(form["postNumber"], out var number) ? number : 1)));
}).RequireAuthorization();
app.MapPost("/actions/topics/{topicId:guid}/subscription", async (Guid topicId, IForumFeatureRepository features, IDiscussionRepository discussions, TimeProvider timeProvider, IAntiforgery antiforgery, HttpContext context) =>
{
    var memberId = context.User.GetMemberId(); if (memberId is null) return Results.Unauthorized(); await antiforgery.ValidateRequestAsync(context); var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var state = form["state"].ToString() switch { "following" => "Following", "ignoring" => "Ignoring", _ => null };
    await features.SetSubscriptionAsync(topicId, memberId.Value, state, Math.Max(0, int.TryParse(form["lastPostNumber"], out var number) ? number : 0), timeProvider.GetUtcNow(), context.RequestAborted);
    var returnPage = Math.Max(1, int.TryParse(form["returnPage"], out var requestedPage) ? requestedPage : 1);
    var route = await discussions.GetRouteAsync(topicId, context.RequestAborted); return route is null ? Results.Redirect("/") : Results.Redirect(TopicPageRoute(route, returnPage));
}).RequireAuthorization();
app.MapPost("/actions/notifications/read", async (IForumFeatureRepository features, TimeProvider timeProvider, IAntiforgery antiforgery, HttpContext context) =>
{
    var memberId = context.User.GetMemberId(); if (memberId is null) return Results.Unauthorized(); await antiforgery.ValidateRequestAsync(context);
    await features.MarkNotificationsReadAsync(memberId.Value, timeProvider.GetUtcNow(), context.RequestAborted); return Results.Redirect("/notifications");
}).RequireAuthorization();
app.MapPost("/actions/reports/{reportId:guid}/status", async (Guid reportId, ForumFeatureService features, IAntiforgery antiforgery, HttpContext context) =>
{
    var memberId = context.User.GetMemberId(); if (memberId is null) return Results.Unauthorized(); await antiforgery.ValidateRequestAsync(context); var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var result = await features.ResolveReportAsync(memberId.Value, reportId, form["status"].ToString(), context.RequestAborted); return result.Status == ForumFeatureStatus.Forbidden ? Results.Forbid() : Results.Redirect("/admin/reports");
}).RequireAuthorization(ForumPolicies.AdministerForum);
app.MapPost("/actions/tags/{tagId:guid}/delete", async (Guid tagId, IForumFeatureRepository features, IAntiforgery antiforgery, HttpContext context) =>
{
    await antiforgery.ValidateRequestAsync(context); await features.DeleteTagAsync(tagId, context.RequestAborted); return Results.Redirect("/admin/tags");
}).RequireAuthorization(ForumPolicies.AdministerForum);
app.MapPost("/actions/users/{userId:guid}/suspend", async (Guid userId, UserManager<ApplicationUser> users, GetMemberProfileHandler profiles, IAntiforgery antiforgery, HttpContext context) =>
{
    await antiforgery.ValidateRequestAsync(context); var actorId=context.User.GetMemberId(); if(actorId is null)return Results.Unauthorized(); var actor=await profiles.ByIdAsync(actorId.Value,context.RequestAborted); if(actor?.Login!="admin")return Results.Forbid();
    var targetProfile=await profiles.ByIdAsync(userId,context.RequestAborted); if(targetProfile?.Login=="admin")return Results.Forbid(); var user=await users.FindByIdAsync(userId.ToString()); if(user is null)return Results.NotFound(); var form=await context.Request.ReadFormAsync(context.RequestAborted);
    var suspended=bool.TryParse(form["suspended"],out var requested)&&requested; await users.SetLockoutEndDateAsync(user,suspended?DateTimeOffset.UtcNow.AddYears(100):null); return Results.Redirect("/admin/users");
}).RequireAuthorization(ForumPolicies.AdministerForum);
app.MapPost("/actions/users/{userId:guid}/role", async (Guid userId, UserManager<ApplicationUser> users, GetMemberProfileHandler profiles, IAntiforgery antiforgery, HttpContext context) =>
{
    await antiforgery.ValidateRequestAsync(context); var actorId=context.User.GetMemberId(); if(actorId is null)return Results.Unauthorized(); var actor=await profiles.ByIdAsync(actorId.Value,context.RequestAborted); if(actor?.Login!="admin")return Results.Forbid();
    var targetProfile=await profiles.ByIdAsync(userId,context.RequestAborted); if(targetProfile?.Login=="admin")return Results.Forbid(); var user=await users.FindByIdAsync(userId.ToString()); if(user is null)return Results.NotFound(); var form=await context.Request.ReadFormAsync(context.RequestAborted); var role=form["role"].ToString(); if(role is not (ForumRoles.Member or ForumRoles.Moderator or ForumRoles.Administrator))return Results.BadRequest();
    var current=await users.GetRolesAsync(user); await users.RemoveFromRolesAsync(user,current.Where(item=>item is ForumRoles.Member or ForumRoles.Moderator or ForumRoles.Administrator)); await users.AddToRoleAsync(user,ForumRoles.Member); if(role!=ForumRoles.Member)await users.AddToRoleAsync(user,role); return Results.Redirect("/admin/users");
}).RequireAuthorization(ForumPolicies.AdministerForum);
app.MapPost("/actions/groups/{roleName}/permissions", async (string roleName, RoleManager<IdentityRole<Guid>> roles, GetMemberProfileHandler profiles, IAntiforgery antiforgery, HttpContext context) =>
{
    await antiforgery.ValidateRequestAsync(context); var actorId=context.User.GetMemberId(); if(actorId is null)return Results.Unauthorized(); var actor=await profiles.ByIdAsync(actorId.Value,context.RequestAborted); if(actor?.Login!="admin")return Results.Forbid();
    var role=await roles.FindByNameAsync(roleName); if(role is null)return Results.NotFound(); var form=await context.Request.ReadFormAsync(context.RequestAborted); var selected=form["permissions"].ToHashSet(StringComparer.Ordinal); var claims=await roles.GetClaimsAsync(role);
    foreach(var claim in claims.Where(item=>item.Type==ForumPermissions.ClaimType&&!selected.Contains(item.Value)))await roles.RemoveClaimAsync(role,claim);
    foreach(var permission in selected.Where(value=>!string.IsNullOrWhiteSpace(value)).Select(value=>value!).Where(ForumPermissions.All.Contains).Except(claims.Where(item=>item.Type==ForumPermissions.ClaimType).Select(item=>item.Value)))await roles.AddClaimAsync(role,new Claim(ForumPermissions.ClaimType,permission));
    return Results.Redirect("/admin/permissions");
}).RequireAuthorization(ForumPolicies.AdministerForum);
app.MapPost("/actions/messages/create", async (MessagingService messaging, GetMemberProfileHandler profiles, IAntiforgery antiforgery, HttpContext context) =>
{
    var actor=context.User.GetMemberId();if(actor is null)return Results.Unauthorized();await antiforgery.ValidateRequestAsync(context);var form=await context.Request.ReadFormAsync(context.RequestAborted);var recipient=await profiles.ByLoginAsync(form["recipient"].ToString().ToLowerInvariant(),context.RequestAborted);if(recipient is null)return Results.Redirect("/messages/new?error=recipient");var id=await messaging.CreateAsync(actor.Value,recipient.Id,form["subject"].ToString(),form["body"].ToString(),context.RequestAborted);return id is Guid conversationId?Results.Redirect($"/messages/{conversationId}"):Results.Redirect("/messages/new?error=invalid");
}).RequireAuthorization();
app.MapPost("/actions/messages/{conversationId:guid}/send", async (Guid conversationId, MessagingService messaging, IAntiforgery antiforgery, HttpContext context) =>
{
    var actor=context.User.GetMemberId();if(actor is null)return Results.Unauthorized();await antiforgery.ValidateRequestAsync(context);var form=await context.Request.ReadFormAsync(context.RequestAborted);await messaging.SendAsync(conversationId,actor.Value,form["body"].ToString(),context.RequestAborted);return Results.Redirect($"/messages/{conversationId}");
}).RequireAuthorization();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.Run();

static string TopicRoutePath(TopicListItem topic) => $"/d/{topic.Number}-{topic.Slug}";

static string TopicPageRoute(TopicRoute route, int page) =>
    page <= 1 ? route.Path : $"{route.Path}?page={page}";

static string TopicPostRoute(TopicRoute route, int postNumber) =>
    $"{TopicPageRoute(route, TopicPagination.PageForPost(postNumber))}#post-{postNumber}";

public partial class Program;

internal sealed record MarkdownPreviewRequest(string Body);
