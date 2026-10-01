using UnityEngine;

namespace Yaui.Showcase
{
    // The screen: a header, three panels side by side (character, grid, details) and a footer.
    public sealed partial class InventoryShowcase
    {
        const float SlotSize = 75f;

        void BuildScreen()
        {
            var go = new GameObject("Inventory");
            _panel = go.AddComponent<YauiPanel>();
            _panel.ReferenceResolution = new Vector2(1920f, 1080f);
            _panel.Match = 0.5f;
            go.AddComponent<YauiRaycaster>();
            _panels.Add(_panel);
            _root = go.GetComponent<YauiElement>();
            _root.RaycastTarget = false;

            // The backdrop: a custom shader over the whole screen, and dust drifting up in front of it.
            var backdrop = Ui.El(_root, "Backdrop", Lay.Col.Cover(), Sty.Fill(Color.white));
            backdrop.Material = new Material(auroraShader != null ? auroraShader : Shader.Find("Yaui/Showcase/Aurora"));

            var dustLayer = Ui.El(_root, "Dust", Lay.Col.Cover());
            var dust = Fx.NewSystem(dustLayer.gameObject, _particles);
            var main = dust.main;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 12f);
            main.startSize = new ParticleSystem.MinMaxCurve(4f, 13f);
            main.startColor = new Color(1f, 0.82f, 0.5f, 0.55f);
            var emission = dust.emission;
            emission.enabled = true;
            emission.rateOverTime = 16f;
            var shape = dust.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(2000f, 1100f, 1f);
            var velocity = dust.velocityOverLifetime;
            velocity.enabled = true;
            velocity.x = new ParticleSystem.MinMaxCurve(-12f, 12f);
            velocity.y = new ParticleSystem.MinMaxCurve(8f, 34f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            Fx.Fade(dust);
            dust.Play();
            dustLayer.gameObject.AddComponent<YauiParticle>();

            BuildHeader();
            var body = Ui.El(_root, "Body", Lay.Row.Grow().Gap(22f).Pad(28f, 0f));
            BuildGrid(body);
            BuildCharacter(body);
            BuildDetail(body);
            BuildFooter();
            UpdateCapacity();
        }

        static TransitionColors Brighten(float normal = 0.86f)
        {
            // The tint only darkens: the normal state is the darker one, so the pointer brightens.
            var colors = TransitionColors.Default;
            colors.normal = new Color(normal, normal, normal, 1f);
            colors.highlighted = colors.selected = Color.white;
            colors.pressed = new Color(0.7f, 0.7f, 0.7f, 1f);
            return colors;
        }

        YauiElement HoloFrame(YauiElement parent, float radius)
        {
            var frame = Ui.El(parent, "Holo", Lay.Col.Cover(), Sty.Fill(Color.white).Radius(radius));
            frame.Material = _holo;
            return frame;
        }

        #region Header and footer

        void BuildHeader()
        {
            var header = Ui.El(_root, "Header", Lay.Row.H(84f).Fixed().Items(FlexAlign.Center).Gap(26f).Pad(32f, 0f));
            Ui.Text(header, "Title", "<b>INVENTORY</b>", 30f, Pal.Text).Shadowed(5f);

            var tabs = Ui.El(header, "Screens", Lay.Row.Gap(6f).Margin(18f, 0f, 0f, 0f));
            foreach (var name in new[] { "Character", "Inventory", "Skills", "Quests", "Codex" })
            {
                var active = name == "Inventory";
                var tab = Ui.El(tabs, name, Lay.Col.Pad(16f, 8f).Items(FlexAlign.Center).Gap(5f),
                    Sty.Fill(Ui.Hex(0xFFFFFF, active ? 0.08f : 0.02f)).Radius(10f)).Hit();
                Ui.Text(tab, "Label", active ? "<b>" + name + "</b>" : name, 16f, active ? Pal.Gold : Pal.Dim);
                Ui.El(tab, "Underline", Lay.Col.Size(22f, 3f).Fixed(),
                    Sty.Fill(active ? Pal.Gold : Color.clear).Radius(2f));
                tab.gameObject.AddComponent<YauiButton>().Colors = Brighten(0.7f);
            }

            Ui.El(header, "Spacer", Lay.Col.Grow());

            _gold = Currency(header, Icon.Circle, Pal.Gold, $"<b>{_goldShown:N0}</b>");
            Currency(header, Icon.Diamond, Ui.Hex(0x67E8F9), "<b>2,480</b>");
            Currency(header, Icon.Star, Pal.Rarity[3], "<b>37</b>");
        }

        YauiText Currency(YauiElement parent, Icon icon, Color color, string value)
        {
            var pill = Ui.El(parent, "Currency", Lay.Row.H(40f).Fixed().Items(FlexAlign.Center).Gap(9f).Pad(14f, 0f),
                Sty.Fill(Ui.Hex(0x0C0814, 0.7f)).Radius(20f).Border(1f, new Color(color.r, color.g, color.b, 0.4f)));
            Ui.Image(pill, "Icon", _icons[(int)icon], 18f, color);
            return Ui.Text(pill, "Value", value, 16f, Pal.Text);
        }

        void BuildFooter()
        {
            var footer = Ui.El(_root, "Footer", Lay.Row.H(70f).Fixed().Items(FlexAlign.Center).Gap(22f).Pad(32f, 0f));
            Hint(footer, "LMB", "Select");
            Hint(footer, "E", "Equip");
            Hint(footer, "R", "Enhance");
            Hint(footer, "X", "Sell");
            Hint(footer, "Wheel", "Scroll");
            Ui.El(footer, "Spacer", Lay.Col.Grow());

            var stats = Ui.El(footer, "Stats", Lay.Row.Items(FlexAlign.Center).Gap(16f).Pad(18f, 8f),
                Sty.Fill(Ui.Hex(0x0C0814, 0.8f)).Radius(14f).Border(1f, Ui.Hex(0xF5C451, 0.5f)));
            Ui.Text(stats, "Logo", "<b>YAUI</b>", 22f, Pal.Gold);
            _stats = Ui.Text(stats, "Counts", "", 15f, Pal.Text);
            _statsDetail = Ui.Text(stats, "Draws", "", 15f, Pal.Text);
        }

        static void Hint(YauiElement parent, string key, string label)
        {
            var hint = Ui.El(parent, label, Lay.Row.Items(FlexAlign.Center).Gap(8f));
            var cap = Ui.El(hint, "Key", Lay.Col.Pad(9f, 3f).Center(),
                Sty.Fill(Ui.Hex(0xFFFFFF, 0.1f)).Radius(6f).Border(1f, Ui.Hex(0xFFFFFF, 0.25f)));
            Ui.Text(cap, "Text", "<b>" + key + "</b>", 13f, Pal.Text);
            Ui.Text(hint, "Label", label, 15f, Pal.Dim);
        }

        #endregion

        #region Grid

        void BuildGrid(YauiElement body)
        {
            var panel = Ui.El(body, "Grid", Lay.Col.Grow().W(0f).Pad(18f).Gap(14f), Pal.Panel);
            // The grid is the middle panel: it is built first (the character takes its gear from it).
            panel.transform.SetSiblingIndex(0);

            var top = Ui.El(panel, "Top", Lay.Row.Items(FlexAlign.Center).Gap(8f));
            for (var i = 0; i < CategoryNames.Length; i++)
            {
                var index = i;
                var tab = Ui.El(top, CategoryNames[i], Lay.Col.Pad(14f, 6f), Sty.None).Hit();
                _tabLabels.Add(Ui.Text(tab, "Label", "<b>" + CategoryNames[i] + "</b>", 14f, Pal.Dim));
                var button = tab.gameObject.AddComponent<YauiButton>();
                button.Colors = Brighten(0.8f);
                button.OnClick.AddListener(() => SetCategory(index));
                _tabs.Add(tab);
            }

            _capacity = Ui.Text(top, "Capacity", "", 14f, Pal.Dim, Lay.Col.Grow());
            _capacity.Align = TextAlign.Right;

            // The scroll view: the viewport clips to its rounded box per pixel, the content wraps the slots.
            var scroll = Ui.El(panel, "Scroll", Lay.Row.Grow().Gap(8f));
            var viewport = Ui.El(scroll, "Viewport", Lay.Col.Grow(), Sty.None.Radius(14f));
            viewport.ClipChildren = true;
            _content = Ui.El(viewport, "Content", Lay.Row.Abs(0f, 0f, 0f, Lay.Auto).Wrap().Gap(8f).Pad(3f));

            for (var i = 0; i < slotCount; i++)
            {
                var slot = BuildSlot(_content);
                // The last rows are free.
                if (i < slotCount - 30 && Random.value < 0.93f) slot.Item = RandomItem();

                Refresh(slot);
                _slots.Add(slot);
            }

            _frames = Ui.El(_content, "Frames", Lay.Col.Cover());
            foreach (var slot in _slots)
                if (slot.Item != null && slot.Item.Rarity == 4)
                    Refresh(slot);

            // The ring around the selected slot, moved by its transform.
            _selection = Ui.El(_content, "Selection",
                Lay.Col.Abs(-3f, -3f, Lay.Auto, Lay.Auto).Size(SlotSize + 6f, SlotSize + 6f),
                Sty.None.Radius(15f).Border(3f, Color.white));

            // Sparkles of the legendary items: one system over the content, clipped with it.
            var sparkles = Ui.El(_content, "Sparkles", Lay.Col.Cover());
            _gridSparkles = Fx.NewSystem(sparkles.gameObject, _particles);
            Fx.Fade(_gridSparkles);
            _gridSparkles.Play();
            sparkles.gameObject.AddComponent<YauiParticle>();

            var track = Ui.El(scroll, "Scrollbar", Lay.Col.W(8f).Fixed(), Sty.Fill(Ui.Hex(0xFFFFFF, 0.06f)).Radius(4f))
                .Hit();
            var handle = Ui.El(track, "Handle", Lay.Col, Sty.Fill(Ui.Hex(0xF5C451, 0.7f)).Radius(4f)).Hit();
            var scrollbar = track.gameObject.AddComponent<YauiScrollbar>();
            scrollbar.Handle = handle;
            scrollbar.TargetElement = handle;
            scrollbar.Direction = TrackDirection.TopToBottom;
            scrollbar.Colors = Brighten();

            _scroll = scroll.gameObject.AddComponent<YauiScrollView>();
            _scroll.Viewport = viewport;
            _scroll.Content = _content;
            _scroll.Horizontal = false;
            _scroll.VerticalScrollbar = scrollbar;
            _scroll.ScrollSensitivity = 60f;

            SetCategory(0);
        }

        Slot BuildSlot(YauiElement content)
        {
            var slot = new Slot();
            // A slot is a button; its events bubble up to the scroll view.
            slot.Element = Ui.El(content, "Slot", Lay.Col.Size(SlotSize, SlotSize).Fixed().Center()).Hit();
            var button = slot.Element.gameObject.AddComponent<YauiButton>();
            button.Colors = Brighten();
            button.OnClick.AddListener(() => Select(slot));

            slot.Icon = Ui.Image(slot.Element, "Icon", null, 40f, Color.white);
            slot.Corner = Ui.Text(slot.Element, "Level", "", 13f, Pal.Text, Lay.Col.Abs(6f, 3f, Lay.Auto, Lay.Auto))
                .Shadowed(2f);
            slot.Count = Ui.Text(slot.Element, "Count", "", 14f, Pal.Text, Lay.Col.Abs(Lay.Auto, Lay.Auto, 6f, 4f))
                .Shadowed(2f);
            slot.Badge = Ui.El(slot.Element, "Equipped", Lay.Col.Abs(Lay.Auto, 6f, 6f, Lay.Auto).Size(11f, 11f),
                Sty.Fill(Pal.Green).Radius(6f).Border(2f, Pal.Ink));
            slot.Wear = Ui.El(slot.Element, "Durability", Lay.Col.Abs(9f, Lay.Auto, 9f, 6f).H(3f),
                Sty.Fill(Ui.Hex(0x000000, 0.5f)).Radius(2f));
            slot.WearFill = Ui.El(slot.Wear, "Fill", Lay.Col.Abs(0f, 0f, Lay.Auto, 0f), Sty.Fill(Pal.Green).Radius(2f));
            return slot;
        }

        #endregion

        #region Character

        void BuildCharacter(YauiElement body)
        {
            var panel = Ui.El(body, "Character", Lay.Col.W(430f).Fixed().Pad(22f).Gap(16f), Pal.Panel);
            panel.transform.SetSiblingIndex(0);

            var top = Ui.El(panel, "Top", Lay.Row.Items(FlexAlign.Center).Justify(FlexJustify.SpaceBetween));
            var who = Ui.El(top, "Who", Lay.Col.Gap(2f));
            Ui.Text(who, "Name", "<b>Ruccho</b>", 28f, Pal.Text);
            Ui.Text(who, "Class", "Lv 87  ·  Flexbox Knight", 14f, Pal.Dim);
            var power = Ui.El(top, "Power", Lay.Col.Items(FlexAlign.FlexEnd));
            Ui.Text(power, "Label", "<b>POWER</b>", 11f, Pal.Gold);
            _power = Ui.Text(power, "Value", "", 30f, Pal.Gold).Shadowed(4f);

            // The gear on both sides of the emblem.
            var doll = Ui.El(panel, "Gear", Lay.Row.H(326f).Fixed().Items(FlexAlign.Center)
                .Justify(FlexJustify.SpaceBetween));
            var left = Ui.El(doll, "Left", Lay.Col.Gap(12f));
            BuildEmblem(doll);
            var right = Ui.El(doll, "Right", Lay.Col.Gap(12f));
            for (var i = 0; i < 8; i++) _gear[i] = BuildGear(i < 4 ? left : right, i);

            var stats = Ui.El(panel, "Stats", Lay.Col.Pad(16f, 14f).Gap(11f), Pal.Inset);
            var colors = new[] { Pal.Red, Ui.Hex(0x60A5FA), Pal.Green, Pal.Rarity[3], Pal.Gold };
            var icons = new[] { Icon.Sword, Icon.Shield, Icon.Heart, Icon.Star, Icon.Bolt };
            for (var i = 0; i < StatNames.Length; i++)
            {
                var row = Ui.El(stats, StatNames[i], Lay.Row.Items(FlexAlign.Center).Gap(10f));
                Ui.Image(row, "Icon", _icons[(int)icons[i]], 16f, colors[i]);
                Ui.Text(row, "Label", StatNames[i], 14f, Pal.Dim, Lay.Col.W(74f).Fixed());
                var stat = new StatRow { Bar = Bar.Create(row, "Bar", Lay.Row.Grow().H(9f), 4f, colors[i], null, true) };
                stat.Value = Ui.Text(row, "Value", "", 15f, Pal.Text, Lay.Col.W(58f).Fixed());
                stat.Value.Align = TextAlign.Right;
                _statRows.Add(stat);
            }

            var set = Ui.El(panel, "Set", Lay.Col.Pad(16f, 12f).Gap(6f), Pal.Inset);
            var title = Ui.El(set, "Title", Lay.Row.Items(FlexAlign.Center).Justify(FlexJustify.SpaceBetween));
            Ui.Text(title, "Name", "<b>Stormcaller's Regalia</b>", 15f, Pal.Rarity[3]);
            Ui.Text(title, "Count", "<b>3 / 5</b>", 13f, Pal.Dim);
            Ui.Text(set, "Two", "(2)  +12% Attack while above half health", 13f, Pal.Green);
            Ui.Text(set, "Three", "(3)  Critical hits chain to a nearby enemy", 13f, Pal.Green);
            Ui.Text(set, "Five", "(5)  Your ultimate charges 30% faster", 13f, Pal.Faint);

            // Resistances: sectors of the uber shader under a disc.
            var gauges = Ui.El(panel, "Resistances", Lay.Row.Grow().Items(FlexAlign.Center)
                .Justify(FlexJustify.SpaceBetween));
            var elements = new[] { Icon.Flame, Icon.Snowflake, Icon.Bolt, Icon.Crescent, Icon.Burst };
            var tints = new[] { Ui.Hex(0xFF8A3D), Ui.Hex(0x67E8F9), Pal.Gold, Pal.Rarity[3], Pal.Green };
            for (var i = 0; i < elements.Length; i++)
            {
                var gauge = Ui.El(gauges, "Gauge", Lay.Col.Items(FlexAlign.Center).Gap(6f));
                var ring = Ui.El(gauge, "Ring", Lay.Col.Size(62f, 62f).Fixed().Center(),
                    Sty.Fill(Ui.Hex(0xFFFFFF, 0.08f)).Radius(31f));
                var value = Random.Range(0.25f, 0.95f);
                Ui.Radial(ring, "Value", Lay.Col.Cover(), 31f, tints[i]).SetArc(-90f, value * 360f);
                var face = Ui.El(ring, "Face", Lay.Col.Abs(6f, 6f, 6f, 6f).Center(), Sty.Fill(Ui.Hex(0x171024)).Radius(25f));
                Ui.Image(face, "Icon", _icons[(int)elements[i]], 22f, tints[i]);
                Ui.Text(gauge, "Percent", $"<b>{value * 100f:0}%</b>", 13f, Pal.Dim);
            }
        }

        void BuildEmblem(YauiElement parent)
        {
            var emblem = Ui.El(parent, "Emblem", Lay.Col.Size(218f, 218f).Fixed().Center());
            var disc = Ui.Hex(0x171024);
            var insets = new[] { 0f, 24f, 46f };
            var arcs = new[] { 250f, 170f, 300f };
            var colors = new[] { Ui.Hex(0xF5C451, 0.75f), Ui.Hex(0xC084FC, 0.8f), Ui.Hex(0x67E8F9, 0.7f) };
            for (var i = 0; i < insets.Length; i++)
            {
                // A ring: a turning sector, with a disc on top of all but its rim.
                var inset = insets[i];
                var radius = 109f - inset;
                var ring = Ui.Radial(emblem, "Ring", Lay.Col.Abs(inset, inset, inset, inset), radius, colors[i]);
                ring.SetArc(0f, arcs[i]);
                _rings.Add(ring);
                var cover = inset + 5f;
                Ui.El(emblem, "Disc", Lay.Col.Abs(cover, cover, cover, cover), Sty.Fill(disc).Radius(radius));
            }

            var core = Ui.El(emblem, "Core", Lay.Col.Abs(64f, 64f, 64f, 64f).Center(),
                Sty.Fill(Ui.Hex(0x2A1B45)).Radius(45f).Border(2f, Ui.Hex(0xF5C451, 0.8f))
                    .Shadow(Ui.Hex(0xF5C451, 0.5f), 14f));
            Ui.Image(core, "Icon", _icons[(int)Icon.Sword], 52f, Pal.Gold);
        }

        Gear BuildGear(YauiElement parent, int index)
        {
            var gear = new Gear();
            gear.Element = Ui.El(parent, Kinds[index].Name, Lay.Col.Size(72f, 72f).Fixed().Center()).Hit();
            gear.Icon = Ui.Image(gear.Element, "Icon", _icons[(int)Kinds[index].Icon], 38f, Color.white);
            gear.Frame = HoloFrame(gear.Element, 14f);
            var button = gear.Element.gameObject.AddComponent<YauiButton>();
            button.Colors = Brighten();
            button.OnClick.AddListener(() => ShowGear(gear));

            // The best item of the grid for this slot starts equipped.
            Slot best = null;
            foreach (var slot in _slots)
                if (slot.Item != null && Kinds[slot.Item.Kind].Slot == index && slot.Item.Rarity is 2 or 3 &&
                    (best == null || slot.Item.Rarity > best.Item.Rarity))
                    best = slot;

            if (best != null)
            {
                best.Item.Equipped = true;
                gear.Item = best.Item;
                Refresh(best);
            }

            RefreshGear(gear);
            return gear;
        }

        // Selects the equipped item in the grid and scrolls to it.
        void ShowGear(Gear gear)
        {
            foreach (var slot in _slots)
            {
                if (slot.Item == null || slot.Item != gear.Item) continue;

                if (!slot.Element.gameObject.activeSelf) SetCategory(0);

                Select(slot);
                _scroll.ScrollIntoView(slot.Element);
                return;
            }
        }

        #endregion

        #region Detail

        void BuildDetail(YauiElement body)
        {
            var panel = Ui.El(body, "Detail", Lay.Col.W(410f).Fixed().Pad(24f), Pal.Panel);
            _detail = Ui.El(panel, "Content", Lay.Col.Grow().Gap(14f));

            var top = Ui.El(_detail, "Top", Lay.Col.Items(FlexAlign.Center).Gap(12f).Pad(0f, 10f));
            _detailTile = Ui.El(top, "Tile", Lay.Col.Size(136f, 136f).Fixed().Center());
            _detailIcon = Ui.Image(_detailTile, "Icon", null, 82f, Color.white);
            _detailFrame = HoloFrame(_detailTile, 22f);
            _burst = Fx.NewSystem(_detailTile.gameObject, _particles);
            var main = _burst.main;
            main.gravityModifier = 14f;
            Fx.Fade(_burst);
            Fx.Shrink(_burst);
            _burst.Play();
            _detailTile.gameObject.AddComponent<YauiParticle>().Position = YauiCustomDrawPosition.AfterChildren;

            _toast = Ui.Text(top, "Toast", "", 26f, Pal.Gold, Lay.Col.Abs(0f, 70f, 0f, Lay.Auto));
            _toast.Align = TextAlign.Center;
            _toast.OutlineColor = Pal.Ink;
            _toast.OutlineWidth = 2f;
            _toast.Shadowed(4f);
            _toast.Opacity = 0f;

            _detailName = Ui.Text(top, "Name", "", 24f, Pal.Text).Shadowed(3f);
            var meta = Ui.El(top, "Meta", Lay.Row.Items(FlexAlign.Center).Gap(10f));
            _detailType = Ui.Text(meta, "Type", "", 14f, Pal.Dim);
            var pill = Ui.El(meta, "Level", Lay.Col.Pad(9f, 2f), Sty.Fill(Ui.Hex(0xFFFFFF, 0.1f)).Radius(9f));
            _detailLevel = Ui.Text(pill, "Text", "", 12f, Pal.Text);

            var stats = Ui.El(_detail, "Stats", Lay.Col.Pad(16f, 12f).Gap(8f), Pal.Inset);
            foreach (var name in StatNames)
            {
                var row = Ui.El(stats, name, Lay.Row.Items(FlexAlign.Center));
                var stat = new DetailStat
                {
                    Name = Ui.Text(row, "Name", name, 15f, Pal.Dim, Lay.Col.Grow()),
                    Value = Ui.Text(row, "Value", "", 16f, Pal.Text, Lay.Col.W(76f).Fixed()),
                    Change = Ui.Text(row, "Change", "", 14f, Pal.Green, Lay.Col.W(64f).Fixed())
                };
                stat.Value.Align = TextAlign.Right;
                stat.Change.Align = TextAlign.Right;
                _detailStats.Add(stat);
            }

            var enhance = Ui.El(_detail, "Enhance", Lay.Col.Pad(16f, 12f).Gap(9f), Pal.Inset);
            _detailEnhance = Ui.Text(enhance, "Title", "", 14f, Pal.Text);
            var pips = Ui.El(enhance, "Pips", Lay.Row.Gap(4f));
            for (var i = 0; i < 15; i++)
                _pips.Add(Ui.El(pips, "Pip", Lay.Col.Grow().H(9f), Sty.Fill(Ui.Hex(0xFFFFFF, 0.1f)).Radius(3f)));

            var sockets = Ui.El(_detail, "Sockets", Lay.Row.Items(FlexAlign.Center).Gap(10f).Pad(16f, 12f), Pal.Inset);
            for (var i = 0; i < 4; i++)
            {
                var socket = Ui.El(sockets, "Socket", Lay.Col.Size(42f, 42f).Fixed().Center());
                _sockets.Add(socket);
                _socketIcons.Add(Ui.Image(socket, "Gem", _icons[(int)Icon.Star + i * 3 % 4], 22f, Color.clear));
            }

            _detailNotes = Ui.Text(sockets, "Notes", "", 13f, Pal.Dim, Lay.Col.Grow().W(0f).Margin(8f, 0f, 0f, 0f));
            _detailNotes.Align = TextAlign.Right;

            _detailFlavor = Ui.Text(_detail, "Flavor", "", 14f, Pal.Dim, Lay.Col.Fixed().Margin(4f, 2f, 4f, 0f));
            _detailFlavor.WordWrap = true;

            Ui.El(_detail, "Spacer", Lay.Col.Grow());

            var price = Ui.El(_detail, "Price", Lay.Row.Items(FlexAlign.Center).Gap(8f));
            Ui.Text(price, "Label", "Sell value", 14f, Pal.Dim, Lay.Col.Grow());
            Ui.Image(price, "Coin", _icons[(int)Icon.Circle], 16f, Pal.Gold);
            _detailPrice = Ui.Text(price, "Value", "", 16f, Pal.Gold);

            var buttons = Ui.El(_detail, "Buttons", Lay.Row.Gap(10f));
            _equipButton = Action(buttons, "Equip", Pal.Gold, Pal.Ink, Lay.Col.Grow().W(0f), Equip, out _equipLabel);
            _enhanceButton = Action(buttons, "Enhance", Pal.Rarity[3], Pal.Ink, Lay.Col.Grow().W(0f), Enhance, out _);
            Action(buttons, "Sell", Ui.Hex(0x2A2038), Pal.Text, Lay.Col.W(84f).Fixed(), Sell, out _);
        }

        static YauiElement Action(YauiElement parent, string label, Color color, Color text, LayoutStyle layout,
            UnityEngine.Events.UnityAction action, out YauiText labelText)
        {
            var element = Ui.El(parent, label, layout.H(50f).Center(),
                Sty.Fill(color).Radius(13f).Border(1f, Ui.Hex(0xFFFFFF, 0.25f))
                    .Shadow(new Color(color.r, color.g, color.b, 0.35f), 10f, 0f, new Vector2(0f, 4f))).Hit();
            labelText = Ui.Text(element, "Label", "<b>" + label + "</b>", 17f, text);
            var button = element.gameObject.AddComponent<YauiButton>();
            button.Colors = Ui.Tints(0.9f, 0.75f);
            button.OnClick.AddListener(action);
            return element;
        }

        #endregion
    }
}
