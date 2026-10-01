using UnityEngine;

namespace Yaui.Showcase
{
    // The shop: a top bar, the gacha banner and the products side by side, the battle pass, and the pull overlay.
    public sealed partial class GachaShowcase
    {
        void BuildShop()
        {
            var go = new GameObject("Shop");
            _panel = go.AddComponent<YauiPanel>();
            _panel.ReferenceResolution = new Vector2(1920f, 1080f);
            _panel.Match = 0.5f;
            go.AddComponent<YauiRaycaster>();
            _panels.Add(_panel);
            _root = go.GetComponent<YauiElement>();
            _root.RaycastTarget = false;

            // The backdrop: turning rays over a gradient, in one custom shader.
            var backdrop = Ui.El(_root, "Backdrop", Lay.Col.Cover(), Sty.Fill(Color.white));
            var sky = new Material(Find(sunburstShader, "Yaui/Showcase/Sunburst"));
            sky.SetColor("_ColorA", Ui.Hex(0x5EC8FF));
            sky.SetColor("_ColorB", Ui.Hex(0xFFA6DC));
            sky.SetColor("_RayColor", new Color(1f, 1f, 1f, 0.16f));
            sky.SetVector("_Center", new Vector4(0.5f, 1.15f, 0f, 0f));
            sky.SetFloat("_Rays", 18f);
            sky.SetFloat("_Speed", 0.06f);
            backdrop.Material = sky;

            BuildTopBar();
            var middle = Ui.El(_root, "Middle", Lay.Row.Grow().Gap(26f).Pad(28f, 0f));
            BuildGacha(middle);
            BuildProducts(middle);
            BuildPass();
            BuildOverlay();
            BuildEffects();
            UpdatePity();
        }

        // A box with a vertical gradient: the second color travels in the border color (see Gradient.shader).
        YauiElement Grad(Component parent, string name, LayoutStyle layout, Color top, Color bottom, float radius)
        {
            var element = Ui.El(parent, name, layout, new BoxStyle
            {
                backgroundColor = top,
                borderColor = bottom,
                cornerRadius = new Vector4(radius, radius, radius, radius)
            });
            element.Material = _gradient;
            return element;
        }

        // A band of light crossing the element now and then.
        void Sheen(YauiElement parent, float radius)
        {
            Ui.El(parent, "Sheen", Lay.Col.Cover(), Sty.Fill(Color.white).Radius(radius)).Material = _sheen;
        }

        // An outline as wide as the distance field of the font atlas allows (about a tenth of the font size; less
        // here, to leave the field room).
        static YauiText Loud(YauiText text, Color outline)
        {
            text.OutlineColor = outline;
            text.OutlineWidth = text.FontSize * 0.055f;
            return text;
        }

        #region Top bar

        void BuildTopBar()
        {
            var bar = Ui.El(_root, "Top", Lay.Row.H(92f).Fixed().Items(FlexAlign.Center).Gap(18f).Pad(28f, 0f));
            var back = Ui.El(bar, "Back", Lay.Col.Size(54f, 54f).Fixed().Center(), Pal.Card.Radius(27f)).Hit();
            Ui.Image(back, "Icon", _icons[(int)Icon.Arrow], 24f, Pal.Ink).Rotation = -90f;
            back.gameObject.AddComponent<YauiButton>().Colors = Ui.Tints();
            Loud(Ui.Text(bar, "Title", "<b>SHOP</b>", 40f, Color.white), Pal.Ink);

            var stats = Ui.El(bar, "Stats", Lay.Row.Items(FlexAlign.Center).Gap(12f).Pad(16f, 8f).Margin(18f, 0f, 0f, 0f),
                Sty.Fill(Ui.Hex(0x1E1B4B, 0.82f)).Radius(18f));
            Ui.Text(stats, "Logo", "<b>YAUI</b>", 18f, Pal.Yellow);
            _stats = Ui.Text(stats, "Text", "", 15f, Color.white);

            Ui.El(bar, "Spacer", Lay.Col.Grow());
            _gems = Currency(bar, Icon.Diamond, Pal.Sky, $"<b>{_gemsShown:N0}</b>");
            Currency(bar, Icon.Circle, Pal.Yellow, "<b>482,900</b>");
            Currency(bar, Icon.Star, Pal.Pink, "<b>12</b>");
        }

        YauiText Currency(YauiElement bar, Icon icon, Color color, string value)
        {
            var pill = Ui.El(bar, "Currency", Lay.Row.H(50f).Fixed().Items(FlexAlign.Center).Gap(10f).Pad(8f, 0f),
                Pal.Card.Radius(25f));
            var badge = Ui.El(pill, "Badge", Lay.Col.Size(36f, 36f).Fixed().Center(),
                Sty.Fill(Color.Lerp(color, Color.white, 0.75f)).Radius(18f));
            Ui.Image(badge, "Icon", _icons[(int)icon], 22f, color);
            var text = Ui.Text(pill, "Value", value, 20f, Pal.Ink, Lay.Col.Fixed().Margin(0f, 0f, 6f, 0f));
            var plus = Ui.El(pill, "Add", Lay.Col.Size(34f, 34f).Fixed().Center(), Sty.Fill(Pal.Mint).Radius(17f)).Hit();
            Ui.Image(plus, "Icon", _icons[(int)Icon.Cross], 16f, Color.white);
            plus.gameObject.AddComponent<YauiButton>().Colors = Ui.Tints(0.9f, 0.75f);
            return text;
        }

        #endregion

        #region Gacha

        void BuildGacha(YauiElement middle)
        {
            var column = Ui.El(middle, "Gacha", Lay.Col.W(800f).Fixed().Gap(16f));
            var stage = Ui.El(column, "Stage", Lay.Col.Grow());

            // A white slanted frame, and inside it the banner: a mask in the same slanted shape.
            var frame = Ui.Add<SkewElement>(stage, "Frame", Lay.Col.Cover(), Sty.None);
            frame.Skew = 0.07f;
            frame.Radius = 34f;
            _banner = Ui.Add<SkewElement>(stage, "Banner", Lay.Col.Abs(7f, 7f, 7f, 7f), Sty.None);
            _banner.Skew = 0.07f;
            _banner.Radius = 28f;
            _banner.Color = Pal.Purple;
            _banner.gameObject.AddComponent<YauiMask>();

            // The pages are side by side; the strip slides them through the mask.
            _strip = Ui.El(_banner, "Pages", Lay.Row.Cover());
            BuildPage("PICK UP", "Starlight Festival", "Limited SSR  ·  drop rate doubled", Ui.Hex(0xFF7EC7),
                Ui.Hex(0x6D3BFF), Icon.Star, Pal.Yellow);
            BuildPage("NEW", "月夜のセレナーデ", "Moonlit Serenade  ·  2 new SSR", Ui.Hex(0x4CC9FF), Ui.Hex(0x2B2F9E),
                Icon.Crescent, Ui.Hex(0xFFF6C2));
            BuildPage("RERUN", "Sunfire Carnival", "Fan favorites return for one week", Ui.Hex(0xFFD23F),
                Ui.Hex(0xFF4F7B), Icon.Burst, Color.white);

            var dots = Ui.El(stage, "Dots", Lay.Row.Abs(0f, Lay.Auto, 0f, 22f).Justify(FlexJustify.Center).Gap(14f));
            for (var i = 0; i < 3; i++)
            {
                var index = i;
                var dot = Ui.El(dots, "Dot", Lay.Col.Size(12f, 12f).Fixed(), Sty.Fill(Color.white).Radius(6f)).Hit();
                dot.gameObject.AddComponent<YauiButton>().OnClick.AddListener(() => ShowPage(index));
                _dots.Add(dot);
            }

            var info = Ui.El(column, "Info", Lay.Row.Items(FlexAlign.Center).Gap(16f));
            var timer = Ui.El(info, "Timer", Lay.Row.H(50f).Fixed().Items(FlexAlign.Center).Gap(10f).Pad(16f, 0f),
                Pal.Card.Radius(25f));
            Ui.Text(timer, "Label", "Ends in", 15f, Pal.Dim);
            _countdown = Ui.Text(timer, "Time", "", 20f, Pal.Pink);
            var pity = Ui.El(info, "Pity", Lay.Col.Grow().W(0f).Gap(6f).Pad(18f, 9f), Pal.Card.Radius(25f));
            _pityText = Ui.Text(pity, "Label", "", 15f, Pal.Ink);
            var track = Ui.El(pity, "Track", Lay.Col.H(10f), Sty.Fill(Ui.Hex(0xE8E6F7)).Radius(5f));
            _pityFill = Grad(track, "Fill", Lay.Col.Abs(0f, 0f, Lay.Auto, 0f), Pal.Yellow, Pal.Pink, 5f);

            var buttons = Ui.El(column, "Buttons", Lay.Row.H(104f).Fixed().Gap(18f));
            PullButton(buttons, 1, Color.white, Color.white, Pal.Ink);
            PullButton(buttons, 10, Ui.Hex(0xFFE25A), Ui.Hex(0xFF8A1F), Color.white);
        }

        void BuildPage(string tag, string title, string subtitle, Color top, Color bottom, Icon art, Color artColor)
        {
            var page = Ui.El(_strip, title, Lay.Col.W(Length.Percent(100f)).Fixed());
            Grad(page, "Sky", Lay.Col.Cover(), top, bottom, 0f);

            // The art: a large icon over a glow of turning rays, with sparkles floating around.
            var glow = Ui.El(page, "Glow", Lay.Col.Abs(Lay.Auto, -110f, -70f, Lay.Auto).Size(640f, 640f),
                Sty.Fill(Color.white));
            glow.Material = _glow;
            var image = Ui.Image(page, "Art", IconFactory.CreateLarge(art), artColor,
                Lay.Col.Abs(Lay.Auto, 44f, 84f, Lay.Auto).Size(330f, 330f));
            Float(image, 14f);
            for (var i = 0; i < 9; i++)
            {
                var size = Random.Range(18f, 54f);
                var sparkle = Ui.Image(page, "Sparkle", _icons[(int)(i % 3 == 0 ? Icon.Diamond : Icon.Star)],
                    new Color(1f, 1f, 1f, Random.Range(0.5f, 0.95f)),
                    Lay.Col.Abs(Random.Range(60f, 700f), Random.Range(20f, 300f), Lay.Auto, Lay.Auto).Size(size, size));
                Float(sparkle, Random.Range(6f, 18f), Random.Range(-40f, 40f));
            }

            var text = Ui.El(page, "Text", Lay.Col.Abs(58f, Lay.Auto, Lay.Auto, 58f).Gap(8f).Items(FlexAlign.FlexStart));
            var ribbon = Ui.El(text, "Tag", Lay.Col.Pad(16f, 5f), Sty.Fill(Pal.Yellow).Radius(15f));
            ribbon.Rotation = -3f;
            Ui.Text(ribbon, "Text", "<b>" + tag + "</b>", 17f, Pal.Ink);
            Loud(Ui.Text(text, "Title", "<b>" + title + "</b>", 60f, Color.white), Ui.Hex(0x2A1670));
            Ui.Text(text, "Subtitle", "<b>" + subtitle + "</b>", 20f, Color.white).Shadowed(4f);

            // The featured items, with holographic frames.
            var featured = Ui.El(text, "Featured", Lay.Row.Gap(12f).Margin(0f, 10f, 0f, 0f));
            for (var i = 0; i < 3; i++)
            {
                var tile = Ui.El(featured, "Item", Lay.Col.Size(84f, 84f).Fixed().Center(),
                    Sty.Fill(Ui.Hex(0xFFFFFF, 0.28f)).Radius(20f));
                Ui.Image(tile, "Icon", _icons[(_strip.transform.childCount * 5 + i * 3) % 16], 46f, Color.white);
                Ui.El(tile, "Holo", Lay.Col.Cover(), Sty.Fill(Color.white).Radius(20f)).Material = _holo;
            }
        }

        void PullButton(YauiElement parent, int count, Color top, Color bottom, Color text)
        {
            // The shadow is on the button; the gradient is a child (the gradient shader draws no shadow).
            var button = Ui.El(parent, "Pull x" + count, Lay.Col.Grow(count == 10 ? 1.5f : 1f).W(0f).Center().Gap(4f),
                Sty.Fill(bottom).Radius(28f).Shadow(Ui.Hex(0x1E1B4B, 0.3f), 12f, 0f, new Vector2(0f, 8f))).Hit();
            Grad(button, "Face", Lay.Col.Cover(), top, bottom, 28f);
            var label = Ui.Text(button, "Label", $"<b>Pull x{count}</b>", 30f, text);
            if (count == 10) Loud(label, Ui.Hex(0xB34700));

            var cost = Ui.El(button, "Cost", Lay.Row.Items(FlexAlign.Center).Gap(8f).Pad(14f, 3f),
                Sty.Fill(Ui.Hex(0x1E1B4B, count == 10 ? 0.35f : 0.08f)).Radius(14f));
            Ui.Image(cost, "Gem", _icons[(int)Icon.Diamond], 18f, count == 10 ? Color.white : Pal.Sky);
            Ui.Text(cost, "Price", $"<b>{count * PullCost:N0}</b>", 18f, text);
            if (count == 10)
            {
                Sheen(button, 28f);
                var badge = Ui.El(button, "Badge", Lay.Col.Abs(Lay.Auto, -14f, 18f, Lay.Auto).Pad(14f, 5f),
                    Sty.Fill(Pal.Pink).Radius(15f).Border(3f, Color.white));
                badge.Rotation = 4f;
                Ui.Text(badge, "Text", "<b>SR+ GUARANTEED</b>", 14f, Color.white);
            }

            var control = button.gameObject.AddComponent<YauiButton>();
            control.Colors = Ui.Tints(0.94f, 0.82f);
            control.OnClick.AddListener(() =>
            {
                Pop(button);
                Pull(count, true);
            });
        }

        #endregion

        #region Products

        void BuildProducts(YauiElement middle)
        {
            var column = Ui.El(middle, "Products", Lay.Col.Grow().W(0f).Gap(14f));
            var tabs = Ui.El(column, "Tabs", Lay.Row.Gap(10f));
            foreach (var name in new[] { "Featured", "Gems", "Bundles", "Daily deals", "Cosmetics" })
            {
                var active = name == "Featured";
                var tab = Ui.El(tabs, name, Lay.Col.Pad(22f, 9f),
                    Sty.Fill(active ? Pal.Ink : Color.white).Radius(21f)
                        .Shadow(Ui.Hex(0x1E1B4B, 0.15f), 8f, 0f, new Vector2(0f, 4f))).Hit();
                Ui.Text(tab, "Label", "<b>" + name + "</b>", 17f, active ? Color.white : Pal.Dim);
                tab.gameObject.AddComponent<YauiButton>().Colors = Ui.Tints(0.92f, 0.8f);
            }

            var grid = Ui.El(column, "Grid", Lay.Row.Grow().Wrap().Gap(16f));
            AddProduct(grid, "Handful of Gems", 60, 0, "$0.99", Pal.Sky, null, 1);
            AddProduct(grid, "Pouch of Gems", 330, 30, "$4.99", Pal.Sky, null, 2);
            AddProduct(grid, "Chest of Gems", 1100, 180, "$14.99", Pal.Purple, "POPULAR", 3);
            AddProduct(grid, "Vault of Gems", 3900, 900, "$49.99", Pal.Pink, "BEST VALUE", 5);
            AddProduct(grid, "Starter Pack", 500, 500, "$1.99", Pal.Orange, "-80%", 3);
            AddProduct(grid, "Monthly Pass", 3000, 0, "$4.99", Pal.Mint, "30 DAYS", 2);
            AddProduct(grid, "Festival Bundle", 1600, 400, "$19.99", Pal.Yellow, "LIMITED", 4);
            AddProduct(grid, "Whale's Hoard", 8800, 2400, "$99.99", Pal.Blue, null, 6);
        }

        void AddProduct(YauiElement grid, string name, int gems, int bonus, string price, Color color, string tag,
            int pile)
        {
            // Four cards in a row: a quarter of the grid each, less the gaps.
            var card = Ui.El(grid, name, Lay.Col.W(Length.Percent(23.6f)).H(Length.Percent(48.6f)).Fixed().Pad(12f).Gap(8f)
                .Items(FlexAlign.Stretch), Pal.Card).Hit();
            var product = new Product { Card = card, Gems = gems + bonus };
            _products.Add(product);

            // The art: a pile of gems on a tinted field.
            var art = Ui.El(card, "Art", Lay.Row.Grow().Center(), Sty.Fill(Color.Lerp(color, Color.white, 0.8f)).Radius(16f));
            for (var i = 0; i < pile; i++)
            {
                var size = 76f - i * 7f;
                var gem = Ui.Image(art, "Gem", _icons[(int)(i % 2 == 0 ? Icon.Diamond : Icon.Star)],
                    Color.Lerp(color, i % 2 == 0 ? Color.white : Pal.Yellow, i * 0.12f),
                    Lay.Col.Size(size, size).Fixed().Margin(i == 0 ? 0f : -30f, 0f, 0f, i % 2 * 18f));
                gem.Rotation = (i - pile * 0.5f) * 14f;
            }

            if (tag != null)
            {
                var ribbon = Ui.El(card, "Tag", Lay.Col.Abs(-8f, -10f, Lay.Auto, Lay.Auto).Pad(13f, 5f),
                    Sty.Fill(Pal.Pink).Radius(14f).Border(3f, Color.white));
                ribbon.Rotation = -6f;
                Ui.Text(ribbon, "Text", "<b>" + tag + "</b>", 14f, Color.white);
            }

            var title = Ui.El(card, "Title", Lay.Col.Items(FlexAlign.Center).Gap(1f));
            Ui.Text(title, "Name", "<b>" + name + "</b>", 18f, Pal.Ink);
            Ui.Text(title, "Amount", bonus > 0 ? $"{gems:N0} <color=#FF5FA2><b>+{bonus:N0} bonus</b></color>" : $"{gems:N0} gems",
                14f, Pal.Dim);

            // A plain box: a gradient or a sheen on every card would split the draw call eight times.
            var buy = Ui.El(card, "Buy", Lay.Col.H(46f).Fixed().Center(),
                Sty.Fill(Pal.Yellow).Radius(15f).Border(2f, Ui.Hex(0xFFA51F)));
            Ui.Text(buy, "Price", "<b>" + price + "</b>", 20f, Pal.Ink);

            var button = card.gameObject.AddComponent<YauiButton>();
            button.Colors = Ui.Tints(0.96f, 0.88f);
            button.OnClick.AddListener(() => Buy(product));
        }

        #endregion

        #region Pass

        void BuildPass()
        {
            var pass = Ui.El(_root, "Pass", Lay.Col.H(262f).Fixed().Margin(28f, 20f, 28f, 22f).Pad(18f, 14f).Gap(10f),
                Pal.Card.Radius(28f));

            var header = Ui.El(pass, "Header", Lay.Row.Items(FlexAlign.Center).Gap(16f));
            Ui.Text(header, "Title", "<b>BATTLE PASS</b>", 22f, Pal.Ink);
            Ui.Text(header, "Season", "Season 7  ·  Skyward Parade", 15f, Pal.Dim);
            var level = Ui.El(header, "Level", Lay.Col.Size(40f, 40f).Fixed().Center().Margin(12f, 0f, 0f, 0f),
                Sty.Fill(Pal.Ink).Radius(20f));
            _passLevel = Ui.Text(level, "Text", "", 18f, Color.white);
            var track = Ui.El(header, "Experience", Lay.Col.Grow().H(16f), Sty.Fill(Ui.Hex(0xE8E6F7)).Radius(8f));
            _passFill = Grad(track, "Fill", Lay.Col.Abs(0f, 0f, Lay.Auto, 0f), Pal.Sky, Pal.Purple, 8f);
            var premium = Ui.El(header, "Premium", Lay.Row.Items(FlexAlign.Center).Gap(8f).Pad(16f, 7f),
                Sty.Fill(Pal.Yellow).Radius(18f));
            Ui.Image(premium, "Icon", _icons[(int)Icon.Star], 16f, Pal.Ink);
            Ui.Text(premium, "Text", "<b>PREMIUM ACTIVE</b>", 14f, Pal.Ink);

            // The reward track: a horizontal scroll view.
            var scroll = Ui.El(pass, "Scroll", Lay.Col.Grow(), Sty.None.Radius(16f)).Hit();
            scroll.ClipChildren = true;
            var content = Ui.El(scroll, "Content", Lay.Row.Abs(0f, 0f, Lay.Auto, 0f));
            var rewards = new[] { Icon.Diamond, Icon.Circle, Icon.Star, Icon.Flame, Icon.Heart, Icon.Bolt, Icon.Burst };
            for (var i = 0; i < TierCount; i++)
            {
                var tier = new PassTier();
                var column = Ui.El(content, "Tier", Lay.Col.W(TierWidth).Fixed().Items(FlexAlign.Center)
                    .Justify(FlexJustify.SpaceBetween));
                tier.Free = Reward(column, rewards[i % rewards.Length], Pal.Party[i % 6], false,
                    $"x{(i % 5 + 1) * 50}", out tier.FreeCheck);

                var node = Ui.El(column, "Step", Lay.Row.H(30f).Fixed().Self(FlexAlign.Stretch).Center());
                tier.Line = Ui.El(node, "Line", Lay.Col.Abs(0f, 12f, 0f, Lay.Auto).H(6f), Sty.Fill(Ui.Hex(0xDADCF0)));
                tier.Node = Ui.El(node, "Node", Lay.Col.Size(30f, 30f).Fixed().Center());
                tier.Number = Ui.Text(tier.Node, "Number", $"<b>{i + 1}</b>", 13f, Pal.Dim);

                tier.Premium = Reward(column, rewards[(i + 3) % rewards.Length], Pal.Yellow, true,
                    i % 10 == 9 ? "SSR" : $"x{(i % 4 + 1) * 120}", out tier.PremiumCheck);

                // The claim button of the current tier, over its rewards.
                tier.Claim = Ui.El(column, "Claim", Lay.Col.Abs(14f, 60f, 14f, Lay.Auto).Pad(0f, 4f).Center(),
                    Sty.Fill(Pal.Pink).Radius(14f).Border(3f, Color.white)
                        .Shadow(Ui.Hex(0xFF5FA2, 0.6f), 8f)).Hit();
                Ui.Text(tier.Claim, "Text", "<b>CLAIM</b>", 15f, Color.white);
                tier.Claim.gameObject.AddComponent<YauiButton>().OnClick.AddListener(() => _experience = 1f);
                _tiers.Add(tier);
            }

            _passScroll = scroll.gameObject.AddComponent<YauiScrollView>();
            _passScroll.Viewport = scroll;
            _passScroll.Content = content;
            _passScroll.Vertical = false;
            _passScroll.ScrollSensitivity = 60f;
        }

        YauiElement Reward(YauiElement column, Icon icon, Color color, bool premium, string amount,
            out YauiElement check)
        {
            var tile = Ui.El(column, premium ? "Premium" : "Free", Lay.Col.Size(98f, 76f).Fixed().Center(),
                Sty.Fill(premium ? Ui.Hex(0xFFF3C4) : Ui.Hex(0xF3F2FB)).Radius(16f)
                    .Border(2f, premium ? Ui.Hex(0xFFC93C) : Ui.Hex(0xE2E0F2)));
            Ui.Image(tile, "Icon", _icons[(int)icon], 36f, premium ? Pal.Orange : color);
            Ui.Text(tile, "Amount", "<b>" + amount + "</b>", 13f, Pal.Ink, Lay.Col.Abs(Lay.Auto, Lay.Auto, 8f, 4f));
            check = Ui.El(tile, "Claimed", Lay.Col.Abs(Lay.Auto, -6f, -6f, Lay.Auto).Size(26f, 26f).Center(),
                Sty.Fill(Pal.Mint).Radius(13f).Border(3f, Color.white));
            Ui.Image(check, "Icon", _icons[(int)Icon.Check], 12f, Color.white);
            return tile;
        }

        #endregion

        #region Overlay and effects

        void BuildOverlay()
        {
            // The result of a pull, over the whole shop. A press closes it.
            _overlay = Ui.El(_root, "Pull Result", Lay.Col.Cover().Center().Gap(34f), Sty.Fill(Ui.Hex(0x140A3C, 0.86f))).Hit();
            _overlay.gameObject.AddComponent<YauiButton>().OnClick.AddListener(ClosePull);
            _overlay.Scale = Vector2.zero;

            Loud(Ui.Text(_overlay, "Title", "<b>RESULTS</b>", 54f, Color.white), Pal.Pink);

            // The parts of the cards are grouped by material, not by card: all glows, all faces, all contents, all
            // frames. Each group is consecutive in the draw order, so the ten cards cost four draw calls instead of
            // forty. The four grids have the same layout, and a card moves its four parts together.
            var cards = Ui.El(_overlay, "Cards", Lay.Col.W(1130f).Fixed());
            var grid = Lay.Row.Wrap().Justify(FlexJustify.Center).Gap(26f);
            var glows = Ui.El(cards, "Glows", grid);
            var faces = Ui.El(cards, "Faces", grid.Cover());
            var contents = Ui.El(cards, "Contents", grid.Cover());
            var frames = Ui.El(cards, "Frames", grid.Cover());
            var cell = Lay.Col.Size(200f, 262f).Fixed();
            for (var i = 0; i < 10; i++)
            {
                var card = new PullCard();
                // An SSR has turning rays behind the card, and a holographic frame on it.
                var glow = Ui.El(glows, "Glow", cell);
                card.Glow = Ui.El(glow, "Rays", Lay.Col.Abs(-110f, -80f, -110f, -80f), Sty.Fill(Color.white));
                card.Glow.Material = _glow;
                card.Face = Grad(faces, "Face", cell, Color.white, Color.white, 20f);

                var content = Ui.El(contents, "Content", cell.Pad(14f).Items(FlexAlign.Center)
                    .Justify(FlexJustify.SpaceBetween));
                var rarity = Ui.El(content, "Rarity", Lay.Col.Pad(14f, 3f).Self(FlexAlign.FlexStart),
                    Sty.Fill(Ui.Hex(0xFFFFFF, 0.9f)).Radius(13f));
                card.Rarity = Ui.Text(rarity, "Text", "", 18f, Pal.Ink);
                card.Icon = Ui.Image(content, "Icon", null, 104f, Color.white);
                card.Name = Ui.Text(content, "Name", "", 17f, Color.white).Shadowed(3f);
                card.New = Ui.El(content, "New", Lay.Col.Abs(Lay.Auto, -12f, -12f, Lay.Auto).Pad(12f, 4f),
                    Sty.Fill(Pal.Pink).Radius(13f).Border(3f, Color.white));
                card.New.Rotation = 8f;
                Ui.Text(card.New, "Text", "<b>NEW</b>", 15f, Color.white);

                var frame = Ui.El(frames, "Frame", cell);
                card.Frame = Ui.El(frame, "Holo", Lay.Col.Cover(), Sty.Fill(Color.white).Radius(20f));
                card.Frame.Material = _holo;

                card.Parts = new[] { glow, card.Face, content, frame };
                _cards.Add(card);
            }

            _overlayHint = Ui.Text(_overlay, "Hint", "", 20f, Ui.Hex(0xFFFFFF, 0.75f));
        }

        void BuildEffects()
        {
            // On top of everything: confetti (plain quads that tumble) and sparkles (additive dots).
            var layer = Ui.El(_root, "Effects", Lay.Col.Cover());
            var paper = new Material(Find(particleShader, "Yaui/Particle")) { mainTexture = Texture2D.whiteTexture };
            _confetti = Fx.NewSystem(layer.gameObject, paper);
            var main = _confetti.main;
            main.maxParticles = 3000;
            main.startSize3D = true;
            main.gravityModifier = 75f;
            var limit = _confetti.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 100000f;
            limit.drag = 1.6f;
            Fx.Fade(_confetti);
            _confetti.Play();

            var sparkleObject = new GameObject("Sparkles");
            sparkleObject.transform.SetParent(layer.transform, false);
            _sparkles = Fx.NewSystem(sparkleObject, Fx.Material(particleShader, true));
            Fx.Fade(_sparkles);
            Fx.Shrink(_sparkles);
            _sparkles.Play();
            layer.gameObject.AddComponent<YauiParticle>();

            _toast = Ui.Text(_root, "Toast", "", 40f, Pal.Sky, Lay.Col.Abs(0f, 0f, Lay.Auto, Lay.Auto).Size(400f, 80f));
            _toast.Align = TextAlign.Center;
            _toast.VerticalAlign = VerticalAlign.Middle;
            Loud(_toast, Color.white);
            _toast.Opacity = 0f;
        }

        #endregion
    }
}
