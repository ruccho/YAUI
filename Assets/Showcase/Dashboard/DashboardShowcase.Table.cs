using UnityEngine;

namespace Yaui.Showcase
{
    // The customers: hundreds of rows in a scroll view. Scrolling moves the content by its transform, so it never
    // lays out, and the rows outside the viewport are clipped away in the vertex shader.
    public sealed partial class DashboardShowcase
    {
        const float CountryWidth = 124f;
        const float StatusWidth = 104f;
        const float PlanWidth = 100f;
        const float UsageWidth = 156f;
        const float RevenueWidth = 104f;
        const float TrendWidth = 84f;

        static readonly string[] FirstNames =
        {
            "Mira", "Jonas", "Ana", "Yusuf", "Noor", "Liam", "Sofia", "Kenji", "Elena", "Omar", "Priya", "Lucas",
            "Ingrid", "Mateo", "Chloe", "Arjun", "Hana", "Felix", "Zoe", "Tariq"
        };

        static readonly string[] LastNames =
        {
            "Tanaka", "Weber", "Souza", "Demir", "Haddad", "Murphy", "Rossi", "Sato", "Petrova", "Farouk", "Nair",
            "Martin", "Larsen", "Garcia", "Dubois", "Mehta", "Kim", "Novak", "Clarke", "Aziz"
        };

        static readonly string[] LocalNames =
            { "田中 美咲", "佐藤 健", "鈴木 花子", "김서연", "박지훈", "王伟", "李娜", "高橋 涼" };

        static readonly string[] Companies =
        {
            "Acme Corp", "Nordlicht GmbH", "Lumen Labs", "Pixel Foundry", "Kumo Systems", "Atlas & Co", "Brightside",
            "Orbital", "Fjord Analytics", "Quanta", "Hikari Works", "Maple Ridge"
        };

        static readonly string[] Countries =
        {
            "Japan", "Germany", "Brazil", "Türkiye", "United States", "Ireland", "Italy", "France", "India", "Korea",
            "Norway", "Spain", "Canada", "Egypt"
        };

        static readonly string[] Plans = { "Enterprise", "Team", "Pro", "Free" };

        void BuildTable(YauiElement bottom)
        {
            var card = Ui.El(bottom, "Customers", Lay.Col.Grow().W(0f), Pal.Card);
            Enter(card, 0.78f);

            var top = Ui.El(card, "Top", Lay.Row.H(56f).Fixed().Items(FlexAlign.Center).Gap(10f).Pad(20f, 0f));
            Ui.Text(top, "Title", "<b>Customers</b>", 16f, Pal.Text);
            _tableCount = Ui.Text(top, "Count", $"{rowCount:N0} of {rowCount:N0} customers", 13f, Pal.Faint,
                Lay.Col.Grow());
            foreach (var chip in new[] { "All", "Active", "Trial", "Past due" })
            {
                var active = chip == "All";
                var element = Ui.El(top, chip, Lay.Col.Pad(13f, 5f),
                    Sty.Fill(active ? Ui.Hex(0xEEF0FE) : Pal.Surface).Radius(14f)
                        .Border(1f, active ? Ui.Hex(0xC7CBF8) : Pal.Line)).Hit();
                Ui.Text(element, "Text", active ? "<b>" + chip + "</b>" : chip, 13f, active ? Pal.Accent : Pal.Dim);
                element.gameObject.AddComponent<YauiButton>().Colors = Ui.Tints();
            }

            var header = Ui.El(card, "Columns", RowLayout(34f), Sty.Fill(Ui.Hex(0xF7F8FC)));
            Ui.El(header, "Check", Lay.Col.Size(18f, 18f).Fixed());
            Column(header, "CUSTOMER", Lay.Col.Grow().W(0f));
            Column(header, "COUNTRY", Lay.Col.W(CountryWidth).Fixed());
            Column(header, "STATUS", Lay.Col.W(StatusWidth).Fixed());
            Column(header, "PLAN", Lay.Col.W(PlanWidth).Fixed());
            Column(header, "USAGE", Lay.Col.W(UsageWidth).Fixed());
            Column(header, "MRR", Lay.Col.W(RevenueWidth).Fixed()).Align = TextAlign.Right;
            Column(header, "30 DAYS", Lay.Col.W(TrendWidth).Fixed());
            Ui.El(header, "Scrollbar", Lay.Col.W(14f).Fixed());

            // The scroll view: a viewport that clips, a content that moves, and a scrollbar.
            var scroll = Ui.El(card, "Scroll", Lay.Row.Grow());
            var viewport = Ui.El(scroll, "Viewport", Lay.Col.Grow());
            viewport.ClipChildren = true;
            var content = Ui.El(viewport, "Content", Lay.Col.Abs(0f, 0f, 0f, Lay.Auto));
            var track = Ui.El(scroll, "Scrollbar", Lay.Col.W(8f).Fixed().Margin(3f, 6f, 5f, 12f),
                Sty.Fill(Ui.Hex(0xEEF0F6)).Radius(4f)).Hit();
            var handle = Ui.El(track, "Handle", Lay.Col, Sty.Fill(Ui.Hex(0xBFC6D6)).Radius(4f)).Hit();
            var scrollbar = track.gameObject.AddComponent<YauiScrollbar>();
            scrollbar.Handle = handle;
            scrollbar.TargetElement = handle;
            scrollbar.Direction = TrackDirection.TopToBottom;
            scrollbar.Colors = Ui.Tints(0.85f, 0.7f);

            for (var i = 0; i < rowCount; i++) _rows.Add(BuildRow(content, i));

            _scroll = scroll.gameObject.AddComponent<YauiScrollView>();
            _scroll.Viewport = viewport;
            _scroll.Content = content;
            _scroll.Horizontal = false;
            _scroll.VerticalScrollbar = scrollbar;
            _scroll.ScrollSensitivity = 44f;
        }

        static LayoutStyle RowLayout(float height)
        {
            return Lay.Row.H(height).Fixed().Items(FlexAlign.Center).Gap(14f).Pad(20f, 0f);
        }

        static YauiText Column(YauiElement header, string label, LayoutStyle layout)
        {
            return Ui.Text(header, label, "<b>" + label + "</b>", 11f, Pal.Faint, layout);
        }

        Row BuildRow(YauiElement content, int index)
        {
            var local = index % 6 == 4;
            var name = local
                ? LocalNames[index / 6 % LocalNames.Length]
                : FirstNames[index * 7 % FirstNames.Length] + " " + LastNames[index * 3 % LastNames.Length];
            var company = Companies[index * 5 % Companies.Length];
            var country = Countries[index * 11 % Countries.Length];
            var plan = Plans[Mathf.Min((int)(Random.value * Random.value * 5f), 3) ^ 3];
            var status = Random.value;
            var color = Pal.Series[index % Pal.Series.Length];

            // A row is a button: it tints under the pointer. Its events bubble up to the scroll view.
            var element = Ui.El(content, "Row", RowLayout(46f),
                Sty.Fill(index % 2 == 0 ? Pal.Surface : Ui.Hex(0xFAFBFD))).Hit();
            element.gameObject.AddComponent<YauiButton>().Colors = Ui.Tints(0.955f, 0.91f);

            var check = Ui.El(element, "Check", Lay.Col.Size(18f, 18f).Fixed(),
                Sty.Fill(Pal.Surface).Radius(5f).Border(1.5f, Ui.Hex(0xC5CCDA))).Hit();
            var mark = Ui.El(check, "On", Lay.Col.Abs(-1.5f, -1.5f, -1.5f, -1.5f).Center(),
                Sty.Fill(Pal.Accent).Radius(5f));
            Ui.Image(mark, "Icon", _icons[(int)Icon.Check], 11f, Color.white);
            var toggle = check.gameObject.AddComponent<YauiToggle>();
            toggle.Graphic = mark;
            toggle.Colors = Ui.Tints();
            toggle.SetIsOnWithoutNotify(index % 7 == 2);

            var customer = Ui.El(element, "Customer", Lay.Row.Grow().W(0f).Items(FlexAlign.Center).Gap(11f));
            var initials = local ? name.Substring(0, 1) : name.Substring(0, 1) + name.Substring(name.IndexOf(' ') + 1, 1);
            Avatar(customer, initials, color, 30f);
            var who = Ui.El(customer, "Who", Lay.Col);
            Ui.Text(who, "Name", "<b>" + name + "</b>", 14f, Pal.Text);
            Ui.Text(who, "Company", company, 12f, Pal.Faint);

            Ui.Text(element, "Country", country, 13.5f, Pal.Dim, Lay.Col.W(CountryWidth).Fixed());

            var (label, background, foreground) = status < 0.62f
                ? ("Active", Ui.Hex(0xD1FAE5), Ui.Hex(0x047857))
                : status < 0.8f
                    ? ("Trial", Ui.Hex(0xFEF3C7), Ui.Hex(0xB45309))
                    : status < 0.92f
                        ? ("Past due", Ui.Hex(0xFEE2E2), Ui.Hex(0xB91C1C))
                        : ("Paused", Ui.Hex(0xE5E7EB), Ui.Hex(0x4B5563));
            var statusCell = Ui.El(element, "Status", Lay.Row.W(StatusWidth).Fixed());
            var pill = Ui.El(statusCell, "Pill", Lay.Row.Items(FlexAlign.Center).Gap(6f).Pad(9f, 3f),
                Sty.Fill(background).Radius(11f));
            Ui.El(pill, "Dot", Lay.Col.Size(6f, 6f).Fixed(), Sty.Fill(foreground).Radius(3f));
            Ui.Text(pill, "Text", "<b>" + label + "</b>", 12f, foreground);

            Ui.Text(element, "Plan", plan, 13.5f, Pal.Text, Lay.Col.W(PlanWidth).Fixed());

            var row = new Row
            {
                Element = element,
                Search = (name + " " + company + " " + country + " " + plan + " " + label).ToLowerInvariant(),
                Revenue = Random.Range(40f, 900f) * (plan == "Enterprise" ? 14f : plan == "Team" ? 5f : 1f),
                Usage = Random.value,
                History = new float[10]
            };

            var usage = Ui.El(element, "Usage", Lay.Row.W(UsageWidth).Fixed().Items(FlexAlign.Center).Gap(9f));
            var rail = Ui.El(usage, "Rail", Lay.Col.Size(104f, 6f).Fixed(), Sty.Fill(Ui.Hex(0xE8EBF3)).Radius(3f));
            row.UsageFill = Ui.El(rail, "Fill", Lay.Col.Abs(0f, 0f, Lay.Auto, 0f), Sty.Fill(Pal.Accent).Radius(3f));
            row.UsageLabel = Ui.Text(usage, "Label", "", 12f, Pal.Dim);
            SetUsage(row);

            var revenue = Ui.El(element, "MRR", Lay.Row.W(RevenueWidth).Fixed().Justify(FlexJustify.FlexEnd));
            row.Flash = Ui.El(revenue, "Flash", Lay.Col.Pad(7f, 3f), Sty.Fill(FlashIdle).Radius(7f));
            row.Mrr = Ui.Text(row.Flash, "Text", $"<b>${row.Revenue:N0}</b>", 14f, Pal.Text);

            row.Trend = Ui.Add<BarsElement>(element, "Trend", Lay.Col.Size(TrendWidth, 24f).Fixed(), Sty.None);
            row.Trend.Color = Color.Lerp(color, Color.white, 0.55f);
            row.Trend.LastColor = color;
            row.History[0] = Random.Range(0.2f, 0.9f);
            for (var i = 1; i < row.History.Length; i++) row.History[i] = Walk(row.History[i - 1], 0.55f);

            row.Trend.SetValues(row.History);
            return row;
        }
    }
}
