using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace Yaui.Showcase
{
    /// <summary>
    /// An analytics dashboard like a web application, built in code at startup and fed with fake live data: a
    /// sidebar, a toolbar with working controls (search, dropdown, switch, slider), KPI cards, charts, a heatmap, an
    /// activity feed and a scrolling table of hundreds of rows.
    /// </summary>
    public sealed partial class DashboardShowcase : MonoBehaviour
    {
        [SerializeField] Shader meshShader;

        [SerializeField] int rowCount = 240;

        /// <summary>Scrolls the table by itself until the pointer is used.</summary>
        [SerializeField] bool autoScroll = true;

        static class Pal
        {
            public static readonly Color Background = Ui.Hex(0xF2F4F9);
            public static readonly Color Surface = Color.white;
            public static readonly Color Line = Ui.Hex(0xE3E7F0);
            public static readonly Color Text = Ui.Hex(0x111827);
            public static readonly Color Dim = Ui.Hex(0x6B7280);
            public static readonly Color Faint = Ui.Hex(0x9AA3B5);
            public static readonly Color Accent = Ui.Hex(0x4F46E5);
            public static readonly Color Green = Ui.Hex(0x10B981);
            public static readonly Color Red = Ui.Hex(0xEF4444);
            public static readonly Color Amber = Ui.Hex(0xF59E0B);
            public static readonly Color Sky = Ui.Hex(0x0EA5E9);
            public static readonly Color Pink = Ui.Hex(0xEC4899);
            public static readonly Color Navy = Ui.Hex(0x0F172A);
            public static readonly Color[] Series = { Accent, Sky, Pink, Amber, Green };

            public static BoxStyle Card => Sty.Fill(Surface).Radius(14f).Border(1f, Line)
                .Shadow(Ui.Hex(0x0F172A, 0.07f), 12f, 0f, new Vector2(0f, 6f));
        }

        sealed class Kpi
        {
            public YauiText Value;
            public YauiText Delta;
            public YauiElement Pill;
            public BarsElement Spark;
            public float[] History;
            public float Current;
            public float Scale;
            public string Format;
            public bool LowerIsBetter;
        }

        sealed class Row
        {
            public YauiElement Element;
            public string Search;
            public YauiText Mrr;
            public YauiElement Flash;
            public YauiElement UsageFill;
            public YauiText UsageLabel;
            public BarsElement Trend;
            public float[] History;
            public float Revenue;
            public float Usage;
            public float FlashTime;
            public Color FlashColor;
        }

        sealed class FeedItem
        {
            public YauiElement Element;
            public YauiElement Dot;
            public YauiText Text;
            public YauiText Time;
            public float Age;
        }

        // Never fully transparent: a box that appears and disappears rebuilds the draw order of the panel.
        static readonly Color FlashIdle = new(1f, 1f, 1f, 0.004f);

        struct Entrance
        {
            public YauiElement Element;
            public float Delay;
        }

        readonly List<YauiPanel> _panels = new();
        readonly ShowcaseStats _counter = new();
        readonly List<Entrance> _entrances = new();
        readonly List<Kpi> _kpis = new();
        readonly List<Row> _rows = new();
        readonly List<Row> _flashing = new();
        readonly List<FeedItem> _feed = new();
        readonly List<YauiElement> _heat = new();
        readonly List<RadialElement> _donut = new();
        readonly List<YauiText> _donutLabels = new();
        readonly float[] _shares = { 0.42f, 0.27f, 0.19f, 0.12f };
        readonly float[] _shareTargets = { 0.42f, 0.27f, 0.19f, 0.12f };
        float[] _heatValues;

        Sprite[] _icons;
        Material _meshMaterial;
        YauiPanel _panel;
        YauiElement _root;
        LineChart _chart;
        readonly List<YauiText> _legend = new();
        BarsElement _bars;
        float[] _barValues;
        float[] _barTargets;
        YauiText _donutTotal;
        YauiScrollView _scroll;
        YauiText _tableCount;
        YauiText _stats;
        YauiText _statsDetail;
        YauiText _clock;

        bool _live = true;
        float _speed = 1f;
        float _time;
        float _chartPhase;
        float _rowBudget;
        float _feedTimer = 1.2f;
        float _slowTimer;
        float _statsTimer;
        float _kpiTimer;
        float _scrollPause;
        float _scrollDirection = 1f;
        float _smoothedDelta = 1f / 60f;
        int _feedIndex;

        void Start()
        {
            Random.InitState(20261002);
            _icons = IconFactory.CreateAll();
            var shader = meshShader != null ? meshShader : Shader.Find("Yaui/Particle");
            _meshMaterial = new Material(shader) { mainTexture = Texture2D.whiteTexture };

            if (EventSystem.current == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem));
                events.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }

            BuildDashboard();
        }

        void Update()
        {
            var dt = Time.deltaTime;
            _smoothedDelta = Mathf.Lerp(_smoothedDelta, Time.unscaledDeltaTime, 0.05f);
            _time += dt;
            UpdateEntrances();
            UpdateFlashes(dt);
            UpdateFeedAnimation(dt);
            UpdateAutoScroll(dt);

            if (_live)
            {
                var step = dt * _speed;
                UpdateChart(step);
                UpdateBars(step);
                UpdateDonut(step);
                UpdateHeat(step);

                _rowBudget += step * 14f;
                while (_rowBudget >= 1f)
                {
                    _rowBudget -= 1f;
                    UpdateRandomRow();
                }

                _kpiTimer -= step;
                if (_kpiTimer <= 0f)
                {
                    _kpiTimer = 0.6f;
                    UpdateKpis();
                }

                _feedTimer -= step;
                if (_feedTimer <= 0f)
                {
                    _feedTimer = Random.Range(1.4f, 2.6f);
                    PostFeed();
                }
            }

            _slowTimer -= dt;
            if (_slowTimer <= 0f)
            {
                _slowTimer = 0.25f;
                UpdateStats();
            }
        }

        // Cards rise and fade in one after another: transforms and opacity only, no layout.
        void UpdateEntrances()
        {
            for (var i = _entrances.Count - 1; i >= 0; i--)
            {
                var e = _entrances[i];
                var t = Mathf.Clamp01((_time - e.Delay) / 0.45f);
                var eased = 1f - (1f - t) * (1f - t) * (1f - t);
                e.Element.Opacity = eased;
                e.Element.Translate = new Vector2(0f, (1f - eased) * 28f);
                if (t >= 1f) _entrances.RemoveAt(i);
            }
        }

        void Enter(YauiElement element, float delay)
        {
            element.Opacity = 0f;
            _entrances.Add(new Entrance { Element = element, Delay = delay });
        }

        #region Live data

        void UpdateChart(float step)
        {
            _chartPhase += step * 1.1f;
            while (_chartPhase >= 1f)
            {
                _chartPhase -= 1f;
                for (var i = 0; i < _chart.AllSeries.Count; i++)
                {
                    var values = _chart.AllSeries[i].Values;
                    var last = values[values.Count - 1];
                    values.RemoveAt(0);
                    values.Add(Walk(last, 0.3f + 0.2f * i));
                    _legend[i].Text = $"<b>{last * (9200f - i * 2100f):N0}</b>";
                }
            }

            _chart.Phase = _chartPhase;
        }

        static float Walk(float value, float center)
        {
            var next = value + Random.Range(-0.09f, 0.09f) + (center - value) * 0.08f;
            return Mathf.Clamp(next, 0.06f, 0.94f);
        }

        void UpdateBars(float step)
        {
            if (Random.value < step * 1.5f) _barTargets[Random.Range(0, _barTargets.Length)] = Random.Range(0.25f, 1f);

            for (var i = 0; i < _barValues.Length; i++)
                _barValues[i] = Mathf.Lerp(_barValues[i], _barTargets[i], 1f - Mathf.Exp(-step * 4f));

            // One element, twelve quads: the values are written straight into its primitives.
            _bars.SetValues(_barValues);
        }

        void UpdateDonut(float step)
        {
            if (Random.value < step * 0.5f)
            {
                var sum = 0f;
                for (var i = 0; i < _shareTargets.Length; i++) sum += _shareTargets[i] = Random.Range(0.1f, 0.5f);
                for (var i = 0; i < _shareTargets.Length; i++) _shareTargets[i] /= sum;
            }

            var start = -90f;
            for (var i = 0; i < _shares.Length; i++)
            {
                _shares[i] = Mathf.Lerp(_shares[i], _shareTargets[i], 1f - Mathf.Exp(-step * 2f));
                _donut[i].SetArc(start, _shares[i] * 360f);
                start += _shares[i] * 360f;
            }
        }

        void UpdateHeat(float step)
        {
            // A few cells change a frame: only their colors are written.
            var changes = Mathf.Max(1, Mathf.RoundToInt(step * 90f));
            for (var i = 0; i < changes; i++)
            {
                var index = Random.Range(0, _heat.Count);
                _heatValues[index] = Mathf.Clamp01(_heatValues[index] + Random.Range(-0.3f, 0.3f));
                _heat[index].BackgroundColor = HeatColor(_heatValues[index]);
            }
        }

        static Color HeatColor(float value)
        {
            return Color.Lerp(Ui.Hex(0xE9ECFB), Pal.Accent, value * value);
        }

        void UpdateKpis()
        {
            var kpi = _kpis[Random.Range(0, _kpis.Count)];
            var history = kpi.History;
            for (var i = 0; i < history.Length - 1; i++) history[i] = history[i + 1];

            history[^1] = Walk(history[^1], 0.6f);
            kpi.Spark.SetValues(history);

            var change = (history[^1] - history[^2]) * 12f;
            kpi.Current = Mathf.Max(kpi.Current * (1f + change * 0.01f), 0f);
            kpi.Value.Text = "<b>" + string.Format(kpi.Format, kpi.Current * kpi.Scale) + "</b>";
            var up = change >= 0f;
            var good = up != kpi.LowerIsBetter;
            kpi.Delta.Text = $"<b>{(up ? "+" : "")}{change:0.0}%</b>";
            kpi.Delta.Color = good ? Ui.Hex(0x047857) : Ui.Hex(0xB91C1C);
            kpi.Pill.BackgroundColor = good ? Ui.Hex(0xD1FAE5) : Ui.Hex(0xFEE2E2);
        }

        void UpdateRandomRow()
        {
            var row = _rows[Random.Range(0, _rows.Count)];
            if (!row.Element.gameObject.activeSelf) return;

            var delta = Random.Range(-0.08f, 0.1f);
            row.Revenue = Mathf.Max(row.Revenue * (1f + delta), 12f);
            row.Mrr.Text = $"<b>${row.Revenue:N0}</b>";
            row.Usage = Mathf.Clamp01(row.Usage + Random.Range(-0.12f, 0.14f));
            SetUsage(row);

            var history = row.History;
            for (var i = 0; i < history.Length - 1; i++) history[i] = history[i + 1];

            history[^1] = Mathf.Clamp01(history[^1] + delta * 3f);
            row.Trend.SetValues(history);

            // The change flashes behind the number: the color of a box, no layout and no text generation.
            row.FlashColor = delta >= 0f ? Pal.Green : Pal.Red;
            if (row.FlashTime <= 0f) _flashing.Add(row);

            row.FlashTime = 1f;
        }

        static void SetUsage(Row row)
        {
            var percent = Mathf.Round(row.Usage * 100f);
            row.UsageLabel.Text = $"{percent:0}%";
            row.UsageFill.BackgroundColor = row.Usage > 0.85f ? Pal.Red : row.Usage > 0.65f ? Pal.Amber : Pal.Accent;
            var layout = row.UsageFill.Layout;
            layout.width = Length.Percent(percent);
            row.UsageFill.Layout = layout;
        }

        void UpdateFlashes(float dt)
        {
            for (var i = _flashing.Count - 1; i >= 0; i--)
            {
                var row = _flashing[i];
                row.FlashTime -= dt * 1.4f;
                var c = row.FlashColor;
                c.a = Mathf.Max(row.FlashTime * 0.32f, FlashIdle.a);
                row.Flash.BackgroundColor = c;
                if (row.FlashTime <= 0f) _flashing.RemoveAt(i);
            }
        }

        #endregion

        #region Controls

        void OnSearch(string query)
        {
            query = query.Trim().ToLowerInvariant();
            var shown = 0;
            foreach (var row in _rows)
            {
                var match = query.Length == 0 || row.Search.Contains(query);
                if (match) shown++;

                // Hidden rows leave the layout: the table closes up.
                if (row.Element.gameObject.activeSelf != match) row.Element.gameObject.SetActive(match);
            }

            _tableCount.Text = $"{shown:N0} of {_rows.Count:N0} customers";
            _scroll.ScrollPosition = Vector2.zero;
            _scrollPause = 6f;
        }

        void OnRange(int index)
        {
            // Another range is other data: the chart starts over.
            foreach (var series in _chart.AllSeries)
                for (var i = 0; i < series.Values.Count; i++)
                    series.Values[i] = i == 0 ? Random.Range(0.2f, 0.8f) : Walk(series.Values[i - 1], 0.5f);

            for (var i = 0; i < _barTargets.Length; i++) _barTargets[i] = Random.Range(0.25f, 1f);
        }

        void UpdateAutoScroll(float dt)
        {
            var mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.isPressed || mouse.scroll.ReadValue() != Vector2.zero))
                _scrollPause = 6f;

            _scrollPause -= dt;
            if (!autoScroll || _scrollPause > 0f || _time < 2.5f) return;

            var range = _scroll.ScrollRange.y;
            if (range <= 0f) return;

            var position = _scroll.ScrollPosition;
            position.y += _scrollDirection * dt * 70f;
            if (position.y >= range || position.y <= 0f) _scrollDirection = -_scrollDirection;

            position.y = Mathf.Clamp(position.y, 0f, range);
            _scroll.ScrollPosition = position;
        }

        #endregion

        #region Feed

        static readonly string[] FeedLines =
        {
            "<b>Mira Tanaka</b> upgraded to <color=#4F46E5><b>Enterprise</b></color>",
            "<b>田中 美咲</b> さんがチームに 12 人を招待しました",
            "New signup from <b>Berlin</b>: <b>Jonas Weber</b>",
            "<b>Nordlicht GmbH</b> exported <i>Q3 revenue report</i>",
            "<b>김서연</b> 님이 대시보드를 공유했습니다",
            "Payment of <color=#10B981><b>$4,280</b></color> received from <b>Acme Corp</b>",
            "<b>Yusuf Demir</b> created the funnel <i>Onboarding v3</i>",
            "<color=#EF4444><b>Past due</b></color>: invoice #20418 for <b>Lumen Labs</b>",
            "<b>佐藤 健</b> さんがレポート「週次 KPI」を更新しました",
            "<b>Noor Haddad</b> commented: «ممتاز، شكراً»",
            "Trial started: <b>Pixel Foundry</b> (14 days)",
            "<b>Ana Souza</b> connected <b>Stripe</b> and <b>BigQuery</b>"
        };

        void PostFeed()
        {
            // The oldest item moves to the top with the new text: a change of sibling order.
            var item = _feed[^1];
            _feed.RemoveAt(_feed.Count - 1);
            _feed.Insert(0, item);
            item.Element.transform.SetAsFirstSibling();
            item.Text.Text = FeedLines[_feedIndex % FeedLines.Length];
            item.Dot.BackgroundColor = Pal.Series[_feedIndex % Pal.Series.Length];
            item.Time.Text = "now";
            item.Age = 0f;
            _feedIndex++;
            for (var i = 1; i < _feed.Count; i++) _feed[i].Time.Text = $"{i * 2 + _feedIndex % 2}m";
        }

        void UpdateFeedAnimation(float dt)
        {
            var item = _feed[0];
            if (item.Age >= 1f) return;

            item.Age = Mathf.Min(item.Age + dt * 3f, 1f);
            var eased = 1f - (1f - item.Age) * (1f - item.Age);
            item.Element.Opacity = eased;
            item.Element.Translate = new Vector2((1f - eased) * 40f, 0f);
        }

        #endregion

        void UpdateStats()
        {
            _statsTimer -= 0.25f;
            if (_statsTimer <= 0f)
            {
                _statsTimer = 1f;
                _counter.CountElements(_panels);
            }

            _counter.CountDraws(_panels);
            _stats.Text = $"<b>{_counter.Primitives:N0}</b> quads\n<b>{_counter.Elements:N0}</b> elements";
            _statsDetail.Text = $"<b>{_counter.DrawCalls}</b> draw calls\n<b>{1f / _smoothedDelta:0}</b> fps";
            var now = System.DateTime.Now;
            _clock.Text = $"Updated {now:HH:mm:ss}";
        }
    }
}
