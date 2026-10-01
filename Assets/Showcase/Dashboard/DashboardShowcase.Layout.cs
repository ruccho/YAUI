using UnityEngine;

namespace Yaui.Showcase
{
    // The page: a sidebar and a main column of a toolbar and three rows of cards, all flexbox.
    public sealed partial class DashboardShowcase
    {
        void BuildDashboard()
        {
            var go = new GameObject("Dashboard");
            _panel = go.AddComponent<YauiPanel>();
            _panel.ReferenceResolution = new Vector2(1920f, 1080f);
            _panel.Match = 0.5f;
            go.AddComponent<YauiRaycaster>();
            _panels.Add(_panel);

            _root = go.GetComponent<YauiElement>();
            _root.Layout = Lay.Row;
            _root.Box = Sty.Fill(Pal.Background);

            BuildSidebar();

            var main = Ui.El(_root, "Main", Lay.Col.Grow());
            BuildToolbar(main);

            var page = Ui.El(main, "Page", Lay.Col.Grow().Pad(24f, 22f).Gap(20f));
            BuildKpis(page);
            BuildCharts(page);

            var bottom = Ui.El(page, "Bottom", Lay.Row.Grow().Gap(20f));
            BuildTable(bottom);
            var side = Ui.El(bottom, "Side", Lay.Col.W(400f).Fixed().Gap(20f));
            BuildHeatmap(side);
            BuildFeed(side);
        }

        #region Sidebar and toolbar

        void BuildSidebar()
        {
            var bar = Ui.El(_root, "Sidebar", Lay.Col.W(244f).Fixed().Pad(18f, 20f).Gap(4f),
                Sty.Fill(Pal.Navy).Shadow(Ui.Hex(0x0F172A, 0.25f), 14f));

            var brand = Ui.El(bar, "Brand", Lay.Row.Items(FlexAlign.Center).Gap(12f).Margin(4f, 0f, 0f, 22f));
            var logo = Ui.El(brand, "Logo", Lay.Col.Size(38f, 38f).Fixed().Center(),
                Sty.Fill(Pal.Accent).Radius(11f).Shadow(Ui.Hex(0x4F46E5, 0.6f), 8f));
            Ui.Image(logo, "Icon", _icons[(int)Icon.Hexagon], 20f, Color.white);
            var name = Ui.El(brand, "Name", Lay.Col);
            Ui.Text(name, "Title", "<b>YAUI Analytics</b>", 17f, Color.white);
            Ui.Text(name, "Subtitle", "Acme Corp · Production", 12f, Ui.Hex(0x7C89A8));

            Section(bar, "WORKSPACE");
            Nav(bar, Icon.Grid, "Overview", null, true);
            Nav(bar, Icon.User, "Customers", "2,481", false);
            Nav(bar, Icon.Bars, "Revenue", null, false);
            Nav(bar, Icon.Bolt, "Events", "Live", false);
            Nav(bar, Icon.Target, "Funnels", null, false);
            Nav(bar, Icon.Diamond, "Reports", "12", false);
            Section(bar, "SYSTEM");
            Nav(bar, Icon.Shield, "Security", null, false);
            Nav(bar, Icon.Heart, "Team", null, false);
            Nav(bar, Icon.Star, "Billing", null, false);

            Ui.El(bar, "Spacer", Lay.Col.Grow());

            // What this page costs to draw.
            var stats = Ui.El(bar, "Stats", Lay.Col.Pad(14f).Gap(8f),
                Sty.Fill(Ui.Hex(0xFFFFFF, 0.06f)).Radius(12f).Border(1f, Ui.Hex(0xFFFFFF, 0.1f)));
            Ui.Text(stats, "Title", "<b>RENDERED BY YAUI</b>", 11f, Ui.Hex(0x8B9BFF));
            _stats = Ui.Text(stats, "Counts", "", 15f, Color.white);
            _statsDetail = Ui.Text(stats, "Draws", "", 15f, Ui.Hex(0xB6C0D8));

            var user = Ui.El(bar, "User", Lay.Row.Items(FlexAlign.Center).Gap(10f).Margin(2f, 14f, 0f, 0f));
            Avatar(user, "RC", Pal.Pink, 34f);
            var who = Ui.El(user, "Who", Lay.Col);
            Ui.Text(who, "Name", "<b>Ruccho</b>", 14f, Color.white);
            Ui.Text(who, "Mail", "ruccho@example.com", 12f, Ui.Hex(0x7C89A8));
        }

        static void Section(YauiElement parent, string label)
        {
            Ui.Text(parent, "Section", "<b>" + label + "</b>", 11f, Ui.Hex(0x5C6A8A),
                Lay.Col.Fixed().Margin(10f, 14f, 0f, 6f));
        }

        void Nav(YauiElement parent, Icon icon, string label, string badge, bool active)
        {
            var item = Ui.El(parent, label, Lay.Row.H(40f).Fixed().Items(FlexAlign.Center).Gap(12f).Pad(12f, 0f),
                Sty.Fill(active ? Pal.Accent : Ui.Hex(0xFFFFFF, 0.05f)).Radius(10f)).Hit();
            Ui.Image(item, "Icon", _icons[(int)icon], 16f, active ? Color.white : Ui.Hex(0x8FA0C4));
            Ui.Text(item, "Label", active ? "<b>" + label + "</b>" : label, 14f,
                active ? Color.white : Ui.Hex(0xC3CCE0), Lay.Col.Grow());
            if (badge != null)
            {
                var pill = Ui.El(item, "Badge", Lay.Col.Pad(8f, 2f), Sty.Fill(Ui.Hex(0xFFFFFF, 0.12f)).Radius(9f));
                Ui.Text(pill, "Text", badge, 11f, Ui.Hex(0xC3CCE0));
            }

            item.gameObject.AddComponent<YauiButton>().Colors = Ui.Tints(0.8f, 0.6f);

        }

        YauiElement Avatar(YauiElement parent, string initials, Color color, float size)
        {
            var avatar = Ui.El(parent, "Avatar", Lay.Col.Size(size, size).Fixed().Center(),
                Sty.Fill(Color.Lerp(color, Color.white, 0.78f)).Radius(size * 0.5f));
            Ui.Text(avatar, "Initials", "<b>" + initials + "</b>", size * 0.36f, Color.Lerp(color, Color.black, 0.35f));
            return avatar;
        }

        void BuildToolbar(YauiElement main)
        {
            var bar = Ui.El(main, "Toolbar", Lay.Row.H(68f).Fixed().Items(FlexAlign.Center).Gap(16f).Pad(24f, 0f),
                Sty.Fill(Pal.Surface).Shadow(Ui.Hex(0x0F172A, 0.06f), 8f, 0f, new Vector2(0f, 2f)));

            var title = Ui.El(bar, "Title", Lay.Col.Margin(0f, 0f, 12f, 0f));
            Ui.Text(title, "Name", "<b>Overview</b>", 21f, Pal.Text);
            _clock = Ui.Text(title, "Clock", "", 12f, Pal.Faint);

            // Search: an input field. Typing filters the table.
            var search = Ui.El(bar, "Search", Lay.Row.Size(330f, 40f).Fixed().Items(FlexAlign.Center).Gap(9f).Pad(13f, 0f),
                Sty.Fill(Pal.Background).Radius(10f).Border(1f, Pal.Line)).Hit();
            Ui.Image(search, "Icon", _icons[(int)Icon.Search], 15f, Pal.Faint);
            var area = Ui.El(search, "Text Area", Lay.Col.Grow().H(22f).Justify(FlexJustify.Center));
            area.ClipChildren = true;
            var placeholder = Ui.Text(area, "Placeholder", "Search customers, plans, countries...", 14f, Pal.Faint,
                Lay.Col.Abs(0f, 2f, Lay.Auto, Lay.Auto));
            var text = Ui.Text(area, "Text", "", 14f, Pal.Text);
            var input = search.gameObject.AddComponent<YauiInputField>();
            input.TextComponent = text;
            input.Placeholder = placeholder;
            input.CaretColor = Pal.Accent;
            input.Colors = Ui.Tints(0.97f, 0.94f);
            input.OnValueChanged.AddListener(OnSearch);

            Ui.El(bar, "Spacer", Lay.Col.Grow());

            BuildDropdown(bar);

            // Live: a switch that pauses the data.
            var live = Ui.El(bar, "Live", Lay.Row.Items(FlexAlign.Center).Gap(9f));
            Ui.Text(live, "Label", "Live", 14f, Pal.Dim);
            var track = Ui.El(live, "Switch", Lay.Col.Size(42f, 24f).Fixed(), Sty.Fill(Ui.Hex(0xCBD2E0)).Radius(12f))
                .Hit();
            Ui.El(track, "Knob", Lay.Col.Abs(3f, 3f, Lay.Auto, Lay.Auto).Size(18f, 18f), Sty.Fill(Color.white).Radius(9f));
            var on = Ui.El(track, "On", Lay.Row.Cover().Items(FlexAlign.Center).Justify(FlexJustify.FlexEnd).Pad(3f, 0f),
                Sty.Fill(Pal.Green).Radius(12f));
            Ui.El(on, "Knob", Lay.Col.Size(18f, 18f).Fixed(),
                Sty.Fill(Color.white).Radius(9f).Shadow(Ui.Hex(0x000000, 0.2f), 2f));
            var toggle = track.gameObject.AddComponent<YauiToggle>();
            toggle.Graphic = on;
            toggle.Colors = Ui.Tints();
            toggle.SetIsOnWithoutNotify(true);
            toggle.OnValueChanged.AddListener(value => _live = value);

            // Speed: a slider.
            var speed = Ui.El(bar, "Speed", Lay.Row.Items(FlexAlign.Center).Gap(10f));
            Ui.Text(speed, "Label", "Speed", 14f, Pal.Dim);
            var slider = Ui.El(speed, "Slider", Lay.Col.Size(120f, 24f).Fixed().Justify(FlexJustify.Center),
                Sty.Fill(new Color(1f, 1f, 1f, 0.01f))).Hit();
            var rail = Ui.El(slider, "Rail", Lay.Col.H(5f), Sty.Fill(Ui.Hex(0xDCE1EC)).Radius(2.5f));
            var fill = Ui.El(rail, "Fill", Lay.Col, Sty.Fill(Pal.Accent).Radius(2.5f));
            var handle = Ui.El(rail, "Handle", Lay.Col.Abs(Lay.Auto, -6.5f, Lay.Auto, Lay.Auto).Size(18f, 18f),
                Sty.Fill(Color.white).Radius(9f).Border(2f, Pal.Accent).Shadow(Ui.Hex(0x4F46E5, 0.35f), 4f)).Hit();
            var control = slider.gameObject.AddComponent<YauiSlider>();
            control.Fill = fill;
            control.Handle = handle;
            control.TargetElement = handle;
            control.Colors = Ui.Tints();
            control.MinValue = 0.2f;
            control.MaxValue = 4f;
            control.Value = 1f;
            control.OnValueChanged.AddListener(value => _speed = value);

            var export = Ui.El(bar, "Export", Lay.Row.H(40f).Fixed().Items(FlexAlign.Center).Gap(8f).Pad(16f, 0f),
                Sty.Fill(Pal.Accent).Radius(10f).Shadow(Ui.Hex(0x4F46E5, 0.4f), 8f, 0f, new Vector2(0f, 4f))).Hit();
            Ui.Image(export, "Icon", _icons[(int)Icon.Arrow], 13f, Color.white);
            Ui.Text(export, "Label", "<b>Export</b>", 14f, Color.white);
            export.gameObject.AddComponent<YauiButton>().Colors = Ui.Tints(0.88f, 0.76f);

            Avatar(bar, "RC", Pal.Pink, 38f);
        }

        void BuildDropdown(YauiElement bar)
        {
            var root = Ui.El(bar, "Range",
                Lay.Row.Size(176f, 40f).Fixed().Items(FlexAlign.Center).Justify(FlexJustify.SpaceBetween).Pad(13f, 0f),
                Sty.Fill(Pal.Surface).Radius(10f).Border(1f, Pal.Line)).Hit();
            var caption = Ui.Text(root, "Caption", "", 14f, Pal.Text);
            Ui.Image(root, "Chevron", _icons[(int)Icon.Chevron], 12f, Pal.Dim);

            // The list: an inactive template, copied on top of the panel when the dropdown opens.
            var template = Ui.El(root, "Template", Lay.Col.Pad(6f).Gap(2f).Margin(0f, 6f, 0f, 0f),
                Pal.Card.Radius(12f)).Hit();
            var item = Ui.El(template, "Item", Lay.Row.H(36f).Fixed().Items(FlexAlign.Center).Gap(8f).Pad(10f, 0f),
                Sty.Fill(Pal.Surface).Radius(8f)).Hit();
            var itemText = Ui.Text(item, "Text", "", 14f, Pal.Text, Lay.Col.Grow());
            var mark = Ui.Image(item, "Mark", _icons[(int)Icon.Check], 12f, Pal.Accent);
            var toggle = item.gameObject.AddComponent<YauiToggle>();
            toggle.Graphic = mark;
            toggle.Colors = Ui.Tints(0.94f, 0.88f);
            template.gameObject.SetActive(false);

            var dropdown = root.gameObject.AddComponent<YauiDropdown>();
            dropdown.Template = template;
            dropdown.CaptionText = caption;
            dropdown.ItemText = itemText;
            dropdown.Colors = Ui.Tints(0.97f, 0.93f);
            dropdown.AddOptions(new[] { "Last 24 hours", "Last 7 days", "Last 30 days", "This quarter", "This year" });
            dropdown.SetValueWithoutNotify(1);
            dropdown.RefreshShownValue();
            dropdown.OnValueChanged.AddListener(OnRange);
        }

        #endregion

        #region Cards

        static YauiText CardTitle(YauiElement card, string title, string note)
        {
            var header = Ui.El(card, "Header", Lay.Row.Items(FlexAlign.Center).Justify(FlexJustify.SpaceBetween));
            Ui.Text(header, "Title", "<b>" + title + "</b>", 16f, Pal.Text);
            return Ui.Text(header, "Note", note, 12f, Pal.Faint);
        }

        void BuildKpis(YauiElement page)
        {
            var row = Ui.El(page, "KPIs", Lay.Row.Gap(20f));
            AddKpi(row, "Monthly revenue", 482300f, 1f, "${0:N0}", false, Pal.Accent, Icon.Bars);
            AddKpi(row, "Active users", 28410f, 1f, "{0:N0}", false, Pal.Sky, Icon.User);
            AddKpi(row, "Conversion", 4.82f, 1f, "{0:0.00}%", false, Pal.Green, Icon.Target);
            AddKpi(row, "Avg. session", 312f, 1f / 60f, "{0:0.0} min", false, Pal.Amber, Icon.Bolt);
            AddKpi(row, "Churn", 1.94f, 1f, "{0:0.00}%", true, Pal.Pink, Icon.Heart);
        }

        void AddKpi(YauiElement row, string label, float value, float scale, string format, bool lowerIsBetter,
            Color color, Icon icon)
        {
            var card = Ui.El(row, label, Lay.Col.Grow().Pad(18f, 16f).Gap(10f), Pal.Card);
            card.Layout = card.Layout.W(0f);
            Enter(card, 0.1f + _kpis.Count * 0.07f);

            var top = Ui.El(card, "Top", Lay.Row.Items(FlexAlign.Center).Gap(10f));
            var badge = Ui.El(top, "Badge", Lay.Col.Size(30f, 30f).Fixed().Center(),
                Sty.Fill(Color.Lerp(color, Color.white, 0.84f)).Radius(9f));
            Ui.Image(badge, "Icon", _icons[(int)icon], 15f, color);
            Ui.Text(top, "Label", label, 14f, Pal.Dim, Lay.Col.Grow());

            var kpi = new Kpi
            {
                Current = value,
                Scale = scale,
                Format = format,
                LowerIsBetter = lowerIsBetter,
                History = new float[18]
            };
            kpi.Pill = Ui.El(top, "Delta", Lay.Col.Pad(8f, 3f), Sty.Fill(Ui.Hex(0xD1FAE5)).Radius(10f));
            kpi.Delta = Ui.Text(kpi.Pill, "Text", "<b>+2.4%</b>", 12f, Ui.Hex(0x047857));

            var bottom = Ui.El(card, "Bottom", Lay.Row.Items(FlexAlign.FlexEnd).Justify(FlexJustify.SpaceBetween));
            kpi.Value = Ui.Text(bottom, "Value", "<b>" + string.Format(format, value * scale) + "</b>", 30f, Pal.Text);
            kpi.Spark = Ui.Add<BarsElement>(bottom, "Spark", Lay.Col.Size(108f, 38f).Fixed(), Sty.None);
            kpi.Spark.Color = Color.Lerp(color, Color.white, 0.6f);
            kpi.Spark.LastColor = color;
            kpi.History[0] = Random.Range(0.3f, 0.8f);
            for (var i = 1; i < kpi.History.Length; i++) kpi.History[i] = Walk(kpi.History[i - 1], 0.6f);

            kpi.Spark.SetValues(kpi.History);
            _kpis.Add(kpi);
        }

        void BuildCharts(YauiElement page)
        {
            var row = Ui.El(page, "Charts", Lay.Row.H(310f).Fixed().Gap(20f));

            // The line chart: a mesh of a custom draw, between the grid under it and the labels around it.
            var card = Ui.El(row, "Traffic", Lay.Col.Grow(2.1f).W(0f).Pad(20f, 18f).Gap(12f), Pal.Card);
            Enter(card, 0.5f);
            var header = Ui.El(card, "Header", Lay.Row.Items(FlexAlign.Center).Gap(22f));
            Ui.Text(header, "Title", "<b>Traffic</b>", 16f, Pal.Text, Lay.Col.Grow());
            var names = new[] { "Visitors", "Sign-ups", "Purchases" };
            for (var i = 0; i < names.Length; i++)
            {
                var legend = Ui.El(header, names[i], Lay.Row.Items(FlexAlign.Center).Gap(7f));
                Ui.El(legend, "Dot", Lay.Col.Size(9f, 9f).Fixed(), Sty.Fill(Pal.Series[i]).Radius(5f));
                Ui.Text(legend, "Name", names[i], 13f, Pal.Dim);
                _legend.Add(Ui.Text(legend, "Value", "", 13f, Pal.Text, Lay.Col.Fixed().W(46f)));
            }

            var body = Ui.El(card, "Body", Lay.Row.Grow().Gap(10f));
            var axis = Ui.El(body, "Axis", Lay.Col.Justify(FlexJustify.SpaceBetween).Items(FlexAlign.FlexEnd));
            foreach (var label in new[] { "10k", "7.5k", "5k", "2.5k", "0" }) Ui.Text(axis, "Tick", label, 11f, Pal.Faint);

            var plot = Ui.El(body, "Plot", Lay.Col.Grow().Justify(FlexJustify.SpaceBetween));
            plot.ClipChildren = true;
            for (var i = 0; i < 5; i++) Ui.El(plot, "Grid", Lay.Col.H(1f), Sty.Fill(Pal.Line));

            var lines = Ui.El(plot, "Lines", Lay.Col.Cover());
            _chart = lines.gameObject.AddComponent<LineChart>();
            _chart.Material = _meshMaterial;
            for (var i = 0; i < 3; i++)
            {
                var series = new LineChart.Series { Color = Pal.Series[i], Fill = i == 0 ? 0.22f : 0.1f };
                var value = 0.3f + 0.2f * i;
                for (var k = 0; k < 44; k++) series.Values.Add(value = Walk(value, 0.3f + 0.2f * i));

                _chart.AllSeries.Add(series);
            }

            // Bars: one element writes twelve quads.
            card = Ui.El(row, "Revenue", Lay.Col.Grow().W(0f).Pad(20f, 18f).Gap(12f), Pal.Card);
            Enter(card, 0.58f);
            CardTitle(card, "Revenue by month", "USD, thousands");
            _bars = Ui.Add<BarsElement>(card, "Bars", Lay.Col.Grow(), Sty.None);
            _bars.Gap = 9f;
            _bars.Radius = 5f;
            _bars.Color = Ui.Hex(0xC7CBF8);
            _bars.LastColor = Pal.Accent;
            _barValues = new float[12];
            _barTargets = new float[12];
            for (var i = 0; i < 12; i++) _barTargets[i] = Random.Range(0.25f, 1f);

            var months = Ui.El(card, "Months", Lay.Row.Justify(FlexJustify.SpaceBetween).Pad(4f, 0f));
            foreach (var month in "JFMAMJJASOND") Ui.Text(months, "Month", month.ToString(), 11f, Pal.Faint);

            // A donut: sectors of the uber shader under a disc.
            card = Ui.El(row, "Plans", Lay.Col.W(300f).Fixed().Pad(20f, 18f).Gap(10f), Pal.Card);
            Enter(card, 0.66f);
            CardTitle(card, "Plans", "by seats");
            var center = Ui.El(card, "Center", Lay.Col.Grow().Center());
            var ring = Ui.El(center, "Ring", Lay.Col.Size(150f, 150f).Fixed().Center());
            var plans = new[] { "Enterprise", "Team", "Pro", "Free" };
            for (var i = 0; i < plans.Length; i++)
                _donut.Add(Ui.Radial(ring, plans[i], Lay.Col.Cover(), 75f, Pal.Series[i]));

            var hole = Ui.El(ring, "Hole", Lay.Col.Abs(24f, 24f, 24f, 24f).Center(), Sty.Fill(Pal.Surface).Radius(60f));
            _donutTotal = Ui.Text(hole, "Total", "<b>2,481</b>", 22f, Pal.Text);
            Ui.Text(hole, "Label", "customers", 11f, Pal.Faint);

            var legends = Ui.El(card, "Legend", Lay.Row.Wrap().Gap(6f).Justify(FlexJustify.Center));
            for (var i = 0; i < plans.Length; i++)
            {
                var legend = Ui.El(legends, plans[i], Lay.Row.Items(FlexAlign.Center).Gap(6f).W(118f));
                Ui.El(legend, "Dot", Lay.Col.Size(9f, 9f).Fixed(), Sty.Fill(Pal.Series[i]).Radius(5f));
                Ui.Text(legend, "Name", plans[i], 13f, Pal.Dim);
            }
        }

        void BuildHeatmap(YauiElement side)
        {
            var card = Ui.El(side, "Heatmap", Lay.Col.Pad(18f, 16f).Gap(12f), Pal.Card);
            Enter(card, 0.86f);
            CardTitle(card, "Activity by hour", "last 7 days");
            var grid = Ui.El(card, "Grid", Lay.Col.Gap(3f));
            _heatValues = new float[7 * 24];
            for (var day = 0; day < 7; day++)
            {
                var row = Ui.El(grid, "Day", Lay.Row.Gap(3f));
                for (var hour = 0; hour < 24; hour++)
                {
                    // Busy in the daytime of weekdays.
                    var busy = Mathf.Exp(-(hour - 14f) * (hour - 14f) / 40f) * (day < 5 ? 1f : 0.45f);
                    var value = Mathf.Clamp01(busy * Random.Range(0.5f, 1.1f));
                    _heatValues[_heat.Count] = value;
                    _heat.Add(Ui.El(row, "Hour", Lay.Col.Grow().H(12.2f), Sty.Fill(HeatColor(value)).Radius(3f)));
                }
            }
        }

        void BuildFeed(YauiElement side)
        {
            var card = Ui.El(side, "Feed", Lay.Col.Grow().Pad(18f, 16f).Gap(10f), Pal.Card);
            Enter(card, 0.94f);
            CardTitle(card, "Live activity", "all workspaces");
            var list = Ui.El(card, "List", Lay.Col.Grow().Gap(9f));
            list.ClipChildren = true;
            for (var i = 0; i < 6; i++)
            {
                var item = new FeedItem { Age = 1f };
                item.Element = Ui.El(list, "Item", Lay.Row.Fixed().Gap(10f));
                item.Dot = Ui.El(item.Element, "Dot", Lay.Col.Size(9f, 9f).Fixed().Margin(0f, 5f, 0f, 0f),
                    Sty.Fill(Pal.Series[i % Pal.Series.Length]).Radius(5f));
                item.Text = Ui.Text(item.Element, "Text", FeedLines[i], 13.5f, Pal.Text, Lay.Col.Grow().W(0f));
                item.Text.WordWrap = true;
                item.Time = Ui.Text(item.Element, "Time", $"{i * 2 + 1}m", 12f, Pal.Faint);
                _feed.Add(item);
            }

            _feedIndex = 6;
        }

        #endregion
    }
}
