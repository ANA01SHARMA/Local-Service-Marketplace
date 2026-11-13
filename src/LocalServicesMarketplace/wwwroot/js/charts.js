// Chart.js helpers. Charts are declared in markup:
//   <canvas data-chart="bar|hbar|line" data-labels='[...]' data-values='[...]'
//           data-colors='[...]' data-format="count|money" aria-label="..."></canvas>
// Rules followed: one series per chart (no dual axes), thin marks with 4px rounded data ends,
// solid hairline grid, values in text colours, tooltip on hover/focus, and a table view next to each chart.
(function () {
    "use strict";
    if (!window.Chart) return;

    var INK = "#0F172A", MUTED = "#64748B", GRID = "#E2E8F0", SERIES = "#2563EB";
    var money = new Intl.NumberFormat("en-IN", { style: "currency", currency: "INR", maximumFractionDigits: 0 });
    var count = new Intl.NumberFormat("en-IN");

    Chart.defaults.font.family = '"Plus Jakarta Sans", system-ui, sans-serif';
    Chart.defaults.font.size = 12;
    Chart.defaults.color = MUTED;
    Chart.defaults.plugins.legend.display = false;
    Chart.defaults.plugins.tooltip.backgroundColor = INK;
    Chart.defaults.plugins.tooltip.padding = 10;
    Chart.defaults.plugins.tooltip.cornerRadius = 8;
    Chart.defaults.plugins.tooltip.displayColors = false;
    Chart.defaults.plugins.tooltip.titleFont = { weight: "600" };
    // No entrance/resize animation: the data is the point, and resizes stay instant.
    Chart.defaults.animation = false;

    function fmt(kind) { return function (v) { return kind === "money" ? money.format(v) : count.format(v); }; }

    document.querySelectorAll("canvas[data-chart]").forEach(function (canvas) {
        var type = canvas.dataset.chart;
        var labels = JSON.parse(canvas.dataset.labels || "[]");
        var values = JSON.parse(canvas.dataset.values || "[]");
        var colors = canvas.dataset.colors ? JSON.parse(canvas.dataset.colors) : null;
        var format = fmt(canvas.dataset.format);
        var horizontal = type === "hbar";

        var dataset = type === "line"
            ? {
                data: values, borderColor: SERIES, backgroundColor: SERIES, borderWidth: 2, tension: 0.3,
                pointRadius: 4, pointHoverRadius: 6, pointBackgroundColor: SERIES, pointBorderColor: "#fff", pointBorderWidth: 2
            }
            : {
                data: values, backgroundColor: colors || SERIES, hoverBackgroundColor: colors || "#1D4ED8",
                borderRadius: 4, borderSkipped: "start", borderColor: "#fff", borderWidth: 0,
                maxBarThickness: horizontal ? 22 : 28, categoryPercentage: 0.7, barPercentage: 0.8
            };

        var valueAxis = {
            beginAtZero: true,
            grid: { color: GRID, drawTicks: false },
            border: { display: false },
            ticks: { padding: 8, maxTicksLimit: 5, callback: format, precision: canvas.dataset.format === "money" ? undefined : 0 }
        };
        var categoryAxis = {
            grid: { display: false },
            border: { color: GRID },
            ticks: { padding: 6, color: horizontal ? INK : MUTED, font: horizontal ? { weight: "600" } : undefined }
        };

        new Chart(canvas, {
            type: type === "line" ? "line" : "bar",
            data: { labels: labels, datasets: [dataset] },
            options: {
                indexAxis: horizontal ? "y" : "x",
                responsive: true,
                maintainAspectRatio: false,
                interaction: { mode: "index", intersect: false },
                layout: { padding: { top: 4, right: horizontal ? 12 : 4 } },
                scales: horizontal ? { x: valueAxis, y: categoryAxis } : { x: categoryAxis, y: valueAxis },
                plugins: {
                    tooltip: {
                        callbacks: {
                            label: function (ctx) { return (canvas.dataset.series ? canvas.dataset.series + ": " : "") + format(ctx.parsed[horizontal ? "x" : "y"]); }
                        }
                    }
                }
            }
        });
    });
})();
