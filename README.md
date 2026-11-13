# Local Services Marketplace (Kaarigar)

A full-stack **ASP.NET Core MVC + Web API** application that connects customers with verified local
professionals (plumbers, electricians, tutors, cleaners, beauticians and more). Customers search, compare
and book time slots. Providers manage their profile, availability, bookings and earnings. Admins verify
providers, moderate the marketplace and read reports.

> **University demonstration project.** It is complete and works end to end, but it is not hardened for
> production. All people, bookings and reviews in the seed data are fictional and flagged as sample data.

| | |
|---|---|
| **Stack** | .NET 10 (LTS), ASP.NET Core MVC + Web API, EF Core 10, ASP.NET Core Identity, SQL Server 2022 |
| **Frontend** | Razor views, Bootstrap 5.3, Bootstrap Icons, Chart.js 4, vanilla JS (all served locally) |
| **Runs in** | Docker Compose: `db` (SQL Server), `app` (web), `mailpit` (SMTP catcher), `newman` (API tests) |

---

## Quick start (Docker, recommended)

Prerequisites: Docker Desktop / Docker Engine with Compose v2. Nothing else is needed.

```bash
cp .env.example .env          # demo values; change passwords if you like
docker compose up --build -d  # first build takes a few minutes
```

| What | URL |
|---|---|
| Website | http://localhost:5080 |
| Mailpit (password-reset & contact e-mails) | http://localhost:8025 |
| OpenAPI document (import into Postman) | http://localhost:5080/openapi/v1.json |
| Health check | http://localhost:5080/health |
| SQL Server (SSMS / Azure Data Studio) | `127.0.0.1,14333`, user `sa`, password from `.env` |

On start-up the app **applies EF Core migrations** (tables, indexes, constraints, SQL views and stored
procedures) and, in Development, **seeds demo data**. The sign-in details are in
[`docs/DEMO_CREDENTIALS.md`](docs/DEMO_CREDENTIALS.md).

> **Before presenting:** run `docker compose down -v && docker compose up -d` for a clean demo database.
> Each restart also tops up two weeks of free time slots for the sample providers, and two waiting requests
> for the demo provider, so an older database stays usable.

```bash
docker compose logs -f app                    # follow application logs
docker compose --profile test run --rm newman # run the Postman API test suite against the stack
docker compose down                           # stop (data kept in volumes)
docker compose down -v                        # stop and wipe the database + uploads (fresh demo)
```

## Running without Docker for the app (optional)

Keep only the database in Docker and run the web app with the .NET SDK (10.0.100+):

```bash
docker compose up -d db mailpit
dotnet tool restore
dotnet run --project src/LocalServicesMarketplace      # http://localhost:5058
```

`appsettings.Development.json` already points at `127.0.0.1,14333` with the `.env.example` password.
To manage migrations by hand: `dotnet ef migrations list -p src/LocalServicesMarketplace`.

---

## Features

### Customer
Register and sign in. Browse categories, search, and filter providers by city, price range, rating and
available date (query-string filters). Read profiles with verification badge, experience, skills, prices,
ratings and reviews. Book a service in a free time slot, then follow its status. Cancel or reschedule within
policy. View upcoming bookings and history, review completed jobs, get notifications, raise complaints, and
manage the profile and password.

### Service provider
Register as a professional and complete the profile (photo, headline, bio, skills, experience, city, service
areas). Manage priced services (CRUD) and publish availability, either in bulk (date range × weekdays ×
hours) or removing single slots or whole days. Upload verification documents to **private** storage and
submit for review. Accept or reject requests, mark jobs completed, and see upcoming and completed jobs,
earnings (chart + monthly table), reviews and notifications. Providers are **invisible and unbookable until
an admin approves them**.

### Admin
- **Dashboard:** live KPIs, a monthly bookings chart (SQL view), status distribution, top-rated providers (SQL view), pending verifications, recent users, bookings and platform activity.
- **People:** manage users (search, filter, block/unblock with reason) and providers.
- **Verification queue:** open private documents, approve or reject each document and the provider.
- **Marketplace:** category CRUD, service moderation (hide/delete), and every booking with filters (an admin can cancel).
- **Complaints:** reply and resolve.
- **Reports:** stored-procedure monthly report with CSV export, provider performance (SQL view), revenue by category and top customers (LINQ joins).

### Booking workflow (the core)
`Pending → Confirmed → Completed`, or `Pending → Rejected`, or `Pending/Confirmed → Cancelled`.

| Rule | Where it is enforced |
|---|---|
| A slot can't hold two active bookings | Filtered unique index `UX_Bookings_ActiveSlot` (`Status IN (0,1)`) + `dbo.CreateBooking` locking (`UPDLOCK, HOLDLOCK`) |
| Availability is checked on the server | `dbo.CreateBooking` re-validates provider, service, slot, lead time and conflicts inside one transaction |
| A customer can't double-book themselves | Overlap check in `dbo.CreateBooking` and in reschedule |
| No cancellation within 2 hours | `BookingPolicy.CanCustomerCancel` (configurable `Marketplace:CancellationWindowHours`) |
| Rescheduling rechecks availability | `BookingService.RescheduleAsync` runs in a **SERIALIZABLE** transaction; the provider must re-accept |
| Only the assigned provider can accept, reject or complete | `BookingService` ownership checks + optimistic concurrency (`RowVersion`) |
| Only the booking's customer can cancel (admins can too) | `BookingService.CancelAsync` |
| Reviews only after completion, one per booking | `BookingPolicy.CanReview` + unique index on `Reviews.BookingId` |
| Unapproved or blocked providers can't be booked | `dbo.CreateBooking` + search filters |
| Stale requests expire | `BookingExpiryService` (hosted `BackgroundService`) auto-cancels pending requests whose time passed |

Platform commission is 10% (`Marketplace:CommissionRate`). Each booking snapshots its price, fee and the
provider's earning in `decimal(10,2)` columns.

---

## How the .NET syllabus is covered

| Topic | Where to look |
|---|---|
| MVC pattern, Razor views, layouts, partial views | `Controllers/`, `Views/Shared/_Layout.cshtml`, `_DashboardLayout.cshtml`, `_BookingTable.cshtml`, `_SlotPicker.cshtml` |
| Tag Helpers & View Components (custom) | `Infrastructure/TagHelpers.cs` (`<avatar>`, `<status-badge>`, `<stars>`, `<pager>`), `NotificationBellViewComponent` |
| ViewData / ViewBag / TempData | page titles (`ViewData["Title"]`), dropdown lists (`ViewBag.Categories`), flash messages (`TempData["Success"]`) |
| Session state | "Recently viewed" providers on `/Services` (`ServicesController`) |
| Query-string filtering | `/Services?q=&category=&city=&minPrice=&maxPrice=&minRating=&availableOn=&sort=&page=` |
| Model validation | Data annotations, `IValidatableObject` (`AddSlotsInputModel`), custom `[MustBeTrue]`, jQuery unobtrusive validation |
| Dependency injection | `Program.cs`: services registered by interface (`Services/Interfaces/IServices.cs`); singleton, scoped and transient lifetimes |
| Options pattern | `Infrastructure/Options.cs` bound and validated at start-up (`ValidateOnStart`) |
| Custom middleware | `Middleware/ExceptionHandlingMiddleware.cs`, `RequestTimingMiddleware.cs`, `AccountStatusMiddleware.cs` |
| Built-in middleware | static files, routing, authentication, authorization, session, rate limiting, request localization, status-code pages, HSTS, health checks |
| Authentication & authorization | ASP.NET Core Identity (hashed passwords, lockout), role-based `[Authorize(Roles=…)]`, cookie for MVC + **JWT bearer** for the API via a policy scheme |
| Routing | Convention-based (`{controller}/{action}/{id?}`, `providers/{id}`) and attribute-based (`/api/...`, `/become-a-provider`) |
| Web API | `Controllers/Api/ApiControllers.cs`: REST verbs, 200/201/204/400/401/403/404/409, `ProblemDetails`, `CreatedAtRoute` |
| EF Core Code First | `Data/ApplicationDbContext.cs`, Fluent API configs, `Data/Migrations/` |
| SQL views, stored procedures, joins | see the next section |
| Logging & error handling | `ILogger<T>` everywhere, structured logs, branded error pages, `ProblemDetails` for the API |
| Background services | `Services/BookingExpiryService.cs` (`BackgroundService` + `PeriodicTimer`) |
| Caching | `IMemoryCache` for category tiles and account-status checks |
| Async/await, LINQ | throughout the service layer |
| Testing | `tests/` (xUnit + Testcontainers SQL Server), Postman/newman, `scripts/smoke-pages.sh` |

### SQL Server objects (created by migration `AddReportingViewsAndProcedures`)

| Object | Type | Used by |
|---|---|---|
| `dbo.TopRatedProviders` | View | Home page "Highly rated professionals", admin dashboard |
| `dbo.MonthlyBookingStatistics` | View | Admin dashboard monthly chart + table |
| `dbo.ProviderPerformance` | View | Provider dashboard KPIs, admin provider review, Reports |
| `dbo.CreateBooking` | Stored procedure | **Every booking** (MVC and API), via `ExecuteSqlRawAsync` with `OUTPUT` parameters |
| `dbo.CalculateProviderAverageRating` | Stored procedure | After every review (`ReviewService`), and by the seeder |
| `dbo.GetMonthlyBookingReport` | Stored procedure | Admin Reports page + CSV export |

Joins: booking lists project across Bookings, Users, Providers, Services, Categories and Reviews
(`BookingService.Project`). The admin user list and reports use explicit LINQ `join … into` / `group by`
queries (`AdminService.GetUsersAsync`, `ReportService.GetReportsAsync`). The database design is described in
[`docs/DATABASE.md`](docs/DATABASE.md).

---

## Security

- ASP.NET Core Identity password hashing (PBKDF2). There are no plaintext passwords; lockout applies after 5 failures.
- Role-based authorization on every dashboard and API endpoint. Public registration can only create Customer or Provider accounts.
- Anti-forgery tokens on every MVC POST (global `AutoValidateAntiforgeryToken`). The API uses JWT only and opts out.
- Booking, review and cancellation rules are enforced on the server and in SQL, never only in JavaScript.
- EF Core parameterised queries everywhere. Stored procedures are called with `SqlParameter`s.
- Uploads are checked for size, extension **and magic bytes**, stored under random names, and capped by `RequestSizeLimit`.
- Verification documents live in `storage/private` (outside `wwwroot`). Only the owning provider or an admin can stream them.
- Blocking a user rotates their security stamp, and `AccountStatusMiddleware` signs them out (or returns 403 on the API) within seconds.
- The rate limiter covers login, registration, password reset and contact. Password-reset responses don't reveal whether an account exists.
- Security headers: `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy`. Data Protection keys persist on a volume.

## Tests

The app, database, mail server and Postman (newman) tests all run in containers. `dotnet test` and the
smoke script run on the host: they need the .NET 10 SDK, Docker (for Testcontainers) and `curl`.

```bash
dotnet test                                    # 24 tests: unit + integration (needs Docker for SQL Server)
docker compose --profile test run --rm newman  # 42 API requests / 63 assertions against the running stack
scripts/smoke-pages.sh http://localhost:5080   # signs in as each role and requests ~55 pages
```

The integration tests start a throw-away SQL Server container with Testcontainers, apply the real
migrations, and cover:
- 8 customers racing for one slot (exactly one wins)
- the price/fee snapshot
- customer self-overlap
- unapproved providers
- the 2-hour rule
- slot release on cancel
- provider ownership
- review rules and the rating stored procedure
- reschedule availability

The Postman collection is in `postman/`. Import it, set `baseUrl`, and run the folders in order.

## Project structure

```
LocalServicesMarketplace/
├── compose.yaml                 # db + app + mailpit (+ newman test profile)
├── .env.example                 # demo configuration (copy to .env)
├── src/LocalServicesMarketplace/
│   ├── Controllers/             # MVC controllers (Home, Account, Services, Booking, Customer, Provider, Admin, ...)
│   │   └── Api/                 # REST API controllers (Auth, Services, Providers, Bookings, Reviews, Categories)
│   ├── Data/                    # ApplicationDbContext, Fluent configurations, Migrations, Seed
│   ├── Infrastructure/          # options, tag helpers, time zone, helpers, health check
│   ├── Middleware/              # custom middleware
│   ├── Models/                  # entities, enums, reporting (view/SP) types, API DTOs
│   ├── Services/                # business logic (+ Interfaces/)
│   ├── ViewModels/
│   ├── Views/
│   ├── wwwroot/                 # css, js, images, vendored libraries
│   └── Dockerfile
├── tests/LocalServicesMarketplace.Tests/
├── postman/                     # Postman collection
├── scripts/smoke-pages.sh
└── docs/                        # credentials, database design
```

## Configuration

Every setting can be overridden with environment variables (`Section__Key`). The main ones:

| Setting | Default | Meaning |
|---|---|---|
| `Marketplace:CommissionRate` | `0.10` | Platform fee |
| `Marketplace:CancellationWindowHours` | `2` | No cancel/reschedule inside this window |
| `Marketplace:MinBookingLeadMinutes` | `60` | Slots starting sooner can't be booked |
| `Marketplace:AllowEarlyCompletion` | `true` | Lets a provider complete a job before its start time, so a live demo doesn't have to wait. Set `false` for real-world behaviour |
| `Marketplace:TimeZone` | `Asia/Kolkata` | Display time zone. Everything is stored in UTC |
| `Seed:Enabled` / `Seed:SampleData` | `true` in compose | Development-only seeding |
| `Seed:RandomSeed` (`SEED_RANDOM_SEED`) | `0` | `0` = new random sample data per fresh database; set the logged seed to reproduce a dataset |
| `Jwt:Key` | from `.env` | API token signing key (32+ chars) |

## Troubleshooting

| Problem | Fix |
|---|---|
| `db` never becomes healthy | SQL Server needs ~2 GB RAM. The SA password must have upper and lower case, a digit and a symbol |
| Port already in use | Change `APP_HOST_PORT`, `DB_HOST_PORT` or `MAILPIT_UI_PORT` in `.env` |
| Want a fresh demo | `docker compose down -v && docker compose up -d` |
| Credentials in `appsettings.Development.json` | Demo-only values that match `.env.example`, for running outside Docker. Change both for anything real |
| Times look wrong | All times are shown in IST (`Marketplace:TimeZone`) |
