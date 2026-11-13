# Demo credentials (development only)

> Reset before a presentation: `docker compose down -v && docker compose up -d` (about 30 seconds).

These accounts exist **only** when the app runs with `ASPNETCORE_ENVIRONMENT=Development` and
`Seed:Enabled=true` (the default in `compose.yaml`). Never use these values outside a local demo.

## Administrator

The admin account is created from configuration, not hard-coded:

| Setting (`.env`) | Default in `.env.example` |
|---|---|
| `ADMIN_EMAIL` | `admin@lsm.local` |
| `ADMIN_PASSWORD` | `Admin@Lsm2026!` |

After the first sign-in a banner asks you to change the password (**My profile → Change password**).
To use different credentials, edit `.env` **before** the first start, or run `docker compose down -v` to reseed.

## Sample customers and providers

Each fresh database gets **different random sample data**:
- per-provider ratings (about 2.5 to 5 stars) with comments that match the stars
- past job counts
- prices
- working hours (slots start at :00, :15, :30 or :45, and last as long as the trade needs)
- days off
- how busy each provider is

The random seed is printed in the app log (`docker compose logs app | grep "random seed"`). Put it in `.env` as
`SEED_RANDOM_SEED` to get exactly the same dataset again. The scenarios in the table below are placed
deliberately and are the same in every dataset.

Every sample account uses the password from `DEMO_USER_PASSWORD` (default `Demo@12345`). Sample data uses the
`@demo.lsm.local` domain and has a purple **Demo** badge in admin screens, so it can't be confused with live records.

| Role | E-mail | Good for demonstrating |
|---|---|---|
| Customer | `aarav.sharma@demo.lsm.local` | Has pending, confirmed and completed bookings, reviews and notifications |
| Customer | `isha.malhotra@demo.lsm.local` | Has a resolved complaint |
| Customer | `kabir.mehta@demo.lsm.local` | Has an open complaint |
| Provider (verified) | `rohit.verma@demo.lsm.local` | 2 pending requests to accept or reject, and a confirmed job from yesterday to "mark as completed" |
| Provider (verified) | `suresh.yadav@demo.lsm.local` | Plumber with history and reviews |
| Provider (awaiting verification) | `imran.qureshi@demo.lsm.local` | Shows the "under review" state; approve as admin |
| Provider (awaiting verification) | `lakshmi.menon@demo.lsm.local` | Second item in the verification queue |
| Provider (incomplete) | `rajesh.thakur@demo.lsm.local` | Onboarding checklist with nothing done yet |

Other sample customers: `sneha.reddy`, `rahul.bansal`, `pooja.saxena`, `ishaan.kapoor`, `ananya.gupta`, `rohan.desai`,
`meghna.iyer`, `vivek.arora`, `tanvi.joshi`, `siddharth.rao`, `nidhi.bhatt`. Other providers: `mohammed.arif`, `priya.nair`,
`harpreet.singh`, `deepak.kumar`, `ananya.iyer`, `neha.kapoor`, `vikram.joshi`, `sanjay.gupta`, `kavita.rao`,
`amit.chauhan`, `farhan.sheikh`, `meera.pillai`. All are `@demo.lsm.local`.

Password-reset e-mails go to **Mailpit**: http://localhost:8025.

## Suggested 5-minute demo script

1. **Visitor:** open the home page, search "AC" in Noida, filter by rating, and open a provider profile.
2. **Customer** (`aarav.sharma`): Book now, pick a service and a time slot, then send. The booking is *Pending*.
3. **Provider** (`rohit.verma`): Booking requests, then Accept. Open the job and choose Mark as completed.
4. **Customer:** My reviews, then Leave a review. The provider's rating updates (via `dbo.CalculateProviderAverageRating`).
5. **Admin:** Provider verification, then open Imran Qureshi, open the PDF, and Approve. He now appears in search.
6. **Admin:** Reports shows `dbo.GetMonthlyBookingReport`, the views and the CSV export. Manage users lets you block an account.
7. **API:** run `docker compose --profile test run --rm newman`, or import `postman/` into Postman.
