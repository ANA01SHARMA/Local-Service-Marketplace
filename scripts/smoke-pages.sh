#!/usr/bin/env bash
# Smoke test: signs in as each role (cookie + antiforgery token) and requests every page.
# Usage: scripts/smoke-pages.sh [baseUrl]    (default http://localhost:5080)
set -uo pipefail
BASE="${1:-http://localhost:5080}"
PASS="${DEMO_USER_PASSWORD:-Demo@12345}"
ADMIN_EMAIL="${ADMIN_EMAIL:-admin@lsm.local}"
ADMIN_PASSWORD="${ADMIN_PASSWORD:-Admin@Lsm2026!}"
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT
fail=0

check() { # jar url [expected]
  local code; code=$(curl -s -o "$TMP/body" -w '%{http_code}' -b "$1" -c "$1" "$BASE$2")
  local want="${3:-200}"
  if [[ "$code" == "$want" ]]; then printf '  ok   %s %s\n' "$code" "$2"; else printf '  FAIL %s %s (expected %s)\n' "$code" "$2" "$want"; fail=1; fi
}

login() { # jar email password
  local token
  token=$(curl -s -c "$1" -b "$1" "$BASE/Account/Login" | grep -o 'name="__RequestVerificationToken" type="hidden" value="[^"]*"' | head -1 | sed 's/.*value="//;s/"$//')
  curl -s -o /dev/null -w '%{http_code} %{redirect_url}\n' -c "$1" -b "$1" -X POST "$BASE/Account/Login" \
    --data-urlencode "Email=$2" --data-urlencode "Password=$3" --data-urlencode "__RequestVerificationToken=$token"
}

echo "== anonymous"
A="$TMP/anon"
for p in / /Home/About /become-a-provider /Home/Contact /Home/Terms /Services "/Services?category=plumbing&city=New%20Delhi&minRating=4&sort=price_asc" \
         /providers/1 /Account/Login /Account/Register /Account/RegisterProvider /Account/ForgotPassword /health; do check "$A" "$p"; done
check "$A" /Account/AccessDenied 403
check "$A" /nope 404
check "$A" /Customer 302
check "$A" /api/bookings 401

echo "== customer";  C="$TMP/cust";  login "$C" "aarav.sharma@demo.lsm.local" "$PASS"
for p in /Customer "/Customer/Bookings" "/Customer/Bookings?status=upcoming" "/Customer/Bookings?status=history" /Customer/Reviews /Notifications /Support /Account/Profile "/Booking/Create?providerId=1"; do check "$C" "$p"; done
check "$C" /Admin 302
check "$C" /Provider 302

echo "== provider";  P="$TMP/prov";  login "$P" "rohit.verma@demo.lsm.local" "$PASS"
for p in /Provider /Provider/Profile /Provider/Services /Provider/Availability /Provider/Requests "/Provider/Jobs?status=upcoming" "/Provider/Jobs?status=completed" /Provider/Earnings /Provider/Reviews /Provider/Verification /Notifications /Support; do check "$P" "$p"; done

echo "== admin";  D="$TMP/admin";  login "$D" "$ADMIN_EMAIL" "$ADMIN_PASSWORD"
for p in /Admin "/Admin/Users" "/Admin/Users?role=Provider&status=active" /Admin/Providers "/Admin/Providers?status=Pending" /Admin/Verification /Admin/ReviewProvider/15 /Admin/Categories "/Admin/Categories?edit=1" /Admin/Services /Admin/Bookings "/Admin/Bookings?status=completed" /Admin/Complaints /Admin/Reports /Admin/ExportMonthlyReport?year=2026 /Booking/Details/1 /Notifications; do check "$D" "$p"; done

[[ $fail == 0 ]] && echo "ALL PAGES OK" || { echo "SOME PAGES FAILED"; exit 1; }
