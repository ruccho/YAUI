using UnityEngine;

namespace Yaui.Showcase
{
    // The HUD: one overlay panel, laid out with flexbox and anchored to the edges of the screen.
    public sealed partial class BattleHudShowcase
    {
        static readonly Vector2 NumberSize = new(340f, 76f);

        static readonly string[] MemberNames =
        {
            "Ruccho", "Mira", "Tobi", "Kaede", "Jun", "Ilse", "Noor", "Dax", "Yuki", "Bren",
            "Sora", "Vex", "Lune", "Orin", "Pax", "Ryo", "Tess", "Ume", "Wren", "Zed"
        };

        void BuildHud()
        {
            var go = new GameObject("HUD");
            _panel = go.AddComponent<YauiPanel>();
            _panels.Add(_panel);
            _panel.ReferenceResolution = new Vector2(1920f, 1080f);
            _panel.Match = 0.5f;
            _root = go.GetComponent<YauiElement>();
            _root.RaycastTarget = false;

            // Soft dark bands at the top and the bottom: the shadows of boxes outside the screen.
            var shade = Sty.Fill(Color.black).Shadow(new Color(0f, 0f, 0f, 0.6f), 60f);
            Ui.El(_root, "Shade Top", Lay.Col.Abs(0f, -50f, 0f, Lay.Auto).H(50f), shade);
            Ui.El(_root, "Shade Bottom", Lay.Col.Abs(0f, Lay.Auto, 0f, -50f).H(50f), shade);

            BuildEffects();
            BuildNumbers();
            BuildPlayerFrame();
            BuildRaidFrames();
            BuildBossFrame();
            BuildMinimapAndQuests();
            BuildChat();
            BuildMeter();
            BuildActionBars();
            BuildCombo();

            _xp = Bar.Create(_root, "Experience", Lay.Row.Abs(0f, Lay.Auto, 0f, 0f).H(9f), 0f, Pal.Purple, _liquid);
            for (var i = 1; i < 20; i++)
                Ui.El(_xp.Track, "Tick", Lay.Col.Abs(Length.Percent(i * 5f), 0f, Lay.Auto, 0f).W(2f),
                    Sty.Fill(new Color(0f, 0f, 0f, 0.5f)));

            var stats = Ui.El(_root, "Stats",
                Lay.Row.Abs(Lay.Auto, Lay.Auto, 24f, 24f).W(344f).Pad(14f, 9f).Gap(14f).Items(FlexAlign.Center),
                Pal.Panel.Border(1f, Ui.Hex(0x4DD8FF, 0.55f)));
            Ui.Text(stats, "Logo", "<b>YAUI</b>", 26f, Pal.Cyan);
            var lines = Ui.El(stats, "Lines", Lay.Col.Gap(2f));
            _stats = Ui.Text(lines, "Counts", "", 15f, Pal.Text);
            _statsDetail = Ui.Text(lines, "Draws", "", 15f, Pal.Text);
        }

        #region Effects

        void BuildEffects()
        {
            var material = new Material(Find(particleShader, "Yaui/Particle")) { mainTexture = IconFactory.CreateGlow() };
            material.SetFloat("_Additive", 1f);
            _particleMaterial = material;

            // One YauiParticle draws the systems on its GameObject and below it, under the rest of the HUD.
            var layer = Ui.El(_root, "Effects", Lay.Col.Cover());

            _sparks = NewSystem(layer.gameObject, material);
            var main = _sparks.main;
            main.maxParticles = 4000;
            main.gravityModifier = 70f;
            var size = _sparks.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            var renderer = _sparks.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.035f;
            renderer.lengthScale = 1.4f;
            _sparks.Play();

            var embersObject = new GameObject("Embers");
            embersObject.transform.SetParent(layer.transform, false);
            var embers = NewSystem(embersObject, material);
            main = embers.main;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(6f, 18f);
            main.startColor = new Color(1f, 0.6f, 0.3f, 0.9f);
            var emission = embers.emission;
            emission.enabled = true;
            emission.rateOverTime = 26f;
            var shape = embers.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = new Vector3(0f, -560f, 0f);
            shape.scale = new Vector3(2000f, 40f, 1f);
            var velocity = embers.velocityOverLifetime;
            velocity.enabled = true;
            velocity.x = new ParticleSystem.MinMaxCurve(-30f, 30f);
            velocity.y = new ParticleSystem.MinMaxCurve(50f, 150f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            Fade(embers);
            embers.Play();

            layer.gameObject.AddComponent<YauiParticle>();
        }

        Material _particleMaterial;

        static ParticleSystem NewSystem(GameObject go, Material material)
        {
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            // YauiParticle draws systems that simulate in local space, in canvas units.
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startSpeed = 0f;
            main.startLifetime = 0.5f;
            main.startSize = 10f;
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            return system;
        }

        static void Fade(ParticleSystem system)
        {
            var color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
        }

        void BuildNumbers()
        {
            var layer = Ui.El(_root, "Damage Numbers", Lay.Col.Cover());
            for (var i = 0; i < 220; i++)
            {
                // A fixed size makes each number a layout boundary: a new text never lays out anything else.
                var text = Ui.Text(layer, "Number", "", 28f, Color.white,
                    Lay.Col.Abs(0f, 0f, Lay.Auto, Lay.Auto).Size(NumberSize.x, NumberSize.y));
                text.Align = TextAlign.Center;
                text.VerticalAlign = VerticalAlign.Middle;
                text.OutlineColor = Ui.Hex(0x3A1500);
                text.Shadowed(2f);
                text.Scale = Vector2.zero;
                _numbers.Add(new DamageNumber { Text = text });
            }
        }

        #endregion

        #region Left

        void BuildPlayerFrame()
        {
            var frame = Ui.El(_root, "Player Frame",
                Lay.Row.Abs(24f, 24f, Lay.Auto, Lay.Auto).Gap(14f).Pad(12f).Items(FlexAlign.Center), Pal.Panel);

            var avatar = Ui.El(frame, "Avatar", Lay.Col.Size(88f, 88f).Fixed().Center(),
                Sty.Fill(Ui.Hex(0x16264A)).Radius(44f).Border(3f, Pal.Cyan).Shadow(Ui.Hex(0x4DD8FF, 0.55f), 9f));
            Ui.Image(avatar, "Icon", _icons[(int)Icon.Sword], 46f, Pal.Cyan);
            var level = Ui.El(avatar, "Level", Lay.Col.Abs(Lay.Auto, Lay.Auto, -8f, -6f).Size(36f, 24f).Center(),
                Sty.Fill(Pal.Gold).Radius(12f).Border(2f, Pal.Ink));
            Ui.Text(level, "Text", "<b>87</b>", 14f, Pal.Ink);

            var info = Ui.El(frame, "Info", Lay.Col.Gap(6f).W(330f));
            var header = Ui.El(info, "Header", Lay.Row.Items(FlexAlign.FlexEnd).Justify(FlexJustify.SpaceBetween));
            Ui.Text(header, "Name", "<b>Ruccho</b>", 24f, Pal.Text).Shadowed();
            Ui.Text(header, "Title", "Flexbox Knight", 14f, Pal.Dim);

            _playerHp = Bar.Create(info, "Health", Lay.Row.H(26f), 8f, Pal.Green, _liquid, true);
            _playerHpLabel = BarLabel(_playerHp, 15f);
            _playerMp = Bar.Create(info, "Mana", Lay.Row.H(16f), 6f, Pal.Blue, _liquid);
            _playerMpLabel = BarLabel(_playerMp, 11f);

            var buffs = Ui.El(info, "Buffs", Lay.Row.Gap(4f));
            for (var i = 0; i < 10; i++)
                _buffs.Add(MakeBuff(buffs, 29.4f, (Icon)((i * 5 + 2) % 16), Pal.Classes[(i + 1) % Pal.Classes.Length],
                    false));
        }

        static YauiText BarLabel(Bar bar, float size)
        {
            var label = Ui.Text(bar.Track, "Label", "", size, Color.white, Lay.Col.Cover());
            label.Align = TextAlign.Center;
            label.VerticalAlign = VerticalAlign.Middle;
            return label.Shadowed(2f);
        }

        Buff MakeBuff(YauiElement parent, float size, Icon icon, Color color, bool label)
        {
            var radius = size * 0.22f;
            var slot = Ui.El(parent, "Buff", Lay.Col.Size(size, size).Fixed().Center(),
                Sty.Fill(Color.Lerp(Pal.Ink, color, 0.3f)).Radius(radius)
                    .Border(1f, new Color(color.r, color.g, color.b, 0.8f)));
            Ui.Image(slot, "Icon", _icons[(int)icon], size * 0.6f, color);
            var buff = new Buff
            {
                Slot = slot,
                Sweep = Ui.Radial(slot, "Time", Lay.Col.Cover(), radius, new Color(0f, 0f, 0f, 0.62f)),
                Duration = 1f
            };
            if (label)
            {
                buff.Label = Ui.Text(slot, "Seconds", "", size * 0.42f, Color.white, Lay.Col.Cover());
                buff.Label.Align = TextAlign.Center;
                buff.Label.VerticalAlign = VerticalAlign.Middle;
                buff.Label.Shadowed(2f);
            }

            return buff;
        }

        void BuildRaidFrames()
        {
            var raid = Ui.El(_root, "Raid Frames",
                Lay.Row.Abs(24f, 172f, Lay.Auto, Lay.Auto).W(420f).Wrap().Gap(4f).Pad(8f), Pal.Panel);
            for (var i = 0; i < MemberNames.Length; i++)
            {
                var color = Pal.Classes[i * 3 % Pal.Classes.Length];
                var member = new Member { Name = MemberNames[i], Color = color, Health = Random.Range(0.5f, 1f) };
                var cell = Ui.El(raid, member.Name, Lay.Col.Size(97f, 42f).Fixed().Pad(5f, 4f).Gap(3f),
                    Sty.Fill(Color.Lerp(Pal.Ink, color, 0.16f)).Radius(6f)
                        .Border(1f, new Color(color.r, color.g, color.b, 0.45f)));
                var row = Ui.El(cell, "Row", Lay.Row.Items(FlexAlign.Center).Gap(4f));
                Ui.Image(row, "Role", _icons[(int)(i % 5 == 0 ? Icon.Shield : i % 5 == 1 ? Icon.Cross : Icon.Sword)],
                    12f, color);
                Ui.Text(row, "Name", member.Name, 13f, Pal.Text);
                member.Hp = Bar.Create(cell, "Health", Lay.Row.H(9f), 4f, i % 5 == 1 ? Pal.Green : color, null, true);
                _members.Add(member);
            }
        }

        void BuildChat()
        {
            var chat = Ui.El(_root, "Chat", Lay.Col.Abs(24f, Lay.Auto, Lay.Auto, 34f).Size(464f, 244f).Pad(10f).Gap(8f),
                Pal.Panel);
            var tabs = Ui.El(chat, "Tabs", Lay.Row.Gap(6f));
            var names = new[] { "General", "Party", "Raid", "Combat", "Loot" };
            for (var i = 0; i < names.Length; i++)
            {
                var active = i == 2;
                var tab = Ui.El(tabs, names[i], Lay.Col.Pad(12f, 4f),
                    Sty.Fill(active ? Ui.Hex(0x4DD8FF, 0.22f) : Ui.Hex(0xFFFFFF, 0.05f)).Radius(11f)
                        .Border(1f, active ? Ui.Hex(0x4DD8FF, 0.7f) : Ui.Hex(0xFFFFFF, 0.1f)));
                Ui.Text(tab, "Text", names[i], 13f, active ? Pal.Cyan : Pal.Dim);
            }

            // The lines wrap to the width of the panel; older lines leave through the top of the clip.
            var body = Ui.El(chat, "Lines", Lay.Col.Grow().Justify(FlexJustify.FlexEnd).Gap(3f));
            body.ClipChildren = true;
            for (var i = 0; i < 9; i++)
            {
                var line = Ui.Text(body, "Line", "", 15f, Pal.Text, Lay.Col.Fixed());
                line.WordWrap = true;
                _chat.Add(line);
            }

            for (var i = 0; i < 7; i++) PostChat(ChatLines[_chatIndex++ % ChatLines.Length]);
        }

        #endregion

        #region Top

        void BuildBossFrame()
        {
            const float width = 780f;
            var top = Ui.El(_root, "Top Center", Lay.Col.Abs(0f, 22f, 0f, Lay.Auto).Items(FlexAlign.Center).Gap(7f));

            var header = Ui.El(top, "Header", Lay.Row.W(width).Items(FlexAlign.FlexEnd).Gap(10f));
            Ui.Image(header, "Icon", _icons[(int)Icon.Burst], 30f, Pal.Red);
            Ui.Text(header, "Name", "<b>Vorathiel, the Unbatched</b>", 27f, Pal.Text).Shadowed(4f);
            _bossPhase = Ui.Text(header, "Phase", "PHASE 1", 15f, Pal.Gold, Lay.Col.Grow()).Shadowed();
            _bossPercent = Ui.Text(header, "Percent", "", 24f, Pal.Text).Shadowed();

            _bossHp = Bar.Create(top, "Health", Lay.Row.Size(width, 30f).Fixed(), 8f, Ui.Hex(0xE5305C), _liquid,
                true);
            _bossHp.Track.Box = _bossHp.Track.Box.Shadow(Ui.Hex(0xFF2D55, 0.45f), 12f);
            for (var i = 1; i < 10; i++)
                Ui.El(_bossHp.Track, "Tick", Lay.Col.Abs(Length.Percent(i * 10f), 0f, Lay.Auto, 0f).W(2f),
                    Sty.Fill(new Color(0f, 0f, 0f, 0.45f)));

            _bossHpLabel = BarLabel(_bossHp, 16f);

            var row = Ui.El(top, "Row", Lay.Row.W(width).Items(FlexAlign.Center).Justify(FlexJustify.SpaceBetween));
            var debuffs = Ui.El(row, "Debuffs", Lay.Row.Gap(5f));
            for (var i = 0; i < 9; i++)
                _debuffs.Add(MakeBuff(debuffs, 34f, (Icon)((i * 3 + 1) % 16), Pal.Classes[(i + 2) % Pal.Classes.Length],
                    true));

            _bossCast = Bar.Create(row, "Cast", Lay.Row.Size(330f, 22f).Fixed(), 6f, Pal.Orange, _liquid);
            _bossCastLabel = BarLabel(_bossCast, 14f);
            _bossCastLabel.Text = BossSpells[0];
        }

        void BuildMinimapAndQuests()
        {
            const float size = 220f;
            var column = Ui.El(_root, "Top Right",
                Lay.Col.Abs(Lay.Auto, 24f, 24f, Lay.Auto).Items(FlexAlign.FlexEnd).Gap(12f));

            var frame = Ui.El(column, "Minimap Frame", Lay.Col.Size(size, size).Fixed(),
                Sty.Fill(Ui.Hex(0x071222, 0.9f)).Radius(size * 0.5f)
                    .Shadow(new Color(0f, 0f, 0f, 0.55f), 12f, 0f, new Vector2(0f, 5f)));
            // Everything in the map is clipped to the circle per pixel.
            var map = Ui.El(frame, "Minimap", Lay.Col.Cover(), Sty.None.Radius(size * 0.5f));
            map.ClipChildren = true;

            var line = Sty.Fill(Ui.Hex(0x4DD8FF, 0.13f));
            for (var i = 1; i < 6; i++)
            {
                Ui.El(map, "Grid", Lay.Col.Abs(Length.Percent(i * 100f / 6f), 0f, Lay.Auto, 0f).W(1f), line);
                Ui.El(map, "Grid", Lay.Col.Abs(0f, Length.Percent(i * 100f / 6f), 0f, Lay.Auto).H(1f), line);
            }

            foreach (var inset in new[] { 30f, 68f })
                Ui.El(map, "Range", Lay.Col.Abs(inset, inset, inset, inset),
                    Sty.None.Radius(size).Border(1f, Ui.Hex(0x4DD8FF, 0.25f)));

            _radar = Ui.Radial(map, "Radar", Lay.Col.Cover(), size * 0.5f, Ui.Hex(0x4DD8FF, 0.16f));
            _radar.SetArc(-90f, 55f);

            var center = size * 0.5f;
            for (var i = 0; i < _enemies.Count; i++)
            {
                var dot = _enemies[i].Boss ? 16f : 8f;
                _mapDots.Add(Ui.El(map, "Enemy",
                    Lay.Col.Abs(center - dot * 0.5f, center - dot * 0.5f, Lay.Auto, Lay.Auto).Size(dot, dot),
                    Sty.Fill(_enemies[i].Boss ? Pal.Purple : Pal.Red).Radius(dot)
                        .Shadow(Ui.Hex(0xFF4D6D, 0.7f), 3f)));
            }

            Ui.El(map, "Player", Lay.Col.Abs(center - 7f, center - 7f, Lay.Auto, Lay.Auto).Size(14f, 14f),
                Sty.Fill(Color.white).Radius(7f).Border(3f, Pal.Cyan).Shadow(Ui.Hex(0x4DD8FF, 0.9f), 5f));
            Ui.El(frame, "Rim", Lay.Col.Cover(), Sty.None.Radius(size * 0.5f).Border(2.5f, Ui.Hex(0x4DD8FF, 0.75f)));

            var zone = Ui.El(frame, "Zone", Lay.Col.Abs(0f, Lay.Auto, 0f, -10f).Items(FlexAlign.Center));
            var badge = Ui.El(zone, "Badge", Lay.Col.Pad(12f, 3f),
                Sty.Fill(Ui.Hex(0x0B1020, 0.95f)).Radius(11f).Border(1f, Ui.Hex(0x4DD8FF, 0.6f)));
            Ui.Text(badge, "Text", "Obsidian Hollow", 13f, Pal.Text);

            var quests = Ui.El(column, "Quests", Lay.Col.W(310f).Pad(14f).Gap(7f).Margin(0f, 8f, 0f, 0f), Pal.Panel);
            Ui.Text(quests, "Title", "<b>QUESTS</b>", 13f, Pal.Gold);
            Ui.Text(quests, "Name", "<b>The Unbatched King</b>", 18f, Pal.Text);
            for (var i = 0; i < 4; i++)
            {
                var objective = Ui.El(quests, "Objective", Lay.Row.Items(FlexAlign.Center).Gap(8f));
                _checks.Add(Ui.El(objective, "Check", Lay.Col.Size(14f, 14f).Fixed(),
                    Sty.None.Radius(4f).Border(1.5f, Pal.Dim)));
                _objectives.Add(Ui.Text(objective, "Text", "", 14f, Pal.Text));
            }
        }

        #endregion

        #region Bottom

        void BuildMeter()
        {
            var meter = Ui.El(_root, "Damage Meter",
                Lay.Col.Abs(Lay.Auto, Lay.Auto, 24f, 100f).W(344f).Pad(10f).Gap(3f), Pal.Panel);
            _meterTitle = Ui.Text(meter, "Title", "", 13f, Pal.Dim, Lay.Col.Fixed().Margin(2f, 0f, 0f, 4f));
            for (var i = 0; i < 8; i++)
            {
                var row = Ui.El(meter, "Row",
                    Lay.Row.H(23f).Fixed().Items(FlexAlign.Center).Justify(FlexJustify.SpaceBetween).Pad(8f, 0f),
                    Sty.Fill(Ui.Hex(0xFFFFFF, 0.04f)).Radius(5f));
                _meter.Add(new MeterRow
                {
                    Fill = Ui.El(row, "Fill", Lay.Col.Abs(0f, 0f, Lay.Auto, 0f).W(Length.Percent(100f)),
                        Sty.Fill(Pal.Blue).Radius(5f)),
                    Name = Ui.Text(row, "Name", "", 14f, Pal.Text).Shadowed(2f),
                    Value = Ui.Text(row, "Value", "", 14f, Pal.Text).Shadowed(2f)
                });
            }
        }

        void BuildActionBars()
        {
            var bottom = Ui.El(_root, "Bottom Center",
                Lay.Col.Abs(0f, Lay.Auto, 0f, 26f).Items(FlexAlign.Center).Gap(10f));

            _castFrame = Ui.El(bottom, "Cast", Lay.Col.Items(FlexAlign.Center));
            _cast = Bar.Create(_castFrame, "Bar", Lay.Row.Size(420f, 22f).Fixed(), 7f, Pal.Cyan, _liquid);
            _cast.Track.Box = _cast.Track.Box.Shadow(Ui.Hex(0x4DD8FF, 0.4f), 8f);
            _cast.Value = 0f;
            _castLabel = BarLabel(_cast, 14f);

            var row = Ui.El(bottom, "Bars", Lay.Row.Items(FlexAlign.Center).Gap(18f));
            var bars = Ui.El(row, "Skills", Lay.Col.Gap(6f).Pad(8f), Pal.Panel.Radius(16f));
            var keys = new[]
            {
                "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=",
                "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12"
            };
            for (var r = 0; r < 2; r++)
            {
                var bar = Ui.El(bars, "Bar", Lay.Row.Gap(6f));
                for (var i = 0; i < 12; i++) _skills.Add(MakeSkill(bar, r * 12 + i, keys[r * 12 + i]));
            }

            // The ultimate: a ring that fills with the damage dealt, with particles drawn on top of its children.
            _ult = Ui.El(row, "Ultimate", Lay.Col.Size(124f, 124f).Fixed().Center(),
                Sty.Fill(Ui.Hex(0x1A1430)).Radius(62f).Border(2f, Ui.Hex(0xFFC857, 0.6f))
                    .Shadow(Ui.Hex(0xFFC857, 0.5f), 14f));
            _ultRing = Ui.Radial(_ult, "Charge", Lay.Col.Cover(), 62f, Pal.Gold);
            Ui.El(_ult, "Face", Lay.Col.Abs(9f, 9f, 9f, 9f), Sty.Fill(Ui.Hex(0x1A1430)).Radius(60f));
            Ui.Image(_ult, "Icon", _icons[(int)Icon.Burst], 58f, Pal.Gold);
            _ultLabel = Ui.Text(_ult, "Label", "", 14f, Color.white, Lay.Col.Abs(0f, Lay.Auto, 0f, 14f));
            _ultLabel.Align = TextAlign.Center;
            _ultLabel.Shadowed();
            var shine = Ui.El(_ult, "Sheen", Lay.Col.Cover(), Sty.Fill(Color.white).Radius(62f));
            shine.Material = _sheen;

            var glitter = NewSystem(_ult.gameObject, _particleMaterial);
            var main = glitter.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(5f, 12f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(10f, 60f);
            main.startColor = Pal.Gold;
            var emission = glitter.emission;
            emission.enabled = true;
            emission.rateOverTime = 22f;
            var shape = glitter.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 58f;
            shape.radiusThickness = 0f;
            Fade(glitter);
            glitter.Play();
            _ult.gameObject.AddComponent<YauiParticle>().Position = YauiCustomDrawPosition.AfterChildren;
        }

        Skill MakeSkill(YauiElement parent, int index, string key)
        {
            const float radius = 11f;
            var color = Pal.Classes[index * 2 % Pal.Classes.Length];
            var slot = Ui.El(parent, "Skill " + key, Lay.Col.Size(54f, 54f).Fixed().Center(),
                Sty.Fill(Color.Lerp(Pal.Ink, color, 0.24f)).Radius(radius)
                    .Border(1.5f, new Color(color.r, color.g, color.b, 0.75f)));
            Ui.Image(slot, "Icon", _icons[(index * 7 + 3) % _icons.Length], 32f, color);

            var skill = new Skill
            {
                Slot = slot,
                Color = color,
                Cooldown = Random.Range(4f, 22f),
                Sweep = Ui.Radial(slot, "Cooldown", Lay.Col.Cover(), radius, new Color(0f, 0f, 0f, 0.7f)),
                Label = Ui.Text(slot, "Seconds", "", 22f, Color.white, Lay.Col.Cover())
            };
            skill.Remaining = Random.Range(0f, skill.Cooldown);
            skill.Label.Align = TextAlign.Center;
            skill.Label.VerticalAlign = VerticalAlign.Middle;
            skill.Label.Shadowed();
            Ui.Text(slot, "Key", key, 11f, Pal.Text, Lay.Col.Abs(5f, 2f, Lay.Auto, Lay.Auto)).Shadowed(2f);

            // A ready skill shines: a custom shader, masked by the rounded box of this element.
            skill.Sheen = Ui.El(slot, "Sheen", Lay.Col.Cover(), Sty.Fill(Color.white).Radius(radius));
            skill.Sheen.Material = _sheen;
            skill.Sheen.Scale = Vector2.zero;
            return skill;
        }

        void BuildCombo()
        {
            _combo = Ui.El(_root, "Combo", Lay.Col.Abs(Lay.Auto, 262f, 372f, Lay.Auto).Items(FlexAlign.FlexEnd));
            var transform = TransformStyle.Identity;
            transform.pivot = new Vector2(1f, 0.5f);
            transform.rotation = -4f;
            _combo.RenderTransform = transform;

            _comboNumber = Ui.Text(_combo, "Number", "", 80f, Pal.Gold);
            _comboNumber.OutlineColor = Ui.Hex(0x5A2A00);
            _comboNumber.OutlineWidth = 5f;
            _comboNumber.Shadowed(6f);
            var label = Ui.El(_combo, "Label", Lay.Row.Items(FlexAlign.Center).Gap(8f));
            Ui.Text(label, "Hits", "<b>HITS</b>", 24f, Pal.Text).Shadowed();
            var bonus = Ui.El(label, "Bonus", Lay.Col.Pad(10f, 3f), Sty.Fill(Pal.Orange).Radius(11f));
            Ui.Text(bonus, "Text", "<b>x2.4 DMG</b>", 14f, Pal.Ink);
        }

        #endregion
    }
}
