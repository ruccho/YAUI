"""Summarizes a comparison CSV and draws the chart of the README.

Usage: python chart.py <yaui-bench-comparison-*.csv> <output directory>

Prints the table of the median over rounds of each scenario's median, and writes benchmark-light.svg and
benchmark-dark.svg: the main thread and GPU time added by the UI to the empty scene.
"""
import csv
import os
import statistics
import sys
from collections import defaultdict

SYSTEMS = [("YAUI", "YAUI"), ("uGUI", "uGUI + TextMeshPro"), ("UITK", "UI Toolkit")]

CPU_CASES = [
    ("GridNone", "Grid: no changes"),
    ("GridColor10", "Grid: 10% change color"),
    ("GridMove10", "Grid: 10% move"),
    ("GridText10", "Grid: 10% change text"),
    ("GridHitTest", "Grid: 10 hit tests"),
    ("ListScroll", "List: scroll"),
    ("ListResize10", "List: 10% relayout"),
]

GPU_CASES = [
    ("GridNone+Ballast", "Grid"),
    ("ListScroll+Ballast", "List"),
]

THEMES = {
    "light": dict(surface="#fcfcfb", primary="#0b0b0b", secondary="#52514e", muted="#898781", grid="#e1e0d9",
                  border="rgba(11,11,11,0.10)", series=["#2a78d6", "#eb6834", "#1baf7a"]),
    "dark": dict(surface="#1a1a19", primary="#ffffff", secondary="#c3c2b7", muted="#898781", grid="#2c2c2a",
                 border="rgba(255,255,255,0.10)", series=["#3987e5", "#d95926", "#199e70"]),
}

FONT = "-apple-system, BlinkMacSystemFont, 'Segoe UI', Helvetica, Arial, sans-serif"


def load(path):
    """(case, system) -> metric -> median over rounds."""
    lines = [l for l in open(path, encoding="utf-8") if not l.startswith("#")]
    values = defaultdict(lambda: defaultdict(list))
    for row in list(csv.DictReader(lines)):
        case, _, system = row["scenario"].split("#")[0].partition(".")
        values[(case, system or "-")][row["metric"]].append(float(row["median"]))
    return {key: {m: statistics.median(v) for m, v in metrics.items()} for key, metrics in values.items()}


def added(data, case, system, empty, metric):
    """Time added to the empty scene; differences within noise below zero are shown as zero."""
    return max(0.0, data[(case, system)][metric] - data[(empty, "-")][metric])


def bar(x0, y, length, thickness, color):
    """A bar from the baseline with a 4px rounded data end."""
    if length <= 0:
        return ""
    r = min(4.0, length, thickness / 2)
    x1 = x0 + length
    return (f'<path d="M{x0:.1f},{y:.1f} H{x1 - r:.1f} Q{x1:.1f},{y:.1f} {x1:.1f},{y + r:.1f} '
            f'V{y + thickness - r:.1f} Q{x1:.1f},{y + thickness:.1f} {x1 - r:.1f},{y + thickness:.1f} '
            f'H{x0:.1f} Z" fill="{color}"/>')


def draw(cpu, gpu, theme, subtitle):
    t = THEMES[theme]
    width, left, right = 820, 190, 70
    plot = width - left - right
    axis_max = 14.0
    scale = plot / axis_max
    thickness, gap, group_gap = 13, 2, 16
    group = len(SYSTEMS) * thickness + (len(SYSTEMS) - 1) * gap

    out = []
    y = 36
    out.append(f'<text x="24" y="{y}" font-size="19" font-weight="600" fill="{t["primary"]}">'
               f'Time added by the UI per frame (ms, lower is better)</text>')
    y += 22
    out.append(f'<text x="24" y="{y}" font-size="13" fill="{t["secondary"]}">{subtitle}</text>')

    # Legend.
    y += 26
    x = 24
    for i, (_, label) in enumerate(SYSTEMS):
        out.append(f'<rect x="{x}" y="{y - 10}" width="12" height="12" rx="3" fill="{t["series"][i]}"/>')
        out.append(f'<text x="{x + 18}" y="{y}" font-size="13" fill="{t["primary"]}">{label}</text>')
        x += 18 + len(label) * 7.4 + 24

    def section(title, rows, y):
        y += 34
        out.append(f'<text x="24" y="{y}" font-size="14" font-weight="600" fill="{t["primary"]}">{title}</text>')
        y += 14
        top = y
        height = len(rows) * (group + group_gap)
        for tick in range(0, int(axis_max) + 1, 2):
            gx = left + tick * scale
            out.append(f'<line x1="{gx:.1f}" y1="{top}" x2="{gx:.1f}" y2="{top + height}" stroke="{t["grid"]}" '
                       f'stroke-width="1"/>')
            out.append(f'<text x="{gx:.1f}" y="{top + height + 16}" font-size="11" text-anchor="middle" '
                       f'fill="{t["muted"]}">{tick}</text>')
        for label, values in rows:
            gy = y + group_gap / 2
            out.append(f'<text x="{left - 12}" y="{gy + group / 2 + 4:.1f}" font-size="13" text-anchor="end" '
                       f'fill="{t["secondary"]}">{label}</text>')
            for i, value in enumerate(values):
                by = gy + i * (thickness + gap)
                broken = value > axis_max
                length = min(value, axis_max) * scale
                out.append(bar(left, by, length, thickness, t["series"][i]))
                if broken:
                    # The bar is cut at the end of the axis: a gap in the surface color marks the break.
                    bx = left + length - 18
                    out.append(f'<path d="M{bx:.1f},{by + thickness + 1:.1f} L{bx + 5:.1f},{by - 1:.1f}" '
                               f'stroke="{t["surface"]}" stroke-width="3"/>')
                out.append(f'<text x="{left + length + 5:.1f}" y="{by + thickness - 2:.1f}" font-size="11" '
                           f'fill="{t["primary"]}">{value:.1f}</text>')
            y += group + group_gap
        return y + 18

    y = section("CPU (main thread)", cpu, y)
    y = section("GPU", gpu, y)
    y += 20
    out.append(f'<text x="24" y="{y}" font-size="11" fill="{t["muted"]}">Grid: 1,500 cells (4,500 elements). '
               f'List: 500 rows in a scroll view. Time added to an empty scene; median of 3 runs.</text>')
    y += 16
    out.append(f'<text x="24" y="{y}" font-size="11" fill="{t["muted"]}">GPU: measured at a fixed clock under a '
               f'full screen load. uGUI uses nested canvases, no extra raycast targets and static TMP scale.</text>')
    height = y + 24

    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" '
            f'viewBox="0 0 {width} {height}" font-family="{FONT}">\n'
            f'<rect x="0.5" y="0.5" width="{width - 1}" height="{height - 1}" rx="12" fill="{t["surface"]}" '
            f'stroke="{t["border"]}"/>\n' + "\n".join(o for o in out if o) + "\n</svg>\n")


def main():
    path, out_dir = sys.argv[1], sys.argv[2]
    data = load(path)
    cpu_metric, gpu_metric = "CPU Main (ms)", "GPU (ms)"

    print("| Scenario | " + " | ".join(label for _, label in SYSTEMS) + " |")
    print("|---" * (len(SYSTEMS) + 1) + "|")
    print("| Empty | " + " | ".join(f'{data[("Empty", "-")][cpu_metric]:.2f}' for _ in SYSTEMS) + " |")
    for case, label in CPU_CASES:
        cells = []
        for system, _ in SYSTEMS:
            total = data[(case, system)][cpu_metric]
            cells.append(f"{total:.2f} (+{added(data, case, system, 'Empty', cpu_metric):.2f})")
        print(f"| {label} | " + " | ".join(cells) + " |")
    for case, label in GPU_CASES:
        cells = [f"+{added(data, case, system, 'Empty+Ballast', gpu_metric):.2f}" for system, _ in SYSTEMS]
        print(f"| GPU: {label} | " + " | ".join(cells) + " |")

    cpu = [(label, [added(data, case, s, "Empty", cpu_metric) for s, _ in SYSTEMS]) for case, label in CPU_CASES]
    gpu = [(label, [added(data, case, s, "Empty+Ballast", gpu_metric) for s, _ in SYSTEMS])
           for case, label in GPU_CASES]
    subtitle = "Pixel 5 (Android 14, Vulkan), IL2CPP release build, Unity 6.7"
    for theme in THEMES:
        with open(os.path.join(out_dir, f"benchmark-{theme}.svg"), "w", encoding="utf-8") as f:
            f.write(draw(cpu, gpu, theme, subtitle))


if __name__ == "__main__":
    main()
