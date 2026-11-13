// Kaarigar - small progressive enhancements. Every feature works without JavaScript;
// these scripts only improve the experience (slot day tabs, confirm modals, toasts, sidebar).
(function () {
    "use strict";

    // ---- Slot picker: show one day at a time --------------------------------------------
    document.querySelectorAll("[data-slot-picker]").forEach(function (picker) {
        var tabs = picker.querySelectorAll("[data-day-tab]");
        var days = picker.querySelectorAll("[data-day]");
        function show(key) {
            tabs.forEach(function (t) { t.setAttribute("aria-selected", t.dataset.dayTab === key ? "true" : "false"); });
            days.forEach(function (d) { d.hidden = d.dataset.day !== key; });
        }
        tabs.forEach(function (tab) {
            tab.addEventListener("click", function () { show(tab.dataset.dayTab); });
        });
        // Start on the day containing the checked slot, otherwise the first day.
        var checked = picker.querySelector("input[type=radio]:checked");
        var start = checked ? checked.closest("[data-day]") : days[0];
        if (start) show(start.dataset.day);
    });

    // ---- Horizontal scrollers: fade the edge that has more, keep the selected item in view --
    // (Runs after the slot picker so the selected day tab is known.)
    document.querySelectorAll("[data-scroll-x]").forEach(function (el) {
        var update = function () {
            var max = el.scrollWidth - el.clientWidth;
            var more = el.scrollLeft < max - 2, back = el.scrollLeft > 2;
            el.dataset.overflow = max <= 2 ? "none" : more && back ? "both" : back ? "start" : "end";
        };
        var current = el.querySelector(".active, [aria-current='page'], [aria-selected='true']");
        if (current && el.scrollWidth > el.clientWidth) {
            // Centre it with scrollLeft (scrollIntoView would also move the page vertically).
            el.scrollLeft = Math.max(0, current.offsetLeft - (el.clientWidth - current.offsetWidth) / 2);
        }
        update();
        el.addEventListener("scroll", update, { passive: true });
        window.addEventListener("resize", update);
    });

    // ---- Services filters: live result count, per-group "Clear", later-date calendar ------
    // Without JavaScript the panel is a plain GET form; these only add feedback while choosing.
    var filterForm = document.querySelector("[data-filter-form]");
    if (filterForm) {
        var submitBtn = filterForm.querySelector("[data-filter-submit]");
        var countText = submitBtn.querySelector("[data-filter-count]");
        var groups = filterForm.querySelectorAll("[data-filter-group]");
        var laterRadio = filterForm.querySelector("[data-date-custom]");
        var laterBtn = filterForm.querySelector("[data-date-more]");
        var laterInput = filterForm.querySelector("[data-date-input]");
        var laterText = laterBtn ? laterBtn.querySelector("[data-date-text]") : null;
        var countTimer, countSeq = 0;

        var hasValue = function (el) {
            if (el.disabled) return false;
            return el.type === "radio" ? el.checked && el.value !== "" : el.value !== "";
        };
        var syncGroups = function () {
            groups.forEach(function (g) {
                var set = Array.prototype.some.call(g.querySelectorAll("input[name], select[name]"), hasValue);
                g.classList.toggle("is-set", set);
                var clear = g.querySelector("[data-clear]");
                if (clear) clear.hidden = !set;
            });
            if (laterRadio && !laterRadio.checked) {
                laterRadio.disabled = true;
                laterBtn.classList.remove("is-set");
                laterText.textContent = "Pick a later date";
                laterInput.value = "";
            }
        };
        var refreshCount = function () {
            var seq = ++countSeq;
            var params = new URLSearchParams();
            new FormData(filterForm).forEach(function (v, k) { if (v !== "") params.append(k, v); });
            params.set("pageSize", "3");
            submitBtn.classList.add("is-loading");
            fetch("/api/providers?" + params.toString(), { headers: { Accept: "application/json" } })
                .then(function (r) { return r.ok ? r.json() : Promise.reject(r.status); })
                .then(function (data) {
                    if (seq !== countSeq) return;
                    var n = data.totalCount;
                    countText.textContent = n === 0 ? "No matches" : "Show " + n + " professional" + (n === 1 ? "" : "s");
                    submitBtn.classList.toggle("is-empty", n === 0);
                })
                .catch(function () { if (seq === countSeq) { countText.textContent = "Show results"; submitBtn.classList.remove("is-empty"); } })
                .then(function () { if (seq === countSeq) submitBtn.classList.remove("is-loading"); });
        };
        var changed = function () {
            syncGroups();
            clearTimeout(countTimer);
            countTimer = setTimeout(refreshCount, 300);
        };
        filterForm.addEventListener("change", changed);
        filterForm.addEventListener("input", function (e) { if (e.target.type === "number") changed(); });

        groups.forEach(function (g) {
            var clear = g.querySelector("[data-clear]");
            if (!clear) return;
            clear.addEventListener("click", function () {
                g.querySelectorAll("input[name], select[name]").forEach(function (el) {
                    if (el.type === "radio") el.checked = el.value === "" && !el.hasAttribute("data-date-custom");
                    else el.value = "";
                });
                changed();
                var first = g.querySelector("select, input:not([type=radio]), input[type=radio]:checked");
                if (first) first.focus();
            });
        });

        if (laterBtn && laterInput) {
            laterBtn.addEventListener("click", function () {
                try { laterInput.showPicker(); } catch (err) { laterInput.style.pointerEvents = "auto"; laterInput.focus(); }
            });
            laterInput.addEventListener("change", function () {
                if (!laterInput.value) return;
                laterRadio.disabled = false;
                laterRadio.value = laterInput.value;
                laterRadio.checked = true;
                var d = new Date(laterInput.value + "T00:00:00");
                laterText.textContent = d.toLocaleDateString("en-IN", { weekday: "short", day: "numeric", month: "short" });
                laterBtn.classList.add("is-set");
            });
        }
        syncGroups();
    }

    // ---- Booking form: live summary ------------------------------------------------------
    var summary = document.querySelector("[data-booking-summary]");
    if (summary) {
        var form = summary.closest("form") || document;
        var update = function () {
            var service = form.querySelector("input[name=ServiceId]:checked");
            var slot = form.querySelector("input[name=TimeSlotId]:checked");
            var s = summary.querySelector("[data-summary-service]");
            var p = summary.querySelector("[data-summary-price]");
            var t = summary.querySelector("[data-summary-time]");
            if (service) { s.textContent = service.dataset.title; p.textContent = service.dataset.price; }
            if (slot) { t.textContent = slot.dataset.label; }
        };
        form.addEventListener("change", update);
        update();
    }

    // ---- Reusable "reason" modal (cancel / reject / block) -------------------------------
    var reasonModal = document.getElementById("reasonModal");
    if (reasonModal) {
        reasonModal.addEventListener("show.bs.modal", function (e) {
            var trigger = e.relatedTarget;
            if (!trigger) return;
            var f = reasonModal.querySelector("form");
            f.action = trigger.dataset.action;
            reasonModal.querySelector(".modal-title").textContent = trigger.dataset.title || "Are you sure?";
            reasonModal.querySelector("[data-modal-body]").textContent = trigger.dataset.body || "";
            var submit = reasonModal.querySelector("[type=submit]");
            submit.textContent = trigger.dataset.confirm || "Confirm";
            submit.className = "btn " + (trigger.dataset.variant || "btn-danger");
            var reason = reasonModal.querySelector("textarea");
            reason.value = "";
            reason.required = trigger.dataset.reasonRequired === "true";
            reason.placeholder = trigger.dataset.placeholder || "Optional note";
            reasonModal.querySelector("[name=returnUrl]").value = window.location.pathname + window.location.search;
        });
    }

    // ---- Confirm dialogs on simple destructive forms -------------------------------------
    document.addEventListener("submit", function (e) {
        var msg = e.target.getAttribute("data-confirm");
        if (msg && !window.confirm(msg)) e.preventDefault();
    });

    // ---- Disable submit buttons after the first click (prevents double booking clicks) --
    document.addEventListener("submit", function (e) {
        if (e.defaultPrevented) return;
        var f = e.target;
        if (window.jQuery && window.jQuery(f).data("validator") && !window.jQuery(f).valid()) return;
        f.querySelectorAll("button[type=submit]").forEach(function (b) {
            b.disabled = true;
            b.dataset.originalText = b.innerHTML;
            b.innerHTML = '<span class="spinner-border spinner-border-sm me-2" aria-hidden="true"></span>' + b.textContent.trim();
        });
    });
    window.addEventListener("pageshow", function () {
        document.querySelectorAll("button[data-original-text]").forEach(function (b) { b.disabled = false; b.innerHTML = b.dataset.originalText; });
    });

    // ---- Toasts auto-dismiss ------------------------------------------------------------
    document.querySelectorAll(".toast-msg").forEach(function (t) {
        var close = function () { t.remove(); };
        var btn = t.querySelector("button");
        if (btn) btn.addEventListener("click", close);
        if (t.dataset.autohide !== "false") setTimeout(close, 6000);
    });

    // ---- Dashboard sidebar on small screens ----------------------------------------------
    var sidebar = document.querySelector(".dash-sidebar");
    var backdrop = document.querySelector(".dash-backdrop");
    document.querySelectorAll("[data-sidebar-toggle]").forEach(function (btn) {
        btn.addEventListener("click", function () {
            sidebar.classList.toggle("show");
            backdrop.classList.toggle("show");
        });
    });
    if (backdrop) backdrop.addEventListener("click", function () { sidebar.classList.remove("show"); backdrop.classList.remove("show"); });

    // ---- Auto-submit filter selects --------------------------------------------------------
    document.querySelectorAll("[data-autosubmit]").forEach(function (el) {
        el.addEventListener("change", function () { el.form.submit(); });
    });

    // ---- Slug helper on the category form ------------------------------------------------
    var nameInput = document.querySelector("[data-slug-source]");
    var slugInput = document.querySelector("[data-slug-target]");
    if (nameInput && slugInput) {
        nameInput.addEventListener("input", function () {
            if (slugInput.dataset.touched) return;
            slugInput.value = nameInput.value.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
        });
        slugInput.addEventListener("input", function () { slugInput.dataset.touched = "1"; });
    }
})();
