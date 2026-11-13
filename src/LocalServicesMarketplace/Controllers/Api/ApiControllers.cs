using LocalServicesMarketplace.Infrastructure;
using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.Models.Api;
using LocalServicesMarketplace.Services.Interfaces;
using LocalServicesMarketplace.ViewModels;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LocalServicesMarketplace.Controllers.Api;

/// <summary>
/// Base class for the REST API: attribute routing, automatic model validation (400 problem+json),
/// JWT bearer authentication only, and no antiforgery (no cookies involved).
/// </summary>
[ApiController]
[IgnoreAntiforgeryToken]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public abstract class ApiControllerBase : ControllerBase
{
    protected int UserId => User.GetUserId();

    protected ActionResult Failure(ServiceResult result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => Problem(title: "Not found", detail: result.Error, statusCode: StatusCodes.Status404NotFound),
        ServiceErrorKind.Forbidden => Problem(title: "Forbidden", detail: result.Error, statusCode: StatusCodes.Status403Forbidden),
        ServiceErrorKind.Conflict => Problem(title: "Conflict", detail: result.Error, statusCode: StatusCodes.Status409Conflict),
        _ => Problem(title: "Request could not be processed", detail: result.Error, statusCode: StatusCodes.Status400BadRequest)
    };
}

// ====================================================================== /api/auth

[Route("api/auth")]
public class AuthApiController(
    UserManager<User> userManager,
    SignInManager<User> signInManager,
    IAccountService accounts,
    ITokenService tokens,
    TimeProvider clock) : ApiControllerBase
{
    /// <summary>POST /api/auth/login - exchange e-mail + password for a JWT.</summary>
    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("auth")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null) return Problem(title: "Invalid credentials", statusCode: StatusCodes.Status401Unauthorized);
        if (user.IsBlocked) return Problem(title: "This account has been blocked.", statusCode: StatusCodes.Status403Forbidden);

        var check = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (check.IsLockedOut) return Problem(title: "Account temporarily locked after failed attempts.", statusCode: StatusCodes.Status423Locked);
        if (!check.Succeeded) return Problem(title: "Invalid credentials", statusCode: StatusCodes.Status401Unauthorized);

        user.LastLoginAtUtc = clock.GetUtcNow().UtcDateTime;
        await userManager.UpdateAsync(user);
        return Ok(await IssueAsync(user));
    }

    /// <summary>POST /api/auth/register - create a Customer or Provider account and return a JWT.</summary>
    [HttpPost("register"), AllowAnonymous, EnableRateLimiting("auth")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        ServiceResult<User> result;
        if (request.Role == AppRoles.Provider)
        {
            if (request.CategoryId is null) return Problem(title: "CategoryId is required for providers.", statusCode: StatusCodes.Status400BadRequest);
            result = await accounts.RegisterProviderAsync(new RegisterProviderViewModel
            {
                FullName = request.FullName, Email = request.Email, PhoneNumber = request.PhoneNumber, City = request.City,
                Password = request.Password, ConfirmPassword = request.Password, AcceptTerms = true,
                CategoryId = request.CategoryId, ExperienceYears = request.ExperienceYears
            }, ct);
        }
        else
        {
            result = await accounts.RegisterCustomerAsync(new RegisterCustomerViewModel
            {
                FullName = request.FullName, Email = request.Email, PhoneNumber = request.PhoneNumber, City = request.City,
                Password = request.Password, ConfirmPassword = request.Password, AcceptTerms = true
            }, ct);
        }
        if (!result.Succeeded) return Failure(result);
        return CreatedAtAction(nameof(Me), null, await IssueAsync(result.Value!));
    }

    /// <summary>GET /api/auth/me - the authenticated user.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me()
    {
        var user = await userManager.FindByIdAsync(UserId.ToString());
        if (user is null) return NotFound();
        return ToDto(user, await accounts.GetPrimaryRoleAsync(user));
    }

    private async Task<AuthResponse> IssueAsync(User user)
    {
        var roles = await userManager.GetRolesAsync(user);
        var (token, expires) = tokens.CreateToken(user, roles);
        return new AuthResponse(token, expires, ToDto(user, await accounts.GetPrimaryRoleAsync(user)));
    }

    private static UserDto ToDto(User u, string role) => new(u.Id, u.FullName, u.Email ?? "", role, u.City, u.PhoneNumber);
}

// ====================================================================== /api/categories

[Route("api/categories")]
public class CategoriesApiController(ICategoryService categories) : ApiControllerBase
{
    [HttpGet, AllowAnonymous]
    public async Task<IEnumerable<CategoryDto>> Get(CancellationToken ct) =>
        (await categories.GetActiveTilesAsync(ct)).Select(c => new CategoryDto(c.Id, c.Name, c.Slug, c.Description, c.ProviderCount, c.StartingPrice));
}

// ====================================================================== /api/services

[Route("api/services")]
public class ServicesApiController(IProviderService providers, IAdminService admin) : ApiControllerBase
{
    /// <summary>GET /api/services?q=&amp;category=&amp;city=&amp;minPrice=&amp;maxPrice=&amp;page=</summary>
    [HttpGet, AllowAnonymous]
    public Task<PagedResult<ServiceDto>> Get([FromQuery] ServiceSearchQuery query, CancellationToken ct) =>
        providers.SearchServicesAsync(query, ct);

    [HttpGet("{id:int}", Name = "GetService"), AllowAnonymous]
    public async Task<ActionResult<ServiceDto>> GetById(int id, CancellationToken ct)
    {
        var service = await providers.GetServiceAsync(id, User.TryGetUserId(), User.IsInRole(AppRoles.Admin), ct);
        return service is null ? NotFound() : service;
    }

    /// <summary>POST /api/services - a provider adds a service to their own profile.</summary>
    [HttpPost, Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = AppRoles.Provider)]
    [ProducesResponseType<ServiceDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ServiceDto>> Create(ServiceInputModel input, CancellationToken ct)
    {
        var provider = await providers.GetByUserIdAsync(UserId, ct);
        if (provider is null) return Forbid();
        input.Id = null;
        var result = await providers.SaveServiceAsync(provider.Id, input, ct);
        if (!result.Succeeded) return Failure(result);
        var dto = await providers.GetServiceAsync(result.Value!.Id, UserId, false, ct);
        return CreatedAtRoute("GetService", new { id = result.Value.Id }, dto);
    }

    /// <summary>PUT /api/services/{id} - the owning provider updates a service.</summary>
    [HttpPut("{id:int}"), Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = AppRoles.Provider)]
    public async Task<ActionResult<ServiceDto>> Update(int id, ServiceInputModel input, CancellationToken ct)
    {
        var provider = await providers.GetByUserIdAsync(UserId, ct);
        if (provider is null) return Forbid();
        input.Id = id;
        var result = await providers.SaveServiceAsync(provider.Id, input, ct);
        if (!result.Succeeded) return Failure(result);
        return await providers.GetServiceAsync(id, UserId, false, ct) is { } dto ? dto : NotFound();
    }

    /// <summary>DELETE /api/services/{id} - owning provider (soft-deletes if booked) or admin.</summary>
    [HttpDelete("{id:int}"), Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = AppRoles.Provider + "," + AppRoles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        ServiceResult result;
        if (User.IsInRole(AppRoles.Admin))
        {
            result = await admin.DeleteServiceAsync(UserId, id, ct);
        }
        else
        {
            var provider = await providers.GetByUserIdAsync(UserId, ct);
            if (provider is null) return Forbid();
            result = await providers.DeleteServiceAsync(provider.Id, id, ct);
        }
        return result.Succeeded ? NoContent() : Failure(result);
    }
}

// ====================================================================== /api/providers

[Route("api/providers")]
public class ProvidersApiController(IProviderService providers, IBookingService bookings, ITimeZoneService tz) : ApiControllerBase
{
    /// <summary>GET /api/providers?q=&amp;category=&amp;city=&amp;minPrice=&amp;maxPrice=&amp;minRating=&amp;availableOn=yyyy-MM-dd&amp;sort=</summary>
    [HttpGet, AllowAnonymous]
    public Task<PagedResult<ProviderCardVm>> Get([FromQuery] ProviderSearchQuery query, CancellationToken ct) =>
        providers.SearchAsync(query, ct);

    [HttpGet("{id:int}"), AllowAnonymous]
    public async Task<ActionResult<ProviderDetailDto>> GetById(int id, CancellationToken ct)
    {
        var d = await providers.GetDetailsAsync(id, null, false, ct);
        if (d is null) return NotFound();
        var p = d.Provider;
        return new ProviderDetailDto(p.Id, p.User.FullName, p.Headline, p.Bio, p.Category.Name, p.City, p.ExperienceYears,
            p.SkillList, p.ServiceAreaList, p.AverageRating, p.TotalReviews, d.CompletedJobs, p.IsApproved, p.IsAcceptingBookings,
            d.Services.Select(s => new ServiceDto
            {
                Id = s.Id, Title = s.Title, Description = s.Description, Price = s.Price, PriceUnit = s.PriceUnit,
                DurationMinutes = s.DurationMinutes, IsActive = s.IsActive, CategoryId = s.CategoryId, CategoryName = s.Category.Name,
                ProviderId = p.Id, ProviderName = p.User.FullName, City = p.City, ProviderRating = p.AverageRating
            }));
    }

    /// <summary>GET /api/providers/{id}/slots?days=7 - bookable slots (server-side availability check).</summary>
    [HttpGet("{id:int}/slots"), AllowAnonymous]
    public async Task<ActionResult<IEnumerable<SlotDto>>> Slots(int id, int days = 7, CancellationToken ct = default)
    {
        if (await providers.GetDetailsAsync(id, null, false, ct) is null) return NotFound();
        var slotDays = await bookings.GetAvailableSlotsAsync(id, Math.Clamp(days, 1, 30), null, ct);
        return Ok(slotDays.SelectMany(d => d.Slots).Select(s =>
            new SlotDto(s.Id, s.StartUtc, s.EndUtc, tz.Format(s.StartUtc, "yyyy-MM-dd"), tz.Format(s.StartUtc, "HH:mm"))));
    }
}

// ====================================================================== /api/bookings

[Route("api/bookings")]
public class BookingsApiController(IBookingService bookings, ITimeZoneService tz) : ApiControllerBase
{
    /// <summary>GET /api/bookings?status=upcoming|history|pending|... - scoped to the caller's role.</summary>
    [HttpGet]
    public Task<PagedResult<BookingListItemVm>> Get([FromQuery] BookingQuery query, CancellationToken ct) =>
        bookings.QueryAsync(Scope, Scope == BookingScope.Admin ? null : UserId, query, ct);

    [HttpGet("{id:int}", Name = "GetBooking")]
    public async Task<ActionResult<BookingDto>> GetById(int id, CancellationToken ct)
    {
        var result = await bookings.GetDetailsAsync(id, UserId, User.IsInRole(AppRoles.Admin), ct);
        return result.Succeeded ? ToDto(result.Value!.Booking) : Failure(result);
    }

    /// <summary>POST /api/bookings - customer books a slot (runs the CreateBooking stored procedure).</summary>
    [HttpPost, Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = AppRoles.Customer)]
    [ProducesResponseType<BookingDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookingDto>> Create(CreateBookingRequest request, CancellationToken ct)
    {
        var result = await bookings.CreateAsync(UserId,
            new CreateBookingCommand(request.ProviderId, request.ServiceId, request.TimeSlotId, request.Address, request.City, request.Notes), ct);
        if (!result.Succeeded) return Failure(result);
        var details = await bookings.GetDetailsAsync(result.Value, UserId, false, ct);
        return CreatedAtRoute("GetBooking", new { id = result.Value }, ToDto(details.Value!.Booking));
    }

    /// <summary>PUT /api/bookings/{id} - { action: accept | reject | complete | reschedule, timeSlotId, reason }</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<BookingDto>> Update(int id, UpdateBookingRequest request, CancellationToken ct)
    {
        var result = request.Action switch
        {
            "accept" when User.IsInRole(AppRoles.Provider) => await bookings.AcceptAsync(UserId, id, ct),
            "reject" when User.IsInRole(AppRoles.Provider) => await bookings.RejectAsync(UserId, id, request.Reason, ct),
            "complete" when User.IsInRole(AppRoles.Provider) => await bookings.CompleteAsync(UserId, id, ct),
            "reschedule" when User.IsInRole(AppRoles.Customer) => request.TimeSlotId is int slot
                ? await bookings.RescheduleAsync(UserId, id, slot, ct)
                : ServiceResult.Fail("timeSlotId is required to reschedule."),
            _ => ServiceResult.Forbidden($"Your role cannot perform '{request.Action}'.")
        };
        if (!result.Succeeded) return Failure(result);
        return await GetById(id, ct);
    }

    /// <summary>DELETE /api/bookings/{id}?reason= - cancel (customer within policy, or admin).</summary>
    [HttpDelete("{id:int}"), Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = AppRoles.Customer + "," + AppRoles.Admin)]
    public async Task<ActionResult<BookingDto>> Cancel(int id, [FromQuery] string? reason, CancellationToken ct)
    {
        var result = await bookings.CancelAsync(UserId, id, reason, User.IsInRole(AppRoles.Admin), ct);
        if (!result.Succeeded) return Failure(result);
        return await GetById(id, ct);
    }

    private BookingScope Scope =>
        User.IsInRole(AppRoles.Admin) ? BookingScope.Admin : User.IsInRole(AppRoles.Provider) ? BookingScope.Provider : BookingScope.Customer;

    private BookingDto ToDto(Booking b) => new(
        b.Id, b.Reference, b.Status, b.CustomerId, b.Customer.FullName, b.ProviderId, b.Provider.User.FullName,
        b.Service.Title, b.ScheduledStartUtc, b.ScheduledEndUtc, tz.Format(b.ScheduledStartUtc, "yyyy-MM-dd HH:mm"),
        b.Price, b.City, b.Address, b.StatusReason, b.Review is not null, b.CreatedAtUtc);
}

// ====================================================================== /api/reviews

[Route("api/reviews")]
public class ReviewsApiController(IReviewService reviews) : ApiControllerBase
{
    /// <summary>GET /api/reviews/{providerId}?page=1</summary>
    [HttpGet("{providerId:int}"), AllowAnonymous]
    public Task<PagedResult<ReviewVm>> Get(int providerId, int page = 1, CancellationToken ct = default) =>
        reviews.GetProviderReviewsAsync(providerId, page, 10, ct);

    /// <summary>POST /api/reviews - only for the customer's own COMPLETED, not-yet-reviewed booking.</summary>
    [HttpPost, Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = AppRoles.Customer)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateReviewRequest request, CancellationToken ct)
    {
        var result = await reviews.SubmitAsync(UserId, request.BookingId, request.Rating, request.Comment, ct);
        if (!result.Succeeded) return Failure(result);
        return StatusCode(StatusCodes.Status201Created, new { id = result.Value, request.BookingId, request.Rating });
    }
}
